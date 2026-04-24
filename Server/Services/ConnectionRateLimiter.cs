using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Server.Options;

namespace Server.Services;

/// <summary>
/// Ограничивает частоту запросов от одного соединения, чтобы один шумный клиент не выжигал весь инстанс.
/// </summary>
internal sealed class ConnectionRateLimiter : IDisposable
{
    private readonly HubGuardOptions _options;
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new(StringComparer.Ordinal);

    public ConnectionRateLimiter(IOptions<RealtimeServerOptions> options)
    {
        _options = options.Value.HubGuard;
    }

    public bool TryAcquire(string connectionId)
    {
        var limiter = _limiters.GetOrAdd(connectionId, static (_, settings) => CreateLimiter(settings), _options);
        using var lease = limiter.AttemptAcquire(1);
        return lease.IsAcquired;
    }

    public void Release(string connectionId)
    {
        if (_limiters.TryRemove(connectionId, out var limiter))
        {
            limiter.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }

        _limiters.Clear();
    }

    private static TokenBucketRateLimiter CreateLimiter(HubGuardOptions options)
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.TokenLimit,
            TokensPerPeriod = options.TokensPerPeriod,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(options.ReplenishmentPeriodMs),
            AutoReplenishment = true
        });
    }
}
