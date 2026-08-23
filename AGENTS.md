# SharpAgent — Agent Instructions

This repository is being implemented as a controlled, trusted-local AI coding-agent product. Read this file completely before making changes.

## 1. Source of truth and precedence

Use the documents below in this order when they apply:

1. This file defines repository-wide working rules and progressive disclosure.
2. [Functional specification](doc/functional-spec.md) defines product behavior, scope, API obligations, acceptance criteria, and release constraints.
3. [Technical design specification](doc/technical-design-spec.md) defines the approved implementation architecture and engineering decisions.
4. [HTML prototype](<prototype(html)/index.html>) defines intended interaction and visual behavior. It is not production implementation code or a data model.

When sources conflict:

- Functional specification wins for product scope, user-visible behavior, and acceptance criteria.
- Technical design wins for architecture, trust boundaries, persistence, API, and implementation structure.
- Prototype wins only for UI intent when it does not conflict with the first two.
- Do not silently change an approved architectural decision. Explain the conflict and request direction, or update the relevant design document as an explicit, reviewable change.

## 2. Progressive disclosure: what to read for each task

Do not load every document by default. Start with the relevant material below, then expand only when the task crosses a boundary.

| Task | Read first | Then read if needed |
|---|---|---|
| Project setup, dependencies, repository layout | Functional spec sections 1–4 and 17–19 | Technical design sections 2–3 and 14 |
| React pages, routes, themes, responsive UI | Functional spec section 9 and the HTML prototype | Technical design section 10 |
| Session lifecycle, runs, cancellation, archive, resume | Functional spec sections 7, 8.2 and 16 | Technical design sections 4–6 |
| MAF Harness, todos, compaction, runtime behavior | Functional spec sections 8.3, 12 and 13 | Technical design section 6 |
| REST API, SSE, idempotency, error responses | Functional spec section 10 | Technical design section 9 |
| SQLite, EF Core, audit/event persistence | Functional spec sections 11–12 | Technical design sections 4–5 |
| Provider adapters or model validation | Functional spec sections 8.6 and 13 | Technical design section 7 |
| Files, patches, shell/tests, worktrees or containers | Functional spec sections 8.4, 14 and AC-07 | Technical design section 8 |
| Policy, approvals, fingerprints and run limits | Functional spec sections 8.5 and 14 | Technical design sections 5 and 8 |
| Dashboard, usage, health, reports or metrics | Functional spec sections 8.7–8.8 | Technical design section 11 |
| Tests and delivery readiness | Functional spec sections 15–19 | Technical design sections 13–14 |

Before changing an unfamiliar directory, look for a closer AGENTS.md or equivalent repository instruction file and follow it in addition to this file.

## 3. Product guardrails that must not be weakened

- The MVP is local or trusted-internal only. Authentication is intentionally absent; do not expose it as a shared or Internet-facing service.
- The browser MUST NOT receive provider keys, secret references, unrestricted environment values, filesystem access, or a direct shell endpoint.
- React sends provider-neutral model profile IDs, never provider credentials or raw provider request shapes.
- MAF is the agent runtime, but SharpAgent owns policy, approvals, workspace boundaries, audit history, and tool execution.
- Plan mode MUST NOT write files or run side-effecting shell commands.
- Patches, writes, tests, and other side-effecting actions require an application policy decision and, by default, a single-use approval.
- Approval is bound to one run and an immutable action fingerprint, expires by server time, and must be revalidated immediately before execution.
- Direct Git commit, push, publish, install, delete, general-purpose shell access, workspace-network access, and autonomous background operation are out of scope for the MVP.
- The executor operates only inside an isolated run worktree or selected container boundary. Never treat a command allowlist as the only sandbox.
- SQLite stores application facts and non-secret configuration metadata. Never write a provider key to SQLite, API responses, SSE payloads, browser storage, logs, or fixtures.
- Do not expose hidden model reasoning. Show concise intent, tool, scope, result, and safe error summaries only.

## 4. Required stack and boundaries

| Area | Required approach |
|---|---|
| Frontend | React, Vite, strict TypeScript, React Router |
| UI | shadcn/ui components with Tailwind CSS 4 |
| Backend | .NET 10, Microsoft Agent Framework, Entity Framework Core |
| Persistence | SQLite plus EF Core migrations |
| Model providers | OpenCode Go, DeepSeek, OpenRouter through adapters |
| Live updates | REST commands/queries plus SSE per session |
| Authentication | Not part of the MVP |

Maintain these application boundaries:

~~~text
React UI → HTTP/SSE contracts only
API → Application services → Domain
Runtime.Maf / Providers / Workspace / Infrastructure → Application + Domain
~~~

Do not leak EF entities, MAF types, provider SDK types, <code>Process</code>, or raw filesystem types across those boundaries.

## 5. Required implementation habits

### Before coding

1. Inspect the current worktree and preserve unrelated user changes.
2. Identify the functional requirement IDs and technical-design sections affected.
3. State any assumption that changes scope, safety, user-visible behavior, or data shape.
4. Prefer the smallest vertical slice that is testable end to end.
5. Reuse existing components, patterns, and test helpers before adding new abstractions.

### While coding

