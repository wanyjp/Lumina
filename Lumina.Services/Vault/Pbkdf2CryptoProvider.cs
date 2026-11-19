using System.Security.Cryptography;
using System.Text;
using Lumina.Services.Contracts;

namespace Lumina.Services.Vault;

/// <summary>
/// P3-07 加密实现：PBKDF2-HMAC-SHA256 派生 32 字节主密钥（每安装 16 字节随机盐），
/// AES-GCM 每次随机 12 字节 nonce、16 字节认证标签，密文布局为
/// <c>[版本 1B][nonce 12B][tag 16B][ciphertext]</c>。
/// </summary>
public sealed class Pbkdf2CryptoProvider : ICryptoProvider
{
    /// <summary>默认迭代次数：OWASP 2023 对 PBKDF2-HMAC-SHA256 的建议下限。</summary>
    public const int DefaultIterations = 600_000;

    public const int SaltSize = 16;
    public const int KeySize = 32;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const byte BlobVersion = 1;

    private static readonly byte[] VerifyContext = Encoding.UTF8.GetBytes("lumina.vault.verify.v1");

    private readonly int _iterations;

    public Pbkdf2CryptoProvider(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        _iterations = iterations;
    }

    public int Iterations => _iterations;

    public (byte[] Key, byte[] Salt) DeriveMasterKey(string password, byte[]? salt = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        salt ??= RandomNumberGenerator.GetBytes(SaltSize);
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException($"盐长度必须为 {SaltSize} 字节。", nameof(salt));
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return (key, salt);
    }

    public byte[] Encrypt(byte[] data, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateKey(key);

        var blob = new byte[1 + NonceSize + TagSize + data.Length];
        blob[0] = BlobVersion;

        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        var cipher = blob.AsSpan(1 + NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return blob;
    }

    public byte[] Decrypt(byte[] blob, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ValidateKey(key);

        if (blob.Length < 1 + NonceSize + TagSize || blob[0] != BlobVersion)
        {
            throw new CryptographicException("保险库密文格式无效。");
        }

        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        var cipher = blob.AsSpan(1 + NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (AuthenticationTagMismatchException exception)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new CryptographicException("保险库数据校验失败：主密码不正确或数据已损坏。", exception);
        }

        return plain;
    }

    public byte[] ComputeVerificationHash(byte[] key)
    {
        ValidateKey(key);
        return HMACSHA256.HashData(key, VerifyContext);
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节。", nameof(key));
        }
    }
}
