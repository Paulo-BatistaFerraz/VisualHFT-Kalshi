using System;
using System.Net.Http;
using System.Threading.Tasks;
using MarketConnectors.Kalshi.Auth;
using Xunit;
using Xunit.Abstractions;

namespace MarketConnectors.Kalshi.Tests;

public class LiveRestSmokeTest
{
    private readonly ITestOutputHelper _out;
    public LiveRestSmokeTest(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// Sanity check: same KalshiSigner against a known-good REST endpoint.
    /// If this passes but the WS test fails, the bug is WS-specific (scope, path).
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task SignerWorks_OnRestMarketsEndpoint()
    {
        var pemPath = Environment.GetEnvironmentVariable("KALSHI_PEM_PATH")
                      ?? throw new InvalidOperationException(
                          "Set KALSHI_PEM_PATH to the absolute path of your Kalshi private key PEM file.");
        var keyId   = Environment.GetEnvironmentVariable("KALSHI_KEY_ID")
                      ?? throw new InvalidOperationException(
                          "Set KALSHI_KEY_ID to your Kalshi API access key id.");
        const string baseUrl = "https://api.elections.kalshi.com";
        const string path = "/trade-api/v2/markets";

        using var signer = KalshiSigner.FromPemFile(keyId, pemPath);
        var headers = signer.BuildHeaders("GET", path);

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var req = new HttpRequestMessage(HttpMethod.Get, path + "?limit=1&status=open");
        req.Headers.Add("KALSHI-ACCESS-KEY", headers.KeyId);
        req.Headers.Add("KALSHI-ACCESS-SIGNATURE", headers.Signature);
        req.Headers.Add("KALSHI-ACCESS-TIMESTAMP", headers.Timestamp);
        req.Headers.Add("Accept", "application/json");

        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        _out.WriteLine($"Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
        _out.WriteLine($"Body (first 200): {body[..Math.Min(200, body.Length)]}");
        Assert.True(resp.IsSuccessStatusCode, $"REST should 200; got {(int)resp.StatusCode}");
    }
}
