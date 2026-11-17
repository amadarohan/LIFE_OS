# Life OS — Decisions (ADR log)

## ADR-001 — Target .NET 9 instead of .NET 10

**Status:** Accepted (forced by environment)
**Context:** The specification requires .NET 10 LTS. The machine has SDKs 2.1, 2.2, 5.0, 8.0.428,
9.0.311. No .NET 10 SDK is installed.
**Decision:** Target `net9.0` with EF Core 9. The user's instruction explicitly allows falling
back to the latest available version.
**Consequences:** All code is written against APIs that exist in both 9 and 10. Upgrading is a
`TargetFramework` change plus package bumps. No architectural impact.

## ADR-002 — No MediatR

**Status:** Accepted
**Context:** The spec allows MediatR "only if it genuinely improves use-case organization".
**Decision:** Use plain injected application services (`ProjectService`, `TaskService`, ...)
with DTO in / result out. Validation runs through a small `IValidator<T>` pipeline helper.
**Consequences:** Fewer moving parts, easier debugging, no reflection-based dispatch. If the
use-case count grows past ~80, a dispatcher can be introduced without changing call sites.

## ADR-003 — Single DbContext, explicit configurations

**Status:** Accepted
**Decision:** One `LifeOsDbContext` for the whole modular monolith, with one
`IEntityTypeConfiguration<T>` per entity and no reliance on conventions for keys, indexes,
delete behavior, precision, or concurrency.
**Consequences:** Simple transactions across modules; schema intent is explicit and reviewable.

## ADR-004 — `Guid.CreateVersion7()` identifiers

**Status:** Accepted
**Decision:** All primary keys are UUIDv7, generated in the domain.
**Consequences:** Time-ordered keys avoid the index fragmentation of random GUIDs, and IDs can
be created before hitting the database (needed for AI proposals and offline capture).

## ADR-005 — Jalali display only, Gregorian storage

**Status:** Accepted
**Decision:** Store `DateTimeOffset` (UTC) and `DateOnly`. Convert to Jalali exclusively in
`LifeOS.Web/Services/Formatting/JalaliDateConverter`.
**Consequences:** No ambiguity in storage, no locale-dependent bugs, one place to test.

## ADR-006 — AI providers over raw HTTP, no vendor SDKs

**Status:** Accepted
**Decision:** Implement Ollama and OpenAI-compatible providers with `HttpClient` + `System.Text.Json`.
**Consequences:** No SDK version churn, no vendor lock-in, providers are trivially fakeable in
tests. Cost: we implement request/response mapping ourselves, which is small and well-tested.

## ADR-007 — Execution tools are not model-callable

**Status:** Accepted
**Decision:** The model can call read/analysis/proposal tools. Execution tools are invoked only
by `AiActionExecutor` after an approved `AIAction`.
**Consequences:** The approval-first guarantee is structural, not prompt-dependent.

## ADR-008 — DPAPI for sensitive settings

**Status:** Accepted
**Decision:** Encrypt `AppSetting` values where `IsSensitive = true` using Windows DPAPI
(current-user scope) through `ISecretProtector`.
**Consequences:** No plaintext API keys on disk. Non-Windows falls back to a key file with a
startup warning; documented in `SECURITY.md`.

## ADR-009 — Integration tests against real SQL Server

**Status:** Accepted
**Context:** Docker availability is not guaranteed on this machine; SQL Server 2019 is installed.
**Decision:** Integration tests create a uniquely named database on `localhost`, run migrations,
execute, then drop it. Connection string comes from `LIFEOS_TEST_CONNECTION` or a default.
**Consequences:** Real constraint/index behavior is tested. Tests require a local SQL Server;
they are skipped with a clear message when it is unreachable.

## ADR-010 — Persian UI, English identifiers

**Status:** Accepted
**Decision:** All user-facing strings are Persian and live in components or a resource helper.
All code identifiers, table names, and log messages are English.
**Consequences:** No mixed-language leakage into the UI; code stays greppable.
