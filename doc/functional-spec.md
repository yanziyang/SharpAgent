# SharpAgent Functional Specification

**Version:** 1.0
**Date:** 23 August 2026
**Status:** MVP baseline
**Primary audience:** Coding agents and delivery team
**Companion document:** [Human-readable specification](functional-spec.html)
**Related decision record:** [Feasibility report](SharpAgent-Feasibility-Report.html)

## 1. Purpose

SharpAgent is a self-hosted, single-user or tightly trusted internal AI coding-agent application. It helps a developer understand a repository, plan work, make controlled changes, run tests, and review the result.

Microsoft Agent Framework (MAF) is the agent runtime. SharpAgent supplies the product layer around it:

- React web application for task entry, live progress, approvals, diffs, and session history.
- .NET 10 backend that owns sessions, policies, workspace tools, provider credentials, and event streaming.
- Provider-neutral model adapter for OpenCode Go, DeepSeek, and OpenRouter.
- SQLite persistence through Entity Framework Core.
- Isolated workspace boundary for file and shell operations.

This specification defines the controlled MVP. It deliberately does not promise full parity with Pi Agent or OpenCode.

## 2. Product outcome

For a selected local repository, a developer can:

1. Create a task in Plan or Execute mode.
2. Watch the agent create and update a visible todo plan.
3. Review streamed progress, tool activity, and changes.
4. Approve or deny sensitive actions before they run.
5. Review a file diff, test results, final summary, and usage information.
6. Resume a prior session without losing task context or audit history.

## 3. Scope, assumptions, and release gates

### 3.1 In scope for the MVP

- Local or trusted internal web deployment with no authentication.
- One user interaction at a time per session.
- Registered local workspaces.
- Read/search file tools; controlled patch application; controlled shell/test execution.
- Model routing to OpenCode Go, DeepSeek, and OpenRouter.
- MAF Harness for planning, todos, session/context handling, streamed events, and tool orchestration.
- SQLite persistence for sessions, audit records, configuration, approvals, usage, and change metadata.
- Responsive browser experience for desktop and tablet.

### 3.2 Explicitly out of scope

- Login, account management, roles, teams, or multi-tenant isolation.
- Shared or public Internet-facing production deployment.
- IDE extensions, pull-request creation, direct Git hosting integration, or autonomous commits/pushes.
- Unbounded background agents, unattended long-running jobs, or unrestricted network access from tools.
- Enterprise knowledge connectors, MCP servers, collaboration, billing, or Azure deployment.
- A promise to support every model exposed by a provider.

### 3.3 Release gates

The MVP must not be used as a shared service or exposed to the Internet until authentication and authorization are added.

The MVP must not execute a file modification, side-effecting shell command, or external action without a policy decision and, where required, an explicit approval.

Provider keys must remain server-side. They must never be returned by API responses, written to browser storage, or stored as plaintext in SQLite.

## 4. Target stack

| Area | Required choice |
|---|---|
| Frontend | React, Vite, strict TypeScript, React Router |
| UI system | shadcn/ui |
| Styling | Tailwind CSS 4 |
| Backend | .NET 10, Microsoft Agent Framework, Entity Framework Core |
| Persistence | SQLite |
| Authentication | Not included in MVP |
| Model providers | OpenCode Go, DeepSeek, OpenRouter |
| Event transport | REST commands plus Server-Sent Events (SSE) per session |

The implementation may use a different internal transport only if it preserves the API and event behavior defined below.

## 5. Personas

| Persona | Description | MVP ability |
|---|---|---|
| Developer | Trusted user working on a repository. | Create, monitor, approve, cancel, resume, and review sessions. |
| Local operator | Person configuring the local/trusted deployment; this is not an authenticated product role. | Configure providers, approved models, workspace roots, tool policy, and budgets. |
| Agent runtime | MAF Harness plus SharpAgent adapters and tools. | Plan tasks, call approved tools, emit events, and stop at policy or budget limits. |

## 6. Core concepts

