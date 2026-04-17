# MAF для рабочих use cases (локально и через Cursor API)

## Короткий ответ

Да, смысл есть, но с оговоркой:

- если нужен **повторяемый workflow** (Jira + Wiki + шаблонный план + контроль шагов) — MAF полезен;
- если нужен только “поболтать как в Cursor” без интеграций/процессов — MAF обычно избыточен;
- если вообще **без модели** (ни cloud, ни локальной) — ценность ограничится детерминированной автоматизацией и шаблонами.

## Главная цель (зафиксировано)

Для этого сценария приоритет такой:

1. **Детерминированный workflow** как основной режим.
2. **Экономия токенов/денег** за счет выполнения максимума шагов без модели.
3. Модель подключается только там, где deterministic-шаги уже не дают приемлемого качества.

Это означает подход **model-last**, а не model-first.

## Режимы по модели/API

Есть 4 практичных режима:

1. **No-LLM**  
   Только deterministic logic: парсинг тикета, сбор linked pages, генерация плана по жесткому шаблону.
2. **Local-LLM**  
   Модель локально (Ollama/vLLM/llama.cpp), MAF ходит в локальный OpenAI-compatible endpoint.
3. **Cursor Agent API (token)**  
   MAF вызывает удаленного Cursor background agent через `https://api.cursor.com` как tool/executor и получает результат асинхронно.
4. **Hybrid later**  
   База на local-LLM, сложные случаи можно позже отправлять в cloud (опционально).

Для задачи “посмотри Jira + Wiki + составь план” практичны режимы **Local-LLM** или **Cursor Agent API**.

## Важное уточнение: Cursor token и роль Cursor в MAF

Если ты хочешь использовать **Cursor token** и общаться "через Cursor Agent", это обычно не равно "Cursor как прямой chat-completions backend для MAF".

- У Cursor есть API для **Background Agents** (создание/мониторинг задач по Bearer token на `api.cursor.com`).
- У Cursor есть BYOK для IDE, где Cursor использует ключи провайдеров внутри своего продукта.
- Официальный фокус Cursor API сейчас именно на agent-task API, а не на универсальном endpoint вида "используй Cursor как обычную модель в любом SDK".

Практичный вариант:

1. MAF использует локальную/обычную OpenAI-compatible модель для чата, классификации, планирования.
2. Cursor Agent API подключается как **инструмент-исполнитель** (tool) внутри workflow:
   - `cursor_agent_create_task`
   - `cursor_agent_get_status`
   - `cursor_agent_get_artifacts`
3. MAF оркестрирует Jira/Wiki/гейты, а Cursor-агент выполняет кодовые подпроцессы.

## Когда имеет смысл сделать Cursor "основной мозг"

Можно сделать режим, где почти все сложные инженерные шаги выполняет Cursor background agent, а MAF остается оркестратором:

- MAF собирает контекст (Jira/Wiki/репо-метаданные),
- формирует task prompt,
- запускает Cursor Agent,
- опрашивает статус,
- публикует результат и gate-решение.

Это особенно полезно, если хочешь сохранить единый "стиль Cursor-агента" и использовать его сильную сторону в кодогенерации.

## Ограничения режима Cursor Agent API

- Асинхронная модель взаимодействия (create -> poll -> fetch result), не мгновенный chat-completions цикл.
- Нужен контроль таймаутов/ретраев/идемпотентности задач.
- Важно хранить audit trail: кто запустил задачу, какой prompt, какой commit/PR получен.
- Для “диалогового” UX в чате обычно нужен отдельный быстрый LLM слой или кэшированный статусный ответ.

## Где MAF реально помогает разработчику

MAF полезен как **оркестратор работы**, а не как просто чат:

- разделение на шаги (workflow/executors);
- tool calling к Jira/Confluence/Git;
- checkpointing/observability/HITL;
- одинаковый процесс для всех тикетов (меньше случайности).

## Где MAF не лучший выбор

