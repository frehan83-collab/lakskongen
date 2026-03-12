# Test Script for Cycle 1: Hatchery Bug Fix + Disease Outbreak Model
# Run after Simulation Engineer completes to assess realism, efficiency, innovation

Write-Host "=== CYCLE 1 TEST SUITE ===" -ForegroundColor Cyan
Write-Host "Testing: Hatchery Bug Fix + Disease Outbreak Model"
Write-Host "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""

# 1. Check if Hatchery.gd has the fix
Write-Host "1. HATCHERY BUG FIX VALIDATION" -ForegroundColor Yellow
$hatcheryPath = "godot/scripts/Hatchery.gd"
if (Test-Path $hatcheryPath) {
    $content = Get-Content $hatcheryPath -Raw
    $hasBackwardIteration = $content -match 'for i in range\(eggs\.size\(\) - 1, -1, -1\):'
    $hasDuplicateBug = $content -match 'eggs\.duplicate\(true\)'
    
    if ($hasBackwardIteration) {
        Write-Host "   ✅ Hatchery uses backward iteration (fix implemented)" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Hatchery does NOT use backward iteration" -ForegroundColor Red
    }
    
    if ($hasDuplicateBug) {
        Write-Host "   ❌ Hatchery still contains .duplicate(true) bug" -ForegroundColor Red
    } else {
        Write-Host "   ✅ No .duplicate(true) bug detected" -ForegroundColor Green
    }
} else {
    Write-Host "   ❌ Hatchery.gd not found at $hatcheryPath" -ForegroundColor Red
}

Write-Host ""

# 2. Check for DiseaseOutbreak.gd and validate parameters
Write-Host "2. DISEASE OUTBREAK MODEL VALIDATION" -ForegroundColor Yellow
$diseasePath = "godot/scripts/DiseaseOutbreak.gd"
if (Test-Path $diseasePath) {
    $content = Get-Content $diseasePath -Raw
    Write-Host "   ✅ DiseaseOutbreak.gd found" -ForegroundColor Green
    
    # Check for key disease parameters
    $checks = @(
        @{Name="ISA probability"; Pattern="ISA.*0\.005|0\.5%"},
        @{Name="PD probability"; Pattern="PD.*(0\.02|0\.04|2%|4%)"},
        @{Name="CMS probability"; Pattern="CMS.*(0\.01|0\.03|1%|3%)"},
        @{Name="Vaccination effectiveness"; Pattern="vaccin.*(0\.9|0\.8|90%|80%)"},
        @{Name="Spread distance"; Pattern="spread.*(5|15|50).*km"}
    )
    
    foreach ($check in $checks) {
        if ($content -match $check.Pattern) {
            Write-Host "   ✅ $($check.Name) appears realistic" -ForegroundColor Green
        } else {
            Write-Host "   ⚠️  $($check.Name) not found or unrealistic" -ForegroundColor Yellow
        }
    }
    
    # Check for probabilistic triggers
    if ($content -match "rand|random|probability") {
        Write-Host "   ✅ Uses probabilistic triggers" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  No probabilistic triggers detected" -ForegroundColor Yellow
    }
    
    # Check for reference to real data
    if ($content -match "Chile|2007|reference|Norwegian") {
        Write-Host "   ✅ References real outbreak data" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  No reference to real outbreak data" -ForegroundColor Yellow
    }
    
} else {
    Write-Host "   ❌ DiseaseOutbreak.gd not found at $diseasePath" -ForegroundColor Red
}

Write-Host ""

# 3. Code quality checks
Write-Host "3. CODE QUALITY ASSESSMENT" -ForegroundColor Yellow

function Test-FileExists($path) {
    if (Test-Path $path) {
        $size = (Get-Item $path).Length
        $lines = (Get-Content $path).Count
        Write-Host "   ✅ $path exists ($lines lines, $size bytes)" -ForegroundColor Green
        return $true
    }
    Write-Host "   ❌ $path not found" -ForegroundColor Red
    return $false
}

$filesToCheck = @(
    "godot/scripts/Hatchery.gd",
    "godot/scripts/DiseaseOutbreak.gd",
    "src/LakskongenSim/Biology/GrowthModel.cs",
    "src/LakskongenSim/Health/DiseaseModel.cs"
)

