# Life OS — Implementation Roadmap

Each phase must build, run, and pass its tests before the next begins.
`IMPLEMENTATION_STATUS.md` is updated at the end of every phase.

## Phase 0 — Architecture baseline ✅

- Solution structure, project references, dependency rules
- `ARCHITECTURE.md`, `DATABASE_DESIGN.md` (with ERD), `AI_ARCHITECTURE.md`, `SECURITY.md`,
  `UI_UX_GUIDELINES.md`, `DECISIONS.md`, `LOCAL_SETUP.md`
- Environment verification (.NET SDK, SQL Server)

## Phase 1 — Life foundation

- Domain entities for all modules (single migration, complete schema)
- EF Core configurations, constraints, indexes, interceptors
- `InitialCreate` migration applied to SQL Server
- Application use cases: LifeDomains, Projects, Goals, Tasks
- Blazor shell: RTL layout, sidebar, topbar, design tokens, Vazirmatn
- Formatting services (Jalali, money, numbers)
- Pages: Dashboard, Life Domains, Projects (list/detail), Goals (list/detail), Tasks
- Quick Capture (task/note/journal/commitment)
- Settings (general, planning, finance)
- Unit + integration tests for the above

**Exit criteria:** app starts, migrations apply, CRUD works end-to-end, dashboard shows real
data, tests green.

## Phase 2 — Execution intelligence

- Task Inbox / Today / Upcoming / Overdue / Waiting / Deferred views
- Weekly plan + daily plan + time blocks
- Capacity engine (available vs planned, buffer, overload detection)
- Priority engine (documented, configurable, explainable)
- Project health + stagnation detection
- Progress snapshots, weekly review
- Reality check (estimate vs actual)

**Exit criteria:** a realistic week can be planned, overload is visible, stagnation is
detected, no automatic destructive status changes.

## Phase 3 — AI core

- `IAiProvider`, Ollama provider, OpenAI-compatible provider, fake provider
- Provider router with sensitivity gate
- Context engine with budgets, source references, injection guard
- Conversation + message persistence
- Typed tool registry (read / analysis / proposal / execution)
- Approval workflow with idempotency and staleness detection
- AI panel, AI workspace, AI action approvals page
- Memory service
- AI audit trail

**Exit criteria:** AI reads real context, proposes structured actions, cannot mutate without
approval, sensitive data never silently leaves the machine, provider failures degrade gracefully.

## Phase 4 — Life intelligence

- Finance: accounts, transactions, recurring, commitments, budgets, forecast
- Knowledge base, journal, tags, knowledge links
- Decision intelligence with reviews
- Calendar and commitments
- Life Debt, Opportunity Cost Analyzer, Personal Operating Review

**Exit criteria:** actuals and forecasts separated, decisions reviewable, life debt explainable,
opportunity cost uses real data.

## Phase 5 — Advanced intelligence

- Proactive insights, scheduled background analysis
- Weekly planning suggestions, monthly reviews
- Advanced memory management, advanced search
- Export/import enhancements

## Phase 6 — Hardening

- Full regression suite, migration review, backup/restore verification
- Security, performance, UI consistency, error handling reviews
- Documentation completion, local deployment guide
