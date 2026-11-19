namespace Lumina.Services.Compatibility;

/// <summary>
/// 兼容性分类与降级说明。纯函数，不触碰系统 API，便于在测试中覆盖
/// Windows 10 / Windows 11 / 混合 DPI / 能力缺失等组合。
/// </summary>
public static class CompatibilityAssessor
{
    /// <summary>Windows 10 2004（19041）是本项目的最低支持版本，与 TFM 对齐。</summary>
    public const int MinimumSupportedBuild = 19041;

    public static WindowsCompatibilityTier Classify(int buildNumber) => buildNumber switch
    {
        >= 26100 => WindowsCompatibilityTier.Windows11_24H2Plus,
        >= 22000 => WindowsCompatibilityTier.Windows11,
        >= MinimumSupportedBuild => WindowsCompatibilityTier.Windows10,
        _ => WindowsCompatibilityTier.Unsupported,
    };

    public static string TierLabel(WindowsCompatibilityTier tier) => tier switch
    {
        WindowsCompatibilityTier.Windows10 => "Windows 10 22H2（仅兼容抽检）",
        WindowsCompatibilityTier.Windows11 => "Windows 11（21H2–23H2）",
        WindowsCompatibilityTier.Windows11_24H2Plus => "Windows 11 24H2+",
        _ => "低于 Windows 10 2004（不受支持）",
    };

    public static string DpiAwarenessLabel(DpiAwarenessMode mode) => mode switch
    {
        DpiAwarenessMode.PerMonitorV2 => "Per-Monitor V2",
        DpiAwarenessMode.PerMonitor => "Per-Monitor",
        DpiAwarenessMode.System => "System",
        DpiAwarenessMode.Unaware => "Unaware",
        _ => "未知",
    };

    /// <summary>
    /// 生成降级说明：只列出确实影响体验的项，全部正常时返回空列表。
    /// 这些文案同时出现在启动日志与设置「关于」页，便于用户与支持人员核对。
    /// </summary>
    public static IReadOnlyList<string> EvaluateDegradations(
        WindowsCompatibilityTier tier,
        DpiAwarenessMode dpiAwareness,
        IReadOnlyList<DisplaySnapshot> displays,
        bool ocrAvailable,
        bool audioAvailable,
        bool wgcAvailable)
    {
        ArgumentNullException.ThrowIfNull(displays);

        var notes = new List<string>();

        if (tier == WindowsCompatibilityTier.Unsupported)
        {
            notes.Add("系统版本低于 Windows 10 2004，未纳入支持范围，仅保证进程可启动。");
        }
        else if (tier == WindowsCompatibilityTier.Windows10)
        {
            notes.Add("Windows 10 仅做兼容抽检：界面按纯色半透明层实现，不依赖系统云母/圆角特效。");
        }

        if (dpiAwareness != DpiAwarenessMode.PerMonitorV2)
        {
            notes.Add($"DPI 感知为 {DpiAwarenessLabel(dpiAwareness)}，混合缩放显示器下界面可能模糊或位置偏移。");
        }

        if (displays.Count == 0)
        {
            notes.Add("未探测到显示器，小组件位置将回落到主屏工作区。");
        }
        else if (displays.Select(display => display.Dpi).Distinct().Count() > 1)
        {
            notes.Add("检测到混合 DPI 显示器：小组件按所在屏 DPI 换算位置，跨屏后会自动收敛到可见区域。");
        }

        if (!ocrAvailable)
        {
            notes.Add("OCR 不可用：系统缺少识别语言包，截图取字入口已置灰。");
        }

        if (!audioAvailable)
        {
            notes.Add("无音频输出设备：白噪音不可用，其余模块不受影响。");
        }

        if (!wgcAvailable)
        {
            notes.Add("窗口捕获不可用：窗口截图回落 PrintWindow/BitBlt，被遮挡内容可能为空白。");
        }

        return notes;
    }
}
