using System.Threading.Channels;
using Highload.Realtime.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Server.Hubs;
using Server.Options;

namespace Server.Services;

/// <summary>
/// Собирает burst-трафик в короткие батчи, чтобы уменьшить число fan-out операций и аллокаций.
/// </summary>
internal sealed class BatchedMessageDispatcher : BackgroundService
{
    private readonly BatchingOptions _options;
    private readonly Channel<RealtimeEnvelope> _queue;
    private readonly IHubContext<RealtimeHub, IRealtimeClient> _hubContext;
    private readonly RealtimeMetrics _metrics;
    private readonly NodeIdentity _nodeIdentity;
    private int _queueDepth;

    public BatchedMessageDispatcher(
        IOptions<RealtimeServerOptions> options,
        IHubContext<RealtimeHub, IRealtimeClient> hubContext,
        RealtimeMetrics metrics,
        NodeIdentity nodeIdentity)
    {
        _options = options.Value.Batching;
        _hubContext = hubContext;
        _metrics = metrics;
        _nodeIdentity = nodeIdentity;

        _queue = Channel.CreateBounded<RealtimeEnvelope>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public PublishAck Enqueue(RealtimePublishRequest request, string sourceConnectionId)
    {
        if (_options.QueueCapacity > 0)
        {
            var saturation = (double)Volatile.Read(ref _queueDepth) / _options.QueueCapacity;
            if (saturation >= _options.DropThresholdRatio)
            {
                _metrics.RecordDropped("queue_high_watermark");
                return new PublishAck
                {
                    Accepted = false,
                    Reason = "Queue is saturated and batching entered graceful degradation mode."
                };
            }
        }

        var publishedAt = DateTimeOffset.UtcNow;
        var envelope = new RealtimeEnvelope
        {
            Kind = RealtimeMessageKind.Batch,
            SenderId = request.SenderId,
            GroupName = request.GroupName,
            Payload = request.Payload,
            SequenceNumber = request.SequenceNumber,
            SourceConnectionId = sourceConnectionId,
            NodeId = _nodeIdentity.NodeId,
            SentAtUtc = request.SentAtUtc,
            PublishedAtUtc = publishedAt,
            DeliveredAtUtc = publishedAt
        };

        if (!_queue.Writer.TryWrite(envelope))
        {
            _metrics.RecordDropped("queue_full");
            return new PublishAck
            {
                Accepted = false,
                Reason = "Batch queue is full."
            };
        }

        var depth = Interlocked.Increment(ref _queueDepth);
        _metrics.SetBatchQueueDepth(depth);

        return new PublishAck
        {
            Accepted = true,
            ServerTimeUtc = publishedAt
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushInterval = TimeSpan.FromMilliseconds(_options.FlushIntervalMs);
        var buffer = new List<RealtimeEnvelope>(_options.MaxBatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _queue.Reader.WaitToReadAsync(stoppingToken))
                {
                    continue;
                }

                while (_queue.Reader.TryRead(out var item) && buffer.Count < _options.MaxBatchSize)
                {
                    buffer.Add(item);
                    UpdateQueueDepth(-1);
                }

                if (buffer.Count < _options.MaxBatchSize)
                {
                    try
                    {
                        await Task.Delay(flushInterval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        await FlushPendingAsync(buffer);
                        break;
                    }

                    while (_queue.Reader.TryRead(out var item) && buffer.Count < _options.MaxBatchSize)
                    {
                        buffer.Add(item);
                        UpdateQueueDepth(-1);
                    }
                }

                if (buffer.Count == 0)
                {
                    continue;
                }

                await FlushAsync(buffer, stoppingToken);
                buffer.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Нормальный путь остановки hosted service.
        }
    }

    private async Task FlushPendingAsync(List<RealtimeEnvelope> buffer)
    {
        // Во время graceful shutdown дочищаем уже набранный буфер и остаток очереди,
        // чтобы не терять сообщения, которые успели попасть в batching pipeline.
        while (true)
        {
            while (_queue.Reader.TryRead(out var item) && buffer.Count < _options.MaxBatchSize)
            {
                buffer.Add(item);
                UpdateQueueDepth(-1);
            }

            if (buffer.Count == 0)
            {
                return;
            }

            await FlushAsync(buffer, CancellationToken.None);
            buffer.Clear();
        }
    }

    private async Task FlushAsync(List<RealtimeEnvelope> batch, CancellationToken cancellationToken)
    {
        var deliveredAt = DateTimeOffset.UtcNow;
        var routed = new Dictionary<string, List<RealtimeEnvelope>>(StringComparer.Ordinal);

        foreach (var envelope in batch)
        {
            var routedEnvelope = new RealtimeEnvelope
            {
                Kind = envelope.Kind,
                SenderId = envelope.SenderId,
                GroupName = envelope.GroupName,
                Payload = envelope.Payload,
                SequenceNumber = envelope.SequenceNumber,
                SourceConnectionId = envelope.SourceConnectionId,
                NodeId = envelope.NodeId,
                SentAtUtc = envelope.SentAtUtc,
                PublishedAtUtc = envelope.PublishedAtUtc,
                DeliveredAtUtc = deliveredAt
            };

            var key = routedEnvelope.GroupName ?? string.Empty;
            if (!routed.TryGetValue(key, out var list))
            {
                list = [];
                routed[key] = list;
            }

            list.Add(routedEnvelope);
            _metrics.RecordPublished(
                payloadBytes: routedEnvelope.Payload.Length * sizeof(char),
                latencyMs: (deliveredAt - routedEnvelope.PublishedAtUtc).TotalMilliseconds);
        }

        foreach (var (groupName, envelopes) in routed)
        {
            var payload = envelopes.ToArray();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                await _hubContext.Clients.All.ReceiveBatch(payload);
            }
            else
            {
                await _hubContext.Clients.Group(groupName).ReceiveBatch(payload);
            }

            _metrics.RecordBatchSize(payload.Length);
            _metrics.RecordDelivered(payload.Length);
        }
    }

    private void UpdateQueueDepth(int delta)
    {
        var depth = Interlocked.Add(ref _queueDepth, delta);
        _metrics.SetBatchQueueDepth(depth);
    }
}
