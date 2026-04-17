# Workflow Server Backlog

## Правила ведения

- Статусы: `todo | in_progress | done | blocked`.
- При завершении задачи переносим ее в раздел `Done` в кратком виде.
- Сначала закрываем `P0`, затем `P1`.

## UI ориентир (n8n-like)

Цель: похожий UX-паттерн, а не копия продукта.

Обязательные принципы для MVP UI:
- 3-панельный layout: `Nodes palette (left) | Canvas (center) | Properties/Inspector (right)`.
- Верхний toolbar: `Save`, `Run`, `Stop`, `Deploy (disabled в MVP)`, `Version`.
- Canvas: grid + zoom/pan + mini-map + node selection.
- Быстрый feedback: заметный статус run и node state (`pending/running/succeeded/failed`).
- Горячие клавиши: `Del`, `Ctrl/Cmd+S`, `Ctrl/Cmd+C/V`, `Ctrl/Cmd+Z/Y`.
- Никаких блокирующих модалок для частых действий (использовать inline panel и toast).

## P0 (MVP)

### HAWF-005 — Definitions API (CRUD)
- Status: `done`
- Priority: `P0`
- Задача: `POST/GET /workflows` + хранение в SQLite.
- Критерий: UI сохраняет и загружает workflow из backend.
- Зависимости: HAWF-001, HAWF-004

### HAWF-006 — Runtime MVP (Deterministic DAG)
- Status: `done`
- Priority: `P0`
- Задача: исполнение графа в topological order + node stubs (`Input`, `Transform`, `Log`, `Output`).
- Критерий: run завершается `succeeded/failed`, node статусы доступны.
- Зависимости: HAWF-005

### HAWF-007 — Run API + Run Timeline in UI
- Status: `done`
- Priority: `P0`
- Задача: `POST /runs`, `GET /runs/{id}`, `GET /runs/{id}/nodes` + отображение прогресса в UI.
- Критерий: run запускается из toolbar и видно progression по нодам.
- Зависимости: HAWF-006

### HAWF-008 — Trigger Layer v1 (manual + external_signal)
- Status: `done`
- Priority: `P0`
- Задача: `POST /signals/{source}` + idempotency key.
- Критерий: повтор одного сигнала не создает дубль run.
- Зависимости: HAWF-006

### HAWF-009 — HA Add-on Packaging
- Status: `done`
- Priority: `P0`
- Задача: контейнер API+UI, `ingress: true`, базовый options/env mapping.
- Критерий: UI доступен из Home Assistant sidebar, run выполняется в addon.
- Зависимости: HAWF-007, HAWF-008

## P1 (после MVP)

### HAWF-010 — Observability Baseline
- Status: `todo`
- Priority: `P1`
- Задача: structured logs + run/node correlation ids + базовые метрики.
- Критерий: можно отследить run end-to-end по логам и метрикам.
- Зависимости: HAWF-007

### HAWF-011 — Corporate Readiness Seams
- Status: `todo`
- Priority: `P1`
- Задача: abstraction для auth middleware, config-driven storage, подготовка SQLite->Postgres.
- Критерий: runtime запускается в HA и standalone с разным конфигом.
- Зависимости: HAWF-009

### HAWF-012 — Versioning & Draft/Publish Model
- Status: `todo`
- Priority: `P1`
- Задача: draft/published версии workflow.
- Критерий: run всегда привязан к конкретной версии.
- Зависимости: HAWF-005, HAWF-007

### HAWF-013 — Checkpoint/Resume for Runs
- Status: `todo`
- Priority: `P1`
- Задача: checkpoints на super-step/node boundary + resume/replay.
- Критерий: run можно продолжить после рестарта worker.
- Зависимости: HAWF-006

### HAWF-014 — Agent Adapter Contract (Cursor/Claude)
- Status: `todo`
- Priority: `P1`
- Задача: ввести `IAgentExecutor` (`Ask`, `CreateTask`, `GetStatus`, `GetResult`) без жесткой привязки к провайдеру.
- Критерий: минимум один adapter подключается как отдельный `AgentTask` node.
- Зависимости: HAWF-006

## Now (первый спринт)

1. HAWF-010
2. HAWF-011
3. HAWF-012

## Done

