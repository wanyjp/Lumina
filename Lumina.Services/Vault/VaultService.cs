using System.Security.Cryptography;
using System.Text;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Vault;

/// <summary>
/// P3-07 保险库服务：首次设置主密码、校验解锁、手动/闲置/锁屏锁定、条目字段加解密。
/// 密钥只保存在内存；<see cref="Lock"/> 会立即零化密钥并停止自动锁定计时。
/// 主密码校验哈希在落库前附加本机保护（DPAPI），数据库被拷走也无法直接校验。
/// </summary>
public sealed class VaultService : IVaultService, IDisposable
{
    public const int MinimumPasswordLength = 8;

    private const int MinimumAnswerLength = 2;
    private const int MaximumAnswerLength = 100;
    private const int MaximumQuestionLength = 80;
    private const int RecoverySaltSize = 16;

    /// <summary>校验哈希首字节的保护方案标记。</summary>
    private const byte HashSchemePlain = 0;
    private const byte HashSchemeProtected = 1;

    private static readonly TimeSpan AutoLockCheckInterval = TimeSpan.FromSeconds(10);

    private readonly IVaultStore _store;
    private readonly ICryptoProvider _crypto;
    private readonly IVaultSecretProtector _protector;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    private VaultMetadata? _meta;
    private byte[]? _key;
    private DateTimeOffset _lastActivity;
    private Timer? _autoLockTimer;
    private bool _disposed;

    public VaultService(
        IVaultStore store,
        ICryptoProvider crypto,
        IVaultSecretProtector protector,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(protector);

        _store = store;
        _crypto = crypto;
        _protector = protector;
        _now = now ?? (() => DateTimeOffset.Now);
        _meta = store.ReadMetadata();
    }

    public event Action? Changed;

    public event Action? LockStateChanged;

