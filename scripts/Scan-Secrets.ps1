#Requires -Version 7
<#
.SYNOPSIS
    Repository secret scan for SharpAgent.

.DESCRIPTION
    Proves LLM-Key.md stays Git-ignored and untracked WITHOUT ever opening the file,
    then scans tracked content and generated artifacts for high-confidence secret shapes.
    Findings are reported as paths and rule identifiers only; matched values are never printed.

.NOTES
    Part of the offline quality gate (scripts/verify-quality.ps1).
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-GitAvailable {
    git rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Not a Git repository: $RepoRoot"
    }
}

function Assert-KeyFileIgnored {
    # check-ignore answers using Git metadata only; the file content is never read.
    git -C $RepoRoot check-ignore --quiet -- 'LLM-Key.md'
    if ($LASTEXITCODE -ne 0) {
        throw 'Ignore regression: LLM-Key.md is NOT ignored by Git. Fix .gitignore before continuing.'
    }
    Write-Host '[secrets] OK: LLM-Key.md is ignored.' -ForegroundColor Green

    # Untracked proof: listing the index never touches file contents.
    $tracked = git -C $RepoRoot ls-files -- 'LLM-Key.md'
    if ($tracked) {
        throw 'Secret regression: LLM-Key.md is tracked by Git. Remove it from the index/history immediately.'
    }
    Write-Host '[secrets] OK: LLM-Key.md is untracked.' -ForegroundColor Green
}

function Get-SecretRules {
    @{
        'openai-style-key'      = @{ Pattern = 'sk-[A-Za-z0-9_\-]{16,}' }
        'anthropic-key'         = @{ Pattern = 'sk-ant-[A-Za-z0-9_\-]{16,}' }
        'github-token'          = @{ Pattern = 'gh[pousr]_[A-Za-z0-9]{20,}' }
        'aws-access-key'        = @{ Pattern = 'AKIA[0-9A-Z]{16}' }
        'slack-token'           = @{ Pattern = 'xox[baprs]-[A-Za-z0-9\-]{10,}' }
        'private-key-block'     = @{ Pattern = '-----BEGIN (RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----' }
        'assigned-secret-value' = @{
            # Only flags quoted literal assignments, e.g. apiKey: "...".
            # Env-var references (process.env.X, $env:X, <placeholders>) never match.
            Pattern = '(?i)\b(api[_\-]?key|secret|passwd|password|token|bearer)\b\s*[:=]\s*["''"][^"''"<\$\{\s][^"''"]{11,}["''"]'
        }
    }
}

# Values that are obviously synthetic documentation/test markers, never real credentials.
$SyntheticValueAllowlist = @(
    'sk-test', 'sk-fake', 'sk-example', 'sk-sample', 'sk-placeholder', 'sk-probe',
    'sk-ant-test', 'REDACTED', 'changeme', 'change-me', 'dummy', 'not-a-real',
    '<your', '${', '$(', '%('
)

function Test-TextFile {
    param([string]$Path)
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if (@('.png', '.jpg', '.jpeg', '.gif', '.ico', '.woff', '.woff2', '.ttf', '.pdf', '.zip', '.db') -contains $extension) {
        return $false
    }

    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        if ($bytes.Length -gt 4) {
            foreach ($offset in 0..([Math]::Min(511, $bytes.Length - 1))) {
                if ($bytes[$offset] -eq 0) { return $false }
            }
        }
        return $true
    } catch {
        return $false
    }
}

function Find-SecretMatches {
    param(
        [string]$Path,
        [hashtable]$Rules
    )

    $findings = [System.Collections.Generic.List[string]]::new()
    $lines = [System.IO.File]::ReadAllLines($Path)

    foreach ($ruleName in $Rules.Keys) {
        $pattern = $Rules[$ruleName].Pattern
        for ($index = 0; $index -lt $lines.Length; $index++) {
            $line = $lines[$index]
            if ($line -match $pattern) {
                $value = $Matches[0]
                $lower = $value.ToLowerInvariant()
                $isSynthetic = $SyntheticValueAllowlist | Where-Object { $lower.StartsWith($_.ToLowerInvariant()) }
                if (-not $isSynthetic) {
                    # Path + rule + line number only. Never print the matched value.
                    $findings.Add(('{0}: {1} (line {2})' -f $Path, $ruleName, ($index + 1)))
                }
            }
        }
    }

    return $findings
}

Assert-GitAvailable
Assert-KeyFileIgnored

$rules = Get-SecretRules
$candidates = [System.Collections.Generic.HashSet[string]]::new()

foreach ($file in (git -C $RepoRoot ls-files)) {
    $candidates.Add((Join-Path $RepoRoot $file)) | Out-Null
}

foreach ($artifactDir in @('artifacts', 'playwright-report', 'test-results')) {
    $full = Join-Path $RepoRoot $artifactDir
    if (Test-Path -LiteralPath $full) {
        foreach ($file in (Get-ChildItem -LiteralPath $full -Recurse -File)) {
            $candidates.Add($file.FullName) | Out-Null
        }
    }
}

$totalFindings = [System.Collections.Generic.List[string]]::new()
$scanned = 0

foreach ($path in $candidates) {
    if (-not (Test-Path -LiteralPath $path)) { continue }
    if (-not (Test-TextFile -Path $path)) { continue }

    $scanned++
    foreach ($finding in (Find-SecretMatches -Path $path -Rules $rules)) {
        $totalFindings.Add($finding)
    }
}

if ($totalFindings.Count -gt 0) {
    Write-Host "[secrets] FAIL: $($totalFindings.Count) potential secret(s) found." -ForegroundColor Red
    foreach ($finding in $totalFindings) {
        Write-Host "  $finding" -ForegroundColor Red
    }
    Write-Host '[secrets] Values are intentionally not displayed. Inspect flagged paths manually.'
    exit 1
}

Write-Host "[secrets] OK: scanned $scanned files, no secret-shaped content." -ForegroundColor Green
exit 0
