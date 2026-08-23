# SharpAgent Implementation Plan V0.1

**Status:** Delivery plan for implementation agents

**Date:** 23 August 2026

**Primary implementation target:** Responsive web application for trusted local/internal use
**Required quality gate:** More than 90% unit-test coverage and more than 90% Playwright end-to-end coverage

## 1. Purpose

This plan turns the approved SharpAgent product material into an implementable, testable delivery sequence for coding agents working with OpenCode and DeepSeek. It is deliberately detailed enough to support incremental implementation, review, and handoff without asking an agent to rediscover product scope.

SharpAgent is a browser-based coding-agent application. Its interaction style may take inspiration from Codex Desktop, Claude Desktop, and OpenCode Desktop, but the delivered product is a React web application. Do not build Electron window chrome, desktop-only navigation, or a login screen for this MVP.

The objective is a controlled coding-agent MVP in which a trusted developer can select a registered workspace, create a Plan or Execute session, observe a live plan and activity, approve high-impact actions, review a change set and test evidence, and resume prior work safely.

## 2. Authoritative inputs and reading order

Read the following in order before changing code. Use progressive disclosure: read the next linked section only when work reaches that area.

| Priority | Source | How it is used |
|---|---|---|
| 1 | [Functional Specification](functional-spec.md) | Product scope, required behavior, functional requirements, acceptance conditions, release gates. |
| 2 | [Technical Design Specification](technical-design-spec.md) | Application boundaries, persistence, API and SSE contracts, safety architecture, component design, and test obligations. |
| 3 | [HTML prototype](<../prototype(html)/index.html>) | Visual hierarchy, responsive interaction intent, labels, themes, and demonstration flows. |
| 4 | [Prototype walkthrough](<../prototype(html)/README.md>) | Review path and prototype states that must become real product states. |
| 5 | [AGENTS.md](<../AGENTS.md>) | Repository-level engineering, safety, and progressive-disclosure rules. |

If the prototype conflicts with a specification, the Functional Specification wins for product behavior and the Technical Design Specification wins for architecture. The prototype is not authorization to add a feature outside the specifications.

Important interpretation rules:

- The MVP is trusted local/internal only and has no authentication, accounts, roles, teams, or public Internet deployment.
- React, Vite, strict TypeScript, React Router, shadcn/ui, and Tailwind CSS 4 are required on the frontend.
- .NET 10, Microsoft Agent Framework, Entity Framework Core, and SQLite are required on the backend.
- OpenCode Go, DeepSeek, and OpenRouter are provider adapters. The browser must never call a provider directly.
- Microsoft Agent Framework is an implementation detail behind application-owned interfaces. It does not replace SharpAgent policy, approvals, workspace isolation, audit records, or canonical SSE events.
- The desktop references guide a calm conversation-first layout only. The implementation remains keyboard-accessible and responsive in a normal browser at desktop and tablet widths.

## 3. Delivery principles and non-negotiable safety rules

1. Build from the inside out: domain and safety boundaries first, then provider/runtime behavior, then API and UI.
2. Treat every tool request as untrusted until the SharpAgent policy layer authorizes it.
3. Persist canonical events before publishing them through SSE. The UI must be able to refresh, replay, and reconstruct its state.
4. Keep model/provider payloads, framework types, credentials, raw shell output, hidden reasoning, and machine-specific paths out of React projections.
5. Plan mode is read-and-plan only. It must never modify a file or run a side-effecting command.
6. Execute mode must produce a visible plan before the first high-impact action, unless the approved simple-follow-up exception is explicitly implemented and tested.
7. Default to disposable Git worktrees for run execution. Never silently apply an agent patch directly to the registered base checkout.
8. Make no broad terminal endpoint. The application exposes a narrow, approved command catalog through a server-side executor.
9. Feature flags must guard prerelease or experimental Microsoft Agent Framework integration points.
10. Do not add authentication merely because the prototype has a demo entry state; it is outside the MVP.

## 4. Local OpenCode Go Plan testing policy

The repository contains a local file named <code>LLM-Key.md</code>. It is already Git-ignored and must remain untracked. This plan intentionally does not reproduce, inspect, print, parse into source control, or expose its contents.

### 4.1 Secret handling requirements

- Never add <code>LLM-Key.md</code>, its contents, a copied key, provider authorization headers, or provider URLs containing credentials to Git, fixtures, snapshots, issue text, logs, browser storage, SQLite, artifacts, or test reports.
- Keep the application test contract environment-based: live tests consume <code>SHARPAGENT_OPENCODE_GO_API_KEY</code> only at process start.
- A developer may set that environment variable locally from the ignored file using a local-only procedure. The production application and committed test code must not need to read <code>LLM-Key.md</code>.
- Add an ignore-regression test or repository check that verifies <code>LLM-Key.md</code> remains ignored and untracked without reading the file content.
- Mask all secret-shaped values in HTTP diagnostics. Test failures may name a profile or model display name, but may not print request headers, raw provider payloads, or environment values.
- Live provider tests are forbidden in CI by default. They run only when both <code>RUN_LIVE_PROVIDER_TESTS=1</code> and <code>SHARPAGENT_OPENCODE_GO_API_KEY</code> are present on an explicitly authorized local machine.

