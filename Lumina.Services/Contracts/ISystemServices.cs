using Lumina.Domain.Enums;

namespace Lumina.Services.Contracts;

/// <summary>全局热键。实现于 P0-06 / P2-08；冲突检测覆盖全表。</summary>
public interface IHotkeyManager
{
    bool Register(string action, HotkeyGesture gesture);

    bool IsFree(HotkeyGesture gesture, out string? conflictAction);

    /// <summary>取消单项注册；未注册时无副作用。</summary>
    void Unregister(string action);

    void UnregisterAll();

    event Action<string>? Pressed;
}

/// <summary>开机自启。实现于 P0-06；开关以系统实际状态为准。</summary>
public interface IAutoStartupService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

/// <summary>
/// 通知通道。实现于 P1-12；系统通道失败时返回 false，由调度器复位系统通知开关并降级为应用内提醒。
/// <paramref name="route"/> 为点击通知后跳转的模块 id。
/// </summary>
public interface INotifier
{
    Task<bool> ShowToastAsync(
        string title,
        string body,
        string? route = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 小组件宿主。实现于 P0-07；显示与更新均为幂等操作，P1-13 接入完整小组件设置。
/// </summary>
public interface IWidgetHost
{
    void ShowWidget(WidgetState state);

    /// <summary>
    /// 更新组件状态并持久化：已打开时即时生效，未打开时不主动显示。
    /// 用于模块设置里切换置顶、穿透、不透明度与皮肤。
    /// </summary>
    void UpdateWidget(WidgetState state);

    void CloseWidget(WidgetKind kind);

    void PersistLayout();

    WidgetState GetState(WidgetKind kind);

    bool IsVisible(WidgetKind kind);

    /// <summary>按持久化的显示状态恢复小组件；首次运行默认显示时钟组件。</summary>
    void RestoreVisible();
}

/// <summary>备份导出与恢复。实现于 P0-04 / P4-04。</summary>
public interface IBackupService
{
    Task ExportAsync(string path, CancellationToken cancellationToken = default);

    Task ImportAsync(string path, CancellationToken cancellationToken = default);
}
