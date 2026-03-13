# build.ps1 — Build the LakskongenSim solution
# Usage: .\scripts\build.ps1 [-Configuration Release] [-RunTests]

param(
    [string]$Configuration = "Debug",
    [switch]$RunTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $root "LakskongenSim.sln"

Write-Host "=== LakskongenSim Build ===" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration"
Write-Host "Solution      : $sln"
Write-Host ""

# Build
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build $sln --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Build succeeded." -ForegroundColor Green

# Tests (optional)
if ($RunTests) {
    Write-Host ""
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test $sln --configuration $Configuration --nologo --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "TESTS FAILED" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "All tests passed." -ForegroundColor Green
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