### 4.2 Strict OpenCode Go Plan model allowlist

For the OpenCode Go Plan live validation and smoke tests, the only allowed model display names are:

1. **Ox Alpha Free**
2. **Muse Spark 1.2 Contributor**
3. **MiMo-V2.5**

Implementation requirements:

- Store the provider model identifier separately from the user-facing display name. Obtain the real provider identifier from an authorized local profile/configuration process; do not invent an identifier in code.
- Add a server-side allowlist keyed by the exact approved display names above. A request for any other OpenCode Go Plan model must be rejected before an outbound call.
- The UI selector exposes only enabled, validated model profiles; it must not contain a free-text provider model field in normal user flows.
- Live smoke tests run one parameterized, non-destructive validation against each of the three allowlisted profiles. They must not enumerate or try any other OpenCode Go Plan model.
- DeepSeek and OpenRouter adapters receive deterministic contract tests and fake-server integration tests in this delivery. Do not use their real provider credentials or call their services unless separately authorized.

### 4.3 Safe live-provider test contract

The locally opt-in test for each allowed OpenCode Go Plan model must:

1. Load the API key from the process environment only.
2. Build a short canonical request with a harmless planning prompt and no repository content.
3. Use Plan-only capability validation with no registered workspace.
4. Verify stream start, bounded content/status normalization, completion, usage/error normalization, and secret redaction.
5. Supply no file, patch, shell, test, network, or external-action tool that could cause a side effect.
6. Use a short timeout, no automatic retry, a bounded output size, and a cost/token ceiling defined by the local profile.
7. Emit a minimal pass/fail report containing profile display name, capability result, latency bucket, and sanitized failure category only.

An unavailable provider, quota exhaustion, or model-side error is a valid reported validation failure. It must not cause the test to fall back to another model, mutate configuration, or leak diagnostic secrets.

## 5. Target repository layout

Use a single repository and solution with clear application boundaries. Names may vary slightly only when they retain the same dependency direction.

~~~text
/
  AGENTS.md
  doc/
    functional-spec.md
    technical-design-spec.md
    ImplementationPlan V0.1.md
  prototype(html)/
  src/
    SharpAgent.sln
    backend/
      SharpAgent.Domain/
      SharpAgent.Application/
      SharpAgent.Infrastructure/
      SharpAgent.Runtime.Maf/
      SharpAgent.Api/
    frontend/
      sharpagent-web/
  tests/
    SharpAgent.Domain.Tests/
    SharpAgent.Application.Tests/
    SharpAgent.Infrastructure.Tests/
    SharpAgent.Api.IntegrationTests/
    SharpAgent.Provider.ContractTests/
    SharpAgent.LiveProviderTests/
    web-unit/
    web-e2e/
  test-assets/
    workspaces/
    provider-fixtures/
  scripts/
    verify-quality.ps1
    run-live-opencode-smoke.ps1
~~~

Dependency direction:

~~~text
React browser
  -> Typed REST and SSE client
  -> SharpAgent.Api
  -> SharpAgent.Application
  -> SharpAgent.Domain

SharpAgent.Infrastructure and SharpAgent.Runtime.Maf
  -> implement Application interfaces
  -> never become dependencies of Domain or React
~~~

Keep provider transports, EF Core, file system access, Git worktree management, process execution, Microsoft Agent Framework, and secret resolution at the infrastructure edge. DTOs, reducer events, policy proposals, and application commands must remain provider-neutral.

## 6. Implementation workstreams

The delivery sequence below is intentionally ordered. Complete a phase and its evidence before expanding the next phase. Parallel work is acceptable only when it preserves the listed dependencies and does not duplicate safety controls.

| Workstream | Primary outcome | Depends on |
|---|---|---|
| Foundation | Buildable solution, strict frontend baseline, quality tooling | None |
| Domain and persistence | Portable domain model, SQLite migrations, append-only audit data | Foundation |
| Safety and workspace | Root validation, worktree preparation, policy, approvals, bounded executor | Domain and persistence |
| Providers and profiles | Canonical streaming/provider adapters, validation, capability gating | Domain and persistence |
| MAF runtime | Harness-backed planning, todos, compaction, canonical events | Providers and safety |
| API and SSE | Idempotent commands, projections, replayable stream | Runtime and persistence |
| Web application | Conversation-first session workspace, settings, dashboard, responsive themes | API contract |
| Reporting and hardening | Statistics, health, observability, accessibility, recovery | All prior work |
| Acceptance | Automated proof of every mandatory requirement and coverage gates | All prior work |

## 7. Phase 0 — foundation and engineering controls

### 7.1 Implement

