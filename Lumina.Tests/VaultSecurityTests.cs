using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lumina.Data.Vault;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Vault;

namespace Lumina.Tests;

/// <summary>
/// P3-10 安全专项：锁定后内存主密钥被清零、库文件与 WAL 不出现主密码/条目密码/备注明文。
/// </summary>
public class VaultSecurityTests
{
    private const string MasterPassword = "Lumina#Master#2026";
    private const string EntryPassword = "Plain#Entry#9f3a2c";
    private const string EntryNote = "备注明文-Probe-7d41";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 11, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Lock_zeroes_master_key_in_memory()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        await service.SetupAsync(MasterPassword, TestContext.Current.CancellationToken);

        var field = typeof(VaultService).GetField(
            "_key",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var key = (byte[]?)field.GetValue(service);
        Assert.NotNull(key);
        Assert.Contains(key!, value => value != 0);

        service.Lock();

        Assert.Null(field.GetValue(service));
        Assert.All(key!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Database_and_wal_do_not_contain_plaintext_secrets()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        await service.SetupAsync(MasterPassword, TestContext.Current.CancellationToken);
        var entry = await service.SaveDraftAsync(
            new VaultEntryDraft
            {
                Category = VaultCategory.Login,
                Title = "安全抽查条目",
                Username = "probe@lumina.app",
                Password = EntryPassword,
                Note = EntryNote,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // 读回确认密文可解密，保证数据确实落库。
        Assert.Equal(EntryPassword, await service.RevealPasswordAsync(entry.Id, TestContext.Current.CancellationToken));

        foreach (var path in new[] { temp.DatabasePath, temp.DatabasePath + "-wal" })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = ReadAllBytesShared(path);
            Assert.False(
                Contains(bytes, Encoding.UTF8.GetBytes(MasterPassword)),
                $"{Path.GetFileName(path)} 出现了主密码明文");
            Assert.False(
                Contains(bytes, Encoding.UTF8.GetBytes(EntryPassword)),
                $"{Path.GetFileName(path)} 出现了条目密码明文");
            Assert.False(
                Contains(bytes, Encoding.UTF8.GetBytes(EntryNote)),
                $"{Path.GetFileName(path)} 出现了备注明文");
        }
    }

    private static VaultService CreateService(TemporaryDataRoot temp) =>
        new(
            new VaultRepository(temp.Database),
            new Pbkdf2CryptoProvider(iterations: 1_000),
            new PassThroughProtector(),
            () => Now);

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private sealed class PassThroughProtector : IVaultSecretProtector
    {
        public bool IsAvailable => true;

        public byte[] Protect(byte[] data) => (byte[])data.Clone();

        public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
    }
}
