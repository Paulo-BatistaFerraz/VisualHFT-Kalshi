using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MarketConnectors.Kalshi.Auth;
using MarketConnectors.Kalshi.Rest;
using MarketConnectors.Kalshi.Settings;
using VisualHFT.Commons.PluginManager;
using VisualHFT.Enums;
using VisualHFT.PluginManager;
using VisualHFT.UserSettings;

namespace MarketConnectors.Kalshi;

/// <summary>
/// Kalshi prediction-markets read-only data connector.
///
/// Default mode: REST polling (works with read-only keys). WebSocket mode is
/// available in code but blocked by Kalshi until the key has WS scope.
/// </summary>
public class KalshiPlugin : BasePluginDataRetriever
{
    private static readonly ILog log =
        LogManager.GetLogger(typeof(KalshiPlugin));

    private KalshiPluginSettings _settings = new();
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private KalshiSigner? _signer;
    private KalshiRestPoller? _poller;

    public override string Name { get; set; } = "Kalshi Plugin";
    public override string Version { get; set; } = "0.2.0";
    public override string Description { get; set; } =
        "Live order book for Kalshi prediction markets (REST polling). Read-only.";
    public override string Author { get; set; } = "Paulo";

    public override ISetting Settings
    {
        get => _settings;
        set => _settings = (KalshiPluginSettings)value;
    }

    public override Action CloseSettingWindow { get; set; } = () => { };

    public KalshiPlugin()
    {
        SetReconnectionAction(InternalStartAsync);
        log.Info($"{Name} loaded.");
    }

    public override async Task StartAsync()
    {
        await base.StartAsync();
        try
        {
            await InternalStartAsync();
            if (Status == ePluginStatus.STOPPED_FAILED) return;
            log.Info($"{Name} started.");
            RaiseOnDataReceived(GetProviderModel(eSESSIONSTATUS.CONNECTED));
            Status = ePluginStatus.STARTED;
        }
        catch (Exception ex)
        {
            LogException(ex, ex.Message);
            await HandleConnectionLost(ex.Message, ex);
        }
    }

    private async Task InternalStartAsync()
    {
        await StopPollingAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_settings.KeyId) ||
            string.IsNullOrWhiteSpace(_settings.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                "Kalshi plugin: Settings.KeyId and Settings.PrivateKeyPath are required.");
        }

        _signer = KalshiSigner.FromPemFile(_settings.KeyId, _settings.PrivateKeyPath);

        var tickers = (_settings.Tickers ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tickers.Count == 0)
            throw new InvalidOperationException("Kalshi plugin: Settings.Tickers is empty.");

        _poller = new KalshiRestPoller(
            _settings.BaseRestUrl, _signer, _settings.Depth, _settings.PollMs,
            providerId: _settings.Provider.ProviderID,
            providerName: _settings.Provider.ProviderName ?? "Kalshi");
        _poller.OnOrderBook += (ticker, lob) =>
        {
            try { RaiseOnDataReceived(lob); }
            catch (Exception ex) { log.Warn($"raise orderbook {ticker}: {ex.Message}"); }
        };
        _poller.OnError += ex => LogException(ex, $"poller error: {ex.Message}");

        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => _poller.RunAsync(tickers, _pollCts.Token));
        log.Info($"Polling {tickers.Count} ticker(s) at {_settings.PollMs}ms.");
    }

    public override async Task StopAsync()
    {
        Status = ePluginStatus.STOPPING;
        await StopPollingAsync().ConfigureAwait(false);
        Status = ePluginStatus.STOPPED;
        RaiseOnDataReceived(GetProviderModel(eSESSIONSTATUS.DISCONNECTED));
        await base.StopAsync();
    }

    private async Task StopPollingAsync()
    {
        if (_pollCts is null) return;
        try { _pollCts.Cancel(); } catch { /* ignore */ }
        if (_pollTask is not null)
        {
            try { await _pollTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
        _poller?.Dispose();
        _poller = null;
        _signer?.Dispose();
        _signer = null;
        _pollCts.Dispose();
        _pollCts = null;
    }

    public override object GetUISettings() => _settings;

    protected override void LoadSettings()
    {
        var loaded = LoadFromUserSettings<KalshiPluginSettings>();
        if (loaded != null) _settings = loaded;
        else InitializeDefaultSettings();
    }

    protected override void SaveSettings() => SaveToUserSettings(_settings);

    protected override void InitializeDefaultSettings() => _settings = new KalshiPluginSettings();
}
