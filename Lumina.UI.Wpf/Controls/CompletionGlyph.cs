using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumina.Domain.Enums;

namespace Lumina.UI.Wpf.Controls;

/// <summary>
/// 完成状态图标：由 CompletionStyle 选择四套模板，Done 变化时播放 180ms 先快后慢动画；
/// 系统减少动画或用户关闭动画时瞬时切换。
/// </summary>
public sealed class CompletionGlyph : Control
{
    public static readonly DependencyProperty CompletionStyleProperty = DependencyProperty.Register(
        nameof(CompletionStyle),
        typeof(CompletionStyle),
        typeof(CompletionGlyph),
        new PropertyMetadata(CompletionStyle.Stroke));

    public static readonly DependencyProperty DoneProperty = DependencyProperty.Register(
        nameof(Done),
        typeof(bool),
        typeof(CompletionGlyph),
        new PropertyMetadata(false, OnDoneChanged));

    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(
        nameof(ReducedMotion),
        typeof(bool),
        typeof(CompletionGlyph),
        new PropertyMetadata(false, OnReducedMotionChanged));

    public CompletionStyle CompletionStyle
    {
        get => (CompletionStyle)GetValue(CompletionStyleProperty);
        set => SetValue(CompletionStyleProperty, value);
    }

    public bool Done
    {
        get => (bool)GetValue(DoneProperty);
        set => SetValue(DoneProperty, value);
    }

    public bool ReducedMotion
    {
        get => (bool)GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    private static void OnDoneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is CompletionGlyph glyph && glyph.IsLoaded && !glyph.ReducedMotion)
        {
            glyph.PlayTransition();
        }
    }

    private static void OnReducedMotionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not CompletionGlyph glyph || !(bool)e.NewValue)
        {
            return;
        }

        glyph.StopTransition();
    }

    private void PlayTransition()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);
        Opacity = 1;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        var scale = (ScaleTransform)RenderTransform;
        var scaleAnimation = new DoubleAnimation(0.88, 1, duration)
        {
            EasingFunction = ease,
        };
        var opacityAnimation = new DoubleAnimation(0.55, 1, duration)
        {
            EasingFunction = ease,
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void StopTransition()
    {
        if (RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        RenderTransform = Transform.Identity;
    }
}
