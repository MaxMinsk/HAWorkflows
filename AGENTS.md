# AGENTS.md — Worflows

## Обязательный процесс

Для всех задач в этом проекте обязательно соблюдать flow из:
- [Notes~/Workflow server/development_flow.md](/Users/maximkaz/Documents/Education AI/Worflows/Notes~/Workflow server/development_flow.md)
- [src/Workflow.Web/Frontend/README.md](/Users/maximkaz/Documents/Education AI/Worflows/src/Workflow.Web/Frontend/README.md)

Ключевые правила (обязательно):
- работаем по backlog (`P0 -> P1`);
- после завершения задачи обновляем `backlog.md` (`Done` + `Now`);
- тесты откладываем до завершения модуля;
- после задачи запускаем локально и выполняем ручную проверку;
- оставляем сервисы запущенными, чтобы пользователь мог проверить.
- для frontend решений считаем `src/Workflow.Web/Frontend/README.md` source of truth по архитектурным и TS-принципам.

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
