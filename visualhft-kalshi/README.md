# visualhft-kalshi

A read-only Kalshi prediction-markets connector plugin for the
[VisualHFT](https://github.com/visualHFT/VisualHFT) desktop app.

**Read-only by design.** Only the in-app order panel (in the parent VisualHFT
fork) places orders; this plugin streams data only.

| Piece | State |
|---|---|
| `KalshiPlugin` (extends `BasePluginDataRetriever`) | builds clean |
| `KalshiSigner` (RSA-PSS-SHA256) | 3 unit tests pass |
| `KalshiSettings` implements `ISetting` | done |
| `KalshiWsClient` (ClientWebSocket + signed upgrade) | code done; 401 on basic-tier keys (use REST polling) |
| Mappers (book + trade → VisualHFT.Model) | TODO |
| Plugin wire-up (StartAsync routes WS → mappers → RaiseOnDataReceived) | TODO |

Full design: [`docs/superpowers/specs/2026-04-29-visualhft-kalshi-design.md`](docs/superpowers/specs/2026-04-29-visualhft-kalshi-design.md)

## Prerequisites

- Windows (VisualHFT targets `net10.0-windows`)
- **.NET 10 SDK** (`dotnet --list-sdks` should show ≥ `10.0.203`)
- The upstream **VisualHFT repo cloned as a sibling**:
  ```
  <parent>/
  ├── visualhft-kalshi/        ← this repo
  └── VisualHFT/               ← the VisualHFT fork sibling
  ```
  This combined repo (`VisualHFT-Kalshi`) already ships both side-by-side, so
  if you cloned the parent you already have the right layout.

## Build & test

```bash
cd visualhft-kalshi
dotnet build
dotnet test                          # offline tests only (signer)
dotnet test --filter Category=Live   # live REST + WS (requires env vars below)
```

## Credentials

Generate a Kalshi API key at <https://kalshi.com> (or
<https://demo.kalshi.co> for demo) → Profile → **API Keys** → **Create new
API key**. Save the private key Kalshi shows you (one-time display) and the
key id. Drop the PEM file somewhere outside the repo — `keys/` and `*.pem`
are gitignored, but it's safer to keep credentials entirely outside any
working tree.

Then set environment variables before running tests or VisualHFT:

```powershell
$env:KALSHI_PEM_PATH = "C:\path\to\your\kalshi.pem"
$env:KALSHI_KEY_ID   = "<your key id>"
# Optional overrides
$env:KALSHI_WS_URL   = "wss://api.elections.kalshi.com/trade-api/ws/v2"
$env:KALSHI_TICKER   = "KXHIGHTATL-26APR29-B82.5"
```

The VisualHFT fork's helpers also read `KALSHI_DEMO_KEY_ID` /
`KALSHI_DEMO_PEM_PATH` and `KALSHI_PROD_KEY_ID` / `KALSHI_PROD_PEM_PATH` —
see `VisualHFT/Helpers/KalshiCredentials.cs` for details.

## Drop the DLL into VisualHFT and run

```bash
# build VisualHFT itself once (from the sibling VisualHFT/ folder)
cd ../VisualHFT
dotnet build VisualHFT.csproj

# copy this plugin into VisualHFT's output dir
cp ../visualhft-kalshi/src/MarketConnectors.Kalshi/bin/Debug/net10.0-windows/MarketConnectors.Kalshi.dll \
   bin/Debug/net10.0-windows/

# launch VisualHFT
./bin/Debug/net10.0-windows/VisualHFT.exe
```

The plugin appears in VisualHFT's connector list. Toggle it on, configure the
ticker in settings, and the live order book renders on screen.

## Layout

```
visualhft-kalshi/
├── docs/superpowers/specs/2026-04-29-visualhft-kalshi-design.md
├── src/MarketConnectors.Kalshi/
│   ├── KalshiPlugin.cs              # entry point
│   ├── Auth/KalshiSigner.cs         # RSA-PSS, 3 unit tests
│   ├── Ws/KalshiWsClient.cs         # ClientWebSocket + signed headers
│   ├── Settings/KalshiPluginSettings.cs
│   └── MarketConnectors.Kalshi.csproj
├── tests/MarketConnectors.Kalshi.Tests/
│   ├── KalshiSignerTests.cs         # offline (default test run)
│   ├── LiveRestSmokeTest.cs         # [Category=Live]
│   └── LiveWsSmokeTest.cs           # [Category=Live]
├── samples/                          # populate from real WS payloads
├── MarketConnectors.Kalshi.slnx
└── .gitignore                        # bin/, obj/, keys/, *.pem
```

## License

Code in this repo: MIT. Upstream VisualHFT: Apache 2.0.
