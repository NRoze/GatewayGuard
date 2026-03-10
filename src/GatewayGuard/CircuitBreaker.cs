using System;
using System.Threading;

namespace GatewayGuard;

public class CircuitBreaker
{
    private int _failures;
    private readonly int _threshold;
    private bool _open;
    private readonly Timer _resetTimer;

    public CircuitBreaker(int threshold, TimeSpan resetTime)
    {
        _threshold = threshold;
        _resetTimer = new Timer(_ => _open = false, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool AllowRequest()
    {
        return !_open;
    }

    public void RecordFailure()
    {
        _failures++;
        if (_failures >= _threshold)
        {
            _open = true;
            _resetTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            _failures = 0;
        }
    }

    public void RecordSuccess()
    {
        _failures = 0;
    }
}