| Term | Meaning |
|---|---|
| Workspace | Registered repository root and allowed operational boundary. |
| Session | Persistent task context, messages, plan, state, audit log, and result. |
| Run | One execution attempt within a session. A session can have multiple runs after resume. |
| Plan mode | Agent may inspect and plan but must not modify files or run side-effecting commands. |
| Execute mode | Agent may request permitted workspace actions, subject to policy and approvals. |
| Tool action | Read/search, patch, shell, test, or other application-managed action proposed by the agent. |
| Approval | Developer decision granting or denying one specific requested action. |
| Change set | Files changed during a run, including before/after content or a patch representation. |
| Model profile | Configured and tested model/provider combination allowed for use. |

## 7. End-to-end behavior

### 7.1 Primary journey

1. Developer selects a registered workspace and permitted model profile.
2. Developer enters a task and chooses Plan or Execute mode.
3. SharpAgent creates a persistent session and opens its session page.
4. Backend starts a MAF-backed run and streams lifecycle events through SSE.
5. Agent creates or updates todos, gathers permitted repository context, and reports progress.
6. In Execute mode, agent requests tools through the SharpAgent policy layer.
7. If an action needs approval, run enters awaiting approval; UI shows exact action, affected scope, reason, and risk category.
8. After approval, SharpAgent executes the action inside the workspace boundary and streams output.
9. At completion, agent returns a concise summary, changed files, validation results, unresolved issues, and model usage.
10. Developer can review, continue, cancel, archive, or resume the session.

### 7.2 Session state model

    draft
      -> planning
      -> executing
      -> awaiting_approval
      -> reviewing
      -> completed

    planning / executing / awaiting_approval / reviewing
      -> cancelled
      -> failed
      -> interrupted

    completed / failed / cancelled / interrupted
      -> planning or executing (resume creates a new run)

State rules:

- Draft means session exists but no run started.
- Planning may inspect allowed context and create todos; it must not write files.
- Executing processes allowed tools and model requests.
- Awaiting approval blocks the requested action; only waiting status may be emitted.
- Reviewing prepares the final result.
- Completed, failed, and cancelled are terminal. Interrupted is recoverable where context is available.
- Resume creates a new AgentRun linked to the existing session; it must not overwrite earlier audit history.

## 8. Functional requirements

### 8.1 Workspace management

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-001 | Operator can register a workspace with display name, absolute root path, optional default model profile, and allowed path rules. | Must | Registered workspace appears in New Task selector; invalid or missing root cannot be saved. |
| FR-002 | Backend resolves and validates workspace root before every tool action. | Must | Tool request outside resolved root is denied and audited. |
| FR-003 | Product displays workspace state: available, unavailable, or validation failed. | Must | Missing or inaccessible workspace cannot start a run. |
| FR-004 | Developer can view read-only workspace metadata on a session. | Should | Session header identifies workspace and active policy profile. |

### 8.2 Session and task management

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-010 | Developer can create a session with workspace, task text, run mode, and model profile. | Must | Valid input creates a session and redirects to /sessions/:sessionId. |
| FR-011 | Task text is stored exactly as submitted and shown in history. | Must | Reloading session shows original task. |
| FR-012 | Developer can list recent sessions with state, workspace, model profile, updated time, and outcome. | Must | Dashboard is latest-update ordered and links to session. |
| FR-013 | Developer can cancel an active run. | Must | Cancellation is persisted, active tool is stopped where possible, and session becomes cancelled. |
| FR-014 | Developer can resume terminal or interrupted session with optional follow-up instruction. | Must | New run is created; earlier summary, todos, audit history, and changes remain visible. |
| FR-015 | Developer can archive a session without deleting audit data. | Should | Archived session is hidden by default and available in archive filter. |

### 8.3 Agent planning and execution

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-020 | Each multi-step run creates or updates a visible todo list. | Must | Session events include todo creation/update; UI shows active, pending, and completed items. |
| FR-021 | Plan mode never invokes patch, write, or side-effecting shell tools. | Must | Attempts are denied before execution and recorded as policy denials. |
| FR-022 | Execute mode begins with a plan before side-effecting work begins, except an explicitly approved simple follow-up. | Must | Audit has plan/todo event before first patch or side-effecting shell event. |
| FR-023 | Runtime compacts or summarizes earlier context when limits approach, preserving task, decisions, todos, approvals, changes, and key outputs. | Must | Context-compacted event is emitted and session remains resumable. |
| FR-024 | Developer sees concise activity summaries, not hidden chain-of-thought. | Must | Events expose intent, tool, scope, result, and error summaries only. |
| FR-025 | Runtime stops at configured time, tool-call, token, or cost limit. | Must | Session moves to failed or interrupted with a clear reason and resume guidance. |
| FR-026 | MAF Harness integration is isolated behind an application runtime interface. | Must | UI, persistence, and policy layers do not directly depend on MAF types. |

