using Lumina.Services.Contracts;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lumina.SystemAdapters.Audio;

/// <summary>
/// P1-04 WASAPI 共享模式输出：一条输出流承载全部文件声源的混音，支持设备枚举与切换。
/// 支持 WAV / MP3 / OGG Vorbis，统一转换为 44.1 kHz 单声道并循环播放。
/// 设备缺失、文件无法解码或流初始化失败时置为不可用并给出提示，不向界面抛异常。
/// </summary>
public sealed class WasapiNoiseOutput : INoiseAudioOutput
{
    private const int OutputSampleRate = 44100;

    private readonly object _gate = new();
    private readonly MixingSampleProvider _mixer;
    private readonly GainSampleProvider _gain;
    private readonly Dictionary<string, ActiveChannel> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _volumes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiOut? _output;
    private bool _available;
    private string? _statusMessage;
    private bool _disposed;

    public WasapiNoiseOutput()
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, 1))
        {
            ReadFully = true,
        };
        _gain = new GainSampleProvider(_mixer);

        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _available = true;
        }
        catch (Exception ex)
        {
            _available = false;
            _statusMessage = $"未找到可用的音频输出设备：{ex.Message}";
        }
    }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    public string? StatusMessage
    {
        get
        {
            lock (_gate)
            {
                return _statusMessage;
            }
        }
    }

    public IReadOnlyList<AudioOutputDevice> EnumerateDevices()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return [];
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                string? defaultId = null;

                try
                {
                    using var fallback = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultId = fallback.ID;
                }
                catch (Exception)
                {
                    // 没有默认设备时仍返回已启用设备列表。
                }

                var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                var devices = new List<AudioOutputDevice>(collection.Count);

                for (var i = 0; i < collection.Count; i++)
                {
                    var device = collection[i];
                    devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName, device.ID == defaultId));
                    device.Dispose();
                }

                return devices
                    .OrderByDescending(device => device.IsDefault)
                    .ThenBy(device => device.DisplayName, StringComparer.CurrentCulture)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _statusMessage = $"无法枚举音频输出设备：{ex.Message}";
                return [];
            }
        }
    }

    public bool TrySelectDevice(string? deviceId, out string? error)
    {
        lock (_gate)
        {
            error = null;

            if (_disposed)
            {
                error = "音频输出已释放。";
                return false;
            }

            try
            {
                CloseOutput();
                ReleaseDevice();
                _enumerator ??= new MMDeviceEnumerator();
                _device = deviceId is null
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : _enumerator.GetDevice(deviceId);

                _available = true;
                _statusMessage = null;

                if (_active.Count > 0)
                {
                    StartOutput();
                }

                return _available;
            }
            catch (Exception ex)
            {
                error = $"切换输出设备失败：{ex.Message}";
                _statusMessage = error;
                _available = false;
                TryRestoreDefault();
                return false;
            }
        }
    }

    public void StartChannel(string soundId, string absolutePath)
    {
        lock (_gate)
        {
            if (_disposed || !_available)
            {
                return;
            }

            _active.Add(soundId);

            if (!_channels.TryGetValue(soundId, out var channel))
            {
                try
                {
                    channel = CreateChannel(soundId, absolutePath);
                    _channels[soundId] = channel;
                }
                catch (Exception ex)
                {
                    _statusMessage = $"无法播放「{Path.GetFileName(absolutePath)}」：{ex.Message}";
                    _active.Remove(soundId);
                    return;
                }
            }

            if (!_mixer.MixerInputs.Contains(channel.Provider))
            {
                _mixer.AddMixerInput(channel.Provider);
            }

            StartOutput();
        }
    }

    public void StopChannel(string soundId)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _active.Remove(soundId);

            if (_channels.Remove(soundId, out var channel))
            {
                if (_mixer.MixerInputs.Contains(channel.Provider))
                {
                    _mixer.RemoveMixerInput(channel.Provider);
                }

                channel.Stream.Dispose();
            }

            if (_active.Count == 0)
            {
                StopOutput();
            }
        }
    }

    public void SetChannelVolume(string soundId, double volume)
    {
        lock (_gate)
        {
            var normalized = (float)Math.Clamp(volume, 0, 1);
            _volumes[soundId] = normalized;

            if (_channels.TryGetValue(soundId, out var channel))
            {
                channel.Provider.Volume = normalized;
            }
        }
    }

    public void SetMasterGain(double gain)
    {
        _gain.Gain = (float)Math.Clamp(gain, 0, 1);
    }

    public void StopAll()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopAllCore();
        }
    }

    private ActiveChannel CreateChannel(string soundId, string path)
    {
        // OGG 使用 NVorbis 解码；WAV/MP3 使用 NAudio 内置读取器。
        WaveStream stream = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);

        try
        {
            ISampleProvider sample = new LoopingSampleProvider(stream);

            if (sample.WaveFormat.Channels > 1)
            {
                sample = new StereoToMonoSampleProvider(sample)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f,
                };
            }

            if (sample.WaveFormat.SampleRate != OutputSampleRate)
            {
                sample = new WdlResamplingSampleProvider(sample, OutputSampleRate);
            }

            var volume = new VolumeSampleProvider(sample)
            {
                Volume = _volumes.GetValueOrDefault(soundId, 0.5f),
            };

            return new ActiveChannel(volume, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void StopAllCore()
    {
        _mixer.RemoveAllMixerInputs();

        foreach (var channel in _channels.Values)
        {
            channel.Stream.Dispose();
        }

        _channels.Clear();
        _active.Clear();
        StopOutput();
    }

    private void StartOutput()
    {
        if (!_available)
        {
            return;
        }

        try
        {
            if (_device is null)
            {
                _enumerator ??= new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            if (_output is null)
            {
                var output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: 120);
                output.Init(new SampleToWaveProvider(_gain));
                _output = output;
            }

            if (_output.PlaybackState != PlaybackState.Playing)
            {
                _output.Play();
            }
        }
        catch (Exception ex)
        {
            _available = false;
            _statusMessage = $"音频输出初始化失败：{ex.Message}";
            CloseOutput();
        }
    }

    private void StopOutput()
    {
        try
        {
            _output?.Stop();
        }
        catch (Exception)
        {
            // 停止路径不向界面抛出。
        }
    }

    private void CloseOutput()
    {
        var output = _output;
        _output = null;

        if (output is null)
        {
            return;
        }

        try
        {
            output.Stop();
        }
        catch (Exception)
        {
            // 忽略停止失败，继续释放。
        }

        output.Dispose();
    }

    private void ReleaseDevice()
    {
        var device = _device;
        _device = null;
        device?.Dispose();
    }

    private void TryRestoreDefault()
    {
        try
        {
            CloseOutput();
            ReleaseDevice();
            _enumerator ??= new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _available = true;

            if (_active.Count > 0)
            {
                StartOutput();
            }
        }
        catch (Exception)
        {
            _available = false;
            _statusMessage ??= "系统默认音频输出不可用。";
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopAllCore();
            CloseOutput();
            ReleaseDevice();
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    private sealed class ActiveChannel
    {
        public ActiveChannel(VolumeSampleProvider provider, WaveStream stream)
        {
            Provider = provider;
            Stream = stream;
        }

        public VolumeSampleProvider Provider { get; }

        public WaveStream Stream { get; }
    }
}

/// <summary>采样流循环器：读到文件末尾后回到开头，保证环境声持续播放。</summary>
public sealed class LoopingSampleProvider : ISampleProvider
{
    private readonly WaveStream _stream;
    private readonly ISampleProvider _source;

    public LoopingSampleProvider(WaveStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
        _source = stream.ToSampleProvider();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var total = 0;

        while (total < count)
        {
            var read = _source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (_stream.Position <= 0)
                {
                    break;
                }

                _stream.Position = 0;
                continue;
            }

            total += read;
        }

        return total;
    }
}

/// <summary>总增益采样器：淡出时按 0–1 线性写入，避免重建输出流。</summary>
internal sealed class GainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile float _gain = 1f;

    public GainSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Gain
    {
        get => _gain;
        set => _gain = Math.Clamp(value, 0f, 1f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var gain = _gain;

        for (var i = 0; i < read; i++)
        {
            buffer[offset + i] *= gain;
        }

        return read;
    }
}
