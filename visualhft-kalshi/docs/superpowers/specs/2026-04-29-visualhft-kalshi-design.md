# VisualHFT-Kalshi Plugin — Design

**Date:** 2026-04-29
**Owner:** Paulo
**Status:** approved (verbal — user confirmed prod path, "let's go")

## Goal

Add Kalshi prediction markets as a real-time data source for the VisualHFT
desktop app. Operator selects one or more Kalshi market tickers; the plugin
maintains live order books and trade tape on screen using VisualHFT's existing
microstructure UI.

**Out of scope:** order placement, settlement event processing, microstructure
metric rederivation for binary contracts. Read-only market data only.

## Toolchain

| Decision | Choice | Why |
|---|---|---|
| Language/runtime | C# / `net10.0-windows` | VisualHFT targets it; can't deviate |
| SDK | .NET 10.0.203 (just installed via winget) | Required for `net10.0-windows` |
| Upstream | `visualHFT/VisualHFT` cloned as **sibling** at `..\VisualHFT` | Lets us reference `VisualHFT.Commons.csproj` directly without forking |
| Plugin target | One DLL: `MarketConnectors.Kalshi.dll` | Drops into VisualHFT's `AppDomain.BaseDirectory` (`bin\<config>\net10.0-windows\`) at runtime |
| Auth | C# native RSA-PSS via `RSA.ImportFromPem` + `RSACryptoServiceProvider.SignData(..., RSASignaturePadding.Pss)` | No native deps. Mirrors our Python signer 1:1. |
| WS endpoint | **Prod** `wss://api.elections.kalshi.com/trade-api/ws/v2` | User's existing read-only key works; demo data is sparse. |
| Credentials reuse | Read PEM + key id from a configurable absolute path (env var `KALSHI_PEM_PATH` / `KALSHI_KEY_ID`) | No second key generation needed |

## Project structure

```
visualhft-kalshi/
├── docs/superpowers/specs/2026-04-29-visualhft-kalshi-design.md
├── src/MarketConnectors.Kalshi/
│   ├── KalshiPlugin.cs              # extends BasePluginDataRetriever
│   ├── Auth/
│   │   ├── KalshiSigner.cs          # RSA-PSS-SHA256, port of Python signer
│   │   └── KeyLoader.cs             # PEM + key id from settings
│   ├── Ws/
│   │   ├── KalshiWsClient.cs        # ClientWebSocket wrapper, reconnect, auth headers
│   │   └── Messages.cs              # subscribe / orderbook_snapshot / orderbook_delta / trade DTOs
│   ├── Mapping/
│   │   ├── BookMapper.cs            # Kalshi yes_dollars/no_dollars → VisualHFT.Model.OrderBook
│   │   └── TradeMapper.cs           # Kalshi trade msg → VisualHFT.Model.Trade
│   ├── Settings/
│   │   └── KalshiPluginSettings.cs  # base_url, key_path, key_id, tickers, depth
│   └── MarketConnectors.Kalshi.csproj
├── tests/MarketConnectors.Kalshi.Tests/
│   ├── KalshiSignerTests.cs         # known PEM + timestamp → expected signature
│   ├── BookMapperTests.cs           # snapshot + delta → consistent OrderBook
│   └── MarketConnectors.Kalshi.Tests.csproj
├── samples/
│   ├── orderbook_snapshot.json      # captured WS payloads for tests
│   ├── orderbook_delta.json
│   └── trade.json
├── MarketConnectors.Kalshi.sln
├── .gitignore                        # bin/, obj/, secrets, *.user
└── README.md
```

## Components

| Component | Purpose | Inputs / Outputs | Depends on |
|---|---|---|---|
| `KalshiSigner` | Produce RSA-PSS-SHA256 signature for `{ts}{method}{path}` | (key id, PEM) → `(timestamp, signature, key_id)` headers | `System.Security.Cryptography.RSA` |
| `KeyLoader` | Resolve PEM + key id from settings or env | settings → `Signer` instance | `KalshiSigner` |
| `KalshiWsClient` | Open WS, send authenticated subscribe, receive messages, reconnect with exp backoff (1s→30s cap, infinite retries) | tickers + channels → `IObservable<JsonDocument>` (or callback) | `System.Net.WebSockets.ClientWebSocket`, `KalshiSigner` |
| `Messages` | DTOs for sub/snapshot/delta/trade | JSON ↔ records | `System.Text.Json` |
| `BookMapper` | Apply snapshot, then deltas; output normalized `VisualHFT.Model.OrderBook` | Kalshi `yes_dollars[]` / `no_dollars[]` (price strings, qty strings) → bids/asks of (double price, double size, eMDUpdateAction) | `VisualHFT.Commons.Model` |
| `TradeMapper` | Convert Kalshi trade msg | trade DTO → `VisualHFT.Model.Trade` | `VisualHFT.Commons.Model` |
| `KalshiPlugin` | Plugin entry point. Wires WsClient + mappers, calls `RaiseOnDataReceived` on each book/trade update | settings → live data | All of the above + `BasePluginDataRetriever` |