### 8.4 File, patch, and shell tools

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-030 | Agent can read file, list directory, search text, and inspect repository state only within workspace boundary. | Must | Each action validates resolved path and returns bounded output. |
| FR-031 | Agent can propose file changes as a named patch/change set. | Must | UI shows affected files and before/after or unified diff. |
| FR-032 | Applying a patch requires approval by default. | Must | No file change occurs before recorded approval unless a later policy profile expressly allows it. |
| FR-033 | Agent can run approved shell commands and tests only through workspace execution service. | Must | Browser has no shell endpoint; audit records command, working directory, exit code, duration, and truncated output. |
| FR-034 | Shell execution uses an isolated worktree or container profile. Container networking is disabled by default. | Must | Active execution profile is recorded per run; unsafe defaults cannot be silently selected. |
| FR-035 | Tool output is size-limited and redacted where configured. | Must | Excessive output is truncated with marker; unbounded raw output is not sent to model or browser. |
| FR-036 | System prevents traversal and denies symlinks resolving outside workspace. | Must | Tests for parent traversal, foreign absolute paths, and escaping symlinks are denied. |

### 8.5 Policy and approvals

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-040 | Every tool request is evaluated by SharpAgent policy before execution. | Must | Tool adapter cannot touch filesystem or shell without policy decision. |
| FR-041 | Policy outcomes are allow, require approval, or deny. | Must | Each tool audit event includes outcome and reason/rule. |
| FR-042 | Read-only in-boundary actions may be automatically allowed by default policy. | Must | Read/search can proceed without approval when configured. |
| FR-043 | Patches, writes, side-effecting shell commands, and external/network actions require approval by default. | Must | These actions create pending approval instead of executing. |
| FR-044 | Approval shows action type, command or patch summary, affected paths, workspace, reason, and impact. | Must | Developer can decide without inspecting server logs. |
| FR-045 | Approval is single-use, tied to one run and action fingerprint, and expires. | Must | Replaying old approval or changing command/patch invalidates it. |
| FR-046 | Developer can approve once, deny, or cancel run from approval prompt. | Must | Every choice emits an event and updates state. |
| FR-047 | Denied action returns bounded result to runtime for safe re-plan or conclusion. | Must | Agent does not retry the same denied action indefinitely. |

### 8.6 Provider and model behavior

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-050 | Backend supports configured adapters for OpenCode Go, DeepSeek, and OpenRouter. | Must | Operator can enable/disable each adapter without frontend code change. |
| FR-051 | Frontend uses provider-neutral model profile identifier and never handles provider credentials or raw external request shapes. | Must | Browser sends modelProfileId, never API key or endpoint URL. |
| FR-052 | Each profile declares tested capabilities: streaming, tool calling, context limit, input/output modalities, cost metadata, and availability. | Must | Execute mode only allows profiles validated for streaming and tools. |
| FR-053 | OpenCode Go adapter selects endpoint style required by selected model. | Must | Application receives canonical stream/tool contract independent of OpenCode Go endpoint shape. |
| FR-054 | DeepSeek and OpenRouter adapters normalize responses, errors, tool calls, token usage, and stream events. | Must | Provider-specific payloads do not reach React components or MAF-facing application code. |
| FR-055 | Provider secrets and endpoint configuration are server-side only. | Must | SQLite stores reference/non-sensitive metadata only; no API returns secrets. |
| FR-056 | Automatic fallback is off by default; enabled fallback uses explicit approved chain and emits provider-fallback event. | Should | Fallback never silently changes model mid-tool action. |

