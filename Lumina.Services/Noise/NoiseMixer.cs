using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Noise;

/// <summary>
/// P1-04 混音器：负责声源状态、独立音量、总音量、组合保存与线性淡出；
/// 声源来自 <see cref="NoiseLibrary"/> 的 8 个父类别，实际出声由
/// <see cref="INoiseAudioOutput"/> 的系统适配器实现。
/// 主音量复用 <c>audioVol</c> 偏好键，设备与通道状态写入 <c>noise.mix.v1</c>。
/// </summary>
public sealed class NoiseMixer : INoiseMixer, IDisposable
{
    private const string MixKey = "noise.mix.v1";
    private const string ScenesKey = "noise.scenes.v1";
    private static readonly TimeSpan FadeStep = TimeSpan.FromMilliseconds(40);

    private readonly INoiseAudioOutput _output;
    private readonly ISettingsStore _settings;
    private readonly NoiseLibrary _library;
    private readonly Dictionary<string, SoundPreset> _presets;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private readonly Dictionary<string, double> _volumes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _playing = new(StringComparer.Ordinal);
    private readonly List<NoiseScene> _scenes = [];

    private IReadOnlyList<AudioOutputDevice> _devices = [];
    private string? _outputDeviceId;
    private double _master;
    private string? _statusMessage;
    private int _fadeVersion;
    private bool _disposed;

