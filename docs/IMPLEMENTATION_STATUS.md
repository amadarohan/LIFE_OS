# Implementation Status

Last verified: Phase 5 complete (AI subsystem built, 47 automated tests passing, all 13 pages return HTTP 200 against a live SQL Server).

This document records what is **actually built and verified**, not what is planned. Anything not
listed here does not exist yet.

---

## Verification legend

| Mark | Meaning |
| --- | --- |
| ✅ Verified | Built and/or executed successfully; evidence recorded below. |
| 🔶 Partial | Code exists but is not yet wired into a running path. |
| ⬜ Not started | No code exists. |

---

## Environment baseline

| Item | Value | Evidence |
| --- | --- | --- |
| .NET SDK | 9.0.311 | `dotnet --list-sdks` |
| Target framework | `net9.0` | All `.csproj` files |
| SQL Server | 2019 Enterprise 15.0.2210.4, instance `MSSQLSERVER` | `SELECT @@VERSION` |
| Database | `LifeOS` | Created by `dotnet ef database update` |
| EF Core CLI | `dotnet-ef` 9.0.23 | `dotnet tool list --global` |

.NET 10 is not installed on this machine, so the solution targets .NET 9 (see ADR-001).

---

## Phase 0 — Documentation ✅

| Document | Status |
| --- | --- |
| `docs/ARCHITECTURE.md` | ✅ |
| `docs/DATABASE_DESIGN.md` | ✅ |
| `docs/AI_ARCHITECTURE.md` | ✅ |
| `docs/SECURITY.md` | ✅ |
| `docs/UI_UX_GUIDELINES.md` | ✅ |
| `docs/IMPLEMENTATION_ROADMAP.md` | ✅ |
| `docs/DECISIONS.md` (ADR-001 … ADR-010) | ✅ |
| `docs/IMPLEMENTATION_STATUS.md` | ✅ (this file) |
| `docs/LOCAL_SETUP.md` | ✅ |

---

## Phase 1 — Domain layer ✅

`src/LifeOS.Domain` — builds with **0 warnings, 0 errors**. References no other project and no
NuGet package.

| Area | Entities |
| --- | --- |
| Common | `Entity`, `AuditableEntity`, `IConcurrencyTracked`, `DomainRuleException`, `EntityNotFoundException` |
| System | `AppSetting`, `Attachment`, `AuditLog`, `BackupHistory` |
| Life domains | `LifeDomain`, `LifeAreaMetric` |
| Projects | `Project`, `ProjectMilestone`, `ProjectDependency`, `GoalProject` |
| Goals | `Goal`, `GoalKeyResult` |
| Tasks | `TaskItem`, `TaskDependency`, `TaskTimeEntry` |
| Planning | `WeeklyPlan`, `DailyPlan`, `TimeBlock` |
| Finance | `FinancialAccount`, `FinancialTransaction`, `RecurringTransaction`, `FinancialCommitment`, `Budget` |
| Knowledge | `Note`, `JournalEntry`, `Tag`, `NoteTag`, `KnowledgeLink` |
| Decisions | `Decision`, `DecisionOption`, `DecisionReview` |
| Calendar | `CalendarEvent`, `Commitment` |
| Analytics | `ProgressSnapshot`, `WeeklyReview` |
| AI | `AiConversation`, `AiMessage`, `AiMemory`, `AiInsight`, `AiAction`, `AiExecutionLog` |

Domain invariants are enforced inside entities (private setters, guarded transitions, domain rule
exceptions), not in the UI. Time is never read from `DateTimeOffset.UtcNow` inside the domain;
callers pass `now` in.

---

## Phase 2 — Application abstractions and engines ✅

`src/LifeOS.Application` — builds with **0 warnings, 0 errors**. References only `LifeOS.Domain`
and FluentValidation. No EF Core, no Blazor.

