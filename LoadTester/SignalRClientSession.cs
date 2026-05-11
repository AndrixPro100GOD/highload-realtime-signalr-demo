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
    private readonly string _trafficProfile;
    private int _disconnectRecorded;
    private DateTimeOffset _nextPublishAtUtc = DateTimeOffset.MinValue;
    private string _serverConnectionId = string.Empty;

    public SignalRClientSession(string baseUrl, string senderId, string groupName, string trafficProfile)
    {
        SenderId = senderId;
        _groupName = groupName;
        _trafficProfile = trafficProfile;

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

    public bool IsActive => _connection.State == HubConnectionState.Connected;

    public async Task StartAsync(int connectTimeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(connectTimeoutMs));

        await _connection.StartAsync(timeout.Token);
        await _connection.InvokeCoreAsync("JoinGroup", [_groupName], timeout.Token);
        var control = await _connection.InvokeCoreAsync<HubControlEvent>("GetConnectionInfo", [], timeout.Token);
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
            // Performance tuning: targeted профиль измеряет удержание тысяч WebSocket без лавины N*N fan-out.
            if (IsProfile("targeted"))
            {
                ack = await SendTargetedAsync(request, cancellationToken);
            }
            else if (batchEvery > 0 && sequenceNumber % batchEvery == 0)
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("QueueGroupMessage", [request], cancellationToken);
            }
            else if (IsProfile("broadcast") || (IsProfile("mixed") && sequenceNumber % 5 == 0))
            {
                ack = await _connection.InvokeCoreAsync<PublishAck>("SendBroadcast", [request], cancellationToken);
            }
            else if (IsProfile("mixed") && sequenceNumber % 3 == 0)
            {
                ack = await SendTargetedAsync(request, cancellationToken);
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

    internal TimeSpan GetPacingDelay(int sendIntervalMs)
    {
        if (sendIntervalMs <= 0)
        {
            return TimeSpan.Zero;
        }

        var delay = _nextPublishAtUtc - DateTimeOffset.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    internal void MarkPublishCompleted(int sendIntervalMs)
    {
        if (sendIntervalMs <= 0)
        {
            return;
        }

        // Performance tuning: планируем следующий publish отдельно от NBomber operation timeout.
        _nextPublishAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(sendIntervalMs);
    }

    internal bool TryMarkDisconnected()
    {
        return Interlocked.Exchange(ref _disconnectRecorded, 1) == 0;
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

        _connection.Closed += exception =>
        {
            foreach (var waiter in _awaiters.Values.ToList())
            {
                if (exception is null)
                {
                    waiter.TrySetCanceled();
                }
                else
                {
                    waiter.TrySetException(exception);
                }
            }

            return Task.CompletedTask;
        };
    }

    private void Complete(RealtimeEnvelope envelope)
    {
        if (_awaiters.TryRemove(envelope.SequenceNumber, out var waiter))
        {
            waiter.TrySetResult(envelope);
        }
    }

    private Task<PublishAck> SendTargetedAsync(RealtimePublishRequest request, CancellationToken cancellationToken)
    {
        return _connection.InvokeCoreAsync<PublishAck>("SendToConnection", [new TargetedPublishRequest
        {
            SenderId = request.SenderId,
            GroupName = request.GroupName,
            Payload = request.Payload,
            SequenceNumber = request.SequenceNumber,
            SentAtUtc = request.SentAtUtc,
            TargetConnectionId = ConnectionId
        }], cancellationToken);
    }

    private bool IsProfile(string profile)
    {
        return string.Equals(_trafficProfile, profile, StringComparison.OrdinalIgnoreCase);
    }
}
