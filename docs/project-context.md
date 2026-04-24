# Контекст проекта для людей и ИИ-агентов

Краткий «источник правды» по репозиторию **highload-realtime-signalr-demo**. Правило Cursor указывает агентам читать этот файл перед крупными задачами.

## Зачем этот репозиторий

Демонстрация навыков **high-load backend** и **real-time** на .NET: множество WebSocket-подключений (SignalR), высокий поток сообщений, **горизонтальное масштабирование**, **observability**. Аудитория — работодатели, технические интервью.

## Текущее состояние (стек и код)

| Область | Статус |
|---------|--------|
| UI | **Blazor WebAssembly** (`net10.0`), **MudBlazor**, страница `/realtime` для ручного smoke |
| Backend / SignalR | Реализован в `Server/`: Hub, MessagePack, Redis backplane, batching, rate limiting, health |
| Shared contracts | `Shared/` с DTO и MessagePack contracts для server / client / load tester |
| Redis | Используется как backplane в `Production` / docker-compose; в `Development` может быть отключён для single-instance smoke |
| Load testing | `LoadTester/` на **NBomber**, `tests/load/signalr.js` на **k6** |
| Docker | `Dockerfile` публикует `Server/`; `docker-compose.yml` поднимает `app`, `nginx`, `redis`, `postgres`, `prometheus`, `grafana`, `redis-exporter` |
| Документация | Каталог **`docs/`** (архитектура, setup, performance, tech-stack, contribute) |

Подробная архитектура: `docs/architecture.md`. Запуск: `docs/setup.md`.

---

## Последние изменения

_Агентам и разработчикам: при значимом коммите добавляйте строку сверху блока (новые сверху)._

| Дата | Изменения |
|------|-----------|
| 2026-04-24 | **`bin/`**, **`obj/`**, **`.vs/`** убраны из индекса Git (`git rm -r --cached`); дальше игнорируются через `.gitignore`. Удалён случайный файл **`$null`**. |
| 2026-04-24 | Репозиторий возвращён на **.NET 10**. PostgreSQL bootstrap теперь автоматически создаёт целевую БД, схему и seed data при старте сервера; `launchSettings.json` очищен от лишнего `applicationUrl`, чтобы не было warning от Kestrel. |
| 2026-04-24 | Добавлены **`Server/`**, **`Shared/`**, **`LoadTester/`**, `Makefile`, `tests/load/signalr.js`, hosted Blazor WASM + SignalR + MessagePack + Redis backplane + OpenTelemetry/Prometheus + Grafana/Prometheus provisioning. Обновлены **README**, `docs/setup.md`, `docs/performance.md`, `docs/architecture.md`, `docs/tech-stack.md`. |
| 2026-04-24 | Добавлены **`.gitignore`**, **`.dockerignore`**, **Dockerfile** (Blazor WASM → nginx), **`docker/nginx.conf`**, **`docker-compose.yml`** (blazor + redis). Обновлены **README**, **docs/setup.md**, **docs/tech-stack.md**. |
| 2026-04-24 | Созданы каталоги **`docs/`** и **`adr/`** (ADR 0001 SignalR + Redis), обновлён **README** под портфолио-демо. |

---

## Как удобно пользоваться ИИ-агентом в этом репо

1. **Старт чата с контекстом** — в первом сообщении укажи цель («добавить Hub», «починить Docker») и, при необходимости, `@docs/architecture.md` или `@docs/project-context.md`.
2. **Узкие правки** — открой нужный файл в редакторе: сработают file-specific rules; для архитектуры всё равно полезно свериться с `docs/project-context.md`.
3. **После крупной задачи** — попроси агента **обновить таблицу «Последние изменения»** здесь или допиши сам одной строкой.
4. **Один запрос — одна цель** — так меньше расхождений с задуманной high-load архитектурой.
5. **Проверка Docker** — на Windows нужен запущенный **Docker Desktop**; команды: `docker compose up --build`, UI: `http://localhost:8080`.
6. **ADR** — решения уровня «почему Redis, а не Azure SignalR» фиксируй в `adr/`, не только в чате.
