using System.Runtime.InteropServices;
using Lumina.Services.Compatibility;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Windows.Graphics.Capture;
using Windows.Media.Ocr;

namespace Lumina.SystemAdapters.Compatibility;

/// <summary>
/// Windows 运行环境探测：系统版本、进程 DPI 感知、显示器布局、OCR/音频/窗口捕获能力。
/// 所有系统调用都包在 try/catch 内，单项失败只降级该项，不影响启动。
/// </summary>
public sealed class WindowsCompatibilityProbe : ICompatibilityProbe
{
    private const int DpiTypeEffective = 0;
    private const int MonitorInfoExSize = 104;

    private readonly ILogger<WindowsCompatibilityProbe>? _logger;

    public WindowsCompatibilityProbe(ILogger<WindowsCompatibilityProbe>? logger = null)
    {
        _logger = logger;
    }

    public CompatibilitySnapshot Capture()
    {
        var build = ReadBuildNumber();
        var tier = CompatibilityAssessor.Classify(build);
        var displays = ReadDisplays();
        var dpiAwareness = ReadDpiAwareness();
        var (ocrAvailable, ocrLanguage) = ReadOcr();
        var (audioAvailable, audioDevice) = ReadAudio();
        var wgcAvailable = ReadWgc();

        var degradations = CompatibilityAssessor.EvaluateDegradations(
            tier,
            dpiAwareness,
            displays,
            ocrAvailable,
            audioAvailable,
            wgcAvailable);

        var snapshot = new CompatibilitySnapshot(
            RuntimeInformation.OSDescription,
            build,
            tier,
            dpiAwareness,
            displays,
            ocrAvailable,
            ocrLanguage,
            audioAvailable,
            audioDevice,
            wgcAvailable,
            degradations);

        // 摘要由 App 以 AppLog.CompatibilitySnapshot 统一记录，这里只补充降级明细，避免重复。
        foreach (var note in degradations)
        {
            _logger?.LogInformation("兼容性降级：{Note}", note);
        }

        return snapshot;
    }

    private static int ReadBuildNumber()
    {
        // 清单声明 supportedOS 后，Environment.OSVersion 返回真实版本，不会被兼容性垫片改写。
        var version = Environment.OSVersion.Version;
        return version.Build > 0 ? version.Build : 19041;
    }

    private static DpiAwarenessMode ReadDpiAwareness()
    {
        try
        {
            var context = GetThreadDpiAwarenessContext();
            if (context == IntPtr.Zero)
            {
                return DpiAwarenessMode.Unknown;
            }

            if (AreDpiAwarenessContextsEqual(context, new IntPtr(-4)))
            {
                return DpiAwarenessMode.PerMonitorV2;
            }

            return GetAwarenessFromDpiAwarenessContext(context) switch
            {
                1 => DpiAwarenessMode.Unaware,
                2 => DpiAwarenessMode.System,
                3 => DpiAwarenessMode.PerMonitor,
                _ => DpiAwarenessMode.Unknown,
            };
        }
        catch
        {
            return DpiAwarenessMode.Unknown;
        }
    }

    private List<DisplaySnapshot> ReadDisplays()
    {
        var displays = new List<DisplaySnapshot>();

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx { Size = MonitorInfoExSize };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return true;
                }

                var dpi = 96u;
                if (GetDpiForMonitor(monitor, DpiTypeEffective, out var dpiX, out _) == 0 && dpiX > 0)
                {
                    dpi = dpiX;
                }

                displays.Add(new DisplaySnapshot(
                    string.IsNullOrWhiteSpace(info.DeviceName) ? "显示器" : info.DeviceName,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top,
                    dpi,
                    (info.Flags & 1) != 0));

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "显示器枚举失败，兼容性快照将缺少显示器信息。");
        }

        return displays;
    }

    private (bool Available, string Language) ReadOcr()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.AvailableRecognizerLanguages
                    .Select(OcrEngine.TryCreateFromLanguage)
                    .FirstOrDefault(created => created is not null);

            return engine is null
                ? (false, "无")
                : (true, engine.RecognizerLanguage?.LanguageTag ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OCR 能力探测失败。");
            return (false, "探测失败");
        }
    }

    private (bool Available, string Device) ReadAudio()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device is null
                ? (false, "无")
                : (true, device.FriendlyName ?? "默认输出");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "音频输出设备探测失败。");
            return (false, "无可用设备");
        }
    }

    private bool ReadWgc()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Windows.Graphics.Capture 能力探测失败。");
            return false;
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
