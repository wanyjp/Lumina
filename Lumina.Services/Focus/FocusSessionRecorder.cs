using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Focus;

/// <summary>
/// 会话结束事件的订阅者：把番茄/计时器发布的 <see cref="FocusSessionEvent"/> 落库（P3-01）。
/// <para>
/// 幂等由两层保证：<see cref="FocusSessionHub"/> 在内存按会话 ID 去重，
/// 数据库对 <c>session_key</c> 建唯一索引并使用 <c>INSERT OR IGNORE</c>。
/// 写入在事件线程同步完成（单条 &lt; 5 ms），确保应用退出前会话已落盘。
/// </para>
/// </summary>
public sealed class FocusSessionRecorder : IDisposable
{
    private readonly FocusSessionHub _hub;
    private readonly IFocusTracker _tracker;
    private readonly ILogger<FocusSessionRecorder>? _logger;
    private bool _disposed;

    public FocusSessionRecorder(
        FocusSessionHub hub,
        IFocusTracker tracker,
        ILogger<FocusSessionRecorder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(tracker);

        _hub = hub;
        _tracker = tracker;
        _logger = logger;
        _hub.Published += OnPublished;
    }

    private void OnPublished(FocusSessionEvent session)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var record = new FocusSession
            {
                SessionKey = session.SessionId.ToString("N"),
                Source = session.Source,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt,
                DurationSeconds = (int)Math.Round(
                    Math.Max(0, session.Duration.TotalSeconds),
                    MidpointRounding.AwayFromZero),
                Finished = session.Completed,
                Note = null,
            };

            _tracker.RecordSessionAsync(record).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 统计失败不得影响计时主流程。
            _logger?.LogWarning(ex, "专注会话写入失败：source={Source} key={Key}", session.Source, session.SessionId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hub.Published -= OnPublished;
    }
}
