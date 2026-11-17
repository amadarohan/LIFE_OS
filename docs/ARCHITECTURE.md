# Life OS — Architecture

> Personal Life Operating System. Local-first, single-user, approval-first AI.

## 1. Runtime baseline (verified on this machine)

| Item | Requested | Actual | Decision |
|---|---|---|---|
| .NET SDK | 10 LTS | 9.0.311 (also 8.0.428) | Target `net9.0`. .NET 10 SDK is not installed. |
| EF Core | 10 | 9.x | `Microsoft.EntityFrameworkCore.SqlServer` 9.x |
| SQL Server | any | SQL Server 2019 Enterprise, instance `MSSQLSERVER`, Windows auth | Used directly |
| Blazor | Web App, Interactive Server | `blazor --interactivity Server` | Used |

The deviation from .NET 10 is recorded in `DECISIONS.md` (ADR-001). Upgrading later is a
`TargetFramework` + package bump; no architectural change is required.

## 2. Style

**Modular monolith + Clean Architecture.** Four projects, one deployable.

```
LifeOS.Domain          entities, enums, value objects, domain rules, domain exceptions
LifeOS.Application     use cases, DTOs, validators, abstractions, engines (priority, capacity, ...)
LifeOS.Infrastructure  EF Core, AI providers, routing, tools, backup, file storage, logging
LifeOS.Web             Blazor components, layout, formatting services, composition root
```

Dependency direction:

```
Web ──► Application ──► Domain
 │            ▲
 └──► Infrastructure ──┘   (Infrastructure implements Application abstractions)
```

Hard rules enforced by project references:

- `Domain` references nothing.
- `Application` references `Domain` only. No EF Core, no Blazor.
- `Infrastructure` references `Application` + `Domain`.
- `Web` references `Application` + `Infrastructure` (composition root only).

## 3. Module map (Application/Features)

| Module | Responsibility |
|---|---|
| `LifeDomains` | domains, metrics, domain health |
| `Projects` | projects, milestones, dependencies, project↔goal links |
| `Goals` | goals, key results, progress derivation |
| `Tasks` | task CRUD, status transitions, dependencies, time entries |
| `Planning` | weekly/daily plans, time blocks, capacity |
| `Finance` | accounts, transactions, recurring, commitments, budgets, forecast |
| `Knowledge` | notes, journal, tags, knowledge links |
| `Decisions` | decisions, options, reviews |
| `Calendar` | events, commitments |
| `Analytics` | snapshots, weekly review, life debt, reality check, stagnation |
| `AI` | conversations, messages, insights, actions, approval, memory |
| `Settings` | app settings, provider config |
| `Backup` | backup/restore/export |

Each feature folder holds: `Dtos`, `Validators`, `*Service` (use case), and query objects.
No MediatR — the use cases are plain injected services. Rationale in `DECISIONS.md` (ADR-002).

## 4. Data access

- One `LifeOsDbContext` with `DbSet`s per aggregate root.
- Explicit `IEntityTypeConfiguration<T>` for every entity. No reliance on conventions for
  keys, indexes, delete behavior, precision, or concurrency.
- Read paths use `AsNoTracking()` + projection into DTOs. No N+1: aggregate queries are
  written as single projections or explicit `Include` chains.
- Write paths load tracked entities, call domain methods, then `SaveChangesAsync`.
- `TimeProvider` is injected everywhere time matters. Nothing calls `DateTimeOffset.UtcNow`
  outside `SystemTimeProvider`.
- `AuditInterceptor` writes `AuditLog` rows for tracked changes on audited entity types.
- `RowVersion` (`rowversion`) on `LifeDomain`, `Project`, `Goal`, `TaskItem`, `Decision`,
  `FinancialAccount`, `FinancialCommitment`, `WeeklyPlan`.

## 5. AI subsystem

```
Blazor UI (global panel + page-embedded + AI workspace)
        │
        ▼
AiAssistantService (Application)  ── conversation persistence, response contracts
        │
        ▼
AiOrchestrator (Infrastructure)
   ├── ContextEngine        builds minimal, traceable, privacy-tagged context
   ├── ProviderRouter       local vs cloud, sensitivity-aware
   ├── ToolRegistry         typed read/analysis/proposal/execution tools
   ├── ApprovalWorkflow     AIAction lifecycle, idempotency, staleness
   ├── MemoryService        profile / episodic / operational memory
   └── AiAuditTrail         AIExecutionLog
        │
        ▼
Application use cases ──► Domain ──► SQL Server
```

Non-negotiable invariants:

1. Providers never touch the database. They receive a prompt and return text/JSON.
2. No SQL generation, no arbitrary code execution, no direct EF entity mutation.
3. Every mutating tool is reachable **only** through an approved `AIAction`.
4. Sensitive context (finance, journal, health) is tagged `Sensitive`; the router refuses
   cloud providers for sensitive payloads unless the user has explicitly authorized it.
5. Retrieved user content is wrapped as data, never as instructions (prompt-injection guard).
6. A proposal is never reported as executed. Status transitions are explicit and audited.

Details: `AI_ARCHITECTURE.md`.

## 6. UI

- Blazor Web App, Interactive Server, RTL-first, Persian UI, English identifiers.
- Design system: dark gothic minimalism — near-black surfaces, muted gray type, restrained
  crimson accent, hairline borders, no gradients/glassmorphism.
- Centralized formatting: `IDateTimeFormatter` (Jalali), `IMoneyFormatter`, `INumberFormatter`.
  Jalali conversion lives in one place (`JalaliDateConverter`), never in components.
- Storage is always Gregorian UTC. Jalali is display-only.
- Global Quick Capture (`Ctrl+K`), global AI panel, sidebar navigation, breadcrumbs.

Details: `UI_UX_GUIDELINES.md`.

## 7. Security posture

Local-first, single user, no auth system. Still enforced: localhost binding, antiforgery,
upload validation, path-traversal guards, secret handling outside source control, no stack
traces in production UI, backup/restore confirmation gates. Details: `SECURITY.md`.

## 8. Testing

- `LifeOS.UnitTests` — domain rules, priority engine, capacity, finance math, approval rules,
  Jalali conversion, prompt-injection guard.
- `LifeOS.IntegrationTests` — EF mappings/constraints against real SQL Server (dedicated
  database, created and dropped per run), approval workflow end-to-end, backup/restore.
- AI tests use `FakeAiProvider`; no network calls in tests.
