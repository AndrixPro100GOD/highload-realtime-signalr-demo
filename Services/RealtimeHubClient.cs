using Highload.Realtime.Shared;
using MessagePack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace highload_realtime_signalr_demo.Services;

/// <summary>
/// Инкапсулирует клиентское SignalR-соединение для Blazor UI, чтобы страница не держала transport-логику внутри себя.
/// </summary>
public sealed class RealtimeHubClient : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<RealtimeHubClient> _logger;
    private readonly List<RealtimeEnvelope> _messages = [];
    private HubConnection? _connection;

    public RealtimeHubClient(
        NavigationManager navigationManager,
        ILogger<RealtimeHubClient> logger)
    {
        _navigationManager = navigationManager;
        _logger = logger;
    }

    /// <summary>
    /// Срабатывает при любом изменении статуса или получении сообщений.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Последние доставленные сообщения; список ограничен, чтобы UI сам не стал источником аллокаций.
    /// </summary>
    public IReadOnlyList<RealtimeEnvelope> Messages => _messages;

    /// <summary>
    /// Последнее контрольное событие от Hub.
    /// </summary>
    public HubControlEvent? LastControlEvent { get; private set; }

    /// <summary>
    /// Есть ли активное SignalR-соединение.
    /// </summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Текущий ConnectionId клиента, если соединение уже поднято.
    /// </summary>
    public string? ConnectionId => _connection?.ConnectionId;

    /// <summary>
    /// Поднимает SignalR-соединение с MessagePack и авто-reconnect.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                return;
            }

            await _connection.DisposeAsync();
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_navigationManager.ToAbsoluteUri(RealtimeRoutes.HubPath))
            .WithAutomaticReconnect([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .WithServerTimeout(TimeSpan.FromSeconds(30))
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
            })
            .Build();

        RegisterHandlers(_connection);
        await _connection.StartAsync(cancellationToken);
        LastControlEvent = await _connection.InvokeAsync<HubControlEvent>("GetConnectionInfo", cancellationToken);
        NotifyStateChanged();
    }

    /// <summary>
    /// Закрывает SignalR-соединение.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
        LastControlEvent = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Подписывает клиента на группу.
    /// </summary>
    public Task JoinGroupAsync(string groupName, CancellationToken cancellationToken = default)
        => InvokeHubAsync("JoinGroup", cancellationToken, groupName);

    /// <summary>
    /// Убирает клиента из группы.
    /// </summary>
    public Task LeaveGroupAsync(string groupName, CancellationToken cancellationToken = default)
        => InvokeHubAsync("LeaveGroup", cancellationToken, groupName);

    /// <summary>
    /// Отправляет broadcast всем клиентам.
    /// </summary>
    public Task<PublishAck> SendBroadcastAsync(RealtimePublishRequest request, CancellationToken cancellationToken = default)
        => InvokeHubAsync<PublishAck>("SendBroadcast", cancellationToken, request);

    /// <summary>
    /// Отправляет сообщение группе без очереди батчинга.
    /// </summary>
    public Task<PublishAck> SendGroupAsync(RealtimePublishRequest request, CancellationToken cancellationToken = default)
        => InvokeHubAsync<PublishAck>("SendToGroup", cancellationToken, request);

    /// <summary>
    /// Ставит сообщение в серверную очередь батчинга.
    /// </summary>
    public Task<PublishAck> QueueGroupAsync(RealtimePublishRequest request, CancellationToken cancellationToken = default)
        => InvokeHubAsync<PublishAck>("QueueGroupMessage", cancellationToken, request);

    /// <summary>
    /// Отправляет сообщение самому себе или другому ConnectionId.
    /// </summary>
    public Task<PublishAck> SendTargetedAsync(TargetedPublishRequest request, CancellationToken cancellationToken = default)
        => InvokeHubAsync<PublishAck>("SendToConnection", cancellationToken, request);

    /// <summary>
    /// Освобождает ресурсы клиента при завершении приложения или уходе со страницы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private void RegisterHandlers(HubConnection connection)
    {
        connection.On<RealtimeEnvelope>(nameof(IRealtimeClient.ReceiveMessage), envelope =>
        {
            AddEnvelope(envelope);
        });

        connection.On<RealtimeEnvelope[]>(nameof(IRealtimeClient.ReceiveBatch), envelopes =>
        {
            foreach (var envelope in envelopes)
            {
                AddEnvelope(envelope);
            }
        });

        connection.On<HubControlEvent>(nameof(IRealtimeClient.ReceiveControl), controlEvent =>
        {
            LastControlEvent = controlEvent;
            NotifyStateChanged();
        });

        connection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "Realtime client reconnecting.");
            NotifyStateChanged();
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            NotifyStateChanged();
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            _logger.LogWarning(error, "Realtime client closed.");
            NotifyStateChanged();
            return Task.CompletedTask;
        };
    }

    private void AddEnvelope(RealtimeEnvelope envelope)
    {
        _messages.Insert(0, envelope);

        if (_messages.Count > 200)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }

        NotifyStateChanged();
    }

    private Task InvokeHubAsync(string methodName, CancellationToken cancellationToken, params object?[] args)
    {
        return EnsureConnection().InvokeCoreAsync(methodName, args, cancellationToken);
    }

    private Task<T> InvokeHubAsync<T>(string methodName, CancellationToken cancellationToken, params object?[] args)
    {
        return EnsureConnection().InvokeCoreAsync<T>(methodName, args, cancellationToken);
    }

    private HubConnection EnsureConnection()
    {
        return _connection is { State: HubConnectionState.Connected }
            ? _connection
            : throw new InvalidOperationException("SignalR connection is not established.");
    }

    private void NotifyStateChanged() => Changed?.Invoke();
}
