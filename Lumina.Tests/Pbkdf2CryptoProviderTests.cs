using System.Security.Cryptography;
using System.Text;
using Lumina.Services.Vault;

namespace Lumina.Tests;

/// <summary>P3-07 加密参数验收：随机盐、随机 IV、AES-GCM 认证与校验哈希。</summary>
public class Pbkdf2CryptoProviderTests
{
    private static Pbkdf2CryptoProvider CreateProvider() => new(iterations: 1_000);

    [Fact]
    public void Default_iterations_match_owasp_baseline()
    {
        Assert.True(new Pbkdf2CryptoProvider().Iterations >= 600_000);
    }

    [Fact]
    public void Derive_is_deterministic_for_same_salt()
    {
        var provider = CreateProvider();
        var salt = RandomNumberGenerator.GetBytes(Pbkdf2CryptoProvider.SaltSize);

        var (first, firstSalt) = provider.DeriveMasterKey("correct horse battery", salt);
        var (second, secondSalt) = provider.DeriveMasterKey("correct horse battery", salt);

        Assert.Equal(Pbkdf2CryptoProvider.KeySize, first.Length);
        Assert.Equal(salt, firstSalt);
        Assert.Equal(salt, secondSalt);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_salt_or_password_yields_different_key()
    {
        var provider = CreateProvider();
        var salt = RandomNumberGenerator.GetBytes(Pbkdf2CryptoProvider.SaltSize);
        var otherSalt = RandomNumberGenerator.GetBytes(Pbkdf2CryptoProvider.SaltSize);

        var (baseline, _) = provider.DeriveMasterKey("master-password", salt);
        var (otherPassword, _) = provider.DeriveMasterKey("master-passwore", salt);
        var (otherSaltKey, _) = provider.DeriveMasterKey("master-password", otherSalt);

        Assert.NotEqual(baseline, otherPassword);
        Assert.NotEqual(baseline, otherSaltKey);
    }

    [Fact]
    public void Derive_generates_16_byte_salt_when_omitted()
    {
        var (_, salt) = CreateProvider().DeriveMasterKey("master-password");

        Assert.Equal(Pbkdf2CryptoProvider.SaltSize, salt.Length);
    }

    [Fact]
    public void Encrypt_round_trips_utf8_payload()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");
        var plain = Encoding.UTF8.GetBytes("S3cret! 密码");

        var blob = provider.Encrypt(plain, key);

        Assert.Equal(1 + 12 + 16 + plain.Length, blob.Length);
        Assert.Equal(plain, provider.Decrypt(blob, key));
    }

    [Fact]
    public void Encrypt_uses_random_nonce_for_each_call()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");
        var plain = Encoding.UTF8.GetBytes("same payload");

        var first = provider.Encrypt(plain, key);
        var second = provider.Encrypt(plain, key);

        Assert.NotEqual(first, second);
        Assert.Equal(plain, provider.Decrypt(first, key));
        Assert.Equal(plain, provider.Decrypt(second, key));
    }

    [Fact]
    public void Tampered_blob_fails_authentication()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");
        var blob = provider.Encrypt(Encoding.UTF8.GetBytes("payload"), key);
        blob[^1] ^= 0x5A;

        Assert.Throws<CryptographicException>(() => provider.Decrypt(blob, key));
    }

    [Fact]
    public void Wrong_key_fails_authentication()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");
        var (otherKey, _) = provider.DeriveMasterKey("other-password");
        var blob = provider.Encrypt(Encoding.UTF8.GetBytes("payload"), key);

        Assert.Throws<CryptographicException>(() => provider.Decrypt(blob, otherKey));
    }

    [Fact]
    public void Short_or_unknown_blob_is_rejected()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");

        Assert.Throws<CryptographicException>(() => provider.Decrypt([1, 2, 3], key));
        Assert.Throws<CryptographicException>(() => provider.Decrypt(new byte[40], key));
    }

    [Fact]
    public void Verification_hash_is_stable_and_key_bound()
    {
        var provider = CreateProvider();
        var (key, _) = provider.DeriveMasterKey("master-password");
        var (otherKey, _) = provider.DeriveMasterKey("other-password");

        Assert.Equal(provider.ComputeVerificationHash(key), provider.ComputeVerificationHash(key));
        Assert.NotEqual(provider.ComputeVerificationHash(key), provider.ComputeVerificationHash(otherKey));
    }
}
