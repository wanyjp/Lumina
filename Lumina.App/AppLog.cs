using Microsoft.Extensions.Logging;

namespace Lumina.App;

/// <summary>
/// 应用启动与退出日志的源生成方法。使用 <see cref="LoggerMessageAttribute"/> 避免
/// 在日志级别关闭时仍计算参数（CA1873），同时保持结构化字段名。
/// </summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "{Product} 启动，版本 {Version}，数据目录 {Root}")]
    public static partial void Startup(ILogger logger, string product, string version, string root);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information,
        Message = "数据库就绪：schema v{Previous} → v{Current}，应用迁移 {Applied} 个，升级前备份 {Backup}，FTS5={Fts5}")]
    public static partial void DatabaseReady(
        ILogger logger,
        int previous,
        int current,
        int applied,
        string backup,
        bool fts5);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information,
        Message = "主题就绪：选择 {Requested}，生效 {Effective}，高对比度 {HighContrast}，减少动画 {ReduceMotion}")]
    public static partial void ThemeReady(
        ILogger logger,
        string requested,
        string effective,
        bool highContrast,
        bool reduceMotion);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning,
        Message = "文件对账：孤儿文件 {Orphan} 个，缺失文件 {Missing} 个，已清理 {Deleted} 个")]
    public static partial void FilesReconciled(ILogger logger, int orphan, int missing, int deleted);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning,
        Message = "孤儿文件对账失败，已跳过")]
    public static partial void FileReconcileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information,
        Message = "{Product} 退出，退出码 {ExitCode}")]
    public static partial void Exit(ILogger logger, string product, int exitCode);

    [LoggerMessage(EventId = 106, Level = LogLevel.Error,
        Message = "UI 线程未处理异常")]
    public static partial void DispatcherUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 107, Level = LogLevel.Critical,
        Message = "进程级未处理异常，正在终止：{IsTerminating}")]
    public static partial void DomainUnhandled(ILogger logger, bool isTerminating, Exception? exception);

    [LoggerMessage(EventId = 108, Level = LogLevel.Error,
        Message = "未观察的任务异常")]
    public static partial void UnobservedTask(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 109, Level = LogLevel.Critical,
        Message = "数据库迁移失败，已回滚到 v{Previous}")]
    public static partial void MigrationFailed(ILogger logger, int previous, Exception exception);

    [LoggerMessage(EventId = 110, Level = LogLevel.Critical,
        Message = "数据库初始化失败")]
    public static partial void DatabaseInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 111, Level = LogLevel.Information,
        Message = "全局热键已注册：{Gesture}")]
    public static partial void HotkeyRegistered(ILogger logger, string gesture);

    [LoggerMessage(EventId = 112, Level = LogLevel.Warning,
        Message = "全局热键注册失败：{Gesture}（可能被其他程序占用）")]
    public static partial void HotkeyRegistrationFailed(ILogger logger, string gesture);

    [LoggerMessage(EventId = 113, Level = LogLevel.Warning,
        Message = "热键管理器初始化失败，已跳过全局热键")]
    public static partial void HotkeyInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning,
        Message = "托盘初始化失败，关闭窗口将直接退出")]
    public static partial void TrayInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 118, Level = LogLevel.Information,
        Message = "托盘已注册：{IconSource}，ShellAccepted={Visible}；Windows 11 可能把新图标收进隐藏区")]
    public static partial void TrayReady(ILogger logger, string iconSource, bool visible);

    [LoggerMessage(EventId = 119, Level = LogLevel.Information,
        Message = "托盘注册状态：{Status}")]
    public static partial void TrayStatus(ILogger logger, string status);

    [LoggerMessage(EventId = 120, Level = LogLevel.Warning,
        Message = "进程完整性级别异常：{Level}。托盘注册会被 UIPI 拒绝，已向用户提示修复方式")]
    public static partial void IntegrityWarning(ILogger logger, string level);

    [LoggerMessage(EventId = 121, Level = LogLevel.Information,
        Message = "截图对账：清理文件已缺失的记录 {Missing} 条；未入库的孤儿文件保留待人工确认")]
    public static partial void ScreenshotsReconciled(ILogger logger, int missing);

    [LoggerMessage(EventId = 115, Level = LogLevel.Warning,
        Message = "单实例激活监听启动失败，第二个实例将无法唤起主窗口")]
    public static partial void ActivationInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 116, Level = LogLevel.Information,
        Message = "关闭主窗口，已隐藏到托盘（进程与后台服务继续运行）")]
    public static partial void HiddenToTray(ILogger logger);

    [LoggerMessage(EventId = 117, Level = LogLevel.Warning,
        Message = "时钟小组件初始化失败，已跳过")]
    public static partial void WidgetInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 122, Level = LogLevel.Information,
        Message = "热键重注册（{Reason}）：{Summary}")]
    public static partial void HotkeyReregistered(ILogger logger, string reason, string summary);

    [LoggerMessage(EventId = 123, Level = LogLevel.Information,
        Message = "全局热键已触发：{Action}")]
    public static partial void HotkeyActionInvoked(ILogger logger, string action);

    [LoggerMessage(EventId = 124, Level = LogLevel.Warning,
        Message = "全局热键「{Action}」执行失败")]
    public static partial void HotkeyActionFailed(ILogger logger, string action, Exception exception);

    [LoggerMessage(EventId = 125, Level = LogLevel.Information,
        Message = "全局热键注册结果：{Summary}")]
    public static partial void HotkeySummary(ILogger logger, string summary);

    [LoggerMessage(EventId = 126, Level = LogLevel.Information,
        Message = "兼容性快照：{Summary}")]
    public static partial void CompatibilitySnapshot(ILogger logger, string summary);

    [LoggerMessage(EventId = 127, Level = LogLevel.Information,
        Message = "首帧就绪：{ElapsedMs} ms（{Detail}）")]
    public static partial void FirstFrameReady(ILogger logger, double elapsedMs, string detail);

    [LoggerMessage(EventId = 128, Level = LogLevel.Warning,
        Message = "启动首帧超时：已等待 {ElapsedMs} ms，切换软件渲染并重新布局（第 {Attempt} 次）")]
    public static partial void StartupRenderDegraded(ILogger logger, double elapsedMs, int attempt);

    [LoggerMessage(EventId = 129, Level = LogLevel.Error,
        Message = "软件渲染兜底后仍无首帧：累计 {ElapsedMs} ms。界面可能不可见，托盘与后台服务继续运行")]
    public static partial void StartupRenderFailed(ILogger logger, double elapsedMs);

    [LoggerMessage(EventId = 130, Level = LogLevel.Information,
        Message = "系统恢复（{Reason}）：休眠 {DormantSeconds} s，补扫提醒 {Fired} 条（{Rescanned}）")]
    public static partial void SystemResumed(ILogger logger, string reason, double dormantSeconds, int fired, bool rescanned);

    [LoggerMessage(EventId = 131, Level = LogLevel.Warning,
        Message = "系统恢复补扫提醒失败（{Reason}）")]
    public static partial void SystemResumeFailed(ILogger logger, string reason, Exception exception);

    [LoggerMessage(EventId = 132, Level = LogLevel.Information,
        Message = "已应用待恢复备份：{Backup}（schema v{Version}），覆盖前快照 {Safety}")]
    public static partial void PendingRestoreApplied(
        ILogger logger,
        string backup,
        int version,
        string safety);

    [LoggerMessage(EventId = 133, Level = LogLevel.Warning,
        Message = "待恢复备份未能应用（{Backup}）：{Error}")]
    public static partial void PendingRestoreFailed(ILogger logger, string backup, string error);

    [LoggerMessage(EventId = 134, Level = LogLevel.Warning,
        Message = "数据库疑似损坏，已将原文件隔离到 {Path}")]
    public static partial void DatabaseQuarantined(ILogger logger, string path);

    [LoggerMessage(EventId = 135, Level = LogLevel.Information,
        Message = "已重建空数据库，原文件保留在 {Path}")]
    public static partial void DatabaseRebuilt(ILogger logger, string path);

    [LoggerMessage(EventId = 136, Level = LogLevel.Warning,
        Message = "数据目录回退：{Notice}")]
    public static partial void DataRootFallback(ILogger logger, string notice);

    [LoggerMessage(EventId = 137, Level = LogLevel.Information,
        Message = "首次引导：{Detail}")]
    public static partial void Onboarding(ILogger logger, string detail);
}
