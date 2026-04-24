# Нагрузочное тестирование и метрики

Этот документ фиксирует текущую методику и уже полученные baseline-результаты для `highload-realtime-signalr-demo`.

## Что именно измеряем

| Метрика | Что показывает |
|---|---|
| `Concurrent connections` | Сколько одновременных SignalR/WebSocket клиентов реально держит инстанс |
| `Messages / sec` | Скорость публикации и доставки сообщений |
| `Latency p50/p95/p99` | Насколько быстро доходит сообщение при нагрузке |
| `Active connections` | Текущая нагрузка на Hub |
| `Batch queue depth` | Начинается ли backpressure и деградация |
| `Redis commands / memory` | Насколько backplane сам становится bottleneck |
| `process_working_set_bytes`, `process_cpu_time_seconds_total` | Давление на CPU и память процесса |

## Инструменты

### Основной

- `LoadTester/` на **NBomber**: поднимает реальные SignalR-клиенты, держит соединение между итерациями и гоняет смешанный traffic:
  - `SendBroadcast`
  - `SendToGroup`
  - `SendToConnection`
  - `QueueGroupMessage`

### Альтернативный

- `tests/load/signalr.js` на **k6**: проверяет WebSocket/SignalR negotiate и basic traffic path.

### Наблюдаемость

- `OpenTelemetry` + `/metrics`
- `Prometheus`
- `Grafana`
- `redis_exporter`

## Готовые сценарии запуска

### Single-instance, локально

```bash
dotnet run --project Server/Server.csproj
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --ramp-up=60 --steady=120 --ramp-down=15 --payload-bytes=128
```

### Multi-instance, docker compose

```bash
docker compose up --build -d --scale app=3
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=5000 --ramp-up=120 --steady=300 --ramp-down=30 --payload-bytes=128
```

### Через Makefile

```bash
make compose-up
make compose-scale APP_SCALE=5
make loadtest CONNECTIONS=5000 RAMP_UP=120 STEADY=300 RAMP_DOWN=30
make k6 CONNECTIONS=200
```

## Уже полученные baseline-результаты

Ниже результаты коротких реальных прогонов на текущей машине. Они подходят как sanity baseline после изменений, но не как «предел железа».

| Дата | Конфигурация | Инстансы app | Connections | Итог | Latency | Комментарий |
|---|---|---:|---:|---|---|---|
| 2026-04-24 | `dotnet run`, локальный single-instance | 1 | 5 | `2300 ok`, `~230 RPS` | mean `15.67 ms`, p95 `62.94 ms` | smoke прогон без Redis backplane в `Development` |
| 2026-04-24 | `docker compose`, nginx + Redis + Postgres + Prometheus + Grafana | 3 | 20 | `16312 ok`, `~1165 RPS` | mean `13.02 ms`, p95 `51.62 ms` | стабильный scale-out smoke через L7 и Redis |

Для более агрессивного smoke был ещё прогон на `50` connections и `96B payload`; он показал `~2669 RPS`, но уже с заметным количеством reconnect/transport ошибок. Это полезно как индикатор того, что дальнейшая работа должна идти в сторону OS limits, real sticky-cookie ingress и более жёсткой сетевой настройки под burst.

## Как интерпретировать графики

- Рост `signalr_batch_queue_depth` означает, что сервер уже начал гасить burst через batching/backpressure.
- Рост `signalr_requests_rate_limited_total` означает, что один или несколько клиентов вышли за per-connection budget.
- Рост `redis_commands_processed_total` при одновременном росте p95/p99 обычно указывает на Redis как следующий bottleneck.
- Рост `process_working_set_bytes` без роста RPS часто означает аллокационное давление и GC.

## Что смотреть в Grafana

Provisioned dashboard: `SignalR Highload Overview`.

Рекомендуемый набор скриншотов для портфолио:

```text
docs/images/performance/active-connections.png
docs/images/performance/publish-latency.png
docs/images/performance/redis-throughput.png
docs/images/performance/cpu-memory.png
```

Основные панели:

1. `Активные SignalR-соединения`
2. `Поток сообщений`
3. `Серверная latency publish`
4. `Backpressure и queue depth`
5. `Память процесса`
6. `CPU процесса`
7. `Redis throughput и клиенты`
8. `Redis память`

## Практические рекомендации

### Чтобы дойти до десятков тысяч connections локально

1. Поднимать `CONNECTIONS` в `LoadTester` постепенно, а не сразу на пике.
2. Следить за лимитами ОС: порты, file descriptors, backlog, Docker Desktop networking.
3. Увеличивать `ThreadPool`, socket backlog и Redis limits только вместе с замерами.
4. Держать payload маленьким и бинарным; MessagePack уже включён по умолчанию.

### Чтобы выйти к 1M+

1. Уводить deployment в Kubernetes.
2. Использовать ingress/controller с реальным sticky session механизмом.
3. Переходить на Redis Cluster или managed SignalR слой.
4. Разносить load generation по нескольким машинам.
5. Отдельно профилировать fan-out групп и горячие комнаты.

Подробности по окружению: [setup.md](./setup.md). Общая архитектура: [architecture.md](./architecture.md).
