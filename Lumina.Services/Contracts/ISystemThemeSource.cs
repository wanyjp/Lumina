namespace Lumina.Services.Contracts;

/// <summary>
/// 系统明暗外观来源（P0-05 新增）。UI 只依赖此契约，Windows 实现位于系统适配层。
/// 仅在用户选择“跟随系统”时生效；手动选档后以手动值为准。
/// </summary>
public interface ISystemThemeSource : IDisposable
{
    bool IsDarkMode { get; }

    event EventHandler? Changed;
}
