using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 区域截图选区覆盖层：每个显示器一张置顶分层窗口，
/// 用 UpdateLayeredWindow 提供逐像素 Alpha——选区外半透明压暗、
/// 选区内完全透亮、白色边框与尺寸标签。鼠标输入经 SetCapture 路由到
/// 按下时所在的窗口，坐标统一取 GetCursorPos 物理像素（虚拟屏坐标，
/// 支持负坐标多显示器）。在专用 STA 线程上运行独立消息循环。
/// </summary>
internal static class RegionSelectionOverlay
{
    /// <summary>有效选区的最小物理像素边长；小于该值视为误触。</summary>
    public const int MinimumSelection = 8;

    /// <summary>运行选区流程；阻塞直到用户确认或取消。返回虚拟屏物理像素矩形，取消返回 null。</summary>
    public static ScreenNativeMethods.NativeRect? Run(CancellationToken cancellationToken = default)
    {
        ScreenNativeMethods.NativeRect? result = null;
        using var done = new ManualResetEventSlim(false);
        SelectionContext? context = null;

        var thread = new Thread(() =>
        {
            try
            {
                context = new SelectionContext(rect => result = rect);
                Application.Run(context);
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

        // 取消时关闭覆盖层窗体，避免 STA 线程与置顶窗口残留。
        using var registration = cancellationToken.Register(() => context?.Cancel());
        done.Wait(cancellationToken);
        return result;
    }

    private sealed class SelectionContext : ApplicationContext
    {
        private readonly List<SelectionForm> _forms = [];
        private readonly Action<ScreenNativeMethods.NativeRect?> _complete;
        private readonly System.Windows.Forms.Timer _releasePoll;
        private ScreenNativeMethods.NativePoint _start;
        private ScreenNativeMethods.NativeRect? _selection;
        private bool _dragging;
        private bool _pressActive;
        private bool _completed;
        private long _pressStartedAt;

        public SelectionContext(Action<ScreenNativeMethods.NativeRect?> complete)
        {
            _complete = complete;

            // 分层窗口选区内 alpha=0 可能让 WM_LBUTTONUP 丢失；
            // 以 25ms 轮询左键状态作为"松开即完成"的兜底（Greenshot/ShareX 同类做法）。
            _releasePoll = new System.Windows.Forms.Timer { Interval = 25 };
            _releasePoll.Tick += (_, _) => PollForRelease();
            _releasePoll.Start();

            foreach (var monitor in ScreenNativeMethods.EnumMonitorRects())
            {
                var form = new SelectionForm(monitor, this);
                _forms.Add(form);
                form.Show();
            }

            if (_forms.Count == 0)
            {
                Complete(null);
            }
            else
            {
                RenderAll();
            }
        }

        /// <summary>外部取消：把关闭动作切到窗体的 UI 线程，按“取消”收尾。</summary>
        public void Cancel()
        {
            if (_completed)
            {
                return;
            }

            var form = _forms.FirstOrDefault();
            if (form is { IsHandleCreated: true } && !form.IsDisposed)
            {
                form.BeginInvoke(new Action(() => Complete(null)));
                return;
            }

            Complete(null);
        }

        public void HandleMouseDown(ScreenNativeMethods.NativePoint point)
        {
            _dragging = true;
            _pressActive = true;
            _pressStartedAt = Environment.TickCount64;
            _start = point;
            _selection = null;
            RenderAll();
        }

        public void HandleMouseMove(ScreenNativeMethods.NativePoint point)
        {
            if (!_dragging)
            {
                return;
            }

            _selection = Normalize(_start, point);
            RenderAll();
        }

        /// <summary>
        /// 松手即完成：鼠标抬起消息与轮询兜底共用同一收敛逻辑。
        /// Enter 仅用于"先松手、再微调"的可选确认，不再是必需操作。
        /// </summary>
        public void HandleMouseUp(ScreenNativeMethods.NativePoint point) => FinishPress(point);

        /// <summary>轮询兜底：左键已松开但 WM_LBUTTONUP 未送达时主动收敛。</summary>
        private void PollForRelease()
        {
            if (!_pressActive || _completed || ScreenNativeMethods.IsLeftButtonDown())
            {
                return;
            }

            // 避免按下当帧的误判，要求至少 80ms 按压。
            if (Environment.TickCount64 - _pressStartedAt < 80)
            {
                return;
            }

            _ = ScreenNativeMethods.GetCursorPos(out var cursor);
            FinishPress(cursor);
        }

        private void FinishPress(ScreenNativeMethods.NativePoint endPoint)
        {
            if (!_pressActive || _completed)
            {
                return;
            }

            _pressActive = false;
            _dragging = false;
            _selection = Normalize(_start, endPoint);

            if (IsValid(_selection))
            {
                Complete(_selection);
            }
            else
            {
                // 过小视为误触：清空选区，允许重新拖拽。
                _selection = null;
                RenderAll();
            }
        }

        public void HandleCancel() => Complete(null);

        public void HandleConfirm()
        {
            if (IsValid(_selection))
            {
                Complete(_selection);
            }
        }

        /// <summary>系统夺走鼠标捕获：终止拖拽但保持覆盖层等待重新操作。</summary>
        public void AbortDrag()
        {
            if (_completed)
            {
                return;
            }

            _pressActive = false;
            _dragging = false;
            _selection = null;
            RenderAll();
        }

        public void RenderAll()
        {
            foreach (var form in _forms)
            {
                form.Render(_selection);
            }
        }

        private static bool IsValid(ScreenNativeMethods.NativeRect? selection)
            => selection is { } value
                && value.Width >= MinimumSelection
                && value.Height >= MinimumSelection;

        private static ScreenNativeMethods.NativeRect Normalize(
            ScreenNativeMethods.NativePoint start,
            ScreenNativeMethods.NativePoint end)
            => new(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));

        private void Complete(ScreenNativeMethods.NativeRect? rect)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _pressActive = false;
            _releasePoll.Stop();
            _releasePoll.Dispose();
            _complete(rect);

            foreach (var form in _forms)
            {
                form.Close();
            }

            ExitThread();
        }
    }

    private sealed class SelectionForm : Form
    {
        private const long WsExLayered = 0x00080000L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExTopmost = 0x00000008L;

        private readonly ScreenNativeMethods.NativeRect _monitor;
        private readonly SelectionContext _context;

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

        public SelectionForm(ScreenNativeMethods.NativeRect monitor, SelectionContext context)
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
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            DestroyDeviceSurface();
            base.OnHandleDestroyed(e);
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
                Height = -_monitor.Height, // 自上而下
                Planes = 1,
                BitCount = 32,
                Compression = 0, // BI_RGB
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

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case ScreenNativeMethods.WmLButtonDown:
                    ScreenNativeMethods.SetCapture(Handle);
                    _context.HandleMouseDown(CursorPosition());
                    return;

                case ScreenNativeMethods.WmMouseMove:
                    _context.HandleMouseMove(CursorPosition());
                    return;

                case ScreenNativeMethods.WmLButtonUp:
                    // 先收敛业务（松手即完成），再释放捕获；
                    // 否则 ReleaseCapture 触发的 WM_CAPTURECHANGED 会清空拖拽状态。
                    _context.HandleMouseUp(CursorPosition());
                    ScreenNativeMethods.ReleaseCapture();
                    return;

                case ScreenNativeMethods.WmRButtonUp:
                    _context.HandleCancel();
                    return;

                case ScreenNativeMethods.WmKeyDown:
                    var key = m.WParam.ToInt64();
                    if (key == ScreenNativeMethods.VkEscape)
                    {
                        _context.HandleCancel();
                    }
                    else if (key == ScreenNativeMethods.VkReturn)
                    {
                        _context.HandleConfirm();
                    }
                    return;

                case ScreenNativeMethods.WmCaptureChanged:
                    // 仅当左键仍按下（外部夺走捕获）才中止；
                    // 松手后由 ReleaseCapture 引发的该消息不应破坏已完成的选区。
                    if (ScreenNativeMethods.IsLeftButtonDown())
                    {
                        _context.AbortDrag();
                    }
                    return;
            }

            base.WndProc(ref m);
        }