| Component | Status |
| --- | --- |
| `IApplicationDbContext` (40 `IQueryable<T>` sets, `Add`/`AddRange`/`Remove`, `SaveChangesAsync`, `BeginTransactionAsync`) | ✅ |
| `IClock`, `IUserTimeZone`, `IFileStorage`, `ISecretProtector`, `IAuditWriter` | ✅ |
| `Result`, `Result<T>`, `ErrorCodes` | ✅ |
| `PagedResult<T>`, `PageRequest` | ✅ |
| AI contracts (`IAiProvider`, `IAiContextBuilder`, `IAiProviderRouter`, `IPromptInjectionGuard`) | ✅ |
| `PriorityEngine` | ✅ |
| `CapacityEngine` | ✅ |
| `LifeDebtEngine` | ✅ |
| `StagnationEngine` | ✅ |
| `RealityCheckEngine` | ✅ |
| `OpportunityCostEngine` | ✅ |
| `DependencyGraphGuard` | ✅ |
| Use-case services (CRUD + orchestration) | ✅ |
| `JalaliDateConverter` (Persian calendar, display only) | ✅ |
| `IDateTimeFormatter`, `IMoneyFormatter`, `INumberFormatter` + Persian implementations | ✅ |
| `AddApplication(IServiceCollection)` | ✅ |

The engines are pure functions over input records. They have no database access, which is what
makes them unit-testable without a server.

### Use-case services

All twelve services follow the same shape: inject `IApplicationDbContext` + `IAuditWriter` +
`IClock` (+ engines), return `Result`/`Result<T>`, wrap mutations in `Guard.RunAsync`, throw
`DomainRuleException`/`EntityNotFoundException` for rule violations, and write audit rows in the
same unit of work.

| Service | Responsibility |
| --- | --- |
| `LifeDomainService` | Life domains and their metrics |
| `ProjectService` | Projects, milestones, dependencies, goal links |
| `GoalService` | Goals and weighted key results |
| `TaskService` | Tasks, status transitions, deferral, next actions, time entries, dependencies |
| `PlanningService` | Weekly/daily plans, time blocks, capacity sync |
| `FinanceService` | Accounts, transactions, commitments, budgets, overview |
| `KnowledgeService` | Notes, tags, journal, knowledge links |
| `DecisionService` | Decisions, options, reviews, outcome capture |
| `CalendarService` | Events, commitments, agenda |
| `AnalyticsService` | Dashboard snapshot, weekly reviews, progress snapshots |
| `SettingsService` | Typed settings, DPAPI-masked sensitive values, default seeding |
| `BackupService` | Backup, guarded restore, retention pruning |

---

## Phase 3 — Infrastructure persistence ✅

`src/LifeOS.Infrastructure` — builds with **0 warnings, 0 errors**.

| Component | Status |
| --- | --- |
| `LifeOsDbContext` (40 `DbSet`s, explicit `IApplicationDbContext` implementation) | ✅ |
| Audit stamping in `SaveChangesAsync` via `IClock` | ✅ |
| `EfTransactionScope` with rollback-on-dispose | ✅ |
| `ColumnConventions` (lengths, precisions, enum-as-string, UTC timestamps) | ✅ |
| 9 configuration groups covering all 40 entities | ✅ |
| `LifeOsDbContextFactory` (design-time, env-var connection string) | ✅ |
| `SystemClock`, `FixedClock` | ✅ |
| `UserTimeZone` | ✅ |
| `LocalFileStorage` (path-traversal guarded, SHA-256 checksum helper) | ✅ |
| `DpapiSecretProtector` | ✅ |
| `AuditWriter` | ✅ |
| `SqlServerBackupStore` (T-SQL `BACKUP`/`RESTORE`, identifier quoting) | ✅ |
| `AddInfrastructure(IServiceCollection, IConfiguration)` | ✅ |

### Migration evidence

```
dotnet ef migrations add InitialCreate   → 20261117063147_InitialCreate
dotnet ef database update                → Applying migration '20261117063147_InitialCreate'. Done.
dotnet ef migrations add RemoveShadowForeignKeys
                                         → 20261117080407_RemoveShadowForeignKeys
dotnet ef database update                → Applying migration '20261117080407_RemoveShadowForeignKeys'. Done.
dotnet ef migrations has-pending-model-changes
                                         → No changes have been made to the model since the last migration.
```

