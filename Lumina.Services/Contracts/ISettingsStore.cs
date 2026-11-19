namespace Lumina.Services.Contracts;

/// <summary>
/// 配置中枢。抽屉与设置中心共享同一实例；写入即生效并发布 <see cref="Changed"/>。
/// 实现于 P0-04（SQLite/JSON 双实现）。
/// </summary>
public interface ISettingsStore
{
    T? Get<T>(string key, T? fallback = default);

    void Set<T>(string key, T value, string? module = null);

    IObservable<SettingChanged> Changed { get; }

    Task SaveAsync(CancellationToken cancellationToken = default);
}
