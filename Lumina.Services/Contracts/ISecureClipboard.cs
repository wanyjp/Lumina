namespace Lumina.Services.Contracts;

/// <summary>
/// P3-09 安全复制通道：密码等敏感文本写入系统剪贴板时标记为“不进入历史/不同步”，
/// 只记录内容哈希，到期后仅清理本次写入且未被覆盖的内容。
/// 客观边界：Windows 剪贴板对同会话进程可读，本通道只降低残留与采集面，不宣称绝对防护。
/// </summary>
public interface ISecureClipboard
{
    /// <summary>当前策略的清除秒数；0 表示不自动清除。</summary>
    int ClearAfterSeconds { get; }

    /// <summary>复制敏感文本；<paramref name="text"/> 为空或通道不可用时返回失败。</summary>
    SecureCopyResult CopySecret(string? text);

    /// <summary>立即清除本次写入的内容；内容已被其他复制覆盖时不清理。</summary>
    bool ClearIfOwned();

    /// <summary>剪贴板内容被自动清除（超时、锁定或读取失败放弃）。</summary>
    event Action<SecureClipboardClearReason>? Cleared;
}

/// <summary>
/// 剪贴板底层通道：写入时附加排除格式，读取只返回哈希，避免明文在服务层二次停留。
/// </summary>
public interface ISecureClipboardChannel
{
    /// <summary>写入敏感文本并请求监听器跳过本次变更。</summary>
    bool TryWriteSecret(string text);

    /// <summary>
    /// 当前剪贴板文本的 SHA-256（小写十六进制）；剪贴板可读但没有文本（如图片）返回空串，
    /// 打开剪贴板失败等不可读情况返回 <c>null</c>。
    /// </summary>
    string? TryReadTextHash();

    /// <summary>清空剪贴板。</summary>
    bool TryClear();
}

/// <summary>安全复制结果。</summary>
public readonly record struct SecureCopyResult(
    bool Success,
    int ClearAfterSeconds,
    SecureCopyFailure Failure)
{
    public static SecureCopyResult Ok(int clearAfterSeconds) => new(true, clearAfterSeconds, SecureCopyFailure.None);

    public static SecureCopyResult Fail(SecureCopyFailure failure) => new(false, 0, failure);

    /// <summary>复制成功且会按秒数自动清除。</summary>
    public bool AutoClear => Success && ClearAfterSeconds > 0;
}

public enum SecureCopyFailure
{
    None = 0,
    Empty = 1,
    ClipboardUnavailable = 2,
}

/// <summary>剪贴板被清空的原因。</summary>
public enum SecureClipboardClearReason
{
    /// <summary>到达设定秒数。</summary>
    Expired = 0,

    /// <summary>保险库锁定或退出。</summary>
    Locked = 1,

    /// <summary>剪贴板长期被占用，读取多次失败后放弃（内容可能仍残留）。</summary>
    Unavailable = 2,
}
