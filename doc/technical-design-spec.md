# SharpAgent Technical Design Specification

**Version:** 1.0  
**Date:** 23 August 2026  
**Status:** Implementation baseline  
**Primary audience:** Coding agents and delivery team  
**Companion document:** [Human-readable technical design](technical-design-spec.html)  
**Inputs:** [Functional specification](functional-spec.md), [approved HTML prototype](<../prototype(html)/index.html>), and [feasibility report](SharpAgent-Feasibility-Report.html)

## 1. Purpose

This document turns the functional specification and approved prototype into an implementation-ready design for the controlled MVP.

SharpAgent is a trusted-local, web-based AI coding-agent application. A React browser UI calls a .NET 10 backend that uses Microsoft Agent Framework (MAF), SQLite, provider-neutral model adapters, and a controlled workspace executor. The system supports planning, todos, safe repository actions, approvals, streaming, review, cancellation, archive, and resume.

Authentication, shared deployment, unrestricted network tools, autonomous Git operations, and multi-user collaboration are out of scope.

### 1.1 Design principles

1. **The application owns safety.** MAF orchestrates work; SharpAgent independently owns policy, approval validity, workspace boundaries, persistence, and tool execution.
2. **The browser is a client, never an executor.** It receives no provider credentials, unrestricted environment values, filesystem handle, or shell endpoint.
3. **Durable facts precede live events.** Persist state changes and audit events before publishing SSE notifications.
4. **MAF stays replaceable.** Controllers, entities, DTOs, policy services, and React state never expose MAF types.
5. **Provider details end at adapters.** Application code operates on validated model profiles and canonical events.
6. **Runs are bounded.** One active run per session, strict limits, cancellation, and deterministic terminal or recoverable state.
7. **Production does not depend on prototype state.** The prototype demonstrates behavior; server projections are the production source of truth.

### 1.2 Requirement traceability

| Design area | Functional specification | Prototype behavior |
|---|---|---|
| Workspace and session lifecycle | FR-001–015 | New session, archive, restore, resume controls |
| MAF planning and context | FR-020–026 | Plan/todos, compaction and recovery scenarios |
| Controlled tools | FR-030–036 | Patch preview, terminal approval, change review |
| Policy and approvals | FR-040–047 | Single-use approval metadata, deny and cancel |
| Provider profiles | FR-050–056 | OpenCode Go, DeepSeek, OpenRouter validation gate |
| Results and reports | FR-060–064, FR-073 | Activity, review, dashboard and statistics |
| Configuration and health | FR-070–072 | Limits, provider readiness, workspace/policy settings |

## 2. Solution architecture

~~~mermaid
flowchart LR
  UI[React web application] -->|REST commands and queries| API[SharpAgent.Api]
  UI <-->|SSE session events| API
  API --> APP[Application services]
  APP --> RT[IAgentRuntime]
  RT --> MAF[MAF Harness]
  RT --> ADAPT[Provider adapters]
  ADAPT --> OC[OpenCode Go]
  ADAPT --> DS[DeepSeek]
  ADAPT --> OR[OpenRouter]
  APP --> WS[Workspace executor]
  APP --> DB[(SQLite / EF Core)]
  WS --> DB
~~~

### 2.1 Deployment topology

| Element | MVP design |
|---|---|
| HTTP host | ASP.NET Core/Kestrel. Loopback by default; trusted private binding only when explicitly configured. |
| React delivery | Vite production build served by the .NET host or a same-origin trusted static host. |
| Database | One SQLite file on a persistent local volume, with WAL enabled. |
| Workspaces | Explicitly registered roots. Every active run uses an isolated worktree or container environment. |
| Secrets | Environment variables, OS secret store, or deployment injection. SQLite holds references and non-secret metadata only. |
| Background work | One in-process bounded run coordinator. No distributed worker or scale-out in MVP. |
| Observability | Structured local logs, metrics and traces. Azure export is an optional later adapter. |

The MVP MUST NOT be exposed to untrusted networks while authentication is absent.

### 2.2 Runtime sequence

