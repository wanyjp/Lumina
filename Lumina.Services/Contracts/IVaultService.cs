using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Contracts;

/// <summary>
/// 保险库。P3-07 实现加密、锁定与条目读写；未解锁不加载条目，
/// 解锁后的密钥只存在内存中，锁定即清零。
/// </summary>
public interface IVaultService
{
    /// <summary>是否已经设置主密码（首次运行需要先创建保险库）。</summary>
    bool IsConfigured { get; }

    bool IsLocked { get; }

    /// <summary>闲置自动锁定分钟数；0 表示仅手动锁定。</summary>
    int AutoLockMinutes { get; }

    /// <summary>复制密码后自动清除剪贴板的秒数；0 表示不自动清除（P3-09）。</summary>
    int ClearClipboardSeconds { get; }

    /// <summary>首次创建保险库：生成随机盐与校验哈希，成功后直接进入解锁状态。</summary>
    Task SetupAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// 首次创建保险库并设置找回用的安全问题；安全答案不区分大小写，
    /// 由它派生的密钥包裹主密钥，忘记主密码时可用于重置。
    /// </summary>
    Task SetupAsync(
        string masterPassword,
        string securityQuestion,
        string securityAnswer,
        CancellationToken cancellationToken = default);

    /// <summary>是否设置了可用的安全问题找回。</summary>
    bool HasRecovery { get; }

    /// <summary>找回主密码的安全问题；未设置时为 null。</summary>
    string? RecoveryQuestion { get; }

    /// <summary>
    /// 用安全答案找回主密码：答案正确时用新主密码重新加密全部条目并自动解锁。
    /// 答案错误返回 false。
    /// </summary>
    Task<bool> ResetPasswordAsync(
        string securityAnswer,
        string newMasterPassword,
        CancellationToken cancellationToken = default);

    /// <summary>校验当前主密码后修改主密码，并重新加密全部条目；当前密码错误抛 <c>VaultException</c>。</summary>
    Task ChangePasswordAsync(
        string currentPassword,
        string newMasterPassword,
        CancellationToken cancellationToken = default);

    Task<bool> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>手动锁定：清空内存密钥并停止自动锁定计时。</summary>
    void Lock();

    /// <summary>用户与应用交互时调用，重置闲置计时。</summary>
    void NotifyActivity();

    Task SetAutoLockMinutesAsync(int minutes, CancellationToken cancellationToken = default);

    /// <summary>设置复制密码后自动清除剪贴板的秒数；0 表示不自动清除，上限 300 秒。</summary>
    Task SetClearClipboardSecondsAsync(int seconds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultEntry>> ListAsync(
        VaultCategory category,
        string? keyword = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增条目。<paramref name="entry"/> 的 <c>EncryptedPassword</c>/<c>EncryptedNote</c>
    /// 传 UTF-8 明文，服务内加密后落库；返回值与列表都只含密文。
    /// </summary>
    Task AddEntryAsync(VaultEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新条目。密码/备注字段为 <c>null</c> 表示保持原值，传空数组表示清除；
    /// 其余字段以传入值为准。
    /// </summary>
    Task UpdateEntryAsync(VaultEntry entry, CancellationToken cancellationToken = default);

    Task DeleteEntryAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>解密密码并更新使用时间；锁定状态抛 <c>VaultLockedException</c>。</summary>
    Task<string> RevealPasswordAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>解密备注用于详情展示，不更新使用时间。</summary>
    Task<string> RevealNoteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>读取编辑用明文草稿（密码与备注解密），不更新使用时间。</summary>
    Task<VaultEntryDraft?> ReadDraftAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>保存编辑草稿：<paramref name="id"/> 为 0 时新增，否则更新。</summary>
    Task<VaultEntry> SaveDraftAsync(
        VaultEntryDraft draft,
        long id = 0,
        CancellationToken cancellationToken = default);

    /// <summary>只切换收藏状态，避免编辑其它字段时重复加密密码。</summary>
    Task SetFavoriteAsync(long id, bool favorite, CancellationToken cancellationToken = default);

    /// <summary>条目或锁定策略发生变化。</summary>
    event Action? Changed;

    /// <summary>解锁 / 锁定状态切换（含首次创建）。</summary>
    event Action? LockStateChanged;
}

/// <summary>
/// 加密提供者。P3-07 实现：PBKDF2-HMAC-SHA256 派生主密钥，AES-GCM 随机 IV 加密字段，
/// 主密码只存校验哈希，密钥材料不落日志。
/// </summary>
public interface ICryptoProvider
{
    /// <summary>当前派生参数使用的迭代次数；随保险库元数据保存。</summary>
    int Iterations { get; }

    (byte[] Key, byte[] Salt) DeriveMasterKey(string password, byte[]? salt = null);

    byte[] Encrypt(byte[] data, byte[] key);

    byte[] Decrypt(byte[] blob, byte[] key);

    /// <summary>由主密钥派生校验哈希（HMAC-SHA256），用于比对主密码而不保存密钥。</summary>
    byte[] ComputeVerificationHash(byte[] key);
}
