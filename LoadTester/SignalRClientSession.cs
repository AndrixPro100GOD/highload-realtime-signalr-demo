using System.Collections.Concurrent;
using Highload.Realtime.Shared;
using MessagePack;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace LoadTester;

/// <summary>
/// Держит долгоживущее SignalR-соединение для одного NBomber virtual user и ждёт round-trip ответов по sequence number.
/// </summary>
internal sealed class SignalRClientSession : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RealtimeEnvelope>> _awaiters = new();
    private readonly HubConnection _connection;
    private readonly string _groupName;
    private string _serverConnectionId = string.Empty;

    public SignalRClientSession(string baseUrl, string senderId, string groupName)
    {
        SenderId = senderId;
        _groupName = groupName;

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(baseUrl, UriKind.Absolute), RealtimeRoutes.HubPath), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .WithServerTimeout(TimeSpan.FromSeconds(30))
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
            })
            .Build();

        RegisterHandlers();
    }

    public string SenderId { get; }

    public string ConnectionId => string.IsNullOrWhiteSpace(_serverConnectionId)
        ? _connection.ConnectionId ?? string.Empty
        : _serverConnectionId;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _connection.StartAsync(cancellationToken);
        await _connection.InvokeCoreAsync("JoinGroup", [_groupName], cancellationToken);
        var control = await _connection.InvokeCoreAsync<HubControlEvent>("GetConnectionInfo", [], cancellationToken);
        _serverConnectionId = control.ConnectionId;
    }

    public async Task<PublishAck> PublishAndWaitAsync(
        long sequenceNumber,
        int payloadBytes,
        int batchEvery,
        int receiveTimeoutMs,
        CancellationToken cancellationToken)
    {
        var payload = new string('x', Math.Max(payloadBytes, 16));
        var request = new RealtimePublishRequest
        {
            SenderId = SenderId,
            GroupName = _groupName,
            Payload = payload,
            SequenceNumber = sequenceNumber,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        var waiter = new TaskCompletionSource<RealtimeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaiters[sequenceNumber] = waiter;

        try
        {
            PublishAck ack;
            if (sequenceNumber % batchEvery == 0)
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("QueueGroupMessage", [request], cancellationToken);
            }
            else if (sequenceNumber % 5 == 0)
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("SendBroadcast", [request], cancellationToken);
            }
            else if (sequenceNumber % 3 == 0)
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("SendToConnection", [new TargetedPublishRequest
                {
                    SenderId = request.SenderId,
                    GroupName = request.GroupName,
                    Payload = request.Payload,
                    SequenceNumber = request.SequenceNumber,
                    SentAtUtc = request.SentAtUtc,
                    TargetConnectionId = ConnectionId
                }], cancellationToken);
            }
            else
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("SendToGroup", [request], cancellationToken);
            }

            if (!ack.Accepted)
            {
                return ack;
            }

            try
            {
                // Отдельно различаем timeout ожидания round-trip и внешнюю остановку сценария,
                // чтобы NBomber не смешивал медленные ответы с реальными exception.
                _ = await waiter.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(receiveTimeoutMs),
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"Round-trip timeout for sequence {sequenceNumber} after {receiveTimeoutMs} ms.",
                    exception);
            }

            return ack;
        }
        finally
        {
            _awaiters.TryRemove(sequenceNumber, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var waiter in _awaiters.Values)
        {
            waiter.TrySetCanceled();
        }

        await _connection.DisposeAsync();
    }

    private void RegisterHandlers()
    {
        _connection.On<RealtimeEnvelope>(nameof(IRealtimeClient.ReceiveMessage), envelope => Complete(envelope));
        _connection.On<RealtimeEnvelope[]>(nameof(IRealtimeClient.ReceiveBatch), envelopes =>
        {
            foreach (var envelope in envelopes)
            {
                Complete(envelope);
            }
        });
    }

    private void Complete(RealtimeEnvelope envelope)
    {
        if (_awaiters.TryRemove(envelope.SequenceNumber, out var waiter))
        {
            waiter.TrySetResult(envelope);
        }
    }
}
