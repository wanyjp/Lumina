namespace Lumina.Services.Configuration;

using Lumina.Domain.Clipboard;
using Lumina.Services.Clipboard;

/// <summary>配置键名；桌面版统一写入 `app_setting.key`。</summary>
public static class PreferenceKeys
{
    public const string Theme = "theme";
    public const string CheckStyle = "checkStyle";
    public const string ReduceMotion = "reduceMotion";
    public const string Skin = "skin";
    public const string ClockFormat = "clockFormat";
    public const string PeriodMode = "periodMode";
    public const string CalendarLayout = "calLayout";
    public const string WeekStart = "weekStart";
    public const string PomodoroFullscreen = "pomoFull";

    /// <summary>计时器目标时长（秒）；与番茄钟分别记忆，互不覆盖。</summary>
    public const string TimerTargetSeconds = "timerTargetSec";

    /// <summary>番茄钟专注时长（秒）；与计时器分别记忆，互不覆盖。</summary>
    public const string PomodoroTargetSeconds = "pomoTargetSec";
    public const string AudioVolume = "audioVol";
    public const string ScheduleReminder = "schedRemind";
    public const string CountdownReminder = "cdRemind";
    public const string InAppReminder = "inAppRemind";
    public const string SystemNotification = "sysNotify";

    /// <summary>计时器/番茄钟到点提示音（chime1 / chime2 / silent）。</summary>
    public const string NotificationSound = "notifySound";
    public const string WorldSelectedCity = "worldSelectedCity";
    public const string ClipboardCaptureEnabled = "clipCapture";
    public const string ClipboardPaused = "clipPaused";
    public const string ClipboardRetentionLimit = "clipLimit";
    public const string ClipboardIgnoreRules = "clipIgnoreRules";
    public const string ClipboardBlockOneTimeCodes = "clipBlockOtp";
    public const string ClipboardShowTags = "clipShowTags";
    public const string ClipboardMinimizeAfterPaste = "clipMinimizeAfterPaste";
    public const string ClipboardHtmlPlainOnly = "clipHtmlPlainOnly";

    /// <summary>纯文本相同但来源不同时是否合并为同一条历史记录。</summary>
    public const string ClipboardMergeSources = "clipMergeSources";
    public const string TimerWidgetCompact = "timerWidgetCompact";
    public const string ColorHistory = "colorHistory";
    public const string ScreenshotThumbOnly = "shotThumbOnly";

    /// <summary>截图历史保留张数；超出后按最旧优先淘汰。</summary>
    public const string ScreenshotRetentionLimit = "shotLimit";

    /// <summary>小组件玻璃层改用 WGC 逐帧捕获（实验）。</summary>
    public const string WidgetGlassCapture = "widgetGlassCapture";

    /// <summary>首次运行引导是否已完成（关闭引导即视为完成）。</summary>
    public const string OnboardingCompleted = "onboardingCompleted";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Theme, CheckStyle, ReduceMotion, Skin, ClockFormat, PeriodMode, CalendarLayout, WeekStart,
        PomodoroFullscreen, TimerTargetSeconds, PomodoroTargetSeconds, AudioVolume, ScheduleReminder, CountdownReminder,
        InAppReminder, SystemNotification, NotificationSound, WorldSelectedCity,
        ClipboardCaptureEnabled, ClipboardPaused, ClipboardRetentionLimit,
        ClipboardIgnoreRules, ClipboardBlockOneTimeCodes, ClipboardShowTags,
        ClipboardMinimizeAfterPaste, ClipboardHtmlPlainOnly, ClipboardMergeSources,
        TimerWidgetCompact, ColorHistory, ScreenshotThumbOnly, ScreenshotRetentionLimit, WidgetGlassCapture,
        OnboardingCompleted,
    };
}

/// <summary>配置默认值；读取失败或字段缺失时回落此处。</summary>
public static class PreferenceDefaults
{
    public const string Theme = "auto";
    public const string CheckStyle = "stroke";
    public const bool ReduceMotion = false;
    public const string Skin = "glass";
    public const string ClockFormat = "24";
    public const string PeriodMode = "modern";
    public const string CalendarLayout = "horizontal";
    public const int WeekStart = 1;
    public const bool PomodoroFullscreen = true;

    /// <summary>默认 25 分钟；计时器与番茄钟各存一份。</summary>
    public const int TimerTargetSeconds = 25 * 60;

    public const int PomodoroTargetSeconds = 25 * 60;
    public const int AudioVolume = 60;
    public const string ScheduleReminder = "m15";
    public const string CountdownReminder = "d1";
    public const bool InAppReminder = true;

    /// <summary>系统通知默认开启：计时器/番茄钟到点时窗口可能不在前台，仅靠应用内提醒会漏。</summary>
    public const bool SystemNotification = true;

