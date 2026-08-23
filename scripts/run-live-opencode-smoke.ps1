#Requires -Version 7
<#
.SYNOPSIS
    Local opt-in OpenCode Go Plan live smoke evidence runner.

.DESCRIPTION
    Runs the live provider tests ONLY when both explicit local opt-in conditions hold:
      - RUN_LIVE_PROVIDER_TESTS=1
      - SHARPAGENT_OPENCODE_GO_API_KEY is set

    The API key must be provided by the operator from an authorized local source
    (for example exported manually from the ignored LLM-Key.md file). This script,
    committed code, and CI never read that file, and neither prints the key.

    When tests run, they cover exactly the approved allowlist:
      Ox Alpha Free, Muse Spark 1.2 Contributor, MiMo-V2.5
    Results are written as redacted metadata to artifacts/live-provider/report.md
    (Git-ignored). Values of keys are never included.

.EXAMPLE
    $env:RUN_LIVE_PROVIDER_TESTS = '1'
    $env:SHARPAGENT_OPENCODE_GO_API_KEY = '<pasted locally>'
    pwsh scripts/run-live-opencode-smoke.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$flagSet = [string]::Equals($env:RUN_LIVE_PROVIDER_TESTS, '1', [System.StringComparison]::Ordinal)
$keyPresent = -not [string]::IsNullOrWhiteSpace($env:SHARPAGENT_OPENCODE_GO_API_KEY)

if (-not $flagSet -or -not $keyPresent) {
    Write-Host 'SKIP: live provider tests require explicit local opt-in:' -ForegroundColor Yellow
    if (-not $flagSet) { Write-Host '  - RUN_LIVE_PROVIDER_TESTS=1' }
    if (-not $keyPresent) { Write-Host '  - SHARPAGENT_OPENCODE_GO_API_KEY set from an authorized local source' }
    Write-Host 'No outbound provider calls were made.'
    exit 2
}

$reportDirectory = Join-Path $RepoRoot 'artifacts/live-provider'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

Write-Host 'Running opt-in live OpenCode Go Plan smoke tests (allowlist: Ox Alpha Free, Muse Spark 1.2 Contributor, MiMo-V2.5)...'

dotnet test (Join-Path $RepoRoot 'tests/SharpAgent.LiveProviderTests/SharpAgent.LiveProviderTests.csproj') `
    -c $Configuration `
    --nologo
$exit = $LASTEXITCODE

$timestamp = (Get-Date).ToUniversalTime().ToString('u')
$category = switch ($exit) {
    0 { 'pass-or-sanitized-failure-recorded-by-tests' }
    default { 'test-run-failed' }
}
@"

# Live provider evidence (redacted)

- TimestampUtc: $timestamp
- OutcomeCategory: $category
- Allowlist: Ox Alpha Free; Muse Spark 1.2 Contributor; MiMo-V2.5
- Notes: per-model capability results and sanitized failure categories are emitted by the test run itself. No key material, endpoint URLs with credentials, or raw provider payloads are stored here.
"@ | Set-Content -LiteralPath (Join-Path $reportDirectory 'report.md') -Encoding utf8

Write-Host "Evidence written to artifacts/live-provider/report.md (git-ignored)."
exit $exit