### 8.7 Results, review, and observability

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-060 | Final result includes plain-language summary, todos, changed files, validation commands/results, warnings, and next steps. | Must | Completed session renders structured Review panel. |
| FR-061 | UI provides diff-oriented change review with filenames, status, and content preview. | Must | Developer can navigate all changed files on session page. |
| FR-062 | System records chronological audit timeline for input, state, tool proposals, policy, approvals, tool results, provider events, and outcome. | Must | Timeline persists after refresh and resume. |
| FR-063 | System captures provider, model profile, tokens when supplied, estimated cost, latency, tool count, and context compaction count. | Must | Usage appears in run summary and simple dashboard. |
| FR-064 | Provider errors, tool failures, cancellations, and denials are distinguishable in UI and audit. | Must | Failed session states whether retry/resume is appropriate. |

### 8.8 Configuration and health

| ID | Requirement | Priority | Acceptance condition |
|---|---|---|---|
| FR-070 | Operator can set run duration, maximum tool calls, maximum context size, estimated cost, and approval expiry. | Must | New run uses saved policy profile; active values appear in session metadata. |
| FR-071 | Operator can configure small approved model catalog and default model profile. | Must | Disabled/untested profile cannot be selected for new task. |
| FR-072 | Service exposes health endpoint for application, SQLite, workspace executor, and enabled-provider readiness without secrets. | Must | Failed dependency is reported degraded/unready. |
| FR-073 | Dashboard shows sessions by state, completed runs, average duration, approvals, tool failures, and estimated cost. | Should | Values come from persisted run and audit data. |

## 9. Frontend behavior and routes

### 9.1 Required routes

| Route | Purpose |
|---|---|
| / | Dashboard: recent sessions, status summary, metrics, New Task entry point. |
| /sessions/new | Create session: workspace, task, mode, profile, optional tighter limits. |
| /sessions/:sessionId | Session workspace: activity, todos, approvals, changes, terminal output, final review, usage. |
| /sessions/:sessionId/changes | Focused change/diff review; may be route or session sub-view. |
| /sessions/archive | Archived sessions. |
| /settings/workspaces | Workspace registration. |
| /settings/models | Provider/model profile configuration and health. |
| /settings/policy | Tool policy and run-limit configuration. |
| /settings/runtime | Read-only service, health, and version information. |

There is intentionally no login route in the MVP.

### 9.2 Session page layout

The session page must be desktop-first and collapse to one column on tablet:

- Header: session state, workspace, model profile, mode, elapsed time, Stop/Resume action.
- Primary panel: task, streamed activity timeline, assistant summaries, pending approval cards.
- Supporting panel: todo plan, changed files, terminal/test output, usage, session metadata.
- Review panel: final summary, diff links, validation result, warnings, follow-up prompt.

Use shadcn/ui primitives for buttons, dialogs, sheets, tabs, cards, badges, dropdowns, alerts, tooltips, scroll areas, and accessible form controls.

### 9.3 User-visible event vocabulary

    session_created
    run_started
    status
    assistant_summary
    todo_created
    todo_updated
    context_compacted
    tool_proposed
    policy_decision
    approval_requested
    approval_resolved
    tool_started
    tool_output
    tool_completed
    change_detected
    provider_fallback
    usage_updated
    run_completed
    run_failed
    run_cancelled
    heartbeat

Unknown event type must render as a non-breaking informational timeline entry and be logged for diagnostics.

## 10. Backend API contract

All endpoints use JSON except SSE event stream. Date/time values use ISO-8601 UTC. IDs use opaque UUIDs or ULIDs. Frontend sends Idempotency-Key header for create, run, cancel, resume, and approval-resolution commands.

### 10.1 Command and query endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | /api/health | Service/dependency readiness. |
| GET | /api/dashboard | Summary metrics and recent sessions. |
| GET | /api/workspaces | Registered workspaces and availability. |
| POST | /api/workspaces | Create workspace registration. |
| PATCH | /api/workspaces/{workspaceId} | Update non-secret workspace settings. |
| GET | /api/model-profiles | Enabled model profiles and capabilities. |
| POST | /api/model-profiles/validate | Operator validation of a configured profile; never exposes secrets. |
| GET | /api/policy-profiles | Available policy/run-limit profiles. |
| GET | /api/sessions | Session list with filters and pagination. |
| POST | /api/sessions | Create draft session. |
| GET | /api/sessions/{sessionId} | Full session projection. |
| POST | /api/sessions/{sessionId}/runs | Start or resume a run. |
| POST | /api/sessions/{sessionId}/cancel | Request cancellation of active run. |
| GET | /api/sessions/{sessionId}/events | SSE stream of live and replayable session events. |
| GET | /api/sessions/{sessionId}/changes | Changed-file metadata and diff content. |
| POST | /api/approvals/{approvalId}/resolve | Approve once, deny, or cancel. |

