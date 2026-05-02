using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MarketConnectors.Kalshi.Auth;
using VisualHFT.Model;

namespace MarketConnectors.Kalshi.Rest;

/// <summary>
/// Polls Kalshi REST endpoints periodically for orderbook snapshots and
/// converts them into VisualHFT.Model.OrderBook updates.
///
/// Used in place of WebSocket streaming when the configured API key lacks
/// WS scope. Polling cadence is set in <see cref="KalshiPluginSettings.PollMs"/>.
/// </summary>
public sealed class KalshiRestPoller : IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(typeof(KalshiRestPoller));

    private readonly HttpClient _http;
    private readonly KalshiSigner _signer;
    private readonly int _depth;
    private readonly int _pollMs;
    private readonly int _providerId;
    private readonly string _providerName;

    public event Action<string, OrderBook>? OnOrderBook;
    public event Action<Exception>? OnError;

    public KalshiRestPoller(string baseUrl, KalshiSigner signer, int depth, int pollMs,
                            int providerId = 100, string providerName = "Kalshi")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _signer = signer;
        _depth = depth;
        _pollMs = Math.Max(100, pollMs);
        _providerId = providerId;
        _providerName = providerName;
    }

    public async Task RunAsync(IReadOnlyList<string> tickers, CancellationToken cancel)
    {
        var books = new ConcurrentDictionary<string, OrderBook>();
        foreach (var t in tickers)
        {
            var lob = new OrderBook(symbol: t, priceDecimalPlaces: 0, maxDepth: _depth);
            lob.ProviderID = _providerId;
            lob.ProviderName = _providerName;
            books[t] = lob;
        }

        log.Info($"Polling {tickers.Count} ticker(s) every {_pollMs}ms.");

        while (!cancel.IsCancellationRequested)
        {
            foreach (var ticker in tickers)
            {
                try
                {
                    await PollOnceAsync(books[ticker], ticker, cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    log.Warn($"poll {ticker} failed: {ex.Message}");
                    OnError?.Invoke(ex);
                }
            }
            try { await Task.Delay(_pollMs, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        foreach (var b in books.Values) b.Dispose();
    }

    private async Task PollOnceAsync(OrderBook lob, string ticker, CancellationToken cancel)
    {
        var path = $"/trade-api/v2/markets/{ticker}/orderbook";
        var query = $"?depth={_depth}";
        var headers = _signer.BuildHeaders("GET", path);

        using var req = new HttpRequestMessage(HttpMethod.Get, path + query);
        req.Headers.Add("KALSHI-ACCESS-KEY", headers.KeyId);
        req.Headers.Add("KALSHI-ACCESS-SIGNATURE", headers.Signature);
        req.Headers.Add("KALSHI-ACCESS-TIMESTAMP", headers.Timestamp);
        req.Headers.Add("Accept", "application/json");

        using var resp = await _http.SendAsync(req, cancel).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
        }

        var json = await resp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var bids = new List<BookItem>();
        var asks = new List<BookItem>();
        var now = DateTime.UtcNow;
        const int yesProviderId = 1;

        if (doc.RootElement.TryGetProperty("orderbook_fp", out var ob))
        {
            // YES bids → bids on our normalized "yes-cents" axis (price = yes_dollars * 100)
            if (ob.TryGetProperty("yes_dollars", out var yes) && yes.ValueKind == JsonValueKind.Array)
            {
                foreach (var lvl in yes.EnumerateArray())
                {
                    if (lvl.GetArrayLength() < 2) continue;
                    if (!double.TryParse(lvl[0].GetString(), out var p)) continue;
                    if (!double.TryParse(lvl[1].GetString(), out var q)) continue;
                    bids.Add(new BookItem
                    {
                        Symbol = ticker,
                        ProviderID = yesProviderId,
                        IsBid = true,
                        Price = Math.Round(p * 100.0, 0),
                        Size = q,
                        EntryID = $"y{p:F4}",
                        LayerName = "MM",
                        LocalTimeStamp = now,
                        ServerTimeStamp = now,
                    });
                }
            }
            // NO bids reflected → asks at price = (1 - no_dollars) * 100  (in cents)
            if (ob.TryGetProperty("no_dollars", out var no) && no.ValueKind == JsonValueKind.Array)
            {
                foreach (var lvl in no.EnumerateArray())
                {
                    if (lvl.GetArrayLength() < 2) continue;
                    if (!double.TryParse(lvl[0].GetString(), out var pNo)) continue;
                    if (!double.TryParse(lvl[1].GetString(), out var q)) continue;
                    asks.Add(new BookItem
                    {
                        Symbol = ticker,
                        ProviderID = yesProviderId,
                        IsBid = false,
                        Price = Math.Round((1.0 - pNo) * 100.0, 0),
                        Size = q,
                        EntryID = $"n{pNo:F4}",
                        LayerName = "MM",
                        LocalTimeStamp = now,
                        ServerTimeStamp = now,
                    });
                }
            }
        }

        bids.Sort((a, b) => (b.Price ?? 0).CompareTo(a.Price ?? 0));
        asks.Sort((a, b) => (a.Price ?? 0).CompareTo(b.Price ?? 0));
        lob.LoadData(asks, bids);
        OnOrderBook?.Invoke(ticker, lob);
    }

    public void Dispose() => _http.Dispose();
}
