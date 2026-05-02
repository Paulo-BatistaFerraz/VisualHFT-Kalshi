using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MarketConnectors.Kalshi.Auth;

namespace MarketConnectors.Kalshi.Ws;

/// <summary>
/// Minimal Kalshi WebSocket client. Connects with RSA-PSS-signed auth headers,
/// subscribes to channels for a list of market tickers, raises a callback per
/// inbound JSON message.
///
/// Reconnect is the caller's responsibility (the plugin's reconnect loop).
/// This class focuses on a single connection lifecycle.
/// </summary>
public sealed class KalshiWsClient : IDisposable
{
    private static readonly ILog log =
        LogManager.GetLogger(typeof(KalshiWsClient));

    private readonly Uri _uri;
    private readonly KalshiSigner _signer;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private int _msgIdSeq;

    /// <summary>Raised per inbound JSON document. May be called from a background thread.</summary>
    public event Action<JsonDocument>? OnMessage;

    /// <summary>Raised on socket-level errors (connect fail, read fail).</summary>
    public event Action<Exception>? OnError;

    /// <summary>Raised when the socket closes cleanly or with an error.</summary>
    public event Action<WebSocketCloseStatus?>? OnClose;

    public KalshiWsClient(string url, KalshiSigner signer)
    {
        _uri = new Uri(url);
        _signer = signer;
    }

    public bool IsOpen => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        if (_ws is not null) throw new InvalidOperationException("Already connected/connecting.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        _ws = new ClientWebSocket();

        // Sign the WS upgrade GET against the path; Kalshi requires the same auth as REST.
        var headers = _signer.BuildHeaders("GET", _uri.AbsolutePath);
        _ws.Options.SetRequestHeader("KALSHI-ACCESS-KEY", headers.KeyId);
        _ws.Options.SetRequestHeader("KALSHI-ACCESS-SIGNATURE", headers.Signature);
        _ws.Options.SetRequestHeader("KALSHI-ACCESS-TIMESTAMP", headers.Timestamp);

        log.Info($"Connecting WS {_uri}");
        await _ws.ConnectAsync(_uri, _cts.Token).ConfigureAwait(false);
        log.Info($"Connected. State={_ws.State}");

        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Send a subscribe command:
    /// { "id": N, "cmd": "subscribe", "params": { "channels": [...], "market_tickers": [...] } }
    /// </summary>
    public Task SubscribeAsync(IEnumerable<string> channels, IEnumerable<string> tickers, CancellationToken cancel = default)
    {
        var payload = new
        {
            id = Interlocked.Increment(ref _msgIdSeq),
            cmd = "subscribe",
            @params = new
            {
                channels = channels,
                market_tickers = tickers
            }
        };
        return SendJsonAsync(payload, cancel);
    }

    public async Task SendJsonAsync(object payload, CancellationToken cancel = default)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WS not open");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        log.Debug($"send: {Encoding.UTF8.GetString(bytes)}");
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancel).ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        if (_cts is null) return;
        try
        {
            if (_ws is { State: WebSocketState.Open })
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
        }
        catch (Exception ex) { log.Warn($"close error: {ex.Message}"); }

        _cts.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); }
            catch { /* ignore */ }
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancel)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var assembled = new System.IO.MemoryStream();
            while (!cancel.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                assembled.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        log.Info($"WS close received: status={result.CloseStatus} desc={result.CloseStatusDescription}");
                        OnClose?.Invoke(result.CloseStatus);
                        return;
                    }
                    assembled.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                try
                {
                    var doc = JsonDocument.Parse(assembled.ToArray());
                    OnMessage?.Invoke(doc);
                }
                catch (JsonException jex)
                {
                    log.Warn($"non-JSON or malformed payload: {jex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* expected on close */ }
        catch (Exception ex)
        {
            log.Error("WS read loop error", ex);
            OnError?.Invoke(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            OnClose?.Invoke(_ws?.CloseStatus);
        }
    }

    public void Dispose()
    {
        try { CloseAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