- Create the .NET 10 solution and projects listed in Section 5.
- Create the Vite React application with strict TypeScript enabled. Do not weaken compiler options to accelerate scaffolding.
- Initialize React Router, Tailwind CSS 4, shadcn/ui primitives, accessible icon library, and a small application token layer for four themes: Studio, Midnight, Ocean, and Forest.
- Add formatting, linting, type checking, build scripts, test runners, coverage reporters, and a single local quality command.
- Add local configuration templates that use placeholders only. Commit examples such as <code>appsettings.Development.example.json</code> and <code>.env.example</code>; never commit working credentials.
- Define a safe fake provider and a deterministic temporary workspace fixture for all automated tests.
- Add a repository secret scan that checks tracked content and generated artifacts. The scan must report paths only, never matching secret values.

### 7.2 Verification

- .NET solution restores, builds, formats, and tests with no local provider key.
- React application lints, type-checks, builds, and runs its initial unit test.
- A development API health route and frontend health-state component work against a fake dependency.
- The tracked-file secret check proves that <code>LLM-Key.md</code> is ignored and not staged without opening the file.

### 7.3 Exit criteria

- A clean clone can run deterministic unit, integration, and browser tests with no external credentials.
- One documented local command runs the full offline quality suite.
- The baseline test coverage configuration fails a build below the thresholds in Section 15.

## 8. Phase 1 — domain model, persistence, and state invariants

### 8.1 Implement

- Model Workspace, Session, AgentRun, TodoItem, ApprovalRequest, ToolExecution, ChangeSet, FileChange, ModelProfile, PolicyProfile, AuditEvent, and UsageRecord as defined in the Functional Specification.
- Add EF Core SQLite mappings, migrations, indexes, concurrency/version handling where needed, and repository/application service interfaces.
- Implement the session and run state machines. Reject illegal transitions at the application boundary.
- Make audit events append-only with monotonic per-session sequence values.
- Persist a new run on resume instead of overwriting the prior run.
- Implement idempotency records for session creation, run start/resume, cancellation, and approval resolution. Retain keys locally for the documented initial retention period.
- Build redacted projection DTOs. Never expose raw EF entities or secret-bearing infrastructure objects.

### 8.2 Required tests

- Unit-test every allowed and rejected session/run transition, including cancellation during activity, approval wait, success, failure, interruption, archive, and resume.
- Unit-test idempotency for each command: same key/replay returns the original result; same key/different payload is rejected.
- Integration-test fresh SQLite migration, database creation, persistence reload, ordered audit replay, and resume retaining earlier todos/audit/change metadata.
- Test that ModelProfile, ToolExecution, AuditEvent, and API projections cannot serialize an injected secret.

### 8.3 Exit criteria

- The API can create and reload a draft session from fresh SQLite.
- State transitions are deterministic and protected by tests.
- A resumed session has a different run identifier and retained prior history.

## 9. Phase 2 — workspace isolation, policy, approvals, and controlled tools

### 9.1 Implement

- Add workspace registration, root existence/access validation, allowed-path configuration, availability state, and safe metadata projection.
- Implement canonical path resolution for every proposed tool action. Reject parent traversal, foreign absolute paths, missing roots, and symlinks escaping the registered root.
- Implement disposable Git worktree preparation per run. Record execution profile and worktree metadata as server-safe run evidence.
- Implement a small read/search/list tool set with output budgets, redaction, and no hidden recursive behavior.
- Implement a named patch/change-set proposal and a bounded patch application service.
- Implement a focused command catalog for test execution. Pass executable plus arguments through <code>ProcessStartInfo</code> with <code>UseShellExecute</code> disabled; never send a browser-provided shell string to a shell.
- Enforce process-tree cancellation, timeout, output limit, working-directory fixation, environment allowlist, and redaction.
- Implement <code>IPolicyEvaluator</code> with allow, require approval, and deny outcomes. The policy decision must happen before the tool executor is reachable.
- Implement single-use, expiring approval requests tied to run ID and action fingerprint. Recalculate fingerprint and policy immediately before execution.
- Record policy, approval, tool start, bounded output, completion, and error events in the audit stream.

### 9.2 Required tests

- Table-driven tests for all policy rule categories and default outcomes.
- Tests proving Plan-mode patch/write/side-effecting shell proposals cannot reach filesystem or process executors.
- Path traversal, foreign-root absolute path, and escaping-symlink tests proving no executor call occurred.
- Worktree lifecycle tests proving the registered base checkout is not the patch target.
- Approval tests for approve once, deny, cancel run, expiry, changed fingerprint, consumed approval, changed workspace, and duplicate approval resolution.
- Process tests for timeout, cancellation, oversized output, nonzero exit code, command not in catalog, and redacted environment/output.
- Change-set tests for diff metadata, before/after hash, binary handling, and failed/partial patch result.

### 9.3 Exit criteria

- A fake runtime can read a valid in-boundary fixture file and produce a safe audit event.
- A fake patch and focused test each require their own approval and execute once only after approval.
- All workspace escape cases are denied before a file, shell, or Git operation.

## 10. Phase 3 — provider adapters, model profiles, and validation

### 10.1 Implement

