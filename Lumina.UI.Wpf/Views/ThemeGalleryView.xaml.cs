using System.Windows;
using System.Windows.Controls;
using Lumina.Services.Theming;
using Lumina.UI.Wpf.Themes;

namespace Lumina.UI.Wpf.Views;

/// <summary>
/// P0-05 主题与令牌演示视图。仅用于验收五档主题与组件基线，P0-06 起由真实主界面替换。
/// </summary>
public partial class ThemeGalleryView : UserControl
{
    private readonly ThemeManager _themeManager;
    private bool _subscribed;

    public ThemeGalleryView(ThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(themeManager);

        _themeManager = themeManager;
        InitializeComponent();
        BuildThemeChoices();
        UpdateStatus();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _themeManager.Changed += OnThemeChanged;
            _subscribed = true;
        }

        UpdateStatus();
        UpdateChoices();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _themeManager.Changed -= OnThemeChanged;
            _subscribed = false;
        }
    }

    private void BuildThemeChoices()
    {
        ThemeChoicesPanel.Children.Clear();

        foreach (var themeId in ThemeCatalog.All)
        {
            var choice = new RadioButton
            {
                Content = ThemeCatalog.DisplayName(themeId),
                Tag = themeId,
                GroupName = "ThemeChoice",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = themeId == _themeManager.RequestedTheme,
            };

            choice.Checked += OnThemeChoiceChecked;
            ThemeChoicesPanel.Children.Add(choice);
        }
    }

    private void OnThemeChoiceChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string themeId } && themeId != _themeManager.RequestedTheme)
        {
            _themeManager.SetTheme(themeId);
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        UpdateStatus();
        UpdateChoices();
    }

    private void UpdateChoices()
    {
        foreach (var child in ThemeChoicesPanel.Children)
        {
            if (child is RadioButton { Tag: string themeId } choice)
            {
                choice.IsChecked = themeId == _themeManager.RequestedTheme;
            }
        }
    }

    private void UpdateStatus()
    {
        var effective = ThemeCatalog.DisplayName(_themeManager.EffectiveTheme);
        var accessibility = string.Empty;

        if (_themeManager.IsHighContrast)
        {
            accessibility += " · 高对比度";
        }

        if (_themeManager.IsReducedMotion)
        {
            accessibility += " · 减少动画";
        }

        ThemeStatusText.Text =
            $"当前：{ThemeCatalog.DisplayName(_themeManager.RequestedTheme)} · 生效：{effective}{accessibility}";
    }
}
