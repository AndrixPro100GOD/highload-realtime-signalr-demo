# Публикация Docker-образов

Документ описывает два сценария: обычную локальную работу разработчика и публикацию образов на отдельный Docker host через `docker-compose.myhost.yml`.

Архитектурное решение по этому направлению зафиксировано в [ADR 0003](../adr/0003-host-deploy-and-load-testing-direction.md).

## Обычная локальная работа

Для локального запуска всем разработчикам достаточно основного compose-файла:

```bash
docker compose up --build -d
```

Открыть:

- [http://localhost:8080](http://localhost:8080) — приложение через HAProxy + Nginx.
- [http://localhost:8404/stats](http://localhost:8404/stats) — HAProxy stats.
- [http://localhost:3000](http://localhost:3000) — Grafana.

## Публикация на host

Этот сценарий использует:

- Docker context `host`.
- Основной `docker-compose.yml`.
- Override `docker-compose.myhost.yml`, где заданы registry image names вида `192.168.0.90:5000/highload-realtime-signalr-demo/...`.

### 1. Переключиться на host

```powershell
docker context use host
```

Проверить активный context:

```powershell
docker context show
```

Ожидаемо:

```text
host
```

### 2. Собрать образы

```powershell
docker compose -f docker-compose.yml -f docker-compose.myhost.yml build
```

### 3. Запушить образы

```powershell
docker compose -f docker-compose.yml -f docker-compose.myhost.yml push
```

### 4. Запустить на host

```powershell
docker compose -f docker-compose.yml -f docker-compose.myhost.yml up -d
```

## Удобные батники

Из корня репозитория:

```powershell
.\build-push-to-host.bat
.\deploy-to-host.bat
```

`build-push-to-host.bat` переключает Docker context на `host`, собирает образы и пушит их.

`deploy-to-host.bat` переключает Docker context на `host` и запускает стек через основной compose + host override.

## Проверка после деплоя

```powershell
docker compose -f docker-compose.yml -f docker-compose.myhost.yml ps
```

Health endpoints:

```powershell
curl http://localhost:8080/health/ready
curl http://localhost:8404/healthz
```

Если тесты запускаются не на host-машине, передавайте внешний URL явно:

```powershell
dotnet run --project LoadTester/LoadTester.csproj -- --base-url=http://<host>:8080 --connections=1000
```

## Частые проблемы

| Симптом | Что проверить |
|---|---|
| `context "host" does not exist` | Создать Docker context или использовать правильное имя host context. |
| `push access denied` | Registry `192.168.0.90:5000` недоступен или Docker не залогинен/не доверяет registry. |
| `connection refused` на `:8080` | Контейнер `haproxy` не поднят, порт закрыт firewall'ом или тест запускается не на host-машине. |
| После правок конфигов proxy ничего не меняется | Конфиги HAProxy/Nginx/Prometheus/Grafana копируются в образы; нужен rebuild соответствующих сервисов. |

## Связанные документы

- [ADR 0003: Host deployment и честное нагрузочное тестирование](../adr/0003-host-deploy-and-load-testing-direction.md)
- [testing.md](./testing.md)
- [performance-tuning.md](./performance-tuning.md)
