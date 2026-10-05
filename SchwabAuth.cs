using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Spectre.Console;

namespace PortfolioTracker;

public record SchwabConfig(string AppKey, string AppSecret, string CallbackUrl)
{
    public static SchwabConfig FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("SCHWAB_APP_KEY");
        var secret = Environment.GetEnvironmentVariable("SCHWAB_APP_SECRET");
        var callback = Environment.GetEnvironmentVariable("SCHWAB_CALLBACK_URL") ?? "https://127.0.0.1";

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Set SCHWAB_APP_KEY and SCHWAB_APP_SECRET (from developer.schwab.com) before running.");

        return new SchwabConfig(key, secret, callback);
    }
}

/// <summary>Tokens persisted between runs. Schwab access tokens last 30 min; refresh tokens 7 days.</summary>
public record StoredTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt);

public sealed class SchwabAuth(SchwabConfig config, HttpClient http)
{
    const string AuthorizeUrl = "https://api.schwabapi.com/v1/oauth/authorize";
    const string TokenUrl = "https://api.schwabapi.com/v1/oauth/token";
    static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".portfolio-tracker", "tokens.json");

    StoredTokens? _tokens;

    public async Task<string> GetAccessTokenAsync()
    {
        _tokens ??= Load();

        if (_tokens is null || _tokens.RefreshExpiresAt <= DateTimeOffset.UtcNow)
            await LoginAsync();
        else if (_tokens.AccessExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            await RefreshAsync();

        return _tokens!.AccessToken;
    }

    /// <summary>Interactive OAuth login: open browser, user pastes back the redirect URL.</summary>
    public async Task LoginAsync()
    {
        var url = $"{AuthorizeUrl}?client_id={Uri.EscapeDataString(config.AppKey)}" +
                  $"&redirect_uri={Uri.EscapeDataString(config.CallbackUrl)}";

        AnsiConsole.MarkupLine("[yellow]Schwab login required.[/] Opening your browser...");
        AnsiConsole.MarkupLine($"If it doesn't open, visit:\n[link]{Markup.Escape(url)}[/]\n");
        AnsiConsole.MarkupLine("After approving, your browser will land on a page that may fail to load — that's expected.");
        TryOpenBrowser(url);

        var redirected = AnsiConsole.Ask<string>("Paste the [bold]full URL[/] from the address bar:");
        var code = HttpUtility.ParseQueryString(new Uri(redirected.Trim()).Query)["code"]
                   ?? throw new InvalidOperationException("No 'code' parameter found in that URL.");

        var now = DateTimeOffset.UtcNow;
        var token = await RequestTokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = config.CallbackUrl,
        });

        _tokens = new StoredTokens(token.AccessToken, token.RefreshToken,
            now.AddSeconds(token.ExpiresIn), now + RefreshTokenLifetime);
        Save(_tokens);
    }

    async Task RefreshAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var token = await RequestTokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _tokens!.RefreshToken,
        });

        // The refresh token's 7-day clock starts at login, not at refresh.
        _tokens = _tokens with
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken ?? _tokens.RefreshToken,
            AccessExpiresAt = now.AddSeconds(token.ExpiresIn),
        };
        Save(_tokens);
    }

    async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.AppKey}:{config.AppSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw new SchwabApiException(res.StatusCode, await res.Content.ReadAsStringAsync());

        return (await res.Content.ReadFromJsonAsync<TokenResponse>(Json.Options))!;
    }

    static StoredTokens? Load()
    {
        if (!File.Exists(TokenPath)) return null;
        try { return JsonSerializer.Deserialize<StoredTokens>(File.ReadAllText(TokenPath), Json.Options); }
        catch (JsonException) { return null; }
    }

    static void Save(StoredTokens tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllText(TokenPath, JsonSerializer.Serialize(tokens, Json.Options));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(TokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    static void TryOpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* user can copy the link manually */ }
    }
}
