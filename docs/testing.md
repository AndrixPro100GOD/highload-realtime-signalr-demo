# Testing Guide

Практический runbook для проверки **highload-realtime-signalr-demo** по всем направлениям: сборка, локальный smoke, Docker/infra, SignalR latency, fan-out/backpressure, REST API, media bypass и observability.

## Быстрый порядок проверки


| Шаг | Команда                                                                                                                                                                                                                                                                                                                | Ожидаемый результат                                                             |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| 1   | `dotnet restore`                                                                                                                                                                                                                                                                                                       | Все проекты восстановлены без ошибок.                                           |
| 2   | `dotnet build highload-realtime-signalr-demo.slnx`                                                                                                                                                                                                                                                                     | `Build succeeded`, `0 Error(s)`.                                                |
| 3   | `docker compose config --quiet`                                                                                                                                                                                                                                                                                        | Нет вывода, exit code `0`.                                                      |
| 4   | `docker compose up --build -d --scale realtime-svc=3 --scale nginx-l7=2 --scale api-svc=2`                                                                                                                                                                                                                             | Все контейнеры стартуют, `haproxy` не падает.                                   |
| 5   | `dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --warm-up=30 --warm-up-connections=100 --ramp-up=60 --steady=120 --ramp-down=20 --payload-bytes=64 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-light-targeted`   | `1000` WebSocket, fail rate около `0`, p95 latency в пределах healthy baseline. |
| 6   | `dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=3000 --warm-up=60 --warm-up-connections=300 --ramp-up=180 --steady=300 --ramp-down=30 --payload-bytes=96 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-medium-targeted` | `3000` WebSocket, p95 целевой `< 100 ms` на нормальной машине.                  |
| 7   | `dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=5000 --warm-up=180 --warm-up-connections=100 --ramp-up=600 --steady=600 --ramp-down=60 --payload-bytes=128 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=30000 --connect-retry-delay-ms=1000 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-heavy-targeted` | `5000` WebSocket targeted baseline, без latency spiral.                         |


## Требования к окружению


| Компонент                          | Зачем                                                                                                                  |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **.NET SDK 10.x**                  | Сборка `Server`, `Api`, `LoadTester`, Blazor client.                                                                   |
| **Docker Desktop / Docker Engine** | Полный hybrid stack: HAProxy, Nginx, Redis, PostgreSQL, Prometheus, Grafana.                                           |
| **make**                           | Опционально. На Windows часто отсутствует; основной runbook ниже использует прямые `dotnet` / `docker` / `k6` команды. |
| **k6**                             | REST/API smoke и дополнительная WebSocket-проверка.                                                                    |
| **Linux / WSL2 предпочтительно**   | Для high-load тестов меньше сетевой overhead, чем у Docker Desktop на Windows.                                         |


Перед тяжёлыми прогонами закройте лишние процессы. Если load generator и server stack на одной машине, результат — **локальный baseline**, а не предел архитектуры.

## Windows: если `make` не установлен

На Windows команда `make` обычно недоступна:

```text
"make" не является внутренней или внешней командой
```

Это нормально. Используйте прямые команды из документа — они являются **основными**. `make` в этом проекте — только короткий alias для Linux / WSL / Git Bash.

## 1. Build & Static Validation

### Restore

```bash
dotnet restore
```

Ожидаемо:

- restore всех проектов проходит без ошибок;
- нет конфликтов пакетов.

### Build

```bash
dotnet build highload-realtime-signalr-demo.slnx
```

Ожидаемо:

- `Shared`, `Api`, Blazor client, `LoadTester`, `Server` собираются;
- `0 Error(s)`;
- warning-и допустимы только осознанные, но текущая цель — чистая сборка.

### Docker Compose syntax

```bash
docker compose config --quiet
```

Ожидаемо:

- команда ничего не печатает;
- exit code `0`;
- если есть ошибка, сначала чинить compose, не запускать нагрузку.

## 2. Local Single-Instance Smoke

Используется для проверки кода без Docker и Redis backplane.

```bash
dotnet run --project Server/Server.csproj
```

Открыть:

