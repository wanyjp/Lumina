using System.Security.Cryptography;
using System.Text;
using Lumina.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Security;

/// <summary>
/// P3-09 安全复制：写入前记录内容哈希，按保险库策略定时清理；
/// 清理前重新读取剪贴板，只有仍是本次写入的内容才清空，避免误清用户后来复制的数据。
/// 锁定/退出时立即清理；明文不驻留服务层，只保存哈希与到期时间。
/// </summary>
public sealed class SecureClipboardBroker : ISecureClipboard, IDisposable
{
    /// <summary>到期检查节拍；实际清理时刻按到期时间判断，节拍只影响精度。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>剪贴板被占用时的重试间隔与次数。</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(700);
    private const int MaxRetries = 3;

    private readonly ISecureClipboardChannel _channel;
    private readonly IVaultService _vault;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<SecureClipboardBroker>? _logger;
    private readonly object _gate = new();

    private Timer? _timer;
    private string? _pendingHash;
    private DateTimeOffset _expiresAt = DateTimeOffset.MaxValue;
    private int _retries;
    private bool _disposed;

    public SecureClipboardBroker(
        ISecureClipboardChannel channel,
        IVaultService vault,
        Func<DateTimeOffset>? now = null,
        ILogger<SecureClipboardBroker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(vault);

        _channel = channel;
        _vault = vault;
        _now = now ?? (() => DateTimeOffset.Now);
        _logger = logger;
        _vault.LockStateChanged += OnLockStateChanged;
    }

    public event Action<SecureClipboardClearReason>? Cleared;

    /// <summary>读取配置失败时的兜底清除时长：宁可自动清除，也不要让密码永驻剪贴板。</summary>
    private const int FallbackClearSeconds = 20;

    public int ClearAfterSeconds
    {
        get
        {
            try
            {
                return _vault.ClearClipboardSeconds;
            }
            catch (Exception)
            {
                return FallbackClearSeconds;
            }
        }
    }

    public SecureCopyResult CopySecret(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return SecureCopyResult.Fail(SecureCopyFailure.Empty);
        }

        var hash = ComputeContentHash(text);
        if (!_channel.TryWriteSecret(text))
        {
            _logger?.LogWarning("安全复制写入剪贴板失败。");
            return SecureCopyResult.Fail(SecureCopyFailure.ClipboardUnavailable);
        }

        var seconds = ClearAfterSeconds;
        lock (_gate)
        {
            _pendingHash = hash;
            _retries = 0;
            _expiresAt = seconds > 0 ? _now().AddSeconds(seconds) : DateTimeOffset.MaxValue;
            if (seconds > 0)
            {
                EnsureTimer();
            }
            else
            {
                StopTimer();
            }
        }

        return SecureCopyResult.Ok(seconds);
    }

    public bool ClearIfOwned()
    {
        string? hash;
        lock (_gate)
        {
            hash = _pendingHash;
            _pendingHash = null;
            _expiresAt = DateTimeOffset.MaxValue;
            StopTimer();
        }

        if (hash is null)
        {
            return false;
        }

        var current = _channel.TryReadTextHash();
        if (!string.Equals(current, hash, StringComparison.Ordinal))
        {
            // 已被用户的新复制覆盖（或换成图片等非文本）：不清空，避免误删。
            return false;
        }

        return _channel.TryClear();
    }

    /// <summary>到期检查；由内部节拍调用，单测可直接推进假时钟后调用。</summary>
    internal void CheckExpiry()
    {
        string? hash;
        lock (_gate)
        {
            if (_disposed || _pendingHash is null || _now() < _expiresAt)
            {
                return;
            }

            hash = _pendingHash;
        }

        var current = _channel.TryReadTextHash();
        if (current is null)
        {
            ScheduleRetry(hash);
            return;
        }

        if (current.Length == 0 || !string.Equals(current, hash, StringComparison.Ordinal))
        {
            // 用户已复制别的内容（含图片等非文本）：静默放弃，不误清。
            Finish();
            return;
        }

        if (_channel.TryClear())
        {
            Finish();
            _logger?.LogInformation("安全复制内容已按策略清除（{Seconds} 秒）。", ClearAfterSeconds);
            Cleared?.Invoke(SecureClipboardClearReason.Expired);
            return;
        }

        ScheduleRetry(hash);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vault.LockStateChanged -= OnLockStateChanged;
        lock (_gate)
        {
            _pendingHash = null;
            _expiresAt = DateTimeOffset.MaxValue;
            StopTimer();
        }
    }

    /// <summary>内容指纹：SHA-256(UTF-8) 小写十六进制。</summary>
    public static string ComputeContentHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private void OnLockStateChanged()
    {
        if (!_vault.IsLocked)
        {
            return;
        }

        if (ClearIfOwned())
        {
            _logger?.LogInformation("保险库锁定，已清除剪贴板中的密码。");
            Cleared?.Invoke(SecureClipboardClearReason.Locked);
        }
    }

    private void ScheduleRetry(string hash)
    {
        var giveUp = false;
        lock (_gate)
        {
            // 期间可能已经被新的复制替换，换过内容就不再处理旧到期。
            if (!string.Equals(_pendingHash, hash, StringComparison.Ordinal))
            {
                return;
            }

            _retries++;
            if (_retries > MaxRetries)
            {
                _pendingHash = null;
                _expiresAt = DateTimeOffset.MaxValue;
                StopTimer();
                giveUp = true;
            }
            else
            {
                _expiresAt = _now().Add(RetryDelay);
            }
        }

        if (giveUp)
        {
            _logger?.LogWarning("剪贴板持续被占用，已停止自动清除，内容可能仍然残留。");
            Cleared?.Invoke(SecureClipboardClearReason.Unavailable);
        }
    }

    private void Finish()
    {
        lock (_gate)
        {
            _pendingHash = null;
            _expiresAt = DateTimeOffset.MaxValue;
            StopTimer();
        }
    }

    private void EnsureTimer()
    {
        _timer ??= new Timer(_ => SafeCheckExpiry(), null, CheckInterval, CheckInterval);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeCheckExpiry()
    {
        try
        {
            CheckExpiry();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "安全复制到期检查失败，已跳过本次。");
        }
    }
}
