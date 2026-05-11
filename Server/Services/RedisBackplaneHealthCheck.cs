using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Server.Options;
using StackExchange.Redis;

namespace Server.Services;

/// <summary>
/// Проверяет Redis backplane в readiness, потому что без него multi-instance SignalR теряет межузловой fan-out.
/// </summary>
internal sealed class RedisBackplaneHealthCheck(IOptions<RealtimeServerOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var redisOptions = options.Value.Redis;
        if (!redisOptions.Enabled)
        {
            return HealthCheckResult.Healthy("Redis backplane is disabled for this environment.");
        }

        try
        {
            var configuration = ConfigurationOptions.Parse(redisOptions.Configuration, true);
            configuration.AbortOnConnectFail = redisOptions.AbortOnConnectFail;
            configuration.ConnectRetry = 1;
            configuration.ConnectTimeout = Math.Min(redisOptions.ConnectTimeoutMs, 1_000);
            configuration.SyncTimeout = Math.Min(redisOptions.SyncTimeoutMs, 1_000);
            configuration.AsyncTimeout = Math.Min(redisOptions.AsyncTimeoutMs, 1_000);

            using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            var database = connection.GetDatabase();
            var pong = await database.PingAsync();

            return pong <= TimeSpan.FromSeconds(1)
                ? HealthCheckResult.Healthy("Redis backplane is ready.")
                : HealthCheckResult.Degraded($"Redis backplane latency is high: {pong.TotalMilliseconds:F0} ms.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis backplane is not reachable.", exception);
        }
    }
}
