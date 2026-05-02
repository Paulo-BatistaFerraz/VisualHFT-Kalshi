using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MarketConnectors.Kalshi.Auth;
using MarketConnectors.Kalshi.Ws;
using Xunit;
using Xunit.Abstractions;

namespace MarketConnectors.Kalshi.Tests;

/// <summary>
/// Live integration tests — hit the real Kalshi WS endpoint.
/// Skipped by default. Run with:
///   dotnet test --filter Category=Live
///
/// Reads credentials from these env vars (KALSHI_PEM_PATH and KALSHI_KEY_ID are required):
///   KALSHI_PEM_PATH (required) absolute path to your Kalshi private key PEM
///   KALSHI_KEY_ID   (required) your Kalshi API access key id
///   KALSHI_WS_URL   (default: wss://api.elections.kalshi.com/trade-api/ws/v2)
///   KALSHI_TICKER   (default: KXHIGHTATL-26APR29-B82.5)
/// </summary>
public class LiveWsSmokeTest
{
    private readonly ITestOutputHelper _out;
    public LiveWsSmokeTest(ITestOutputHelper o) => _out = o;

    [Fact]
    [Trait("Category", "Live")]
    public async Task ConnectAndSubscribe_ReceivesAtLeastOneSnapshot()
    {
        var pemPath = Environment.GetEnvironmentVariable("KALSHI_PEM_PATH")
                      ?? throw new InvalidOperationException(
                          "Set KALSHI_PEM_PATH to the absolute path of your Kalshi private key PEM file.");
        var keyId   = Environment.GetEnvironmentVariable("KALSHI_KEY_ID")
                      ?? throw new InvalidOperationException(
                          "Set KALSHI_KEY_ID to your Kalshi API access key id.");
        var wsUrl   = Environment.GetEnvironmentVariable("KALSHI_WS_URL")
                      ?? "wss://api.elections.kalshi.com/trade-api/ws/v2";
        var ticker  = Environment.GetEnvironmentVariable("KALSHI_TICKER")
                      ?? "KXHIGHTATL-26APR29-B82.5";

        using var signer = KalshiSigner.FromPemFile(keyId, pemPath);
        using var ws = new KalshiWsClient(wsUrl, signer);

        var snapshotSeen = new TaskCompletionSource<JsonDocument>();
        var collected = new ConcurrentQueue<string>();
        ws.OnMessage += doc =>
        {
            collected.Enqueue(doc.RootElement.ToString().Substring(0, Math.Min(140, doc.RootElement.ToString().Length)));
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                var t = typeProp.GetString();
                if (t == "orderbook_snapshot")
                {
                    snapshotSeen.TrySetResult(doc);
                }
            }
        };
        ws.OnError += ex => snapshotSeen.TrySetException(ex);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await ws.ConnectAsync(timeout.Token);
        Assert.True(ws.IsOpen, "WS should be open after ConnectAsync");

        await ws.SubscribeAsync(
            channels: new[] { "orderbook_delta" },
            tickers:  new[] { ticker },
            cancel:   timeout.Token);

        var winner = await Task.WhenAny(snapshotSeen.Task, Task.Delay(15_000, timeout.Token));
        await ws.CloseAsync();

        _out.WriteLine($"Collected {collected.Count} message(s):");
        foreach (var msg in collected) _out.WriteLine($"  {msg}");

        Assert.True(snapshotSeen.Task.IsCompletedSuccessfully,
            $"Expected an orderbook_snapshot for {ticker} within 15s. Got {collected.Count} messages.");

        var snap = snapshotSeen.Task.Result;
        var msgEl = snap.RootElement.GetProperty("msg");
        Assert.Equal(ticker, msgEl.GetProperty("market_ticker").GetString());
    }
}
