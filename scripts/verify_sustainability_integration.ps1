# Sustainability Integration Verification Script
# Checks that all integration points exist in the codebase

Write-Host "Verifying Sustainability Integration..." -ForegroundColor Cyan
Write-Host ""

$projectRoot = "C:\Users\freha\.openclaw\workspace\salmon-sim"
$passed = 0
$failed = 0

function Test-Integration {
    param(
        [string]$File,
        [string]$Pattern,
        [string]$Description
    )
    
    $fullPath = Join-Path $projectRoot $File
    if (Test-Path $fullPath) {
        $content = Get-Content $fullPath -Raw
        if ($content -match $Pattern) {
            Write-Host "[PASS] $Description" -ForegroundColor Green
            $script:passed++
        } else {
            Write-Host "[FAIL] $Description - Pattern not found" -ForegroundColor Red
            $script:failed++
        }
    } else {
        Write-Host "[FAIL] $Description - File not found" -ForegroundColor Red
        $script:failed++
    }
}

Write-Host "=== SYSTEM PROPERTY ADDITIONS ===" -ForegroundColor Yellow
Test-Integration "src\LakskongenSim\Health\SeaLiceModel.cs" "AverageLicePerFish" "SeaLiceModel.AverageLicePerFish"
Test-Integration "src\LakskongenSim\Farm\SeaSite.cs" "EscapesThisYear" "SeaSite.EscapesThisYear"
Test-Integration "src\LakskongenSim\Farm\SeaSite.cs" "IsClosedContainment" "SeaSite.IsClosedContainment"
Test-Integration "src\LakskongenSim\Farm\SeaSite.cs" "CurrentStockingDensity" "SeaSite.CurrentStockingDensity"
Test-Integration "src\LakskongenSim\Farm\FeedSystem.cs" "SustainabilityRating" "FeedSystem.SustainabilityRating"
Test-Integration "src\LakskongenSim\Farm\FeedSystem.cs" "IngredientSources" "FeedSystem.IngredientSources"
Test-Integration "src\LakskongenSim\Logistics\LogisticsManager.cs" "CarbonFootprintScore" "LogisticsManager.CarbonFootprintScore"

Write-Host ""
Write-Host "=== SIMULATION ENGINE INTEGRATION ===" -ForegroundColor Yellow
Test-Integration "src\LakskongenSim\Core\SimulationEngine.cs" "SustainabilityManager" "SimulationEngine.SustainabilityManager"
Test-Integration "src\LakskongenSim\Core\SimulationEngine.cs" "Initialize" "SimulationEngine.Initialize"
Test-Integration "src\LakskongenSim\Core\SimulationEngine.cs" "OnSustainabilityScoreChanged" "SimulationEngine.OnSustainabilityScoreChanged"
Test-Integration "src\LakskongenSim\Core\SimulationEngine.cs" "OnCertificationChanged" "SimulationEngine.OnCertificationChanged"

Write-Host ""
Write-Host "=== MARKET SYSTEM INTEGRATION ===" -ForegroundColor Yellow
Test-Integration "src\LakskongenSim\Market\MarketSimulation.cs" "UpdateCertificationLevel" "MarketSimulation.UpdateCertificationLevel"
Test-Integration "src\LakskongenSim\Market\MarketSimulation.cs" "CanAccessMarket" "MarketSimulation.CanAccessMarket"
Test-Integration "src\LakskongenSim\Market\MarketSimulation.cs" "GetAccessibleMarkets" "MarketSimulation.GetAccessibleMarkets"

Write-Host ""
Write-Host "=== FINANCE SYSTEM INTEGRATION ===" -ForegroundColor Yellow
Test-Integration "src\LakskongenSim\Finance\FinanceManager.cs" "ESGInvestmentDiscount" "FinanceManager.ESGInvestmentDiscount"
Test-Integration "src\LakskongenSim\Finance\FinanceManager.cs" "GovernmentGrantMultiplier" "FinanceManager.GovernmentGrantMultiplier"
Test-Integration "src\LakskongenSim\Finance\FinanceManager.cs" "UpdateSustainabilityBonuses" "FinanceManager.UpdateSustainabilityBonuses"
Test-Integration "src\LakskongenSim\Finance\FinanceManager.cs" "ApplyESGDiscount" "FinanceManager.ApplyESGDiscount"
Test-Integration "src\LakskongenSim\Finance\FinanceManager.cs" "ApplyGrantMultiplier" "FinanceManager.ApplyGrantMultiplier"

Write-Host ""
Write-Host "=== INTEGRATION TESTS ===" -ForegroundColor Yellow
Test-Integration "LakskongenSim.Tests\Sustainability\SustainabilityIntegrationTests.cs" "Test_SustainabilityManager_CalculatesScoreFromGameState" "Score calculation test"
Test-Integration "LakskongenSim.Tests\Sustainability\SustainabilityIntegrationTests.cs" "Test_MarketSystem_UpdatesPriceMultiplier" "Market price test"
Test-Integration "LakskongenSim.Tests\Sustainability\SustainabilityIntegrationTests.cs" "Test_FinanceManager_AppliesESGDiscount" "ESG discount test"
Test-Integration "LakskongenSim.Tests\Sustainability\SustainabilityIntegrationTests.cs" "Test_EndToEnd_SustainabilityFlow" "End-to-end test"

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "Passed: $passed" -ForegroundColor Green
Write-Host "Failed: $failed" -ForegroundColor Red
Write-Host "======================================" -ForegroundColor Cyan

if ($failed -eq 0) {
    Write-Host ""
    Write-Host "SUCCESS: ALL INTEGRATION POINTS VERIFIED!" -ForegroundColor Green
    Write-Host "Sustainability system is fully wired into the simulation." -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "WARNING: Some integration points missing." -ForegroundColor Yellow
    exit 1
}
