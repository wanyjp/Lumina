using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Notifications;

/// <summary>一条到点提示：应用内提醒条与系统气泡共用同一份标题/正文/跳转目标。</summary>
public sealed record SessionCompletionAlert(string Title, string Body, string Route);

/// <summary>
/// 计时结束提示：订阅专注会话的「自然完成」事件，产出应用内提醒与系统气泡。
/// <para>
/// 只处理 <see cref="FocusSessionEvent.Completed"/> 为 true 的会话，
/// 因此手动重置、强制结束与中途停止都不会误报；会话幂等键由
/// <see cref="FocusSessionHub"/> 保证，同一次计时不会提醒两次。
/// </para>
/// </summary>
public sealed class SessionCompletionNotifier : IDisposable
{
    private readonly FocusSessionHub _hub;
    private readonly IPomodoroService _pomodoro;
    private readonly INotifier _notifier;
    private readonly INotificationSoundPlayer _sound;
    private readonly ISettingsStore _settings;
    private readonly ILogger<SessionCompletionNotifier>? _logger;
    private bool _disposed;

    public SessionCompletionNotifier(
        FocusSessionHub hub,
        IPomodoroService pomodoro,
        INotifier notifier,
        INotificationSoundPlayer sound,
        ISettingsStore settings,
        ILogger<SessionCompletionNotifier>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(pomodoro);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(settings);

        _hub = hub;
        _pomodoro = pomodoro;
        _notifier = notifier;
        _sound = sound;
        _settings = settings;
        _logger = logger;

        _hub.Published += OnSessionPublished;
    }

    /// <summary>应用内提醒条订阅此事件；在后台线程触发，订阅方需自行切回 UI 线程。</summary>
    public event Action<SessionCompletionAlert>? Raised;

    private void OnSessionPublished(FocusSessionEvent session)
    {
        if (_disposed || !session.Completed)
        {
            return;
        }

        var alert = session.Source == FocusSource.Pomodoro
            ? BuildPomodoroAlert()
            : BuildTimerAlert(session);

        if (alert is null)
        {
            return;
        }

        PlayConfiguredSound();

        if (_settings.Get<bool>(PreferenceKeys.InAppReminder, PreferenceDefaults.InAppReminder))
        {
            try
            {
                Raised?.Invoke(alert);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "应用内计时提醒派发失败。");
            }
        }

        if (_settings.Get<bool>(PreferenceKeys.SystemNotification, PreferenceDefaults.SystemNotification))
        {
            _ = ShowSystemToastAsync(alert);
        }
    }

    private static SessionCompletionAlert BuildTimerAlert(FocusSessionEvent session)
    {
        var duration = FormatDuration(session.Duration);
        return new SessionCompletionAlert(
            "计时结束",
            $"设定的 {duration} 已走完，可以休息一下或开始下一件事。",
            "clock");
    }

    private SessionCompletionAlert? BuildPomodoroAlert()
    {
        var state = _pomodoro.Current;

        if (state.Completed)
        {
            return new SessionCompletionAlert(
                "番茄钟完成",
                $"{Math.Max(1, state.TotalCycles)} 轮专注全部完成，可以开始新一轮或先休息一下。",
                "clock");
        }

        if (state.Phase == PomodoroPhase.Break)
        {
            return new SessionCompletionAlert(
                $"第 {state.Cycle} 轮专注完成",
                $"休息 {FormatDuration(state.Remaining)}，下一轮会自动开始。",
                "clock");
        }

        return null;
    }

    /// <summary>播放用户选择的提示音；静音或文件缺失时静默跳过，不影响提醒本身。</summary>
    private void PlayConfiguredSound()
    {
        try
        {
            var soundId = _settings.Get<string>(
                PreferenceKeys.NotificationSound,
                PreferenceDefaults.NotificationSound);

            _sound.Play(soundId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "播放到点提示音失败。");
        }
    }

    private async Task ShowSystemToastAsync(SessionCompletionAlert alert)
    {
        try
        {
            var delivered = await _notifier
                .ShowToastAsync(alert.Title, alert.Body, alert.Route)
                .ConfigureAwait(false);

            if (!delivered)
            {
                _logger?.LogInformation("系统通知未送达，已保留应用内提醒：{Title}", alert.Title);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "系统通知发送异常：{Title}", alert.Title);
        }
    }

    internal static string FormatDuration(TimeSpan value)
    {
        var total = value < TimeSpan.Zero ? TimeSpan.Zero : value;

        if (total.TotalSeconds < 60)
        {
            return $"{(int)Math.Round(total.TotalSeconds)} 秒";
        }

        if (total.TotalMinutes < 60)
        {
            var seconds = total.Seconds;
            return seconds == 0
                ? $"{(int)total.TotalMinutes} 分钟"
                : $"{(int)total.TotalMinutes} 分 {seconds} 秒";
        }

        var minutes = total.Minutes;
        return minutes == 0
            ? $"{(int)total.TotalHours} 小时"
            : $"{(int)total.TotalHours} 小时 {minutes} 分";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hub.Published -= OnSessionPublished;
    }
}
