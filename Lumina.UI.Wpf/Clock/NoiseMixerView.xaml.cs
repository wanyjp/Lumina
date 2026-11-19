using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Contracts;

namespace Lumina.UI.Wpf.Clock;

/// <summary>
/// P1-04 白噪音混音视图：按音频资源目录的 8 个父类别分类展示，
/// 类内每个音频文件可独立开关与调音量；另提供总音量、组合与线性淡出停止。
/// </summary>
public partial class NoiseMixerView : UserControl
{
    private readonly INoiseMixer _mixer;
    private readonly Dictionary<string, SoundItemControls> _items = new(StringComparer.Ordinal);
    private readonly List<RadioButton> _categoryChoices = [];
    private readonly List<RadioButton> _sceneChoices = [];

    private string _categoryId = string.Empty;
    private bool _suppress;
    private bool _fading;
    private bool _subscribed;
    private string _sceneSignature = string.Empty;
    private string? _selectedSceneId;

    public NoiseMixerView(INoiseMixer mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);

        _mixer = mixer;

        InitializeComponent();

        BuildCategories();
        ApplySnapshot(_mixer.Current);
        RebuildScenes();

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

    private void BuildCategories()
    {
        var categories = _mixer.Categories();
        _categoryId = categories.FirstOrDefault()?.Id ?? string.Empty;

        _suppress = true;
        try
        {
            foreach (var category in categories)
            {
                var choice = new RadioButton
                {
                    Content = $"{category.DisplayName} · {category.Sounds.Count}",
                    Tag = category.Id,
                    GroupName = "NoiseCategoryChoice",
                    Style = (Style)FindResource("Theme.ThemeChoice"),
                    Margin = new Thickness(0, 0, 8, 8),
                    IsChecked = category.Id == _categoryId,
                };
                choice.Checked += OnCategoryChecked;
                CategoryPanel.Children.Add(choice);
                _categoryChoices.Add(choice);
            }
        }
        finally
        {
            _suppress = false;
        }

        RebuildSoundItems();
    }

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string categoryId })
        {
            return;
        }

        _categoryId = categoryId;

        _suppress = true;
        try
        {
            RebuildSoundItems();
            ApplySnapshot(_mixer.Current);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RebuildSoundItems()
    {
        SoundGrid.Children.Clear();
        _items.Clear();

        var category = _mixer.Categories().FirstOrDefault(item => item.Id == _categoryId);
        if (category is null)
        {
            return;
        }

        if (category.Sounds.Count == 0)
        {
            SoundGrid.Children.Add(new TextBlock
            {
                Text = "该分类下没有可用的音频文件。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
                Margin = new Thickness(4, 8, 0, 8),
            });
            return;
        }

        foreach (var preset in category.Sounds)
        {
            var container = new Border
            {
                Style = (Style)FindResource("Theme.Card"),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 10, 10),
            };

            var panel = new StackPanel();

            var header = new Grid();
            var name = new TextBlock
            {
                Text = preset.DisplayName,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 44, 0),
            };
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Theme.Toggle"),
                Tag = preset.Id,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Checked += OnSoundToggled;
            toggle.Unchecked += OnSoundToggled;
            header.Children.Add(name);
            header.Children.Add(toggle);

            var volume = new Slider
            {
                Style = (Style)FindResource("Theme.Slider"),
                Tag = preset.Id,
                Margin = new Thickness(0, 8, 0, 0),
            };
            volume.ValueChanged += OnVolumeChanged;

            var percent = new TextBlock
            {
                Style = (Style)FindResource("Theme.Text.Caption"),
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0),
            };

            panel.Children.Add(header);
            panel.Children.Add(volume);
            panel.Children.Add(percent);
            container.Child = panel;
            SoundGrid.Children.Add(container);

            _items[preset.Id] = new SoundItemControls(toggle, volume, percent);
        }
    }

    private void ApplySnapshot(NoiseMixSnapshot snapshot)
    {
        var previous = _suppress;
        _suppress = true;
        try
        {
            foreach (var channel in snapshot.Channels)
            {
                if (!_items.TryGetValue(channel.SoundId, out var item))
                {
                    continue;
                }

                item.Toggle.IsChecked = channel.IsPlaying;

                var volume = Math.Round(channel.Volume * 100);
                if (Math.Abs(item.Volume.Value - volume) > 0.01)
                {
                    item.Volume.Value = volume;
                }

                item.Percent.Text = $"{volume:0}%";
            }

            var master = Math.Round(snapshot.MasterVolume * 100);
            if (Math.Abs(MasterSlider.Value - master) > 0.01)
            {
                MasterSlider.Value = master;
            }

            MasterPercent.Text = $"{master:0}%";
            StatusText.Text = BuildStatus(snapshot);
            DeviceText.Text = snapshot.OutputDeviceName is null
                ? string.Empty
                : $"输出：{snapshot.OutputDeviceName}";

            SoundGrid.IsEnabled = snapshot.IsOutputAvailable;
            MasterSlider.IsEnabled = snapshot.IsOutputAvailable;
            FadeButton.IsEnabled = snapshot.IsOutputAvailable && !_fading;

            RebuildScenes();
        }
        finally
        {
            _suppress = previous;
        }
    }

    private string BuildStatus(NoiseMixSnapshot snapshot)
    {
        var categoryCount = _mixer.Categories().Count;
        var fileCount = _mixer.Presets().Count;

        if (snapshot.LibraryRoot is null)
        {
            return "未找到白噪音资源包：请将 app_audio 放入 %APPDATA%\\Lumina\\audio。";
        }

        if (!snapshot.IsOutputAvailable)
        {
            return snapshot.StatusMessage ?? "音频输出不可用，声源已禁用。";
        }

        var active = snapshot.Channels.Count(channel => channel.IsPlaying);
        var text = active == 0
            ? $"音频资源：{categoryCount} 类 · {fileCount} 个文件；打开任意声源即可开始混音。"
            : $"正在播放 {active} 路 · 音频资源 {categoryCount} 类 {fileCount} 个文件。";

        return string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? text
            : $"{text} {snapshot.StatusMessage}";
    }

    private void RebuildScenes()
    {
        var scenes = _mixer.Scenes();
        var signature = string.Join("|", scenes.Select(scene => $"{scene.Id}:{scene.DisplayName}"));

        if (signature == _sceneSignature)
        {
            UpdateSceneSelection();
            return;
        }

        _sceneSignature = signature;
        ScenePanel.Children.Clear();
        _sceneChoices.Clear();

        if (scenes.Count == 0)
        {
            ScenePanel.Children.Add(new TextBlock
            {
                Text = "还没有保存组合。",
                Style = (Style)FindResource("Theme.Text.Tertiary"),
            });
            return;
        }

        foreach (var scene in scenes)
        {
            var choice = new RadioButton
            {
                Content = scene.DisplayName,
                Tag = scene.Id,
                GroupName = "NoiseSceneChoice",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = scene.Id == _selectedSceneId,
            };
            choice.Checked += OnSceneChecked;
            ScenePanel.Children.Add(choice);
            _sceneChoices.Add(choice);
        }
    }

    private void UpdateSceneSelection()
    {
        foreach (var choice in _sceneChoices)
        {
            choice.IsChecked = (string?)choice.Tag == _selectedSceneId;
        }
    }

    private void OnSoundToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not CheckBox { Tag: string soundId } toggle)
        {
            return;
        }

        if (toggle.IsChecked == true)
        {
            var volume = _items.TryGetValue(soundId, out var item) ? item.Volume.Value / 100 : 0.4;
            _mixer.Play(soundId, volume);
        }
        else
        {
            _mixer.Stop(soundId);
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || sender is not Slider { Tag: string soundId } slider)
        {
            return;
        }

        var volume = Math.Round(slider.Value);
        if (_items.TryGetValue(soundId, out var item))
        {
            item.Percent.Text = $"{volume:0}%";
        }

        _mixer.SetVolume(soundId, volume / 100);
    }

    private void OnMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress)
        {
            return;
        }

        var volume = Math.Round(MasterSlider.Value);
        MasterPercent.Text = $"{volume:0}%";
        _mixer.SetMasterVolume(volume / 100);
    }

    private void OnSceneChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not RadioButton { Tag: string sceneId })
        {
            return;
        }

        _selectedSceneId = sceneId;

        if (_mixer.ApplyScene(sceneId))
        {
            HintText.Text = "已切换到所选组合。";
        }
        else
        {
            _selectedSceneId = null;
            HintText.Text = "该组合已不存在，请重新保存。";
        }
    }

    private void OnSaveSceneClick(object sender, RoutedEventArgs e)
    {
        var scene = _mixer.SaveScene(SceneNameBox.Text);
        if (scene is null)
        {
            HintText.Text = SceneNameBox.Text.Trim().Length == 0
                ? "请输入组合名称。"
                : "请先打开至少一个声源。";
            return;
        }

        _selectedSceneId = scene.Id;
        SceneNameBox.Text = string.Empty;
        HintText.Text = $"已保存组合「{scene.DisplayName}」。";
    }

    private void OnDeleteSceneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSceneId is null)
        {
            HintText.Text = "请先选择一个组合。";
            return;
        }

        _mixer.DeleteScene(_selectedSceneId);
        _selectedSceneId = null;
        HintText.Text = "已删除所选组合。";
    }

    private async void OnFadeClick(object sender, RoutedEventArgs e)
    {
        if (_fading)
        {
            return;
        }

        _fading = true;
        FadeButton.IsEnabled = false;
        HintText.Text = "正在淡出并停止…";

        try
        {
            await _mixer.FadeOutAsync(TimeSpan.FromSeconds(1.5));
            HintText.Text = "已淡出停止全部声源。";
        }
        catch (Exception ex)
        {
            HintText.Text = $"淡出失败：{ex.Message}";
        }
        finally
        {
            _fading = false;
            ApplySnapshot(_mixer.Current);
        }
    }

    private sealed record SoundItemControls(CheckBox Toggle, Slider Volume, TextBlock Percent);
}
