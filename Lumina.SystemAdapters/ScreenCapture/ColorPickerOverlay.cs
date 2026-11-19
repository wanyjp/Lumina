using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Lumina.Domain.Colors;
using Lumina.Services.Contracts;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 全屏取色覆盖层：进入时先抓取虚拟屏快照，每显示器一张置顶分层窗口，
/// 半透明压暗并以放大镜跟随光标，单击返回 sRGB 颜色，Esc / 右键取消。
/// <para>
/// 颜色统一从<b>快照</b>读取，而不是从屏幕实时像素读取：覆盖层本身带有压暗与放大镜，
/// 直接读屏幕会把覆盖层自身混进去。快照方案同时保证多屏、负坐标与高刷新下的稳定性。
/// </para>
/// </summary>
internal static class ColorPickerOverlay
{
    /// <summary>放大镜取样的边长（物理像素）。</summary>
    public const int SampleCells = 15;

    /// <summary>放大镜每个像素的绘制边长（物理像素）。</summary>
    public const int CellSize = 12;

    /// <summary>运行取色流程；阻塞直到用户拾取或取消。取消返回 null。</summary>
    public static ColorSample? Run(CancellationToken cancellationToken = default)
    {
        ColorSample? result = null;
        using var done = new ManualResetEventSlim(false);
        PickerContext? context = null;

        var thread = new Thread(() =>
        {
            try
            {
                context = new PickerContext(sample => result = sample);
                using var owned = context;
                Application.Run(owned);
            }
            catch (Exception)
            {
                result = null;
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        using var registration = cancellationToken.Register(() => context?.Cancel());
        done.Wait(cancellationToken);
        return result;
    }

    /// <summary>外部取消：把关闭动作切到覆盖层 UI 线程。</summary>
    private static void CancelOnUiThread(IReadOnlyList<Form> forms, Action complete)
    {
        var form = forms.FirstOrDefault();
        if (form is { IsHandleCreated: true } && !form.IsDisposed)
        {
            form.BeginInvoke(complete);
            return;
        }

        complete();
    }

    private sealed class PickerContext : ApplicationContext
    {
        private readonly List<PickerForm> _forms = [];
        private readonly Action<ColorSample?> _complete;
        private readonly ScreenSnapshot _snapshot;
        private System.Windows.Forms.Timer? _timer;
        private bool _completed;
        private bool _escapeWasDown;

        /// <summary>外部取消：把关闭动作切到覆盖层 UI 线程。</summary>
        public void Cancel() => CancelOnUiThread(_forms, () => Complete(null));

        public PickerContext(Action<ColorSample?> complete)
        {
            _complete = complete;
            _snapshot = ScreenSnapshot.Capture();

            foreach (var monitor in ScreenNativeMethods.EnumMonitorRects())
            {
                var form = new PickerForm(monitor, this);
                _forms.Add(form);
                form.Show();
            }

            if (_forms.Count == 0)
            {
                Complete(null);
                return;
            }

            _timer = new System.Windows.Forms.Timer { Interval = 30 };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            Tick();
        }

        /// <summary>左键抬起即拾取当前光标位置的颜色。</summary>
        public void HandleClick()
        {
            if (_completed || !ScreenNativeMethods.GetCursorPos(out var point))
            {
                return;
            }

            Complete(_snapshot.Sample(point.X, point.Y));
        }

        public void HandleCancel() => Complete(null);

        private void Tick()
        {
            if (_completed)
            {
                return;
            }

            // Esc 用异步键状态轮询：分层窗口不保证持有键盘焦点。
            var escapeDown = (ScreenNativeMethods.GetAsyncKeyState(ScreenNativeMethods.VkEscape) & 0x8000) != 0;
            if (escapeDown && !_escapeWasDown)
            {
                Complete(null);
                return;
            }

            _escapeWasDown = escapeDown;

            if (!ScreenNativeMethods.GetCursorPos(out var point))
            {
                return;
            }

            foreach (var form in _forms)
            {
                form.Render(point, _snapshot);
            }
        }

        private void Complete(ColorSample? sample)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _complete(sample);
            _snapshot.Dispose();

            foreach (var form in _forms)
            {
                form.Close();
            }

            ExitThread();
        }
    }

    private sealed class PickerForm : Form
    {
        private const long WsExLayered = 0x00080000L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExTopmost = 0x00000008L;

        private readonly ScreenNativeMethods.NativeRect _monitor;
        private readonly PickerContext _context;
        private readonly string _hint = "移动鼠标取色 · 单击拾取 · Esc / 右键取消";

        private IntPtr _memoryDc;
        private IntPtr _bitmap;
        private IntPtr _previousObject;
        private IntPtr _bits;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BitmapInfoHeader header,
            uint usage,
            out IntPtr bits,
            IntPtr file,
            uint offset);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        public PickerForm(ScreenNativeMethods.NativeRect monitor, PickerContext context)
        {
            _monitor = monitor;
            _context = context;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = new Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
            Cursor = Cursors.Cross;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(WsExLayered | WsExToolWindow | WsExTopmost);
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CreateDeviceSurface();
            Activate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            DestroyDeviceSurface();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case ScreenNativeMethods.WmLButtonUp:
                    _context.HandleClick();
                    return;

                case ScreenNativeMethods.WmRButtonUp:
                    _context.HandleCancel();
                    return;
            }

            base.WndProc(ref m);
        }

        private void CreateDeviceSurface()
        {
            var screenDc = ScreenNativeMethods.GetDC(IntPtr.Zero);
            _memoryDc = ScreenNativeMethods.CreateCompatibleDC(screenDc);
            ScreenNativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            if (_memoryDc == IntPtr.Zero)
            {
                return;
            }

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = _monitor.Width,
                Height = -_monitor.Height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            };

            _bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap != IntPtr.Zero)
            {
                _previousObject = ScreenNativeMethods.SelectObject(_memoryDc, _bitmap);
            }
        }

        private void DestroyDeviceSurface()
        {
            if (_memoryDc != IntPtr.Zero)
            {
                if (_previousObject != IntPtr.Zero)
                {
                    ScreenNativeMethods.SelectObject(_memoryDc, _previousObject);
                    _previousObject = IntPtr.Zero;
                }

                ScreenNativeMethods.DeleteDC(_memoryDc);
                _memoryDc = IntPtr.Zero;
            }

            if (_bitmap != IntPtr.Zero)
            {
                ScreenNativeMethods.DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
            }
        }

        /// <summary>绘制压暗遮罩与放大镜，最后整屏推送到分层窗口。</summary>
        public void Render(ScreenNativeMethods.NativePoint cursor, ScreenSnapshot snapshot)
        {
            if (IsDisposed || _memoryDc == IntPtr.Zero || _bitmap == IntPtr.Zero)
            {
                return;
            }

            var width = _monitor.Width;
            var height = _monitor.Height;
            var localX = cursor.X - _monitor.Left;
            var localY = cursor.Y - _monitor.Top;
            var active = localX >= 0 && localY >= 0 && localX < width && localY < height;

            using var graphics = Graphics.FromHdc(_memoryDc);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            using (var dim = new SolidBrush(Color.FromArgb(78, 0, 0, 0)))
            {
                graphics.FillRectangle(dim, 0, 0, width, height);
            }

            graphics.CompositingMode = CompositingMode.SourceOver;

            if (active)
            {
                DrawCrosshair(graphics, localX, localY, width, height);
                DrawMagnifier(graphics, snapshot, cursor, localX, localY, width, height);
            }

            DrawHint(graphics, width, height);
            PushToDevice(width, height);
        }

        private static void DrawCrosshair(Graphics graphics, int x, int y, int width, int height)
        {
            using var horizontal = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
            using var vertical = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
            graphics.DrawLine(horizontal, 0, y, width, y);
            graphics.DrawLine(vertical, x, 0, x, height);
        }

        private static void DrawMagnifier(
            Graphics graphics,
            ScreenSnapshot snapshot,
            ScreenNativeMethods.NativePoint cursor,
            int localX,
            int localY,
            int screenWidth,
            int screenHeight)
        {
            var cells = ColorPickerOverlay.SampleCells;
            var cell = ColorPickerOverlay.CellSize;
            var gridSize = cells * cell;
            const int padding = 12;
            const int infoHeight = 86;
            var panelWidth = gridSize + padding * 2;
            var panelHeight = gridSize + padding * 2 + infoHeight;

            var x = localX + 26;
            var y = localY + 26;
            if (x + panelWidth > screenWidth - 8)
            {
                x = localX - panelWidth - 26;
            }

            if (y + panelHeight > screenHeight - 8)
            {
                y = localY - panelHeight - 26;
            }

            x = Math.Clamp(x, 8, Math.Max(8, screenWidth - panelWidth - 8));
            y = Math.Clamp(y, 8, Math.Max(8, screenHeight - panelHeight - 8));

            var panel = new Rectangle(x, y, panelWidth, panelHeight);
            using (var background = new SolidBrush(Color.FromArgb(242, 18, 18, 20)))
            {
                graphics.FillRectangle(background, panel);
            }

            using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1f))
            {
                graphics.DrawRectangle(border, panel);
            }

            var grid = new Rectangle(x + padding, y + padding, gridSize, gridSize);
            var half = cells / 2;
            var originX = cursor.X - half;
            var originY = cursor.Y - half;

            for (var row = 0; row < cells; row++)
            {
                for (var column = 0; column < cells; column++)
                {
                    var sample = snapshot.Sample(originX + column, originY + row);
                    using var brush = new SolidBrush(Color.FromArgb(sample.R, sample.G, sample.B));
                    graphics.FillRectangle(
                        brush,
                        grid.Left + column * cell,
                        grid.Top + row * cell,
                        cell,
                        cell);
                }
            }

            using (var gridLine = new Pen(Color.FromArgb(36, 0, 0, 0), 1f))
            {
                for (var index = 1; index < cells; index++)
                {
                    graphics.DrawLine(
                        gridLine,
                        grid.Left + index * cell,
                        grid.Top,
                        grid.Left + index * cell,
                        grid.Bottom);
                    graphics.DrawLine(
                        gridLine,
                        grid.Left,
                        grid.Top + index * cell,
                        grid.Right,
                        grid.Top + index * cell);
                }
            }

            var center = new Rectangle(
                grid.Left + half * cell,
                grid.Top + half * cell,
                cell,
                cell);
            using (var focusBorder = new Pen(Color.White, 2f))
            {
                graphics.DrawRectangle(focusBorder, center);
            }

            var sampleAtCursor = snapshot.Sample(cursor.X, cursor.Y);
            using var titleFont = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using (var swatch = new SolidBrush(Color.FromArgb(sampleAtCursor.R, sampleAtCursor.G, sampleAtCursor.B)))
            {
                graphics.FillRectangle(
                    swatch,
                    grid.Left,
                    grid.Bottom + 10,
                    28,
                    28);
            }

            using (var swatchBorder = new Pen(Color.FromArgb(160, 255, 255, 255), 1f))
            {
                graphics.DrawRectangle(
                    swatchBorder,
                    grid.Left,
                    grid.Bottom + 10,
                    28,
                    28);
            }

            graphics.DrawString(
                sampleAtCursor.Hex,
                titleFont,
                Brushes.White,
                grid.Left + 36,
                grid.Bottom + 8);
            graphics.DrawString(
                $"RGB({sampleAtCursor.R}, {sampleAtCursor.G}, {sampleAtCursor.B})   ({sampleAtCursor.X}, {sampleAtCursor.Y})",
                bodyFont,
                Brushes.Gainsboro,
                grid.Left,
                grid.Bottom + 42);
            graphics.DrawString(
                "单击拾取",
                bodyFont,
                Brushes.Silver,
                grid.Left,
                grid.Bottom + 60);
        }

        private void DrawHint(Graphics graphics, int width, int height)
        {
            using var font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            var size = graphics.MeasureString(_hint, font);
            var x = (width - size.Width) / 2;
            const int y = 24;

            using var chip = new SolidBrush(Color.FromArgb(200, 16, 16, 18));
            graphics.FillRectangle(chip, x - 14, y - 8, size.Width + 28, size.Height + 16);
            graphics.DrawString(_hint, font, Brushes.White, x, y);
        }

        private void PushToDevice(int width, int height)
        {
            var screenDc = ScreenNativeMethods.GetDC(IntPtr.Zero);
            try
            {
                var position = new ScreenNativeMethods.NativePoint(_monitor.Left, _monitor.Top);
                var size = new ScreenNativeMethods.NativeSize(width, height);
                var source = new ScreenNativeMethods.NativePoint(0, 0);
                var blend = new ScreenNativeMethods.BlendFunction
                {
                    BlendOp = ScreenNativeMethods.AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = ScreenNativeMethods.AcSrcAlpha,
                };

                _ = ScreenNativeMethods.UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref position,
                    ref size,
                    _memoryDc,
                    ref source,
                    0,
                    ref blend,
                    ScreenNativeMethods.UlwAlpha);
            }
            finally
            {
                ScreenNativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>虚拟屏快照；像素按 BGRA 存放，统一按 sRGB 直接读取。</summary>
    private sealed class ScreenSnapshot : IDisposable
    {
        private readonly byte[] _pixels;
        private readonly int _stride;
        private readonly ScreenNativeMethods.NativeRect _bounds;

        private ScreenSnapshot(
            byte[] pixels,
            int stride,
            ScreenNativeMethods.NativeRect bounds)
        {
            _pixels = pixels;
            _stride = stride;
            _bounds = bounds;
        }

        public static ScreenSnapshot Capture()
        {
            var bounds = ScreenNativeMethods.VirtualScreen();
            var width = Math.Max(1, bounds.Width);
            var height = Math.Max(1, bounds.Height);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    new Size(width, height),
                    CopyPixelOperation.SourceCopy);
            }

            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var pixels = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return new ScreenSnapshot(pixels, data.Stride, bounds);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>读取虚拟屏坐标处的颜色；越界按边缘钳制，避免放大镜边缘出现黑边。</summary>
        public ColorSample Sample(int x, int y)
        {
            var (red, green, blue) = PixelSampler.SampleBgra(
                _pixels,
                _stride,
                _bounds.Width,
                _bounds.Height,
                x - _bounds.Left,
                y - _bounds.Top);

            return new ColorSample(red, green, blue, x, y);
        }

        public void Dispose()
        {
            // 像素已复制到托管数组，无原生资源需要释放。
        }
    }
}
