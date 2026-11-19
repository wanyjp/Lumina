namespace Lumina.Services.Notifications;

/// <summary>一段可选的到点提示音；<see cref="FileName"/> 为相对程序目录的路径，静音时为 null。</summary>
public sealed record NotificationSoundOption(string Id, string Label, string? FileName);

/// <summary>
/// 到点提示音目录：两段原创合成音 + 静音。文件随 <c>Lumina.App/Sounds</c> 输出，
/// 设置页通过 <see cref="INotificationSoundPlayer"/> 试听，计时器/番茄钟到点自动播放。
/// </summary>
public static class NotificationSoundCatalog
{
    public const string Chime1 = "chime1";
    public const string Chime2 = "chime2";
    public const string Silent = "silent";

    public static IReadOnlyList<NotificationSoundOption> All { get; } =
    [
        new(Chime1, "提示音 1", "Sounds/notify-1.wav"),
        new(Chime2, "提示音 2", "Sounds/notify-2.wav"),
        new(Silent, "静音", null),
    ];

    /// <summary>未知值回落到提示音 1，保证旧配置升级后仍有声音。</summary>
    public static string Normalize(string? soundId) =>
        All.FirstOrDefault(option => string.Equals(option.Id, soundId, StringComparison.OrdinalIgnoreCase))?.Id
        ?? Chime1;

    public static string? FileNameFor(string? soundId) =>
        All.FirstOrDefault(option => string.Equals(option.Id, Normalize(soundId), StringComparison.Ordinal))
            ?.FileName;
}

/// <summary>提示音播放抽象：由 App 层用 WPF MediaPlayer 实现，服务层只负责触发。</summary>
public interface INotificationSoundPlayer
{
    /// <summary>播放指定提示音；null 或未知值回落到提示音 1，静音时不做任何事。</summary>
    void Play(string? soundId);
}
