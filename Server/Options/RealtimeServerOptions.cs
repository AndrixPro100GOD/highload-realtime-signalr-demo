namespace Server.Options;

/// <summary>
/// Централизует все high-load настройки, чтобы их было легко переопределять через appsettings и env vars.
/// </summary>
internal sealed class RealtimeServerOptions
{
    public ThreadPoolTuningOptions ThreadPool { get; init; } = new();

    public KestrelTuningOptions Kestrel { get; init; } = new();

    public SignalRTuningOptions SignalR { get; init; } = new();

    public RedisBackplaneOptions Redis { get; init; } = new();

    public BatchingOptions Batching { get; init; } = new();

    public HubGuardOptions HubGuard { get; init; } = new();
}

internal sealed class ThreadPoolTuningOptions
{
    public int MinWorkerThreads { get; init; } = 256;

    public int MinCompletionPortThreads { get; init; } = 256;
}

internal sealed class KestrelTuningOptions
{
    public int HttpPort { get; init; } = 8080;

    public long? MaxConcurrentConnections { get; init; } = 200_000;

    public long? MaxConcurrentUpgradedConnections { get; init; } = 200_000;

    public int KeepAliveSeconds { get; init; } = 30;

    public int RequestHeadersTimeoutSeconds { get; init; } = 15;

    public int SocketBacklog { get; init; } = 8_192;

    public int? IoQueueCount { get; init; }
}

internal sealed class SignalRTuningOptions
{
    public int KeepAliveIntervalSeconds { get; init; } = 10;

    public int ClientTimeoutIntervalSeconds { get; init; } = 30;

    public int HandshakeTimeoutSeconds { get; init; } = 10;

    public long MaximumReceiveMessageSizeBytes { get; init; } = 64 * 1024;

    public int StreamBufferCapacity { get; init; } = 32;

    public int MaximumParallelInvocationsPerClient { get; init; } = 1;

    public bool EnableDetailedErrors { get; init; }
}

internal sealed class RedisBackplaneOptions
{
    public bool Enabled { get; init; } = true;

    public string Configuration { get; init; } = "localhost:6379,abortConnect=false";

    public string ChannelPrefix { get; init; } = "highload-realtime";

    public bool AbortOnConnectFail { get; init; }

    public int ConnectRetry { get; init; } = 5;

    public int ConnectTimeoutMs { get; init; } = 5_000;

    public int SyncTimeoutMs { get; init; } = 5_000;

    public int AsyncTimeoutMs { get; init; } = 5_000;

    public int KeepAliveSeconds { get; init; } = 15;

    public int ExponentialRetryBaseMs { get; init; } = 2_000;
}

internal sealed class BatchingOptions
{
    public int QueueCapacity { get; init; } = 100_000;

    public int MaxBatchSize { get; init; } = 256;

    public int FlushIntervalMs { get; init; } = 50;

    public double DropThresholdRatio { get; init; } = 0.85;
}

internal sealed class HubGuardOptions
{
    public int TokenLimit { get; init; } = 1_000;

    public int TokensPerPeriod { get; init; } = 500;

    public int ReplenishmentPeriodMs { get; init; } = 1_000;

    public int QueueLimit { get; init; }
}
