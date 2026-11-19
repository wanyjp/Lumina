using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Countdowns;
using Lumina.UI.Wpf.Themes;
using WpfPath = System.Windows.Shapes.Path;

namespace Lumina.UI.Wpf.Countdown;

/// <summary>
/// 倒计时日主视图（P3-04）：分类筛选（带计数、不持久化）、三列卡片、
/// 五档紧迫度配色与进度条、最紧迫档呼吸光效、放大态与三个操作按钮。
/// </summary>
public partial class CountdownView : UserControl
{
    private readonly ICountdownService _countdowns;
    private readonly IWidgetHost _widgetHost;
    private readonly ThemeManager _themeManager;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _statusTimer;
    private readonly List<DropShadowEffect> _breathingEffects = [];
    private readonly List<SolidColorBrush> _breathingBrushes = [];

    private CountdownCategory? _filter;
    private bool _initialized;
    private bool _subscribed;

    public CountdownView(
        ICountdownService countdowns,
        IWidgetHost widgetHost,
        ThemeManager themeManager,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(countdowns);
        ArgumentNullException.ThrowIfNull(widgetHost);
        ArgumentNullException.ThrowIfNull(themeManager);
        ArgumentNullException.ThrowIfNull(settings);

        _countdowns = countdowns;
        _widgetHost = widgetHost;
        _themeManager = themeManager;
        _settings = settings;

        InitializeComponent();
        _initialized = true;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = string.Empty;
        };

