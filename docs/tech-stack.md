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
| HAProxy | `haproxy:3.0-alpine` | L4 TCP entrypoint, media bypass, stats |
| nginx | `nginx:1.27-alpine` | L7 балансировщик, WebSocket proxy, sticky, REST/media routing |

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

### HAProxy + nginx

- `HAProxy` держит внешний L4-слой и не смешивает TCP edge с HTTP path-routing;
- `nginx` остаётся L7-местом для sticky sessions, WebSocket upgrade, `/api/*` и `/video/*` fallback;
- прямой путь `:8081 -> video-svc` показывает, как media workload можно вывести из smart L7 path.

## Что сознательно упрощено

- доменная модель минимальная;
- PostgreSQL и `api-svc` не участвуют в hot path сообщений;
- media в `video-svc` — демо VOD: один `sample.mp4` + `index.m3u8` с byte-range (`Accept-Ranges`), без `.ts` сегментов;
- docker-compose service discovery проще, чем Kubernetes/Nomad deployment.

## Связанные документы

- [Архитектура](./architecture.md)
- [Запуск](./setup.md)
- [Нагрузочные прогоны](./performance.md)
