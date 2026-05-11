namespace LoadTester;

/// <summary>
/// Собирает прикладную сводку поверх NBomber, чтобы connect/idle не маскировали реальный publish traffic.
/// </summary>
internal sealed class LoadTestMetrics
{
    private long _activeConnections;
    private long _peakConnections;
    private long _connectAttempts;
    private long _connectSucceeded;
    private long _connectRetries;
    private long _connectWaits;
    private long _disconnected;
    private long _idleIterations;
    private long _publishSucceeded;
    private long _publishRejected;
    private long _publishTimedOut;
    private long _canceled;
    private long _unhandledExceptions;
    private string _lastConnectError = string.Empty;

    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    public long PeakConnections => Interlocked.Read(ref _peakConnections);

    public long ConnectAttempts => Interlocked.Read(ref _connectAttempts);

    public long ConnectSucceeded => Interlocked.Read(ref _connectSucceeded);

    public long ConnectRetries => Interlocked.Read(ref _connectRetries);

    public long ConnectWaits => Interlocked.Read(ref _connectWaits);

    public long Disconnected => Interlocked.Read(ref _disconnected);

    public long IdleIterations => Interlocked.Read(ref _idleIterations);

    public long PublishSucceeded => Interlocked.Read(ref _publishSucceeded);

    public long PublishRejected => Interlocked.Read(ref _publishRejected);

    public long PublishTimedOut => Interlocked.Read(ref _publishTimedOut);

    public long Canceled => Interlocked.Read(ref _canceled);

    public long UnhandledExceptions => Interlocked.Read(ref _unhandledExceptions);

    public string LastConnectError => Volatile.Read(ref _lastConnectError);

    public void RecordConnectAttempt()
    {
        Interlocked.Increment(ref _connectAttempts);
    }

    public void RecordConnectSucceeded()
    {
        Interlocked.Increment(ref _connectSucceeded);
        var active = Interlocked.Increment(ref _activeConnections);
        UpdatePeakConnections(active);
    }

    public void RecordConnectRetry(string error)
    {
        Interlocked.Increment(ref _connectRetries);
        Volatile.Write(ref _lastConnectError, error);
    }

    public void RecordConnectWait()
    {
        Interlocked.Increment(ref _connectWaits);
    }

    public void RecordDisconnected()
    {
        Interlocked.Increment(ref _disconnected);

        while (true)
        {
            var current = Interlocked.Read(ref _activeConnections);
            if (current <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _activeConnections, current - 1, current) == current)
            {
                return;
            }
        }
    }

    public void RecordIdle()
    {
        Interlocked.Increment(ref _idleIterations);
    }

    public void RecordPublishSucceeded()
    {
        Interlocked.Increment(ref _publishSucceeded);
    }

    public void RecordPublishRejected()
    {
        Interlocked.Increment(ref _publishRejected);
    }

    public void RecordPublishTimedOut()
    {
        Interlocked.Increment(ref _publishTimedOut);
    }

    public void RecordCanceled()
    {
        Interlocked.Increment(ref _canceled);
    }

    public void RecordUnhandledException()
    {
        Interlocked.Increment(ref _unhandledExceptions);
    }

    private void UpdatePeakConnections(long active)
    {
        while (true)
        {
            var currentPeak = Interlocked.Read(ref _peakConnections);
            if (active <= currentPeak)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _peakConnections, active, currentPeak) == currentPeak)
            {
                return;
            }
        }
    }
}
