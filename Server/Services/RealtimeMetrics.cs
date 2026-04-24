using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Server.Services;

/// <summary>
/// Единая точка для кастомных метрик SignalR и backpressure-логики.
/// </summary>
internal sealed class RealtimeMetrics : IDisposable
{
    internal const string MeterName = "Highload.Realtime.Server";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _publishedMessages;
    private readonly Counter<long> _deliveredMessages;
    private readonly Counter<long> _droppedMessages;
    private readonly Counter<long> _rateLimitedRequests;
    private readonly Histogram<double> _publishLatencyMs;
    private readonly Histogram<int> _batchSize;
    private readonly Histogram<int> _payloadBytes;
    private long _activeConnections;
    private int _batchQueueDepth;

    public RealtimeMetrics()
    {
        _publishedMessages = _meter.CreateCounter<long>(
            "signalr_messages_published_total",
            unit: "{message}",
            description: "Число сообщений, принятых сервером в публикацию.");

        _deliveredMessages = _meter.CreateCounter<long>(
            "signalr_messages_delivered_total",
            unit: "{message}",
            description: "Число сообщений, доставленных клиентам после fan-out.");

        _droppedMessages = _meter.CreateCounter<long>(
            "signalr_messages_dropped_total",
            unit: "{message}",
            description: "Число сообщений, отброшенных backpressure-механизмом.");

        _rateLimitedRequests = _meter.CreateCounter<long>(
            "signalr_requests_rate_limited_total",
            unit: "{request}",
            description: "Число запросов Hub, отклонённых rate limiter-ом.");

        _publishLatencyMs = _meter.CreateHistogram<double>(
            "signalr_publish_latency_ms",
            unit: "ms",
            description: "Время серверной обработки публикации до fan-out.");

        _batchSize = _meter.CreateHistogram<int>(
            "signalr_batch_size",
            unit: "{message}",
            description: "Размер батча сообщений, отправленного одним flush.");

        _payloadBytes = _meter.CreateHistogram<int>(
            "signalr_payload_size_bytes",
            unit: "By",
            description: "Оценка размера полезной нагрузки до сериализации.");

        _meter.CreateObservableGauge(
            "signalr_active_connections",
            ObserveActiveConnections,
            unit: "{connection}",
            description: "Текущее число активных SignalR-соединений на инстансе.");

        _meter.CreateObservableGauge(
            "signalr_batch_queue_depth",
            ObserveQueueDepth,
            unit: "{message}",
            description: "Текущая глубина очереди батчинга.");

        _meter.CreateObservableGauge(
            "process_working_set_bytes",
            ObserveWorkingSet,
            unit: "By",
            description: "Working set процесса, чтобы видеть давление по памяти.");

        _meter.CreateObservableCounter(
            "process_cpu_time_seconds_total",
            ObserveCpuTime,
            unit: "s",
            description: "Накопленное процессорное время процесса; Prometheus rate даёт CPU usage.");
    }

    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    public int BatchQueueDepth => Volatile.Read(ref _batchQueueDepth);

    public void ConnectionOpened() => Interlocked.Increment(ref _activeConnections);

    public void ConnectionClosed() => Interlocked.Decrement(ref _activeConnections);

    public void SetBatchQueueDepth(int depth) => Volatile.Write(ref _batchQueueDepth, Math.Max(depth, 0));

    public void RecordPublished(int payloadBytes, double latencyMs)
    {
        _publishedMessages.Add(1);
        _publishLatencyMs.Record(latencyMs);
        _payloadBytes.Record(payloadBytes);
    }

    public void RecordDelivered(int deliveredCount) => _deliveredMessages.Add(deliveredCount);

    public void RecordDropped(string reason)
    {
        _droppedMessages.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordRateLimited() => _rateLimitedRequests.Add(1);

    public void RecordBatchSize(int batchSize)
    {
        if (batchSize > 0)
        {
            _batchSize.Record(batchSize);
        }
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveActiveConnections()
    {
        yield return new Measurement<long>(Interlocked.Read(ref _activeConnections));
    }

    private IEnumerable<Measurement<int>> ObserveQueueDepth()
    {
        yield return new Measurement<int>(Volatile.Read(ref _batchQueueDepth));
    }

    private static IEnumerable<Measurement<long>> ObserveWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        yield return new Measurement<long>(process.WorkingSet64);
    }

    private static IEnumerable<Measurement<double>> ObserveCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        yield return new Measurement<double>(process.TotalProcessorTime.TotalSeconds);
    }
}

/// <summary>
/// Даёт всем сервисам стабильный идентификатор инстанса, чтобы в нагрузочных прогонах было видно fan-out между репликами.
/// </summary>
internal sealed class NodeIdentity
{
    public NodeIdentity(IHostEnvironment environment)
    {
        var hostName = Environment.GetEnvironmentVariable("HOSTNAME");
        NodeId = string.IsNullOrWhiteSpace(hostName)
            ? $"{environment.ApplicationName}-{Environment.MachineName}".ToLowerInvariant()
            : hostName.ToLowerInvariant();
    }

    public string NodeId { get; }
}
