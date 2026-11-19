using System.IO;
using System.Windows;
using System.Windows.Media;
using Lumina.Services.Notifications;
using Microsoft.Extensions.Logging;

namespace Lumina.UI.Wpf.Audio;

/// <summary>
/// 到点提示音播放器：用 WPF <see cref="MediaPlayer"/> 播放随程序发布的 WAV 音效。
/// <para>
/// 计时结束事件来自后台线程，而 <see cref="MediaPlayer"/> 是 DispatcherObject，
/// 必须切回 UI 线程创建与播放；文件缺失或解码失败只记日志，不影响提醒本身。
/// </para>
/// </summary>
public sealed class NotificationSoundPlayer : INotificationSoundPlayer, IDisposable
{
    private readonly ILogger<NotificationSoundPlayer>? _logger;
    private readonly object _gate = new();

    private MediaPlayer? _player;
    private bool _disposed;

    public NotificationSoundPlayer(ILogger<NotificationSoundPlayer>? logger = null)
    {
        _logger = logger;
    }

    public void Play(string? soundId)
    {
        if (_disposed)
        {
            return;
        }

        var fileName = NotificationSoundCatalog.FileNameFor(soundId);
        if (fileName is null)
        {
            // 静音：设置页明确选择了“不播放”。
            return;
        }

        var path = Path.Combine(
            AppContext.BaseDirectory,
            fileName.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            _logger?.LogWarning("提示音文件不存在，已跳过播放：{Path}", path);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            PlayCore(path);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => PlayCore(path)));
        }
    }

    private void PlayCore(string path)
    {
        try
        {
            var player = EnsurePlayer();
            player.Stop();
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "播放提示音失败：{Path}", path);
        }
    }

    private MediaPlayer EnsurePlayer()
    {
        if (_player is not null)
        {
            return _player;
        }

        lock (_gate)
        {
            if (_player is not null)
            {
                return _player;
            }

            var player = new MediaPlayer { Volume = 0.8 };
            player.MediaFailed += (_, args) =>
                _logger?.LogWarning(args.ErrorException, "提示音解码或播放失败。");

            _player = player;
            return player;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player?.Close();
        _player = null;
    }
}