~~~mermaid
sequenceDiagram
  participant U as Developer
  participant UI as React UI
  participant API as API/Application
  participant RT as MAF runtime adapter
  participant P as Policy/Approval
  participant W as Workspace executor
  participant DB as SQLite
  U->>UI: Start or resume session
  UI->>API: REST command plus Idempotency-Key
  API->>DB: Commit command and audit event
  API-->>UI: 202 Accepted
  API->>RT: Start bounded run
  RT-->>API: Plan, todos, summaries, tool proposals
  API->>DB: Persist ordered event
  API-->>UI: SSE event
  API->>P: Evaluate tool proposal
  alt Allowed read-only action
    P->>W: Execute bounded action
  else Approval required
    P->>DB: Persist fingerprinted approval
    API-->>UI: approval_requested
    U->>UI: Approve once, deny, or cancel
    UI->>API: Resolve approval
    API->>P: Revalidate expiry and fingerprint
    P->>W: Execute only when valid
  else Denied
    P-->>RT: Bounded denial result
  end
  W-->>API: Bounded result, diff, output, exit code
  API->>DB: Persist final facts and events
  API-->>UI: SSE update and session projection
~~~

## 3. Repository and dependency structure

### 3.1 Suggested repository layout

~~~text
/
├─ src/
│  ├─ SharpAgent.Api/                 # HTTP, SSE, dependency composition
│  ├─ SharpAgent.Application/         # Use cases, DTOs, interfaces, validation
│  ├─ SharpAgent.Domain/              # Entities, state transitions, value objects
│  ├─ SharpAgent.Infrastructure/      # EF Core, SQLite, event store, secret references
│  ├─ SharpAgent.Runtime.Maf/         # IAgentRuntime implementation and MAF translators
│  ├─ SharpAgent.Providers/           # OpenCode Go, DeepSeek, OpenRouter adapters
│  ├─ SharpAgent.Workspace/           # Paths, worktrees/containers, controlled tools
│  └─ SharpAgent.Contracts/           # Versioned API and SSE contracts
├─ web/
│  └─ src/
│     ├─ app/                          # Router, providers, application shell
│     ├─ features/sessions/
│     ├─ features/workspaces/
│     ├─ features/model-profiles/
│     ├─ features/policy/
│     ├─ features/reports/
│     └─ shared/
├─ tests/
│  ├─ SharpAgent.Domain.Tests/
│  ├─ SharpAgent.Application.Tests/
│  ├─ SharpAgent.Infrastructure.Tests/
│  ├─ SharpAgent.Runtime.Maf.Tests/
│  ├─ SharpAgent.Providers.ContractTests/
│  ├─ SharpAgent.Api.IntegrationTests/
│  └─ web-e2e/
└─ doc/
~~~

### 3.2 Dependency rule

~~~text
Api → Application → Domain
Api → Infrastructure / Runtime.Maf / Providers / Workspace
Infrastructure / Runtime.Maf / Providers / Workspace → Application + Domain
React UI → HTTP contracts only
~~~

<code>SharpAgent.Domain</code> MUST have no dependency on EF Core, ASP.NET Core, MAF, provider SDKs, filesystem APIs, or process APIs.

### 3.3 Version policy

Use central package management and pin versions in <code>Directory.Packages.props</code>. MAF coding-adjacent capabilities may evolve; the exact tested package versions and feature flags MUST be documented by Slice 0.

| Area | Required family |
|---|---|
| Agent runtime | Microsoft Agent Framework Harness and compatible Microsoft.Extensions.AI abstractions |
| Persistence | EF Core SQLite with migrations |
| Frontend | React, Vite, strict TypeScript, React Router, Tailwind CSS 4, generated shadcn/ui components |
| Quality | .NET analyzers, lint/format/type checks, unit/integration/contract/end-to-end tests |

## 4. Domain model and persistence

### 4.1 Aggregate boundaries

<code>Session</code> is the primary aggregate. It owns lifecycle, active-run reference, archive state, and ordered audit history. <code>AgentRun</code>, <code>TodoItem</code>, <code>ApprovalRequest</code>, <code>ChangeSet</code>, <code>ToolExecution</code>, and <code>UsageRecord</code> are run/session records.

<code>Workspace</code>, <code>ModelProfile</code>, and <code>PolicyProfile</code> are operator-managed configuration aggregates.

### 4.2 Entities and critical fields

