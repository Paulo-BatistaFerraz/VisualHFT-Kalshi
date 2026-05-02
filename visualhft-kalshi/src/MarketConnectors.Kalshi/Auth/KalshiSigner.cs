using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MarketConnectors.Kalshi.Auth;

/// <summary>
/// RSA-PSS-SHA256 signer for Kalshi API requests.
///
/// Mirrors the Python cryptography signer 1:1 (kalshi-data/kalshi_client.py):
///   message = $"{timestampMs}{method}{path}"
///   signature = RSA-PSS(SHA256, salt = digest length) over UTF-8 bytes
///   value = base64(signature)
///
/// Headers produced for an HTTP/WS request:
///   KALSHI-ACCESS-KEY        = key id
///   KALSHI-ACCESS-SIGNATURE  = base64 signature
///   KALSHI-ACCESS-TIMESTAMP  = unix ms as decimal string
/// </summary>
public sealed class KalshiSigner : IDisposable
{
    public string KeyId { get; }
    private readonly RSA _rsa;
    private bool _disposed;

    private KalshiSigner(string keyId, RSA rsa)
    {
        KeyId = keyId;
        _rsa = rsa;
    }

    public static KalshiSigner FromPemFile(string keyId, string pemPath)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key id is required.", nameof(keyId));
        if (!File.Exists(pemPath))
            throw new FileNotFoundException($"PEM key not found at {pemPath}", pemPath);

        var pem = File.ReadAllText(pemPath);
        return FromPem(keyId, pem);
    }

    public static KalshiSigner FromPem(string keyId, string pemContents)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pemContents);
        return new KalshiSigner(keyId, rsa);
    }

    /// <summary>
    /// Sign a message of form "{timestampMs}{method}{path}" and return base64.
    /// </summary>
    public string Sign(long timestampMs, string method, string path)
    {
        ThrowIfDisposed();
        var message = Encoding.UTF8.GetBytes($"{timestampMs}{method}{path}");
        var signature = _rsa.SignData(
            message,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Build the three Kalshi auth headers for a given method + path.
    /// Uses current UTC time for the timestamp.
    /// </summary>
    public AuthHeaders BuildHeaders(string method, string path)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new AuthHeaders(
            KeyId,
            Sign(ts, method, path),
            ts.ToString());
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KalshiSigner));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _rsa.Dispose();
        _disposed = true;
    }
}

public readonly record struct AuthHeaders(string KeyId, string Signature, string Timestamp);
