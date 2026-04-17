# Analogs: что уже есть и что перенять

## 1) Контекст анализа

Цель: веб-платформа для **детерминированных workflow** с node-graph UI, где вызов внешнего агента (Cursor/Claude) — отдельный контролируемый шаг, а не “ядро всего процесса”.

Критерии оценки:
- deterministic-first
- удобный graph UX
- durability/replay/retry
- approval/HITL
- RBAC/SSO/audit
- self-host и production readiness

---

## 2) Ключевые аналоги

## A) n8n (визуальный workflow automation)
- Что близко: web graph-editor, executions, workflow history, RBAC/SSO, redaction.
- Сильная сторона: зрелый UX и ops-функции для production.
- Ограничение: не “строгий workflow engine” уровня durable replay как у Temporal.

Что перенять:
1. **Execution viewer** с пошаговой отладкой run.
2. **Workflow history/versioning** в UI.
3. **Redaction execution data** как per-workflow политика.
4. **RBAC через projects/roles** + SSO (OIDC/SAML).

Источники:
- https://docs.n8n.io/workflows/executions/
- https://docs.n8n.io/workflows/history/
- https://docs.n8n.io/workflows/executions/execution-data-redaction/
- https://docs.n8n.io/user-management/rbac/
- https://docs.n8n.io/user-management/oidc/

---

## B) Windmill (scripts + DAG + approval-heavy automation)
- Что близко: workflows как state machine DAG, retries, branches, suspend/approval.
- Сильная сторона: хороший Human-in-the-loop (email/Slack/Teams approval patterns).
- Ограничение: модельная часть для agent routing не центральная.

Что перенять:
1. **Suspend/Resume step** как first-class node.
2. **Approval URLs / внешние каналы approval** (Telegram/Slack/email).
3. **Branch after approval/disapproval/timeout**.
4. **Step-level retries + timeout** в UI и runtime.

Источники:
- https://www.windmill.dev/docs/getting_started/flows_quickstart
- https://www.windmill.dev/docs/flows/flow_approval

---

## C) Temporal (durable code-first orchestration)
- Что близко: deterministic execution, event history, replay-safe model, idempotent activities.
- Сильная сторона: надежность и масштабируемость long-running flows.
- Ограничение: не визуальный low-code конструктор “из коробки”.

Что перенять:
1. **Строгая модель детерминизма** для orchestrator-слоя.
2. **Разделение orchestration vs activity** (node orchestration отдельно от side effects).
3. **Event history + replay contract** для run.
4. **Versioning strategy** для эволюции workflow без поломки активных run.

Источники:
- https://docs.temporal.io/workflows
- https://docs.temporal.io/workflow-definition
- https://docs.temporal.io/activities
- https://docs.temporal.io/visibility

---

## D) Argo Workflows (K8s DAG engine)
- Что близко: DAG, параллелизм, artifact repository, fail-fast policy.
- Сильная сторона: production на Kubernetes, artifact-oriented execution.
- Ограничение: UX/сложность для бизнес-пользователей; K8s-first операционная модель.

Что перенять:
1. **Artifact repository abstraction** (S3/MinIO/Azure/GCS).
2. **Явная политика failFast / continue branches**.
3. **Task dependency model** (multiple roots, nested DAG/subflows).
4. **Секьюрный доступ к artifacts по namespace/policy**.

Источники:
- https://argo-workflows.readthedocs.io/en/latest/walk-through/dag/
- https://argo-workflows.readthedocs.io/en/latest/configure-artifact-repository/

---

## E) Dify / Langflow (agentic workflow platforms)
- Что близко: визуальные AI workflow, conditional branching, API-triggerable execution.
- Сильная сторона: быстрый путь к LLM-heavy сценариям, node orchestration для AI.
- Ограничение: при deterministic-first часто хочется более строгий runtime-контроль.

Что перенять:
1. **Node contracts и typed variables** между узлами.
2. **If/Else + aggregator patterns** для ветвлений.
3. **Workflow API** с sync/async run, status polling, stop endpoint.
4. **DSL import/export** для portability.

Источники:
- https://docs.dify.ai/en/use-dify/nodes/ifelse
- https://docs.dify.ai/versions/3-0-x/en/user-guide/application-orchestrate/readme
- https://docs.dify.ai/en/use-dify/knowledge/knowledge-pipeline/create-knowledge-pipeline
- https://docs.langflow.org/workflow-api

---

## F) LangGraph (durable agent workflows)
- Что близко: checkpoint/persistence, resume, interrupts (HITL), time-travel.
- Сильная сторона: agent-run reliability и отладка execution path.
- Ограничение: сильная заточка под LangChain/LangGraph ecosystem.

Что перенять:
1. **Checkpoint model** (thread/run scoped state snapshots).
2. **Interrupt/resume primitive** для human checkpoint.
3. **Правило “side effects inside tasks”** для корректного replay.
4. **Dev/studio trace UX** для инспекции prompts/tools/results.

Источники:
- https://docs.langchain.com/oss/python/langgraph/durable-execution
- https://docs.langchain.com/oss/javascript/langgraph/studio

---

## 3) Рекомендация для нашей архитектуры (что брать сразу)

### P0 (сразу в MVP)
1. n8n-style: executions timeline + workflow history/versioning.
2. Windmill-style: Suspend/Approval node (with timeout + approve/reject branches).
3. Temporal-style: strict orchestrator determinism + event log + idempotent node contract.
4. Langflow-style: sync/async run API (`start`, `status`, `stop`).

### P1 (после MVP)
1. Argo-style artifact layer (S3/MinIO), artifact refs между нодами.
2. n8n-style data redaction policy на уровне workflow/run.
3. LangGraph-style checkpoint/time-travel для replay от середины графа.

### P2 (enterprise hardening)
1. RBAC + SSO + project/namespace isolation.
2. Multi-tenant policy engine (budget, approvals, data classification).
3. Promotion pipeline: draft -> staged -> published workflow versions.

---

## 4) Что не копировать “как есть”

1. Не тащить heavy k8s-only операционную модель Argo в MVP.
2. Не делать model-first orchestration: AgentStep должен быть отдельным дорогим узлом.
3. Не смешивать orchestration logic и integration side-effects в одном node executor.
4. Не хранить секреты в definition JSON; только через secret refs.

---

## 5) Target blueprint (выжимка)

Минимальная формула:
- **UI/UX**: n8n-like
- **HITL**: Windmill-like
- **Runtime reliability**: Temporal-like
- **Artifacts**: Argo-like
- **AI node contracts**: Dify/Langflow-like
- **Checkpoint/resume**: LangGraph-like

Это дает баланс: быстрый продуктовый UX + инженерная надежность + контроль затрат на агентные вызовы.
