# Запуск проекта

Инструкция покрывает **текущий** состав репозитория (Blazor WebAssembly) и **рекомендуемый** целевой вариант с Docker, когда в решение будут добавлены ASP.NET Core, SignalR и Redis.

## Оглавление

- [Требования](#требования)
- [Локальный запуск (текущее состояние)](#локальный-запуск-текущее-состояние)
- [Docker](#docker)
- [Переменные окружения](#переменные-окружения)
- [Частые проблемы](#частые-проблемы)

---

## Требования

| Компонент | Версия (ориентир) |
|-----------|-------------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.x |
| Docker Desktop (опционально) | актуальная стабильная |

---

## Локальный запуск (текущее состояние)

Из корня репозитория:

```bash
dotnet restore
dotnet run --project highload-realtime-signalr-demo.csproj
```

По умолчанию профиль **http** в `Properties/launchSettings.json` поднимает приложение на **http://localhost:5016** (порт может отличаться — смотрите вывод консоли и `launchSettings.json`).

Для HTTPS используйте профиль `https`:

```bash
dotnet run --project highload-realtime-signalr-demo.csproj --launch-profile https
```

Сборка под релиз:

```bash
dotnet publish -c Release -o ./publish
```

Статические файлы после `publish` можно отдавать любым статическим хостом; для Blazor WASM типичен хостинг через CDN или встроенный dev-сервер для проверки.

---

## Docker

В корне репозитория:

- **`Dockerfile`** — многостадийная сборка Blazor WASM (`dotnet publish`) и раздача `wwwroot` через **nginx** (Alpine).
- **`docker-compose.yml`** — сервис **`blazor`** (UI на порту **8080**) и **`redis`** (порт **6379**) для будущего SignalR backplane; UI к Redis пока не подключается.

```bash
docker compose up --build
```

После старта откройте **http://localhost:8080**.

Только образ клиента (без Redis):

```bash
docker build -t highload-blazor-wasm .
docker run --rm -p 8080:80 highload-blazor-wasm
```

Когда появится отдельный ASP.NET Core + SignalR, его добавят в `docker-compose` рядом с `redis` (см. [architecture.md](./architecture.md)).

---

## Переменные окружения

Пример для будущего API (задокументировать в `appsettings` / compose при появлении кода):

| Переменная | Описание |
|------------|----------|
| `ConnectionStrings__Redis` | Строка подключения к Redis для SignalR backplane |
| `ASPNETCORE_URLS` | URL прослушивания Kestrel |

---

## Частые проблемы

| Симптом | Что проверить |
|---------|----------------|
| Порт занят | Сменить `applicationUrl` в `launchSettings.json` или освободить порт |
| WASM не грузится в браузере | Кэш service worker, консоль DevTools (F12) |
| SignalR не подключается (после добавления бэкенда) | CORS, WebSocket на прокси, HTTPS mixed content |

Дополнительно: [architecture.md](./architecture.md), [tech-stack.md](./tech-stack.md).
