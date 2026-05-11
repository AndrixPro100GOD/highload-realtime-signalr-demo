namespace LoadTester;

/// <summary>
/// Результат получения persistent SignalR-сессии для одного virtual user.
/// </summary>
internal sealed record SessionAcquireResult(
    SignalRClientSession? Session,
    string StatusCode,
    bool IsReady,
    bool IsNewConnection,
    double LatencyMs,
    string? Message = null)
{
    public static SessionAcquireResult Existing(SignalRClientSession session)
    {
        return new SessionAcquireResult(session, "connected-existing", IsReady: true, IsNewConnection: false, LatencyMs: 0);
    }

    public static SessionAcquireResult Connected(SignalRClientSession session, double latencyMs)
    {
        return new SessionAcquireResult(session, "connected", IsReady: true, IsNewConnection: true, LatencyMs: latencyMs);
    }

    public static SessionAcquireResult Wait()
    {
        return new SessionAcquireResult(null, "connect-wait", IsReady: false, IsNewConnection: false, LatencyMs: 0);
    }

    public static SessionAcquireResult Disconnected()
    {
        return new SessionAcquireResult(null, "disconnected", IsReady: false, IsNewConnection: false, LatencyMs: 0);
    }

    public static SessionAcquireResult Retry(double latencyMs, string message)
    {
        return new SessionAcquireResult(null, "connect-retry", IsReady: false, IsNewConnection: false, LatencyMs: latencyMs, Message: message);
    }
}
