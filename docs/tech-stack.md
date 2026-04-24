# Технологический стек

Краткая карта технологий, которые уже реально используются в проекте.

## Основной стек

| Технология | Версия / пакет | Роль |
|---|---|---|
| .NET | 10.0 | единая платформа для server, client и load tester |
| ASP.NET Core | `Server/` | Kestrel, middleware, health, hosting |
| SignalR | `Microsoft.AspNetCore.SignalR.*` | real-time transport, группы, broadcast, targeted delivery |
| MessagePack | `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`, `MessagePack` | компактный бинарный протокол |
| StackExchange.Redis | через `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | backplane для multi-instance fan-out |
| Blazor WebAssembly | `highload-realtime-signalr-demo.csproj` | UI и ручной smoke-клиент |
| MudBlazor | `9.*` | быстрый UI без ручной верстки |
| NBomber | `LoadTester/` | self-load на реальных SignalR connections |
| k6 | `tests/load/signalr.js` | альтернативный WebSocket/SignalR probe |
| OpenTelemetry | `1.15.x` | метрики приложения и runtime |
| Prometheus | compose | scrape `/metrics` |
| Grafana | compose | dashboards |
| PostgreSQL | compose + `Npgsql` | вспомогательная персистентность и readiness |
| nginx | compose | локальный L7 балансировщик |

## Почему именно так

### ASP.NET Core + SignalR

- в .NET это самый практичный способ быстро получить production-friendly WebSocket abstraction;
- есть встроенная модель групп, reconnect semantics и поддержка scale-out;
- хорошо читается как демо для high-load .NET backend.

### MessagePack

- меньше payload;
- меньше сетевого шума и аллокаций;
- хорошо подходит для массового fan-out и synthetic load.

### Redis backplane

- простой локальный scale-out path;
- понятный pub/sub слой между инстансами;
- позволяет показать bottleneck не только в Hub, но и во внешней шине.

### NBomber + k6 вместе

- `NBomber` нужен для реалистичного SignalR-клиента и точного round-trip latency;
- `k6` удобен как дополнительная проверка infra path и WebSocket-проб.

### OpenTelemetry + Prometheus + Grafana

- дают быстрое локальное observability-окружение;
- позволяют видеть не только RPS, но и queue depth, rate limiting, Redis saturation, CPU и память.

## Что сознательно упрощено

- доменная модель минимальная;
- PostgreSQL не участвует в hot path сообщений;
- nginx используется как локальный L7, а не как финальный production ingress для 1M+ scale.

## Связанные документы

- [Архитектура](./architecture.md)
- [Запуск](./setup.md)
- [Нагрузочные прогоны](./performance.md)
