using Lumina.Data.Settings;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Noise;
using Lumina.Services.Settings;

namespace Lumina.Tests;

/// <summary>P1-04 白噪音：8 父类别、文件播放、音量合成、淡出、组合与设备降级。</summary>
public class NoiseMixerTests
{
    private const string RainSound = "rain/伞下听雨";
    private const string RainSound2 = "rain/小雨";
    private const string WaterSound = "water/海浪";
    private const string FireSound = "fire/篝火燃烧";

    [Fact]
    public void Library_preserves_eight_parent_categories_and_scans_files()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var library = CreateLibrary(temp);

        Assert.NotNull(library.Root);
        Assert.Equal(8, library.Categories.Count);
        Assert.Equal(
            new[] { "雨", "水流", "风", "火焰", "森林与夜", "动物", "城市与交通", "户外脚步" },
            library.Categories.Select(category => category.DisplayName));
        Assert.Equal(2, library.Categories[0].Sounds.Count);
        Assert.Single(library.Categories[1].Sounds);
        Assert.Equal(9, library.Sounds.Count);
        Assert.Equal(RainSound, library.Categories[0].Sounds[0].Id);
        Assert.Equal("雨/伞下听雨.wav", library.Categories[0].Sounds[0].RelativePath);
        Assert.NotEmpty(library.Categories[2].Sounds);

