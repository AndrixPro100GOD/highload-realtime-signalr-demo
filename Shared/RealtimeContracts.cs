using System.Diagnostics.CodeAnalysis;
using MessagePack;

namespace Highload.Realtime.Shared;

/// <summary>
/// Хранит публичные маршруты и имена групп для real-time демо.
/// </summary>
public static class RealtimeRoutes
{
    /// <summary>
    /// Маршрут SignalR Hub, который используют UI, NBomber и k6.
    /// </summary>
    public const string HubPath = "/hubs/realtime";

    /// <summary>
    /// Базовая группа для быстрых smoke-тестов и простого broadcast.
    /// </summary>
    public const string DefaultGroup = "benchmark";
}

/// <summary>
/// Типы сообщений, чтобы клиент мог отделять обычные сообщения от батчей и служебных событий.
/// </summary>
public enum RealtimeMessageKind
{
    Broadcast = 0,
    Group = 1,
    Targeted = 2,
    Batch = 3,
    Control = 4
}

 [MessagePackObject]
/// <summary>
/// Запрос на отправку сообщения в Hub.
/// </summary>
public class RealtimePublishRequest
{
    [Key(0)]
    /// <summary>
    /// Логический идентификатор отправителя, полезный для нагрузки и трассировки.
    /// </summary>
    public string SenderId { get; init; } = string.Empty;

    [Key(1)]
    /// <summary>
    /// Целевая группа. Для broadcast может быть пустой.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    [Key(2)]
    /// <summary>
    /// Полезная нагрузка сообщения.
    /// </summary>
    public string Payload { get; init; } = string.Empty;

    [Key(3)]
    /// <summary>
    /// Порядковый номер, чтобы нагрузочные клиенты могли проверять пропуски.
    /// </summary>
    public long SequenceNumber { get; init; }

    [Key(4)]
    /// <summary>
    /// Клиентское время отправки для расчёта задержки end-to-end.
    /// </summary>
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

 [MessagePackObject]
/// <summary>
/// Запрос на целевую доставку по конкретному соединению.
/// </summary>
public sealed class TargetedPublishRequest
{
    [Key(0)]
    /// <summary>
    /// Логический идентификатор отправителя, полезный для нагрузки и трассировки.
    /// </summary>
    public string SenderId { get; init; } = string.Empty;

    [Key(1)]
    /// <summary>
    /// Целевая группа. Для targeted-сценария нужна только для единообразия метрик.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    [Key(2)]
    /// <summary>
    /// Полезная нагрузка сообщения.
    /// </summary>
    public string Payload { get; init; } = string.Empty;

    [Key(3)]
    /// <summary>
    /// Порядковый номер, чтобы нагрузочные клиенты могли проверять пропуски.
    /// </summary>
    public long SequenceNumber { get; init; }

    [Key(4)]
    /// <summary>
    /// Клиентское время отправки для расчёта задержки end-to-end.
    /// </summary>
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [Key(5)]
    /// <summary>
    /// Идентификатор целевого SignalR-соединения.
    /// </summary>
    public string TargetConnectionId { get; init; } = string.Empty;
}

 [MessagePackObject]
/// <summary>
/// Результат приёма сообщения сервером. Нужен для backpressure и rate limiting.
/// </summary>
public sealed class PublishAck
{
    [Key(0)]
    /// <summary>
    /// Показывает, был ли запрос принят в обработку.
    /// </summary>
    public bool Accepted { get; init; }

    [Key(1)]
    /// <summary>
    /// Причина отказа, если сообщение было отброшено.
    /// </summary>
    public string? Reason { get; init; }

    [Key(2)]
    /// <summary>
    /// Время сервера, чтобы нагрузочный клиент мог коррелировать события.
    /// </summary>
    public DateTimeOffset ServerTimeUtc { get; init; } = DateTimeOffset.UtcNow;
}

 [MessagePackObject]
/// <summary>
/// Сообщение, доставленное клиенту.
/// </summary>
public sealed class RealtimeEnvelope
{
    [Key(0)]
    /// <summary>
    /// Тип доставленного сообщения.
    /// </summary>
    public RealtimeMessageKind Kind { get; init; }

    [Key(1)]
    /// <summary>
    /// Логический отправитель сообщения.
    /// </summary>
    public string SenderId { get; init; } = string.Empty;

    [Key(2)]
    /// <summary>
    /// Группа, в которую было отправлено сообщение.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    [Key(3)]
    /// <summary>
    /// Полезная нагрузка.
    /// </summary>
    public string Payload { get; init; } = string.Empty;

    [Key(4)]
    /// <summary>
    /// Порядковый номер, переданный клиентом.
    /// </summary>
    public long SequenceNumber { get; init; }

    [Key(5)]
    /// <summary>
    /// Соединение, которое инициировало публикацию.
    /// </summary>
    public string SourceConnectionId { get; init; } = string.Empty;

    [Key(6)]
    /// <summary>
    /// Инстанс приложения, который обработал публикацию.
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    [Key(7)]
    /// <summary>
    /// Время исходной отправки на клиенте.
    /// </summary>
    public DateTimeOffset SentAtUtc { get; init; }

    [Key(8)]
    /// <summary>
    /// Время публикации на сервере.
    /// </summary>
    public DateTimeOffset PublishedAtUtc { get; init; }

    [Key(9)]
    /// <summary>
    /// Время доставки на серверной стороне callback.
    /// </summary>
    public DateTimeOffset DeliveredAtUtc { get; init; }

    [IgnoreMember]
    /// <summary>
    /// Используется для грубой оценки размера без сериализации.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Payload))]
    public bool HasPayload => !string.IsNullOrEmpty(Payload);
}

 [MessagePackObject]
/// <summary>
/// Служебное событие от Hub: подключение, состояние очереди, текущий инстанс.
/// </summary>
public sealed class HubControlEvent
{
    [Key(0)]
    /// <summary>
    /// Тип контрольного сообщения.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    [Key(1)]
    /// <summary>
    /// Соединение, к которому относится событие.
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;

    [Key(2)]
    /// <summary>
    /// Инстанс приложения, отправивший событие.
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    [Key(3)]
    /// <summary>
    /// Текущее число активных соединений на инстансе.
    /// </summary>
    public long ActiveConnections { get; init; }

    [Key(4)]
    /// <summary>
    /// Текущая глубина очереди батчинга на инстансе.
    /// </summary>
    public int QueueDepth { get; init; }

    [Key(5)]
    /// <summary>
    /// Дополнительное описание события.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    [Key(6)]
    /// <summary>
    /// Время формирования события.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Контракт серверных callback-методов для SignalR клиентов.
/// </summary>
public interface IRealtimeClient
{
    /// <summary>
    /// Доставляет одиночное сообщение.
    /// </summary>
    Task ReceiveMessage(RealtimeEnvelope envelope);

    /// <summary>
    /// Доставляет батч сообщений, чтобы снизить fan-out и аллокации на высокой частоте.
    /// </summary>
    Task ReceiveBatch(RealtimeEnvelope[] envelopes);

    /// <summary>
    /// Передаёт служебное событие о состоянии Hub.
    /// </summary>
    Task ReceiveControl(HubControlEvent controlEvent);
}
