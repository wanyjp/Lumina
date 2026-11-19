using Lumina.Domain.Models;

namespace Lumina.Domain.Stores;

/// <summary>世界时钟城市存储端口；由数据层的 SQLite 实现。</summary>
public interface IWorldCityStore
{
    IReadOnlyList<WorldCity> GetAll();

    long Add(string ianaId, string displayName, int sortOrder);

    void Remove(long id);
}