- Define canonical messages, tool definitions, stream fragments, usage records, provider errors, and <code>IModelProviderAdapter</code>.
- Implement adapter registration for OpenCode Go, DeepSeek, and OpenRouter without provider-specific types leaking into Application, API DTOs, or React.
- Implement <code>IModelProfileRegistry</code> with enabled status, validation status, capability document, display name, provider model identifier, endpoint kind, and non-secret configuration reference.
- Add a model-profile validation command that runs a bounded, non-destructive stream/tool-schema check and persists only safe capability metadata.
- Enforce selector and runtime gating: Execute requires validated streaming and tool-calling capabilities; OpenRouter is Plan-only until its non-destructive validation declares the necessary capabilities.
- Keep automatic fallback disabled. If a later explicit fallback chain is enabled, emit a canonical provider-fallback event before changing profile.
- Add the OpenCode Go Plan allowlist in Section 4.2 at the provider boundary and in profile validation.

### 10.2 Offline tests

- Contract-test request translation, stream normalization, tool-call normalization, usage normalization, rate/provider error mapping, cancellation, and redaction for all three adapters with recorded sanitized fixtures.
- Fake HTTP-server tests for fragmented streams, malformed event frame, unavailable provider, timeout, invalid profile, tool-call response, and bounded error payload.
- Tests that browser-facing responses carry only <code>modelProfileId</code>, display name, capabilities, and safe state; they must never include a credential, raw endpoint configuration, or provider request body.
- Tests that a disabled or unvalidated profile cannot start a session and cannot switch to Execute mode.
- Tests that an OpenCode Go Plan profile whose display name is not in the three-model allowlist is rejected before outbound transport.

### 10.3 Local opt-in live tests

Create the <code>SharpAgent.LiveProviderTests</code> project and tag all live tests clearly. It must skip with an explicit skip reason unless both local opt-in environment variables described in Section 4.1 are present.

Parameterize exactly three tests by the approved display names:

| Display name | Required evidence |
|---|---|
| Ox Alpha Free | Safe Plan-only stream validation, normalized completion or sanitized failure. |
| Muse Spark 1.2 Contributor | Safe Plan-only stream validation, normalized completion or sanitized failure. |
| MiMo-V2.5 | Safe Plan-only stream validation, normalized completion or sanitized failure. |

The test runner must reject any extra parameter row. Store only redacted result metadata in a local ignored report. Never upload that report with a secret or raw provider body.

### 10.4 Exit criteria

- OpenCode Go, DeepSeek, and OpenRouter pass deterministic adapter contracts.
- A valid profile can become selectable only after safe validation.
- On an explicitly authorized local machine, each approved OpenCode Go Plan model has a recorded, non-destructive smoke result.

## 11. Phase 4 — Microsoft Agent Framework runtime integration

### 11.1 Implement

- Create <code>IAgentRuntime</code> in Application and implement it in <code>SharpAgent.Runtime.Maf</code>.
- Keep MAF Harness construction, <code>IChatClient</code>, middleware, coding-oriented plan/todo features, context providers, tool registration, streaming callbacks, and compaction inside this adapter.
- Configure each run with SharpAgent instructions, current mode, bounded tool calls, duration/token/cost limits, visible todo behavior, compaction strategy, cancellation propagation, and event observers.
- Register only narrow facade tools that create canonical proposals. No MAF tool may directly call file APIs, Git, shell, provider configuration, or approval storage.
- Convert framework and provider output to canonical session events. Persist the event first, then publish it to SSE consumers.
- Emit concise activity summaries only. Do not surface hidden chain-of-thought, private prompts, provider payloads, secrets, or unrestricted environment values.
- Implement compaction preserving task, decisions, todos, approvals, change summary, key tool results, and latest run state. Emit <code>context_compacted</code> with a safe summary.
- Ensure runtime stops predictably on policy wait/deny, configured limits, cancellation, provider errors, malformed streams, or completion.
- Put unstable MAF features behind configuration flags and keep a fake runtime for deterministic tests.

### 11.2 Required tests

- Fake-runtime tests for plan creation/update, safe assistant summaries, todo ordering, completion, cancellation, provider interruption, configured limits, and compaction.
- Adapter tests proving a Plan-mode run cannot invoke patch/write/side-effect shell facades even if the model proposes them.
- Tests proving every Execute-mode high-impact action follows a visible plan/todo event.
- Event translator tests proving provider/MAF types do not escape and unknown data becomes a safe informational event.
- Resume tests proving the runtime receives a compacted historical summary plus required retained state without duplicate history.

### 11.3 Exit criteria

- A validated fake profile plans a fixture repository task, emits todos and an ordered event history, survives refresh/replay, and makes no side effects in Plan mode.
- The MAF-facing implementation can be replaced by a fake <code>IAgentRuntime</code> without changing policy, persistence, API, or UI code.

## 12. Phase 5 — API, projections, and replayable SSE

### 12.1 Implement

Implement the Functional Specification command/query surface under <code>/api</code>:

| Area | Required endpoints |
|---|---|
| Health and dashboard | GET health and dashboard projections |
| Workspaces | GET, POST, PATCH registration/availability |
| Profiles and policy | GET profiles/policies and POST profile validation |
| Sessions | GET list, POST create, GET projection, POST run/resume, POST cancel |
| Events and review | GET SSE events, GET changes/diff |
| Approvals | POST resolve with approve once, deny, or cancel run |

