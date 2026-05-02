# Trading-UI / HFT Skills Roadmap

A practical leveling-up plan for building a real Kalshi (or any market-data)
trading system without writing AI-vibe-coded slop.

---

## Where we are now

Stack as built across this session:

- **VisualHFT fork** — WPF + .NET 10, OxyPlot for charts, hand-rolled DataGrids,
  custom Kalshi plugin (REST polling) + a Browser-side polling helper.
- **Python `kalshi-data` + `research/`** — REST client, viz, tick logger,
  feature library, backtest engine (scaffold).
- **Demo trading wired** — RSA-PSS auth, order placement panel.

Honest critique:

- MVVM is **sloppy**. Code-behind has business logic. View-models reference
  views. No DI container, no IoC. No tests.
- Magic strings, hardcoded PEM paths, no settings UI.
- OxyPlot is fine but not industry-standard for trading charts.
- Default WPF DataGrid is *barely* good enough for live data; pros use commercial grids.

This is fine for a personal tool / portfolio piece. It will not survive an
interview if a quant or platform engineer reads the code, and it will not
scale past one operator.

---

## Path forward — pick one of these tracks, not both

### Track A — Native WPF, professional-grade

Stay in C#. Replace each "AI vibe-coded" piece with an industry library.

**Must-learn:**

