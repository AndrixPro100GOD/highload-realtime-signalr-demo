# Архитектура проекта

Документ описывает уже **реальную** архитектуру репозитория после внедрения server-hosted Blazor WASM, SignalR, Redis backplane и observability stack.

## Цели

| Цель | Описание |
|---|---|
| Throughput | десятки тысяч сообщений в секунду при small payload |
| Concurrency | десятки тысяч concurrent WebSocket connections |
| Scale-out | несколько инстансов приложения за L7 балансировщиком |
| Observability | метрики приложения, runtime и Redis без «чёрного ящика» |

## Текущее состояние

Репозиторий состоит из четырёх основных частей:

1. `Server/` — ASP.NET Core host, SignalR Hub, batching, rate limiting, health, OpenTelemetry.
2. `Shared/` — DTO и контракты SignalR callbacks.
3. `highload-realtime-signalr-demo.csproj` — Blazor WASM UI и ручной smoke-клиент.
4. `LoadTester/` — NBomber-based генератор нагрузки.

## Логическая схема

```text
                        ┌────────────────────┐
                        │  nginx (L7 / hash) │
                        │ session affinity    │
                        └─────────┬──────────┘
                                  │
                 ┌────────────────┼────────────────┐
                 ▼                ▼                ▼
           ┌──────────┐     ┌──────────┐     ┌──────────┐
           │ app #1   │     │ app #2   │     │ app #N   │
           │ Kestrel  │     │ Kestrel  │     │ Kestrel  │
           │ SignalR  │     │ SignalR  │     │ SignalR  │
           └────┬─────┘     └────┬─────┘     └────┬─────┘
                │                │                │
                └────────────────┼────────────────┘
                                 │
                        ┌────────▼────────┐
                        │ Redis backplane │
                        └────────┬────────┘
                                 │
               ┌─────────────────┼─────────────────┐
               ▼                 ▼                 ▼
         Prometheus         redis_exporter      Grafana
```

## Потоки данных

### Broadcast / group

1. Клиент подключается к `nginx`.
2. `nginx` маршрутизирует upgrade на один `app` инстанс.
3. Hub публикует сообщение локально.
4. Redis backplane размножает событие на остальные инстансы.
5. Каждый инстанс доставляет сообщение своим локальным клиентам.

### Batched traffic

1. Burst-публикации попадают в bounded channel.
2. `BatchedMessageDispatcher` агрегирует их короткими окнами.
3. Hub отправляет `ReceiveBatch(...)` вместо множества одиночных fan-out операций.

### Backpressure

1. Переполненная batch queue начинает отклонять часть трафика.
2. Per-connection rate limiter режет шумных клиентов.
3. Метрики `signalr_batch_queue_depth`, `signalr_messages_dropped_total`, `signalr_requests_rate_limited_total` показывают момент деградации.

## Почему так

### Hosted Blazor WASM

- UI и backend остаются в одном решении.
- Сервер может сразу раздавать WASM-ассеты и Hub endpoint.
- Удобно для локального smoke и портфолио-демо.

### Redis backplane

- Даёт простую и понятную модель scale-out для SignalR.
- Позволяет воспроизвести multi-instance сценарий локально через `docker compose`.

### nginx как локальный L7

- Не требует docker provider.
- Для локального compose достаточно affinity по `remote_addr`.
- Прост в сопровождении и хорошо показывает сам факт L7-посредника перед SignalR.

## Observability

Приложение публикует:

- `signalr_active_connections`
- `signalr_messages_published_total`
- `signalr_messages_delivered_total`
- `signalr_messages_dropped_total`
- `signalr_requests_rate_limited_total`
- `signalr_publish_latency_ms`
- `signalr_batch_queue_depth`
- `process_working_set_bytes`
- `process_cpu_time_seconds_total`

Redis метрики идут через `redis_exporter`.

## Что менять дальше

Для реального 100k-1M scale этот compose-стек уже будет тесен. Следующий шаг:

1. Kubernetes / Nomad deployment.
2. Настоящий sticky-cookie ingress.
3. Redis Cluster или managed SignalR.
4. Несколько load-generator нод.

Смежные документы: [setup.md](./setup.md), [performance.md](./performance.md), [tech-stack.md](./tech-stack.md), [ADR 0001](../adr/0001-use-signalr-with-redis-backplane.md).