    public NoiseMixer(
        INoiseAudioOutput output,
        ISettingsStore settings,
        NoiseLibrary library,
        Func<DateTimeOffset>? now = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        _output = output;
        _settings = settings;
        _library = library;
        _now = now ?? (() => DateTimeOffset.Now);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));

        var sounds = library.Sounds;
        _presets = sounds.ToDictionary(preset => preset.Id, StringComparer.Ordinal);

        foreach (var preset in sounds)
        {
            _volumes[preset.Id] = preset.Volume;
        }

        _master = Math.Clamp(settings.Get<int>(PreferenceKeys.AudioVolume) / 100.0, 0, 1);

        var saved = settings.Get<NoiseMixSettings>(MixKey);
        if (saved is not null)
        {
            foreach (var channel in saved.Channels)
            {
                if (!_presets.ContainsKey(channel.SoundId))
                {
                    continue;
                }

                _volumes[channel.SoundId] = Math.Clamp(channel.Volume, 0, 1);
                if (channel.IsPlaying)
                {
                    _playing.Add(channel.SoundId);
                }
            }

            _outputDeviceId = saved.OutputDeviceId;
        }

        var restoredScenes = settings.Get<List<NoiseScene>>(ScenesKey);
        if (restoredScenes is not null)
        {
            _scenes.AddRange(restoredScenes.Where(scene =>
                !string.IsNullOrWhiteSpace(scene.Id) && scene.Sounds.Count > 0));
        }

        RefreshDevices();

        if (_outputDeviceId is not null && _devices.All(device => device.Id != _outputDeviceId))
        {
            _outputDeviceId = null;
            _statusMessage = "上次使用的输出设备已移除，已回退系统默认设备。";
        }

        if (!_output.TrySelectDevice(_outputDeviceId, out var error))
        {
            _outputDeviceId = null;
            _output.TrySelectDevice(null, out _);
            _statusMessage = error ?? "输出设备不可用，已回退系统默认设备。";
        }

        if (!_output.IsAvailable && !string.IsNullOrWhiteSpace(_output.StatusMessage))
        {
            _statusMessage = _output.StatusMessage;
        }

        if (library.Root is null)
        {
            _statusMessage = "未找到白噪音资源包：请将 app_audio 放入 %APPDATA%\\Lumina\\audio。";
            _playing.Clear();
        }
        else if (_playing.Count > 0 && _output.IsAvailable)
        {
            RestorePlayingChannels();
        }
        else
        {
            _playing.Clear();
        }
    }

    public event Action<NoiseMixSnapshot>? Changed;

    public NoiseMixSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return Snapshot();
            }
        }
    }

    public string? LibraryRoot => _library.Root;

    public IReadOnlyList<SoundPreset> Presets() => _library.Sounds;

    public IReadOnlyList<NoiseCategory> Categories() => _library.Categories;

    public IReadOnlyList<AudioOutputDevice> OutputDevices()
    {
        lock (_gate)
        {
            return _devices;
        }
    }

    public IReadOnlyList<NoiseScene> Scenes()
    {
        lock (_gate)
        {
            return _scenes.ToArray();
        }
    }

    public void Play(string soundId, double volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var preset = _presets.GetValueOrDefault(soundId);
        if (preset is null)
        {
            return;
        }

        var path = _library.AbsolutePath(preset);
        if (path is null)
        {
            lock (_gate)
            {
                _statusMessage = $"音频文件缺失：{preset.DisplayName}";
            }

            RaiseChanged();
            return;
        }

        var available = false;

        lock (_gate)
        {
            if (!_output.IsAvailable)
            {
                _statusMessage = _output.StatusMessage ?? "音频输出不可用，无法播放。";
            }
            else
            {
                _volumes[soundId] = Math.Clamp(volume <= 0 ? preset.Volume : volume, 0, 1);
                _playing.Add(soundId);
                _fadeVersion++;
                _output.SetChannelVolume(soundId, _volumes[soundId]);
                _output.StartChannel(soundId, path);
                _output.SetMasterGain(_master);
                _statusMessage = null;
                available = true;
            }
        }

        if (!available)
        {
            RaiseChanged();
            return;
        }

        Persist();
        RaiseChanged();
    }

    public void Stop(string soundId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool changed;
        lock (_gate)
        {
            changed = _playing.Remove(soundId);
            if (changed)
            {
                _output.StopChannel(soundId);
            }
        }

        if (!changed)
        {
            return;
        }

        Persist();
        RaiseChanged();
    }

    /// <summary>调整单路音量；拖动滑块的连续写入不触发整体刷新，避免控件跳动。</summary>
    public void SetVolume(string soundId, double volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_presets.ContainsKey(soundId))
        {
            return;
        }

        lock (_gate)
        {
            _volumes[soundId] = Math.Clamp(volume, 0, 1);
            if (_playing.Contains(soundId))
            {
                _output.SetChannelVolume(soundId, _volumes[soundId]);
            }
        }

        Persist();
    }

    public void SetMasterVolume(double volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _master = Math.Clamp(volume, 0, 1);
            _output.SetMasterGain(_master);
        }

        _settings.Set(PreferenceKeys.AudioVolume, (int)Math.Round(_master * 100), module: "noise");
        Persist();
        RaiseChanged();
    }

    public bool SetOutputDevice(string? deviceId, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        error = null;

        if (deviceId is not null)
        {
            lock (_gate)
            {
                if (_devices.All(device => device.Id != deviceId))
                {
                    error = "所选输出设备不存在。";
                    return false;
                }
            }
        }

        lock (_gate)
        {
            // 切换前先把总增益归零，避免旧设备断开瞬间产生爆音。
            _output.SetMasterGain(0);

            if (!_output.TrySelectDevice(deviceId, out error))
            {
                _output.SetMasterGain(_master);
                return false;
            }

            _output.SetMasterGain(_master);
            _outputDeviceId = deviceId;
            RefreshDevices();

            if (deviceId is not null && _devices.All(device => device.Id != deviceId))
            {
                // 设备在切换过程中被移除时回退默认。
                _outputDeviceId = null;
                _output.TrySelectDevice(null, out _);
                _statusMessage = "输出设备已移除，已回退系统默认设备。";
            }
            else
            {
                _statusMessage = null;
            }
        }

        Persist();
        RaiseChanged();
        return true;
    }

    public NoiseScene? SaveScene(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > AppLimits.NoiseSceneNameMaxLength)
        {
            trimmed = trimmed[..AppLimits.NoiseSceneNameMaxLength];
        }

        NoiseScene? scene;
        lock (_gate)
        {
            var sounds = _library.Sounds
                .Where(preset => _playing.Contains(preset.Id))
                .Select(preset => preset with { Volume = _volumes[preset.Id] })
                .ToArray();

            if (sounds.Length == 0)
            {
                return null;
            }

            if (_scenes.Count >= AppLimits.NoiseSceneMaxCount)
            {
                _scenes.RemoveAt(0);
            }

            scene = new NoiseScene(Guid.NewGuid().ToString("N"), trimmed, sounds, _now());
            _scenes.Add(scene);
        }

        Persist();
        RaiseChanged();
        return scene;
    }

    public bool ApplyScene(string sceneId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NoiseScene? scene;
        var available = false;

        lock (_gate)
        {
            scene = _scenes.FirstOrDefault(item => item.Id == sceneId);
            if (scene is null)
            {
                return false;
            }

            if (!_output.IsAvailable)
            {
                _statusMessage = _output.StatusMessage ?? "音频输出不可用，无法应用组合。";
            }
            else
            {
                foreach (var soundId in _playing.ToArray())
                {
                    _output.StopChannel(soundId);
                }

                _playing.Clear();
                _fadeVersion++;
                var missing = 0;

                foreach (var sound in scene.Sounds)
                {
                    var preset = _presets.GetValueOrDefault(sound.Id);
                    var path = preset is null ? null : _library.AbsolutePath(preset);

                    if (preset is null || path is null)
                    {
                        missing++;
                        continue;
                    }

                    _volumes[sound.Id] = Math.Clamp(sound.Volume, 0, 1);
                    _playing.Add(sound.Id);
                    _output.SetChannelVolume(sound.Id, _volumes[sound.Id]);
                    _output.StartChannel(sound.Id, path);
                }

                _output.SetMasterGain(_master);
                _statusMessage = missing > 0 ? $"有 {missing} 个组合音频文件缺失。" : null;
                available = true;
            }
        }

        if (!available)
        {
            RaiseChanged();
            return false;
        }

        Persist();
        RaiseChanged();
        return true;
    }

    public bool DeleteScene(string sceneId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool removed;
        lock (_gate)
        {
            removed = _scenes.RemoveAll(scene => scene.Id == sceneId) > 0;
        }

        if (!removed)
        {
            return false;
        }

        Persist();
        RaiseChanged();
        return true;
    }

    public async Task FadeOutAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        int version;
        double master;
        lock (_gate)
        {
            if (_playing.Count == 0)
            {
                return;
            }

            version = ++_fadeVersion;
            master = _master;
        }

        var startedAt = _now();
        var totalMilliseconds = duration.TotalMilliseconds;

        try
        {
            while (true)
            {
                var progress = totalMilliseconds <= 0
                    ? 1
                    : Math.Clamp((_now() - startedAt).TotalMilliseconds / totalMilliseconds, 0, 1);

                _output.SetMasterGain(master * (1 - progress));
                if (progress >= 1)
                {
                    break;
                }

                await _delay(FadeStep, cancellationToken).ConfigureAwait(true);

                lock (_gate)
                {
                    if (_fadeVersion != version)
                    {
                        // 淡出期间用户重新播放：中止淡出并恢复增益。
                        _output.SetMasterGain(_master);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _output.SetMasterGain(master);
            return;
        }

        lock (_gate)
        {
            if (_fadeVersion != version)
            {
                _output.SetMasterGain(_master);
                return;
            }

            foreach (var soundId in _playing.ToArray())
            {
                _output.StopChannel(soundId);
            }

            _playing.Clear();
            _output.SetMasterGain(_master);
        }

        Persist();
        RaiseChanged();
    }

    private void RestorePlayingChannels()
    {
        var missing = 0;

        foreach (var soundId in _playing.ToArray())
        {
            var preset = _presets.GetValueOrDefault(soundId);
            var path = preset is null ? null : _library.AbsolutePath(preset);

            if (preset is null || path is null)
            {
                _playing.Remove(soundId);
                missing++;
                continue;
            }

            _output.SetChannelVolume(soundId, _volumes[soundId]);
            _output.StartChannel(soundId, path);
        }

        if (missing > 0)
        {
            _statusMessage = $"有 {missing} 个上次播放的音频文件缺失。";
        }

        _output.SetMasterGain(_master);
    }

    private void RefreshDevices()
    {
        try
        {
            _devices = _output.EnumerateDevices();
        }
        catch (Exception ex)
        {
            _devices = [];
            _statusMessage = $"无法枚举音频输出设备：{ex.Message}";
        }
    }

    private NoiseMixSnapshot Snapshot()
    {
        var channels = _library.Sounds
            .Select(preset => new NoiseChannelState(
                preset.Id,
                preset.DisplayName,
                _volumes[preset.Id],
                _playing.Contains(preset.Id)))
            .ToArray();

        var deviceName = _devices.FirstOrDefault(device => device.Id == _outputDeviceId)?.DisplayName
            ?? _devices.FirstOrDefault(device => device.IsDefault)?.DisplayName
            ?? (IsOutputAvailable ? "系统默认输出" : null);

        return new NoiseMixSnapshot(
            channels,
            _master,
            _outputDeviceId,
            deviceName,
            IsOutputAvailable,
            _statusMessage ?? _output.StatusMessage,
            _library.Root);
    }

    private bool IsOutputAvailable => _output.IsAvailable;

    private void Persist()
    {
        NoiseMixSettings settings;
        lock (_gate)
        {
            settings = new NoiseMixSettings(
                _outputDeviceId,
                _library.Sounds
                    .Select(preset => new NoiseChannelSetting(
                        preset.Id,
                        _volumes[preset.Id],
                        _playing.Contains(preset.Id)))
                    .ToArray());
        }

        _settings.Set(MixKey, settings, module: "noise");
        _settings.Set(ScenesKey, Scenes(), module: "noise");
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(Current);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _output.StopAll();
        }
        catch (Exception)
        {
            // 释放路径不向界面抛出。
        }

        _output.Dispose();
    }
}
