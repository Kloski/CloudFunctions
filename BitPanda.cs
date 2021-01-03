using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MyFunctions
{
    public class BitPandaAccBalancesWrapper
    {
        public DateTime Timestamp { get; set; }
        public List<Attributes> FiatBalances { get; set; }
        public List<WalletAttributes> CryptoBalances { get; set; }
        public List<WalletAttributes> CryptoIndexBalances { get; set; }
    }

    public class BitPandaExchangeBalancesWrapper
    {
        public DateTime Timestamp { get; set; }
        public Balance[] Balances { get; set; }
    }

    public class HistoryPrices
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, BitPandaAssetPrices> Data { get; set; }
    }

    public class BitPandaAssetPrices
    {
        [JsonProperty("EUR")] public double Eur { get; set; }

        [JsonProperty("USD")] public double Usd { get; set; }

        [JsonProperty("CHF")] public double Chf { get; set; }

        [JsonProperty("GBP")] public double Gbp { get; set; }

        [JsonProperty("TRY")] public double Try { get; set; }
    }

    public class BitPandaFiat
    {
        [JsonProperty("data")] public List<Datum> Data { get; set; }
    }

    public class Datum
    {
        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("attributes")] public Attributes Attributes { get; set; }

        [JsonProperty("id")] public Guid Id { get; set; }
    }

    public class Attributes
    {
        [JsonProperty("fiat_id")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long FiatId { get; set; }

        [JsonProperty("fiat_symbol")] public string FiatSymbol { get; set; }

        [JsonProperty("balance")] public double Balance { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("pending_transactions_count")]
        public long PendingTransactionsCount { get; set; }
    }

    public class BitPandaAsset
    {
        [JsonProperty("data")] public Data Data { get; set; }
    }

    public class Data
    {
        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("attributes")] public DataAttributes Attributes { get; set; }
    }

    public class DataAttributes
    {
        [JsonProperty("cryptocoin")] public Cryptocoin Cryptocoin { get; set; }

        [JsonProperty("commodity")] public Commodity Commodity { get; set; }

        [JsonProperty("index")] public Index Index { get; set; }
    }

    public class Commodity
    {
        [JsonProperty("metal")] public Cryptocoin Metal { get; set; }
    }

    public class Cryptocoin
    {
        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("attributes")] public CryptocoinAttributes Attributes { get; set; }
    }

    public class CryptocoinAttributes
    {
        [JsonProperty("wallets")] public Wallet[] Wallets { get; set; }
    }

    public class Wallet
    {
        [JsonProperty("type")] public TypeEnum Type { get; set; }

        [JsonProperty("attributes")] public WalletAttributes Attributes { get; set; }

        [JsonProperty("id")] public Guid Id { get; set; }
    }

    public class WalletAttributes
    {
        [JsonProperty("cryptocoin_id")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long CryptocoinId { get; set; }

        [JsonProperty("cryptocoin_symbol")] public string CryptocoinSymbol { get; set; }

        [JsonProperty("balance")] public double Balance { get; set; }

        [JsonProperty("is_default")] public bool IsDefault { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("pending_transactions_count")]
        public long PendingTransactionsCount { get; set; }

        [JsonProperty("deleted")] public bool Deleted { get; set; }

        [JsonProperty("is_index")] public bool IsIndex { get; set; }

        [JsonProperty("fiat_value")] public BitPandaAssetPrices FiatValue { get; set; }
    }

    public class Index
    {
        [JsonProperty("index")] public Cryptocoin IndexIndex { get; set; }
    }

    public enum TypeEnum
    {
        Wallet
    }

    public class ExchangeBalances
    {
        [JsonProperty("account_id")] public Guid AccountId { get; set; }

        [JsonProperty("balances")] public Balance[] Balances { get; set; }
    }

    public class Balance
    {
        [JsonProperty("account_id")] public Guid AccountId { get; set; }

        [JsonProperty("currency_code")] public string CurrencyCode { get; set; }

        [JsonProperty("change")] public double Change { get; set; }

        [JsonProperty("available")] public double Available { get; set; }

        [JsonProperty("locked")] public double Locked { get; set; }

        [JsonProperty("sequence")] public long Sequence { get; set; }

        [JsonProperty("time")] public DateTimeOffset Time { get; set; }

        [JsonProperty("fiat_val")] public BitPandaAssetPrices FiatValue { get; set; }

        [JsonProperty("free_fiat_val")] public BitPandaAssetPrices FreeFiatValue { get; set; }
    }

    public class OrdersResponse
    {
        [JsonProperty("order_history")] public OrderHistory[] OrderHistory { get; set; }

        [JsonProperty("max_page_size")] public long MaxPageSize { get; set; }
    }

    public class OrderHistory
    {
        [JsonProperty("order")] public Order Order { get; set; }

        [JsonProperty("trades")] public object[] Trades { get; set; }
    }

    public class Order
    {
        [JsonProperty("trigger_price")] public double TriggerPrice { get; set; }

        [JsonProperty("time_in_force")] public string TimeInForce { get; set; }

        [JsonProperty("is_post_only")] public bool IsPostOnly { get; set; }

        [JsonProperty("order_id")] public Guid OrderId { get; set; }

        [JsonProperty("client_id")] public Guid ClientId { get; set; }

        [JsonProperty("account_id")] public Guid AccountId { get; set; }

        [JsonProperty("instrument_code")] public string InstrumentCode { get; set; }

        [JsonProperty("time")] public DateTimeOffset Time { get; set; }

        [JsonProperty("side")] public string Side { get; set; }

        [JsonProperty("price")] public double Price { get; set; }

        [JsonProperty("amount")] public double Amount { get; set; }

        [JsonProperty("filled_amount")] public double FilledAmount { get; set; }

        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("sequence")] public long Sequence { get; set; }

        [JsonProperty("status")] public string Status { get; set; }
    }


    internal class ParseStringConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(long) || t == typeof(long?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            long l;
            if (Int64.TryParse(value, out l))
            {
                return l;
            }

            throw new Exception("Cannot unmarshal type long");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            var value = (long) untypedValue;
            serializer.Serialize(writer, value.ToString());
            return;
        }
    }

    public class Instrument
    {
        [JsonProperty("state")] public string State { get; set; }

        [JsonProperty("base")] public Base Base { get; set; }

        [JsonProperty("quote")] public Base Quote { get; set; }

        [JsonProperty("amount_precision")] public long AmountPrecision { get; set; }

        [JsonProperty("market_precision")] public long MarketPrecision { get; set; }

        [JsonProperty("min_size")] public double MinSize { get; set; }
    }

    public class Base
    {
        [JsonProperty("code")] public string Code { get; set; }

        [JsonProperty("precision")] public long Precision { get; set; }
    }

    internal class TypeEnumConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(TypeEnum) || t == typeof(TypeEnum?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (value == "wallet")
            {
                return TypeEnum.Wallet;
            }

            throw new Exception("Cannot unmarshal type TypeEnum");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            var value = (TypeEnum) untypedValue;
            if (value == TypeEnum.Wallet)
            {
                serializer.Serialize(writer, "wallet");
                return;
            }

            throw new Exception("Cannot marshal type TypeEnum");
        }

        public static readonly TypeEnumConverter Singleton = new TypeEnumConverter();
    }
}