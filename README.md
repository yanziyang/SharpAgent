# SharpAgent

A controlled, trusted-local AI coding-agent web application: React + Vite frontend, .NET 10 backend with Microsoft Agent Framework runtime, provider adapters (OpenCode Go, DeepSeek, OpenRouter), SQLite persistence, and policy-gated workspace tools. See `doc/` for the functional specification, technical design, and implementation plan.

## Repository layout

- `src/backend/` — .NET 10 solution (`SharpAgent.sln`): Domain, Application, Infrastructure, Runtime.Maf, Api.
- `src/frontend/sharpagent-web/` — Vite React app (strict TypeScript, Tailwind CSS 4, shadcn/ui).
- `tests/` — backend test projects plus `web-unit/` (Vitest) and `web-e2e/` (Playwright).
- `test-assets/` — deterministic fixtures for automated tests.
- `scripts/` — quality gate and operational scripts.

## Development

Prerequisites: .NET 10 SDK, Node.js 20+ with npm.

```powershell
npm install                      # one-time workspace install
pwsh scripts/verify-quality.ps1  # full offline quality suite
```

The offline quality suite runs the secret scan, format verification, warning-as-error build, all backend tests with 91% coverage thresholds, frontend lint/typecheck/unit tests/build, and the Playwright smoke suite.

Useful inner-loop commands:

| Command | Purpose |
|---|---|
| `dotnet run --project src/backend/SharpAgent.Api` | API on http://localhost:5080 (`GET /api/health`). |
| `npm run dev -w src/frontend/sharpagent-web` | Frontend dev server on http://localhost:5173 (proxies `/api`). |
| `dotnet test src/SharpAgent.sln` | Backend tests. |
| `npm run test -w src/frontend/sharpagent-web` | Frontend unit tests. |

### First-run local walkthrough

Development startup enables an explicit, credential-free local demo catalog:

- `Offline demo (Plan only)` is deterministic and makes no external provider request.
- `Default safe policy` is seeded with bounded limits and approval-gated write/command rules.
- Register a trusted repository root from **Administration → Workspaces**.
- Open **New session**. The conversation page opens directly; choose the run mode,
  model, workspace, and security policy on that page, enter the first message, and
  select **Send** to create the session and start the run.

The demo exercises session lifecycle, durable activity, usage, and review UI without
inspecting or changing files. Real provider profiles must be configured server-side
with secrets kept outside the browser and SQLite.

For local OpenCode Go Plan use, copy
`appsettings.Local.example.json` to the repository root as
`appsettings.Local.json` and put the API key in `OpenCodeGo:ApiKey`. This file
is ignored by Git and is loaded only by the server process. SharpAgent retrieves
the current non-secret model catalog from
`https://opencode.ai/zen/go/v1/models` at API startup and keeps the three
approved Plan profiles: `Ox Alpha Free`, `Muse Spark 1.2 Contributor`, and
`MiMo-V2.5`. The local
`appsettings.Local.json` file contains the catalog URL and non-secret fallback
IDs; the browser never receives the API key.

```powershell
```

Restart the API after changing the local configuration. The browser
receives only provider-neutral profile IDs and safe capability metadata.

For troubleshooting, set `Troubleshooting:LoggingEnabled` to `false` in the root
`appsettings.Local.json` to disable server logging. Logging is server-side only;
provider credentials, raw provider payloads, and hidden reasoning are never sent
to the browser.

### Live provider evidence (local opt-in only)

Never commit `LLM-Key.md` or any key. To run the non-destructive OpenCode Go Plan smoke locally:

```powershell
$env:RUN_LIVE_PROVIDER_TESTS = '1'
$env:SHARPAGENT_OPENCODE_GO_API_KEY = '<paste from your ignored local key file>'
pwsh scripts/run-live-opencode-smoke.ps1
```

Only the approved allowlist runs: Ox Alpha Free, Muse Spark 1.2 Contributor, MiMo-V2.5. Results are written redacted to `artifacts/live-provider/report.md`.

> Security note: this MVP is for local/trusted-internal use without authentication. Do not expose it to untrusted networks.