| Entity | Required design |
|---|---|
| Workspace | Registered root plus canonical root identity captured at validation. Re-canonicalize immediately before every tool action. |
| Session | Add integer optimistic <code>Version</code>, nullable <code>ActiveRunId</code>/<code>ArchivedAt</code>, and <code>LastEventSequence</code>. |
| AgentRun | Sequence, correlation ID, execution environment ID, resume source run, cancellation timestamp, stop reason, compacted context, final summary. |
| TodoItem | Unique sequence per session/run; changes are represented in audit events. |
| ApprovalRequest | Immutable preview fields, action fingerprint, expiry, status, decision, one-use resolution time. |
| ToolExecution | Normalized request/result previews, policy outcome, approval ID, execution profile, exit code, bounded output and redaction/truncation metadata. |
| ChangeSet/FileChange | Run-worktree changes, hashes, textual unified diffs when size-permitted, binary metadata otherwise. |
| AuditEvent | Append-only canonical event envelope; unique session/sequence. |
| UsageRecord | Provider, model profile, tokens when reported, estimated cost, latency, tools, compactions. |
| IdempotencyRecord | New entity: unique operation/key, request hash, response reference, created/expiry timestamps. |
| RunLease | New entity or AgentRun fields used to prevent concurrent runs and detect abandoned work after restart. |

### 4.3 SQLite rules

- Enable WAL journaling, foreign keys and a bounded busy timeout.
- Keep write transactions short. Never hold a database transaction while waiting on a model or shell process.
- Commit EF Core migrations to source control.
- Use application validation plus a transaction/lease to enforce one active run per session.
- Index dashboard/session lists, events by session/sequence, approvals by run/status/expiry, and tool executions by run.
- The MVP is a one-service-process deployment. Multiple writers against the same database file are out of scope.

### 4.4 Event-first persistence

For every state-changing operation:

1. Validate the command and idempotency key.
2. Start a short transaction.
3. Update aggregate records.
4. Append canonical audit event(s) with the next sequence number.
5. Commit.
6. Publish the committed event(s) to in-process SSE subscribers.

Live SSE subscribers are an optimization. On reconnect, the server replays from <code>AuditEvent</code>, making refresh and restart deterministic.

### 4.5 MAF state persistence

Do **not** serialize MAF framework session objects as product records. Persist the application-owned recovery projection:

- original task and follow-up instructions;
- mode, policy profile and validated model profile;
- compacted context summary;
- todos;
- approvals and decisions;
- change/tool facts;
- latest assistant/final summary;
- ordered audit events.

Resume creates a new <code>AgentRun</code> and new MAF session. It rehydrates context from those persisted safe facts. Earlier run records remain immutable.

## 5. State, concurrency and idempotency

### 5.1 Session/run state model

| State | Meaning | Next state |
|---|---|---|
| draft | Session exists without an active run. | planning, executing, archived |
| planning | Read/search and todo planning only. | awaiting approval, reviewing, completed, failed, interrupted, cancelled |
| executing | Bounded tool and model activity. | awaiting approval, reviewing, completed, failed, interrupted, cancelled |
| awaiting approval | One exact proposal is blocked. | executing, reviewing, interrupted, cancelled, failed |
| reviewing | Final facts and outcome are being assembled. | completed, failed, interrupted |
| completed | Successful terminal run. | new run through resume |
| failed | Terminal failure. | new run through explicit resume |
| interrupted | Recoverable stop. | new run through resume |
| cancelled | Operator stop. | new run through resume |
| archived | Inactive visibility state; history remains. | restore to draft, then new run |

<code>Session.Status</code> is a projection. <code>AgentRun.Status</code> is the run-level authority.

### 5.2 One active run

- A session MUST have at most one planning, executing, awaiting-approval, or reviewing run.
- <code>POST /runs</code> acquires a session-level lease inside the create-run transaction; a concurrent request returns <code>409 session_active</code>.
- Cancellation is cooperative: persist the request, emit status, cancel the in-memory token and terminate the controlled process tree when applicable.
- The executor re-checks cancellation immediately before sensitive actions.
- On startup, a persisted active run without a live lease becomes <code>interrupted</code> and is resumable.

### 5.3 Idempotency

All state-changing routes require <code>Idempotency-Key</code>.

1. Hash method, canonical path and request body.
2. A repeated key with same hash returns the saved result.
3. The same key with different hash returns <code>409 idempotency_conflict</code>.
4. Retain keys for a local retention period, initially 24 hours.