- Use strict TypeScript. Do not introduce <code>any</code> to bypass contract problems.
- Use typed API DTOs and a typed reducer for SSE events.
- Make server state authoritative. Do not use browser local storage for sessions, approvals, provider config, usage, or audit data.
- Use browser storage only for non-sensitive UI preferences such as theme and layout.
- Persist state-changing commands and audit events before publishing their SSE events.
- Require an <code>Idempotency-Key</code> for state-changing API calls.
- Keep database transactions short; never hold one while waiting on a provider or command execution.
- Make output, context, time, tool-call, and cost limits explicit and enforceable.
- Make feature flags explicit for experimental or prerelease MAF coding-adjacent capabilities.
- Keep provider behavior behind the provider adapter and capability registry. A model picker never proves a model is eligible to execute tools.
- Use accessible shadcn patterns: labelled dialogs, visible focus, keyboard controls, non-color-only statuses, and safe destructive confirmations.

### Before finishing

1. Run targeted tests first, then the relevant build/lint/typecheck suite.
2. Verify the affected functional acceptance criteria, not only implementation internals.
3. Check responsive behavior for critical session/approval actions at tablet width.
4. Confirm API/SSE/log/error output contains no secret, hidden reasoning, or raw unrestricted environment data.
5. Summarize changed behavior, tests run, and any unresolved issue.

Do not commit, push, reset, reformat unrelated files, delete data, or modify generated/design documents unless the task explicitly authorizes it.

## 6. Mandatory patterns by subsystem

### Session and run lifecycle

- A session can have one active run only.
- Resume creates a new run linked to the existing session; it never overwrites earlier audit history.
- Cancellation is durable and cooperative. Re-check cancellation immediately before sensitive tool execution.
- A restart or unrecoverable stream break moves an active run to an interrupted/recoverable state rather than inventing completion.
- Archive hides an inactive session from the active list but never deletes audit, changes, approvals, or results.

### SSE and audit events

- Use canonical event types from functional spec section 9.3.
- Event IDs are durable, monotonic session sequences.
- Support <code>Last-Event-ID</code>, replay, heartbeat, and projection refresh on a detected gap.
- Unknown event types must render as non-breaking informational activity and be logged for diagnostics.
- Publish events after the database transaction commits.

### Policy and approval

- Normalize a tool proposal before policy evaluation.
- Authorize canonical resolved paths and structured executable-plus-arguments, never browser-supplied command text.
- Approval fingerprint includes run, action type, workspace identity, paths, patch/command content, execution environment, and policy version.
- Denial returns bounded feedback to the runtime; do not retry the same denied action indefinitely.
- Approval expiry, cancellation, changed proposal, or fingerprint mismatch must prevent execution.

### Workspace and tools

- Re-canonicalize the workspace path immediately before every action.
- Resolve symlinks and reject escaping targets, traversal, foreign absolute paths, unsupported link types, and device paths.
- Prefer a disposable Git worktree per run. Treat the registered base checkout as read-only baseline in the MVP.
- Use a restricted focused-command catalog, <code>UseShellExecute = false</code>, process-tree cancellation, output truncation, timeout, and environment allowlist.
- Capture diff/hashes and bounded command results for review.

### Providers and profiles

- Every provider adapter returns the same canonical model, stream, tool, usage, and error contract.
- Provider secrets are resolved server-side only.
- Execute mode requires an enabled, validated profile with streaming and tool-calling capability.
- OpenRouter remains plan-only until profile validation records successful compatible behavior.
- Automatic fallback is off unless an explicit approved routing chain has been implemented and emits a fallback event.

## 7. Recommended implementation order

Implement in vertical slices; do not build a visual-only UI before the underlying safety and event paths exist.

1. Create repository structure, configuration, EF migration baseline, health endpoint, and strict frontend foundation.
2. Implement workspaces, profile registry, provider validation, SQLite session/audit storage, and SSE replay.
3. Implement MAF-backed Plan mode with todos, read/search tools, bounded context compaction, and safe timeline events.
4. Implement policy evaluator, approval service, worktree executor, patch proposal/review, and focused test execution.
5. Implement the session workspace, approval cards, terminal/diff/review tabs, run controls, and responsive behavior.
6. Implement cancellation, archive, resume, dashboard, statistics, settings, provider health, and four themes.
7. Complete contract, policy, workspace, provider, integration, and end-to-end acceptance tests.

## 8. Definition of a good change

A good change:

- traces to one or more functional requirement IDs;
- respects the boundaries in the technical design;
- changes data and API contracts deliberately and migrates SQLite safely;
- includes enough tests to prove the relevant acceptance behavior;
- has safe failure and recovery behavior;
- does not add unsupported autonomy or direct browser-to-tool access;
- keeps the UI responsive, accessible, and aligned with the approved prototype.

Escalate rather than guess when a requested change would:

- alter the no-auth trusted-local deployment constraint;
- introduce a new provider or a direct provider call from the browser;
- allow general shell/Git/network/destructive actions;
- relax policy, approval, path isolation, or budget controls;
- require multi-process SQLite writers or shared-service deployment;
- change an accepted API/event/data contract without migration/versioning.

## 9. Completion checklist

Before describing a task as complete, verify:

- [ ] Relevant functional requirement IDs and acceptance criteria were met.
- [ ] Technical design boundaries were preserved.
- [ ] No credentials, secrets, hidden reasoning, raw provider payloads, or unsafe environment values leak.
- [ ] Plan mode has no side effect path.
- [ ] Sensitive actions remain policy- and approval-gated.
- [ ] Session state and audit events are durable and replayable.
- [ ] Tests, formatting, build, lint and type checks relevant to the change pass.
- [ ] No unrelated user files were changed.