        private static ScreenNativeMethods.NativePoint CursorPosition()
        {
            _ = ScreenNativeMethods.GetCursorPos(out var point);
            return point;
        }

        /// <summary>把当前选区渲染到分层窗口：压暗 + 透明选区 + 边框 + 标签。</summary>
        public void Render(ScreenNativeMethods.NativeRect? selection)
        {
            if (IsDisposed || _memoryDc == IntPtr.Zero || _bitmap == IntPtr.Zero)
            {
                return;
            }

            var width = _monitor.Width;
            var height = _monitor.Height;

            // 选区换算到本显示器局部坐标并裁剪。
            var left = 0;
            var top = 0;
            var right = -1;
            var bottom = -1;
            if (selection is { } value)
            {
                left = Math.Max(0, value.Left - _monitor.Left);
                top = Math.Max(0, value.Top - _monitor.Top);
                right = Math.Min(width, value.Right - _monitor.Left);
                bottom = Math.Min(height, value.Bottom - _monitor.Top);
            }

            using var graphics = Graphics.FromHdc(_memoryDc);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var dim = new SolidBrush(Color.FromArgb(89, 0, 0, 0)))
            {
                graphics.FillRectangle(dim, 0, 0, width, height);
            }

            if (right > left && bottom > top)
            {
                using (var clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                {
                    graphics.FillRectangle(clear, left, top, right - left, bottom - top);
                }

                graphics.CompositingMode = CompositingMode.SourceOver;
                DrawSelectionChrome(graphics, left, top, right, bottom, selection!.Value);
            }
            else
            {
                graphics.CompositingMode = CompositingMode.SourceOver;
                DrawHint(graphics, width, height);
            }

            PushToDevice(width, height);
        }

