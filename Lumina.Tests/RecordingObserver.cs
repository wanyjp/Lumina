namespace Lumina.Tests;

internal sealed class RecordingObserver<T> : IObserver<T>
{
    private readonly List<T> _values = [];

    public IReadOnlyList<T> Values
    {
        get
        {
            lock (_values)
            {
                return _values.ToArray();
            }
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(T value)
    {
        lock (_values)
        {
            _values.Add(value);
        }
    }
}