    /// <summary>到点提示音默认使用第一段原型音效。</summary>
    public const string NotificationSound = "chime1";
    public const long WorldSelectedCity = 0L;
    public const bool ClipboardCaptureEnabled = true;
    public const bool ClipboardPaused = false;
    public const int ClipboardRetentionLimit = EvictionPolicy.DefaultLimit;
    public const string ClipboardIgnoreRules = ClipboardIgnoreDefaults.Json;
    public const bool ClipboardBlockOneTimeCodes = true;
    public const bool ClipboardShowTags = true;
    public const bool ClipboardMinimizeAfterPaste = false;
    public const bool ClipboardHtmlPlainOnly = false;
    public const bool ClipboardMergeSources = true;

    /// <summary>计时类小组件是否使用“小”密度；默认更紧凑。</summary>
    public const bool TimerWidgetCompact = true;

    /// <summary>取色历史色板；JSON 数组，最近使用在前。</summary>
    public const string ColorHistory = "[]";

    /// <summary>截图仅保留缩略图，不保留原图。</summary>
    public const bool ScreenshotThumbOnly = false;

    /// <summary>截图历史默认保留 50 张。</summary>
    public const int ScreenshotRetentionLimit = AppLimits.ScreenshotRetentionDefault;

    /// <summary>玻璃层默认走低开销的 GDI 采样，逐帧捕获需用户显式开启。</summary>
    public const bool WidgetGlassCapture = false;

    /// <summary>首次运行引导默认未完成；已有数据的旧安装由启动逻辑自动标记为已完成。</summary>
    public const bool OnboardingCompleted = false;

    public static IReadOnlyDictionary<string, object?> Values { get; } = new Dictionary<string, object?>
    {
        [PreferenceKeys.Theme] = Theme,
        [PreferenceKeys.CheckStyle] = CheckStyle,
        [PreferenceKeys.ReduceMotion] = ReduceMotion,
        [PreferenceKeys.Skin] = Skin,
        [PreferenceKeys.ClockFormat] = ClockFormat,
        [PreferenceKeys.PeriodMode] = PeriodMode,
        [PreferenceKeys.CalendarLayout] = CalendarLayout,
        [PreferenceKeys.WeekStart] = WeekStart,
        [PreferenceKeys.PomodoroFullscreen] = PomodoroFullscreen,
        [PreferenceKeys.TimerTargetSeconds] = TimerTargetSeconds,
        [PreferenceKeys.PomodoroTargetSeconds] = PomodoroTargetSeconds,
        [PreferenceKeys.AudioVolume] = AudioVolume,
        [PreferenceKeys.ScheduleReminder] = ScheduleReminder,
        [PreferenceKeys.CountdownReminder] = CountdownReminder,
        [PreferenceKeys.InAppReminder] = InAppReminder,
        [PreferenceKeys.SystemNotification] = SystemNotification,
        [PreferenceKeys.NotificationSound] = NotificationSound,
        [PreferenceKeys.WorldSelectedCity] = WorldSelectedCity,
        [PreferenceKeys.ClipboardCaptureEnabled] = ClipboardCaptureEnabled,
        [PreferenceKeys.ClipboardPaused] = ClipboardPaused,
        [PreferenceKeys.ClipboardRetentionLimit] = ClipboardRetentionLimit,
        [PreferenceKeys.ClipboardIgnoreRules] = ClipboardIgnoreRules,
        [PreferenceKeys.ClipboardBlockOneTimeCodes] = ClipboardBlockOneTimeCodes,
        [PreferenceKeys.ClipboardShowTags] = ClipboardShowTags,
        [PreferenceKeys.ClipboardMinimizeAfterPaste] = ClipboardMinimizeAfterPaste,
        [PreferenceKeys.ClipboardHtmlPlainOnly] = ClipboardHtmlPlainOnly,
        [PreferenceKeys.ClipboardMergeSources] = ClipboardMergeSources,
        [PreferenceKeys.TimerWidgetCompact] = TimerWidgetCompact,
        [PreferenceKeys.ColorHistory] = ColorHistory,
        [PreferenceKeys.ScreenshotThumbOnly] = ScreenshotThumbOnly,
        [PreferenceKeys.ScreenshotRetentionLimit] = ScreenshotRetentionLimit,
        [PreferenceKeys.WidgetGlassCapture] = WidgetGlassCapture,
        [PreferenceKeys.OnboardingCompleted] = OnboardingCompleted,
    };
}

/// <summary>与工程方案一致的容量与数量限制。</summary>
public static class AppLimits
{
    public const int ClipboardRetentionDefault = EvictionPolicy.DefaultLimit;
    public const int ClipboardRetentionMin = EvictionPolicy.MinLimit;
    public const int ClipboardRetentionMax = EvictionPolicy.MaxLimit;
    public const int ScreenshotRetentionDefault = 50;
    public const int ScreenshotRetentionMin = 10;
    public const int ScreenshotRetentionMax = 500;
    public const int RepeatGenerationLimitDays = 3700;
    public const int ReminderDedupUpperBound = 600;
    public const int ReminderDedupTrimTarget = 500;
    public const int NoteTagMaxCount = ClipboardTagPolicy.MaxCount;
    public const int NoteTagMaxLength = ClipboardTagPolicy.MaxLength;
    public const int NoiseSceneMaxCount = 6;
    public const int NoiseSceneNameMaxLength = 16;
}