        // 非音频文件不参与扫描。
        Assert.DoesNotContain(library.Sounds, sound => sound.DisplayName == "说明");
    }

    [Fact]
    public void Library_keeps_eight_empty_groups_when_resource_root_has_no_folders()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var emptyRoot = Path.Combine(temp.Root, "empty-audio");
        Directory.CreateDirectory(emptyRoot);

        var library = NoiseLibrary.Scan(emptyRoot);

        Assert.Equal(8, library.Categories.Count);
        Assert.All(library.Categories, category => Assert.Empty(category.Sounds));
        Assert.Empty(library.Sounds);
        Assert.Equal(emptyRoot, library.Root);

        var missing = NoiseLibrary.Empty();
        Assert.Null(missing.Root);
        Assert.Equal(8, missing.Categories.Count);
    }

    [Fact]
    public void Play_and_stop_update_snapshot_and_output_channels()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var output = new FakeNoiseOutput();
        using var mixer = new NoiseMixer(output, CreateSettings(temp), CreateLibrary(temp));

        mixer.Play(RainSound, 0.7);
        mixer.Play(RainSound2, 0.3);

        var snapshot = mixer.Current;
        Assert.Equal(2, snapshot.Channels.Count(channel => channel.IsPlaying));
        Assert.Equal(0.7, Channel(snapshot, RainSound).Volume);
        Assert.Contains(RainSound, output.Started);
        Assert.Contains(RainSound2, output.Started);
        Assert.EndsWith(".wav", output.StartPaths[RainSound]);
        Assert.EndsWith(".mp3", output.StartPaths[RainSound2]);

        mixer.Stop(RainSound);

        Assert.False(Channel(mixer.Current, RainSound).IsPlaying);
        Assert.Contains(RainSound, output.Stopped);
        Assert.True(Channel(mixer.Current, RainSound2).IsPlaying);
    }

    [Fact]
    public void Play_missing_file_does_not_start_channel()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var library = CreateLibrary(temp);
        var rainFile = Path.Combine(library.Root!, "雨", "伞下听雨.wav");
        File.Delete(rainFile);

        var output = new FakeNoiseOutput();
        using var mixer = new NoiseMixer(output, CreateSettings(temp), library);

        mixer.Play(RainSound, 0.6);

        Assert.Empty(output.Started);
        Assert.False(Channel(mixer.Current, RainSound).IsPlaying);
        Assert.Contains("音频文件缺失", mixer.Current.StatusMessage);
    }

    [Fact]
    public void Volume_and_master_volume_are_clamped_and_persisted()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        var output = new FakeNoiseOutput();
        using var mixer = new NoiseMixer(output, settings, CreateLibrary(temp));

        mixer.SetVolume(RainSound, 1.4);
        mixer.SetMasterVolume(0.4);

        Assert.Equal(1.0, Channel(mixer.Current, RainSound).Volume);
        Assert.Equal(0.4, mixer.Current.MasterVolume);
        Assert.Equal(40, settings.Get<int>(PreferenceKeys.AudioVolume));

        mixer.Play(RainSound, 1.0);
        Assert.Equal(1.0, output.ChannelVolumes[RainSound]);
    }

    [Fact]
    public async Task Fade_out_ramps_gain_to_zero_and_stops_all_channels()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var output = new FakeNoiseOutput();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Func<TimeSpan, CancellationToken, Task> delay = (span, _) =>
        {
            now += span;
            return Task.CompletedTask;
        };
        using var mixer = new NoiseMixer(
            output,
            CreateSettings(temp),
            CreateLibrary(temp),
            () => now,
            delay);

        mixer.Play(RainSound, 0.6);
        mixer.Play(FireSound, 0.4);
        output.Gains.Clear();

        await mixer.FadeOutAsync(
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken);

        Assert.Contains(output.Gains, gain => gain <= 0.0001);
        Assert.Contains(RainSound, output.Stopped);
        Assert.Contains(FireSound, output.Stopped);
        Assert.All(mixer.Current.Channels, channel => Assert.False(channel.IsPlaying));
        Assert.Equal(0.6, Math.Round(output.Gains[^1], 3));
    }

    [Fact]
    public void Unavailable_output_degrades_with_message()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var output = new FakeNoiseOutput
        {
            IsAvailable = false,
            StatusMessage = "未找到可用的音频输出设备",
        };
        using var mixer = new NoiseMixer(output, CreateSettings(temp), CreateLibrary(temp));

        mixer.Play(RainSound, 0.5);

        var snapshot = mixer.Current;
        Assert.False(snapshot.IsOutputAvailable);
        Assert.Equal("未找到可用的音频输出设备", snapshot.StatusMessage);
        Assert.All(snapshot.Channels, channel => Assert.False(channel.IsPlaying));
        Assert.Empty(output.Started);
    }

    [Fact]
    public void Device_enumeration_failure_degrades_to_empty_list()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var output = new FakeNoiseOutput { FailEnumerate = true };
        using var mixer = new NoiseMixer(output, CreateSettings(temp), CreateLibrary(temp));

        Assert.Empty(mixer.OutputDevices());
        Assert.Contains("枚举失败", mixer.Current.StatusMessage);
    }

    [Fact]
    public void Scenes_save_apply_delete_and_survive_reload()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        var library = CreateLibrary(temp);
        var output = new FakeNoiseOutput();
        using var mixer = new NoiseMixer(output, settings, library);

        mixer.Play(RainSound, 0.7);
        mixer.Play(WaterSound, 0.3);
        var scene = mixer.SaveScene("夜间雨声");

        Assert.NotNull(scene);
        Assert.Equal(2, scene.Sounds.Count);
        Assert.Single(mixer.Scenes());

        using var reloaded = new NoiseMixer(new FakeNoiseOutput(), CreateSettings(temp), CreateLibrary(temp));
        var reloadedScene = reloaded.Scenes().Single();
        Assert.Equal("夜间雨声", reloadedScene.DisplayName);

        mixer.Stop(RainSound);
        mixer.Stop(WaterSound);

        Assert.True(mixer.ApplyScene(reloadedScene.Id));
        Assert.True(Channel(mixer.Current, RainSound).IsPlaying);
        Assert.True(Channel(mixer.Current, WaterSound).IsPlaying);

        Assert.True(mixer.DeleteScene(reloadedScene.Id));
        Assert.Empty(mixer.Scenes());
    }

    [Fact]
    public void Output_device_and_mix_state_survive_reload()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var settings = CreateSettings(temp);
        var output = new FakeNoiseOutput();
        using var mixer = new NoiseMixer(output, settings, CreateLibrary(temp));

        mixer.Play(FireSound, 0.45);
        Assert.True(mixer.SetOutputDevice("dev-1", out var error));
        Assert.Null(error);

        var reloadedOutput = new FakeNoiseOutput();
        using var reloaded = new NoiseMixer(reloadedOutput, CreateSettings(temp), CreateLibrary(temp));

        Assert.Equal("dev-1", reloaded.Current.OutputDeviceId);
        Assert.Contains("dev-1", reloadedOutput.SelectedDevices);
        Assert.True(Channel(reloaded.Current, FireSound).IsPlaying);
        Assert.Contains(FireSound, reloadedOutput.Started);
    }

    [Fact]
    public void Dispose_stops_and_releases_output_once()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var output = new FakeNoiseOutput();
        var mixer = new NoiseMixer(output, CreateSettings(temp), CreateLibrary(temp));

        mixer.Dispose();
        mixer.Dispose();

        Assert.True(output.StopAllCalled);
        Assert.Equal(1, output.DisposeCount);
    }

    private static SettingsStore CreateSettings(TemporaryDataRoot temp) =>
        new(new SqliteSettingsBackend(temp.Database));

    private static NoiseChannelState Channel(NoiseMixSnapshot snapshot, string soundId) =>
        snapshot.Channels.Single(channel => channel.SoundId == soundId);

    private static NoiseLibrary CreateLibrary(TemporaryDataRoot temp)
    {
        var root = Path.Combine(temp.Root, "audio");
        var files = new[]
        {
            ("雨", "伞下听雨.wav"),
            ("雨", "小雨.mp3"),
            ("水流", "海浪.ogg"),
            ("风", "原野风声.ogg"),
            ("火焰", "篝火燃烧.wav"),
            ("森林与夜", "丛林.mp3"),
            ("动物", "晨间鸟鸣.ogg"),
            ("城市与交通", "城市街道.ogg"),
            ("户外脚步", "雪地脚步.wav"),
        };

        foreach (var (folder, name) in files)
        {
            var directory = Path.Combine(root, folder);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), []);
        }

        Directory.CreateDirectory(Path.Combine(root, "雨"));
        File.WriteAllText(Path.Combine(root, "雨", "说明.txt"), "不是音频");

        return NoiseLibrary.Scan(root);
    }

    private sealed class FakeNoiseOutput : INoiseAudioOutput
    {
        public bool IsAvailable { get; set; } = true;

        public string? StatusMessage { get; set; }

        public bool FailEnumerate { get; set; }

        public List<string> Started { get; } = [];

        public List<string> Stopped { get; } = [];

        public Dictionary<string, string> StartPaths { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, double> ChannelVolumes { get; } = new(StringComparer.Ordinal);

        public List<double> Gains { get; } = [];

        public List<string?> SelectedDevices { get; } = [];

        public bool StopAllCalled { get; private set; }

        public int DisposeCount { get; private set; }

        public IReadOnlyList<AudioOutputDevice> EnumerateDevices()
        {
            if (FailEnumerate)
            {
                throw new InvalidOperationException("枚举失败");
            }

            return [new AudioOutputDevice("dev-1", "扬声器", true)];
        }

        public bool TrySelectDevice(string? deviceId, out string? error)
        {
            error = null;
            SelectedDevices.Add(deviceId);
            return true;
        }

        public void StartChannel(string soundId, string absolutePath)
        {
            Started.Add(soundId);
            StartPaths[soundId] = absolutePath;
            ChannelVolumes.TryAdd(soundId, 0.5);
        }

        public void StopChannel(string soundId) => Stopped.Add(soundId);

        public void SetChannelVolume(string soundId, double volume) => ChannelVolumes[soundId] = volume;

        public void SetMasterGain(double gain) => Gains.Add(gain);

        public void StopAll() => StopAllCalled = true;

        public void Dispose() => DisposeCount++;
    }
}