Additional API requirements:

- Use immutable request/response DTOs and UTC ISO-8601 dates.
- Require an <code>Idempotency-Key</code> header for every mutating command.
- Return <code>202 Accepted</code> for queued or ongoing model work rather than holding an HTTP request open.
- Make query projections safe for browser consumption and independent of EF entities.
- Implement stable SSE event IDs, Last-Event-ID replay, heartbeat at least every 20 seconds during active runs, ordered publication, reconnect support, and safe payload schemas.
- On a replay gap or event parse problem, make the client refetch the session projection and reconnect from its verified event sequence.

### 12.2 Required tests

- API integration tests for validation errors, idempotency, unsupported state, profile gating, approval resolution, cancellation, archive filter, resume, diff retrieval, and safe health degradation.
- SSE integration tests for initial replay, live delivery, ordered IDs, Last-Event-ID reconnect, heartbeat, replay gap recovery indicator, unknown event type, client disconnect, and no secret-bearing payloads.
- Contract snapshots for safe public DTOs. Snapshot sanitizers must remove identifiers/timestamps but never inspect or preserve a credential.
- Test every 4xx/409 error path with a stable problem code that the UI can map to actionable copy.

### 12.3 Exit criteria

- The frontend can be developed against a complete fake/in-memory API contract and later run unchanged against API integration tests.
- A session refresh or temporary SSE interruption does not lose the visible plan, approval, audit history, or review data.

## 13. Phase 6 — React web application and principal user flows

### 13.1 Build the browser shell

Implement the UI as a responsive React Router application, not as a desktop wrapper.

- Desktop layout: left session/navigation rail, central conversation/activity canvas, optional right details/review panel.
- Tablet layout: collapse navigation and details into shadcn Sheet components; retain one clear primary action and readable approval cards.
- Use desktop-inspired density and conversation hierarchy from the prototype without copying product branding or OS window controls.
- Persist only visual preferences such as selected theme and non-sensitive layout state in browser storage. Never persist sessions, approvals, provider configuration, secret data, token values, or SSE payload history there.
- Implement four user-selectable themes: Studio, Midnight, Ocean, Forest. Use semantic design tokens so every component changes consistently and meets contrast requirements.

### 13.2 Build required routes

| Route | Production responsibilities |
|---|---|
| <code>/</code> | Dashboard with recent sessions, summary metrics, current status, New task entry point. |
| <code>/sessions/new</code> | Workspace, task, Plan/Execute mode, validated profile, policy/limit selection, and clear eligibility guidance. |
| <code>/sessions/:sessionId</code> | Header, timeline, todos, approval card, composer/follow-up, run controls, changes, terminal, usage, and final review. |
| <code>/sessions/:sessionId/changes</code> | Focused diff/change-set review with changed-file navigation. |
| <code>/sessions/archive</code> | Filtered archived session list and restore/resume path. |
| <code>/settings/workspaces</code> | Workspace registration, allowed paths, availability/validation state. |
| <code>/settings/models</code> | Provider/model profile health, capability display, enablement, validation action, and selector gating. |
| <code>/settings/policy</code> | Tool policies, duration, tool-call, context, cost, and approval-expiry limits. |
| <code>/settings/runtime</code> | Read-only health, readiness, version, and sanitized dependency state. |
| <code>/settings/appearance</code> | The four persisted themes and accessibility-friendly density/preferences. |

### 13.3 Implement session experience

- Use a route loader/query to obtain the initial session projection, then reduce SSE events into local state.
- Render task input, concise assistant status, safe tool summaries, todo list, context-compaction notices, approval cards, tool output previews, errors, change detection, usage updates, and final result as chronological timeline cards.
- Use a visible state badge plus text; color is never the sole status signal.
- Show an approval card with action type, summary, exact bounded command or patch summary, affected relative paths, workspace, reason, impact, expiry, and three decisions: Approve once, Deny, Cancel run.
- Use an optimistic command only where retry/idempotency semantics are fully implemented. Disable duplicate submission while a command is in flight.
- Display changes in diff-oriented review with file status, file list, preview, validation result, warnings, and next steps.
- Include Cancel, Archive, and Resume controls with confirmation dialogs and clear state-specific guidance.
- Render unknown SSE event types as harmless informational timeline entries while telemetry records them.

### 13.4 Implement administration and reports

- Follow the OpenCode-inspired settings information architecture from the prototype only as a visual reference: focused category rail, clear pages, compact controls, status rows, and dialogs.
- Implement policy and limits, workspace registration, providers/model profiles, runtime health, and appearance as real API-driven forms/pages.
- Do not offer a generic server terminal, secret editor, provider request inspector, or manual raw-model payload input.
- Build dashboard and statistics views from persisted run/audit/usage data: sessions by state, completed runs, average duration, approvals, tool failures, provider failures, estimated cost, compaction count, and relevant filters.
- Ensure all charts or visual summaries have a readable text/table equivalent.

### 13.5 Required frontend unit tests