foreach ($file in $filesToCheck) {
    Test-FileExists $file
}

Write-Host ""

# 4. Preliminary scoring (to be refined after manual review)
Write-Host "4. PRELIMINARY SCORING (BASED ON STATIC ANALYSIS)" -ForegroundColor Cyan

$realismScore = 0
$efficiencyScore = 0  
$innovationScore = 0

# Realism scoring
if ($hasBackwardIteration -and !$hasDuplicateBug) {
    $realismScore += 2  # Hatchery fix is realistic (avoids clone bug)
}

if (Test-Path $diseasePath) {
    $diseaseContent = Get-Content $diseasePath -Raw
    $realismIndicators = 0
    if ($diseaseContent -match "ISA.*0\.005") { $realismIndicators++ }
    if ($diseaseContent -match "PD.*(0\.02|0\.04)") { $realismIndicators++ }
    if ($diseaseContent -match "CMS.*(0\.01|0\.03)") { $realismIndicators++ }
    if ($diseaseContent -match "vaccin.*(0\.9|0\.8)") { $realismIndicators++ }
    if ($diseaseContent -match "spread.*(5|15).*km") { $realismIndicators++ }
    
    $realismScore += [math]::Min(5, $realismIndicators)  # Max 5 for disease realism
}

# Efficiency scoring (code structure)
$efficiencyScore = 3  # Baseline - assumes reasonable implementation
if ($hasBackwardIteration) { $efficiencyScore += 2 }  # O(n) iteration fix
if (Test-Path $diseasePath) {
    $diseaseContent = Get-Content $diseasePath -Raw
    if ($diseaseContent -notmatch "O\(n\^2\)") { $efficiencyScore += 1 }  # No obvious quadratic algorithms
    if ($diseaseContent -match "Dictionary|Hash") { $efficiencyScore += 1 }  # Uses efficient data structures
}

# Innovation scoring
$innovationScore = 2  # Baseline - disease model is expected
if (Test-Path $diseasePath) {
    $diseaseContent = Get-Content $diseasePath -Raw
    if ($diseaseContent -match "probabilistic|stochastic") { $innovationScore += 1 }
    if ($diseaseContent -match "outbreak.*chain|superspreader") { $innovationScore += 2 }  # Advanced mechanics
    if ($diseaseContent -match "vaccination.*schedule|booster") { $innovationScore += 1 }
    if ($diseaseContent -match "environmental.*factor|temperature") { $innovationScore += 1 }
}

# Cap scores at 10
$realismScore = [math]::Min(10, $realismScore)
$efficiencyScore = [math]::Min(10, $efficiencyScore)  
$innovationScore = [math]::Min(10, $innovationScore)

Write-Host "   Realism: $realismScore/10" -ForegroundColor $(if ($realismScore -ge 7) { "Green" } elseif ($realismScore -ge 4) { "Yellow" } else { "Red" })
Write-Host "   Efficiency: $efficiencyScore/10" -ForegroundColor $(if ($efficiencyScore -ge 7) { "Green" } elseif ($efficiencyScore -ge 4) { "Yellow" } else { "Red" })
Write-Host "   Innovation: $innovationScore/10" -ForegroundColor $(if ($innovationScore -ge 7) { "Green" } elseif ($innovationScore -ge 4) { "Yellow" } else { "Red" })

$averageScore = [math]::Round(($realismScore + $efficiencyScore + $innovationScore) / 3, 1)
Write-Host "   Average: $averageScore/10" -ForegroundColor $(if ($averageScore -ge 7) { "Green" } elseif ($averageScore -ge 4) { "Yellow" } else { "Red" })

Write-Host ""

# 5. Convergence check
Write-Host "5. CONVERGENCE CHECK" -ForegroundColor Cyan
if ($averageScore -ge 9.0) {
    Write-Host "   ✅ CONVERGENCE ACHIEVED (scores ≥9.0)" -ForegroundColor Green
    Write-Host "   Cycle 1 complete, ready for integration." -ForegroundColor Green
} else {
    Write-Host "   🔄 CONTINUE ITERATION (scores <9.0)" -ForegroundColor Yellow
    Write-Host "   Deficiencies identified, proceed to Self‑Critique → Refine." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== END TEST SUITE ===" -ForegroundColor Cyan