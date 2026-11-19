using System.Security.Cryptography;
using System.Text;
using Lumina.Data.Vault;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Vault;

namespace Lumina.Tests;

/// <summary>P3-07 保险库服务验收：主密码、字段加密、锁定清键与闲置自动锁定。</summary>
public class VaultServiceTests
{
    private const string Password = "Lumina#2026";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(8));

    private static VaultService CreateService(
        TemporaryDataRoot temp,
        Func<DateTimeOffset>? now = null,
        IVaultSecretProtector? protector = null) =>
        new(
            new VaultRepository(temp.Database),
            new Pbkdf2CryptoProvider(iterations: 1_000),
            protector ?? new PassThroughProtector(),
            now ?? (() => Now));

    [Fact]
    public async Task Setup_creates_metadata_and_unlocks()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        Assert.False(service.IsConfigured);
        Assert.True(service.IsLocked);

        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        Assert.True(service.IsConfigured);
        Assert.False(service.IsLocked);

        var metadata = new VaultRepository(temp.Database).ReadMetadata();
        Assert.NotNull(metadata);
        Assert.Equal(Pbkdf2CryptoProvider.SaltSize, metadata!.Salt.Length);
        Assert.Equal(1_000, metadata.Iterations);
        Assert.Equal(5, metadata.AutoLockMinutes);
        Assert.Equal(1, metadata.MasterHash[0]);
    }

    [Fact]
    public async Task Setup_rejects_short_password()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetupAsync("short", TestContext.Current.CancellationToken));
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public async Task Unlock_accepts_correct_password_and_rejects_wrong_one()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        service.Lock();
        Assert.True(service.IsLocked);
        Assert.False(await service.UnlockAsync("wrong-password", TestContext.Current.CancellationToken));
        Assert.True(service.IsLocked);

        Assert.True(await service.UnlockAsync(Password, TestContext.Current.CancellationToken));
        Assert.False(service.IsLocked);
    }

    [Fact]
    public async Task Locked_service_does_not_load_entries_or_allow_mutation()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        await AddLoginAsync(service);

        service.Lock();

        Assert.Empty(await service.ListAsync(VaultCategory.All, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<VaultLockedException>(() =>
            service.RevealPasswordAsync(1, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<VaultLockedException>(() =>
            service.AddEntryAsync(
                new VaultEntry { Title = "锁定后新增", CreatedAt = Now, UpdatedAt = Now },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entry_password_is_encrypted_at_rest_and_revealable()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        var entry = await AddLoginAsync(service);
        var stored = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));

        Assert.NotNull(stored.EncryptedPassword);
        Assert.NotEqual(Encoding.UTF8.GetBytes("S3cret!"), stored.EncryptedPassword);
        Assert.Equal("S3cret!", await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));

        // 主库与 WAL 都检查一遍，确认落盘没有明文密码。
        foreach (var file in new[] { temp.DatabasePath, $"{temp.DatabasePath}-wal" })
        {
            if (!File.Exists(file))
            {
                continue;
            }

            var bytes = await ReadSharedAsync(file);
            Assert.DoesNotContain("S3cret!", Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public async Task Update_keeps_password_when_null_and_clears_when_empty()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        var current = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        await service.UpdateEntryAsync(
            current with { Title = "GitHub 改名", EncryptedPassword = null },
            TestContext.Current.CancellationToken);

        Assert.Equal("GitHub 改名", Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken)).Title);
        Assert.Equal("S3cret!", await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));

        var updated = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        await service.UpdateEntryAsync(
            updated with { EncryptedPassword = [] },
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_removes_entry()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        await service.DeleteEntryAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await service.ListAsync(VaultCategory.All, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task List_filters_by_category_keyword_and_favorite()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;

        await AddLoginAsync(service);
        await service.AddEntryAsync(
            new VaultEntry
            {
                Title = "招商银行",
                Category = VaultCategory.BankCard,
                Username = "尾号 6688",
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            token);
        await service.AddEntryAsync(
            new VaultEntry
            {
                Title = "身份证",
                Category = VaultCategory.IdDocument,
                Favorite = true,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            token);

        Assert.Single(await service.ListAsync(VaultCategory.BankCard, null, token));
        Assert.Equal("身份证", Assert.Single(await service.ListAsync(VaultCategory.Favorite, null, token)).Title);
        Assert.Equal("GitHub", Assert.Single(await service.ListAsync(VaultCategory.All, "git", token)).Title);
        Assert.Equal("招商银行", Assert.Single(await service.ListAsync(VaultCategory.All, "招商", token)).Title);
        Assert.Single(await service.ListAsync(VaultCategory.All, "octocat", token));
    }

    [Fact]
    public async Task Reveal_updates_used_at()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var clock = Now;
        using var service = CreateService(temp, () => clock);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        clock = Now.AddHours(3);
        await service.RevealPasswordAsync(entry.Id, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        Assert.Equal(clock, stored.UsedAt);
    }

    [Fact]
    public async Task Auto_lock_uses_idle_timeout_and_can_be_disabled()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var clock = Now;
        using var service = CreateService(temp, () => clock);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        await service.SetAutoLockMinutesAsync(1, TestContext.Current.CancellationToken);

        clock = Now.AddSeconds(30);
        service.NotifyActivity();
        clock = Now.AddSeconds(80);
        service.CheckAutoLock();
        Assert.False(service.IsLocked);

        clock = Now.AddSeconds(141);
        service.CheckAutoLock();
        Assert.True(service.IsLocked);

        Assert.True(await service.UnlockAsync(Password, TestContext.Current.CancellationToken));
        await service.SetAutoLockMinutesAsync(0, TestContext.Current.CancellationToken);
        clock = Now.AddHours(5);
        service.CheckAutoLock();
        Assert.False(service.IsLocked);
    }

    [Fact]
    public async Task Clear_clipboard_seconds_are_persisted_and_bounded()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        Assert.Equal(20, service.ClearClipboardSeconds);

        await service.SetClearClipboardSecondsAsync(45, TestContext.Current.CancellationToken);
        Assert.Equal(45, service.ClearClipboardSeconds);
        Assert.Equal(45, new VaultRepository(temp.Database).ReadMetadata()!.ClearClipboardSeconds);

        await service.SetClearClipboardSecondsAsync(0, TestContext.Current.CancellationToken);
        Assert.Equal(0, service.ClearClipboardSeconds);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SetClearClipboardSecondsAsync(301, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unlock_reports_dpapi_account_mismatch()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var protector = new PassThroughProtector();
        using var service = CreateService(temp, protector: protector);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        service.Lock();

        protector.FailOnUnprotect = true;

        await Assert.ThrowsAsync<VaultException>(() =>
            service.UnlockAsync(Password, TestContext.Current.CancellationToken));
        Assert.True(service.IsLocked);
    }

    [Fact]
    public async Task Protector_unavailable_falls_back_to_plain_hash()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var protector = new PassThroughProtector { IsAvailable = false };
        using var service = CreateService(temp, protector: protector);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        var metadata = new VaultRepository(temp.Database).ReadMetadata();
        Assert.NotNull(metadata);
        Assert.Equal(0, metadata!.MasterHash[0]);

        service.Lock();
        Assert.True(await service.UnlockAsync(Password, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveDraft_adds_and_reads_plaintext()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        var saved = await service.SaveDraftAsync(
            new VaultEntryDraft
            {
                Title = "  草稿条目  ",
                Category = VaultCategory.BankCard,
                Username = "  card-user  ",
                Password = "draft-pass-2026",
                Url = "https://bank.example.com",
                Note = "开户行：示例支行",
                Favorite = true,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(saved.Id > 0);
        Assert.Equal("草稿条目", saved.Title);
        Assert.Equal(VaultCategory.BankCard, saved.Category);
        Assert.True(saved.Favorite);

        var draft = await service.ReadDraftAsync(
            saved.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(draft);
        Assert.Equal("card-user", draft!.Username);
        Assert.Equal("draft-pass-2026", draft.Password);
        Assert.Equal("开户行：示例支行", draft.Note);

        Assert.Equal("draft-pass-2026", await service.RevealPasswordAsync(
            saved.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveDraft_update_preserves_created_and_used_times()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var clock = Now;
        using var service = CreateService(temp, () => clock);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var created = await service.SaveDraftAsync(
            new VaultEntryDraft { Title = "旧标题", Password = "old-pass" },
            cancellationToken: TestContext.Current.CancellationToken);

        clock = Now.AddMinutes(5);
        await service.RevealPasswordAsync(created.Id, TestContext.Current.CancellationToken);
        var usedAt = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken)).UsedAt;
        Assert.NotNull(usedAt);

        clock = Now.AddHours(2);
        await service.SaveDraftAsync(
            new VaultEntryDraft
            {
                Title = "新标题",
                Category = VaultCategory.Login,
                Username = "new-user",
                Password = "new-pass",
            },
            created.Id,
            TestContext.Current.CancellationToken);

        var updated = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        Assert.Equal("新标题", updated.Title);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(usedAt, updated.UsedAt);
        Assert.Equal(clock, updated.UpdatedAt);
    }

    [Fact]
    public async Task ReadDraft_and_reveal_note_do_not_mark_used()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        await service.ReadDraftAsync(entry.Id, TestContext.Current.CancellationToken);
        var note = await service.RevealNoteAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.Equal("二次验证已开启", note);
        var stored = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        Assert.Null(stored.UsedAt);
    }

    [Fact]
    public async Task SetFavorite_toggles_without_reencrypting_password()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        await service.SetFavoriteAsync(entry.Id, true, TestContext.Current.CancellationToken);

        var stored = Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
        Assert.True(stored.Favorite);
        Assert.Equal("S3cret!", await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));
    }

    /// <summary>SQLite 连接池仍持有文件句柄，用共享读打开再扫描。</summary>
    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, TestContext.Current.CancellationToken);
        return memory.ToArray();
    }

    [Fact]
    public async Task Setup_with_recovery_persists_question_and_rejects_wrong_answer()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);

        await service.SetupAsync(
            Password,
            "我的出生城市是？",
            "Chengdu",
            TestContext.Current.CancellationToken);

        Assert.True(service.HasRecovery);
        Assert.Equal("我的出生城市是？", service.RecoveryQuestion);

        using var reloaded = CreateService(temp);
        Assert.True(reloaded.HasRecovery);
        Assert.False(await reloaded.ResetPasswordAsync(
            "错误的答案",
            "NewPassword#2027",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reset_with_security_answer_reencrypts_entries_and_switches_password()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(
            Password,
            "我的出生城市是？",
            "Chengdu",
            TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        service.Lock();
        Assert.True(await service.ResetPasswordAsync(
            "  CHENGDU ",
            "NewPassword#2027",
            TestContext.Current.CancellationToken));
        Assert.False(service.IsLocked);
        Assert.Equal("S3cret!", await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));

        service.Lock();
        Assert.False(await service.UnlockAsync(Password, TestContext.Current.CancellationToken));
        Assert.True(await service.UnlockAsync("NewPassword#2027", TestContext.Current.CancellationToken));

        // 重置后安全问题仍然有效。
        Assert.True(await service.ResetPasswordAsync(
            "chengdu",
            "ThirdPassword#2028",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Change_password_reencrypts_entries_and_rotates_recovery()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(
            Password,
            "我的出生城市是？",
            "Chengdu",
            TestContext.Current.CancellationToken);
        var entry = await AddLoginAsync(service);

        await service.ChangePasswordAsync(
            Password,
            "NewPassword#2027",
            TestContext.Current.CancellationToken);
        service.Lock();

        Assert.False(await service.UnlockAsync(Password, TestContext.Current.CancellationToken));
        Assert.True(await service.UnlockAsync("NewPassword#2027", TestContext.Current.CancellationToken));
        Assert.Equal("S3cret!", await service.RevealPasswordAsync(
            entry.Id,
            TestContext.Current.CancellationToken));

        // 修改主密码后无需再次回答安全问题，找回仍可用。
        Assert.True(await service.ResetPasswordAsync(
            "Chengdu",
            "ThirdPassword#2028",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current_password()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<VaultException>(() => service.ChangePasswordAsync(
            "wrong-password",
            "NewPassword#2027",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Legacy_vault_without_recovery_cannot_reset()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        using var service = CreateService(temp);
        await service.SetupAsync(Password, TestContext.Current.CancellationToken);

        Assert.False(service.HasRecovery);
        Assert.Null(service.RecoveryQuestion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetPasswordAsync(
            "any",
            "NewPassword#2027",
            TestContext.Current.CancellationToken));
    }

    private static async Task<VaultEntry> AddLoginAsync(VaultService service)
    {
        await service.AddEntryAsync(
            new VaultEntry
            {
                Title = "GitHub",
                Category = VaultCategory.Login,
                Username = "octocat",
                Url = "https://github.com",
                EncryptedPassword = Encoding.UTF8.GetBytes("S3cret!"),
                EncryptedNote = Encoding.UTF8.GetBytes("二次验证已开启"),
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        return Assert.Single(await service.ListAsync(
            VaultCategory.All,
            null,
            TestContext.Current.CancellationToken));
    }

    private sealed class PassThroughProtector : IVaultSecretProtector
    {
        public bool IsAvailable { get; set; } = true;

        public bool FailOnUnprotect { get; set; }

        public byte[] Protect(byte[] data) => (byte[])data.Clone();

        public byte[] Unprotect(byte[] data) =>
            FailOnUnprotect
                ? throw new CryptographicException("DPAPI 不可用")
                : (byte[])data.Clone();
    }
}