- Router guards and profile capability gating.
- API client DTO parsing, problem-code mapping, request idempotency headers, and no-secret projection types.
- SSE reducer ordering, duplicate event suppression, reconnect/replay handling, gap/refetch behavior, unknown event handling, and sanitized error display.
- Session state derived selectors, approval UX states, todo rendering, change review, cancel/archive/resume controls, and error/recovery cards.
- Form validation for every required New session, workspace, profile, and policy field.
- Theme persistence/restoration, token application, keyboard focus behavior, and responsive Sheet state.

### 13.6 Exit criteria

- The full primary journey works with deterministic fake API events before live runtime integration.
- At 768 CSS pixels and desktop widths, the application is usable without horizontal content loss, mouse-only controls, or inaccessible dialogs.
- Every production screen derives from typed server projections rather than prototype-only local demo state.

## 14. Phase 7 — observability, health, resilience, and pilot hardening

### 14.1 Implement

- Structured sanitized logs with correlation IDs spanning request, session, run, tool action, approval, provider call, and audit event.
- Metrics for session state counts, run duration, time to first status, approval outcomes, tool failures, provider failures/fallbacks, interruption/resume/compaction count, model usage, tokens, estimated cost, and policy/workspace denials.
- Health projection for application, SQLite, workspace executor, and enabled provider readiness. Do not reveal roots, endpoints, token values, raw errors, or secrets.
- Error mapping for policy denial, expired approval, workspace unavailable, provider error, configured limit, patch/test failure, cancellation, and SSE interruption.
- Bounded retention/cleanup policy for transient worktrees, output evidence, idempotency keys, and local telemetry. Preserve required audit/change evidence.
- Content Security Policy and browser headers appropriate for a local web application. Do not weaken CSP to permit browser-to-provider calls.

### 14.2 Required tests

- Correlation propagation across API command, run, tool, event, and projection.
- Sanitized log/event/health tests with injected secret-like values.
- Degraded dependency health, provider error, timeout, cancellation, and reconnect recovery tests.
- Worktree cleanup tests that protect audit/change evidence and never delete a registered base workspace.
- Accessibility tests for keyboard navigation, dialog focus trapping/restoration, labels, status announcements, contrast, and reduced-motion preferences.

### 14.3 Exit criteria

- An operator can understand a failed or degraded state, decide whether it can be resumed, and investigate with correlation IDs without receiving sensitive data.
- Error and recovery behavior matches the prototype walkthrough and Functional Specification.

## 15. Test strategy and mandatory coverage gates

The project must enforce two different kinds of coverage. Passing one does not substitute for the other.

### 15.1 Unit and component code coverage

Run .NET tests with Coverlet-compatible collection and React unit/component tests with Vitest V8 or Istanbul-compatible collection. Measure production code only; exclude generated files, migrations after migration verification, test projects, static prototype files, design artifacts, and configuration examples using a documented exclusion list.

Set the thresholds to **91% or higher** for each applicable metric, which is strictly more than the requested 90%:

| Code area | Statements/lines | Branches | Functions/methods |
|---|---:|---:|---:|
| .NET Domain and Application | 91% | 91% | 91% |
| .NET Infrastructure, Runtime, and API | 91% | 91% | 91% |
| React product source | 91% | 91% | 91% |
| Combined production source | 91% | 91% | 91% |

Do not solve a coverage failure with broad exclusions, untested trivial wrappers, or generated artificial tests. Any exclusion needs a short justification in the coverage configuration and review.

### 15.2 Playwright browser coverage

Use Playwright for both behavioral traceability and instrumented browser source coverage:

1. Run the required test suite against the full API plus deterministic fake runtime/provider infrastructure.
2. Run an instrumented frontend build in Chromium and collect browser source coverage for production React code exercised by the suite.
3. Require **91% or higher** lines, branches, and functions for the instrumented frontend product source in this Playwright run.
4. Maintain a requirement-to-specification scenario matrix with **at least 92% weighted scenario coverage** and **100% coverage of all Must requirements and AC-01 through AC-07**.
5. Run the critical suite in Chromium, Firefox, and WebKit. The coverage threshold may be calculated in Chromium, but all critical workflows must pass in all three engines.

The browser code-coverage gate verifies code execution. The traceability matrix verifies user behavior. Both are required.

### 15.3 Required Playwright suite

