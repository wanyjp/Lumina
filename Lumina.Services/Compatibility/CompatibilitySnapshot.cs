namespace Lumina.Services.Compatibility;

/// <summary>
/// Windows 兼容档位：按 build 号归档，用于决定可用的系统能力与验证范围。
/// Windows 10 22H2 仅做兼容抽检，不承诺全部系统级视觉特效。
/// </summary>
public enum WindowsCompatibilityTier
{
    Unsupported,
    Windows10,
    Windows11,
    Windows11_24H2Plus,
}

/// <summary>进程 DPI 感知模式，对应 Win32 DPI_AWARENESS / DPI_AWARENESS_CONTEXT。</summary>
public enum DpiAwarenessMode
{
    Unknown,
    Unaware,
    System,
    PerMonitor,
    PerMonitorV2,
}

/// <summary>单个显示器的物理像素边界与有效 DPI。</summary>
public sealed record DisplaySnapshot(
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    uint Dpi,
    bool Primary)
{
    public int ScalePercent => Dpi == 0 ? 100 : (int)Math.Round(Dpi * 100.0 / 96.0);

    public string Describe() =>
        $"{DeviceName} {Width}×{Height} @ {ScalePercent}%" + (Primary ? " · 主屏" : string.Empty);
}

/// <summary>
/// 一次兼容性探测的完整快照：系统版本、DPI 感知、显示器、OCR/音频/窗口捕获能力。
/// 由适配器层采集，服务层只做分类与降级说明，便于单元测试。
/// </summary>
public sealed record CompatibilitySnapshot(
    string OperatingSystem,
    int BuildNumber,
    WindowsCompatibilityTier Tier,
    DpiAwarenessMode DpiAwareness,
    IReadOnlyList<DisplaySnapshot> Displays,
    bool OcrAvailable,
    string OcrLanguage,
    bool AudioAvailable,
    string AudioDevice,
    bool WgcAvailable,
    IReadOnlyList<string> Degradations)
{
    public string TierLabel => CompatibilityAssessor.TierLabel(Tier);

    public bool IsSupported => Tier != WindowsCompatibilityTier.Unsupported;

    public string DpiAwarenessLabel => DpiAwareness switch
    {
        DpiAwarenessMode.PerMonitorV2 => "Per-Monitor V2",
        DpiAwarenessMode.PerMonitor => "Per-Monitor",
        DpiAwarenessMode.System => "System",
        DpiAwarenessMode.Unaware => "Unaware",
        _ => "未知",
    };

    public string DisplaySummary => Displays.Count == 0
        ? "未探测到显示器"
        : string.Join("；", Displays.Select(display => display.Describe()));

    public string OcrSummary => OcrAvailable
        ? $"可用（{OcrLanguage}）"
        : "不可用（缺少识别语言包）";

    public string AudioSummary => AudioAvailable
        ? $"可用（{AudioDevice}）"
        : "无可用输出设备";

    public string WgcSummary => WgcAvailable
        ? "可用"
        : "不可用，窗口截图回落 PrintWindow/BitBlt";

    /// <summary>设置页与日志共用的单行摘要。</summary>
    public string Describe() =>
        $"{TierLabel}（build {BuildNumber}）｜DPI {DpiAwarenessLabel}｜显示器 {Displays.Count} 台｜"
        + $"OCR {(OcrAvailable ? "可用" : "不可用")}｜音频 {(AudioAvailable ? "可用" : "不可用")}｜WGC {(WgcAvailable ? "可用" : "不可用")}";
}
