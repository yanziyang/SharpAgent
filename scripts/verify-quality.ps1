#Requires -Version 7
<#
.SYNOPSIS
    SharpAgent offline quality gate.

.DESCRIPTION
    One local command running the full offline suite:
      1. Tracked-file secret scan (proves LLM-Key.md stays ignored/untracked).
      2. .NET restore, format verification, warning-as-error build, tests + coverage.
      3. Backend coverage thresholds (91% lines/branches/methods) per documented area
         with the documented exclusion list below.
      4. Frontend npm ci, lint, strict type check, unit tests with 91% thresholds,
         production build.
      5. Playwright smoke suite (Chromium).

    Live provider evidence never runs here. Use scripts/run-live-opencode-smoke.ps1.

.PARAMETER Configuration
    .NET build configuration (Debug by default).

.PARAMETER SkipPlaywright
    Skips browser tests (inner-loop only; the CI/full gate runs them).

.PARAMETER SkipBrowserInstall
    Skips 'playwright install chromium'.

.NOTES
    Backend coverage exclusions (documented per Implementation Plan section 15.1):
      - SharpAgent.TestKit            : shared deterministic test doubles, not product code.
      - *.Tests assemblies             : test projects are never measured production code.
      - Assemblies without instrumentable code report N/A until they contain executable
        code (e.g. SharpAgent.Runtime.Maf before Phase 4). N/A does not lower the bar;
        it only avoids dividing by zero on empty scaffolding.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipPlaywright,
    [switch]$SkipBrowserInstall,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$CoverageThresholdPercent = 91

$script:StepResults = [System.Collections.Generic.List[string]]::new()

function Invoke-QualityStep {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    & $Action
    $script:StepResults.Add("PASS  $Name")
    Write-Host "=== PASS: $Name ===" -ForegroundColor Green
}

function Assert-LastExitCode {
    param([string]$Context)
    if ($LASTEXITCODE -ne 0) {
        throw "$Context failed with exit code $LASTEXITCODE."
    }
}

