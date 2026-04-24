using Highload.Realtime.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using highload_realtime_signalr_demo.Services;

namespace highload_realtime_signalr_demo.Pages;

/// <summary>
/// Управляет интерактивной страницей для ручной проверки high-load Hub сценариев.
/// </summary>
public partial class Realtime : ComponentBase, IDisposable
{
    private string _senderId = $"browser-{Guid.NewGuid():N}".Substring(0, 16);
    private string _groupName = RealtimeRoutes.DefaultGroup;
    private string _payload = "ping";
    private string _targetConnectionId = string.Empty;
    private string _lastAckMessage = "Клиент ещё не подключён.";
    private Severity _alertSeverity = Severity.Info;

    [Inject]
    public RealtimeHubClient HubClient { get; set; } = default!;

    protected HubControlEvent? ControlEvent => HubClient.LastControlEvent;

    protected IReadOnlyList<RealtimeEnvelope> RecentMessages => HubClient.Messages.Take(30).ToArray();

    protected Color StatusColor => HubClient.IsConnected ? Color.Success : Color.Default;

    protected string StatusText => HubClient.IsConnected ? "Connected" : "Disconnected";

    protected Severity AlertSeverity => _alertSeverity;

    protected override void OnInitialized()
    {
        HubClient.Changed += OnClientChanged;
    }

    public void Dispose()
    {
        HubClient.Changed -= OnClientChanged;
    }

    protected async Task ConnectAsync()
    {
        await HubClient.ConnectAsync();
        _targetConnectionId = HubClient.ConnectionId ?? _targetConnectionId;
        SetAckMessage(true, "SignalR соединение поднято.");
    }

    protected async Task DisconnectAsync()
    {
        await HubClient.DisconnectAsync();
        SetAckMessage(true, "SignalR соединение закрыто.");
    }

    protected async Task JoinGroupAsync()
    {
        await HubClient.JoinGroupAsync(_groupName);
        SetAckMessage(true, $"Клиент подписан на группу '{_groupName}'.");
    }

    protected async Task LeaveGroupAsync()
    {
        await HubClient.LeaveGroupAsync(_groupName);
        SetAckMessage(true, $"Клиент удалён из группы '{_groupName}'.");
    }

    protected async Task SendBroadcastAsync()
    {
        var ack = await HubClient.SendBroadcastAsync(BuildRequest());
        ApplyAck(ack, "Broadcast отправлен.");
    }

    protected async Task SendGroupAsync()
    {
        var ack = await HubClient.SendGroupAsync(BuildRequest());
        ApplyAck(ack, $"Сообщение в группу '{_groupName}' отправлено.");
    }

    protected async Task QueueGroupAsync()
    {
        var ack = await HubClient.QueueGroupAsync(BuildRequest());
        ApplyAck(ack, $"Сообщение поставлено в batch-queue для группы '{_groupName}'.");
    }

    protected async Task SendTargetedAsync()
    {
        var targetConnectionId = string.IsNullOrWhiteSpace(_targetConnectionId)
            ? HubClient.ConnectionId ?? string.Empty
            : _targetConnectionId;

        var ack = await HubClient.SendTargetedAsync(new TargetedPublishRequest
        {
            SenderId = _senderId,
            GroupName = _groupName,
            Payload = _payload,
            SequenceNumber = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SentAtUtc = DateTimeOffset.UtcNow,
            TargetConnectionId = targetConnectionId
        });

        ApplyAck(ack, $"Targeted message отправлено в '{targetConnectionId}'.");
    }

    private RealtimePublishRequest BuildRequest()
    {
        return new RealtimePublishRequest
        {
            SenderId = _senderId,
            GroupName = _groupName,
            Payload = _payload,
            SequenceNumber = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SentAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void ApplyAck(PublishAck ack, string successMessage)
    {
        SetAckMessage(ack.Accepted, ack.Accepted ? successMessage : ack.Reason ?? "Сервер отклонил сообщение.");
    }

    private void SetAckMessage(bool success, string message)
    {
        _alertSeverity = success ? Severity.Success : Severity.Error;
        _lastAckMessage = message;
    }

    private void OnClientChanged()
    {
        _targetConnectionId = HubClient.ConnectionId ?? _targetConnectionId;
        _ = InvokeAsync(StateHasChanged);
    }
}