## 6. MAF runtime adapter

<code>SharpAgent.Runtime.Maf</code> implements the application-facing <code>IAgentRuntime</code>.

~~~csharp
public interface IAgentRuntime
{
    Task<RunStartResult> StartAsync(StartRunCommand command, CancellationToken cancellationToken);
    Task<RunStartResult> ResumeAsync(ResumeRunCommand command, CancellationToken cancellationToken);
    Task RequestCancellationAsync(SessionId sessionId, CancellationToken cancellationToken);
}

public interface IPolicyEvaluator
{
    Task<PolicyDecision> EvaluateAsync(ToolProposal proposal, CancellationToken cancellationToken);
}

public interface IApprovalService
{
    Task<ApprovalRequestDto> CreateAsync(ApprovalCandidate candidate, CancellationToken cancellationToken);
    Task<ApprovalResolutionResult> ResolveAsync(ResolveApprovalCommand command, CancellationToken cancellationToken);
}

public interface IWorkspaceToolExecutor
{
    Task<ToolResult> ExecuteAsync(AuthorizedToolAction action, CancellationToken cancellationToken);
}
~~~

Current Microsoft guidance describes the Harness as an agent composition over an <code>IChatClient</code>, context providers, tools, middleware, plans/todos, streaming and optional compaction. The design uses a Harness agent only inside this adapter.

### 6.1 Adapter responsibilities

1. Load a validated model profile.
2. Obtain a provider-backed <code>IChatClient</code> from an adapter.
3. Create/configure the Harness with SharpAgent instructions, Plan/Execute mode, todo handling, bounded loops/tool calls, compaction, narrow tools, and event observers.
4. Translate framework output into canonical session events.
5. Persist each canonical event before SSE publication.
6. Stop on policy wait/denial, cancellation, limit, provider failure, or completion.

### 6.2 Tool registration rule

MAF tools MUST be narrow façade functions and MUST NOT touch the filesystem, shell, or provider configuration directly.

~~~text
MAF tool call
  → ToolProposalFactory
  → IPolicyEvaluator
  → IApprovalService when required
  → IWorkspaceToolExecutor only after authorization
  → bounded ToolResult back to MAF
~~~

MAF approval capabilities MAY improve experience but MUST NOT replace SharpAgent policy or approval persistence.

### 6.3 Canonical event translation

| Canonical event | Source |
|---|---|
| assistant_summary | Safe generated summary only |
| todo_created, todo_updated | Harness todo changes |
| context_compacted | Application-owned compaction result |
| tool_proposed | Normalized tool request before policy |
| policy_decision | SharpAgent policy result |
| approval_requested, approval_resolved | SharpAgent approval service |
| tool_started, tool_output, tool_completed | Workspace executor |
| change_detected | Change-set capture |
| usage_updated | Provider adapter |
| provider_fallback, run_failed | Runtime/provider error classification |

The translator MUST never emit hidden reasoning, hidden prompts, credentials, raw provider payloads, or unrestricted environment values.

### 6.4 Run coordinator

The API writes a run-start record/event and queues an in-process <code>RunWorkItem</code>. It returns <code>202 Accepted</code> rather than holding the request open during model work.

~~~text
HTTP command → transaction creates AgentRun + run_started
             → coordinator gets cancellation token
             → IAgentRuntime runs
             → events/final state persisted
~~~

## 7. Provider and model-profile design

### 7.1 Provider-neutral adapter

~~~csharp
public interface IModelProviderAdapter
{
    ProviderKind Provider { get; }
    Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        CancellationToken cancellationToken);
    IChatClient CreateChatClient(
        ModelProfile profile,
        ProviderSecretReference secretReference);
}
~~~

Provider-specific model IDs, endpoint kind, headers, retry logic, error payloads and secret resolution remain inside the adapter.

### 7.2 Capability registry

Each profile persists a non-secret capability document:

~~~json
{
  "streaming": true,
  "toolCalling": true,
  "contextWindowTokens": 64000,
  "supportsPlanMode": true,
  "supportsExecuteMode": true,
  "costMetadataAvailable": true,
  "availability": "validated"
}
~~~

| Rule | Implementation |
|---|---|
| Selector eligibility | Enabled and validated profiles only |
| Execute mode | Requires streaming and tool-calling capability |
| OpenRouter | Plan-only until non-destructive profile validation succeeds |
| Fallback | Disabled by default; later explicit chain emits provider_fallback before changing providers |

