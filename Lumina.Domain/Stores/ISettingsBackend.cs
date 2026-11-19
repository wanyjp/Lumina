namespace Lumina.Domain.Stores;

/// <summary>
/// 配置存储端口：只负责键值读写，不解释值语义。
/// 值为调用方序列化后的文本；数据层提供 SQLite 与 JSON 两种实现。
/// </summary>
public interface ISettingsBackend
{
    /// <summary>读取原始值；不存在时返回 <see langword="null"/>。</summary>
    string? Get(string key);

    /// <summary>读取全部键值，用于导出与诊断。</summary>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>写入或覆盖单个键；<paramref name="module"/> 为来源模块标识。</summary>
    void Set(string key, string value, string? module);

    /// <summary>将暂存变更落盘；SQLite 写入即落库，本方法为空操作。</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
