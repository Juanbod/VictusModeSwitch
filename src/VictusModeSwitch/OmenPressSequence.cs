namespace VictusModeSwitch;

internal enum OmenGesture
{
    SinglePress,
    DoublePress,
    TriplePress
}

internal sealed class OmenPressSequence : IDisposable
{
    internal const int GestureWindowMilliseconds = 320;
    private readonly System.Windows.Forms.Timer _timer;
    private int _pressCount;

    public OmenPressSequence()
    {
        _timer = new System.Windows.Forms.Timer { Interval = GestureWindowMilliseconds };
        _timer.Tick += FinishSequence;
    }

    public event Action<OmenGesture>? GestureRecognized;

    public void RegisterPress()
    {
        _timer.Stop();
        _pressCount++;
        if (_pressCount >= 3)
        {
            Emit(OmenGesture.TriplePress);
            return;
        }

        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= FinishSequence;
        _timer.Dispose();
    }

    private void FinishSequence(object? sender, EventArgs eventArgs)
    {
        Emit(_pressCount == 2 ? OmenGesture.DoublePress : OmenGesture.SinglePress);
    }

    private void Emit(OmenGesture gesture)
    {
        _timer.Stop();
        _pressCount = 0;
        GestureRecognized?.Invoke(gesture);
    }
}
