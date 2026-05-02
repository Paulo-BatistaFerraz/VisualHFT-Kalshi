using System;
using System.Security.Cryptography;
using System.Text;
using MarketConnectors.Kalshi.Auth;
using Xunit;

namespace MarketConnectors.Kalshi.Tests;

public class KalshiSignerTests
{
    /// <summary>
    /// Generate a random RSA-2048 key, sign with KalshiSigner, then verify
    /// the signature using the matching public key. Proves we use PSS-SHA256
    /// with the salt convention RSA.VerifyData accepts by default (digest length).
    /// </summary>
    [Fact]
    public void Sign_ProducesPssSignatureVerifiableWithPublicKey()
    {
        using var keypair = RSA.Create(2048);
        var pem = keypair.ExportRSAPrivateKeyPem();

        using var signer = KalshiSigner.FromPem("test-key-id", pem);

        long ts = 1_700_000_000_000L;
        const string method = "GET";
        const string path = "/trade-api/v2/markets";
        string sigB64 = signer.Sign(ts, method, path);

        byte[] sig = Convert.FromBase64String(sigB64);
        byte[] msg = Encoding.UTF8.GetBytes($"{ts}{method}{path}");

        bool ok = keypair.VerifyData(msg, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        Assert.True(ok, "Signature must verify under PSS-SHA256");
    }

    [Fact]
    public void Sign_DifferentTimestamps_ProduceDifferentSignatures()
    {
        using var keypair = RSA.Create(2048);
        using var signer = KalshiSigner.FromPem("k", keypair.ExportRSAPrivateKeyPem());

        var s1 = signer.Sign(1, "GET", "/x");
        var s2 = signer.Sign(2, "GET", "/x");

        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void BuildHeaders_PopulatesAllThreeFields()
    {
        using var keypair = RSA.Create(2048);
        using var signer = KalshiSigner.FromPem("my-key", keypair.ExportRSAPrivateKeyPem());

        var h = signer.BuildHeaders("GET", "/foo");

        Assert.Equal("my-key", h.KeyId);
        Assert.False(string.IsNullOrEmpty(h.Signature));
        Assert.True(long.Parse(h.Timestamp) > 1_700_000_000_000L);
    }
}
