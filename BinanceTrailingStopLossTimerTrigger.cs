using System;
using System.Globalization;
using System.Linq;
using Binance.Net;
using Binance.Net.Enums;
using Binance.Net.Objects.Spot;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace MyFunctions
{
    public static class BinanceTrailingStopLossTimerTrigger
    {
        public const string FIAT_CURRENCY = "USDT";
        public readonly static decimal STOP_LIMIT_PERC = new decimal(1.0002D);

        // TODO: configurable ?
        public const double MIN_PRICE = 50D;
        private readonly static decimal[] PERC_THRESHOLDS = new Decimal[] {new decimal(0.95D), new decimal(0.92D), new decimal(0.89D), new decimal(0.86D), new decimal(0.83D)};

        [FunctionName("BinanceTrailingStopLossTimerTrigger")]
        public static void Run([TimerTrigger("0 0 */6 * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"BinanceTrailingStopLossTimerTrigger function executed at: {DateTime.Now}");

            ReSetStopLossesOfOpenOrders(log);

            log.LogInformation($"BinanceTrailingStopLossTimerTrigger function finished at: {DateTime.Now}");
        }

        public static void ReSetStopLossesOfOpenOrders(ILogger logger)
        {
            var binanceClient = new BinanceClient(new BinanceClientOptions {ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials("API_KEY", "SECRET_KEY")});

            var exchangeInfo = binanceClient.Spot.System.GetExchangeInfo();
            if (exchangeInfo.Success)
            {
                var accInfoResponse = binanceClient.General.GetAccountInfo();
                if (accInfoResponse.Success)
                {
                    var balances = accInfoResponse.Data.Balances.Where(b => b.Total > 0).ToList();
                    var balancesDict = balances.ToDictionary(k => k.Asset, v => v);
                    var balanceAssets = balances.Select(b => b.Asset).Where(a => !FIAT_CURRENCY.Equals(a)).ToHashSet();

                    var openOrdersResponse = binanceClient.Spot.Order.GetOpenOrders();
                    if (openOrdersResponse.Success)
                    {
                        var allAssets = balanceAssets.Select(a => a + FIAT_CURRENCY).ToHashSet();
                        var allOrdersSymbols = openOrdersResponse.Data?.Select(oo => oo.Symbol).Distinct().ToHashSet();
                        var allSymbols = allOrdersSymbols.Union(allAssets);

                        var allPricesResponse = binanceClient.Spot.Market.GetAllPrices();
                        if (allPricesResponse.Success)
                        {
                            var pricesPerSymbol = allPricesResponse.Data?.Where(p => allSymbols.Contains(p.Symbol)).ToList();
                            var sybbolAssetDict = pricesPerSymbol.Select(p => p.Symbol).ToDictionary(k => k, v => v.Replace(FIAT_CURRENCY, ""));

                            string result = "";
                            foreach (var openOrderCurrentPrice in pricesPerSymbol)
                            {
                                var symbol = openOrderCurrentPrice.Symbol;
                                try
                                {
                                    result += $"Symbol: {symbol}";

                                    var symbolOrders = openOrdersResponse.Data.Where(oo => oo.Symbol.Equals(symbol)).ToList();
                                    var cancellableSymbolOrders = symbolOrders.Where(o => !o.IsWorking && o.Status == OrderStatus.New)
                                        .OrderByDescending(oo => oo.Price)
                                        .ToList();

                                    decimal quantitySum;
                                    var asset = sybbolAssetDict[symbol];
                                    if (balancesDict.ContainsKey(asset))
                                    {
                                        var binanceBalance = balancesDict[asset];
                                        quantitySum = binanceBalance.Total;
                                    }
                                    else
                                    {
                                        quantitySum = cancellableSymbolOrders.Select(o => o.Quantity).Sum();
                                    }

                                    var priceSum = quantitySum * openOrderCurrentPrice.Price;
                                    var ordersCount = PERC_THRESHOLDS.Length;
                                    var pricePerOrder = priceSum / ordersCount;
                                    if (Decimal.ToDouble(pricePerOrder) < MIN_PRICE)
                                    {
                                        ordersCount = Convert.ToInt32(Math.Floor(Decimal.ToDouble(priceSum) / MIN_PRICE));
                                        pricePerOrder = priceSum / ordersCount;
                                    }

                                    bool placeOrders = false;
                                    if (symbolOrders.Any()) // existing orders
                                    {
                                        var currentPrice = cancellableSymbolOrders[0].Price;
                                        var newPrice = (openOrderCurrentPrice.Price * PERC_THRESHOLDS[0]);
                                        if (currentPrice < newPrice) // check if change is needed
                                        {
                                            foreach (var order in cancellableSymbolOrders)
                                            {
                                                binanceClient.Spot.Order.CancelOrder(symbol, order.OrderId);
                                            }

                                            placeOrders = true;
                                            result += ($"; Actual prices: {String.Join(", ", cancellableSymbolOrders.Select(so => so.Price).ToArray())}");
                                        }
                                    }
                                    else // not existing orders
                                    {
                                        placeOrders = true;
                                    }

                                    if (placeOrders)
                                    {
                                        var assetPrecesion = exchangeInfo.Data.Symbols.Where(s => s.Name == symbol).FirstOrDefault();
                                        //var assetPrecesion = exchangeInfo?.Data?.Symbols?.Where(s => s.BaseAsset.Equals(asset)).FirstOrDefault();
                                        var binanceSymbolPriceFilter = assetPrecesion.PriceFilter;
                                        var binanceSymbolAmountFilter = assetPrecesion.LotSizeFilter;
                                        //var binanceSymbolPriceFilter = assetPrecesion.Filters.OfType<BinanceSymbolPriceFilter>().First();
                                        var pricePrecesion =
                                            (Decimal.GetBits(decimal.Parse(binanceSymbolPriceFilter.TickSize.ToString(CultureInfo.InvariantCulture).TrimEnd('0')))[3] >> 16) &
                                            0x000000FF;
                                        var amountPrecesion =
                                            (Decimal.GetBits(decimal.Parse(binanceSymbolAmountFilter.StepSize.ToString(CultureInfo.InvariantCulture).TrimEnd('0')))[3] >> 16) &
                                            0x000000FF;
                                        var quantityPerOrder = quantitySum / ordersCount;
                                        var newPrices = new decimal[ordersCount];
                                        for (int i = 0; i < ordersCount; i++)
                                        {
                                            newPrices[i] = openOrderCurrentPrice.Price * PERC_THRESHOLDS[i];
                                            var roundedPrice = decimal.Round(newPrices[i], pricePrecesion, MidpointRounding.ToZero);
                                            var roundedStopPrice = decimal.Round(roundedPrice * STOP_LIMIT_PERC, pricePrecesion, MidpointRounding.ToZero);
                                            var roundedQuantity = decimal.Round(quantityPerOrder, amountPrecesion, MidpointRounding.ToZero);
                                            var webCallResult = binanceClient.Spot.Order.PlaceOrder(symbol,
                                                OrderSide.Sell, OrderType.StopLossLimit, roundedQuantity,
                                                price: roundedPrice, stopPrice: roundedStopPrice, timeInForce: TimeInForce.GoodTillCancel);
                                            if (!webCallResult.Success)
                                            {
                                                result += $"; WebCallResult err: {webCallResult?.Error?.Message}, code: {webCallResult?.Error?.Code}";
                                            }
                                        }

                                        result += ($"; New prices: {String.Join(", ", newPrices)}");
                                    }

                                    result += "\n";
                                }
                                catch (Exception e)
                                {
                                    logger.LogError(e, $"Re-setting orders for {symbol} failed.");
                                    result += $"; Exception msg: {e.Message}";
                                }
                            }

                            logger.LogInformation(result);
                        }
                        else
                        {
                            logger.LogWarning($"allPricesResponse is not Successful. allPricesResponse: {allPricesResponse}");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"openOrdersResponse is not Successful. openOrdersResponse: {openOrdersResponse}");
                    }
                }
                else
                {
                    logger.LogWarning($"accInfoResponse is not Successful. accInfoResponse: {accInfoResponse}");
                }
            }
            else
            {
                logger.LogWarning($"exchangeInfo is not Successful. exchangeInfo: {exchangeInfo}");
            }
        }
    }
}