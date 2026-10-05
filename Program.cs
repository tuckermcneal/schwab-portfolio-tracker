using PortfolioTracker;
using Spectre.Console;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "portfolio";

if (command is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

try
{
    var config = SchwabConfig.FromEnvironment();
    using var http = new HttpClient();
    var auth = new SchwabAuth(config, http);
    var client = new SchwabClient(http, auth);

    switch (command)
    {
        case "login":
            await auth.LoginAsync();
            AnsiConsole.MarkupLine("[green]Logged in. Tokens saved.[/]");
            break;
        // Log in (if needed) before any spinner starts — Spectre can't show a prompt inside a status display.
        case "portfolio":
            await auth.GetAccessTokenAsync();
            await ShowPortfolio(client);
            break;
        case "quote":
            await auth.GetAccessTokenAsync();
            await ShowQuotes(client, args.Skip(1).ToArray());
            break;
        default:
            PrintHelp();
            return 1;
    }
    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or SchwabApiException or HttpRequestException or UriFormatException)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
    return 1;
}

static async Task ShowPortfolio(SchwabClient client)
{
    var accounts = await AnsiConsole.Status().StartAsync("Fetching accounts...", _ => client.GetAccountsAsync());

    decimal grandValue = 0, grandDay = 0, grandOpen = 0;

    foreach (var acct in accounts.Select(a => a.SecuritiesAccount))
    {
        var positions = (acct.Positions ?? new List<Position>()).OrderByDescending(p => p.MarketValue).ToList();
        var totalValue = acct.CurrentBalances?.LiquidationValue ?? positions.Sum(p => p.MarketValue);
        var cash = acct.CurrentBalances?.CashBalance ?? 0;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]{acct.Type} account …{Last4(acct.AccountNumber)}[/]")
            .AddColumn("Symbol")
            .AddColumn(new TableColumn("Qty").RightAligned())
            .AddColumn(new TableColumn("Avg Cost").RightAligned())
            .AddColumn(new TableColumn("Price").RightAligned())
            .AddColumn(new TableColumn("Value").RightAligned())
            .AddColumn(new TableColumn("Day P&L").RightAligned())
            .AddColumn(new TableColumn("Total P&L").RightAligned())
            .AddColumn(new TableColumn("Alloc").RightAligned());

        foreach (var p in positions)
        {
            // Options are quoted per share but sized per contract (x100).
            var multiplier = p.Instrument.AssetType == "OPTION" ? 100 : 1;
            var price = p.Quantity == 0 ? 0 : p.MarketValue / (p.Quantity * multiplier);

            table.AddRow(
                Markup.Escape(p.Instrument.Symbol),
                p.Quantity.ToString("0.####"),
                p.AveragePrice.ToString("C"),
                price.ToString("C"),
                p.MarketValue.ToString("C"),
                Signed(p.CurrentDayProfitLoss, p.CurrentDayProfitLossPercentage),
                Signed(p.OpenProfitLoss, p.OpenProfitLossPercent),
                totalValue == 0 ? "-" : (p.MarketValue / totalValue).ToString("P1"));
        }

        table.AddRow(
            "[grey]CASH[/]", "", "", "",
            cash.ToString("C"), "", "",
            totalValue == 0 ? "-" : (cash / totalValue).ToString("P1"));

        var day = positions.Sum(p => p.CurrentDayProfitLoss);
        var open = positions.Sum(p => p.OpenProfitLoss);
        table.Caption($"Total {totalValue:C}   Day {Signed(day)}   Unrealized {Signed(open)}");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        grandValue += totalValue;
        grandDay += day;
        grandOpen += open;
    }

    if (accounts.Count > 1)
        AnsiConsole.MarkupLine($"[bold]All accounts:[/] {grandValue:C}   Day {Signed(grandDay)}   Unrealized {Signed(grandOpen)}");
}

static async Task ShowQuotes(SchwabClient client, string[] symbols)
{
    if (symbols.Length == 0)
    {
        AnsiConsole.MarkupLine("[red]Usage:[/] quote AAPL MSFT ...");
        return;
    }

    var quotes = await AnsiConsole.Status().StartAsync("Fetching quotes...", _ => client.GetQuotesAsync(symbols));

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Symbol")
        .AddColumn(new TableColumn("Last").RightAligned())
        .AddColumn(new TableColumn("Change").RightAligned())
        .AddColumn(new TableColumn("Volume").RightAligned());

    foreach (var q in quotes)
        table.AddRow(
            Markup.Escape(q.Symbol!),
            q.Quote!.LastPrice.ToString("C"),
            Signed(q.Quote.NetChange, q.Quote.NetPercentChange),
            q.Quote.TotalVolume.ToString("N0"));

    AnsiConsole.Write(table);

    var missing = symbols.Select(s => s.ToUpperInvariant()).Except(quotes.Select(q => q.Symbol!)).ToList();
    if (missing.Count > 0)
        AnsiConsole.MarkupLine($"[yellow]No quote for:[/] {Markup.Escape(string.Join(", ", missing))}");
}

static string Signed(decimal amount, decimal? percent = null)
{
    var color = amount > 0 ? "green" : amount < 0 ? "red" : "grey";
    var text = (amount >= 0 ? "+" : "") + amount.ToString("C");
    if (percent is { } pct)
        text += $" ({(pct >= 0 ? "+" : "")}{pct:0.00}%)";
    return $"[{color}]{Markup.Escape(text)}[/]";
}

static string Last4(string accountNumber) =>
    accountNumber.Length <= 4 ? accountNumber : accountNumber[^4..];

static void PrintHelp() => AnsiConsole.MarkupLine("""
    [bold]Schwab Portfolio Tracker[/] (read-only)

    Usage: dotnet run -- [[command]]

      [green]portfolio[/]         Show positions, P&L and allocation (default)
      [green]quote[/] SYM ...     Get live quotes
      [green]login[/]             Force a fresh Schwab OAuth login

    Env vars: SCHWAB_APP_KEY, SCHWAB_APP_SECRET, SCHWAB_CALLBACK_URL (default https://127.0.0.1)
    """);