| ID | Scenario | Primary proof |
|---|---|---|
| E2E-01 | Dashboard to New session to Plan run | Valid workspace/profile selection creates a session and opens the session route. |
| E2E-02 | Plan-only safety | Timeline/todos stream; guarded executor records zero patch/write/side-effect shell calls. |
| E2E-03 | Controlled patch then focused test | Patch request requires approval; focused test requires a distinct approval; each approved action runs once. |
| E2E-04 | Denied action | Deny creates audit/event evidence, executor is not invoked, and runtime gives a safe conclusion or re-plan. |
| E2E-05 | Cancel active run | Stop control cancels run, persists state, and disables incompatible controls. |
| E2E-06 | Archive and resume | Archived session is filtered by default; resume creates a new run with previous history intact. |
| E2E-07 | Profile gating | Disabled/unvalidated profile cannot start; validated capabilities enable only permitted mode. |
| E2E-08 | OpenRouter constraint | OpenRouter remains Plan-only until validation; UI explains why Execute is unavailable. |
| E2E-09 | SSE reconnect/replay | Event loss/reconnect refetches projection and resumes in order without duplicate timeline cards. |
| E2E-10 | Unknown event | Unknown event is shown as information and does not crash or block later events. |
| E2E-11 | Limit and compaction | Context/limit notices provide clear recovery and resume guidance. |
| E2E-12 | Provider interruption | Sanitized provider failure is distinguishable from tool failure and supports appropriate resume path. |
| E2E-13 | Changes and final review | Changed file navigation, diff preview, test evidence, warnings, usage, and next steps render from persisted data. |
| E2E-14 | Workspace administration | Invalid/missing workspace cannot be saved or started; availability appears in selector. |
| E2E-15 | Policy and approval expiry | Changed action, expired approval, and duplicate decision cannot execute an action. |
| E2E-16 | Dashboard/statistics | Persisted metrics and filters agree with seeded session/run/audit data. |
| E2E-17 | Four themes | Studio, Midnight, Ocean, and Forest persist through reload and remain legible. |
| E2E-18 | Responsive/tablet accessibility | At 768 CSS pixels, navigation/details use sheets, approval remains actionable, and keyboard focus is correct. |
| E2E-19 | No-secret browser boundary | Network responses, DOM, storage, screenshot text, and error states contain no injected secret marker. |
| E2E-20 | No-login MVP | App starts on dashboard/session experience; no authentication dependency or login blocking route exists. |

### 15.4 Acceptance-condition proof map

| Acceptance condition | Automated evidence |
|---|---|
| AC-01 Plan-only | E2E-02 plus guarded executor integration test proves zero write/shell side effects. |
| AC-02 Controlled patch/test | E2E-03 plus approval fingerprint and executor integration tests. |
| AC-03 Denial | E2E-04 plus policy/agent-loop unit tests. |
| AC-04 Profile gating | E2E-07 and E2E-08 plus profile validation tests. |
| AC-05 Resume | E2E-06 plus persistence/state-machine integration tests. |
| AC-06 Secrets | E2E-19 plus DTO/log/event serialization tests and tracked-file secret scan. |
| AC-07 Workspace escape | Workspace/policy integration tests plus a UI denial scenario where relevant. |

### 15.5 Test data and isolation

- Use temporary test workspaces created per test or worker; never point tests at the repository root or a developer workspace.
- Use a fresh temporary SQLite database per test fixture or isolated transaction strategy.
- Use fake clock, fake runtime, fake provider, fake process runner, and fake worktree service for deterministic tests.
- Keep sanitized provider fixtures small and synthetic. Do not capture real provider request/response bodies containing a secret, private task, or repository context.
- Run live OpenCode Go Plan smoke tests separately from the deterministic test suite, serially, locally, and only under the opt-in conditions in Section 4.

## 16. Quality command and CI model

Provide scripts that implement the following intent. Exact command names may differ, but the behavior may not.

~~~text
offline quality gate:
  restore dependencies
  format verification
  backend build and analyzers
  frontend lint and strict type check
  backend unit/integration/provider contract tests with coverage threshold
  frontend unit/component tests with coverage threshold
  fresh SQLite migration verification
  API/SSE integration tests
  Playwright critical suite across Chromium, Firefox, WebKit
  instrumented Playwright coverage threshold
  requirement traceability report
  tracked-file and generated-artifact secret scan

local opt-in provider evidence:
  require RUN_LIVE_PROVIDER_TESTS=1
  require SHARPAGENT_OPENCODE_GO_API_KEY
  run only Ox Alpha Free, Muse Spark 1.2 Contributor, and MiMo-V2.5
  write redacted local evidence only
~~~

CI rules:

- CI never receives or requires <code>LLM-Key.md</code> or the OpenCode Go Plan API key for normal builds.
- CI must fail on failing tests, coverage below 91%, failing requirement traceability, lint/type/build failure, migration failure, newly tracked secret file, or accidental live-provider test activation.
- Publish sanitized coverage, traceability, and Playwright reports as build artifacts only if artifact inspection confirms no secret marker.
- Do not mark a workstream complete on a local manual demo alone. Commit deterministic automated evidence.

## 17. Implementation milestones and review checkpoints

| Milestone | Included phases | Review evidence |
|---|---|---|
| M0 — Safe skeleton | 0 | Build/test scripts, strict frontend baseline, secret handling check, fake provider/workspace. |
| M1 — Plan-only vertical slice | 1, 3, 4, 5 | SQLite persistence, fake validated profile, MAF-backed or fake runtime planning, todos, safe SSE replay, no side effects. |
| M2 — Controlled execution slice | 2, 4, 5 | Worktree, policy, two approvals, patch/test evidence, cancellation, audit history. |
| M3 — Full web MVP | 6 | Dashboard, session workspace, review, archive/resume, settings, themes, responsive tablet UI. |
| M4 — Hardening and release candidate | 7, 15, 16 | Health/metrics, resilience, security/accessibility, all automated gates, redacted live-provider evidence. |