### 7.3 Provider adapters

| Adapter | Required behavior |
|---|---|
| OpenCode Go | Choose endpoint style configured for the model profile; normalize messages, tools, stream deltas, usage and errors. |
| DeepSeek | Use server-side compatible client; normalize stream frames, tool requests, rate/provider errors and usage. |
| OpenRouter | Validate selected upstream model capability; normalize router/provider errors and tool/stream behavior. |

### 7.4 Validation endpoint

<code>POST /api/model-profiles/validate</code>:

1. Resolves the server-side secret reference.
2. Sends a bounded prompt with no repository context.
3. Confirms connection and stream start.
4. Checks safe structured-tool normalization using a synthetic schema.
5. Checks usage/error normalization.
6. Persists non-secret capability/result metadata only.

It MUST NOT access a real workspace, shell, patch tool, or browser-provided secret.

## 8. Policy, approvals and workspace tools

### 8.1 Policy model

<code>ToolProposal</code> contains run ID, tool category, canonical workspace root, resolved relative paths, execution profile, normalized patch or executable-plus-arguments, reason, requested impact, and estimated resource usage.

| Category | Default decision |
|---|---|
| In-boundary read/list/search | Allow |
| Repository-status read | Allow |
| Patch/write | Require approval |
| Test/shell command | Require approval |
| Delete/move/install/publish/commit/push | Deny |
| Workspace-network access | Deny |
| Outside workspace | Deny |

Policy returns <code>allow</code>, <code>require_approval</code>, or <code>deny</code> with rule ID and safe reason. Every decision is an audit event.

### 8.2 Approval fingerprint

~~~text
SHA-256(canonical JSON of:
  runId, actionType, workspaceCanonicalId, resolvedPaths,
  patchContentHash OR executableAndArguments,
  executionEnvironmentId, policyProfileVersion)
~~~

The server creates and validates the fingerprint. The UI receives readable preview, affected paths, expiry, fingerprint prefix, action ID and single-use scope only.

Before execution the service recalculates fingerprint and policy. Changed proposal, expired approval, cancelled run, invalid workspace, or consumed approval means the action MUST NOT run.

### 8.3 Workspace boundary

<code>IWorkspaceService</code> MUST:

1. Validate registered roots.
2. Resolve physical/canonical path just before each action.
3. Resolve symlinks and deny targets outside the canonical root.
4. Reject foreign absolute paths, traversal, device paths and unsupported link types.
5. Pass relative paths, not physical roots, to model/browser projections.

### 8.4 Execution environment

| Preferred MVP | Alternative |
|---|---|
| Disposable Git worktree per run; registered base checkout remains a read-only baseline. | Least-privilege container per run, non-root, network disabled, workspace mount only. |

The recommended default is a disposable worktree. Agent patches are applied inside that worktree and retained as diff evidence. Direct autonomous application to the original base checkout is not an MVP action.

### 8.5 Shell and test execution

The workspace executor MUST:

- use a vetted executable plus argument list, never a browser-provided shell string;
- fix working directory to the run environment;
- use <code>UseShellExecute = false</code>;
- enforce process-tree cancellation, timeout, output size, environment allowlist and redaction;
- record executable, safe arguments, directory, profile, exit code, duration and truncated result;
- expose a small focused-command catalog in MVP, not a general-purpose terminal.

### 8.6 Patch execution

1. Agent creates a named patch/change set.
2. Application validates all paths and generates deterministic preview/fingerprint.
3. Policy requires approval.
4. A valid approval allows atomic worktree application.
5. Application captures before/after hashes and unified diffs.
6. Partial/failing patch returns a bounded tool result; no silent retry.

## 9. HTTP and SSE contract

### 9.1 API conventions

- Base path is <code>/api</code>.
- JSON uses camelCase. Times are ISO-8601 UTC. IDs are opaque ULIDs or UUIDs.
- State-changing commands require <code>Idempotency-Key</code>.
- Errors use RFC 7807 ProblemDetails plus stable <code>errorCode</code>.
- DTOs are immutable records; never serialize EF entities.

### 9.2 Command endpoints