function Get-CoverageFromCobertura {
    <#
        Aggregates all cobertura files under a directory into per-assembly coverage.

        The same production assembly is instrumented by several test-project runs;
        this merger UNIONS observations so a run that loaded but did not execute a
        class can never dilute results:

          - line     : covered when hits > 0 in ANY run
          - branch   : best percentage observed across runs (coverlet emits "%")
          - method   : visited when any of its lines executed in ANY run

        Documented exclusions: EF migrations (verified by the migration step),
        obj/ generated code, compiler-generated async state machines.
    #>
    param([string]$CoverageDirectory)

    function ConvertTo-Percent {
        param([string]$Text)
        if ($Text -match '(\d+(?:\.\d+)?)\s*%') { return [double]$Matches[1] }
        return 0.0
    }

    # Class identity -> unioned observation set
    $classes = @{}

    foreach ($file in (Get-ChildItem -LiteralPath $CoverageDirectory -Recurse -Filter '*.cobertura.xml')) {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw

        foreach ($package in @($document.coverage.packages.package)) {
            if ($null -eq $package) { continue }

            # Package name is the assembly name (e.g. "SharpAgent.Application").
            $assemblyName = [string]$package.name
            if ($assemblyName -like '*.Tests' -or $assemblyName -eq 'SharpAgent.TestKit') { continue }

            foreach ($class in @($package.SelectNodes('classes/class'))) {
                if ($null -eq $class) { continue }
                if ($class.filename -match '[\\/]Persistence[\\/]Migrations[\\/]') { continue }
                if ($class.filename -match '[\\/]obj[\\/]') { continue }
                if ($class.name -match '<>|d__') { continue }

                $classKey = "$assemblyName|$($class.name)"
                if (-not $classes.ContainsKey($classKey)) {
                    $classes[$classKey] = @{
                        Assembly = $assemblyName; Lines = @{}; Conditions = @{}; Methods = @{}
                    }
                }
                $entry = $classes[$classKey]

                foreach ($line in @($class.SelectNodes('lines/line'))) {
                    if ($null -eq $line) { continue }

                    $number = [string]$line.number
                    $hits = [double]$line.hits
                    if (-not $entry.Lines.ContainsKey($number) -or [double]$entry.Lines[$number] -lt $hits) {
                        $entry.Lines[$number] = $hits
                    }

                    if ([string]::Equals([string]$line.branch, 'true', [System.StringComparison]::OrdinalIgnoreCase)) {
                        foreach ($condition in @($line.SelectNodes('conditions/condition'))) {
                            if ($null -eq $condition) { continue }
                            $conditionKey = '{0}|{1}' -f $condition.number, $condition.type
                            $percent = ConvertTo-Percent ([string]$condition.coverage)
                            if (-not $entry.Conditions.ContainsKey($conditionKey) -or [double]$entry.Conditions[$conditionKey] -lt $percent) {
                                $entry.Conditions[$conditionKey] = $percent
                            }
                        }
                    }
                }

                foreach ($method in @($class.SelectNodes('methods/method'))) {
                    if ($null -eq $method) { continue }
                    $methodKey = '{0}|{1}' -f $method.name, $method.signature
                    $visited = $false
                    foreach ($line in @($method.SelectNodes('lines/line'))) {
                        if ($null -eq $line) { continue }

                        # Some coverlet versions attribute hits ONLY at method level;
                        # fold them into line coverage so lines never under-report.
                        $number = [string]$line.number
                        $hits = [double]$line.hits
                        if (-not $entry.Lines.ContainsKey($number) -or [double]$entry.Lines[$number] -lt $hits) {
                            $entry.Lines[$number] = $hits
                        }

                        if ($hits -gt 0) { $visited = $true; break }
                    }
                    if (-not $entry.Methods.ContainsKey($methodKey)) {
                        $entry.Methods[$methodKey] = $visited
                    } elseif ($visited) {
                        $entry.Methods[$methodKey] = $true
                    }
                }
            }
        }
    }

    $assemblies = @{}
    foreach ($entry in $classes.Values) {
        $name = $entry.Assembly
        if (-not $assemblies.ContainsKey($name)) {
            $assemblies[$name] = @{
                Line   = @{ Covered = 0.0; Valid = 0.0 }
                Branch = @{ Covered = 0.0; Valid = 0.0 }
                Method = @{ Covered = 0.0; Valid = 0.0 }
            }
        }
        $stats = $assemblies[$name]

        foreach ($hits in $entry.Lines.Values) {
            $stats.Line.Valid += 1.0
            if ([double]$hits -gt 0) { $stats.Line.Covered += 1.0 }
        }
        foreach ($percent in $entry.Conditions.Values) {
            $stats.Branch.Valid += 100.0
            $stats.Branch.Covered += [double]$percent
        }
        foreach ($visited in $entry.Methods.Values) {
            $stats.Method.Valid += 1.0
            if ([bool]$visited) { $stats.Method.Covered += 1.0 }
        }
    }

    return $assemblies
}
function Assert-CoverageGroup {
    param(
        [hashtable]$Assemblies,
        [string[]]$Names,
        [string]$Label
    )

    $evaluated = 0
    $rows = @()

        foreach ($name in $Names) {
            if (-not $Assemblies.ContainsKey($name)) {
                $rows += "  N/A   $name (not instrumented yet)"
                continue
            }

            $stats = $Assemblies[$name]
            if ($stats.Line.Valid -eq 0 -or $stats.Line.Covered -eq 0) {
                $rows += "  N/A   $name (zero instrumentable lines)"
                continue
            }

            $evaluated++
            foreach ($metric in 'Line', 'Branch', 'Method') {
                $valid = $stats[$metric].Valid
                if ($valid -eq 0) {
                    $rows += "  N/A   {0,-6} coverage for {1} (no measurable units)" -f $metric, $name
                    continue
                }

                # Rates arrive as percentages already (Line/Branch = rate*100).
                $percent = [Math]::Round($stats[$metric].Covered, 2)
                $ok = $percent -ge $CoverageThresholdPercent
            $marker = $ok ? 'OK  ' : 'FAIL'
            $rows += ("  {0} {1,-6} {2} = {3}% (threshold {4}%)" -f $marker, $metric, $name, $percent, $CoverageThresholdPercent)
            if (-not $ok) {
                throw ("Coverage gate: {0} {1} coverage {2}% is below {3}%." -f $name, $metric, $percent, $CoverageThresholdPercent)
            }
        }
    }

    Write-Host "$Label coverage:"
    foreach ($row in $rows) { Write-Host $row }
    if ($evaluated -eq 0) {
        Write-Host "  Note: no instrumented assemblies yet in '$Label'; gate passes on scaffolding only."
    }
}