- [http://localhost:8080](http://localhost:8080)
- [http://localhost:8080/realtime](http://localhost:8080/realtime)

Ожидаемо:

- UI открывается;
- на `/realtime` можно подключиться к SignalR;
- `GetConnectionInfo` возвращает `nodeId`, `connectionId`, `activeConnections`;
- в `Development` Redis может быть выключен, это нормально для single-instance smoke.

Health:

```bash
curl -fsS http://localhost:8080/health/live
curl -fsS http://localhost:8080/health/ready
```

Ожидаемо:

- `/health/live` отвечает `200`;
- `/health/ready` может зависеть от локальных зависимостей, в Docker должен быть `200`.

## 3. Full Docker Hybrid Stack

### Start

```bash
docker compose up --build -d --scale realtime-svc=3 --scale nginx-l7=2 --scale api-svc=2
```

Ожидаемо:

- `haproxy`, `nginx-l7`, `realtime-svc`, `api-svc`, `video-svc`, `redis`, `postgres`, `prometheus`, `grafana`, exporters запущены;
- `haproxy` не падает с ошибкой FD limit;
- `realtime-svc` healthy после Redis/Postgres.

Проверка:

```bash
docker compose ps
```

Ожидаемо:

- ключевые сервисы в состоянии `Up`;
- health для `realtime-svc`, `api-svc`, `video-svc`, `haproxy`, `nginx-l7` — `healthy` или переходит в `healthy` через несколько секунд.

### Edge health

```bash
curl -fsS http://localhost:8404/healthz
curl -fsS http://localhost:8080/health/l7
```

Ожидаемо:

- оба endpoint отвечают `ok`.

### HAProxy stats

Открыть:

- [http://localhost:8404/stats](http://localhost:8404/stats)

Ожидаемо:

- видны frontend/backend;
- `nginx_l7` и `video_direct` имеют backend servers `UP`.

## 4. SignalR Baseline Tests

Эти тесты используют **targeted traffic**. Это важно: они измеряют способность держать тысячи WebSocket и round-trip latency без искусственной `N*N` broadcast-лавины.

По умолчанию команды используют локальный edge `http://localhost:8080`. Для удалённого стенда передавайте `--base-url=<url>` явно или задавайте `LOADTEST_BASEURL`/`BASE_URL`.

`--send-interval-ms=1000` задаёт реалистичный pacing: каждое соединение отправляет примерно `1` publish/sec. LoadTester делает короткие `idle`-итерации между publish, чтобы не упираться в NBomber operation timeout; `idle` в отчёте не является сетевым запросом. Если поставить `0`, тест превращается в throughput stress, и `Per-connection rate limit exceeded` будет ожидаемым результатом защиты Hub, а не признаком latency baseline.

`--connect-concurrency=16` ограничивает одновременные WebSocket handshakes. Если в отчёте много `connect-retry`, edge/load-generator не успевает принимать новые подключения: можно временно снизить до `8`, затем проверять HAProxy/Nginx/server logs и лимиты ОС.

### Light: 1000 WebSocket

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --warm-up=30 --warm-up-connections=100 --ramp-up=60 --steady=120 --ramp-down=20 --payload-bytes=64 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-light-targeted
```

Shortcut, если `make` установлен:

```bash
make loadtest-light
```

Ожидаемо:

- fail rate близко к `0`;
- p95 latency обычно заметно ниже `100 ms` на healthy машине;
- `signalr_active_connections` поднимается примерно до `1000`;
- ThreadPool queue не растёт устойчиво.

### Medium: 3000 WebSocket

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=3000 --warm-up=60 --warm-up-connections=300 --ramp-up=180 --steady=300 --ramp-down=30 --payload-bytes=96 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-medium-targeted
```

Shortcut, если `make` установлен:

```bash
make loadtest-medium
```

Ожидаемо:

- целевой p95 `< 100 ms` на Linux/WSL2/нормальной машине;
- fail rate `< 1%`;
- Redis `blocked_clients = 0`;
- GC не держится в sustained high state.

### Heavy: 5000 WebSocket

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=5000 --warm-up=180 --warm-up-connections=100 --ramp-up=600 --steady=600 --ramp-down=60 --payload-bytes=128 --send-interval-ms=1000 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=30000 --connect-retry-delay-ms=1000 --max-fail-count=50000 --group=benchmark --traffic-profile=targeted --scenario=signalr-heavy-targeted
```

Shortcut, если `make` установлен:

```bash
make loadtest-heavy
```

Ожидаемо:

- `5000` соединений удерживаются без массовых reconnect;
- latency не уходит в секунды;
- в финальной `load tester summary` должно быть `target reached: yes` и `publish traffic observed: yes`;
- статус `publish-ok` должен появиться после завершения ramp-up; без него тест проверил только connect layer;
- в статусах NBomber допустимы `idle` OK-итерации — это pacing, а не отдельные server calls;
- `connect-retry` может появляться во время ramp-up, но не должен доминировать после выхода на steady;
- если p95 выше `100 ms`, сначала смотреть CPU/RAM load generator, потом `realtime-svc`, Redis, HAProxy/Nginx.

## 5. SignalR Fan-out / Backpressure Test

Этот сценарий намеренно тяжелее: смешанный профиль с group/broadcast/batching. Он проверяет не baseline latency, а поведение при fan-out и graceful degradation.

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --warm-up=30 --warm-up-connections=100 --ramp-up=120 --steady=300 --ramp-down=15 --payload-bytes=96 --send-interval-ms=250 --connect-concurrency=16 --connect-acquire-timeout-ms=250 --connect-timeout-ms=15000 --connect-retry-delay-ms=500 --max-fail-count=50000 --group=benchmark --traffic-profile=mixed --batch-every=2 --scenario=signalr-fanout-mixed
```

Shortcut, если `make` установлен:

```bash
make loadtest-fanout CONNECTIONS=1000 RAMP_UP=120 STEADY=300 PAYLOAD_BYTES=96
```

Ожидаемо:

- возможны `rejected` / `dropped` сообщения при saturation;
- система не должна уходить в latency spiral на десятки секунд;
- `signalr_batch_queue_depth` может расти, но не должен бесконечно увеличиваться;
- `signalr_messages_dropped_total` допустим как признак controlled degradation.

## 6. k6 WebSocket Probe

Дополнительная проверка WebSocket negotiate/upgrade path через HAProxy + Nginx.

```bash
k6 run -e BASE_URL=http://localhost:8080 -e VUS=200 -e RAMP_UP=30s -e STEADY=60s -e RAMP_DOWN=10s -e PAYLOAD_BYTES=128 -e GROUP_NAME=benchmark tests/load/signalr.js
```

Shortcut, если `make` установлен:

```bash
make k6 CONNECTIONS=200 RAMP_UP=30 STEADY=60 RAMP_DOWN=10
```

Ожидаемо:

- `negotiate status is 200`;
- `ws upgrade status is 101`;
- `http_req_failed` ниже threshold в скрипте.

## 7. REST API Tests

### API smoke

```bash
curl -fsS http://localhost:8080/api/ping
curl -fsS http://localhost:8080/api/performance/info
```

Ожидаемо:

- оба запроса возвращают JSON;
- `service = highload-realtime-api`;
- путь идёт через `HAProxy -> Nginx -> api-svc`.

### API load

```bash
k6 run -e BASE_URL=http://localhost:8080 -e VUS=100 tests/load/api-smoke.js
```

Shortcut, если `make` установлен:

```bash
make loadtest-api CONNECTIONS=100
```

Ожидаемо:

- `api ping status is 200`;
- `api info status is 200`;
- `http_req_failed < 1%`;
- p95 обычно `< 250 ms` по threshold `api-smoke.js`.

## 8. Media / Video Tests

### L4 bypass

```bash
curl -fsSI http://localhost:8081/video/index.m3u8
curl -fsSI http://localhost:8081/video/sample.mp4
```

Ожидаемо:

- `200 OK`;
- для `sample.mp4` есть `Accept-Ranges: bytes`;
- путь идёт `HAProxy -> video-svc`, минуя Nginx L7.

### L7 fallback

```bash
curl -fsSI http://localhost:8080/video/index.m3u8
curl -fsSI http://localhost:8080/video/sample.mp4
```

Ожидаемо:

- `200 OK`;
- путь идёт `HAProxy -> Nginx -> video-svc`;
- используется для сравнения с bypass.

### Byte-range check

```bash
curl -fsS -H "Range: bytes=0-1023" http://localhost:8081/video/sample.mp4 -o /tmp/sample.part
```

Ожидаемо:

- HTTP `206 Partial Content` или корректная частичная отдача, если клиент показывает статус;
- файл `/tmp/sample.part` около `1 KB`;
- media-сервис не задействует Kestrel.

## 9. Observability Checks

### Prometheus targets

Открыть:

- [http://localhost:9090/targets](http://localhost:9090/targets)

Ожидаемо:

- `realtime-svc` — `UP`;
- `api-svc` — `UP`;
- `haproxy` — `UP`;
- `nginx-l7` — `UP`;
- `redis-exporter` — `UP`.

### Grafana

Открыть:

- [http://localhost:3000](http://localhost:3000) (`admin` / `admin`)

Ожидаемо:

- datasource Prometheus provisioned;
- dashboard `SignalR Highload Overview` виден;
- во время теста двигаются панели active connections, latency, queue depth, Redis, HAProxy/Nginx.

### Ключевые PromQL для быстрой проверки

```promql
signalr_active_connections
sum(rate(signalr_messages_published_total[1m]))
histogram_quantile(0.95, sum(rate(signalr_publish_latency_ms_bucket[5m])) by (le))
signalr_batch_queue_depth
rate(signalr_messages_dropped_total[1m])
redis_connected_clients
redis_blocked_clients
```

Ожидаемо:

- active connections соответствует профилю теста;
- queue depth не растёт бесконечно;
- `redis_blocked_clients` должен быть `0`;
- drops/rate limited появляются только в fan-out/degradation сценариях.

## 10. Cleanup

Остановить стек:

```bash
docker compose down --remove-orphans
```

Остановить и удалить volumes:

```bash
docker compose down --remove-orphans -v
```

Ожидаемо:

- контейнеры удалены;
- при `-v` удалены volume data Redis/PostgreSQL/Prometheus/Grafana.

## Troubleshooting


| Симптом                                        | Вероятная причина                                                    | Что сделать                                                                      |
| ---------------------------------------------- | -------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `haproxy exited (1)` и `Cannot raise FD limit` | `nofile` меньше нужного для `maxconn`                                | Увеличить `haproxy.ulimits.nofile` в `docker-compose.yml`.                       |
| `bind: address already in use`                 | Занят `8080`, `8081`, `8404`, `3000`, `9090`, `5432`, `6379`         | Освободить порт или изменить mapping.                                            |
| NBomber timeout на medium/heavy                | Load generator bottleneck, Docker Desktop overhead, Redis saturation | Снизить connections, проверить Grafana, вынести load generator на другую машину. |
| Высокий p99 при низком CPU                     | Очередь ThreadPool, Redis, proxy timeout, GC                         | Смотреть ThreadPool queue, `signalr_batch_queue_depth`, Redis metrics.           |
| Много drops в `fanout`                         | Сработал backpressure                                                | Нормально для degradation теста, если latency не уходит в десятки секунд.        |
| Prometheus target DOWN                         | Сервис не healthy или неверный network/port                          | `docker compose ps`, `docker compose logs <service>`.                            |


## Критерии успешного полного прогона


| Направление    | Успех                                                       |
| -------------- | ----------------------------------------------------------- |
| Build          | `dotnet build` без ошибок.                                  |
| Compose        | `docker compose config --quiet` без ошибок, stack стартует. |
| Infra          | HAProxy stats, Prometheus, Grafana доступны.                |
| SignalR light  | `1000` WS, fail rate близко к `0`.                          |
| SignalR medium | `3000` WS, p95 целевой `< 100 ms` на healthy машине.        |
| SignalR heavy  | `5000` WS без latency spiral и массовых reconnect.          |
| Fan-out        | Backpressure контролируемый, queue не растёт бесконечно.    |
| API            | k6 smoke проходит thresholds.                               |
| Media          | `index.m3u8` и `sample.mp4` доступны через `8081` и `8080`. |


