using System.Diagnostics;
using Highload.Realtime.Shared;
using Microsoft.AspNetCore.SignalR;
using Server.Services;

namespace Server.Hubs;

/// <summary>
/// Hub для high-load демо: поддерживает broadcast, группы, targeted delivery и батчинг.
/// </summary>
internal sealed class RealtimeHub(
    RealtimeMetrics metrics,
    ConnectionRateLimiter rateLimiter,
    BatchedMessageDispatcher batchedDispatcher,
    NodeIdentity nodeIdentity,
    ILogger<RealtimeHub> logger) : Hub<IRealtimeClient>
{
    /// <summary>
    /// Отдаёт клиенту его идентификатор соединения и текущее состояние инстанса.
    /// </summary>
    public Task<HubControlEvent> GetConnectionInfo()
    {
        return Task.FromResult(CreateControlEvent("connection-info", "Текущее состояние Hub."));
    }

    /// <summary>
    /// Подписывает текущее соединение на группу.
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new HubException("Group name is required.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.Caller.ReceiveControl(CreateControlEvent("group-joined", $"Joined group '{groupName}'."));
    }

    /// <summary>
    /// Удаляет текущее соединение из группы.
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new HubException("Group name is required.");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        await Clients.Caller.ReceiveControl(CreateControlEvent("group-left", $"Left group '{groupName}'."));
    }

    /// <summary>
    /// Публикует сообщение всем клиентам, подключённым к текущему инстансу и другим репликам через Redis backplane.
    /// </summary>
    public async Task<PublishAck> SendBroadcast(RealtimePublishRequest request)
    {
        if (!TryAcceptRequest(request, out var rejectAck))
        {
            return rejectAck;
        }

        var stopwatch = Stopwatch.StartNew();
        var envelope = CreateEnvelope(request, RealtimeMessageKind.Broadcast);
        await Clients.All.ReceiveMessage(envelope);
        stopwatch.Stop();

        metrics.RecordPublished(request.Payload.Length * sizeof(char), stopwatch.Elapsed.TotalMilliseconds);
        metrics.RecordDelivered(1);

        return CreateAcceptedAck();
    }

    /// <summary>
    /// Публикует сообщение конкретной группе без промежуточной очереди.
    /// </summary>
    public async Task<PublishAck> SendToGroup(RealtimePublishRequest request)
    {
        if (!TryAcceptRequest(request, out var rejectAck))
        {
            return rejectAck;
        }

        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            return new PublishAck
            {
                Accepted = false,
                Reason = "GroupName is required for group delivery."
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var envelope = CreateEnvelope(request, RealtimeMessageKind.Group);
        await Clients.Group(request.GroupName).ReceiveMessage(envelope);
        stopwatch.Stop();

        metrics.RecordPublished(request.Payload.Length * sizeof(char), stopwatch.Elapsed.TotalMilliseconds);
        metrics.RecordDelivered(1);

        return CreateAcceptedAck();
    }

    /// <summary>
    /// Отправляет сообщение по конкретному ConnectionId. Используется для targeted latency-тестов.
    /// </summary>
    public async Task<PublishAck> SendToConnection(TargetedPublishRequest request)
    {
        if (!TryAcceptRequest(request.Payload, out var rejectAck))
        {
            return rejectAck;
        }

        if (string.IsNullOrWhiteSpace(request.TargetConnectionId))
        {
            return new PublishAck
            {
                Accepted = false,
                Reason = "TargetConnectionId is required for targeted delivery."
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var envelope = CreateEnvelope(new RealtimePublishRequest
        {
            SenderId = request.SenderId,
            GroupName = request.GroupName,
            Payload = request.Payload,
            SequenceNumber = request.SequenceNumber,
            SentAtUtc = request.SentAtUtc
        }, RealtimeMessageKind.Targeted);
        await Clients.Client(request.TargetConnectionId).ReceiveMessage(envelope);
        stopwatch.Stop();

        metrics.RecordPublished(request.Payload.Length * sizeof(char), stopwatch.Elapsed.TotalMilliseconds);
        metrics.RecordDelivered(1);

        return CreateAcceptedAck();
    }

    /// <summary>
    /// Складывает сообщение в bounded channel, чтобы сервер мог переживать bursts без лавины fan-out операций.
    /// </summary>
    public Task<PublishAck> QueueGroupMessage(RealtimePublishRequest request)
    {
        if (!TryAcceptRequest(request, out var rejectAck))
        {
            return Task.FromResult(rejectAck);
        }

        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            return Task.FromResult(new PublishAck
            {
                Accepted = false,
                Reason = "GroupName is required for batched delivery."
            });
        }

        return Task.FromResult(batchedDispatcher.Enqueue(request, Context.ConnectionId));
    }

    /// <summary>
    /// Увеличивает счётчик активных соединений и отправляет клиенту служебную информацию о текущем узле.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        metrics.ConnectionOpened();
        logger.LogInformation("SignalR connection opened: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.ReceiveControl(CreateControlEvent("connected", "Connection established."));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Освобождает ресурсы ограничителя и обновляет метрики после отключения клиента.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        rateLimiter.Release(Context.ConnectionId);
        metrics.ConnectionClosed();
        logger.LogInformation("SignalR connection closed: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private bool TryAcceptRequest(RealtimePublishRequest request, out PublishAck rejectAck)
    {
        return TryAcceptRequest(request.Payload, out rejectAck);
    }

    private bool TryAcceptRequest(string payload, out PublishAck rejectAck)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            rejectAck = new PublishAck
            {
                Accepted = false,
                Reason = "Payload is required."
            };

            return false;
        }

        if (!rateLimiter.TryAcquire(Context.ConnectionId))
        {
            metrics.RecordRateLimited();
            rejectAck = new PublishAck
            {
                Accepted = false,
                Reason = "Per-connection rate limit exceeded."
            };

            return false;
        }

        rejectAck = new PublishAck { Accepted = true };
        return true;
    }

    private RealtimeEnvelope CreateEnvelope(RealtimePublishRequest request, RealtimeMessageKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        return new RealtimeEnvelope
        {
            Kind = kind,
            SenderId = request.SenderId,
            GroupName = request.GroupName,
            Payload = request.Payload,
            SequenceNumber = request.SequenceNumber,
            SourceConnectionId = Context.ConnectionId,
            NodeId = nodeIdentity.NodeId,
            SentAtUtc = request.SentAtUtc,
            PublishedAtUtc = now,
            DeliveredAtUtc = now
        };
    }

    private PublishAck CreateAcceptedAck()
    {
        return new PublishAck
        {
            Accepted = true,
            ServerTimeUtc = DateTimeOffset.UtcNow
        };
    }

    private HubControlEvent CreateControlEvent(string eventType, string message)
    {
        return new HubControlEvent
        {
            EventType = eventType,
            ConnectionId = Context.ConnectionId,
            NodeId = nodeIdentity.NodeId,
            ActiveConnections = metrics.ActiveConnections,
            QueueDepth = metrics.BatchQueueDepth,
            Message = message
        };
    }
}
