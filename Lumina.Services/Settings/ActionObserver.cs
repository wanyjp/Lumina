namespace Lumina.Services.Settings;

/// <summary>以委托消费 <see cref="ISettingsStore.Changed"/>，避免每处重复实现观察者。</summary>
public sealed class ActionObserver<T> : IObserver<T>
{
    private readonly Action<T> _onNext;

    public ActionObserver(Action<T> onNext)
    {
        ArgumentNullException.ThrowIfNull(onNext);
        _onNext = onNext;
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(T value) => _onNext(value);
}
