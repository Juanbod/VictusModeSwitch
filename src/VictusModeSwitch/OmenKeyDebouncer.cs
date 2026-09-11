namespace VictusModeSwitch;

internal enum OmenKeyInputSource
{
    Keyboard,
    Wmi
}

internal sealed class OmenKeyDebouncer
{
    internal const long SameSourceWindowMilliseconds = 80;
    internal const long CrossSourceWindowMilliseconds = 120;

    private readonly object _sync = new();
    private long _lastPressTicks;
    private OmenKeyInputSource? _lastSource;

    public bool TryAccept(OmenKeyInputSource source, long ticks)
    {
        lock (_sync)
        {
            if (_lastPressTicks != 0)
            {
                var elapsed = ticks - _lastPressTicks;
                var window = _lastSource == source
                    ? SameSourceWindowMilliseconds
                    : CrossSourceWindowMilliseconds;
                if (elapsed >= 0 && elapsed < window)
                {
                    return false;
                }
            }

            _lastPressTicks = ticks;
            _lastSource = source;
            return true;
        }
    }
}
