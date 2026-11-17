# Life OS — AI Architecture

## 1. Principles

1. **Approval-first.** AI reads, analyzes, and proposes. It never mutates persistent data.
2. **Provider-agnostic.** Business logic depends on `IAiProvider`, never on a vendor SDK.
3. **Privacy-aware.** Sensitive context never leaves the machine without explicit consent.
4. **Evidence-based.** Every insight carries source references and a confidence value.
5. **Deterministic core.** Orchestration, routing, validation, and approval are plain C# and
   fully unit-testable. The LLM is a text/JSON generator at the edge.

## 2. Contracts

```csharp
public interface IAiProvider
{
    string ProviderId { get; }
    AiProviderKind Kind { get; }              // Local | Cloud | Fake
    Task<AiProviderResponse> GenerateAsync(AiProviderRequest request, CancellationToken ct);
}

public sealed record AiProviderRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<AiChatMessage> Messages,
    double Temperature,
    int MaxOutputTokens,
    bool ExpectJson);

public sealed record AiProviderResponse(
    string Content,
    string ProviderId,
    string Model,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Duration);
```

`AiProviderRequest` carries **no** database handles, no connection strings, no entity objects —
only rendered text. This is the hard boundary.

## 3. Providers

| Provider | Kind | Notes |
|---|---|---|
| `OllamaAiProvider` | Local | `POST {endpoint}/api/chat`, `stream: false`. Default `http://localhost:11434`. |
| `OpenAiCompatibleProvider` | Cloud | `POST {endpoint}/chat/completions`. Works with OpenAI, OpenRouter, Groq, LM Studio, vLLM. |
| `FakeAiProvider` | Fake | Deterministic scripted responses for tests and offline demos. Never registered in production. |

Provider-specific HTTP lives in `Infrastructure/AI/Providers`. No SDK packages are required,
which keeps the dependency surface small and the providers swappable.

## 4. Provider router

`AiProviderRouter` resolves a provider for a request using, in order:

1. Explicit user override for the request.
2. Sensitivity gate — if the payload is `Sensitive` and the cloud provider is not authorized
   for sensitive data, the cloud provider is removed from the candidate set.
3. Task category preference (`SimpleClassification`, `Extraction`, `Summarization`,
   `SensitiveAnalysis`, `StrategicAnalysis`, `ScenarioEvaluation`).
4. Availability — a provider whose health check failed within the last 60 s is skipped.
5. Fallback — only to a provider that passes the sensitivity gate.

If no candidate survives, the router returns `AiRoutingFailure` with a human-readable reason.
It never silently downgrades privacy.

Default policy (configurable in Settings):

| Category | Preferred |
|---|---|
| SimpleClassification, Extraction, Summarization | Local |
| SensitiveAnalysis (finance, journal, health) | Local |
| StrategicAnalysis, ScenarioEvaluation | Cloud if authorized, else Local |

## 5. Context engine

`AiContextBuilder` assembles context from **Application queries**, never from `DbContext`.

Each context item is a `ContextItem`:

```csharp
public sealed record ContextItem(
    string Key,            // "activeProjects"
    string Label,          // "پروژه‌های فعال"
    string RenderedText,   // compact, structured text
    SensitivityLevel Level,
    IReadOnlyList<ContextSource> Sources);   // entity type + id + display name
```

Rules:

- Budgeted: the builder enforces a character budget per section and a global budget.
- Minimal: only sections relevant to the request category are included.
- Traceable: every item keeps its source entity references, which are persisted on the
  `AIMessage` so the UI can render "منابع" links.
- Tagged: `Public` / `Personal` / `Sensitive`. The aggregate level drives routing.
- Injection-safe: user-authored text (notes, journal, task titles) is wrapped in
  `<user_data>` blocks and the system prompt states that content inside those blocks is data,
  never instructions. `PromptInjectionGuard` additionally strips/neutralizes known override
  patterns and flags the message.

## 6. Tools

`IAiTool` with typed input/output:

```csharp
public interface IAiTool
{
    string Name { get; }
    AiToolCategory Category { get; }   // Read | Analysis | Proposal | Execution
    string Description { get; }
    JsonSchema InputSchema { get; }
    Task<AiToolResult> ExecuteAsync(JsonElement input, AiToolContext context, CancellationToken ct);
}
```

- **Read / Analysis** tools execute immediately and return data to the model.
- **Proposal** tools return a structured `AiActionProposal` — they do not write.
- **Execution** tools are *not* callable by the model. They are invoked by
  `AiActionExecutor` after approval, and they call Application use cases.

Tool input is validated with FluentValidation before execution. Unknown tool names, malformed
JSON, and out-of-range values produce a structured error the model can react to — never an
exception that reaches the user as a stack trace.

## 7. Approval workflow

```
Proposed ──approve──► Approved ──execute──► Executed
   │                     │                    │
   ├──reject──► Rejected  └──fail──► Failed   └── (terminal)
   ├──expire──► Expired
   └──cancel──► Cancelled
```

Guarantees:

- `AIAction.IdempotencyKey` is unique. Re-submitting the same proposal returns the existing row.
- Execution re-validates the payload against current data. If the underlying entity changed
  since proposal time (`ContextHash` mismatch), execution fails with `StaleProposal`.
- Expired proposals (`ExpiresAtUtc < now`) cannot be approved or executed.
- Approval and execution timestamps are recorded separately.
- Every execution writes an `AIExecutionLog` row with input, output, status, and error.
- The UI never shows "done" until `Status == Executed` is read back from the database.

## 8. Memory

| Type | Content | Lifetime |
|---|---|---|
| `Profile` | stable preferences, constraints, working style | long, user-editable |
| `Episodic` | past decisions, completed projects, lessons | long, append-mostly |
| `Operational` | current focus, active constraints | short, `ValidUntilUtc` |

Rules: source reference required, confidence required, `IsValidated` false by default for
AI-derived memories, deactivation instead of deletion, duplicate detection on
`(MemoryType, normalized content hash)`. Operational memory is refreshed from live queries
rather than trusted blindly.

## 9. Roles

`LifeAnalyst`, `StrategicAdvisor`, `ExecutionAgent` are **prompt profiles** selected by the
orchestrator, not autonomous agents. Each profile has its own system prompt, allowed tool set,
and default provider category. This keeps the first implementation deterministic and testable.

## 10. Failure handling

- Per-request timeout (`AI:RequestTimeoutSeconds`, default 120).
- Cancellation propagates from the UI.
- Provider errors are mapped to `AiFailure` with `Kind` (`Timeout`, `Unavailable`,
  `InvalidResponse`, `Refused`, `RateLimited`) and a Persian user-facing message.
- Retries only for idempotent read/analysis calls, max 2, exponential backoff.
- Execution is never retried automatically; it is idempotency-guarded instead.
- If all providers are unavailable, the app remains fully usable; the AI panel shows a clear
  offline state and the rest of Life OS is unaffected.

## 11. Audit

`AIExecutionLog` + `AuditLog` + `AIMessage` together provide: what was asked, what context was
sent (source references, not raw secrets), which provider/model answered, what was proposed,
who approved, what executed, and what the result was. API keys are never logged.
