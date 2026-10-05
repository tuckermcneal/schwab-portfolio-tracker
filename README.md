# Schwab Portfolio Tracker

A small read-only C# CLI that shows your Schwab positions, P&L, allocation, and live quotes.

## Setup

1. Install the .NET SDK: `brew install --cask dotnet-sdk`
2. At developer.schwab.com, create an app with the **Accounts and Trading Production** and
   **Market Data Production** APIs, and set the callback URL to `https://127.0.0.1`.
   Wait until the app's status is "Ready For Use". (The app key you already use with schwabdev works too.)
3. Export your credentials (e.g. in `~/.zshrc`):
   ```bash
   export SCHWAB_APP_KEY="..."
   export SCHWAB_APP_SECRET="..."
   ```

## Usage

```bash
dotnet run                      # portfolio view (logs in on first run)
dotnet run -- quote AAPL NVDA   # live quotes
dotnet run -- login             # force a fresh login
```

Tokens are saved to `~/.portfolio-tracker/tokens.json` (mode 600). Schwab refresh tokens expire
after 7 days, after which you'll be asked to log in again.