### 10.2 Create-session request

    {
      "workspaceId": "ws_01H...",
      "modelProfileId": "model_deepseek_coder_primary",
      "mode": "plan",
      "task": "Identify why the pricing page test fails and propose a safe fix.",
      "policyProfileId": "default-controlled",
      "limits": {
        "maxEstimatedCostUsd": 2.00,
        "maxToolCalls": 40
      }
    }

Validation rules:

- workspaceId, modelProfileId, mode, and non-blank task are required.
- Mode is plan or execute.
- Profile not marked available and tested cannot be used.
- Per-request limits may only tighten selected policy profile; they cannot relax it.

### 10.3 Start/resume-run request

    {
      "instruction": "Also run the focused test after the change.",
      "resumeFromRunId": "run_01H..."
    }

Instruction is optional for first run and optional for resume. Backend rejects request if session has active run or pending approval.

### 10.4 Approval resolution request

    {
      "decision": "approve_once",
      "comment": "Run the focused test only."
    }

Allowed decisions are approve_once, deny, and cancel_run.

### 10.5 SSE requirements

The session event endpoint must:

- Set Content-Type to text/event-stream.
- Include monotonically ordered event IDs.
- Support replay after reconnect using Last-Event-ID.
- Emit heartbeat at least every 20 seconds while a run is active.
- Emit only server-safe data; never credentials, unrestricted environment values, or private model reasoning.
- Allow frontend recovery by fetching session projection when a replay gap is detected.

Illustrative approval event:

    {
      "eventId": "evt_01H...",
      "type": "approval_requested",
      "occurredAt": "2026-08-23T08:30:00Z",
      "runId": "run_01H...",
      "payload": {
        "approvalId": "apr_01H...",
        "actionType": "apply_patch",
        "summary": "Update src/Pricing.tsx and its focused test.",
        "affectedPaths": ["src/Pricing.tsx", "src/Pricing.test.tsx"],
        "reason": "The task requires a file modification.",
        "expiresAt": "2026-08-23T08:40:00Z"
      }
    }

## 11. Domain data model

Use Entity Framework Core migrations. SQLite is the MVP store; repository interfaces must remain portable for a later server database.

| Entity | Minimum fields |
|---|---|
| Workspace | Id, Name, RootPath, Status, AllowedPathsJson, DefaultModelProfileId, CreatedAt, UpdatedAt |
| Session | Id, WorkspaceId, Task, Mode, Status, ModelProfileId, PolicyProfileId, ActiveRunId, ArchivedAt, CreatedAt, UpdatedAt |
| AgentRun | Id, SessionId, Sequence, Status, StartedAt, EndedAt, StopReason, ContextSummary, FinalSummary |
| TodoItem | Id, SessionId, RunId, Sequence, Text, Status, UpdatedAt |
| ApprovalRequest | Id, RunId, ActionFingerprint, ActionType, Summary, AffectedPathsJson, CommandPreview, Status, ExpiresAt, ResolvedAt, Decision, Comment |
| ToolExecution | Id, RunId, ToolName, RequestSummary, PolicyOutcome, ApprovalId, Status, StartedAt, EndedAt, ExitCode, OutputPreview, ErrorSummary |
| ChangeSet | Id, RunId, Status, Summary, CreatedAt |
| FileChange | Id, ChangeSetId, RelativePath, ChangeType, BeforeHash, AfterHash, DiffText, IsBinary |
| ModelProfile | Id, Provider, DisplayName, ProviderModelId, EndpointKind, CapabilitiesJson, Enabled, ValidationStatus, ConfigReference |
| PolicyProfile | Id, Name, RulesJson, MaxRunDuration, MaxToolCalls, MaxEstimatedCost, ApprovalExpiry |
| AuditEvent | Id, SessionId, RunId, Sequence, Type, PayloadJson, OccurredAt |
| UsageRecord | Id, RunId, Provider, ModelProfileId, InputTokens, OutputTokens, EstimatedCost, LatencyMs, ContextCompactions |