At each milestone, review:

1. Spec traceability: which FRs and acceptance conditions are complete, blocked, or intentionally deferred.
2. Boundary integrity: whether a new dependency leaked into React, Domain, or Application layers.
3. Safety: whether a tool or provider action can bypass policy, approval, workspace isolation, or redaction.
4. Tests: coverage report, test quality, flaky-test rate, and untested error paths.
5. UI: desktop and tablet screenshots/Playwright evidence compared with the prototype intent.

## 18. Recommended implementation order within a coding-agent session

When handing a unit of work to another coding agent, constrain the change to one vertical behavior. The recommended sequence is:

1. Read the relevant specification section and identify the exact FR/AC IDs.
2. Add or update a failing unit/integration test.
3. Implement the smallest typed domain/application interface needed.
4. Add infrastructure behavior behind that interface.
5. Add API contract coverage.
6. Add React component/state behavior only after the API contract exists.
7. Add or extend a Playwright scenario for the visible workflow.
8. Run the smallest applicable checks, then the phase quality suite.
9. Update the traceability matrix and evidence links.

Avoid large cross-cutting rewrites. Never refactor safety or persistence boundaries at the same time as adding a provider or UI flow unless the tests make the behavior explicitly safe.

## 19. Risks, decisions, and stop conditions

| Risk or decision | Required response |
|---|---|
| Microsoft Agent Framework coding harness API differs from current assumptions | Keep the spike inside SharpAgent.Runtime.Maf. Preserve <code>IAgentRuntime</code>; update adapter only after tests prove canonical behavior. |
| A provider model does not support the required streaming/tool behavior | Mark profile validation failed or Plan-only. Do not silently substitute a different model or provider. |
| OpenCode Go Plan model identifier is unknown | Resolve it locally through authorized provider configuration, map it to an approved display name, and do not invent/test a different model. |
| A safety test reveals path escape, policy bypass, direct base-checkout write, secret leak, or unbounded shell behavior | Stop the affected workstream. Fix the boundary before continuing with UI or feature work. |
| Coverage target is met by low-value tests but critical error paths are uncovered | Improve the traceability matrix and branch tests; do not lower thresholds or exclude production code without review. |
| SQLite contention or single-node limits block a trusted-local pilot | Preserve repository/application abstractions, document evidence, and defer a server database decision to the pilot-hardening milestone. |
| Authentication or public deployment becomes required | Treat as a new scoped release with threat model and authorization design; do not expose the MVP as-is. |

## 20. Definition of done

The implementation is ready for MVP review only when all of the following are true:

- All Must functional requirements and AC-01 through AC-07 have automated evidence.
- The React application implements all required routes and the complete primary Plan/Execute, approval, review, cancel, archive, resume, settings, statistics, responsive, and theme flows.
- The backend owns provider calls, secrets, policy, approval, workspace tools, SQLite persistence, audit history, and canonical SSE events.
- No patch/write/side-effecting shell action can execute without the required policy and approval decision.
- Plan mode is proven side-effect free.
- Workspaces are validated and protected against traversal, foreign roots, and escaping symlinks.
- All three provider adapters pass deterministic contract tests. The only OpenCode Go Plan live test model display names are Ox Alpha Free, Muse Spark 1.2 Contributor, and MiMo-V2.5.
- Local live-provider evidence, if run, is opt-in, non-destructive, redacted, ignored, and not required by CI.
- <code>LLM-Key.md</code> remains ignored, untracked, unread by committed application/test code, and absent from Git history/artifacts.
- .NET, React, and instrumented Playwright code coverage are each at least 91% for lines/statements, branches, and functions/methods as applicable.
- Playwright requirement coverage is at least 92% weighted, with 100% of Must requirements and AC-01 through AC-07 covered.
- Chromium, Firefox, and WebKit critical browser workflows pass.
- Formatting, linting, strict TypeScript, .NET analyzers, fresh SQLite migration verification, secret scan, accessibility checks, and all offline test suites pass.
- No unresolved high-severity safety, privacy, data-loss, or provider-secret issue remains.

## 21. Handoff template for implementation agents

Use this compact template in each coding-agent handoff or pull request description:

~~~text
Scope:
  [one vertical behavior]

Specifications read:
  [functional/design/prototype sections]

Requirements covered:
  [FR and AC IDs]

Safety boundary preserved:
  [policy/workspace/provider/secret/event boundary]

Tests added or changed:
  [unit, integration, Playwright IDs]

Commands run:
  [offline commands only, unless local live test was explicitly authorized]

Coverage result:
  [backend, frontend, Playwright instrumented, traceability]

Live provider evidence:
  [not run | local opt-in redacted pass/fail for exact approved model]

Known limitations or follow-up:
  [none or explicit issue]
~~~

This template intentionally asks for evidence rather than hidden implementation reasoning. It keeps future agents aligned with the application-owned safety model and makes delivery progress auditable.
