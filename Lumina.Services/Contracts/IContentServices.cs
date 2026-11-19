using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Contracts;

/// <summary>剪贴板监听。实现于 P2-01；采集与 UI 线程分离。</summary>
public interface IClipboardMonitor
{
    bool Running { get; }

    void Start();

    void Stop();

    /// <summary>
    /// 短暂抑制采集（默认 800ms）。应用内回贴会连续触发多次剪贴板变更，
    /// 抑制窗口确保自己写入的内容不会被再次入库或累加重复次数。
    /// </summary>
    void SuppressCapture();

    event Action<ClipboardItem>? Captured;
}

/// <summary>剪贴板回贴：把历史条目按原格式写回系统剪贴板。实现于 P2-03。</summary>
public interface IClipboardWriter
{
    /// <summary>
    /// 按条目原生格式写回：文本/链接为纯文本，HTML 同时提供富文本与纯文本回退，
    /// 图片为位图，文件为文件列表。返回是否成功；失败由调用方降级提示手动复制。
    /// </summary>
    bool TryWrite(ClipboardItem item);

    /// <summary>只写纯文本；空文本返回 false。</summary>
    bool TryWritePlainText(string text);
}

/// <summary>
/// 屏幕捕获。实现于 P2-04；包含区域、全屏、窗口、延时四类。
/// 统一使用物理像素坐标；多显示器与负坐标按虚拟屏处理。
/// 用户取消（区域框选按 Esc / 右键）抛出 <see cref="OperationCanceledException"/>；
/// 捕获失败抛出 <see cref="IOException"/> 或平台异常，由调用方提示并允许重试，不静默返回空图。
/// </summary>
public interface IScreenCaptureService
{
    Task<CaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureFullScreenAsync(CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureWindowAsync(nint windowHandle, CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureDelayedAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

/// <summary>取色探针。实现于 P2-05。</summary>
public interface IColorProbe
{
    ColorSample Pick();

    event Action<ColorSample>? Sampled;
}

/// <summary>OCR。实现于 P2-06；优先 Windows AI TextRecognizer，回退 Windows.Media.Ocr。</summary>
public interface IOcrService
{
    bool Available { get; }

    Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// 倒计时日（P3-03）：CRUD、按锚点滚动后的下次发生日与五档紧迫度。
/// <see cref="All"/> 按紧迫度排序（未过期在前、剩余天数升序；已过期在后、最近过期优先；日期无效最后）。
/// </summary>
public interface ICountdownService
{
    IReadOnlyList<CountdownItem> All();

    UrgencySnapshot Evaluate(CountdownItem item);

    /// <summary>新增（Id=0）或更新；返回落库后的实体（新增时含自增 Id）。</summary>
    Task<CountdownItem> AddOrUpdateAsync(CountdownItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    event Action? Changed;
}

/// <summary>专注统计采集与聚合。实现于 P3-01。</summary>
public interface IFocusTracker
{
    Task RecordSessionAsync(FocusSession session, CancellationToken cancellationToken = default);

    Task<PeriodAggregate> AggregateAsync(PeriodKind kind, int offset, CancellationToken cancellationToken = default);
}

/// <summary>
/// 便签。实现于 P3-05；800ms 自动保存防抖由调用侧（编辑窗）封装，服务只负责落库。
/// </summary>
public interface INoteService
{
    /// <summary>按置顶优先、更新时间倒序返回全部便签。</summary>
    Task<IReadOnlyList<NoteItem>> AllAsync(CancellationToken cancellationToken = default);

    /// <summary>新增（Id=0）或更新；返回落库后的实体（新增时含自增 Id）。</summary>
    Task<NoteItem> SaveAsync(NoteItem note, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    event Action? Changed;
}
