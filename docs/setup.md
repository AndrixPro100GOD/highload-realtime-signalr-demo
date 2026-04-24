# Запуск проекта

Документ описывает уже **текущий** стек: hosted Blazor WASM, ASP.NET Core SignalR server, Redis backplane, PostgreSQL, NBomber, k6 и docker-compose инфраструктуру.

## Требования

| Компонент | Версия |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.x |
| Docker Desktop | актуальная стабильная |
| `k6` | опционально, только для `tests/load/signalr.js` |
| `make` | опционально, для коротких команд |

## Локальный запуск

### Только приложение

```bash
dotnet restore
dotnet run --project Server/Server.csproj
```

По умолчанию dev-профиль поднимает сервер на [http://localhost:8080](http://localhost:8080). В `Development` Redis backplane отключён специально, чтобы single-instance smoke работал даже без локального Redis.

### Ручной smoke через браузер

Откройте:

- [http://localhost:8080](http://localhost:8080)
- [http://localhost:8080/realtime](http://localhost:8080/realtime)

На `/realtime` можно:

1. Поднять SignalR-соединение.
2. Вступить в группу.
3. Отправить `broadcast`, `group`, `targeted`, `queue group`.
4. Посмотреть последние доставленные сообщения и состояние узла.

### NBomber self-load

```bash
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://localhost:8080 --connections=1000 --ramp-up=60 --steady=120 --ramp-down=15 --payload-bytes=128
```

Полезные параметры:

```text
--base-url=http://localhost:8080
--connections=1000
--ramp-up=60
--steady=120
--ramp-down=15
--payload-bytes=128
--group=benchmark
```

### k6

```bash
k6 run -e BASE_URL=http://localhost:8080 -e VUS=200 tests/load/signalr.js
```

## Docker Compose

Полный локальный stack:

```bash
docker compose up --build -d --scale app=3
```

Сервисы:

| Сервис | URL / порт | Назначение |
|---|---|---|
| `nginx` | [http://localhost:8080](http://localhost:8080) | L7 балансировщик с session affinity по `remote_addr` |
| `app` | internal `:8080` | ASP.NET Core + SignalR |
| `redis` | `localhost:6379` | SignalR backplane |
| `postgres` | `localhost:5432` | вспомогательная персистентность и readiness |
| `prometheus` | [http://localhost:9090](http://localhost:9090) | scrape метрик |
| `grafana` | [http://localhost:3000](http://localhost:3000) | dashboards |
| `redis-exporter` | [http://localhost:9121/metrics](http://localhost:9121/metrics) | Redis metrics |

Остановить стек:

```bash
docker compose down --remove-orphans
```

## Makefile

Готовые shortcut-команды:

```bash
make restore
make build
make run-server
make compose-up
make compose-scale APP_SCALE=5
make loadtest CONNECTIONS=1000
make k6 CONNECTIONS=200
```

## Важные переменные окружения

| Переменная | Описание |
|---|---|
| `ConnectionStrings__Postgres` | строка подключения PostgreSQL |
| `Performance__Redis__Configuration` | connection string для SignalR Redis backplane |
| `Performance__Redis__Enabled` | можно отключить Redis backplane в dev |
| `Performance__Kestrel__HttpPort` | порт Kestrel |
| `LOADTEST_BASEURL` | базовый URL для `LoadTester` |
| `BASE_URL` | базовый URL для `k6` |

## Частые проблемы

| Симптом | Что проверить |
|---|---|
| `bind: address already in use` | освободить порт `8080`, `3000`, `5432`, `6379`, `9090` |
| NBomber получает `operation timeout` | проверить, что сервер реально слушает `http://localhost:8080` |
| Multi-instance 404/upgrade ошибки | проверить, что стек идёт через `nginx`, а не напрямую в `app` |
| `/metrics` пустой | проверить `prometheus` и `OpenTelemetry` настройки |
| Grafana пустая | подождать 5-10 секунд после старта compose и убедиться, что datasource `Prometheus` provisioned |

Смежные документы: [architecture.md](./architecture.md), [performance.md](./performance.md), [tech-stack.md](./tech-stack.md).
