using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Settings;

namespace Lumina.UI.Wpf.Settings;

/// <summary>
/// P1-11 设置中心：13 组分类导航，仅构建当前分组；共享 <see cref="ISettingsStore"/>，
/// 外部修改经 SettingsChanged 去抖后重建当前分组。
/// </summary>
public partial class SettingsCenterView : UserControl
{
    private static readonly HashSet<string> SilentKeys = new(StringComparer.Ordinal)
    {
        PreferenceKeys.AudioVolume,
        PreferenceKeys.WorldSelectedCity,
    };

    private readonly SettingsContext _context;
    private readonly DispatcherTimer _refreshTimer;

    private IDisposable? _subscription;
    private string _currentGroupId = SettingsGroupCatalog.General;

    public SettingsCenterView(SettingsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _refreshTimer.Tick += OnRefreshTick;

        GroupList.ItemsSource = SettingsGroupCatalog.All;
        GroupList.SelectedIndex = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>设置页重新显示时重建当前分组，确保抽屉或其他页面的修改已反映。</summary>
    public void Refresh() => ShowGroup(_currentGroupId);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _subscription ??= _context.Settings.Changed.Subscribe(
            new ActionObserver<SettingChanged>(OnSettingChanged));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupList.SelectedItem is SettingsGroup group)
        {
            ShowGroup(group.Id);
        }
    }

    private void ShowGroup(string groupId)
    {
        var group = SettingsGroupCatalog.For(groupId);

        _currentGroupId = groupId;
        GroupTitle.Text = group.Title;
        GroupSummary.Text = group.Summary;
        GroupHost.Content = SettingsSectionFactory.BuildGroup(_context, groupId);
    }

    private void OnSettingChanged(SettingChanged change)
    {
        if (!IsVisible || !IsRelevant(change.Key))
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private bool IsRelevant(string key)
    {
        if (SilentKeys.Contains(key) || key.StartsWith("hotkey.", StringComparison.Ordinal))
        {
            return false;
        }

        return _currentGroupId != SettingsGroupCatalog.Hotkeys;
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        ShowGroup(_currentGroupId);
    }
}
