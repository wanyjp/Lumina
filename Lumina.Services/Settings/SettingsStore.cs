using System.Text.Json;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Settings;

/// <summary>
/// 配置中枢实现：在 <see cref="ISettingsBackend"/> 之上负责类型转换、默认值回落与变更通知。
/// SQLite 后端为生产默认；JSON 后端用于便携与缓存场景。
/// </summary>
public sealed class SettingsStore : ISettingsStore, IObservable<SettingChanged>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISettingsBackend _backend;
    private readonly object _gate = new();
    private readonly List<IObserver<SettingChanged>> _observers = [];

    public SettingsStore(ISettingsBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    public IObservable<SettingChanged> Changed => this;

    public T? Get<T>(string key, T? fallback = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var raw = _backend.Get(key);
        if (raw is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(raw, JsonOptions);
            }
            catch (JsonException)
            {
                // 值损坏时按缺失处理，回落到默认值。
            }
            catch (NotSupportedException)
            {
                // 同上。
            }
        }

        // 值类型未显式传入 fallback 时，其默认值（如 0）不应覆盖配置目录中的默认值。
        if (fallback is not null && !EqualityComparer<T>.Default.Equals(fallback, default))
        {
            return fallback;
        }

        if (PreferenceDefaults.Values.TryGetValue(key, out var defaultValue) && defaultValue is T typed)
        {
            return typed;
        }

        return fallback;
    }

    public void Set<T>(string key, T value, string? module = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var raw = JsonSerializer.Serialize(value, JsonOptions);
        _backend.Set(key, raw, module);
        Publish(new SettingChanged(key, value, module));
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _backend.SaveAsync(cancellationToken);

    public IDisposable Subscribe(IObserver<SettingChanged> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        return new Subscription(this, observer);
    }

    private void Publish(SettingChanged change)
    {
        IObserver<SettingChanged>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnNext(change);
            }
            catch (Exception)
            {
                // 单个订阅者异常不得阻断配置写入。
            }
        }
    }

    private void Unsubscribe(IObserver<SettingChanged> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly SettingsStore _owner;
        private IObserver<SettingChanged>? _observer;

        public Subscription(SettingsStore owner, IObserver<SettingChanged> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            var observer = _observer;
            if (observer is null)
            {
                return;
            }

            _observer = null;
            _owner.Unsubscribe(observer);
        }
    }
}
