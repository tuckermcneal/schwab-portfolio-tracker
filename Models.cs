using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortfolioTracker;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

// ---- OAuth ----

public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

// ---- Trader API: GET /trader/v1/accounts?fields=positions ----

public record AccountWrapper(SecuritiesAccount SecuritiesAccount);

public record SecuritiesAccount(
    string AccountNumber,
    string Type,
    List<Position>? Positions,
    Balances? CurrentBalances);

public record Balances(decimal LiquidationValue, decimal CashBalance);

public record Position(
    decimal LongQuantity,
    decimal ShortQuantity,
    decimal AveragePrice,
    decimal MarketValue,
    decimal CurrentDayProfitLoss,
    decimal CurrentDayProfitLossPercentage,
    decimal LongOpenProfitLoss,
    decimal ShortOpenProfitLoss,
    Instrument Instrument)
{
    public decimal Quantity => LongQuantity - ShortQuantity;
    public decimal OpenProfitLoss => LongOpenProfitLoss + ShortOpenProfitLoss;
    public decimal CostBasis => MarketValue - OpenProfitLoss;
    public decimal OpenProfitLossPercent => CostBasis == 0 ? 0 : OpenProfitLoss / Math.Abs(CostBasis) * 100;
}

public record Instrument(string Symbol, string AssetType, string? Description);

// ---- Market Data API: GET /marketdata/v1/quotes ----

public record QuoteWrapper(string? Symbol, Quote? Quote);

public record Quote(
    decimal LastPrice,
    decimal NetChange,
    decimal NetPercentChange,
    long TotalVolume);
