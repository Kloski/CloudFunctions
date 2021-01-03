using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MyFunctions
{
    public static class BitPandaTrailingStopLoss
    {
        private static ILogger _logger;

        // TODO: 
        public const string API_KEY = "TODO";
        public const string EXCHANGE_API_KEY = "TODO";
        public const string EXCHANGE_BEARER_TOKEN = "Bearer " + EXCHANGE_API_KEY;

        public readonly static double STOP_LIMIT_PERC = 1.0002D;
        public const double MIN_PRICE = 50D;
        private readonly static double[] PERC_THRESHOLDS = new double[] {0.95D, 0.92D, 0.89D, 0.86D, 0.83D};

        [FunctionName("BitPandaTrailingStopLoss")]
        public static void Run([TimerTrigger("0 0 */5 * * *")] TimerInfo myTimer, ILogger log)
        {
            _logger = log;
            _logger.LogInformation($"BitPandaTrailingStopLoss Timer trigger function executed at: {DateTime.Now}");

            // Useless, because BitPanda Web does not have withdraw API
            var bitPandaAssetPrices = GetCurrentPrices();
            // var bitPandaAsset = GetWebAssetWallet(bitPandaAssetPrices);

            var exchangeBalances = GetExchangeBalances(bitPandaAssetPrices);
            var exchangeOrders = GetActiveExchangeOrders();

            var instrumentsData = GetInstrumentsData();

            foreach (var b in exchangeBalances)
            {
                try
                {
                    if (!bitPandaAssetPrices.ContainsKey(b.CurrencyCode))
                    {
                        _logger.LogWarning($"'bitPandaAssetPrices' does not contains asset: {b.CurrencyCode}");
                        continue;
                    }

                    var balanceInstrument = instrumentsData
                        .Where(d => "ACTIVE".Equals(d.State))
                        .Where(d => d.Base.Code.Equals(b.CurrencyCode))
                        .FirstOrDefault(b => b.Quote.Code.Equals("EUR"));
                    if (balanceInstrument == null)
                    {
                        _logger.LogWarning($"No instrument found for currency: {b.CurrencyCode}");
                    }

                    var currencyPair = b.CurrencyCode + "_EUR";

                    var currentPrice = bitPandaAssetPrices[b.CurrencyCode].Eur;
                    var targetMaxPrice = currentPrice * PERC_THRESHOLDS[0];

                    var freeBalance = b.Available;

                    var currencyPairOrders = exchangeOrders.OrderHistory.Where(o => o.Order.InstrumentCode.Equals(currencyPair)).ToList();
                    if (currencyPairOrders.Any())
                    {
                        //var balancesSum = currencyPairOrders.Sum(o => o.Order.Amount);
                        var maxPrice = currencyPairOrders.Max(o => o.Order.Price);
                        if (maxPrice < targetMaxPrice)
                        {
                            var cancelOrderIds = CancelOrders(currencyPair);
                            freeBalance += exchangeOrders.OrderHistory.Where(o => cancelOrderIds.Contains(o.Order.OrderId)).Sum(o => o.Order.Amount);
                        }
                    }


                    var freePriceSum = freeBalance * currentPrice;
                    if (freePriceSum > balanceInstrument.MinSize) // if available more than 5€
                    {
                        var ordersCount = PERC_THRESHOLDS.Length;
                        var pricePerOrder = freePriceSum / ordersCount;
                        if (pricePerOrder < MIN_PRICE)
                        {
                            ordersCount = Convert.ToInt32(Math.Floor(freePriceSum / MIN_PRICE));
                            pricePerOrder = freePriceSum / ordersCount;
                        }

                        var palancePerOrder = freeBalance / ordersCount;

                        for (int i = 0; i < ordersCount; i++)
                        {
                            var treshold = PERC_THRESHOLDS[i];
                            CreateOrder(balanceInstrument, currencyPair, palancePerOrder, currentPrice * treshold, currentPrice * treshold * STOP_LIMIT_PERC);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError($"For cycle for instrument {b.CurrencyCode} failed.");
                }
            }

            _logger.LogInformation($"BitPandaTrailingStopLoss finished successfully at: {DateTime.Now}");
        }

        private static Dictionary<string, BitPandaAssetPrices> GetCurrentPrices()
        {
            _logger.LogInformation($"'AssetWallet' 'GetCurrentPricesJsonAsync' called");

            using var wc2 = new WebClient();
            var bitPandaAssetPricesJson = wc2.DownloadString("https://api.bitpanda.com/v1/ticker");
            var bitPandaAssetPrices = JsonConvert.DeserializeObject<Dictionary<string, BitPandaAssetPrices>>(bitPandaAssetPricesJson);

            _logger.LogInformation($"'AssetWallet' 'GetCurrentPricesJsonAsync' processed");

            return bitPandaAssetPrices;
        }

        private static List<Instrument> GetInstrumentsData()
        {
            _logger.LogInformation($"'AssetWallet' 'GetInstrumentsData' called");

            using var wc2 = new WebClient();
            var bitPandaInstrumentsJson = wc2.DownloadString("https://api.exchange.bitpanda.com/public/v1/instruments");
            var bitPandaInstruments = JsonConvert.DeserializeObject<List<Instrument>>(bitPandaInstrumentsJson);

            _logger.LogInformation($"'AssetWallet' 'GetInstrumentsData' processed");

            return bitPandaInstruments;
        }

        private static List<Balance> GetExchangeBalances(Dictionary<string, BitPandaAssetPrices> bitPandaAssetPricesMap)
        {
            try
            {
                using var wc = new WebClient();
                wc.Headers.Add("Accept", "application/json");
                wc.Headers.Add("Authorization", EXCHANGE_BEARER_TOKEN);
                var strBody = wc.DownloadString("https://api.exchange.bitpanda.com/public/v1/account/balances");
                var exchangeBalances = JsonConvert.DeserializeObject<ExchangeBalances>(strBody);

                System.Array.ForEach(exchangeBalances.Balances, cb =>
                {
                    if (bitPandaAssetPricesMap.ContainsKey(cb.CurrencyCode))
                    {
                        var bitPandaAssetPrice = bitPandaAssetPricesMap[cb.CurrencyCode];
                        var balanceTotal = cb.Available + cb.Locked;
                        cb.FiatValue = new BitPandaAssetPrices()
                        {
                            Chf = balanceTotal * bitPandaAssetPrice.Chf, Eur = balanceTotal * bitPandaAssetPrice.Eur, Gbp = balanceTotal * bitPandaAssetPrice.Gbp,
                            Try = balanceTotal * bitPandaAssetPrice.Try, Usd = balanceTotal * bitPandaAssetPrice.Usd
                        };
                    }
                });
                var balancesResult = exchangeBalances.Balances.Where(cb => (cb.FiatValue?.Eur ?? 0D) > 1D).ToList();

                return balancesResult;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"BitPandaService 'ExchangeBalances' failed.");
                return null;
            }
        }

        private static OrdersResponse GetActiveExchangeOrders()
        {
            try
            {
                using var wc = new WebClient();
                wc.Headers.Add("Accept", "application/json");
                wc.Headers.Add("Authorization", EXCHANGE_BEARER_TOKEN);
                var strBody = wc.DownloadString("https://api.exchange.bitpanda.com/public/v1/account/orders");
                var result = JsonConvert.DeserializeObject<OrdersResponse>(strBody);

                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"BitPandaService 'ExchangeBalances' failed.");
                return null;
            }
        }

        private static void CreateOrder(Instrument balanceInstrument, string instrument, double amount, double price, double price_trigger)
        {
            try
            {
                // Create a request using a URL that can receive a post.
                WebRequest request = WebRequest.Create("https://api.exchange.bitpanda.com/public/v1/account/orders");
                // Set the Method property of the request to POST.
                request.Method = "POST";

                var amountRound = Math.Pow(10, balanceInstrument.AmountPrecision);
                var priceRound = Math.Pow(10, balanceInstrument.MarketPrecision);
                // Create POST data and convert it to a byte array.
                Dictionary<string, string> requestBody = new Dictionary<string, string>
                {
                    {"instrument_code", instrument},
                    {"type", "STOP"},
                    {"side", "SELL"},
                    {"amount", (Math.Floor(amount * amountRound) / amountRound).ToString()}, // round to 5 decimal places
                    {"price", (Math.Floor(price * priceRound) / priceRound).ToString()},
                    {"trigger_price", (Math.Floor(price_trigger * priceRound) / priceRound).ToString()}
                    // {"client_id", Guid.NewGuid().ToString()},
                };
                var postData = JsonConvert.SerializeObject(requestBody);
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);

                // Set the ContentType property of the WebRequest.
                request.ContentType = "application/json";
                // Set the ContentLength property of the WebRequest.
                request.ContentLength = byteArray.Length;

                // Set headers
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Authorization", EXCHANGE_BEARER_TOKEN);

                // Get the request stream.
                Stream dataStream = request.GetRequestStream();
                // Write the data to the request stream.
                dataStream.Write(byteArray, 0, byteArray.Length);
                // Close the Stream object.
                dataStream.Close();

                // Get the response.
                WebResponse response = request.GetResponse();
                // Display the status.
                _logger.LogInformation(((HttpWebResponse) response).StatusDescription);

                // Get the stream containing content returned by the server.
                // The using block ensures the stream is automatically closed.
                using (dataStream = response.GetResponseStream())
                {
                    // Open the stream using a StreamReader for easy access.
                    StreamReader reader = new StreamReader(dataStream);
                    // Read the content.
                    string responseFromServer = reader.ReadToEnd();
                    // Display the content.
                    _logger.LogInformation(responseFromServer);
                }

                // Close the response.
                response.Close();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"CreateOrder failed. instrument: {instrument}, amount: {amount}, price: {price}, price_trigger: {price_trigger}");
            }
        }

        private static List<Guid> CancelOrders(string instrument)
        {
            try
            {
                // Create a request using a URL that can receive a post.
                WebRequest request = WebRequest.Create($"https://api.exchange.bitpanda.com/public/v1/account/orders?instrument_code={instrument}");
                // Set the Method property of the request to POST.
                request.Method = "DELETE";

                // Set headers
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Authorization", EXCHANGE_BEARER_TOKEN);

                // Get the response.
                WebResponse response = request.GetResponse();
                // Display the status.
                _logger.LogInformation(((HttpWebResponse) response).StatusDescription);

                // Get the stream containing content returned by the server.
                // The using block ensures the stream is automatically closed.
                List<Guid> cancelledOrderIds = new List<Guid>();
                using (Stream dataStream = response.GetResponseStream())
                {
                    // Open the stream using a StreamReader for easy access.
                    StreamReader reader = new StreamReader(dataStream);
                    // Read the content.
                    string responseFromServer = reader.ReadToEnd();
                    // Display the content.
                    _logger.LogInformation(responseFromServer);
                    cancelledOrderIds = JsonConvert.DeserializeObject<List<Guid>>(responseFromServer);
                }

                // Close the response.
                response.Close();
                return cancelledOrderIds;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"CancelOrders failed. instrument: {instrument}");
                return new List<Guid>();
            }
        }

        private static BitPandaAccBalancesWrapper GetWebAssetWallet(Dictionary<string, BitPandaAssetPrices> bitPandaAssetPricesMap)
        {
            try
            {
                using var wc = new WebClient();
                wc.Headers.Add("X-API-KEY", API_KEY);
                var strBody = wc.DownloadString("https://api.bitpanda.com/v1/asset-wallets");
                var result = JsonConvert.DeserializeObject<BitPandaAsset>(strBody);

                result.Data.Attributes.Cryptocoin.Attributes.Wallets =
                    result.Data.Attributes.Cryptocoin.Attributes.Wallets.Where(c => c != null && ((c?.Attributes?.Balance ?? 0) > 0)).ToArray();
                result.Data.Attributes.Index.IndexIndex.Attributes.Wallets = result.Data.Attributes.Index.IndexIndex.Attributes.Wallets
                    .Where(c => c != null && ((c?.Attributes?.Balance ?? 0) > 0)).ToArray();
                result.Data.Attributes.Commodity.Metal.Attributes.Wallets = result.Data.Attributes.Commodity.Metal.Attributes.Wallets
                    .Where(c => c != null && ((c?.Attributes?.Balance ?? 0) > 0)).ToArray();

                _logger.LogInformation($"'AssetWallet' data obtained and processed");

                var cryptoBalances = result.Data?.Attributes.Cryptocoin.Attributes.Wallets
                    .Select(fb => fb.Attributes)
                    .Where(a => a.Balance > 0.00000001D).ToList() ?? new List<WalletAttributes>();
                cryptoBalances.ForEach(cb =>
                {
                    if (bitPandaAssetPricesMap.ContainsKey(cb.CryptocoinSymbol))
                    {
                        var bitPandaAssetPrice = bitPandaAssetPricesMap[cb.CryptocoinSymbol];
                        cb.FiatValue = new BitPandaAssetPrices()
                        {
                            Chf = cb.Balance * bitPandaAssetPrice.Chf, Eur = cb.Balance * bitPandaAssetPrice.Eur, Gbp = cb.Balance * bitPandaAssetPrice.Gbp,
                            Try = cb.Balance * bitPandaAssetPrice.Try, Usd = cb.Balance * bitPandaAssetPrice.Usd
                        };
                    }
                });
                cryptoBalances = cryptoBalances.Where(cb => (cb.FiatValue?.Eur ?? 0D) > 1D).ToList();

                var cryptoIndexBalances = result.Data?.Attributes.Index.IndexIndex.Attributes.Wallets
                    .Select(fb => fb.Attributes)
                    .Where(a => a.Balance > 0.00000001D)
                    .ToList() ?? new List<WalletAttributes>();
                cryptoIndexBalances.ForEach(cib =>
                {
                    if (bitPandaAssetPricesMap.ContainsKey(cib.CryptocoinSymbol))
                    {
                        var bitPandaAssetPrice = bitPandaAssetPricesMap[cib.CryptocoinSymbol];
                        cib.FiatValue = new BitPandaAssetPrices()
                        {
                            Chf = cib.Balance * bitPandaAssetPrice.Chf, Eur = cib.Balance * bitPandaAssetPrice.Eur, Gbp = cib.Balance * bitPandaAssetPrice.Gbp,
                            Try = cib.Balance * bitPandaAssetPrice.Try, Usd = cib.Balance * bitPandaAssetPrice.Usd
                        };
                    }
                });
                cryptoIndexBalances = cryptoIndexBalances.Where(cib => (cib.FiatValue?.Eur ?? 0D) > 1D).ToList();

                var bitPandaAccBalancesWrapper = new BitPandaAccBalancesWrapper()
                {
                    Timestamp = DateTime.UtcNow,
                    CryptoBalances = cryptoBalances,
                    CryptoIndexBalances = cryptoIndexBalances
                };

                _logger.LogInformation($"'AssetWallet' 'BitPandaAccBalancesWrapper' created");

                return bitPandaAccBalancesWrapper;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"BitPandaService 'AssetWallet' failed.");
                return null;
            }
        }
    }
}