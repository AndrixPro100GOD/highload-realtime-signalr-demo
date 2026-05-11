# Контекст проекта для людей и ИИ-агентов

Краткий «источник правды» по репозиторию **highload-realtime-signalr-demo**. Правило Cursor указывает агентам читать этот файл перед крупными задачами.

## Зачем этот репозиторий

Демонстрация навыков **high-load backend** и **real-time** на .NET: множество WebSocket-подключений (SignalR), высокий поток сообщений, **горизонтальное масштабирование**, **observability**. Аудитория — работодатели, технические интервью.

## Текущее состояние (стек и код)

| Область | Статус |
|---------|--------|
| UI | **Blazor WebAssembly** (`net10.0`), **MudBlazor**, страница `/realtime` для ручного smoke |
| Backend / SignalR | Реализован в `Server/` как `realtime-svc`: Hub, MessagePack, Redis backplane, batching, rate limiting, health |
| REST API | `Api/` как lightweight Minimal API (`api-svc`) для обычных HTTP-запросов |
| Video / media | `docker/video/`: VOD-плейлист `video/index.m3u8` + один `video/sample.mp4`, byte-range (`Accept-Ranges`), без `.ts` |
| Shared contracts | `Shared/` с DTO и MessagePack contracts для server / client / load tester |
| Redis | Используется как backplane в `Production` / docker-compose; в `Development` может быть отключён для single-instance smoke |
| Load testing | `LoadTester/` на **NBomber**, `tests/load/signalr.js` на **k6** |
| Docker | `Dockerfile` публикует `Server/`; `docker-compose.yml` поднимает HAProxy(L4), Nginx(L7), `realtime-svc`, `api-svc`, `video-svc`, Redis, PostgreSQL, Prometheus, Grafana и exporters |
| Документация | Каталог **`docs/`** (архитектура, setup, performance, tech-stack, contribute) |

Подробная архитектура: `docs/architecture.md`. Запуск: `docs/setup.md`.

---

## Последние изменения

_Агентам и разработчикам: при значимом коммите добавляйте строку сверху блока (новые сверху)._

| Дата | Изменения |
|------|-----------|
| 2026-05-12 | **LoadTester bugfix**: pacing upper-bound записан явно через `>`, а `Closed` handler SignalR берёт snapshot awaiters перед завершением pending publish. |
| 2026-05-12 | Добавлен **ADR 0003**: текущее направление по host deployment, portable defaults и честной интерпретации load testing; `docs/docker-publish.md` и `README` ссылаются на ADR. |
| 2026-05-11 | Добавлена документация публикации Docker-образов на host: `docs/docker-publish.md`, `build-push-to-host.bat`, `deploy-to-host.bat`; `README` и `docs/setup.md` ссылаются на новый workflow. |
| 2026-05-07 | **LoadTester default URL fix**: portable defaults возвращены на `http://localhost:8080`; удалённые стенды теперь задаются только явно через `--base-url`, `LOADTEST_BASEURL` или `BASE_URL`. |
| 2026-05-07 | **LoadTester reconnect fix**: inactive SignalR sessions теперь удаляются из `ScenarioInstanceData` и идут как `disconnected`, а не как массовые `InvalidOperationException`; VU может переподключаться. |
| 2026-05-07 | **Docker publish fix**: для hosted Blazor WASM отключены `PublishTrimmed`/`RunAOTCompilation` в Docker publish, чтобы сборка `realtime-svc` не зависала на `Optimizing assemblies for size`. |
| 2026-05-07 | **LoadTester preflight/early errors**: добавлен `/health/ready` preflight и ранний throttled вывод connect errors, чтобы недоступный endpoint был виден до длинного теста. |
| 2026-05-07 | **LoadTester reporting**: добавлены `connected`/`connect-wait`/`publish-ok` статусы и финальная summary (`peak active connections`, `publish ok`, `target reached`), чтобы heavy не выглядел успешным без publish traffic. |
| 2026-05-06 | **LoadTester connect tuning**: добавлены `--connect-concurrency`, `--connect-timeout-ms`, `--connect-retry-delay-ms`, `--max-fail-count`; transient handshake errors теперь идут как `connect-retry`, без раннего stop heavy. |
| 2026-05-06 | **LoadTester heavy fix**: pacing переведён на короткие `idle`-итерации вместо `Task.Delay(1000)` внутри NBomber operation; это убирает ложные `operation timeout` на heavy. |
| 2026-05-06 | **video-svc**: bind-mount nginx в compose отключён (закомментирован), конфиг берётся из образа (`docker/video/Dockerfile`); при необходимости правок без пересборки раскомментировать том на Linux/macOS или remote context. |
| 2026-05-06 | **Docker / Windows**: конфиги HAProxy, L7 nginx, Prometheus и Grafana в **образах** (`docker/*/Dockerfile`); после правок их конфигов — `docker compose build` соответствующего сервиса. |
| 2026-04-29 | **LoadTester pacing**: добавлен `--send-interval-ms`, baseline light/medium/heavy теперь имитирует реалистичный publish rate и не упирается в per-connection limiter. |
| 2026-04-29 | **`docs/testing.md`** обновлён под Windows: прямые `dotnet`/`docker`/`k6` команды стали основными, `make` помечен как optional shortcut. |
| 2026-04-29 | Добавлен **`docs/testing.md`**: полный testing runbook со smoke/infra/SignalR/API/video/observability командами, ожидаемыми результатами и troubleshooting. |
| 2026-04-28 | Добавлен performance tuning после 5000 WS теста: Kestrel/ThreadPool/GC/SignalR/Redis/proxy/resource limits, новые `Makefile` профили и `docs/performance-tuning.md`. |
| 2026-04-28 | **`docs/architecture.md`**: полная переработка под уровень Senior/Lead — целевая схема, KDD, потоки данных, observability, трейдоффы, next steps. |
| 2026-04-28 | **video-svc**: плейлист HLS V7 с одним `sample.mp4` и byte-range вместо `.ts` сегментов; файлы в `docker/video/content/video/`. |
| 2026-04-27 | Добавлена гибридная схема **HAProxy L4 + Nginx L7**: `realtime-svc`, `api-svc`, `video-svc`, media bypass на `:8081`, proxy/exporter metrics, ADR 0002 и обновлённая документация. |
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