        private static void DrawSelectionChrome(
            Graphics graphics,
            int left,
            int top,
            int right,
            int bottom,
            ScreenNativeMethods.NativeRect selection)
        {
            var rect = Rectangle.FromLTRB(left, top, right, bottom);

            using (var contrast = new Pen(Color.FromArgb(210, 0, 0, 0), 3f))
            {
                graphics.DrawRectangle(contrast, rect);
            }

            using (var border = new Pen(Color.White, 1.6f))
            {
                graphics.DrawRectangle(border, rect);
            }

            var label = $"{selection.Width} × {selection.Height} px";
            using var font = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            DrawLabel(graphics, label, font, left, top - 30, bottom);
        }

        private static void DrawLabel(
            Graphics graphics,
            string text,
            Font font,
            int preferredLeft,
            int preferredTop,
            int selectionBottom)
        {
            var size = graphics.MeasureString(text, font);
            var width = (int)size.Width;
            var height = (int)size.Height;

            var x = Math.Max(6, Math.Min(preferredLeft, (int)graphics.VisibleClipBounds.Width - width - 12));
            var y = preferredTop < 6 ? selectionBottom + 8 : preferredTop;
            y = Math.Max(6, Math.Min(y, (int)graphics.VisibleClipBounds.Height - height - 6));

            using var chip = new SolidBrush(Color.FromArgb(190, 16, 16, 16));
            graphics.FillRectangle(chip, x - 6, y - 3, width + 12, height + 6);
            graphics.DrawString(text, font, Brushes.White, x, y);
        }

        private static void DrawHint(Graphics graphics, int width, int height)
        {
            var hint = "拖拽框选 · 松开鼠标即完成 · Esc 或右键取消";
            using var font = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
            var size = graphics.MeasureString(hint, font);

            var x = (width - size.Width) / 2;
            var y = (height / 2) - 70;

            using var chip = new SolidBrush(Color.FromArgb(200, 16, 16, 16));
            graphics.FillRectangle(chip, x - 14, y - 8, size.Width + 28, size.Height + 16);
            graphics.DrawString(hint, font, Brushes.White, x, y);
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
}
