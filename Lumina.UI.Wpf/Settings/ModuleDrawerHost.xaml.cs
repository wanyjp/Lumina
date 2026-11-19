using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;
using Lumina.UI.Wpf.Themes;

namespace Lumina.UI.Wpf.Settings;

/// <summary>
/// 页内齿轮抽屉：打开时只构建当前模块的设置内容；关闭即释放，
/// 监听 SettingsChanged 并在外部修改后去抖重建。
/// </summary>
public partial class ModuleDrawerHost : UserControl
{
    private static readonly HashSet<string> SilentKeys = new(StringComparer.Ordinal)
    {
        PreferenceKeys.AudioVolume,
        PreferenceKeys.WorldSelectedCity,
    };

    private readonly ISettingsStore _settings;
    private readonly ThemeManager _themes;
    private readonly DispatcherTimer _refreshTimer;

    private IDisposable? _subscription;
    private Func<FrameworkElement>? _contentFactory;
    private string? _moduleId;

    public ModuleDrawerHost(ISettingsStore settings, ThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themes);

        _settings = settings;
        _themes = themes;

        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(140),
        };
        _refreshTimer.Tick += OnRefreshTick;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public string? CurrentModuleId => _moduleId;

    public void Open(string moduleId, string title, Func<FrameworkElement> contentFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(contentFactory);

        _moduleId = moduleId;
        _contentFactory = contentFactory;

        TitleText.Text = $"{title} · 模块设置";
        ContentHost.Content = contentFactory();
        Visibility = Visibility.Visible;

        _subscription ??= _settings.Changed.Subscribe(
            new ActionObserver<SettingChanged>(OnSettingChanged));

        PlayEnterAnimation();

        Focusable = true;
        Keyboard.Focus(this);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        _refreshTimer.Stop();
        _subscription?.Dispose();
        _subscription = null;
        _contentFactory = null;
        _moduleId = null;

        ContentHost.Content = null;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    private void PlayEnterAnimation()
    {
        Panel.BeginAnimation(OpacityProperty, null);
        Panel.RenderTransform = Transform.Identity;
        Panel.Opacity = 1;

        if (_themes.IsReducedMotion)
        {
            return;
        }

        var translate = new TranslateTransform(28, 0);
        Panel.RenderTransform = translate;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(200);

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(28, 0, duration) { EasingFunction = ease });
        Panel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    private void OnSettingChanged(SettingChanged change)
    {
        if (!IsOpen
            || SilentKeys.Contains(change.Key)
            || change.Key.StartsWith("hotkey.", StringComparison.Ordinal))
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();

        if (_contentFactory is not null)
        {
            ContentHost.Content = _contentFactory();
        }
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
