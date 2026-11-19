using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Lumina.Domain.Enums;

namespace Lumina.Widgets;

/// <summary>
/// 组件卡片的实时模糊底层：显示抓屏得到的桌面画面并做高斯模糊，按卡片圆角裁剪。
/// 位于卡片背后，不参与命中测试，因此不影响拖动、点击穿透与圆角。
/// 表面质感由三层叠加：扩散后的背景、极细噪点（磨砂颗粒）与边缘反光。
/// </summary>
public sealed class WidgetGlassSurface : Border
{
    private readonly Grid _root;
    private readonly Image _image;
    private readonly BlurEffect _blur;
    private WriteableBitmap? _backdrop;

    public WidgetGlassSurface()
    {
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        SnapsToDevicePixels = true;

        _blur = new BlurEffect
        {
            Radius = 14,
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance,
        };

        _image = new Image
        {
            Stretch = Stretch.Fill,
            Effect = _blur,
            IsHitTestVisible = false,
        };

        // 先在小图上做高斯模糊、再由 Viewbox 放大：分层窗口走软件合成，
        // 模糊代价与像素数成正比，按 240px 原尺寸模糊能省下约三分之二的 CPU。
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Fill,
            Child = _image,
            // 抓屏比卡片四周各大 42 DIP，负边距让模糊边缘落在裁剪区外。
            Margin = new Thickness(-42),
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(viewbox, BitmapScalingMode.Linear);

        // 磨砂颗粒：极低透明度的明暗斑点，避免大面积模糊看起来像纯色块。
        // 用预渲染的噪点位图平铺，避免每帧重新栅格化上千个几何图形。
        var grain = new Border
        {
            Background = CreateGrainBrush(),
            IsHitTestVisible = false,
            Opacity = 0.18,
        };

        _root = new Grid();
        _root.Children.Add(viewbox);
        _root.Children.Add(grain);
        Child = _root;

        SizeChanged += (_, _) => UpdateClip();
        Loaded += (_, _) => UpdateClip();
    }

    public bool IsActive => Visibility == Visibility.Visible;

    /// <summary>是否已经有一帧背景可显示（用于跳过重复上传）。</summary>
    public bool HasFrame => _backdrop is not null && Visibility == Visibility.Visible;

    /// <summary>
    /// 逐帧捕获模式下由像素级降采样 + 双线性放大完成柔化，关掉高斯模糊可以省下
    /// 分层窗口软件合成里最贵的一笔开销。
    /// </summary>
    public bool BlurEnabled
    {
        get => _image.Effect is not null;
        set => _image.Effect = value ? _blur : null;
    }

    /// <summary>应用皮肤对应的模糊半径；不改变当前画面。</summary>
    public void ApplySkin(WidgetSkin skin)
    {
        _blur.Radius = skin == WidgetSkin.LiquidGlass ? 9 : 7;
        if (Visibility == Visibility.Visible)
        {
            UpdateClip();
        }
    }

    /// <summary>
    /// 写入一帧背景像素。内部复用同一个 <see cref="WriteableBitmap"/>，
    /// 高频采样时只有像素上传开销，不会每帧分配新的位图。
    /// </summary>
    public void SetBackdrop(byte[] pixels, int width, int height, WidgetSkin skin)
    {
        if (pixels is null || width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            Clear();
            return;
        }

        if (_backdrop is null
            || _backdrop.PixelWidth != width
            || _backdrop.PixelHeight != height)
        {
            _backdrop = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _image.Source = _backdrop;
        }

        ApplySkin(skin);
        _backdrop.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        Visibility = Visibility.Visible;
        UpdateClip();
    }

    public void Clear()
    {
        _image.Source = null;
        Visibility = Visibility.Collapsed;
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var radius = CornerRadius.TopLeft;
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
    }

    /// <summary>48×48 平铺的噪点笔刷，明暗各半，整体透明度很低。</summary>
    private static Brush CreateGrainBrush()
    {
        const int tile = 48;
        const int stride = tile * 4;
        var pixels = new byte[stride * tile];
        var random = new Random(20261112);
        for (var i = 0; i < tile * tile; i++)
        {
            var alpha = (byte)(4 + random.Next(10));
            var white = random.Next(2) == 0;
            var index = i * 4;
            pixels[index] = white ? (byte)0xFF : (byte)0x00;
            pixels[index + 1] = white ? (byte)0xFF : (byte)0x00;
            pixels[index + 2] = white ? (byte)0xFF : (byte)0x00;
            pixels[index + 3] = alpha;
        }

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            tile,
            tile,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        var grain = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        grain.Freeze();
        return grain;
    }
}
