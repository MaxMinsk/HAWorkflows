# HAWorkflows

Visual workflow builder и deterministic runtime для локальных agentic pipelines.

## Что это

Инструмент для разработчиков, где рабочие процессы по задачам собираются как граф из нод и исполняются детерминированно. Модель (LLM/coding agent) подключается только там, где deterministic шаги не дают нужного качества.

Текущий фокус: **local-first workflow** для ускорения dev-задач (сбор контекста → evidence → plan → implementation).

## Стек

- **Backend:** ASP.NET Core (.NET 9), SQLite, filesystem artifact store
- **Frontend:** React 18 + Drawflow + TypeScript (Vite)
- **Packaging:** HA add-on (Docker, multi-arch), standalone local

## Быстрый старт (локально)

```bash
# Backend API
cd src/Workflow.Api
dotnet run

# Frontend (dev mode с HMR)
cd src/Workflow.Web/Frontend
npm install
npm run dev

# Web host (проксирует API + Frontend)
cd src/Workflow.Web
dotnet run
```

UI доступен на `http://127.0.0.1:5191`.

## Структура проекта

```
src/
  Workflow.Api/          # HTTP API, runs, settings, MCP
  Workflow.Engine/       # Runtime, nodes, agents, routing, validation
  Workflow.Persistence/  # SQLite workflow definitions
  Workflow.Web/          # SPA host + Frontend (React/Vite)
addon/                   # HA add-on packaging
Notes~/                  # Продуктовая документация, backlog, архитектура
```

## Документация

- [Product Vision](Notes~/product_vision.md)
- [Architecture](Notes~/Workflow%20server/architecture.md)
- [Platform Backlog](Notes~/Workflow%20server/backlog.md)
- [Local Pipeline Backlog](Notes~/Workflow%20server/local-pipeline.md)
- [Node Design](Notes~/Workflow%20server/nodes.md)
- [Frontend README](src/Workflow.Web/Frontend/README.md)

## CI / Build

- `.github/workflows/ci.yml` — typecheck + build (Node + .NET)
- `.github/workflows/addon-image.yml` — multi-arch Docker image → GHCR

## HA Add-on (legacy packaging)

Add-on store URL: `https://github.com/MaxMinsk/HAWorkflows`

Version update: bump `version` in `addon/config.yaml` → push to `main` → image published to GHCR.
