using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// P1-04 白噪音设置：输出设备选择与主音量；与时钟页的白噪音卡片共享同一混音器实例。
/// 设备切换会重建输出流，失败时保留原设备并给出提示。
/// </summary>
public partial class NoiseSettingsView : UserControl
{
    private readonly INoiseMixer _mixer;
    private readonly List<RadioButton> _deviceChoices = [];

    private bool _suppress;
    private bool _subscribed;
    private string _deviceSignature = string.Empty;

    public NoiseSettingsView(INoiseMixer mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);

        _mixer = mixer;

        InitializeComponent();
        ApplySnapshot(_mixer.Current);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            return;
        }

        _mixer.Changed += OnMixerChanged;
        _subscribed = true;
        ApplySnapshot(_mixer.Current);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            return;
        }

        _mixer.Changed -= OnMixerChanged;
        _subscribed = false;
    }

    private void OnMixerChanged(NoiseMixSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(NoiseMixSnapshot snapshot)
    {
        _suppress = true;
        try
        {
            var volume = Math.Round(snapshot.MasterVolume * 100);
            if (Math.Abs(VolumeSlider.Value - volume) > 0.01)
            {
                VolumeSlider.Value = volume;
            }

            VolumeText.Text = $"{volume:0}%";
            VolumeSlider.IsEnabled = snapshot.IsOutputAvailable;
            DevicePanel.IsEnabled = snapshot.IsOutputAvailable;

            RebuildDevices(snapshot);

            StatusText.Text = snapshot.IsOutputAvailable
                ? snapshot.OutputDeviceName is null
                    ? "已连接音频输出。"
                    : $"当前输出：{snapshot.OutputDeviceName}"
                : snapshot.StatusMessage ?? "音频输出不可用。";

            LibraryText.Text = snapshot.LibraryRoot is null
                ? "未找到白噪音资源包：请将 app_audio 放入 %APPDATA%\\Lumina\\audio。"
                : $"音频资源：{_mixer.Categories().Count} 类 · {_mixer.Presets().Count} 个文件";
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RebuildDevices(NoiseMixSnapshot snapshot)
    {
        var devices = _mixer.OutputDevices();
        var signature = string.Join(
            "|",
            devices.Select(device => $"{device.Id}:{device.DisplayName}:{device.IsDefault}"));

        if (signature == _deviceSignature)
        {
            UpdateDeviceSelection(snapshot.OutputDeviceId);
            return;
        }

        _deviceSignature = signature;
        DevicePanel.Children.Clear();
        _deviceChoices.Clear();

        AddDeviceChoice("系统默认（跟随 Windows）", string.Empty, snapshot.OutputDeviceId is null);

        foreach (var device in devices)
        {
            var label = device.IsDefault ? $"{device.DisplayName}（默认）" : device.DisplayName;
            AddDeviceChoice(label, device.Id, device.Id == snapshot.OutputDeviceId);
        }

        if (devices.Count == 0)
        {
            DevicePanel.Children.Add(new TextBlock
            {
                Text = "未检测到音频输出设备。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
            });
        }
    }

    private void AddDeviceChoice(string label, string deviceId, bool isChecked)
    {
        var choice = new RadioButton
        {
            Content = label,
            Tag = deviceId,
            GroupName = "NoiseOutputDevice",
            Style = (Style)FindResource("Theme.ThemeChoice"),
            Margin = new Thickness(0, 0, 8, 8),
            IsChecked = isChecked,
        };
        choice.Checked += OnDeviceChecked;
        DevicePanel.Children.Add(choice);
        _deviceChoices.Add(choice);
    }

    private void UpdateDeviceSelection(string? deviceId)
    {
        var selected = deviceId ?? string.Empty;

        foreach (var choice in _deviceChoices)
        {
            choice.IsChecked = (string?)choice.Tag == selected;
        }
    }

    private void OnDeviceChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string deviceId })
        {
            return;
        }

        _suppress = true;
        try
        {
            if (_mixer.SetOutputDevice(deviceId.Length == 0 ? null : deviceId, out var error))
            {
                DeviceHint.Text = "已切换输出设备；如无声音请检查系统音量与设备连接。";
            }
            else
            {
                DeviceHint.Text = $"{error ?? "切换失败"} 已保留原设备。";
            }

            UpdateDeviceSelection(_mixer.Current.OutputDeviceId);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress)
        {
            return;
        }

        var volume = Math.Round(VolumeSlider.Value);
        VolumeText.Text = $"{volume:0}%";
        _mixer.SetMasterVolume(volume / 100);
    }
}
