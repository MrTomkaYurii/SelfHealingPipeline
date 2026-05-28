# ============================================================
#  run_tests.ps1 — запуск всіх тестів + HTML звіт
#  Використання:  .\run_tests.ps1
# ============================================================

$ErrorActionPreference = "Stop"
$root     = $PSScriptRoot
$reports  = Join-Path $root "test-reports"

New-Item -ItemType Directory -Force -Path $reports | Out-Null
New-Item -ItemType Directory -Force -Path "$reports\python" | Out-Null
New-Item -ItemType Directory -Force -Path "$reports\dotnet" | Out-Null

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SELF-HEALING PIPELINE — TEST RUNNER   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$pythonOk = $true
$dotnetOk = $true

# ── Python tests ──────────────────────────────────────────────────────────────

Write-Host "[1/2] Python unit tests (pytest)" -ForegroundColor Yellow

$venv = Join-Path $root "airflow-pipeline\.venv\Scripts\python.exe"
if (-not (Test-Path $venv)) {
    Write-Host "  SKIP — .venv not found at $venv" -ForegroundColor DarkYellow
    Write-Host "  Run:  cd airflow-pipeline && python -m venv .venv && .venv\Scripts\activate && pip install -r requirements.txt pytest pytest-html pytest-cov" -ForegroundColor DarkYellow
    $pythonOk = $false
} else {
    Push-Location (Join-Path $root "airflow-pipeline")
    try {
        & $venv -m pytest tests/ `
            --html="$reports\python\report.html" `
            --self-contained-html `
            --cov=dags `
            --cov-report="html:$reports\python\coverage" `
            --cov-report=term-missing `
            -v 2>&1

        if ($LASTEXITCODE -ne 0) { $pythonOk = $false }
    } finally {
        Pop-Location
    }
}

Write-Host ""

# ── .NET tests ────────────────────────────────────────────────────────────────

Write-Host "[2/2] .NET unit tests (xUnit)" -ForegroundColor Yellow

Push-Location (Join-Path $root "blazor-dashboard")
try {
    # Build first (coverage target bug requires build+test separately)
    dotnet build Dashboard.Tests -q 2>&1 | Out-Null
    dotnet test Dashboard.Tests --no-build `
        --logger "html;logfilename=$reports\dotnet\report.html" `
        -v normal 2>&1

    if ($LASTEXITCODE -ne 0) { $dotnetOk = $false }

    # Generate readable coverage report if reportgenerator is available
    $coverage = Get-ChildItem "$reports\dotnet\coverage" -Recurse -Filter "coverage.cobertura.xml" |
                Select-Object -First 1
    if ($coverage) {
        dotnet reportgenerator `
            -reports:$coverage.FullName `
            -targetdir:"$reports\dotnet\coverage-html" `
            -reporttypes:Html 2>&1 | Out-Null
        Write-Host "  Coverage HTML: $reports\dotnet\coverage-html\index.html" -ForegroundColor DarkGray
    }
} finally {
    Pop-Location
}

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  RESULTS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$pyStatus  = if ($pythonOk)  { "PASS" } else { "FAIL" }
$netStatus = if ($dotnetOk)  { "PASS" } else { "FAIL" }
$pyColor   = if ($pythonOk)  { "Green" } else { "Red" }
$netColor  = if ($dotnetOk)  { "Green" } else { "Red" }

Write-Host "  Python  : $pyStatus" -ForegroundColor $pyColor
Write-Host "  .NET    : $netStatus" -ForegroundColor $netColor
Write-Host ""
Write-Host "  Reports:" -ForegroundColor Gray
Write-Host "    Python  → $reports\python\report.html" -ForegroundColor Gray
Write-Host "    .NET    → $reports\dotnet\report.html" -ForegroundColor Gray
Write-Host ""

if (-not $pythonOk -or -not $dotnetOk) {
    Write-Host "  Some tests FAILED." -ForegroundColor Red
    exit 1
} else {
    Write-Host "  All tests PASSED." -ForegroundColor Green
    exit 0
}