| Method | Path | Success | Key errors |
|---|---|---|---|
| POST | /api/sessions | 201 Created | workspace_unavailable, model_profile_unavailable, limits_invalid |
| POST | /api/sessions/{id}/runs | 202 Accepted | session_active, approval_pending, session_archived |
| POST | /api/sessions/{id}/cancel | 202 Accepted | run_not_active |
| POST | /api/approvals/{id}/resolve | 200 OK | approval_expired, approval_not_pending, fingerprint_mismatch |
| POST | /api/workspaces | 201 Created | workspace_invalid, workspace_duplicate |
| PATCH | /api/workspaces/{id} | 200 OK | workspace_in_use |
| POST | /api/model-profiles/validate | 202 Accepted or 200 OK | profile_disabled, validation_failed |

### 9.3 Query endpoints

| Method | Path | Projection |
|---|---|---|
| GET | /api/health | Application, SQLite, executor and provider readiness without secrets |
| GET | /api/dashboard | Recent sessions and aggregate persisted measures |
| GET | /api/workspaces | Workspace metadata/status |
| GET | /api/model-profiles | Non-secret profile capabilities |
| GET | /api/policy-profiles | Rule summaries and limits |
| GET | /api/sessions | Filtered/paged summaries |
| GET | /api/sessions/{id} | Full session projection |
| GET | /api/sessions/{id}/changes | Change metadata and bounded diff |
| GET | /api/sessions/{id}/events | SSE replay/live stream |

### 9.4 Session projection

~~~json
{
  "id": "ses_01H...",
  "workspace": { "id": "ws_01H...", "name": "storefront / apps/web", "status": "available" },
  "task": "Identify why the pricing test is flaky.",
  "mode": "execute",
  "status": "awaiting_approval",
  "modelProfile": { "id": "model_deepseek_coder_primary", "displayName": "DeepSeek Coder" },
  "activeRun": {
    "id": "run_01H...",
    "status": "awaiting_approval",
    "limits": { "maxToolCalls": 40, "maxEstimatedCostUsd": 2.0 },
    "usage": { "toolCalls": 3, "contextCompactions": 0 }
  },
  "todos": [],
  "pendingApproval": null,
  "latestResult": null
}
~~~

### 9.5 SSE requirements

~~~text
id: 42
event: approval_requested
data: {"eventId":"evt_01H...","sequence":42,"sessionId":"ses_01H...","runId":"run_01H...","type":"approval_requested","occurredAt":"2026-08-23T08:30:00Z","payload":{...}}
~~~

- Event ID equals the durable monotonically increasing session sequence.
- Support <code>Last-Event-ID</code>.
- Emit <code>heartbeat</code> every 20 seconds for an active stream.
- On sequence gap or parse error, the UI refetches session projection and reconnects after the last verified event.
- Unknown event type renders an informational timeline item and is logged without breaking the page.
- <code>EventSource</code> is adequate for one-way MVP live events; commands remain REST.

## 10. React frontend design

### 10.1 Component structure

~~~text
web/src/
├─ app/
│  ├─ router.tsx
│  ├─ AppShell.tsx
│  └─ theme-provider.tsx
├─ features/
│  ├─ sessions/
│  │  ├─ routes/
│  │  ├─ components/
│  │  ├─ api.ts
│  │  ├─ use-session-events.ts
│  │  └─ session-types.ts
│  ├─ workspaces/
│  ├─ model-profiles/
│  ├─ policy/
│  └─ reports/
└─ shared/
   ├─ api-client.ts
   ├─ components/
   └─ lib/
~~~

### 10.2 Routes

| Route | Component | Primary data |
|---|---|---|
| / | DashboardPage | Dashboard projection |
| /sessions/new | NewSessionPage or modal route | Workspaces, profiles, policy limits |
| /sessions/:sessionId | SessionWorkspacePage | Session projection plus SSE |
| /sessions/:sessionId/changes | SessionChangesPage | Change/diff projection |
| /sessions/archive | ArchivePage | Archived session list |
| /settings/workspaces | WorkspaceSettingsPage | Workspace configuration |
| /settings/models | ModelSettingsPage | Profiles, providers and validation |
| /settings/policy | PolicySettingsPage | Policy profiles and limits |
| /settings/runtime | RuntimeHealthPage | Health projection |

No login route exists in MVP.

### 10.3 Session page composition