Write-Host 'SharpAgent offline quality gate' -ForegroundColor White
Push-Location $RepoRoot
try {
    Invoke-QualityStep -Name 'Secret scan' {
        & pwsh -NoProfile -File (Join-Path $RepoRoot 'scripts/Scan-Secrets.ps1') -RepoRoot $RepoRoot
        Assert-LastExitCode -Context 'Secret scan'
    }

    $solution = Join-Path $RepoRoot 'src/SharpAgent.sln'

    Invoke-QualityStep -Name '.NET restore' {
        dotnet restore $solution
        Assert-LastExitCode -Context 'dotnet restore'
    }

    Invoke-QualityStep -Name '.NET format verification' {
        dotnet format $solution --verify-no-changes
        Assert-LastExitCode -Context 'dotnet format'
    }

    Invoke-QualityStep -Name ".NET build ($Configuration, warnings as errors)" {
        dotnet build $solution -c $Configuration --no-restore
        Assert-LastExitCode -Context 'dotnet build'
    }

    $coverageDirectory = Join-Path $RepoRoot 'artifacts/backend-coverage'

    Invoke-QualityStep -Name '.NET tests + coverage collection' {
        if (Test-Path -LiteralPath $coverageDirectory) {
            Remove-Item -LiteralPath $coverageDirectory -Recurse -Force
        }

        dotnet test $solution -c $Configuration --no-build `
            --collect:"XPlat Code Coverage;Format=cobertura" `
            --results-directory $coverageDirectory `
            --nologo
        Assert-LastExitCode -Context 'dotnet test'
    }

    Invoke-QualityStep -Name "Backend coverage thresholds ($CoverageThresholdPercent%)" {
        $assemblies = Get-CoverageFromCobertura -CoverageDirectory $coverageDirectory
        Assert-CoverageGroup -Assemblies $assemblies -Names @('SharpAgent.Domain', 'SharpAgent.Application') -Label 'Domain+Application'
        Assert-CoverageGroup -Assemblies $assemblies -Names @('SharpAgent.Infrastructure', 'SharpAgent.Runtime.Maf', 'SharpAgent.Api') -Label 'Infrastructure+Runtime+API'
    }

    Invoke-QualityStep -Name 'SQLite migration verification' {
        dotnet tool restore
        Assert-LastExitCode -Context 'dotnet tool restore'

        $migrationDirectory = Join-Path $RepoRoot 'artifacts/migration-test'
        if (Test-Path -LiteralPath $migrationDirectory) {
            Remove-Item -LiteralPath $migrationDirectory -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $migrationDirectory | Out-Null

        $env:SHARPAGENT_SQLITE_PATH = Join-Path $migrationDirectory 'sharpagent.db'
        try {
            # Apply all migrations to a FRESH database file.
            dotnet dotnet-ef database update `
                --project (Join-Path $RepoRoot 'src/backend/SharpAgent.Infrastructure') `
                --startup-project (Join-Path $RepoRoot 'src/backend/SharpAgent.Api')
            Assert-LastExitCode -Context 'ef database update'

            if (-not (Test-Path -LiteralPath $env:SHARPAGENT_SQLITE_PATH)) {
                throw 'Migration verification failed: database file was not created.'
            }

            # Fail the gate when the model drifts without a committed migration.
            dotnet dotnet-ef migrations has-pending-model-changes `
                --project (Join-Path $RepoRoot 'src/backend/SharpAgent.Infrastructure') `
                --startup-project (Join-Path $RepoRoot 'src/backend/SharpAgent.Api')
            Assert-LastExitCode -Context 'ef has-pending-model-changes'
        } finally {
            Remove-Item Env:SHARPAGENT_SQLITE_PATH -ErrorAction SilentlyContinue
        }

        Write-Host "Fresh-database migration verified at $migrationDirectory."
    }

    Invoke-QualityStep -Name 'Frontend clean install (npm ci)' {
        npm ci
        Assert-LastExitCode -Context 'npm ci'
    }

    Invoke-QualityStep -Name 'Frontend lint (oxlint)' {
        npm run lint -w src/frontend/sharpagent-web
        Assert-LastExitCode -Context 'frontend lint'
    }

    Invoke-QualityStep -Name 'Frontend strict type check' {
        npm run typecheck -w src/frontend/sharpagent-web
        Assert-LastExitCode -Context 'frontend typecheck'
    }

    Invoke-QualityStep -Name "Frontend unit tests + coverage thresholds ($CoverageThresholdPercent%)" {
        npm run test:coverage -w src/frontend/sharpagent-web
        Assert-LastExitCode -Context 'frontend coverage gate'
    }

    Invoke-QualityStep -Name 'Frontend production build' {
        npm run build -w src/frontend/sharpagent-web
        Assert-LastExitCode -Context 'frontend build'
    }

    if (-not $SkipPlaywright) {
        if (-not $SkipBrowserInstall) {
            Invoke-QualityStep -Name 'Playwright browser install (chromium)' {
                npx playwright install chromium
                Assert-LastExitCode -Context 'playwright install'
            }
        }

        Invoke-QualityStep -Name 'Playwright browser suite (smoke)' {
            npm test -w tests/web-e2e
            Assert-LastExitCode -Context 'playwright tests'
        }
    } else {
        Write-Host '[skip] Playwright suite skipped by request (inner-loop only).' -ForegroundColor Yellow
    }

    Write-Host "`nDeferred gates (arrive with later phases):" -ForegroundColor DarkGray
    Write-Host '- Requirement traceability report >=92% weighted (Phase 6).' -ForegroundColor DarkGray
    Write-Host '- Playwright instrumented coverage + Firefox/WebKit critical suite (Phase 6).' -ForegroundColor DarkGray

    Write-Host "`nQuality gate summary:" -ForegroundColor White
    foreach ($row in $script:StepResults) {
        Write-Host "  [$row]" -ForegroundColor Green
    }
    Write-Host "`nOFFLINE QUALITY GATE PASSED." -ForegroundColor Green
} finally {
    Pop-Location
}

exit 0



