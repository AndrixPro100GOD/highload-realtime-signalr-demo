<div align="center">

# highload-realtime-signalr-demo

**High-load real-time demo на .NET 10: SignalR, Redis backplane, HAProxy L4, Nginx L7, NBomber, Prometheus, Grafana**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?logo=blazor)](https://learn.microsoft.com/aspnet/core/blazor/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[Архитектура](docs/architecture.md) · [Запуск](docs/setup.md) · [Публикация Docker](docs/docker-publish.md) · [ADR 0003](adr/0003-host-deploy-and-load-testing-direction.md) · [Производительность](docs/performance.md) · [Стек](docs/tech-stack.md) · [ADR](adr/)

</div>

## О проекте
Репозиторий теперь содержит полноценный high-load стек:

- `Server/` на ASP.NET Core + SignalR + MessagePack + Redis backplane (`realtime-svc`).
- `Api/` как лёгкий ASP.NET Core Minimal API (`api-svc`) для обычного REST-трафика.
- `highload-realtime-signalr-demo.csproj` как Blazor WebAssembly UI с ручным smoke-клиентом на `/realtime`.
- `LoadTester/` на NBomber для self-load тестов.
- `tests/load/signalr.js` для альтернативного k6/WebSocket прогона.
- `docker-compose.yml` для локального hybrid scale-out с `haproxy`, `nginx-l7`, `realtime-svc`, `api-svc`, `video-svc`, `redis`, `postgres`, `prometheus`, `grafana`.

Фокус проекта тот же: десятки тысяч concurrent соединений, высокий fan-out, предсказуемое масштабирование и нормальная observability вместо «магии на веру».

## Быстрый старт
Локальный single-instance smoke:

```bash
dotnet restore
dotnet run --project Server/Server.csproj
```

UI и ручной SignalR smoke доступны на [http://localhost:8080/realtime](http://localhost:8080/realtime).

Self-load через NBomber:

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --ramp-up=60 --steady=120 --ramp-down=15
```

Полный docker stack:

```bash
docker compose up --build -d --scale realtime-svc=3 --scale nginx-l7=2 --scale api-svc=2
```

Публикация образов на отдельный Docker host:

```powershell
.\build-push-to-host.bat
.\deploy-to-host.bat
```

Подробно: [docs/docker-publish.md](docs/docker-publish.md).

После старта:

- приложение через HAProxy L4 + Nginx L7: [http://localhost:8080](http://localhost:8080)
- media bypass через HAProxy L4 напрямую в `video-svc`: [http://localhost:8081/video/index.m3u8](http://localhost:8081/video/index.m3u8)
- HAProxy stats: [http://localhost:8404/stats](http://localhost:8404/stats)
- Grafana: [http://localhost:3000](http://localhost:3000) (`admin` / `admin`)
- Prometheus: [http://localhost:9090](http://localhost:9090)
- Redis exporter: [http://localhost:9121/metrics](http://localhost:9121/metrics)

## Performance & Load Testing
### Что уже встроено

- Kestrel тюнинг: `MaxConcurrentConnections`, `MaxConcurrentUpgradedConnections`, `KeepAliveTimeout`, socket backlog, min thread pool.
- SignalR тюнинг: `MessagePack`, bounded batching queue, per-connection rate limiting, backpressure и graceful degradation.
- Redis backplane с `ConfigurationOptions`, `AbortOnConnectFail=false`, `ReconnectRetryPolicy=ExponentialRetry(...)`.
- OpenTelemetry metrics + `/metrics` endpoint + Redis exporter + готовый Grafana dashboard.
- NBomber self-load с persistent SignalR clients, warm-up/ramp-up, `--send-interval-ms` pacing, broadcast/group/targeted/batched traffic.

### Команды self-load

Через `Makefile` (опционально; на Windows без `make` используйте прямые команды из [docs/testing.md](docs/testing.md)):

```bash
make build
make run-server
make loadtest CONNECTIONS=1000 RAMP_UP=60 STEADY=120 RAMP_DOWN=15
make loadtest-light
make loadtest-medium
make loadtest-heavy
make loadtest-fanout CONNECTIONS=1000
make k6 CONNECTIONS=200 RAMP_UP=30 STEADY=60 RAMP_DOWN=10
make compose-up
make compose-up-hybrid APP_SCALE=5 NGINX_SCALE=2 API_SCALE=2
make loadtest-api CONNECTIONS=100
make loadtest-video-smoke
```

Без `make`:

```bash
dotnet run --project Server/Server.csproj
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --warm-up=30 --warm-up-connections=100 --ramp-up=60 --steady=120 --ramp-down=20 --payload-bytes=64 --send-interval-ms=1000 --traffic-profile=targeted
k6 run -e BASE_URL=http://localhost:8080 -e VUS=200 tests/load/signalr.js
k6 run -e BASE_URL=http://localhost:8080 -e VUS=100 tests/load/api-smoke.js
docker compose up --build -d --scale realtime-svc=3 --scale nginx-l7=2 --scale api-svc=2
```

### Performance tuning

После тяжёлого прогона на `5000` соединений добавлен отдельный tuning-профиль: Kestrel/ThreadPool/GC/SignalR/Redis/proxy/resource limits, baseline pacing `--send-interval-ms=1000` и сценарии `light`, `medium`, `heavy`, `fanout`. Подробности: [docs/performance-tuning.md](docs/performance-tuning.md).

### Базовые локальные результаты

Ниже не synthetic guess, а короткие реальные прогоны на текущей машине. Это не «максимум железа», а sanity baseline после интеграции:

| Конфигурация | Сценарий | Итог |
|---|---|---|
| **1 инстанс, `dotnet run`** | NBomber `5 connections / 3s ramp / 5s steady / 2s down / 64B payload` | **2300 ok**, **~230 RPS**, mean **15.67 ms**, p95 **62.94 ms**, fail **0** |
| **3 инстанса, docker compose + nginx + Redis** | NBomber `20 connections / 4s ramp / 8s steady / 2s down / 96B payload` | **16312 ok**, **~1165 RPS**, mean **13.02 ms**, p95 **51.62 ms**, fail **0** |

Это именно smoke/baseline, а не предел. Для «настоящих» high-load прогонов нужно поднимать `CONNECTIONS` на порядки выше и смотреть OS limits, Docker Desktop overhead и Redis saturation.

### Ожидаемые результаты

Типичный ноутбук класса 8C/16T, 32 GB RAM, локальный Docker Desktop:

- **1 инстанс**: порядок **5k-20k concurrent WebSocket connections** и **5k-30k msg/s** при маленьком payload и аккуратной настройке лимитов ОС.
- **3-5 инстансов локально**: порядок **15k-50k connections** и **15k-80k msg/s**, если не упрётесь в Docker Desktop networking, Redis pub/sub и память.

Типичный сервер 16C+, Linux, без Docker Desktop overhead:

- **1 инстанс**: ориентир **20k-80k+ connections**, **20k-100k+ msg/s**.
- **несколько инстансов**: упор быстро смещается в Redis backplane, сетевые лимиты и fan-out модель групп.

### Grafana

Provisioning уже включён. Dashboard автоматически подхватывается из `docker/grafana/dashboards/signalr-overview.json`.

Для артефактов портфолио сохраняйте скриншоты в:

```text
docs/images/performance/
```

Рекомендуемые скриншоты:

1. `active-connections.png`
2. `publish-latency-p95-p99.png`
3. `redis-throughput.png`
4. `memory-and-cpu.png`

### Как масштабировать до 1M+ connections / messages

Локальный compose на это не рассчитан. Для следующего уровня нужен другой deployment target:

1. Kubernetes с `Deployment` для `realtime-svc` / `api-svc` и `HorizontalPodAutoscaler` по CPU, memory и custom metrics.
2. Гибрид L4/L7 или Ingress с настоящим sticky session механизмом на cookie или consistent hashing.
3. Redis Cluster или managed real-time слой вроде Azure SignalR Service.
4. Вынос `/metrics` в OTLP Collector + Prometheus/Grafana Mimir/VictoriaMetrics.
5. Несколько load-generator нод, чтобы не упираться в ephemeral ports и RAM одной машины.
6. Лимиты ОС: `ulimit`, `somaxconn`, `tcp_tw_reuse`, `tcp_fin_timeout`, file descriptors, NIC queues.

## Структура репозитория

```text
highload-realtime-signalr-demo/
├── Server/                      # ASP.NET Core host, SignalR Hub, OTel, batching
├── Api/                         # Lightweight Minimal API для REST workload
├── Shared/                      # Общие DTO и SignalR contracts
├── LoadTester/                  # NBomber self-load console app
├── tests/load/                  # k6 сценарии
├── docker/                      # HAProxy, Nginx L7, video, Prometheus, Grafana
├── docs/                        # Архитектура, setup, performance
├── Layout/ Pages/ wwwroot/      # Blazor WASM UI
├── docker-compose.yml
├── Dockerfile
├── Makefile
└── highload-realtime-signalr-demo.slnx
```

## Документация

| Раздел | Файл |
|---|---|
| Контекст проекта и последние изменения | [docs/project-context.md](docs/project-context.md) |
| Архитектура и scale-out | [docs/architecture.md](docs/architecture.md) |
| Нагрузочное тестирование | [docs/performance.md](docs/performance.md) |
| Полный testing runbook | [docs/testing.md](docs/testing.md) |
| Performance tuning | [docs/performance-tuning.md](docs/performance-tuning.md) |
| Локальный запуск и Docker | [docs/setup.md](docs/setup.md) |
| Технологический стек | [docs/tech-stack.md](docs/tech-stack.md) |
| Вклад в проект | [docs/how-to-contribute.md](docs/how-to-contribute.md) |
| ADR по Redis backplane | [adr/0001-use-signalr-with-redis-backplane.md](adr/0001-use-signalr-with-redis-backplane.md) |
| ADR по HAProxy L4 + Nginx L7 | [adr/0002-hybrid-l4-l7-load-balancing.md](adr/0002-hybrid-l4-l7-load-balancing.md) |

## Лицензия
MIT, см. [LICENSE.txt](LICENSE.txt).
