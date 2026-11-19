using Lumina.Data.Vault;
using Lumina.Services.Clipboard;
using Lumina.Services.Contracts;
using Lumina.Services.Security;
using Lumina.Services.Vault;

namespace Lumina.Tests;

/// <summary>
/// P3-09 安全复制验收：只清理本次写入且未被覆盖的内容、锁定立即清空、
/// 关闭自动清除后仅在锁定时清理、剪贴板被占用时不误清。
/// </summary>
public class SecureClipboardBrokerTests
{
    private const string Secret = "Lumina#9xQ!m2";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Copy_writes_secret_and_clears_after_timeout()
    {
        using var fixture = await Fixture.CreateAsync();
        var cleared = new List<SecureClipboardClearReason>();
        fixture.Broker.Cleared += cleared.Add;

        var result = fixture.Broker.CopySecret(Secret);

        Assert.True(result.Success);
        Assert.True(result.AutoClear);
        Assert.Equal(20, result.ClearAfterSeconds);
        Assert.Equal(Secret, fixture.Channel.Text);

        fixture.Advance(TimeSpan.FromSeconds(21));
        fixture.Broker.CheckExpiry();

        Assert.Null(fixture.Channel.Text);
        Assert.Equal(1, fixture.Channel.ClearCount);
        Assert.Equal(SecureClipboardClearReason.Expired, Assert.Single(cleared));
    }

    [Fact]
    public async Task Expiry_does_not_clear_content_replaced_by_user()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Broker.CopySecret(Secret);
        fixture.Channel.Text = "用户后来复制的内容";

        fixture.Advance(TimeSpan.FromSeconds(30));
        fixture.Broker.CheckExpiry();

        Assert.Equal("用户后来复制的内容", fixture.Channel.Text);
        Assert.Equal(0, fixture.Channel.ClearCount);
    }

    [Fact]
    public async Task Non_text_clipboard_is_treated_as_replaced_without_warning()
    {
        using var fixture = await Fixture.CreateAsync();
        var cleared = new List<SecureClipboardClearReason>();
        fixture.Broker.Cleared += cleared.Add;
        fixture.Broker.CopySecret(Secret);

        // 用户改成复制图片：文本消失但剪贴板可读。
        fixture.Channel.Text = null;
        fixture.Advance(TimeSpan.FromSeconds(30));
        fixture.Broker.CheckExpiry();

        Assert.Empty(cleared);
        Assert.Equal(0, fixture.Channel.ClearCount);
    }

    [Fact]
    public async Task Locking_vault_clears_pending_secret_immediately()
    {
        using var fixture = await Fixture.CreateAsync();
        var cleared = new List<SecureClipboardClearReason>();
        fixture.Broker.Cleared += cleared.Add;
        fixture.Broker.CopySecret(Secret);

        fixture.Vault.Lock();

        Assert.Null(fixture.Channel.Text);
        Assert.Equal(1, fixture.Channel.ClearCount);
        Assert.Equal(SecureClipboardClearReason.Locked, Assert.Single(cleared));
    }

    [Fact]
    public async Task Expiry_is_disabled_when_policy_is_zero()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Vault.SetClearClipboardSecondsAsync(0, TestContext.Current.CancellationToken);

        var result = fixture.Broker.CopySecret(Secret);

        Assert.True(result.Success);
        Assert.False(result.AutoClear);
        Assert.Equal(0, fixture.Broker.ClearAfterSeconds);

        fixture.Advance(TimeSpan.FromHours(2));
        fixture.Broker.CheckExpiry();
        Assert.Equal(Secret, fixture.Channel.Text);

        fixture.Vault.Lock();
        Assert.Null(fixture.Channel.Text);
    }

    [Fact]
    public async Task Empty_text_is_rejected_without_writing()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = fixture.Broker.CopySecret(string.Empty);

        Assert.False(result.Success);
        Assert.Equal(SecureCopyFailure.Empty, result.Failure);
        Assert.Equal(0, fixture.Channel.WriteCount);
    }

    [Fact]
    public async Task Unreadable_clipboard_retries_then_gives_up_without_clearing()
    {
        using var fixture = await Fixture.CreateAsync();
        var cleared = new List<SecureClipboardClearReason>();
        fixture.Broker.Cleared += cleared.Add;
        fixture.Broker.CopySecret(Secret);
        fixture.Channel.ReadFails = true;

        fixture.Advance(TimeSpan.FromSeconds(21));
        fixture.Broker.CheckExpiry();
        Assert.Empty(cleared);
        Assert.Equal(0, fixture.Channel.ClearCount);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            fixture.Advance(TimeSpan.FromSeconds(1));
            fixture.Broker.CheckExpiry();
        }

        Assert.Equal(SecureClipboardClearReason.Unavailable, Assert.Single(cleared));
        Assert.Equal(0, fixture.Channel.ClearCount);
    }

    [Fact]
    public async Task Clipboard_unavailable_reports_failure()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Channel.WriteFails = true;

        var result = fixture.Broker.CopySecret(Secret);

        Assert.False(result.Success);
        Assert.Equal(SecureCopyFailure.ClipboardUnavailable, result.Failure);
    }

    [Fact]
    public void Vault_window_titles_are_treated_as_sensitive_sources()
    {
        Assert.True(ClipboardPrivacyFilter.IsVaultWindow("密码生成器"));
        Assert.True(ClipboardPrivacyFilter.IsVaultWindow("密码条目 - 拾光"));
        Assert.False(ClipboardPrivacyFilter.IsVaultWindow("记事本"));
        Assert.False(ClipboardPrivacyFilter.IsVaultWindow(null));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDataRoot _temp;
        private DateTimeOffset _now;

        private Fixture(TemporaryDataRoot temp, VaultService vault, FakeChannel channel, DateTimeOffset now)
        {
            _temp = temp;
            _now = now;
            Vault = vault;
            Channel = channel;
            Broker = new SecureClipboardBroker(channel, vault, () => _now);
        }

        public VaultService Vault { get; }

        public FakeChannel Channel { get; }

        public SecureClipboardBroker Broker { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var temp = new TemporaryDataRoot();
            temp.Initialize();
            var vault = new VaultService(
                new VaultRepository(temp.Database),
                new Pbkdf2CryptoProvider(iterations: 1_000),
                new PassThroughProtector(),
                () => Now);
            await vault.SetupAsync("Lumina#2026", TestContext.Current.CancellationToken);
            return new Fixture(temp, vault, new FakeChannel(), Now);
        }

        public void Advance(TimeSpan delta) => _now += delta;

        public void Dispose()
        {
            Broker.Dispose();
            Vault.Dispose();
            _temp.Dispose();
        }
    }

    private sealed class FakeChannel : ISecureClipboardChannel
    {
        public string? Text { get; set; }

        public int WriteCount { get; private set; }

        public int ClearCount { get; private set; }

        public bool WriteFails { get; set; }

        public bool ReadFails { get; set; }

        public bool TryWriteSecret(string text)
        {
            if (WriteFails)
            {
                return false;
            }

            Text = text;
            WriteCount++;
            return true;
        }

        public string? TryReadTextHash()
        {
            if (ReadFails)
            {
                return null;
            }

            return string.IsNullOrEmpty(Text)
                ? string.Empty
                : SecureClipboardBroker.ComputeContentHash(Text);
        }

        public bool TryClear()
        {
            Text = null;
            ClearCount++;
            return true;
        }
    }

    private sealed class PassThroughProtector : IVaultSecretProtector
    {
        public bool IsAvailable => true;

        public byte[] Protect(byte[] data) => (byte[])data.Clone();

        public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
    }
}
