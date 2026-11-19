using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Lumina.SystemAdapters.ScreenCapture;

/// <summary>
/// 延时截图倒计时覆盖层：主显示器居中的置顶小卡片，
/// 大数字显示剩余秒数，Esc 取消。在专用 STA 线程上运行。
/// 倒计时结束后由调用方等待覆盖层退场再执行全屏捕获。
/// </summary>
internal static class CountdownOverlay
{
    /// <summary>运行倒计时；返回 true 表示计时完成，false 表示用户取消。</summary>
    public static bool Run(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay <= TimeSpan.Zero)
        {
            return true;
        }

        var result = false;
        using var done = new ManualResetEventSlim(false);
        CountdownContext? context = null;

        var thread = new Thread(() =>
        {
            try
            {
                context = new CountdownContext(delay, value => result = value);
                Application.Run(context);
            }
            catch (Exception)
            {
                result = false;
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

    private sealed class CountdownContext : ApplicationContext
    {
        private readonly CountdownForm _form;

        /// <summary>外部取消：关闭倒计时卡片，按用户取消收尾。</summary>
        public void Cancel()
        {
            if (_form.IsHandleCreated && !_form.IsDisposed)
            {
                _form.BeginInvoke(new Action(_form.Close));
            }
        }

        public CountdownContext(TimeSpan delay, Action<bool> complete)
        {
            _form = new CountdownForm(delay, complete);
            _form.FormClosed += (_, _) => ExitThread();
            _form.Show();
        }
    }

    private sealed class CountdownForm : Form
    {
        private readonly DateTime _deadline;
        private readonly Action<bool> _complete;
        private readonly System.Windows.Forms.Timer _timer;
        private bool _finished;

        public CountdownForm(TimeSpan delay, Action<bool> complete)
        {
            _deadline = DateTime.UtcNow + delay;
            _complete = complete;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Bounds = BuildBounds();

            _timer = new System.Windows.Forms.Timer
            {
                Interval = 100,
            };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = new GraphicsPath();
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var radius = 18;
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - (radius * 2), rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - (radius * 2), rect.Bottom - (radius * 2), radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - (radius * 2), radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            using (var backdrop = new SolidBrush(Color.FromArgb(232, 18, 18, 22)))
            {
                graphics.FillPath(backdrop, path);
            }

            using (var border = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
            {
                graphics.DrawPath(border, path);
            }

            var remaining = Math.Max(0, (_deadline - DateTime.UtcNow).TotalSeconds);
            var seconds = (int)Math.Ceiling(remaining);

            using var numberFont = new Font("Segoe UI", 46f, FontStyle.Bold, GraphicsUnit.Pixel);
            var numberText = seconds.ToString();
            var numberSize = graphics.MeasureString(numberText, numberFont);
            var numberX = (Width - numberSize.Width) / 2;
            var numberY = (Height - numberSize.Height) / 2 - 14;

            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(numberText, numberFont, Brushes.White, numberX, numberY);

            using var captionFont = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var captionBrush = new SolidBrush(Color.FromArgb(190, 255, 255, 255));
            var caption = "秒后全屏截图 · Esc 取消";
            var captionSize = graphics.MeasureString(caption, captionFont);
            graphics.DrawString(
                caption,
                captionFont,
                captionBrush,
                (Width - captionSize.Width) / 2,
                numberY + numberSize.Height + 6);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Complete(false);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        private void OnTick()
        {
            if (DateTime.UtcNow >= _deadline)
            {
                Complete(true);
                return;
            }

            Invalidate();
        }

        private void Complete(bool value)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _timer.Stop();
            _complete(value);
            Close();
        }

        private Rectangle BuildBounds()
        {
            const int width = 200;
            const int height = 170;

            var primary = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, width, height);
            return new Rectangle(
                primary.Left + ((primary.Width - width) / 2),
                primary.Top + ((primary.Height - height) / 2),
                width,
                height);
        }
    }
}