        Zoom.Closed += OnZoomClosed;
        Zoom.EditRequested += OnZoomEditRequested;
        Zoom.DesktopRequested += OnZoomDesktopRequested;
        Zoom.DeleteRequested += OnZoomDeleteRequested;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _countdowns.Changed += OnCountdownChanged;
            _themeManager.Changed += OnThemeChanged;
            _subscribed = true;
        }

        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _countdowns.Changed -= OnCountdownChanged;
            _themeManager.Changed -= OnThemeChanged;
            _subscribed = false;
        }

        _statusTimer.Stop();
        Zoom.Close();
        StopBreathing();
    }

    private void OnCountdownChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Render));
            return;
        }

        Render();
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Render();

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _filter = tag switch
        {
            "work" => CountdownCategory.Work,
            "life" => CountdownCategory.Life,
            "exam" => CountdownCategory.Exam,
            "anniv" => CountdownCategory.Anniversary,
            "other" => CountdownCategory.Other,
            _ => null,
        };

        Render();
    }

    private void Render()
    {
        if (!_initialized)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var items = _countdowns.All();
        var summary = CountdownSummaryBuilder.Build(items, today);

        AllFilter.Content = $"全部 · {summary.Total}";
        WorkFilter.Content = $"工作 · {summary.CountOf(CountdownCategory.Work)}";
        LifeFilter.Content = $"生活 · {summary.CountOf(CountdownCategory.Life)}";
        ExamFilter.Content = $"考试 · {summary.CountOf(CountdownCategory.Exam)}";
        AnniversaryFilter.Content = $"纪念日 · {summary.CountOf(CountdownCategory.Anniversary)}";
        OtherFilter.Content = $"其他 · {summary.CountOf(CountdownCategory.Other)}";

        SummaryLine.Text = summary.Nearest is { } nearest && summary.NearestSnapshot is { } nearestSnapshot
            ? $"共 {summary.Total} 项 · 30 天内 {summary.DueWithin30Days} 项 · 最近「{nearest.Name}」{CountdownText.Remaining(nearestSnapshot)}"
            : $"共 {summary.Total} 项 · 30 天内 {summary.DueWithin30Days} 项";

        var filtered = _filter is { } category
            ? items.Where(item => item.Category == category).ToArray()
            : items.ToArray();

        StopBreathing();
        CardGrid.Children.Clear();

        if (filtered.Length == 0)
        {
            CardGrid.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyTitle.Text = items.Count == 0 ? "还没有倒计时" : "该分类下暂无倒计时";
            EmptyBody.Text = items.Count == 0
                ? "记录考试、纪念日或周期性节点，卡片会按紧迫度自动分档。"
                : "切换到「全部」或新增一个该分类的日期。";
            return;
        }

        CardGrid.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;

        foreach (var item in filtered)
        {
            CardGrid.Children.Add(CreateCard(item));
        }

        CardGrid.Children.Add(CreateAddTile());
    }

    private FrameworkElement CreateCard(CountdownItem item)
    {
        var snapshot = _countdowns.Evaluate(item);
        var brushKey = CountdownVisuals.UrgencyBrushKey(snapshot.Level);
        var isInvalid = snapshot.NextOccurrence is null;

        var strip = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 0, 12),
        };
        strip.SetResourceReference(Border.BackgroundProperty, isInvalid ? "Theme.Urgency.ExpiredBrush" : brushKey);

        var number = new TextBlock
        {
            Text = CountdownText.Number(snapshot),
            FontSize = 42,
            FontWeight = FontWeights.SemiBold,
            FontFamily = (FontFamily)FindResource("Theme.FontFamily"),
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, isInvalid ? "Theme.Urgency.ExpiredBrush" : brushKey);

        var numberUnit = new TextBlock
        {
            Text = "天",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(5, 0, 0, 7),
            Visibility = isInvalid ? Visibility.Collapsed : Visibility.Visible,
        };
        numberUnit.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");

        var numberRow = new StackPanel { Orientation = Orientation.Horizontal };
        numberRow.Children.Add(number);
        numberRow.Children.Add(numberUnit);

        // 保留原句“今天就是 1 / 距离 2 还有”，只把名称加粗放大，不另起标题行。
        var label = CountdownVisuals.CreateLabel(item, snapshot, fontSize: 13, nameFontSize: 15.5);
        label.Margin = new Thickness(0, 9, 0, 0);

        var semantic = new TextBlock
        {
            Text = CountdownText.Semantic(item, snapshot),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(0, 5, 0, 0),
        };
        semantic.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondaryBrush");

        var remaining = new TextBlock
        {
            Text = CountdownText.Remaining(snapshot),
            FontSize = 12.5,
            Margin = new Thickness(0, 6, 0, 0),
        };
        remaining.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondaryBrush");

        var progress = new ProgressBar
        {
            Style = (Style)FindResource("Countdown.Progress"),
            Value = Math.Round(snapshot.ProgressRatio * 100),
            Margin = new Thickness(0, 12, 0, 0),
        };
        progress.SetResourceReference(ProgressBar.ForegroundProperty, isInvalid ? "Theme.Urgency.ExpiredBrush" : brushKey);

        var content = new StackPanel();
        content.Children.Add(CreateCategoryChip(item.Category));
        content.Children.Add(label);
        content.Children.Add(numberRow);
        content.Children.Add(semantic);
        content.Children.Add(remaining);
        content.Children.Add(progress);

        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            MinHeight = 166,
        };
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");
        card.SetResourceReference(Border.BorderThicknessProperty, "Theme.Stroke.Hairline");
        card.SetResourceReference(Border.BackgroundProperty, "Theme.CardBrush");
        card.SetResourceReference(Border.CornerRadiusProperty, "Theme.Radius.Default");

        var layout = new StackPanel();
        layout.Children.Add(strip);
        layout.Children.Add(content);
        card.Child = layout;

        if (snapshot.Level == UrgencyLevel.Urgent && !_themeManager.IsReducedMotion && !isInvalid)
        {
            ApplyBreathing(card, snapshot.Level);
        }

        var button = new Button
        {
            Style = (Style)FindResource("Countdown.CardButton"),
            Content = card,
            Tag = item,
        };
        AutomationProperties.SetName(
            button,
            $"倒计时：{item.Name}，{CountdownText.CategoryLabel(item.Category)}，"
            + $"{CountdownText.Label(item, snapshot)}{CountdownText.Number(snapshot)} 天，"
            + $"{CountdownText.Remaining(snapshot)}");
        button.Click += (_, _) => OpenZoom(item);

        return button;
    }

    private FrameworkElement CreateCategoryChip(CountdownCategory category)
    {
        var icon = new WpfPath
        {
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            Data = CountdownVisuals.CategoryGeometry(category),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
        };
        icon.SetResourceReference(Shape.StrokeProperty, "Theme.TextSecondaryBrush");

        var label = new TextBlock
        {
            Text = CountdownText.CategoryLabel(category),
            FontSize = 10.5,
            Margin = new Thickness(4, 0, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondaryBrush");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(label);

        var chip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(12),
            Child = panel,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Theme.WallSecondaryBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");
        chip.SetResourceReference(Border.BorderThicknessProperty, "Theme.Stroke.Hairline");
        return chip;
    }

    private FrameworkElement CreateAddTile()
    {
        var icon = new WpfPath
        {
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            Data = Geometry.Parse("M12,5 V19 M5,12 H19"),
            StrokeThickness = 1.8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.SetResourceReference(Shape.StrokeProperty, "Theme.TextTertiaryBrush");

        var text = new TextBlock
        {
            Text = "新增日期",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextTertiaryBrush");

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(icon);
        panel.Children.Add(text);

        var border = new Border
        {
            MinHeight = 158,
            Child = panel,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Theme.WallSecondaryBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "Theme.BorderBrush");
        border.SetResourceReference(Border.BorderThicknessProperty, "Theme.Stroke.Hairline");
        border.SetResourceReference(Border.CornerRadiusProperty, "Theme.Radius.Default");

        var button = new Button
        {
            Style = (Style)FindResource("Countdown.CardButton"),
            Content = border,
            ToolTip = "新增倒计时",
        };
        AutomationProperties.SetName(button, "新增倒计时");
        button.Click += OnAddClick;
        return button;
    }

    private void ApplyBreathing(Border card, UrgencyLevel level)
    {
        if (TryFindResource(CountdownVisuals.UrgencyColorKey(level)) is not Color color)
        {
            return;
        }

        var effect = new DropShadowEffect
        {
            Color = color,
            ShadowDepth = 0,
            BlurRadius = 30,
            Opacity = 0.30,
        };
        card.Effect = effect;
        _breathingEffects.Add(effect);

        var animation = new DoubleAnimation(0.24, 0.78, TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        effect.BeginAnimation(DropShadowEffect.OpacityProperty, animation);

        // 描边同步呼吸，给紧迫卡片一条清晰的高亮边缘，避免只有一个模糊光斑。
        var edge = new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B));
        card.BorderBrush = edge;
        _breathingBrushes.Add(edge);

        var edgeAnimation = new ColorAnimation(
            Color.FromArgb(0x30, color.R, color.G, color.B),
            Color.FromArgb(0xD0, color.R, color.G, color.B),
            TimeSpan.FromSeconds(2.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        edge.BeginAnimation(SolidColorBrush.ColorProperty, edgeAnimation);
    }

    private void StopBreathing()
    {
        foreach (var effect in _breathingEffects)
        {
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        }

        foreach (var brush in _breathingBrushes)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }

        _breathingEffects.Clear();
        _breathingBrushes.Clear();
    }

    private void OpenZoom(CountdownItem item)
    {
        ContentScroll.Effect = new BlurEffect
        {
            Radius = 12,
            KernelType = KernelType.Gaussian,
        };
        Zoom.Show(item, _countdowns.Evaluate(item));
    }

    private void OnZoomClosed(object? sender, EventArgs e) => ContentScroll.Effect = null;

    private async void OnZoomEditRequested(object? sender, EventArgs e)
    {
        var item = Zoom.Item;
        Zoom.Close();
        if (item is not null)
        {
            await EditAsync(item);
        }
    }

    private void OnZoomDesktopRequested(object? sender, EventArgs e)
    {
        if (Zoom.Item is not { } item)
        {
            return;
        }

        Zoom.Close();
        var state = _widgetHost.GetState(WidgetKind.Countdown) with { SourceId = item.Id };
        _widgetHost.ShowWidget(state);
        ShowStatus("已添加到桌面");
    }

    private async void OnZoomDeleteRequested(object? sender, EventArgs e)
    {
        if (Zoom.Item is not { } item)
        {
            return;
        }

        Zoom.Close();
        var owner = Window.GetWindow(this);
        var confirm = MessageBox.Show(
            owner,
            $"将删除“{item.Name}”，删除后不可恢复，是否继续？",
            "删除倒计时",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _countdowns.DeleteAsync(new[] { item.Id });
            ShowStatus("已删除该倒计时");
        }
        catch (Exception ex)
        {
            ShowStatus($"删除失败：{ex.Message}");
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await EditAsync(null);
        }
        catch (Exception ex)
        {
            ShowStatus($"新建倒计时失败：{ex.Message}");
        }
    }

    private async Task EditAsync(CountdownItem? item)
    {
        var dialog = new CountdownEditorWindow(
            item,
            DateOnly.FromDateTime(DateTime.Now),
            DefaultReminder())
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
        {
            return;
        }

        try
        {
            await _countdowns.AddOrUpdateAsync(result);
            ShowStatus(item is null ? "已新增倒计时" : "已保存修改");
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "无法保存倒计时",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private ReminderOffset DefaultReminder()
    {
        var key = _settings.Get(PreferenceKeys.CountdownReminder, PreferenceDefaults.CountdownReminder);
        return key switch
        {
            "none" => ReminderOffset.None,
            "ontime" => ReminderOffset.OnTime,
            "m5" => ReminderOffset.Minutes5,
            "m15" => ReminderOffset.Minutes15,
            "m30" => ReminderOffset.Minutes30,
            "h1" => ReminderOffset.Hour1,
            _ => ReminderOffset.Day1,
        };
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
