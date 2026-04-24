<div align="center">

# highload-realtime-signalr-demo

**Демонстрация high-load real-time на .NET: SignalR, масштабирование, наблюдаемость**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?logo=blazor)](https://learn.microsoft.com/aspnet/core/blazor/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[Архитектура](docs/architecture.md) · [Запуск](docs/setup.md) · [Производительность](docs/performance.md) · [Стек](docs/tech-stack.md) · [ADR](adr/)

</div>

---

## Оглавление

- [О проекте](#о-проекте)
- [Ключевые возможности](#ключевые-возможности)
- [Результаты производительности](#результаты-производительности)
- [Быстрый старт](#быстрый-старт)
- [Структура репозитория](#структура-репозитория)
- [Документация](#документация)
- [Лицензия](#лицензия)

---

## О проекте

Репозиторий создан как **портфолио-демо** для работодателей и технических интервью: показать практику проектирования и сопровождения высоконагруженного **real-time** бэкенда на экосистеме **.NET**.

Фокус:

- десятки тысяч **одновременных** WebSocket-подключений (через **SignalR**);
- десятки тысяч **сообщений в секунду**;
- **горизонтальное масштабирование** нескольких инстансов приложения;
- **observability**: метрики и трассировки для анализа под нагрузкой.

> **Состояние кода:** клиент **Blazor WebAssembly** (.NET 10) + **MudBlazor**; **Docker** (nginx + compose с Redis). Серверный SignalR и интеграция UI с Hub — следующий этап ([документация](docs/), [ADR 0001](adr/0001-use-signalr-with-redis-backplane.md)).

---

## Ключевые возможности

| Область | Что планируется / демонстрируется |
|---------|-----------------------------------|
| **Real-time** | SignalR: группы, broadcast, устойчивость к переподключениям |
| **Scalability** | Несколько реплик за балансировщиком + **Redis backplane** |
| **Observability** | Метрики Kestrel/приложения, Redis, опционально OpenTelemetry + Grafana |
| **UI** | Blazor WASM + MudBlazor для наглядной визуализации потока событий |

---

## Результаты производительности

Конкретные цифры зависят от железа и конфигурации прогона. Методология, сценарии и **шаблон таблицы результатов** — в [**docs/performance.md**](docs/performance.md).

Ориентиры для демо-сценария (не SLA):

- порядок **10³–10⁴+** одновременных соединений;
- **10³–10⁴+** сообщений/с при контроле задержки (p95/p99).

После эталонных прогонов обновляйте `docs/performance.md` и при желании добавляйте скриншоты Grafana в `docs/images/performance/`.

---

## Быстрый старт

### Локально (текущий Blazor WASM)

```bash
git clone <URL-репозитория>
cd highload-realtime-signalr-demo
dotnet restore
dotnet run --project highload-realtime-signalr-demo.csproj
```

Откройте в браузере URL из вывода консоли (часто **http://localhost:5016** — см. `Properties/launchSettings.json`).

### Docker Compose

```bash
docker compose up --build
```

UI: **http://localhost:8080**, Redis для будущего Hub: **localhost:6379**. Подробности: [**docs/setup.md**](docs/setup.md).

---

## Структура репозитория

```text
highload-realtime-signalr-demo/
├── adr/                    # Architecture Decision Records
│   └── 0001-use-signalr-with-redis-backplane.md
├── docker/                 # nginx.conf для образа Blazor WASM
├── docs/                   # Основная документация (RAG-friendly)
│   ├── project-context.md
│   ├── architecture.md
│   ├── performance.md
│   ├── setup.md
│   ├── tech-stack.md
│   └── how-to-contribute.md
├── Dockerfile
├── docker-compose.yml
├── .dockerignore
├── .gitignore
├── Layout/                 # Blazor layout
├── Pages/                  # Страницы Blazor
├── Properties/
│   └── launchSettings.json
├── wwwroot/                # Статика WASM
├── App.razor
├── Program.cs
├── highload-realtime-signalr-demo.csproj
└── README.md
```

---

## Документация

| Раздел | Файл |
|--------|------|
| Контекст проекта и последние изменения (для ИИ и команды) | [docs/project-context.md](docs/project-context.md) |
| Архитектура, scale-out, observability | [docs/architecture.md](docs/architecture.md) |
| Нагрузочное тестирование и метрики | [docs/performance.md](docs/performance.md) |
| Локальный запуск и Docker | [docs/setup.md](docs/setup.md) |
| Технологии и обоснование | [docs/tech-stack.md](docs/tech-stack.md) |
| Вклад в проект | [docs/how-to-contribute.md](docs/how-to-contribute.md) |
| ADR: SignalR + Redis backplane | [adr/0001-use-signalr-with-redis-backplane.md](adr/0001-use-signalr-with-redis-backplane.md) |

---

## Лицензия

Проект распространяется по лицензии **MIT** — см. [LICENSE.txt](LICENSE.txt).

---

<div align="center">

Сделано для демонстрации **high-load** и **real-time** компетенций на **.NET**

</div>