- ad-hoc вопросы в IDE без процесса;
- если важна только скорость “одного ответа”;
- если нет готовности поднять локальные интеграции (Jira/Confluence auth, кэш, ACL).

## Целевой workflow: `Ticket -> Context -> Plan`

### Вход

- `JIRA-123`
- опционально: sprint/priority/owner

### Шаги (workflow graph)

1. `LoadTicketExecutor`  
   Забрать summary/description/AC/labels/components/links.
2. `LoadWikiContextExecutor`  
   Прочитать связанные wiki pages (Confluence), вытащить релевантные секции.
3. `LoadCodeContextExecutor`  
   Найти затрагиваемые модули (git paths, ownership, последние PR).
4. `BuildEvidencePackExecutor`  
   Собрать единый структурированный контекст (JSON/MD).
5. `PlanDraftAgentExecutor` (LLM локально)  
   Сгенерировать план: шаги, риски, тесты, rollback.
6. `QualityGateExecutor`  
   Проверить минимальные требования (есть AC mapping, test plan, risks).
7. `HumanApprovalExecutor`  
   Подтверждение человеком.
8. `PublishPlanExecutor`  
   Сохранить итог в файл/коммент в Jira/Confluence.

### Выход

- `plans/JIRA-123-plan.md`
- опционально: Jira comment + чеклист subtasks

## Минимальный полезный scope (MVP)

Сделать один workflow:

- команда: `plan JIRA-123`
- чтение Jira + Confluence
- локальный draft плана
- сохранение markdown

Без автозаписи в Jira на первом этапе (только файл + ручное подтверждение).

## Что должно работать вообще без LLM (обязательный baseline)

Полезные вещи без модели:

- извлечение AC и linked docs;
- генерация каркаса плана по шаблону;
- checklist “что не хватает” (нет AC, нет NFR, нет rollback);
- сбор метаданных по коду (ownership, затронутые модули).

Но “качественный план” без LLM обычно слабее.

## Гейтинг: когда модель реально разрешена

LLM-вызов допускается только если выполнено хотя бы одно условие:

- не хватает структурных данных для детерминированного вывода;
- нужно сжать большой контекст в читабельный summary для человека;
- нужно сгенерировать вариант решения, где допустима эвристика.

Во всех остальных случаях — строгий deterministic path и нулевые токены.

## Минимальная cost-policy

- По умолчанию: `LLM disabled`.
- Команда/флаг run: `allow_llm=true` только для конкретного шага.
- Лимиты:
  - максимум LLM-вызовов на run;
  - бюджет токенов на run;
  - fail-closed: при превышении лимита шаг автоматически уходит в deterministic fallback.

## Рекомендованная архитектура

- MAF runtime (C#)
- tools:
  - `jira_get_issue`
  - `jira_get_linked_issues`
  - `confluence_get_page`
  - `git_collect_context`
- local model endpoint (OpenAI-compatible)
- SQLite для run history + artifacts
- output folder: `workspace/plans/`

## Cursor-подобный UX локально

Можно сделать так:

- в Cursor/IDE ты пишешь команду (`/plan JIRA-123`);
- локальный агент (MAF service) выполняет workflow;
- возвращает результат и файл плана.

То есть Cursor = интерфейс, MAF = движок процесса.

## Риски и ограничения

- локальная модель может хуже держать длинный enterprise-контекст;
- качество зависит от качества evidence pack и prompt-контракта;
- нужно аккуратно контролировать доступы (Jira/Confluence tokens, ACL).

## Итоговая рекомендация

Для твоего сценария (Jira/Wiki -> план) MAF имеет смысл, если цель:

- не просто чат, а **стандартизированный процесс подготовки плана**;
- повторяемость и трейсируемость решений;
- постепенная автоматизация dev workflow.

Практичный путь:

1. запустить `Ticket -> Plan` workflow локально;
2. зафиксировать формат результата;
3. добавить quality gates и approval;
4. только потом расширять (subtasks, release notes, test strategy generation).