- HAWF-001 — Solution Skeleton: создана solution `WorkflowServer.sln`, добавлены проекты `Workflow.Api`, `Workflow.Engine`, `Workflow.Persistence`, `Workflow.Web`; `dotnet build` зеленый; endpoint `/health` отвечает.
- HAWF-002 — UI Shell (n8n-like): реализован web editor shell с 3-панельным layout (palette/canvas/inspector), toolbar (`Save/Run/Stop/Deploy`), responsive UI и `/health` в `Workflow.Web`.
- HAWF-003 — Graph Editor Basic Interactions: добавлены add/remove node, connect/disconnect edges, node selection, inspector update (`id/type/name/config JSON`), локальный save/restore графа и горячие клавиши (`Del`, `Ctrl/Cmd+S`, `Ctrl/Cmd+C/V`).
- HAWF-004 — Workflow JSON Schema v1 + Client Validation: добавлен `workflow.schema.v1.json`; реализована клиентская валидация структуры графа (schemaVersion, nodes/edges), целостности ссылок и проверки на циклы; невалидные графы не сохраняются, ошибки выводятся в Validation panel.
- HAWF-005 — Definitions API (CRUD): добавлены `GET /workflows`, `GET /workflows/{id}`, `POST /workflows`, SQLite-хранилище версий workflow, CORS для `Workflow.Web`; UI сохраняет/загружает workflow через backend и показывает список сохраненных workflow.
- HAWF-006 — Runtime MVP (Deterministic DAG): добавлен runtime-слой в `Workflow.Engine` (валидация графа, topological execution, node stubs `input/transform/log/output`, node statuses, logs, финальный output) и endpoint `POST /runtime/execute-preview` для локальной проверки исполнения.
- HAWF-007 — Run API + Run Timeline in UI: добавлены `POST /runs`, `GET /runs/{id}`, `GET /runs/{id}/nodes` (in-memory run state + background execution); UI toolbar `Run/Stop` теперь запускает run через backend, поллит статус и показывает node timeline/log entries.
- HAWF-015 — Workflow.Web React Migration: UI переведен с vanilla JS на React (Vite build в `wwwroot`), сохранены существующие сценарии `graph editing/save/load/run timeline`, код разложен по `app/shared/features` в `src/Workflow.Web/Frontend/src`.
- HAWF-016 — Workflow.Web Dev HMR Proxy: в Development добавлен proxy `Workflow.Web -> Vite dev server` (YARP, websocket-ready), `GET /health` теперь показывает frontend mode; debug-поток разработки работает через `http://127.0.0.1:5191` с HMR.
- HAWF-017 — App Decomposition (React Best Practices): `App.jsx` сокращен до композиционного слоя, orchestration вынесен в custom hook `useWorkflowBuilder`, layout разложен по компонентам `Toolbar/PalettePanel/CanvasPanel/InspectorPanel`.
- HAWF-018 — Hook Decomposition (React Best Practices): `useWorkflowBuilder` разделен на `useDrawflowEditor`, `useWorkflowStorage`, `useRunPolling`; orchestration слой оставлен тонким, доменная логика разнесена по feature hooks.
- HAWF-019 — Drawflow Editor Decomposition (React Best Practices): `useDrawflowEditor` дополнительно разрезан на `useDrawflowLifecycle`, `useDrawflowKeyboardShortcuts`, `useInspectorState`; в core-хуке оставлена только orchestration-логика графовых операций.
- HAWF-020 — Graph Definition Module Split (React Best Practices): `graphDefinition.js` разделен на специализированные модули `buildWorkflowDefinitionFromDrawflow`, `validateWorkflowDefinition`, `buildDrawflowImportFromDefinition` с barrel re-export для обратной совместимости импортов.
- HAWF-021 — Builder UI/Run Decomposition (React Best Practices): из `useWorkflowBuilder` вынесены инфраструктурные слои `useUiFeedback` (status/toast) и `useRunActions` (run start/stop orchestration); builder оставлен как composition-слой.
- HAWF-022 — Frontend TypeScript Migration: фронтенд полностью мигрирован с JS/JSX на TS/TSX (`src` + `vite.config.ts`), добавлены `tsconfig.json`, `typecheck` script, базовые доменные типы и декларация `drawflow`; сборки `npm run typecheck`, `npm run build`, `dotnet build` — зеленые.
- HAWF-023 — TypeScript Hardening & Shared Abstractions: включен `strict` в `tsconfig`, проставлены явные типы для props/hooks/graph-mappers, добавлена общая typed-утилита `shared/lib/errorMessage.ts`, `npx tsc --strict`, `npm run typecheck`, `npm run build` и `dotnet build` — зеленые.
- HAWF-024 — Drawflow Adapter Extraction: low-level операции Drawflow вынесены в `features/workflow/lib/drawflowGraphAdapter.ts`, `useDrawflowEditor` упрощен до orchestration-слоя; `npm run typecheck`, `npm run build`, `dotnet build` — зеленые.
- HAWF-025 — Workflow Storage Adapter Extraction: localStorage-операции из `useWorkflowStorage` вынесены в `features/workflow/lib/workflowStorageAdapter.ts`; в хуке оставлен orchestration flow и API-взаимодействие; `npm run typecheck`, `npm run build`, `dotnet build` — зеленые.
- HAWF-026 — Editor Restore Storage Decoupling: восстановление графа при старте (`restoreGraphFromLocalStorage`) переведено на `workflowStorageAdapter.readPersistedGraph()` без прямого обращения к `window.localStorage` в `useDrawflowEditor`; `npm run typecheck`, `npm run build`, `dotnet build` — зеленые.
- HAWF-027 — Workflow Graph Service Extraction: доменная логика конвертации/валидации графа вынесена из `useDrawflowEditor` в `features/workflow/lib/workflowGraphService.ts`; в хуке оставлена orchestration-логика для `validate/import`; `npm run typecheck`, `npm run build`, `dotnet build` — зеленые.
- HAWF-008 — Trigger Layer v1 (manual + external_signal): добавлен endpoint `POST /signals/{source}` с обязательным idempotency key (`Idempotency-Key` header или body), внешний сигнал запускает run c `triggerType=external_signal`; повторный сигнал в suppression window возвращает существующий `runId` без дублирования запуска.
- HAWF-009 — HA Add-on Packaging: добавлен add-on каркас (`addon/config.yaml`, `addon/Dockerfile`, `addon/run.sh`, `addon/README.md`, `repository.yaml`), ingress UI через `Workflow.Web:8099`, внутренний API `Workflow.Api:5188`, options->env mapping (`api_database_path`, suppression window), GitHub Actions для CI и публикации multi-arch образа в GHCR (`addon-image.yml`).