Persistence rules:

- Audit events are append-only.
- Secrets are never persisted in ModelProfile, AuditEvent, or ToolExecution.
- Store large raw output only when explicitly configured; default is bounded preview plus hash/reference.
- Change data remains accessible after run ends.

## 12. Application service boundaries

Implementation must create application-facing interfaces with the following responsibilities:

| Boundary | Responsibility |
|---|---|
| IAgentRuntime | Starts/resumes/cancels MAF-backed run; manages plan, todos, context; emits canonical events. |
| IModelProviderAdapter | Converts canonical model request/stream/tool-call contract to one provider/model endpoint. |
| IModelProfileRegistry | Supplies only enabled, validated model profiles and capabilities. |
| IWorkspaceService | Resolves allowed roots and prepares isolated worktree/container. |
| IWorkspaceToolExecutor | Executes read/search/patch/shell/test tools only after policy authorization. |
| IPolicyEvaluator | Returns allow, require approval, or deny for proposed tool action. |
| IApprovalService | Creates, validates, resolves, and expires single-use approvals. |
| ISessionEventStore | Persists/replays ordered session events for SSE and history. |
| IUsageService | Captures model, tool, latency, and cost metrics. |

Experimental or prerelease framework integrations, especially coding-adjacent file/shell features, must be hidden behind these boundaries and protected by feature flags. SharpAgent retains its own policy layer even if a framework feature exposes similar controls.

## 13. Provider adapter contract

Canonical model request includes:

    modelProfile
    systemInstructions
    conversation/context messages
    tool definitions
    streaming=true
    maximum output/token controls
    correlation IDs

Canonical stream emits:

    text_delta
    assistant_summary
    tool_call_requested
    tool_call_completed
    usage
    provider_error
    completed

Adapter-specific requirements:

- OpenCode Go selects configured endpoint style per model profile and normalizes OpenAI Responses, Chat Completions, or Anthropic Messages semantics.
- DeepSeek uses configured compatible API endpoint and normalizes streaming/tool events.
- OpenRouter uses configured OpenAI-like endpoint, validates selected upstream model tool support, and normalizes routing/provider errors.

Profile validation runs bounded, non-destructive smoke test for connection, streaming, structured tool request handling, and usage/error normalization. It must not use a real repository or side-effecting tool.

## 14. Safety behavior

### 14.1 Default action policy

| Action category | Default decision |
|---|---|
| Read in-boundary text file | Allow |
| List/search within boundary | Allow |
| Read repository status | Allow |
| Write or patch file | Require approval |
| Run test or shell command | Require approval |
| Delete, move, install, publish, commit, or push | Deny in MVP unless later explicit policy enables it |
| Access network from tool | Deny by default |
| Access outside workspace | Deny |
| Access provider API from backend adapter | Allow only through configured adapter |

### 14.2 Approval presentation

Approval card contains:

- Clear action title and risk badge.
- Exact or safely truncated command/patch preview.
- Affected files and working directory.
- Reason from agent/tool layer.
- Expiry time.
- Approve once, Deny, and Cancel run actions.

### 14.3 Workspace safety

- Never execute browser-supplied file path or shell string directly.
- Resolve canonical paths before authorization.
- Do not rely on allow/deny command list as only sandbox boundary.
- Prefer disposable worktree or isolated container per run.
- For container execution, disable outbound networking by default and use non-root least-privilege settings.
- Enforce command duration and output limits.

## 15. Non-functional requirements

