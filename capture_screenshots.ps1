# Lakskongen Screenshot Capture Helper
# Automates screenshot capture using Godot's built-in Photo Mode

param(
    [string]$GodotPath = "C:\Program Files\Godot\Godot_v4.3-stable_win64.exe",
    [switch]$LaunchOnly
)

$ProjectPath = "$PSScriptRoot\godot\project.godot"
$OutputDir = "$PSScriptRoot\screenshots\steam_store"

Write-Host "=== Lakskongen Screenshot Capture ===" -ForegroundColor Cyan
Write-Host ""

# Check if Godot exists
if (-not (Test-Path $GodotPath)) {
    Write-Host "❌ Godot not found at: $GodotPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Godot 4.3+ or specify path with -GodotPath" -ForegroundColor Yellow
    Write-Host "Example: .\capture_screenshots.ps1 -GodotPath 'C:\Godot\godot.exe'" -ForegroundColor Yellow
    exit 1
}

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "📁 Created output directory: $OutputDir" -ForegroundColor Green
}

# Launch Godot
Write-Host "🚀 Launching Godot with Lakskongen project..." -ForegroundColor Cyan
Write-Host ""

if ($LaunchOnly) {
    Start-Process -FilePath $GodotPath -ArgumentList "--path `"$PSScriptRoot\godot`""
    Write-Host "✅ Godot launched!" -ForegroundColor Green
    exit 0
}

# Show instructions
Write-Host "📸 Screenshot Capture Instructions:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Wait for Godot to load the project" -ForegroundColor White
Write-Host "2. Click 'Play' (F5) to start the game" -ForegroundColor White
Write-Host "3. Press P to enter Photo Mode" -ForegroundColor White
Write-Host "4. Use these controls:" -ForegroundColor White
Write-Host "   • WASD - Move camera" -ForegroundColor Gray
Write-Host "   • Mouse - Look around" -ForegroundColor Gray
Write-Host "   • Q/E - Move up/down" -ForegroundColor Gray
Write-Host "   • F12 - Take screenshot" -ForegroundColor Gray
Write-Host "   • F - Cycle filters" -ForegroundColor Gray
Write-Host "   • ESC - Exit photo mode" -ForegroundColor Gray
Write-Host ""
Write-Host "5. Capture these 10 scenes:" -ForegroundColor White
Write-Host ""

$screenshots = @(
    "01_dashboard_overview - Full game view with all UI panels",
    "02_fish_pens - Zoomed view of fish pens with health indicators",
    "03_market_trading - Market panel showing price charts",
    "04_processing_plant - Processing chain view",
    "05_map_norwegian_coast - Full map with 8 zones",
    "06_tutorial - Tutorial overlay visible",
    "07_disease_management - Disease management panel",
    "08_harvest - Harvest operation in progress",
    "09_tech_tree - Technology tree panel",
    "10_seasonal_weather - Seasonal variation (winter/summer)"
)

foreach ($shot in $screenshots) {
    Write-Host "   • $shot" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "6. Screenshots are saved to:" -ForegroundColor White
Write-Host "   $env:APPDATA\Godot\app_userdata\Lakskongen\screenshots\" -ForegroundColor Gray
Write-Host ""
Write-Host "7. When done, press ENTER to copy screenshots to project folder" -ForegroundColor Yellow
Write-Host ""

# Launch Godot
Start-Process -FilePath $GodotPath -ArgumentList "`"$ProjectPath`""

# Wait for user
Read-Host "Press ENTER when you've captured all screenshots"

# Find and copy screenshots
$UserDataPath = "$env:APPDATA\Godot\app_userdata\Lakskongen\screenshots"

if (Test-Path $UserDataPath) {
    Write-Host ""
    Write-Host "📦 Copying screenshots..." -ForegroundColor Cyan
    
    $screenshots = Get-ChildItem -Path $UserDataPath -Filter "*.png" | Sort-Object LastWriteTime -Descending
    
    if ($screenshots.Count -eq 0) {
        Write-Host "❌ No screenshots found in $UserDataPath" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Found $($screenshots.Count) screenshot(s)" -ForegroundColor Green
    
    foreach ($screenshot in $screenshots) {
        Copy-Item -Path $screenshot.FullName -Destination $OutputDir -Force
        Write-Host "  ✅ Copied: $($screenshot.Name)" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "✅ All screenshots copied to: $OutputDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Rename files to match Steam requirements" -ForegroundColor White
    Write-Host "  2. Review quality (1920x1080, clear, good composition)" -ForegroundColor White
    Write-Host "  3. Upload to Steam store page" -ForegroundColor White
    
} else {
    Write-Host "❌ Screenshot directory not found: $UserDataPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Make sure you:" -ForegroundColor Yellow
    Write-Host "  1. Entered Photo Mode (P key)" -ForegroundColor White
    Write-Host "  2. Took at least one screenshot (F12 key)" -ForegroundColor White
    exit 1
}

Write-Host ""
Write-Host "🎉 Done!" -ForegroundColor Green