| Region | Production components |
|---|---|
| Header | Title, workspace, mode, profile, state, activity, run controls, details toggle |
| Main timeline | Task, safe summaries, tool events, compaction, todos, approvals, outcome cards |
| Composer | Follow-up input and Plan/Execute control constrained by capabilities |
| Details | Plan, files, usage, workspace/execution safety metadata |
| Review tabs | Activity, changes, terminal, final review |
| Run controls | Resume with follow-up, cancel, archive, recovery explanation |

Use shadcn AlertDialog for cancel/archive confirmation, Dialog for session/provider/limit controls, Tabs for review, Sheet for responsive details/navigation, ScrollArea for timeline/terminal, and Tooltip for compact controls.

### 10.4 Data/event state rules

- Route loader gets initial server projection.
- <code>useSessionEvents(sessionId, lastSequence)</code> owns one EventSource while route is active.
- A typed reducer applies canonical events to session feature state.
- Snapshot refetch resolves reconnect gaps, unknown events and server reset.
- Command buttons show pending state but do not optimistically change approval/terminal status.
- Browser storage holds only theme and non-sensitive layout preferences.
- Session data, approvals, provider configuration, tokens and secrets never enter browser storage.

### 10.5 Prototype-to-production interactions

| Prototype behavior | Production behavior |
|---|---|
| Approve once | REST resolve command; server events determine final card state |
| Deny | Server persists denial and returns bounded runtime feedback |
| Cancel run | Alert dialog then cancellation command; status remains pending until server event |
| Archive | Confirmed archive command; hidden from active list only after server confirmation |
| Resume with follow-up | Start a new run linked to prior run/session context |
| Context compaction | Real safe <code>context_compacted</code> event; never reasoning text |
| Provider/interruption state | Recovery action shown only where server says resumption is safe |
| Limits | Display active run limits from server; policy configuration applies to later runs unless explicitly designed otherwise |
| Profile validation | Server result gates selection; UI does not decide capability |

### 10.6 Responsive and accessibility requirements

- Three-region desktop layout collapses details/navigation to sheets on tablet.
- Approval, cancellation and resume are usable at 768px viewport width.
- Themes are studio, midnight, ocean and forest via CSS variables on <code>data-theme</code>.
- Theme changes persist locally only.
- Dialogs have title/description, focus management, labelled destructive actions and safe Escape behavior.
- Status includes text and icon/badge; color is never the only signal.

## 11. Observability, health and errors

### 11.1 Required correlation

Every request, run, tool, provider call and event carries:

~~~text
correlationId
sessionId
runId
eventSequence
toolExecutionId where applicable
provider and modelProfileId where applicable
~~~

Logs are structured and sanitized. Do not log raw credentials, environment values, provider request bodies, full prompts or unlimited output.

### 11.2 Metrics

Track sessions by state, run duration, time to first status, approval outcomes, tool failures, provider failures/fallbacks, interruptions/resumes/compactions, provider/model usage, tokens, estimated cost and workspace/policy denials.

### 11.3 Health response

~~~json
{
  "status": "ready",
  "checks": [
    { "name": "application", "status": "healthy" },
    { "name": "sqlite", "status": "healthy" },
    { "name": "workspaceExecutor", "status": "ready" },
    { "name": "provider:deepseek", "status": "ready" }
  ],
  "version": "0.1.0"
}
~~~

Health data MUST NOT reveal root paths, provider endpoints, secrets, token values or detailed failures.

### 11.4 Error treatment

| Class | UI behavior | Resume |
|---|---|---|
| Input validation | Inline form feedback | Correct/resubmit |
| Policy denial | Safe outcome and timeline | New narrower instruction |
| Approval expiry/conflict | Expired approval state | New proposal |
| Retryable provider error | Recovery card/timeline | Resume when server permits |
| Tool failure | Bounded terminal/review result | New run from result |
| Limit reached | Paused/interrupted explanation | New run with valid limits/instruction |
| Service/SSE interruption | Reconnect and projection refetch | Resume if run is interrupted |

## 12. Security and safety controls

