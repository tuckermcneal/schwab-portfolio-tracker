using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PortfolioTracker;

public class SchwabApiException(HttpStatusCode status, string body)
    : Exception($"Schwab API returned {(int)status} {status}: {body}");

/// <summary>Read-only wrapper around the Schwab Trader and Market Data APIs.</summary>
public sealed class SchwabClient(HttpClient http, SchwabAuth auth)
{
    const string BaseUrl = "https://api.schwabapi.com";

    public Task<List<AccountWrapper>> GetAccountsAsync() =>
        GetAsync<List<AccountWrapper>>("/trader/v1/accounts?fields=positions");

    public async Task<List<QuoteWrapper>> GetQuotesAsync(IEnumerable<string> symbols)
    {
        var list = Uri.EscapeDataString(string.Join(',', symbols.Select(s => s.ToUpperInvariant())));
        var result = await GetAsync<Dictionary<string, QuoteWrapper>>($"/marketdata/v1/quotes?symbols={list}&fields=quote");

        // Invalid symbols come back under an "errors" key with no quote.
        return result
            .Where(kv => kv.Value.Quote is not null)
            .Select(kv => kv.Value with { Symbol = kv.Value.Symbol ?? kv.Key })
            .ToList();
    }

    async Task<T> GetAsync<T>(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync());

        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw new SchwabApiException(res.StatusCode, await res.Content.ReadAsStringAsync());

        return (await res.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }
}