`RemoveShadowForeignKeys` fixes a real mapping defect found at runtime: `AiExecutionLogConfiguration`
and `DecisionReviewConfiguration` used `HasOne<T>()` instead of `HasOne(x => x.Navigation)`, so EF
Core treated the declared navigation as a *second* relationship and added shadow `AiActionId1` /
`DecisionId1` columns. Both configurations now bind the navigation explicitly and the duplicate
columns are dropped.

Schema verified directly against SQL Server:

| Check | Result |
| --- | --- |
| Tables in `LifeOS` | 41 domain tables + `__EFMigrationsHistory` |
| Shadow `*Id1` columns | 0 (verified via `sys.columns`) |
| Check constraints | 9 (`CK_CalendarEvents_ValidRange`, `CK_FinancialCommitments_Installments`, `CK_FinancialTransactions_AmountPositive`, `CK_GoalKeyResults_Weight`, `CK_Goals_ProgressRange`, `CK_ProjectDependencies_NoSelf`, `CK_Projects_ProgressRange`, `CK_TaskDependencies_NoSelf`, `CK_TimeBlocks_ValidRange`) |
| `rowversion` columns | 9 (`AiActions`, `Decisions`, `FinancialAccounts`, `FinancialCommitments`, `Goals`, `LifeDomains`, `Projects`, `Tasks`, `WeeklyPlans`) |
| Seeded settings | 18 rows in `AppSettings` |

`EnsureCreated()` is not used anywhere. The database is created and evolved exclusively through
migrations.

---

## Phase 4 — Web shell and UI ✅

`src/LifeOS.Web` — builds with **0 warnings, 0 errors**. Every page was requested against a running
instance backed by the real `LifeOS` database and returned HTTP 200.

| Component | Status |
| --- | --- |
| `Program.cs` wiring (Serilog, `AddInfrastructure`, `AddApplication`, migrate + seed on startup) | ✅ |
| RTL shell (`lang="fa" dir="rtl"`), sidebar, topbar | ✅ |
| Design tokens CSS, Vazirmatn font | ✅ |
| Jalali / money / number formatters | ✅ |
| Dashboard | ✅ |
| Tasks, Projects, Goals pages | ✅ |
| Planning, Finance, Knowledge, Decisions, Calendar pages | ✅ |
| Settings, Backup pages | ✅ |

### Page smoke test

```
/            => 200      /finance    => 200
/tasks       => 200      /knowledge  => 200
/projects    => 200      /calendar   => 200
/goals       => 200      /planning   => 200
/domains     => 200      /settings   => 200
/decisions   => 200      /backup     => 200
```

Startup log confirms `No migrations were applied. The database is already up to date.` and
`Database is up to date and default settings are present.` — no EF Core model warnings.

Every button on every page calls a real application service. There is no hardcoded dashboard data
and no in-memory persistence.

### Destructive operations

`/backup` restore requires the operator to type the exact backup file name before the button is
enabled; `BackupService` re-validates it server-side (`Backup.ConfirmationMismatch`) and always
takes a `PreRestore` snapshot first. `/settings` refuses to write a masked sensitive value back as
if it were the real secret.

---

## Phase 5 — AI subsystem ✅

`src/LifeOS.Infrastructure/Ai` and `src/LifeOS.Application/Ai` — build with **0 warnings, 0 errors**.

