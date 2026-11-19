namespace Lumina.Services.Compatibility;

/// <summary>
/// 运行环境能力探测：由适配器层实现，服务层与 UI 只读取快照。
/// 探测必须自行吞掉所有异常——任何一项能力缺失都不应阻断启动。
/// </summary>
public interface ICompatibilityProbe
{
    /// <summary>采集一次快照；应在 STA/UI 线程调用（OCR 与音频 COM 需要）。</summary>
    CompatibilitySnapshot Capture();
}
