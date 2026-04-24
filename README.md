<div align="center">

# highload-realtime-signalr-demo

**High-load real-time demo на .NET 10: SignalR, MessagePack, Redis backplane, NBomber, Prometheus, Grafana**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?logo=blazor)](https://learn.microsoft.com/aspnet/core/blazor/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[Архитектура](docs/architecture.md) · [Запуск](docs/setup.md) · [Производительность](docs/performance.md) · [Стек](docs/tech-stack.md) · [ADR](adr/)

</div>

## О проекте
Репозиторий теперь содержит полноценный high-load стек:

- `Server/` на ASP.NET Core + SignalR + MessagePack + Redis backplane.
- `highload-realtime-signalr-demo.csproj` как Blazor WebAssembly UI с ручным smoke-клиентом на `/realtime`.
- `LoadTester/` на NBomber для self-load тестов.
- `tests/load/signalr.js` для альтернативного k6/WebSocket прогона.
- `docker-compose.yml` для локального scale-out с `app`, `nginx`, `redis`, `postgres`, `prometheus`, `grafana`.

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
docker compose up --build -d --scale app=3
```

После старта:

- приложение и L7 балансировщик: [http://localhost:8080](http://localhost:8080)
- Grafana: [http://localhost:3000](http://localhost:3000) (`admin` / `admin`)
- Prometheus: [http://localhost:9090](http://localhost:9090)
- Redis exporter: [http://localhost:9121/metrics](http://localhost:9121/metrics)

## Performance & Load Testing
### Что уже встроено

- Kestrel тюнинг: `MaxConcurrentConnections`, `MaxConcurrentUpgradedConnections`, `KeepAliveTimeout`, socket backlog, min thread pool.
- SignalR тюнинг: `MessagePack`, bounded batching queue, per-connection rate limiting, backpressure и graceful degradation.
- Redis backplane с `ConfigurationOptions`, `AbortOnConnectFail=false`, `ReconnectRetryPolicy=ExponentialRetry(...)`.
- OpenTelemetry metrics + `/metrics` endpoint + Redis exporter + готовый Grafana dashboard.
- NBomber self-load с persistent SignalR clients, ramp-up, broadcast/group/targeted/batched traffic.

### Команды self-load

Через `Makefile`:

```bash
make build
make run-server
make loadtest CONNECTIONS=1000 RAMP_UP=60 STEADY=120 RAMP_DOWN=15
make k6 CONNECTIONS=200 RAMP_UP=30 STEADY=60 RAMP_DOWN=10
make compose-up
make compose-scale APP_SCALE=5
```

Без `make`:

```bash
dotnet run --project Server/Server.csproj
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --ramp-up=60 --steady=120 --ramp-down=15 --payload-bytes=128
k6 run -e BASE_URL=http://localhost:8080 -e VUS=200 tests/load/signalr.js
docker compose up --build -d --scale app=3
```

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

1. Kubernetes с `Deployment` для app и `HorizontalPodAutoscaler` по CPU, memory и custom metrics.
2. Ingress / L7 с настоящим sticky session механизмом на cookie или consistent hashing.
3. Redis Cluster или managed real-time слой вроде Azure SignalR Service.
4. Вынос `/metrics` в OTLP Collector + Prometheus/Grafana Mimir/VictoriaMetrics.
5. Несколько load-generator нод, чтобы не упираться в ephemeral ports и RAM одной машины.
6. Лимиты ОС: `ulimit`, `somaxconn`, `tcp_tw_reuse`, `tcp_fin_timeout`, file descriptors, NIC queues.

## Структура репозитория

```text
highload-realtime-signalr-demo/
├── Server/                      # ASP.NET Core host, SignalR Hub, OTel, batching
├── Shared/                      # Общие DTO и SignalR contracts
├── LoadTester/                  # NBomber self-load console app
├── tests/load/                  # k6 сценарии
├── docker/                      # nginx LB, Prometheus, Grafana provisioning
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
| Локальный запуск и Docker | [docs/setup.md](docs/setup.md) |
| Технологический стек | [docs/tech-stack.md](docs/tech-stack.md) |
| Вклад в проект | [docs/how-to-contribute.md](docs/how-to-contribute.md) |
| ADR по Redis backplane | [adr/0001-use-signalr-with-redis-backplane.md](adr/0001-use-signalr-with-redis-backplane.md) |

## Лицензия
MIT, см. [LICENSE.txt](LICENSE.txt).
