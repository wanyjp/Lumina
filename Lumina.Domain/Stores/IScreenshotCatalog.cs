using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>
/// 截图元数据仓储抽象；实现于 Data 层（SQLite）。
/// 放在 Domain 是为了让 Data 与 Services 都只依赖 Domain，维持分层方向。
/// </summary>
public interface IScreenshotCatalog
{
    long Add(ScreenshotRecord record);

    IReadOnlyList<ScreenshotRecord> Recent(int limit);

    IReadOnlyList<ScreenshotRecord> All();

    IReadOnlyList<ScreenshotRecord> Oldest(int count);

    ScreenshotRecord? Find(long id);

    int Count();

    void Delete(long id);
}
