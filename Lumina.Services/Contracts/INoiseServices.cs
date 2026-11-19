namespace Lumina.Services.Contracts;

/// <summary>音频输出设备；<c>Id</c> 为空表示系统默认设备。</summary>
public sealed record AudioOutputDevice(string Id, string DisplayName, bool IsDefault);

/// <summary>单个声源的播放状态；音量统一为 0–1。</summary>
public sealed record NoiseChannelState(string SoundId, string DisplayName, double Volume, bool IsPlaying);

/// <summary>父类别；与音频资源目录中的父文件夹一一对应。</summary>
public sealed record NoiseCategory(string Id, string DisplayName, IReadOnlyList<SoundPreset> Sounds);

/// <summary>白噪音混音快照；界面只读该对象，不直接访问底层音频流。</summary>
public sealed record NoiseMixSnapshot(
    IReadOnlyList<NoiseChannelState> Channels,
    double MasterVolume,
    string? OutputDeviceId,
    string? OutputDeviceName,
    bool IsOutputAvailable,
    string? StatusMessage,
    string? LibraryRoot);

/// <summary>已保存的声源组合。</summary>
public sealed record NoiseScene(
    string Id,
    string DisplayName,
    IReadOnlyList<SoundPreset> Sounds,
    DateTimeOffset SavedAt);

/// <summary>单声源持久化状态。</summary>
public sealed record NoiseChannelSetting(string SoundId, double Volume, bool IsPlaying);

/// <summary>混音器持久化状态；主音量单独存 <c>audioVol</c> 偏好键。</summary>
public sealed record NoiseMixSettings(string? OutputDeviceId, IReadOnlyList<NoiseChannelSetting> Channels);

/// <summary>
/// 音频输出端口：由系统适配器实现，负责设备枚举、声源启动与增益写入。
/// 所有实现必须可释放，并在设备缺失时降级而不抛出到界面。
/// </summary>
public interface INoiseAudioOutput : IDisposable
{
    bool IsAvailable { get; }

    string? StatusMessage { get; }

    IReadOnlyList<AudioOutputDevice> EnumerateDevices();

    /// <summary>切换到指定设备；<paramref name="deviceId"/> 为 null 表示系统默认。</summary>
    bool TrySelectDevice(string? deviceId, out string? error);

    /// <summary>按音频库中的绝对路径启动一路文件声源；同一 ID 重复启动会被忽略。</summary>
    void StartChannel(string soundId, string absolutePath);

    void StopChannel(string soundId);

    /// <summary>设置单路音量，0–1。</summary>
    void SetChannelVolume(string soundId, double volume);

    /// <summary>设置输出总增益，0–1；淡出使用该接口。</summary>
    void SetMasterGain(double gain);

    void StopAll();
}

/// <summary>
/// 白噪音混音器：8 种内置声源、多声源并行、独立音量与总音量、
/// 组合保存、输出设备切换与 1–2 秒线性淡出。
/// </summary>
public interface INoiseMixer
{
    NoiseMixSnapshot Current { get; }

    string? LibraryRoot { get; }

    /// <summary>内置声源定义；<c>Volume</c> 为默认音量。</summary>
    IReadOnlyList<SoundPreset> Presets();

    /// <summary>按父类别分组的声源，顺序与音频资源目录一致。</summary>
    IReadOnlyList<NoiseCategory> Categories();

    IReadOnlyList<AudioOutputDevice> OutputDevices();

    IReadOnlyList<NoiseScene> Scenes();

    void Play(string soundId, double volume);

    void Stop(string soundId);

    void SetVolume(string soundId, double volume);

    void SetMasterVolume(double volume);

    /// <summary>切换输出设备；失败时保留原设备并给出原因。</summary>
    bool SetOutputDevice(string? deviceId, out string? error);

    /// <summary>保存当前播放组合；名称为空或没有播放声源时返回 null。</summary>
    NoiseScene? SaveScene(string name);

    bool ApplyScene(string sceneId);

    bool DeleteScene(string sceneId);

    /// <summary>在指定时长内线性淡出并停止全部声源。</summary>
    Task FadeOutAsync(TimeSpan duration, CancellationToken cancellationToken = default);

    event Action<NoiseMixSnapshot>? Changed;
}
