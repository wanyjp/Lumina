using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.UI.Wpf.Countdown;

/// <summary>
/// 倒计时放大态：背景遮罩 + 卡片放大回弹，右侧编辑 / 添加到桌面 / 删除三个圆形按钮。
/// 点遮罩或 Esc 关闭；按钮带 ToolTip 与无障碍名称，键盘可聚焦。
/// </summary>
public partial class CountdownZoomOverlay : UserControl
{
    private Window? _owner;

    public CountdownZoomOverlay()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public event EventHandler? EditRequested;

    public event EventHandler? DesktopRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? Closed;

    public CountdownItem? Item { get; private set; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(CountdownItem item, UrgencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(snapshot);

        Item = item;

        CategoryText.Text = CountdownText.CategoryLabel(item.Category);
        CategoryIcon.Data = CountdownVisuals.CategoryGeometry(item.Category);
        BuildLabel(item, snapshot);
        NumberText.Text = CountdownText.Number(snapshot);
        NumberUnit.Visibility = snapshot.NextOccurrence is null ? Visibility.Collapsed : Visibility.Visible;
        SemanticText.Text = CountdownText.Semantic(item, snapshot);
        RemainingText.Text = CountdownText.Remaining(snapshot);
        NoteText.Text = CountdownText.UrgencyNote(snapshot.Level);

        var brushKey = CountdownVisuals.UrgencyBrushKey(snapshot.Level);
        NumberText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        Progress.SetResourceReference(ProgressBar.ForegroundProperty, brushKey);
        Progress.Value = Math.Round(snapshot.ProgressRatio * 100);

        Visibility = Visibility.Visible;
        AttachOwner();
        StartOpenAnimation();
        AnimateActions();
        EditButton.Focus();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        DetachOwner();

        var duration = Motion("Theme.Motion.Fast", 130);
        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            ResetVisuals();
            return;
        }

        var fade = new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = EaseOut(),
        };
        fade.Completed += (_, _) => ResetVisuals();
        Card.BeginAnimation(OpacityProperty, fade);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.94, duration));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.94, duration));
    }

    private void ResetVisuals()
    {
        Card.BeginAnimation(OpacityProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Card.Opacity = 1;
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void StartOpenAnimation()
    {
        var duration = Motion("Theme.Motion.Normal", 180);
        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            Card.Opacity = 1;
            CardScale.ScaleX = 1;
            CardScale.ScaleY = 1;
            return;
        }

        Card.Opacity = 0;
        CardScale.ScaleX = 0.86;
        CardScale.ScaleY = 0.86;

        var ease = EaseOut();
        Card.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var bounce = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
        CardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.86, 1, duration) { EasingFunction = bounce });
        CardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.86, 1, duration) { EasingFunction = bounce });
    }

    /// <summary>三个操作按钮自下而上依次出现，和卡片放大同步。</summary>
    private void AnimateActions()
    {
        var duration = Motion("Theme.Motion.Normal", 180);
        if (duration.TimeSpan <= TimeSpan.Zero)
        {
            foreach (var button in new[] { EditButton, DesktopButton, DeleteButton })
            {
                button.Opacity = 1;
                button.RenderTransform = null;
            }

            return;
        }

        var buttons = new[] { EditButton, DesktopButton, DeleteButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            var delay = TimeSpan.FromMilliseconds(45 * index);
            button.Opacity = 0;
            var translate = new TranslateTransform(12, 0);
            button.RenderTransform = translate;

            var ease = EaseOut();
            button.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = ease });
            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(12, 0, duration) { BeginTime = delay, EasingFunction = ease });
        }
    }

    private void AttachOwner()
    {
        DetachOwner();
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.PreviewKeyDown += OnOwnerPreviewKeyDown;
        }
    }

    private void DetachOwner()
    {
        if (_owner is not null)
        {
            _owner.PreviewKeyDown -= OnOwnerPreviewKeyDown;
            _owner = null;
        }
    }

    private void OnOwnerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsOpen || e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    /// <summary>放大态主文案：保留“今天就是 1 / 距离 2 还有”，名称加粗放大。</summary>
    private void BuildLabel(CountdownItem item, UrgencySnapshot snapshot)
    {
        LabelText.Inlines.Clear();
        var (prefix, name, suffix) = CountdownText.LabelParts(item, snapshot);

        if (prefix.Length > 0)
        {
            LabelText.Inlines.Add(new Run(prefix));
        }

        if (name.Length > 0)
        {
            var nameRun = new Run(name)
            {
                FontWeight = FontWeights.Bold,
                FontSize = 24,
            };
            nameRun.SetResourceReference(TextElement.ForegroundProperty, "Theme.TextBrush");
            LabelText.Inlines.Add(nameRun);
        }

        if (suffix.Length > 0)
        {
            LabelText.Inlines.Add(new Run(suffix));
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e) =>
        EditRequested?.Invoke(this, EventArgs.Empty);

    private void OnDesktopClick(object sender, RoutedEventArgs e) =>
        DesktopRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClick(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachOwner();

    private Duration Motion(string key, double fallbackMilliseconds) =>
        TryFindResource(key) is Duration duration
            ? duration
            : new Duration(TimeSpan.FromMilliseconds(fallbackMilliseconds));

    private IEasingFunction EaseOut() =>
        TryFindResource("Theme.EaseOut") as IEasingFunction
        ?? new CubicEase { EasingMode = EasingMode.EaseOut };
}