| Library | Replaces | Why |
|---|---|---|
| [`CommunityToolkit.Mvvm`](https://github.com/CommunityToolkit/dotnet) | hand-rolled `INotifyPropertyChanged` | Source generators, `[ObservableProperty]`, `[RelayCommand]`. Microsoft official. |
| [`Caliburn.Micro`](https://github.com/Caliburn-Micro/Caliburn.Micro) or [`Prism`](https://prismlibrary.com/) | view-locator + DI | Conventions over configuration. Real MVVM. |
| [`Microsoft.Extensions.DependencyInjection`](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) | `new` everywhere | Constructor injection, scoped lifetimes. |
| [`LiveCharts2`](https://github.com/beto-rodriguez/LiveCharts2) | OxyPlot | GPU-accelerated, animated, modern. |
| [`Syncfusion WPF`](https://www.syncfusion.com/wpf) (community license) | DataGrid | `SfDataGrid` is what the default `DataGrid` should have been. |
| [`ReactiveUI` / `Rx.NET`](https://github.com/reactiveui/ReactiveUI) | manual event wiring | Streams of market data with combinators (Throttle, Buffer, DistinctUntilChanged). |
| [`Serilog`](https://github.com/serilog/serilog) | log4net | Structured logging. Makes `kibana`/Grafana wireup trivial later. |
| [`xUnit` + `FluentAssertions`](https://github.com/fluentassertions/fluentassertions) | nothing | Tests. Add them. |

**Skills order:**

1. Re-implement one Kalshi feature end-to-end with `CommunityToolkit.Mvvm` + DI.
   No code-behind, no `RaisePropertyChanged("Foo")`. Pure `[ObservableProperty]`.
2. Replace one OxyPlot chart with LiveCharts2. Compare.
3. Replace one DataGrid with `SfDataGrid` (Syncfusion). Cell highlighting, sticky
   headers, virtualization out of the box.
4. Wrap the BrowserPoller's outputs in `IObservable<OrderBook>` via Rx.NET.
   Subscribe with throttling and batching.
5. Add `ILogger<T>`-injected Serilog. Drop log4net.
6. Add xUnit tests for the signer and the book-mapper. Bring code coverage
   up over time.

---

### Track B — Web frontend (recommended for new builds)

Most new prop shops stand up a Next.js or SvelteKit front-end against a Python/Go/Rust
service. Cross-platform, faster iteration, far better charting libraries.

**Must-learn:**

| Library | Role | Source |
|---|---|---|
| [`TradingView Lightweight Charts`](https://github.com/tradingview/lightweight-charts) | charts | The actual library used by binance.com, kalshi.com, hyperliquid. ~50 KB, GPU-accelerated. |
| [`AG Grid`](https://github.com/ag-grid/ag-grid) (Community) | data tables | Industry standard. Pivoting, filtering, virtualization. |
| [`Next.js`](https://github.com/vercel/next.js) | app shell | App Router, RSCs, Turbopack. |
| [`Tailwind CSS`](https://github.com/tailwindlabs/tailwindcss) + [`shadcn/ui`](https://github.com/shadcn-ui/ui) | styling | Composable components, no design debt. |
| [`TanStack Query`](https://github.com/TanStack/query) | server-state caching | Stale-while-revalidate, retries, dedup. |
| [`Zustand`](https://github.com/pmndrs/zustand) | client state | Lightweight Redux replacement. |
| [`Recharts`](https://github.com/recharts/recharts) or [`Apache ECharts`](https://github.com/apache/echarts) | secondary charts | When TradingView is too heavy. |
| [`Socket.IO`](https://github.com/socketio/socket.io) or raw `WebSocket` | live data transport | Always with reconnect + heartbeat. |

**Backend (talks to Kalshi, exposes WS to your UI):**

| Library | Role |
|---|---|
| [`FastAPI`](https://github.com/fastapi/fastapi) (Python) | REST + WS endpoints, auth, OpenAPI |
| [`Pydantic`](https://github.com/pydantic/pydantic) | typed DTOs |
| [`websockets`](https://github.com/python-websockets/websockets) | async WS client to Kalshi |
| [`asyncio` + `aiolimiter`](https://github.com/mjpieters/aiolimiter) | throttling |

**Or in C#:**
- ASP.NET Core minimal APIs + SignalR for WS push.

**Skills order:**

1. Stand up a `kalshi-api/` FastAPI service. Wraps your existing Python
   `kalshi_client.py`. Exposes `/markets`, `/orderbook/{ticker}`, `/trades/{ticker}`,
   and a WS endpoint that streams updates.
2. `kalshi-ui/` Next.js app. Single page: ticker selector → TradingView chart.
3. Add an AG Grid Watch List that mirrors the WPF Watch List.
4. Add the Strike Ladder + per-market depth view.
5. Add demo order entry. Same hard caps as the WPF version.

---

## Reference repos to read (not generated, hand-written)

| Repo | Why | URL |
|---|---|---|
| **NautilusTrader** | Institutional-grade algo platform. Python+Rust. Real architecture. | https://github.com/nautechsystems/nautilus_trader |
| **freqtrade** + `frequi` | Crypto bot + Vue frontend, production-grade. | https://github.com/freqtrade/freqtrade |
| **hummingbot** | Market-making framework. Plugin architecture worth studying. | https://github.com/hummingbot/hummingbot |
| **OpenBB Terminal** | Open-source Bloomberg clone. Solid layout/architecture. | https://github.com/OpenBB-finance/OpenBB |
| **VisualHFT** (your fork's parent) | Read the existing plugins. Especially `MarketConnectors.Coinbase` for a clean `BasePluginDataRetriever` example. | https://github.com/visualHFT/VisualHFT |
| **hftbacktest** | Rust HFT backtest framework. Reading the engine teaches you queue position + latency modeling. | https://github.com/nkaz001/hftbacktest |
| **TradingView Lightweight Charts examples** | The `examples/` folder shows real-trading-view recipes. | https://github.com/tradingview/lightweight-charts/tree/master/website/tutorials |

---

## Skills ordered by leverage (1 = study first)

1. **Proper MVVM (or React state mgmt)** — biggest single jump in code quality.
   Two days with Prism samples or Zustand docs. Everything downstream improves.
2. **Reactive / async data flow** — `IObservable<T>`, async generators, debounce,
   buffer, batch. A live trading UI that doesn't drop frames depends on this.
3. **WebSocket robustness** — reconnect, heartbeat, dedupe-on-resubscribe,
   exponential backoff. The thing that makes a flaky network not destroy your
   strategy.
4. **Industry chart library** — TradingView Lightweight Charts (web) or
   LiveCharts2 (WPF). Spend a weekend; the rest of the UI looks better forever.
5. **Industry data grid** — AG Grid (web) or Syncfusion / DevExpress (WPF).
   Replaces every hand-rolled `DataGrid` with something a trader will tolerate.
6. **Dependency injection + testing** — `IServiceCollection` + xUnit / Vitest.
   Without DI, your code can't be unit tested. Without tests, every change is
   a coin flip.
7. **Real-time perf**: virtualization, object pooling, frame skipping, dispatcher
   priorities. Only relevant once #1–#6 are in place.
8. **Order management mechanics** — FIFO queue position, latency modeling, fill
   logic. These are the building blocks of a real backtest engine. Read
   hftbacktest's source.

---

## Path I'd actually recommend you walk

Given the system you're building (Kalshi prediction-market trading bot) and
how you want to position yourself:

1. **Don't keep extending the WPF UI.** It will plateau in quality.
2. **Stand up a small ASP.NET Core or FastAPI service** that wraps your existing
   Kalshi REST client. Add a `/ws` endpoint that streams orderbook + trade
   updates. ~1 week of focused work.
3. **Build a clean Next.js + TradingView + AG Grid front-end** against that
   service. ~2 weeks. Replaces the WPF tool entirely.
4. **Keep the Python `research/` framework** for backtesting and feature
   engineering. Don't ever try to do that in C#.
5. **At each step, study the reference repos.** Read one or two files from
   NautilusTrader or freqtrade per session. Imitate their style.

That's the path from "AI-vibe-coded portfolio piece" to "I can defend every
file in an interview."

---

## What to avoid

- **Don't add commercial dependencies you can't afford** (Telerik, DevExpress full)
  unless you actually need them or have student licenses.
- **Don't pile on more LLM-generated code without refactoring.** Every doubling
  of the codebase without tests doubles your future debugging time.
- **Don't reinvent charts.** TradingView Lightweight Charts and LiveCharts2 are
  free, fast, and battle-tested. Hand-drawing OHLC bars in OxyPlot is a trap.
- **Don't reinvent grids.** Same logic; AG Grid Community is genuinely free.
- **Don't ship a single-binary monolith.** Service + UI lets you swap either
  side without rewriting both.