## Binary-contract mapping rule (critical)

Kalshi orderbooks return only YES bids and NO bids. There are no explicit asks.

For VisualHFT, which expects bids + asks, we map as:
- **bids** ← Kalshi yes-bids (price = yes_dollars, size = qty)
- **asks** ← Kalshi no-bids reflected: price = `1.00 − no_dollars`, size = qty

Both prices are then scaled to cents (`* 100`) before passing to VisualHFT,
matching the unit convention of crypto plugins (which use cents/decimals
internally already).

This is mathematically exact: a NO bid at 0.05 is offering to buy NO at 5¢,
which is identical to offering to sell YES at 95¢.

## Data flow

```
                    ┌──────────────────────────┐
   settings  ─────► │      KalshiPlugin        │
                    │ (BasePluginDataRetriever) │
                    └────────────┬─────────────┘
                                 │ tickers, channels
                                 ▼
                       ┌──────────────────┐
                       │  KalshiWsClient  │ ── auth headers ── KalshiSigner ── PEM/key id
                       │ (ClientWebSocket) │
                       └────────┬─────────┘
                                │ JSON payloads
                                ▼
                  ┌─────────────────────────────┐
                  │   route by msg.type         │
                  └─────┬──────────────┬────────┘
                        ▼              ▼
                ┌────────────┐  ┌────────────┐
                │ BookMapper │  │ TradeMapper│
                └─────┬──────┘  └────┬───────┘
                      ▼              ▼
              VisualHFT.Model.   VisualHFT.Model.
                  OrderBook         Trade
                      │              │
                      └──────┬───────┘
                             ▼
                   RaiseOnDataReceived(...)
                             │
                             ▼
                    VisualHFT UI (live)
```

## Error handling

| Failure | Response |
|---|---|
| PEM file missing / not readable | Plugin status `STOPPED_FAILED`, log error with file path |
| Signer init fails (bad PEM, wrong key type) | Same |
| WS connect failure (TLS, DNS, 401) | Reconnect with exp backoff, max 30s; never give up |
| WS receives unexpected JSON shape | Log + skip message, do not crash plugin |
| Invalid orderbook delta (e.g. delete level that doesn't exist) | Log warning, drop to "needs resnapshot", request fresh snapshot |
| Plugin Stop while WS is mid-message | Cancel via `CancellationTokenSource`, drain, close cleanly |

## Testing

- **`KalshiSignerTests`** — Given a fixed PEM + fixed timestamp + path, signature must match a known-good reference (regenerated once from the working Python signer).
- **`BookMapperTests`** — Snapshot fixture → expected OrderBook with N bids/asks. Then apply deltas → expected mutations. NO-side reflection test: snapshot with NO bid at 0.05 size 100 → asks contain (0.95, 100).
- **`TradeMapperTests`** — Trade fixture → Trade model with correct price (yes_price/100), size, side.
- WS client integration test: skipped in CI (no network), runnable locally as `dotnet test --filter Category=Live`.

## Phased plan (this session)

| Phase | Output | Validation |
|---|---|---|
| 1. Tooling | .NET 10 SDK installed, VisualHFT cloned as sibling | `dotnet --list-sdks` shows 10.x; `..\VisualHFT\` exists ✅ done |
| 2. Skeleton | `.sln` + `.csproj` + empty `KalshiPlugin` class extending `BasePluginDataRetriever` | `dotnet build` succeeds |
| 3. Signer | `KalshiSigner` + tests | Signature matches Python output for a known fixture |
| 4. WS client | `KalshiWsClient` connects, sends sub, receives ≥1 snapshot | Manual run: prints raw orderbook_snapshot |
| 5. Mappers | `BookMapper` + `TradeMapper` + tests | Tests pass against captured fixtures |
| 6. Wire-up | `KalshiPlugin.StartAsync` ties them together, raises events | Manual run inside VisualHFT shows live Kalshi book |

**Stop point this session: end of Phase 4 (snapshot received).** Mappers + UI
integration carry over to next session if scope creeps. Phase 6 is what makes
data appear on screen and is the meaningful demo milestone — push for it but
don't sacrifice quality.

## Risks

- **VisualHFT is built for continuous LOBs.** Some metrics (VPIN, OTT ratio)
  may render nonsensically for binary contracts. Acceptable: those are
  separate study plugins; we just provide market data and let the user
  ignore the studies that don't apply.
- **`net10.0-windows` is bleeding-edge** (released ~Nov 2025). Some packages
  may not have updated TFM yet. Mitigation: keep dep surface minimal
  (`System.Text.Json`, `System.Net.WebSockets` — both built-in).
- **Plugin loading uses `Assembly.LoadFrom`.** If our DLL has a transitive
  dep that isn't in VisualHFT's bin folder, runtime load fails. Mitigation:
  zero non-built-in NuGet deps for now.
- **Kalshi WS schema may drift** (we already saw schema drift on REST). Keep
  parser tolerant: ignore unknown fields, validate critical ones.