| Area | Requirement |
|---|---|
| Responsive UX | Support desktop/tablet. Critical session and approval actions usable at 768px viewport width. |
| Accessibility | Semantic HTML, keyboard controls, visible focus, sufficient contrast, labelled dialogs, non-color-only status. |
| Streaming | Show run-start or status promptly; target first visible status within 3 seconds when provider is responsive. |
| Reliability | Persist state-changing commands and audit events before reporting success to UI. |
| Recoverability | SSE reconnect does not lose ordered events; interrupted session is resumable where context exists. |
| Observability | Correlation ID in logs, runs, tool calls, provider calls, and SSE events. |
| Cost control | Enforce configured maximums; show estimated cost where provider data permits. |
| Privacy | Credentials stay server-side. Do not expose environment values, hidden prompts, or private model reasoning. |
| Maintainability | Strict TypeScript, linting, formatting, .NET analyzers, EF migrations, provider contract tests, policy tests, and end-to-end tests. |

## 16. MVP acceptance scenarios

### AC-01: Plan repository task without changing files

Given valid workspace and Plan mode, when developer asks SharpAgent to investigate failing test, then agent may read/search and create todos, no write/patch/side-effect runs, and plan/activity/final investigation summary persist after refresh.

### AC-02: Apply approved fix and run focused test

Given valid workspace and Execute mode, when agent proposes patch, session enters awaiting approval and developer sees affected files/patch summary. When developer approves once, only that patch runs. Later test command requests its own approval. Review shows diff, exit code, output preview, and final summary.

### AC-03: Deny risky action

Given active Execute run, when agent proposes command denied by policy or developer, command does not run, denial is audited, runtime revises plan or concludes safely, and it does not loop on same denied action.

### AC-04: Provider profile gating

Given enabled but unvalidated OpenRouter profile, it is unavailable for Execute mode. After successful operator smoke test, it becomes selectable according to declared capabilities.

### AC-05: Resume after interruption

Given session interrupted during run, reopening shows earlier todos, approvals, tool results, changes, and error. Resume creates new run from preserved context and new audit sequence.

### AC-06: Protect provider credentials

Given configured provider keys, no page, session, event stream, browser storage, or error detail contains key, secret, or raw server config.

### AC-07: Prevent workspace escape

Given registered workspace, a traversal path, foreign absolute path, or escaping symbolic link is denied before filesystem action and audited.

## 17. Delivery slices

### Slice 0 — Technical validation

- One registered workspace.
- One provider adapter smoke test per requested provider.
- MAF-backed Plan mode with todo and SSE events.
- Read/search-only workspace tools.
- SQLite session/audit persistence.
- Basic health endpoint and usage capture.

Exit criterion: selected profile reliably streams, produces plan, and completes read-only repository task.

### Slice 1 — Controlled MVP

- Full session dashboard and session UI.
- Execute mode with policy, approval cards, patch application, test execution, diffs, cancellation, and resume.
- Approved model catalog and model profile validation.
- Run limits, audit timeline, usage summary, and operator settings.

Exit criterion: trusted developer completes small repository change with visible approval, diff review, and validation evidence.

### Slice 2 — Pilot hardening

- Better workspace isolation, error recovery, provider fallback policy, dashboards, retention/configuration refinement, and pilot measurement.

Exit criterion: pilot evidence demonstrates useful, reliable, safely controlled completion of target tasks.

## 18. Constraints and decisions

| Topic | MVP decision |
|---|---|
| Authentication | Not implemented; deployment is local/trusted only. |
| Database | SQLite with EF Core; keep repository/migration design portable. |
| Workspace isolation | Isolated worktree or container; final mechanism selected in Slice 0. |
| Shell | Application-managed execution behind workspace tool executor; command policy alone is not sandbox. |
| MAF coding features | Use Harness where appropriate; hide evolving/prerelease integrations behind interfaces and feature flags. |
| Azure | Not required for MVP; hosting/monitoring export is optional later. |
| Automatic fallback | Disabled by default; enable only after validation and explicit routing rules. |

## 19. Definition of done

Controlled MVP is functionally complete when:

- All Must requirements and acceptance scenarios pass.
- Each requested provider has server-side adapter and validation path.
- Unvalidated or incapable model cannot execute tools.
- Plan mode cannot write or execute side-effecting commands.
- Execute mode requires explicit approval for all default-sensitive actions.
- Session state, approvals, tool records, diffs, and terminal results survive refresh and can resume.
- SQLite migrations, API contract tests, provider adapter tests, policy tests, and end-to-end acceptance tests run in CI.
- No-auth deployment limitation is visible in product and release documentation.