| Area | Required control |
|---|---|
| Deployment | Local/trusted only until authentication exists |
| Secrets | Server-side resolver; SQLite references/non-secret metadata only |
| Browser | No direct filesystem, shell, provider endpoint or credential API |
| Workspace | Canonical physical-path authorization and symlink escape prevention |
| Commands | Structured executable/arguments, restricted catalog, timeout/output/process-tree control |
| Patches | Path validation, deterministic fingerprint, server revalidation, one-time approval |
| Network | Workspace executor blocks outbound network by default; only adapters call providers |
| Audit | Append-only history includes denials, expiries and cancellations |

Suggested single-page-app headers:

~~~text
Content-Security-Policy: default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
~~~

Tune CSP for final static asset strategy. Do not weaken it to let a browser call providers directly.

## 13. Testing and quality gates

### 13.1 Test matrix

| Layer | Required tests |
|---|---|
| Domain | States, approval transitions, fingerprints, event sequencing, archive/resume |
| Application | Validation, idempotency, policy, profile gating, projections |
| Infrastructure | Migrations, SQLite transactions, event replay, redaction |
| Workspace | Traversal/symlink escape, patch atomicity, timeout/output bounds |
| Provider contract | Streaming, tools, usage, retryable/non-retryable error normalization per profile |
| MAF adapter | Todo/event/compaction translation, cancellation, no framework-type leakage |
| API | REST errors, idempotency, SSE replay/heartbeat, health privacy |
| Frontend | Event reducer, forms, capability gating, accessibility |
| End-to-end | AC-01–AC-07, cancel/archive/resume, responsive approvals |

### 13.2 Mandatory proof

| Acceptance scenario | Automated evidence |
|---|---|
| AC-01 Plan-only | Guarded executor proves no write/shell call |
| AC-02 Controlled patch/test | Two distinct approvals each cause one bounded action |
| AC-03 Denial | Executor receives no command; no duplicate proposal loop |
| AC-04 Profile gating | Unvalidated profile cannot Execute; validation enables declared capabilities |
| AC-05 Resume | New run ID with retained prior audit/todos/changes |
| AC-06 Secrets | DTO/log/event snapshots contain no configured secret |
| AC-07 Workspace escape | Traversal, foreign absolute path and escaping symlink fail before tool execution |

### 13.3 CI

~~~text
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
npm ci
npm run lint
npm run typecheck
npm run test
npm run build
Playwright critical-path tests
EF migration verification against a fresh SQLite database
~~~

## 14. Delivery plan and decisions

### Slice 0 — technical validation

- Select and prove the isolated workspace mechanism.
- Implement all requested provider adapters and validation smoke tests.
- Run Harness-backed Plan mode with todos and SSE.
- Persist SQLite session/audit projection and read/search tools.
- Expose basic health and usage.

**Exit:** a validated profile plans a real repository task and survives refresh without side effects.

### Slice 1 — controlled MVP

- Build all React routes and responsive session workspace.
- Add Execute mode, policy, approvals, patch changes and focused tests.
- Add canonical event replay, cancellation, archive/resume, diff/review, usage, dashboard and settings.
- Pass all acceptance scenarios.

**Exit:** a trusted developer completes a small change inside an isolated worktree with approval, diff and validation evidence.

### Slice 2 — pilot hardening

- Refine worktree/container isolation, degraded-provider recovery, metrics/retention and optional fallback policy.
- Write deployment and backup guidance.

**Exit:** pilot evidence supports a decision on broader deployment, authentication and server database needs.

### Open decisions

| Decision | Default/owner | Resolve by |
|---|---|---|
| Exact MAF packages/flags | Architecture owner after Slice 0 proof | Before Slice 1 |
| Worktree versus container | Prefer disposable Git worktree; security review confirms | Slice 0 |
| Focused command catalog | Delivery team and pilot developer | Slice 1 start |
| Secret store | Local operator | Deployment setup |
| SQLite backup/retention | Local operator | Before pilot |
| Frontend fetch/cache helper | Team choice, preserving typed projection/SSE reducer pattern | Frontend foundation |

## 15. References

- [Functional specification](functional-spec.md)
- [Approved HTML prototype](<../prototype(html)/index.html>)
- [Feasibility report](SharpAgent-Feasibility-Report.html)
- [Microsoft Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/)
- [Microsoft Agent Framework Harness](https://learn.microsoft.com/en-us/agent-framework/concepts/harness)
- [Microsoft Agent Framework Harness getting started](https://learn.microsoft.com/en-us/agent-framework/get-started/harness)
