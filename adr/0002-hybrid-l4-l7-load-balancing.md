# ADR 0002: Гибридная балансировка HAProxy L4 + Nginx L7

## Метаданные

| Поле | Значение |
|------|----------|
| **Статус** | Принято (Accepted) |
| **Дата** | 2026-04-27 |
| **Авторы** | Команда / владелец репозитория |

## Контекст

Проект демонстрирует разные профили high-load трафика: долгоживущие SignalR/WebSocket соединения, обычные REST API запросы и медиа-контент. Один L7-балансировщик хорошо подходит для WebSocket upgrade, sticky sessions и rate limiting, но не показывает production-паттерн, где быстрый L4 edge отделён от более «умной» HTTP-маршрутизации.

Важно: TCP-балансировщик в `mode tcp` не видит HTTP path. Поэтому path-based routing остаётся обязанностью L7, а прямой media bypass делается отдельным L4 frontend/портом.

## Решение

Использовать гибрид:

- **HAProxy** как внешний L4 entrypoint в TCP mode.
- **Nginx** как внутренний L7-шлюз для `/hubs/realtime`, `/realtime/*`, `/hub/*`, `/api/*` и UI.
- **video-svc** как отдельный media path, доступный напрямую через L4 на `:8081`.

Локальная схема портов:

| Порт | Путь |
|------|------|
| `8080` | клиент -> HAProxy L4 -> Nginx L7 -> `realtime-svc` / `api-svc` |
| `8081` | клиент -> HAProxy L4 -> `video-svc` напрямую |
| `8404` | HAProxy stats |

## Последствия

### Положительные

- Показывает production-like разделение fast path и smart path.
- SignalR остаётся за L7, где проще управлять WebSocket, sticky и rate limiting.
- REST API и video workload можно масштабировать отдельно от real-time hot path.
- Наблюдаемость расширяется до proxy-слоёв, а не только приложения.

### Отрицательные / риски

- Больше hop-ов, конфигурации и health checks.
- Нужно аккуратно прокидывать real client IP через PROXY protocol и `X-Forwarded-*`.
- В docker-compose service discovery проще, чем в реальном Kubernetes/Nomad окружении.
- Media bypass по path на одном TCP frontend невозможен без перехода HAProxy в HTTP mode или TLS termination на L4.

## Альтернативы

| Альтернатива | Почему не выбрана |
|--------------|-------------------|
| Только Nginx L7 | Проще, но хуже демонстрирует edge L4 и разные профили трафика. |
| HAProxy в HTTP mode | Позволяет routing по path, но перестаёт быть чистым L4-примером. |
| Envoy + Traefik | Более cloud-native, но тяжелее для текущего demo scope. |
| Kubernetes Ingress сразу | Production-like, но хуже для локального портфолио-запуска одним compose. |

## Связанные документы

- [docs/architecture.md](../docs/architecture.md)
- [docs/setup.md](../docs/setup.md)
- [ADR 0001: SignalR + Redis backplane](./0001-use-signalr-with-redis-backplane.md)
