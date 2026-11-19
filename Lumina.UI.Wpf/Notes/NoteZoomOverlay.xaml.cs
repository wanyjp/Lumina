using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Services.Notes;

namespace Lumina.UI.Wpf.Notes;

/// <summary>
/// 便签放大态：遮罩 + 固定尺寸卡片（标题/操作固定，仅正文内部滚动），
/// 右侧编辑 / 添加到桌面 / 删除三个圆形按钮；点遮罩或 Esc 回弹。
/// </summary>
public partial class NoteZoomOverlay : UserControl
{
    private Window? _owner;

    public NoteZoomOverlay()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public event EventHandler? EditRequested;

    public event EventHandler? DesktopRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? Closed;

    public NoteItem? Item { get; private set; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(NoteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Item = item;
        TitleText.Text = string.IsNullOrWhiteSpace(item.Title) ? "无标题" : item.Title;
        TitleText.SetResourceReference(
            TextBlock.ForegroundProperty,
            string.IsNullOrWhiteSpace(item.Title) ? "Theme.TextTertiaryBrush" : "Theme.TextBrush");

        var palette = NotePalette.Resolve(item.Color);
        ColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Hex));

        var (total, done) = ChecklistText.Count(item.Content);
        MetaText.Text = total > 0
            ? $"更新于 {item.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · ☑ {done}/{total}"
            : $"更新于 {item.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

        BodyHost.Child = NotePreviewRenderer.Build(NoteMarkdown.Parse(item.Content), compact: false);

        Visibility = Visibility.Visible;
        AttachOwner();
        StartOpenAnimation();
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

        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = EaseOut() };
        fade.Completed += (_, _) => ResetVisuals();
        ZoomCard.BeginAnimation(OpacityProperty, fade);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.94, duration));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.94, duration));
    }

    private void ResetVisuals()
    {
        ZoomCard.BeginAnimation(OpacityProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ZoomCard.Opacity = 1;
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
            ZoomCard.Opacity = 1;
            CardScale.ScaleX = 1;
            CardScale.ScaleY = 1;
            return;
        }

        ZoomCard.Opacity = 0;
        CardScale.ScaleX = 0.9;
        CardScale.ScaleY = 0.9;

        ZoomCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut() });
        var bounce = new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut };
        CardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, duration) { EasingFunction = bounce });
        CardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, duration) { EasingFunction = bounce });
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

    private void OnEditClick(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this, EventArgs.Empty);

    private void OnDesktopClick(object sender, RoutedEventArgs e) => DesktopRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachOwner();

    private Duration Motion(string key, double fallbackMilliseconds) =>
        TryFindResource(key) is Duration duration
            ? duration
            : new Duration(TimeSpan.FromMilliseconds(fallbackMilliseconds));

    private IEasingFunction EaseOut() =>
        TryFindResource("Theme.EaseOut") as IEasingFunction
        ?? new CubicEase { EasingMode = EasingMode.EaseOut };
}
