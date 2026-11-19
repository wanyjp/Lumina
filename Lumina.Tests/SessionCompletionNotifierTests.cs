using Lumina.Domain.Enums;
using Lumina.Services.Clock;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;
using Lumina.Services.Notifications;

namespace Lumina.Tests;

/// <summary>
/// 到点提醒链路：番茄钟全部轮次结束时，必须同时走到提示音、应用内提醒与系统通知，
/// 避免再次出现“时间到了但什么都没有”的静默完成。
/// </summary>
public class SessionCompletionNotifierTests
{
    [Fact]
    public void Completed_pomodoro_plays_configured_sound_and_raises_alerts()
    {
        var clock = new FakeClock();
        var hub = new FocusSessionHub();
        using var pomodoro = new PomodoroService(hub, () => clock.Now);

        var notifier = new RecordingNotifier();
        var sound = new RecordingSoundPlayer();
        var settings = new MemorySettings();
        settings.Set(PreferenceKeys.NotificationSound, NotificationSoundCatalog.Chime2);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, true);

        using var completion = new SessionCompletionNotifier(hub, pomodoro, notifier, sound, settings);
        SessionCompletionAlert? alert = null;
        completion.Raised += value => alert = value;

        pomodoro.Start(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 1);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(
            SpinWait.SpinUntil(() => pomodoro.Current.Completed, TimeSpan.FromSeconds(8)),
            "番茄钟未在预期时间内完成");

        Assert.True(
            SpinWait.SpinUntil(() => alert is not null, TimeSpan.FromSeconds(3)),
            "全部轮次完成后没有产生应用内提醒");

        Assert.Equal(NotificationSoundCatalog.Chime2, sound.LastSoundId);
        Assert.Equal("番茄钟完成", alert!.Title);
        Assert.Contains("1 轮", alert.Body);
        Assert.Equal("番茄钟完成", notifier.LastTitle);
    }

    [Fact]
    public void Silent_choice_is_forwarded_to_player_and_still_alerts()
    {
        var clock = new FakeClock();
        var hub = new FocusSessionHub();
        using var pomodoro = new PomodoroService(hub, () => clock.Now);

        var sound = new RecordingSoundPlayer();
        var settings = new MemorySettings();
        settings.Set(PreferenceKeys.NotificationSound, NotificationSoundCatalog.Silent);
        settings.Set(PreferenceKeys.InAppReminder, true);
        settings.Set(PreferenceKeys.SystemNotification, false);

        using var completion = new SessionCompletionNotifier(
            hub,
            pomodoro,
            new RecordingNotifier(),
            sound,
            settings);

        SessionCompletionAlert? alert = null;
        completion.Raised += value => alert = value;

        pomodoro.Start(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(SpinWait.SpinUntil(() => alert is not null, TimeSpan.FromSeconds(8)));
        // 播放器收到 silent 后会自行静默；提醒条与文案仍照常产出。
        Assert.Equal(NotificationSoundCatalog.Silent, sound.LastSoundId);
    }

    private sealed class FakeClock
    {
        private DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(8));

        public DateTimeOffset Now => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class RecordingNotifier : INotifier
    {
        public string? LastTitle { get; private set; }

        public Task<bool> ShowToastAsync(
            string title,
            string body,
            string? route = null,
            CancellationToken cancellationToken = default)
        {
            LastTitle = title;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingSoundPlayer : INotificationSoundPlayer
    {
        public string? LastSoundId { get; private set; }

        public void Play(string? soundId) => LastSoundId = soundId;
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public T? Get<T>(string key, T? fallback = default) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : fallback;

        public void Set<T>(string key, T value, string? module = null) => _values[key] = value;

        public IObservable<SettingChanged> Changed { get; } = new NeverObservable();

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private sealed class NeverObservable : IObservable<SettingChanged>
        {
            public IDisposable Subscribe(IObserver<SettingChanged> observer) => new NoopDisposable();
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
