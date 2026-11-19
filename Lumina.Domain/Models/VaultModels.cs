using Lumina.Domain.Enums;

namespace Lumina.Domain.Models;

/// <summary>
/// 保险库元数据：每次安装随机盐、主密码校验哈希、KDF 迭代次数与锁定策略。
/// 主密码本身不落库，校验哈希由调用方做附加保护（DPAPI）后再写入。
/// </summary>
public sealed record VaultMetadata
{
    /// <summary>PBKDF2 随机盐（16 字节），首次设置主密码时生成。</summary>
    public required byte[] Salt { get; init; }

    /// <summary>主密码校验哈希；首字节为保护方案标记（0 明文、1 DPAPI）。</summary>
    public required byte[] MasterHash { get; init; }

    /// <summary>派生密钥使用的 PBKDF2 迭代次数，随库保存，升级默认值后仍能解锁旧库。</summary>
    public int Iterations { get; init; } = 600_000;

    /// <summary>闲置自动锁定分钟数；0 表示仅手动锁定。</summary>
    public int AutoLockMinutes { get; init; } = 5;

    /// <summary>复制密码后清除剪贴板的秒数（P3-09 使用）。</summary>
    public int ClearClipboardSeconds { get; init; } = 20;

    public DateTimeOffset? LastLockedAt { get; init; }

    /// <summary>找回主密码的安全问题；为空表示该保险库未启用找回。</summary>
    public string? RecoveryQuestion { get; init; }

    /// <summary>安全答案派生找回密钥时使用的随机盐（16 字节）。</summary>
    public byte[]? RecoveryAnswerSalt { get; init; }

    /// <summary>由安全答案派生的密钥包裹的主密钥，用于忘记主密码时恢复。</summary>
    public byte[]? RecoveryBlob { get; init; }

    /// <summary>由主密钥包裹的找回密钥，用于修改主密码时无需再次回答安全问题。</summary>
    public byte[]? RecoveryKeyBlob { get; init; }
}

/// <summary>
/// 便签/条目编辑用的明文草稿：仅存在于内存与编辑窗，服务负责在保存时加密。
/// </summary>
public sealed record VaultEntryDraft
{
    public VaultCategory Category { get; init; } = VaultCategory.Login;

    public required string Title { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Url { get; init; }

    public string? Note { get; init; }

    public bool Favorite { get; init; }
}