    public bool IsConfigured
    {
        get
        {
            lock (_gate)
            {
                return _meta is not null;
            }
        }
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                return _key is null;
            }
        }
    }

    public int AutoLockMinutes
    {
        get
        {
            lock (_gate)
            {
                return _meta?.AutoLockMinutes ?? 5;
            }
        }
    }

    public int ClearClipboardSeconds
    {
        get
        {
            lock (_gate)
            {
                return _meta?.ClearClipboardSeconds ?? 20;
            }
        }
    }

    public async Task SetupAsync(string masterPassword, CancellationToken cancellationToken = default) =>
        await SetupCoreAsync(masterPassword, question: null, answer: null, cancellationToken);

    public async Task SetupAsync(
        string masterPassword,
        string securityQuestion,
        string securityAnswer,
        CancellationToken cancellationToken = default)
    {
        var question = NormalizeQuestion(securityQuestion);
        var answer = NormalizeAnswer(securityAnswer);
        ValidateAnswer(answer);

        await SetupCoreAsync(masterPassword, question, answer, cancellationToken);
    }

    public bool HasRecovery
    {
        get
        {
            lock (_gate)
            {
                return _meta is not null
                    && !string.IsNullOrWhiteSpace(_meta.RecoveryQuestion)
                    && _meta.RecoveryBlob is { Length: > 0 }
                    && _meta.RecoveryAnswerSalt is { Length: > 0 };
            }
        }
    }

    public string? RecoveryQuestion
    {
        get
        {
            lock (_gate)
            {
                return _meta?.RecoveryQuestion;
            }
        }
    }

    private async Task SetupCoreAsync(
        string masterPassword,
        string? question,
        string? answer,
        CancellationToken cancellationToken)
    {
        ValidatePassword(masterPassword);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_meta is not null)
            {
                throw new InvalidOperationException("保险库已经设置过主密码。");
            }
        }

        // PBKDF2 高迭代是 CPU 密集操作，放到线程池，避免解锁时卡住界面。
        var (key, salt) = await Task.Run(
            () => _crypto.DeriveMasterKey(masterPassword),
            cancellationToken);

        byte[]? recoveryKey = null;
        try
        {
            var hash = _crypto.ComputeVerificationHash(key);
            byte[]? recoverySalt = null;
            byte[]? recoveryBlob = null;
            byte[]? recoveryKeyBlob = null;

            if (question is not null && answer is not null)
            {
                recoverySalt = RandomNumberGenerator.GetBytes(RecoverySaltSize);
                recoveryKey = _crypto.DeriveMasterKey(answer, recoverySalt).Key;
                recoveryBlob = _crypto.Encrypt(key, recoveryKey);
                recoveryKeyBlob = _crypto.Encrypt(recoveryKey, key);
            }

            var metadata = new VaultMetadata
            {
                Salt = salt,
                MasterHash = ProtectHash(hash),
                Iterations = _crypto.Iterations,
                AutoLockMinutes = 5,
                ClearClipboardSeconds = 20,
                RecoveryQuestion = question,
                RecoveryAnswerSalt = recoverySalt,
                RecoveryBlob = recoveryBlob,
                RecoveryKeyBlob = recoveryKeyBlob,
            };

            lock (_gate)
            {
                if (_meta is not null)
                {
                    throw new InvalidOperationException("保险库已经设置过主密码。");
                }

                _store.WriteMetadata(metadata);
                _meta = metadata;
                ReplaceKey(key);
                _lastActivity = _now();
                StartTimer();
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            if (recoveryKey is not null)
            {
                CryptographicOperations.ZeroMemory(recoveryKey);
            }
        }

        Changed?.Invoke();
        LockStateChanged?.Invoke();
    }

    public async Task<bool> ResetPasswordAsync(
        string securityAnswer,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityAnswer);
        ValidatePassword(newMasterPassword);
        cancellationToken.ThrowIfCancellationRequested();

        VaultMetadata meta;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            meta = _meta ?? throw new InvalidOperationException("尚未设置主密码，请先创建保险库。");
        }

        if (meta.RecoveryBlob is not { Length: > 0 }
            || meta.RecoveryAnswerSalt is not { Length: > 0 })
        {
            throw new InvalidOperationException("该保险库未设置安全问题，无法找回主密码。");
        }

        var answer = NormalizeAnswer(securityAnswer);
        var recoveryKey = await Task.Run(
            () => _crypto.DeriveMasterKey(answer, meta.RecoveryAnswerSalt).Key,
            cancellationToken);

        byte[]? oldKey = null;
        byte[]? newKey = null;
        try
        {
            try
            {
                oldKey = _crypto.Decrypt(meta.RecoveryBlob, recoveryKey);
            }
            catch (CryptographicException)
            {
                return false;
            }

            var expected = UnprotectHash(meta.MasterHash);
            var actual = _crypto.ComputeVerificationHash(oldKey);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return false;
            }

            var (derived, newSalt) = await Task.Run(
                () => _crypto.DeriveMasterKey(newMasterPassword),
                cancellationToken);
            newKey = derived;

            lock (_gate)
            {
                ReEncryptAll(oldKey, newKey);

                var updated = meta with
                {
                    Salt = newSalt,
                    MasterHash = ProtectHash(_crypto.ComputeVerificationHash(newKey)),
                    Iterations = _crypto.Iterations,
                    RecoveryBlob = _crypto.Encrypt(newKey, recoveryKey),
                    RecoveryKeyBlob = _crypto.Encrypt(recoveryKey, newKey),
                };

                _store.WriteMetadata(updated);
                _meta = updated;
                ReplaceKey(newKey);
                _lastActivity = _now();
                StartTimer();
            }

            newKey = null;
        }
        finally
        {
            if (oldKey is not null)
            {
                CryptographicOperations.ZeroMemory(oldKey);
            }

            if (newKey is not null)
            {
                CryptographicOperations.ZeroMemory(newKey);
            }

            CryptographicOperations.ZeroMemory(recoveryKey);
        }

        Changed?.Invoke();
        LockStateChanged?.Invoke();
        return true;
    }

    public async Task ChangePasswordAsync(
        string currentPassword,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentPassword);
        ValidatePassword(newMasterPassword);
        cancellationToken.ThrowIfCancellationRequested();

        VaultMetadata meta;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            meta = _meta ?? throw new InvalidOperationException("尚未设置主密码，请先创建保险库。");
        }

        var (oldKey, _) = await Task.Run(
            () => _crypto.DeriveMasterKey(currentPassword, meta.Salt),
            cancellationToken);

        byte[]? newKey = null;
        byte[]? recoveryKey = null;
        try
        {
            var expected = UnprotectHash(meta.MasterHash);
            var actual = _crypto.ComputeVerificationHash(oldKey);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new VaultException("当前主密码不正确。");
            }

            var (derived, newSalt) = await Task.Run(
                () => _crypto.DeriveMasterKey(newMasterPassword),
                cancellationToken);
            newKey = derived;

            if (meta.RecoveryKeyBlob is { Length: > 0 })
            {
                recoveryKey = _crypto.Decrypt(meta.RecoveryKeyBlob, oldKey);
            }

            lock (_gate)
            {
                ReEncryptAll(oldKey, newKey);

                var updated = meta with
                {
                    Salt = newSalt,
                    MasterHash = ProtectHash(_crypto.ComputeVerificationHash(newKey)),
                    Iterations = _crypto.Iterations,
                    RecoveryBlob = recoveryKey is null ? null : _crypto.Encrypt(newKey, recoveryKey),
                    RecoveryKeyBlob = recoveryKey is null ? null : _crypto.Encrypt(recoveryKey, newKey),
                };

                _store.WriteMetadata(updated);
                _meta = updated;
                ReplaceKey(newKey);
                _lastActivity = _now();
                StartTimer();
            }

            newKey = null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldKey);
            if (newKey is not null)
            {
                CryptographicOperations.ZeroMemory(newKey);
            }

            if (recoveryKey is not null)
            {
                CryptographicOperations.ZeroMemory(recoveryKey);
            }
        }

        Changed?.Invoke();
        LockStateChanged?.Invoke();
    }

    /// <summary>把全部条目的密码/备注从旧密钥重加密到新密钥；批量写入在单个事务内完成。</summary>
    private void ReEncryptAll(byte[] oldKey, byte[] newKey)
    {
        var entries = _store.QueryAll();
        var reencrypted = new List<VaultEntry>(entries.Count);

        foreach (var entry in entries)
        {
            var password = ReEncryptField(entry.EncryptedPassword, oldKey, newKey);
            var note = ReEncryptField(entry.EncryptedNote, oldKey, newKey);
            if (!ReferenceEquals(password, entry.EncryptedPassword)
                || !ReferenceEquals(note, entry.EncryptedNote))
            {
                reencrypted.Add(entry with
                {
                    EncryptedPassword = password,
                    EncryptedNote = note,
                });
            }
        }

        _store.UpdateEncryptedBatch(reencrypted, _now());
    }

    private byte[]? ReEncryptField(byte[]? blob, byte[] oldKey, byte[] newKey)
    {
        if (blob is not { Length: > 0 })
        {
            return blob;
        }

        var plain = _crypto.Decrypt(blob, oldKey);
        try
        {
            return _crypto.Encrypt(plain, newKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static string NormalizeQuestion(string question)
    {
        var normalized = question?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("安全问题不能为空。", nameof(question));
        }

        if (normalized.Length > MaximumQuestionLength)
        {
            throw new ArgumentException($"安全问题最多 {MaximumQuestionLength} 个字符。", nameof(question));
        }

        return normalized;
    }

    /// <summary>安全答案不区分大小写，并折叠多余空白。</summary>
    private static string NormalizeAnswer(string answer)
    {
        var parts = (answer ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static void ValidateAnswer(string answer)
    {
        if (answer.Length < MinimumAnswerLength)
        {
            throw new ArgumentException($"安全答案至少需要 {MinimumAnswerLength} 个字符。", nameof(answer));
        }

        if (answer.Length > MaximumAnswerLength)
        {
            throw new ArgumentException($"安全答案最多 {MaximumAnswerLength} 个字符。", nameof(answer));
        }
    }

    public async Task<bool> UnlockAsync(
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(masterPassword))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        VaultMetadata meta;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            meta = _meta ?? throw new InvalidOperationException("尚未设置主密码，请先创建保险库。");
        }

        var (key, _) = await Task.Run(
            () => _crypto.DeriveMasterKey(masterPassword, meta.Salt),
            cancellationToken);

        try
        {
            var expected = UnprotectHash(meta.MasterHash);
            var actual = _crypto.ComputeVerificationHash(key);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                CryptographicOperations.ZeroMemory(key);
                return false;
            }

            lock (_gate)
            {
                ReplaceKey(key);
                _lastActivity = _now();
                StartTimer();
            }

            LockStateChanged?.Invoke();
            return true;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    public void Lock()
    {
        lock (_gate)
        {
            if (_key is null)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(_key);
            _key = null;
            StopTimer();

            var now = _now();
            _meta = _meta is null ? null : _meta with { LastLockedAt = now };

            try
            {
                _store.MarkLocked(now);
            }
            catch (Exception)
            {
                // 锁定不依赖落库结果：密钥已经在内存里清零。
            }
        }

        LockStateChanged?.Invoke();
    }

    public void NotifyActivity()
    {
        lock (_gate)
        {
            if (_key is not null)
            {
                _lastActivity = _now();
            }
        }
    }

    public Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, 120);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var meta = _meta ?? throw new InvalidOperationException("尚未设置主密码，请先创建保险库。");
            if (meta.AutoLockMinutes == minutes)
            {
                return Task.CompletedTask;
            }

            var updated = meta with { AutoLockMinutes = minutes };
            _store.WriteMetadata(updated);
            _meta = updated;
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetClearClipboardSecondsAsync(int seconds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, 300);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var meta = _meta ?? throw new InvalidOperationException("尚未设置主密码，请先创建保险库。");
            if (meta.ClearClipboardSeconds == seconds)
            {
                return Task.CompletedTask;
            }

            var updated = meta with { ClearClipboardSeconds = seconds };
            _store.WriteMetadata(updated);
            _meta = updated;
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VaultEntry>> ListAsync(
        VaultCategory category,
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 锁定后不加载条目：连密文都不读，界面直接显示锁屏。
        if (IsLocked)
        {
            return Task.FromResult<IReadOnlyList<VaultEntry>>([]);
        }

        IEnumerable<VaultEntry> query = _store.QueryAll();
        query = category switch
        {
            VaultCategory.Favorite => query.Where(entry => entry.Favorite),
            VaultCategory.All => query,
            _ => query.Where(entry => entry.Category == category),
        };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var needle = keyword.Trim();
            query = query.Where(entry =>
                Contains(entry.Title, needle)
                || Contains(entry.Username, needle)
                || Contains(entry.Url, needle));
        }

        return Task.FromResult<IReadOnlyList<VaultEntry>>(query.ToArray());
    }

    public Task AddEntryAsync(VaultEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Id != 0)
        {
            throw new ArgumentException("新增条目不应携带 Id。", nameof(entry));
        }

        var title = NormalizeTitle(entry.Title);
        var now = _now();

        lock (_gate)
        {
            var key = RequireKey();
            var stored = entry with
            {
                Id = 0,
                Title = title,
                Category = NormalizeCategory(entry.Category),
                Username = NormalizeOptional(entry.Username),
                Url = NormalizeOptional(entry.Url),
                EncryptedPassword = EncryptField(entry.EncryptedPassword, key),
                EncryptedNote = EncryptField(entry.EncryptedNote, key),
                CreatedAt = now,
                UpdatedAt = now,
                UsedAt = null,
            };

            _store.Insert(stored, now);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task UpdateEntryAsync(VaultEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.Id);

        var title = NormalizeTitle(entry.Title);
        var now = _now();

        lock (_gate)
        {
            var key = RequireKey();
            var existing = _store.Find(entry.Id)
                ?? throw new ArgumentException("条目不存在。", nameof(entry));

            // null 表示保留原密文；空数组表示清除；其余按 UTF-8 明文重新加密。
            var password = entry.EncryptedPassword is null
                ? existing.EncryptedPassword
                : EncryptField(entry.EncryptedPassword, key);
            var note = entry.EncryptedNote is null
                ? existing.EncryptedNote
                : EncryptField(entry.EncryptedNote, key);

            var stored = existing with
            {
                Category = NormalizeCategory(entry.Category),
                Title = title,
                Username = NormalizeOptional(entry.Username),
                Url = NormalizeOptional(entry.Url),
                EncryptedPassword = password,
                EncryptedNote = note,
                Favorite = entry.Favorite,
                UpdatedAt = now,
            };

            _store.Update(stored, now);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _ = RequireKey();
            _store.Delete(id);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task<string> RevealPasswordAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        cancellationToken.ThrowIfCancellationRequested();

        string password;
        lock (_gate)
        {
            var key = RequireKey();
            var entry = _store.Find(id)
                ?? throw new ArgumentException("条目不存在。", nameof(id));

            if (entry.EncryptedPassword is not { Length: > 0 } blob)
            {
                return Task.FromResult(string.Empty);
            }

            password = DecryptText(blob, key);
            _store.MarkUsed(id, _now());
        }

        return Task.FromResult(password);
    }

    public Task<string> RevealNoteAsync(long id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        cancellationToken.ThrowIfCancellationRequested();

        string note;
        lock (_gate)
        {
            var key = RequireKey();
            var entry = _store.Find(id)
                ?? throw new ArgumentException("条目不存在。", nameof(id));
            note = DecryptText(entry.EncryptedNote, key);
        }

        return Task.FromResult(note);
    }

    public Task<VaultEntryDraft?> ReadDraftAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        cancellationToken.ThrowIfCancellationRequested();

        VaultEntryDraft? draft;
        lock (_gate)
        {
            var key = RequireKey();
            var entry = _store.Find(id)
                ?? throw new ArgumentException("条目不存在。", nameof(id));

            draft = new VaultEntryDraft
            {
                Category = entry.Category,
                Title = entry.Title,
                Username = entry.Username,
                Password = DecryptText(entry.EncryptedPassword, key),
                Url = entry.Url,
                Note = DecryptText(entry.EncryptedNote, key),
                Favorite = entry.Favorite,
            };
        }

        return Task.FromResult<VaultEntryDraft?>(draft);
    }

    public Task<VaultEntry> SaveDraftAsync(
        VaultEntryDraft draft,
        long id = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        cancellationToken.ThrowIfCancellationRequested();

        var title = NormalizeTitle(draft.Title);
        var now = _now();
        VaultEntry saved;

        lock (_gate)
        {
            var key = RequireKey();
            var password = EncryptText(draft.Password, key);
            var note = EncryptText(draft.Note, key);

            if (id == 0)
            {
                var entry = new VaultEntry
                {
                    Category = NormalizeCategory(draft.Category),
                    Title = title,
                    Username = NormalizeOptional(draft.Username),
                    Url = NormalizeOptional(draft.Url),
                    EncryptedPassword = password,
                    EncryptedNote = note,
                    Favorite = draft.Favorite,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var newId = _store.Insert(entry, now);
                saved = entry with { Id = newId };
            }
            else
            {
                var existing = _store.Find(id)
                    ?? throw new ArgumentException("条目不存在。", nameof(id));

                saved = existing with
                {
                    Category = NormalizeCategory(draft.Category),
                    Title = title,
                    Username = NormalizeOptional(draft.Username),
                    Url = NormalizeOptional(draft.Url),
                    EncryptedPassword = password,
                    EncryptedNote = note,
                    Favorite = draft.Favorite,
                    UpdatedAt = now,
                };

                _store.Update(saved, now);
            }
        }

        Changed?.Invoke();
        return Task.FromResult(saved);
    }

    public Task SetFavoriteAsync(
        long id,
        bool favorite,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _ = RequireKey();
            var entry = _store.Find(id)
                ?? throw new ArgumentException("条目不存在。", nameof(id));
            _store.Update(entry with { Favorite = favorite }, _now());
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>测试与计时器共用：超过闲置阈值时锁定。</summary>
    internal void CheckAutoLock()
    {
        lock (_gate)
        {
            if (_key is null || _meta is null || _meta.AutoLockMinutes <= 0)
            {
                return;
            }

            if (_now() - _lastActivity < TimeSpan.FromMinutes(_meta.AutoLockMinutes))
            {
                return;
            }
        }

        Lock();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopTimer();
            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key);
                _key = null;
            }
        }
    }

    private byte[] RequireKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key ?? throw new VaultLockedException();
    }

    private void ReplaceKey(byte[] key)
    {
        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
        }

        _key = key;
    }

    private void StartTimer()
    {
        _autoLockTimer ??= new Timer(
            _ =>
            {
                try
                {
                    CheckAutoLock();
                }
                catch (Exception)
                {
                    // 自动锁定失败不能影响进程；下一次节拍会重试。
                }
            },
            state: null,
            AutoLockCheckInterval,
            AutoLockCheckInterval);
    }

    private void StopTimer()
    {
        _autoLockTimer?.Dispose();
        _autoLockTimer = null;
    }

    private byte[] ProtectHash(byte[] hash)
    {
        if (!_protector.IsAvailable)
        {
            return Wrap(HashSchemePlain, hash);
        }

        return Wrap(HashSchemeProtected, _protector.Protect(hash));
    }

    private byte[] UnprotectHash(byte[] stored)
    {
        if (stored.Length < 2)
        {
            throw new VaultException("保险库元数据损坏，无法校验主密码。");
        }

        if (stored[0] == HashSchemePlain)
        {
            return stored[1..];
        }

        if (stored[0] != HashSchemeProtected)
        {
            throw new VaultException("保险库元数据使用了未知的保护方案。");
        }

        if (!_protector.IsAvailable)
        {
            throw new VaultException("保险库与本机 Windows 账户绑定，当前环境无法解锁。");
        }

        try
        {
            return _protector.Unprotect(stored[1..]);
        }
        catch (Exception exception)
        {
            throw new VaultException("保险库与本机 Windows 账户绑定，请在创建它的账户下解锁。", exception);
        }
    }

    private static byte[] Wrap(byte scheme, byte[] payload)
    {
        var result = new byte[payload.Length + 1];
        result[0] = scheme;
        Buffer.BlockCopy(payload, 0, result, 1, payload.Length);
        return result;
    }

    private string DecryptText(byte[]? blob, byte[] key)
    {
        if (blob is not { Length: > 0 })
        {
            return string.Empty;
        }

        var plain = _crypto.Decrypt(blob, key);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private byte[]? EncryptText(string? text, byte[] key)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            return _crypto.Encrypt(bytes, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private byte[]? EncryptField(byte[]? plain, byte[] key)
    {
        if (plain is not { Length: > 0 })
        {
            return null;
        }

        return _crypto.Encrypt(plain, key);
    }

    private static string NormalizeTitle(string? title)
    {
        var normalized = title?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("条目名称不能为空。", nameof(title));
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static VaultCategory NormalizeCategory(VaultCategory category) =>
        category == VaultCategory.Favorite || category == VaultCategory.All
            ? VaultCategory.Login
            : category;

    private static bool Contains(string? value, string needle) =>
        value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"主密码至少需要 {MinimumPasswordLength} 位。", nameof(password));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("主密码不能全是空白字符。", nameof(password));
        }
    }
}
