# AGENTS.md — HAWorkflows

## Обязательный процесс

Для всех задач в этом проекте обязательно соблюдать flow из:
- [Notes~/Workflow server/development_flow.md](/Users/maximkaz/Documents/Education AI/HAWorkflows/Notes~/Workflow server/development_flow.md)
- [src/Workflow.Web/Frontend/README.md](/Users/maximkaz/Documents/Education AI/HAWorkflows/src/Workflow.Web/Frontend/README.md)

Ключевые правила (обязательно):
- для platform/runtime задач работаем по [Notes~/Workflow server/backlog.md](/Users/maximkaz/Documents/Education AI/HAWorkflows/Notes~/Workflow server/backlog.md) (`P0 -> P1`);
- для задач локального пайплайна используем **отдельный backlog**: [Notes~/Workflow server/local-pipeline.md](/Users/maximkaz/Documents/Education AI/HAWorkflows/Notes~/Workflow server/local-pipeline.md);
- после завершения задачи обновляем соответствующий backlog (`Done` + `Now`);
- после каждой завершенной задачи делаем git commit в текущей рабочей ветке;
- push/release выполняем только по явному запросу пользователя;
- тесты откладываем до завершения модуля;
- после задачи запускаем локально и выполняем ручную проверку;
- оставляем сервисы запущенными, чтобы пользователь мог проверить.
- для frontend решений считаем `src/Workflow.Web/Frontend/README.md` source of truth по архитектурным и TS-принципам.

## Текущий продуктовый фокус (обязательно)

- Текущий фокус: **локальный workflow** (не server-first).
- Активная ветка разработки для этого трека: `codex/local-flow`.
- Новую работу по local-first workflow ведем в текущей ветке `codex/local-flow`, пока явно не решим перейти на другую.
- Не проектируем отдельный remote/local product split на текущем этапе: сначала обкатываем local workflow, а remote/corporate режим вернем позже как deployment/runtime profile.
- Продуктовая цель: внутренний инструмент для разработчиков компании.
- Инструмент должен позволять разработчикам делиться между собой:
  - настройками workflow;
  - шаблонами и best practices выполнения задач.
- Обязательно собираем метрики по run (время/стоимость/успешность), чтобы распространять лучшие практики на основе данных.
- Обязательно оптимизируем расход токенов через stage-based стратегию моделей:
  - разные модели для разных этапов;
  - тяжелые модели только там, где это реально нужно.

## React стандарты (обязательно для UI-модулей)

Цель: модульный, читаемый, масштабируемый код.

## TypeScript стандарты (обязательно для UI-модулей)

- Пишем только на TypeScript: `*.ts` / `*.tsx`; новые `*.js` / `*.jsx` не добавляем.
- Обязателен `strict` mode в `tsconfig`; любые ослабления — только с явной задачей в backlog и объяснением причины.
- На границах модулей (props, публичные функции hooks, API payload/response, shared utils) задаем явные типы.
- `any` не используем; для внешнего/ненадежного payload используем `unknown` + type guards.
- Дублирующуюся инфраструктурную логику (например, нормализация ошибок, общие маппинги) выносим в `shared/lib` с типизированным контрактом.
- Перед завершением задачи для UI обязательно проходят:
  - `npm run typecheck`
  - `npm run build`

### 1) Структура по feature, а не по типам файлов

- Используем feature-first подход: код фичи хранится рядом (`co-location`).
- Не делаем "свалку" глобальных папок вида `components/`, `hooks/`, `utils/` для всего приложения без границ.
- Базовый каркас:

```text
src/
  app/                      # bootstrap, providers, router, global app wiring
  shared/                   # переиспользуемые части без бизнес-контекста
    ui/
    lib/
    hooks/
    api/
    types/
  features/
    <feature-name>/
      api/
      components/
      hooks/
      types/
      lib/
  routes/ or pages/         # route-level composition
```

Для текущего `Workflow.Web` (React frontend в `src/Workflow.Web/Frontend`) используем:

```text
src/Workflow.Web/Frontend/src/
  app/                      # bootstrap и композиция модулей
  shared/                   # api/ui/config без бизнес-контекста
  features/
    editor/
    workflow/
    runs/
```

### 2) Границы зависимостей (unidirectional imports)

- Разрешенный поток зависимостей: `shared -> features -> app/routes`.
- Внутри `features` запрещаем прямые импорты между разными фичами (композиция только на уровне `app/routes`).
- Для контроля границ используем ESLint (`import/no-restricted-paths`).

### 3) Компоненты и hooks

- Компоненты держим максимально "чистыми": рендер и композиция UI.
- Побочные эффекты и интеграции выносим в hooks/сервисы.
- Переиспользуемую stateful-логику выносим в custom hooks.
- Именование:
  - React components: `PascalCase`.
  - Custom hooks: `useXxx`.
  - Feature folders: `kebab-case`.

### 4) Конвенции файлов

- Один компонент/хук/модуль — один файл.
- Не использовать blanket barrel exports (`index.ts` на всю feature) по умолчанию; предпочитать явные импорты.
- Имена файлов и директорий должны быть консистентными по проекту (выбираем один стиль и держим его везде).

### 5) State management

- Локальный UI state — внутри компонента/feature.
- Глобальный state — только когда действительно нужен нескольким feature/route.
- При использовании Redux — структура и slices внутри feature (single-file logic per feature slice).

### 6) Framework-specific правила

- Если UI на Next.js App Router: соблюдаем file/folder conventions (`app/`, `layout`, `page`, route groups, private folders).
- Если UI SPA (Vite/React): сохраняем те же модульные принципы, но без Next-specific файловых конвенций.

## Источники (референс)

- React: [Thinking in React](https://react.dev/learn/thinking-in-react)
- React: [Reusing Logic with Custom Hooks](https://react.dev/learn/reusing-logic-with-custom-hooks)
- React: [eslint-plugin-react-hooks](https://react.dev/reference/eslint-plugin-react-hooks)
- Redux: [Style Guide (feature folders)](https://redux.js.org/style-guide/)
- Next.js: [Project Structure and Organization](https://nextjs.org/docs/app/getting-started/project-structure)
- Community baseline: [bulletproof-react / project-structure.md](https://github.com/alan2207/bulletproof-react/blob/master/docs/project-structure.md)
