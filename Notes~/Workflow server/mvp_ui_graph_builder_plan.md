# План реализации: минимальный каркас UI для построения workflow-графов

## 1) Цель MVP

Собрать рабочий минимум, где можно:
- визуально собрать граф workflow в web UI,
- сохранить/загрузить workflow,
- запустить workflow вручную,
- увидеть базовый статус выполнения по нодам.

Промежуточный деплой: **как Home Assistant Add-on**.  
Целевой деплой позже: **корпоративная среда** (без переписывания ядра).

## 2) Scope MVP (что делаем / не делаем)

### Делаем
- Web UI: canvas + drag/drop node + edge connections.
- Backend API: CRUD workflow definition + start run + get run status.
- Runtime: исполнение простого DAG (без сложных retries/policies).
- 3-4 deterministic node-заглушки (например `Input`, `Transform`, `Log`, `Output`).
- HA Add-on упаковка и запуск через ingress.

### Пока не делаем
- Полноценный production-grade scheduler.
- Сложные approval policies.
- Корпоративные SSO/RBAC (оставляем seam для будущего).
- Полноценные интеграции Jira/Logs/Wiki (только каркас node types).

## 3) Технологический каркас

- **Frontend**: React + React Flow + TypeScript.
- **Backend**: ASP.NET Core (Minimal API).
- **Storage (MVP/HA)**: SQLite.
- **Container**: один контейнер с API + static UI.
- **HA интеграция**: Add-on с `ingress: true`.

Принцип: runtime-ядро отделено от hosting, чтобы позже поднять те же API в корпоративном k8s.

## 4) Минимальная архитектура модулей

1. `Workflow.Api`  
   REST endpoints, валидация входа, DTO.

2. `Workflow.Engine`  
   DAG validation + execution order + run state machine.

3. `Workflow.Persistence`  
   SQLite repository для definitions/runs/node-runs.

4. `Workflow.Web`  
   React UI (editor + run monitor).

5. `Workflow.Host.HAAddon`  
   Конфиг контейнера/ingress для Home Assistant.

## 5) Пошаговый план

## Этап 0. Bootstrap (1-2 дня)
- Создать solution и проекты (`Api`, `Engine`, `Persistence`, `Web`).
- Настроить локальный запуск (`docker compose` + `dotnet watch` + `vite`).
- Зафиксировать workflow JSON schema v1.

Критерий готовности:
- Пустой UI открывается, API `/health` отвечает.

## Этап 1. Graph Editor MVP (3-4 дня)
- Canvas на React Flow.
- Добавление/удаление node.
- Соединение edge.
- Sidebar с node properties (id/type/name/config json).
- Export/import JSON.

Критерий готовности:
- Можно собрать граф из 3+ нод и сохранить JSON локально.

## Этап 2. Backend Definition API (2-3 дня)
- `POST /workflows` (create/update draft).
- `GET /workflows/{id}`.
- `GET /workflows` (list).
- Валидация: acyclic graph, существующие node id, корректные edges.
- SQLite таблицы `workflow_definitions`.

Критерий готовности:
- UI сохраняет workflow в backend и загружает обратно.

## Этап 3. Runtime MVP (3-4 дня)
- `POST /runs` (manual trigger).
- `GET /runs/{runId}`.
- `GET /runs/{runId}/nodes`.
- Последовательное исполнение DAG по topological order.
- Node executors-заглушки:
  - `InputNode`
  - `TransformNode` (простая deterministic трансформация)
  - `LogNode`
  - `OutputNode`
- SQLite таблицы `workflow_runs`, `node_runs`, `run_events`.

Критерий готовности:
- Из UI можно нажать Run и увидеть статусы нод до `Succeeded/Failed`.

## Этап 4. Trigger Layer v1 (2 дня)
- Поддержать `trigger_type`:
  - `manual`
  - `external_signal` (webhook endpoint)
- `POST /signals/{source}` -> создание run.
- Idempotency key для external signal.

Критерий готовности:
- Один и тот же внешний сигнал не создает дублирующие run.

## Этап 5. HA Add-on packaging (2-3 дня)
- Dockerfile для API + UI.
- `addon/config.yaml`: `ingress: true`, options schema, env mapping.
- Healthcheck/startup script.
- Документация: как установить и обновлять add-on.

Критерий готовности:
- UI открывается из Home Assistant sidebar, workflow можно создать и запустить.

## Этап 6. Hardening перед корпоративным переносом (2-3 дня)
- Вынести конфиг storage/provider в env.
- Добавить abstraction для auth middleware (пока noop для HA).
- Structured logs + базовые метрики.
- Подготовить миграцию SQLite -> Postgres.

Критерий готовности:
- Один и тот же runtime стартует в HA и вне HA с разным конфигом.

## 6) Минимальные REST endpoints (MVP)

- `GET /health`
- `POST /workflows`
- `GET /workflows`
- `GET /workflows/{id}`
- `POST /runs`
- `GET /runs/{id}`
- `GET /runs/{id}/nodes`
- `POST /signals/{source}`

## 7) Модель данных MVP

- `workflow_definitions(id, name, version, graph_json, status, updated_at)`
- `workflow_runs(run_id, workflow_id, trigger_type, trigger_payload_json, status, started_at, ended_at)`
- `node_runs(run_id, node_id, status, started_at, ended_at, error)`
- `run_events(event_id, run_id, ts, level, event_type, payload_json)`

## 8) План миграции HA -> corporate

1. Сохранить API контракты неизменными.
2. Переключить storage SQLite -> Postgres.
3. Добавить auth proxy / OIDC middleware.
4. Вынести worker в отдельный deployment.
5. Подключить ingress + observability stack.

Важно: HA addon остается как “single-node deployment profile”, корпоративный контур — “scaled profile”.

## 9) Риски и как снизить

- Риск: UI и runtime “слипнутся” в один монолит.  
  Мера: отдельные проекты + контракт через REST.

- Риск: расширение node types сломает старые workflow.  
  Мера: versioned schema и backward-compatible parser.

- Риск: дубли run по внешним сигналам.  
  Мера: idempotency key + suppression window.

- Риск: зависимость от HA-специфики.  
  Мера: HA-only слой только в `Host.HAAddon`.

## 10) Definition of Done для MVP

- Пользователь в HA открывает UI, собирает граф, сохраняет его, запускает вручную и видит статусы нод.
- Внешний webhook может создать run через `external_signal`.
- Каркас легко переносится в отдельный корпоративный deployment без изменения Engine/API контрактов.