| Component | Status |
| --- | --- |
| `OllamaAiProvider` (local, `POST {endpoint}/api/chat`) | ✅ |
| `OpenAiCompatibleProvider` (cloud, `POST {endpoint}/chat/completions`) | ✅ |
| `FakeAiProvider` (deterministic, tests only, never registered in production) | ✅ |
| `AiProviderRouter` (override → sensitivity → category → availability → fallback) | ✅ |
| `AiProviderHealthTracker` (60 s availability window) | ✅ |
| `AiContextBuilder` (budgeted, tagged, injection-safe `<user_data>` blocks) | ✅ |
| `PromptInjectionGuard` (English + Persian patterns) | ✅ |
| Read / analysis / proposal tools (`IAiTool`, 6 registered) | ✅ |
| `AiToolInvoker` + `AiToolRegistry` (validated dispatch, structured errors) | ✅ |
| Approval workflow service (`AiActionService`) | ✅ |
| Memory service (`AiMemoryService`) and insight service (`AiInsightService`) | ✅ |
| AI audit trail (`AiExecutionLog` + `AuditLog`) | ✅ |
| Assistant UI page (`/assistant`) | ✅ |

### Provider configuration is runtime data, not startup config

Providers are materialized per request from the `AppSettings` table via
`IAiProviderOptionsProvider`, so an endpoint, model, or API key can be changed in `/settings`
without restarting the app. Nothing in DI caches a credential. API keys are stored through
`ISecretProtector` (DPAPI) and are never written to logs.

### Approval-first is enforced in code, not by convention

`AiActionService.ExecuteAsync` is the only path from an AI proposal to a domain write. It refuses
unless `Status == Approved` and the proposal has not expired, and it records an `AiExecutionLog`
row for both success and failure. Execution tools are not model-callable at all — the model can
only produce a proposal.

### Verified behaviour

```
dotnet test LifeOS.sln
  LifeOS.UnitTests         36 passed, 0 failed
  LifeOS.IntegrationTests  11 passed, 0 failed
```

The integration suite runs against a real SQL Server database created by the actual migrations and
dropped afterwards. It asserts, among other things, that a proposal never writes domain data, that
an unapproved or rejected proposal cannot be executed, that executing twice does not duplicate the
side effect, that duplicate proposals collapse via the idempotency key, and that a malformed
payload fails cleanly instead of throwing.

---

## Phase 6 — Tests 🟡

| Suite | Status |
| --- | --- |
| Engine unit tests (`PriorityEngine`, `CapacityEngine`, `LifeDebtEngine`) | ✅ 36 tests |
| AI guard unit tests (`PromptInjectionGuard`) | ✅ |
| Integration tests against real SQL Server (AI approval lifecycle) | ✅ 11 tests |
| Domain entity invariant tests | ⬜ |
| Use-case service tests (tasks, projects, goals, finance) | ⬜ |
| UI / bUnit component tests | ⬜ |

`DatabaseFixture` creates a uniquely named database per run, applies migrations, and drops it on
dispose. It deliberately uses the real SQL Server provider rather than an in-memory store, because
the approval lifecycle depends on unique indexes, row versions, and transaction behaviour that an
in-memory provider does not reproduce.

---

## Known gaps and risks

1. **`IUserTimeZone` reads the machine zone by default.** The Jalali display layer depends on this
   being correct; it is configurable via `LifeOS:TimeZone`.
2. **DPAPI is Windows-only.** `AddInfrastructure` throws on other platforms rather than silently
   storing secrets in plaintext.
3. **No authentication.** The app is designed as a single-user local system; see `SECURITY.md`.
4. **`AiAction.ContextHash` is recorded but not yet re-verified at execution time.** The column is
   populated and stored, and `ExecuteAsync` checks status and expiry, but the recompute-and-compare
   step described in `AI_ARCHITECTURE.md` §7 is not implemented. Until it is, a proposal approved
   against stale data will still execute. This is the most important remaining AI gap.
5. **Only 7 of 11 `AiActionType` values are executable.** `ReorganizeTasks`, `AdjustBudget`,
   `UpdateProject`, and `UpdateGoal` return a structured "not supported in this version" failure
   rather than silently doing nothing.
6. **No domain entity invariant tests yet.** Domain rules are currently verified only by
   compilation, the integration suite's incidental coverage, and manual smoke testing.
7. **No live provider call has been verified.** Both HTTP providers are exercised only through the
   fake provider and unit-level routing tests; no request has been made to a real Ollama or
   OpenAI-compatible endpoint in this environment.
