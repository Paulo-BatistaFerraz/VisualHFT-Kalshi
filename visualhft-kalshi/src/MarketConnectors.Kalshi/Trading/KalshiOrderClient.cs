using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using log4net;
using MarketConnectors.Kalshi.Auth;

namespace MarketConnectors.Kalshi.Trading;

public enum KalshiSide { Yes, No }
public enum KalshiAction { Buy, Sell }

public readonly record struct KalshiOrderResult(
    bool Success,
    string OrderId,
    string Status,
    int Count,
    string ErrorMessage);

/// <summary>
/// Minimal Kalshi order placement / cancellation client.
/// Read-only paths stay on the polling client; orders go through here.
///
/// Hard-coded safety rails (in addition to UI checks):
///   - count must be 1..MAX_COUNT
///   - price must be 1..99 cents
///   - ticker must be non-empty and start with "KX"
/// </summary>
public sealed class KalshiOrderClient : IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(typeof(KalshiOrderClient));

    public const int MAX_COUNT = 5; // contracts per order

    private readonly HttpClient _http;
    private readonly KalshiSigner _signer;

    public KalshiOrderClient(string baseUrl, KalshiSigner signer)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _signer = signer;
    }

    public static KalshiOrderClient ForDemo()
    {
        const string demoBase = "https://demo-api.kalshi.co";
        var demoKeyId = Environment.GetEnvironmentVariable("KALSHI_DEMO_KEY_ID")
            ?? throw new InvalidOperationException(
                "Set KALSHI_DEMO_KEY_ID to your Kalshi demo API access key id.");
        var demoPemPath = Environment.GetEnvironmentVariable("KALSHI_DEMO_PEM_PATH")
            ?? throw new InvalidOperationException(
                "Set KALSHI_DEMO_PEM_PATH to the absolute path of your Kalshi demo private key PEM file.");
        return new KalshiOrderClient(demoBase, KalshiSigner.FromPemFile(demoKeyId, demoPemPath));
    }

    public async Task<KalshiOrderResult> PlaceLimitAsync(
        string ticker, KalshiSide side, KalshiAction action, int priceCents, int count)
    {
        // Defensive checks — UI should already block these but enforce here too.
        if (string.IsNullOrWhiteSpace(ticker) || !ticker.StartsWith("KX", StringComparison.OrdinalIgnoreCase))
            return new(false, "", "", 0, "ticker must start with 'KX'");
        if (count < 1 || count > MAX_COUNT)
            return new(false, "", "", 0, $"count must be 1..{MAX_COUNT}");
        if (priceCents < 1 || priceCents > 99)
            return new(false, "", "", 0, "price must be 1..99 cents");

        var path = "/trade-api/v2/portfolio/orders";
        var clientOrderId = Guid.NewGuid().ToString();

        var payload = new JsonObject
        {
            ["ticker"] = ticker,
            ["client_order_id"] = clientOrderId,
            ["type"] = "limit",
            ["action"] = action == KalshiAction.Buy ? "buy" : "sell",
            ["side"] = side == KalshiSide.Yes ? "yes" : "no",
            ["count"] = count
        };
        // Kalshi wants yes_price for yes-side orders, no_price for no-side
        if (side == KalshiSide.Yes) payload["yes_price"] = priceCents;
        else                         payload["no_price"]  = priceCents;

        var bodyJson = payload.ToJsonString();
        var headers = _signer.BuildHeaders("POST", path);

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Add("KALSHI-ACCESS-KEY", headers.KeyId);
        req.Headers.Add("KALSHI-ACCESS-SIGNATURE", headers.Signature);
        req.Headers.Add("KALSHI-ACCESS-TIMESTAMP", headers.Timestamp);
        req.Headers.Add("Accept", "application/json");
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        log.Info($"PLACE  {ticker}  {side} {action} {count}@{priceCents}c  cid={clientOrderId}");
        try
        {
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                log.Warn($"order rejected: {(int)resp.StatusCode} {respBody}");
                return new(false, "", "", 0, $"{(int)resp.StatusCode}: {respBody.Substring(0, Math.Min(180, respBody.Length))}");
            }
            using var doc = JsonDocument.Parse(respBody);
            var orderEl = doc.RootElement.GetProperty("order");
            string orderId = orderEl.TryGetProperty("order_id", out var idEl) ? idEl.GetString() ?? "" : "";
            string status  = orderEl.TryGetProperty("status",   out var stEl) ? stEl.GetString() ?? "" : "";
            log.Info($"order placed: id={orderId} status={status}");
            return new(true, orderId, status, count, "");
        }
        catch (Exception ex)
        {
            log.Error("order placement failed", ex);
            return new(false, "", "", 0, ex.Message);
        }
    }

    public async Task<bool> CancelAsync(string orderId)
    {
        if (string.IsNullOrEmpty(orderId)) return false;
        var path = $"/trade-api/v2/portfolio/orders/{orderId}";
        var headers = _signer.BuildHeaders("DELETE", path);
        using var req = new HttpRequestMessage(HttpMethod.Delete, path);
        req.Headers.Add("KALSHI-ACCESS-KEY", headers.KeyId);
        req.Headers.Add("KALSHI-ACCESS-SIGNATURE", headers.Signature);
        req.Headers.Add("KALSHI-ACCESS-TIMESTAMP", headers.Timestamp);
        req.Headers.Add("Accept", "application/json");
        log.Info($"CANCEL {orderId}");
        try
        {
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            log.Error("cancel failed", ex);
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
