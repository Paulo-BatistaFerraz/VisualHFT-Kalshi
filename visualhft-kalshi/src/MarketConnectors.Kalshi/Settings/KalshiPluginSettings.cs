using System;
using VisualHFT.Enums;
using VisualHFT.UserSettings;

namespace MarketConnectors.Kalshi.Settings;

/// <summary>
/// User-configurable settings for the Kalshi plugin.
/// Persisted via VisualHFT's settings system.
/// </summary>
public class KalshiPluginSettings : ISetting
{
    // ISetting members (required by VisualHFT)
    public string Symbol { get; set; } = "KXHIGHTATL-26APR29-B82.5";
    public VisualHFT.Model.Provider Provider { get; set; } = new()
    {
        ProviderID = 100,                 // arbitrary unique id (avoids clashing with crypto venues)
        ProviderName = "Kalshi"
    };
    public AggregationLevel AggregationLevel { get; set; } = AggregationLevel.Ms100;

    // Kalshi-specific (DEMO so trading + viewing share the same env; orders land where you can see them)
    public string BaseWsUrl { get; set; } = "wss://demo-api.kalshi.co/trade-api/ws/v2";

    /// <summary>Absolute path to the RSA private key PEM file. Set via the
    /// VisualHFT plugin Settings dialog, or seed from the KALSHI_DEMO_PEM_PATH
    /// environment variable.</summary>
    public string PrivateKeyPath { get; set; } =
        Environment.GetEnvironmentVariable("KALSHI_DEMO_PEM_PATH") ?? "";

    /// <summary>Kalshi API access key id. Set via the VisualHFT plugin Settings
    /// dialog, or seed from the KALSHI_DEMO_KEY_ID environment variable.</summary>
    public string KeyId { get; set; } =
        Environment.GetEnvironmentVariable("KALSHI_DEMO_KEY_ID") ?? "";

    /// <summary>Comma-separated list of Kalshi DEMO tickers (so orders + view share env).
    /// LAX B70.5 is the only two-sided demo market right now; others are listed so the
    /// strike ladder still groups them by event for context.</summary>
    public string Tickers { get; set; } =
        "KXHIGHLAX-26APR30-B68.5,KXHIGHLAX-26APR30-B70.5,KXHIGHLAX-26APR30-B72.5," +
        "KXHIGHNY-26APR30-B61.5,KXHIGHNY-26APR30-B63.5,KXHIGHNY-26APR30-B65.5";

    /// <summary>Orderbook depth requested in subscribe messages.</summary>
    public int Depth { get; set; } = 50;

    /// <summary>REST base URL — used by the polling fallback when WS isn't available.</summary>
    public string BaseRestUrl { get; set; } = "https://demo-api.kalshi.co";

    /// <summary>Polling cadence per ticker, in milliseconds. Lower = more real-time
    /// but more REST calls. Kalshi's basic tier rate limit is ~10 req/s; with N
    /// tickers polling every PollMs, total req/s ≈ N * 1000 / PollMs.</summary>
    public int PollMs { get; set; } = 400;

    /// <summary>If true, use REST polling instead of WebSocket (set when key lacks WS scope).</summary>
    public bool UseRestPolling { get; set; } = true;
}
