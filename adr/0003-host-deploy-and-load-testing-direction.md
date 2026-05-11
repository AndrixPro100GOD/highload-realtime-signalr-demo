# ADR 0003: Host deployment и честное нагрузочное тестирование

## Метаданные

| Поле | Значение |
|------|----------|
| **Статус** | Принято (Accepted) |
| **Дата** | 2026-05-12 |
| **Авторы** | Команда / владелец репозитория |

## Контекст

Проект вырос из локального demo stack в более близкий к production стенд:

- локальный compose нужен всем разработчикам и должен запускаться без привязки к конкретной машине;
- тяжёлые проверки WebSocket (`3000-5000+` соединений) лучше выполнять на отдельном host или максимально близко к нему;
- Docker-образы для HAProxy, Nginx, Prometheus, Grafana, `realtime-svc`, `api-svc`, `video-svc` собираются и публикуются через host-specific compose override;
- `LoadTester` должен честно отличать успешный publish traffic от connect/retry/idle состояний;
- документация и defaults не должны содержать персональные IP/hostnames, иначе quick start ломается у других разработчиков.

Во время тестов выяснилось, что прокси-слои вроде Tailscale Serve/HTTPS tunnel могут стать bottleneck раньше приложения. Поэтому результаты, полученные через такой слой, нельзя считать валидным baseline для 5000 WebSocket.

## Решение

1. **Portable defaults**
   - `LoadTester` по умолчанию использует `http://localhost:8080`.
   - `Makefile` по умолчанию использует `BASE_URL=http://localhost:8080`.
   - Удалённые стенды задаются явно через `--base-url`, `LOADTEST_BASEURL` или `BASE_URL`.

2. **Разделить локальный и host workflow**
   - Обычная локальная работа:

     ```bash
     docker compose up --build -d
     ```

   - Публикация на host:

     ```powershell
     docker context use host
     docker compose -f docker-compose.yml -f docker-compose.myhost.yml build
     docker compose -f docker-compose.yml -f docker-compose.myhost.yml push
     docker compose -f docker-compose.yml -f docker-compose.myhost.yml up -d
     ```

   - Для Windows добавлены батники:
     - `build-push-to-host.bat`
     - `deploy-to-host.bat`

3. **Host-specific compose override**
   - `docker-compose.myhost.yml` хранит image names для registry host.
   - Основной `docker-compose.yml` остаётся переносимым локальным entrypoint.

4. **Честный LoadTester**
   - Статусы `connected`, `connect-wait`, `connect-retry`, `disconnected`, `idle`, `publish-ok` разделены.
   - Финальная summary показывает:
     - `peak active connections`;
     - `publish ok`;
     - `target reached`;
     - `publish traffic observed`.
   - Если `target reached: no` или `publish traffic observed: no`, результат не считается валидным latency baseline.

5. **Где запускать heavy load**
   - Для baseline `3000-5000` WebSocket предпочтительно запускать `LoadTester` на host-машине или рядом с ней.
   - Для удалённого клиента разрешён явный `--base-url`, но proxy/tunnel слой должен быть указан в выводах как часть тестируемого пути.

## Последствия

### Положительные

- Quick start снова работает у любого разработчика без знания частных IP.
- Host deployment зафиксирован отдельным runbook и батниками.
- Нагрузочные тесты больше не выглядят успешными, если фактически проверяли только connect/retry layer.
- Результаты можно интерпретировать честно: приложение, edge, tunnel и load generator не смешиваются в один “p95”.

### Отрицательные / риски

- Появился дополнительный compose override и registry dependency.
- Для host workflow нужен заранее настроенный Docker context и доступ к registry.
- Результаты heavy tests всё ещё зависят от OS limits, firewall, Docker networking и места запуска load generator.
- Tailscale/HTTPS proxy удобен для smoke, но не является целевым путём для доказательства 5000 WebSocket baseline.

## Что считаем валидным baseline

Для `targeted` SignalR baseline:

```text
target reached:           yes
publish traffic observed: yes
publish ok:               > 0
peak active connections:  близко к target
```

Для `mixed` / fan-out сценариев допустимы controlled rejects/drops, если система не уходит в latency spiral и сохраняет наблюдаемое backpressure-поведение.

## Дальнейшее развитие

- Вынести host-specific registry адрес в `.env` или Compose variables.
- Добавить отдельный `docker-compose.prod-like.yml` для Linux host limits/sysctls.
- Зафиксировать recommended Linux tuning: `ulimit`, `somaxconn`, ephemeral ports, firewall.
- Добавить CI smoke для `docker compose config` и publish/build образов.
- На следующем этапе перейти от compose host workflow к Kubernetes/Gateway API, сохранив идеи ADR 0002.

## Связанные документы

- [docs/docker-publish.md](../docs/docker-publish.md)
- [docs/testing.md](../docs/testing.md)
- [docs/performance-tuning.md](../docs/performance-tuning.md)
- [docs/setup.md](../docs/setup.md)
- [ADR 0002: Гибридная балансировка HAProxy L4 + Nginx L7](./0002-hybrid-l4-l7-load-balancing.md)
