# Performance Tuning

Документ фиксирует настройки, добавленные после прогона `5000` WebSocket connections с симптомами: latency до десятков секунд, тысячи fail, GC time около `67%`, ThreadPool queue > `25k`, RSS около `1.9 GB`.

## Цель

Стабильный baseline для `3000-5000` concurrent WebSocket в локальном compose/WSL/Linux окружении:

| Метрика | Цель |
|---------|------|
| SignalR targeted p95 | `< 100 ms` на healthy машине |
| Fail rate | `< 1%` для targeted/light-medium сценариев |
| ThreadPool queue | близко к `0` после ramp-up |
| GC time | без длительных sustained spikes |
| Redis | `blocked_clients = 0`, без резкого роста latency |

Важно: `5000` клиентов, каждый из которых постоянно публикует `broadcast/group`, создаёт fan-out порядка `N*N`. Такой тест измеряет лавину доставки, а не способность держать `5000` WebSocket. Для latency baseline используется `targeted` профиль с `--send-interval-ms=1000`; fan-out тесты запускаются отдельно.

## Что изменено

### Kestrel

- `MaxConcurrentConnections` и `MaxConcurrentUpgradedConnections` ограничены до `50000`, чтобы процесс не уходил в OOM при ошибочной нагрузке.
- `KeepAliveTimeout = 60s`, `RequestHeadersTimeout = 10s`.
- `SocketBacklog = 32768`.
- HTTP/1.1 остаётся обязательным для WebSocket; HTTP/2 включён для обычного HTTP. HTTP/3 не включён в локальном compose, потому что требует TLS/QUIC.

### ThreadPool

- `ThreadPool.SetMinThreads(512, 256)` для быстрого ramp-up.
- `ThreadPool.SetMaxThreads(8192, 1024)` как guard rail от бесконтрольного роста.
- В compose добавлен `COMPlus_ThreadPool_ForceMinWorkerThreads=512`.

### SignalR

- `MaximumReceiveMessageSizeBytes = 16 KB`.
- `StreamBufferCapacity = 8`.
- `HandshakeTimeout = 5s`.
- `MaximumParallelInvocationsPerClient = 1`.
- Убраны `Information`-логи на каждый connect/disconnect; они включаются только на `Debug`.

### Redis backplane

- StackExchange.Redis остаётся мультиплексированным single connection для backplane.
- Таймауты снижены до `2000 ms`, `connectRetry = 10`, `keepAlive = 30`.
- Redis в compose увеличен до `maxmemory 1024mb`, `tcp-backlog 32768`, задан `client-output-buffer-limit pubsub`.

### Backpressure и batching

- Batching queue снижена с `100000` до `20000`.
- High watermark — `70%`.
- `MaxBatchSize = 512`, `FlushIntervalMs = 20`.
- `Channel` переведён на `FullMode = Wait`, чтобы `TryWrite` честно возвращал отказ на полной очереди.
- Per-connection `TokenBucketRateLimiter`: `120` burst tokens, `60/s`.
- HTTP global limiter теперь комбинирует `ConcurrencyLimiter` + `TokenBucketRateLimiter`.

### LoadTester pacing

- `--send-interval-ms` добавляет паузу между publish-операциями одного виртуального клиента.
- Pacing реализован короткими `idle`-итерациями, а не длинным `Task.Delay(1000)` внутри одной NBomber operation; это убирает ложные `operation timeout`.
- Publish latency измеряется через custom latency только вокруг publish/ack, поэтому pacing снижает RPS, но не завышает p95.
- Baseline-профили используют `1000 ms`, то есть примерно `1` publish/sec на WebSocket.
- WebSocket handshakes ограничены через `--connect-concurrency=16`, чтобы warm-up/ramp-up не создавал connect storm на HAProxy/Nginx и самом load generator.
- Ожидание свободного handshake slot ограничено `--connect-acquire-timeout-ms=250`; VU больше не висит на semaphore до конца теста.
- Временные connect errors возвращаются как `connect-retry`, занятый handshake gate — как `connect-wait`; финальная summary отдельно показывает peak connections и publish traffic.
- Fan-out профиль использует `250 ms`, чтобы сильнее давить на batching/backpressure без мгновенного превращения теста в бесконечный tight loop.
- `--send-interval-ms=0` оставлен для throughput stress; `Per-connection rate limit exceeded` в таком режиме является ожидаемой защитной реакцией.

### HAProxy + Nginx

- HAProxy: `maxconn 100000`, `backlog`, `tcpka`, таймауты `30m` для long-lived WebSocket.
- Nginx: отдельный `nginx.conf` с `worker_processes auto`, `worker_connections 65535`, `worker_rlimit_nofile 200000`.
- Upstream keepalive включён для `realtime`, `api`, `video`.
- L7 negotiate bursts режутся до попадания в Kestrel.

### Docker resources

| Сервис | CPU | Memory | nofile |
|--------|-----|--------|--------|
| `realtime-svc` | `4` | `2g` | `200000` |
| `haproxy` | `2` | `512m` | `250000` |
| `nginx-l7` | `2` | `512m` | `200000` |
| `redis` | `2` | `1536m` | `200000` |

Это не «магические» значения: они дают честный локальный guard rail. Для Linux bare-metal лимиты ОС (`ulimit`, `somaxconn`, ephemeral ports) всё равно важнее compose.

## Профили тестирования

### Light

```bash
make compose-up-hybrid APP_SCALE=3 NGINX_SCALE=2 API_SCALE=2
make loadtest-light
```

Ожидание: быстрый sanity для `1000` WebSocket, `--send-interval-ms=1000`, почти без ошибок.

### Medium

```bash
make compose-up-hybrid APP_SCALE=4 NGINX_SCALE=2 API_SCALE=2
make loadtest-medium
```

Ожидание: основной профиль для `3000` WebSocket, `--send-interval-ms=1000`.

### Heavy

```bash
make compose-up-hybrid APP_SCALE=5 NGINX_SCALE=2 API_SCALE=2
make loadtest-heavy
```

Ожидание: `5000` WebSocket, targeted round-trip, `--send-interval-ms=1000`. Если p95 > `100 ms`, сначала смотреть ThreadPool queue, Redis, CPU `realtime-svc`, затем L7/L4.

### Fan-out / degradation

```bash
make loadtest-fanout CONNECTIONS=1000 RAMP_UP=120 STEADY=300 PAYLOAD_BYTES=96
```

Это стресс именно fan-out/backpressure. Здесь допустимы rejected/dropped сообщения, если система не уходит в latency spiral.

## Что мониторить во время теста

| Слой | Метрики |
|------|---------|
| SignalR | `signalr_active_connections`, `signalr_publish_latency_ms`, `signalr_batch_queue_depth`, `signalr_messages_dropped_total`, `signalr_requests_rate_limited_total` |
| Runtime | ThreadPool queue, CPU, working set, GC time |
| Redis | `used_memory`, `connected_clients`, `blocked_clients`, `instantaneous_ops_per_sec`, pub/sub output buffers |
| HAProxy | frontend/backend sessions, retries, connection errors |
| Nginx | active connections, requests/s, 4xx/5xx |

## Методика

1. Запустить stack и дать `60-90s` на warm-up.
2. Запустить light, затем medium, затем heavy.
3. Между тестами очищать старые соединения: `docker compose restart realtime-svc nginx-l7 haproxy` при необходимости.
4. Не смешивать targeted latency baseline и fan-out stress в один вывод.
5. Если генератор нагрузки на той же машине, считать результат локальным baseline, а не пределом архитектуры.
