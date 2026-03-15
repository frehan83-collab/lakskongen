# T69: Audio Asset Download and Conversion Script
# Lakskongen - Salmon Farming Simulator

$ErrorActionPreference = "Continue"
$ffmpegPath = "C:\Users\freha\AppData\Local\Temp\ffmpeg_dl\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"
$audioRoot = $PSScriptRoot
$tempDownload = "$env:TEMP\lakskongen_audio"

New-Item -ItemType Directory -Force -Path $tempDownload | Out-Null

Write-Host "=== T69: Audio Asset Integration ===" -ForegroundColor Cyan
Write-Host ""

$downloadCount = 0
$convertCount = 0
$failCount = 0

function Download-File {
    param($url, $output)
    try {
        Write-Host "Downloading: $([System.IO.Path]::GetFileName($output))" -ForegroundColor Yellow
        Invoke-WebRequest -Uri $url -OutFile $output -UseBasicParsing -ErrorAction Stop
        $script:downloadCount++
        return $true
    } catch {
        Write-Host "FAILED: $_" -ForegroundColor Red
        $script:failCount++
        return $false
    }
}

function Convert-ToOgg {
    param($input, $output)
    try {
        Write-Host "Converting to OGG: $([System.IO.Path]::GetFileName($output))" -ForegroundColor Yellow
        & $ffmpegPath -i $input -c:a libvorbis -q:a 5 $output -y 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $script:convertCount++
            return $true
        }
        $script:failCount++
        return $false
    } catch {
        Write-Host "FAILED: $_" -ForegroundColor Red
        $script:failCount++
        return $false
    }
}

function Convert-ToWav {
    param($input, $output)
    try {
        Write-Host "Converting to WAV: $([System.IO.Path]::GetFileName($output))" -ForegroundColor Yellow
        & $ffmpegPath -i $input -acodec pcm_s16le -ar 44100 $output -y 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $script:convertCount++
            return $true
        }
        $script:failCount++
        return $false
    } catch {
        Write-Host "FAILED: $_" -ForegroundColor Red
        $script:failCount++
        return $false
    }
}

# PHASE 1: UI SOUNDS
Write-Host "[1/4] UI SOUNDS (6 files)" -ForegroundColor Green

$kenneyZip = "$tempDownload\kenney_ui-audio.zip"
$kenneyExtract = "$tempDownload\kenney_ui"

if (Download-File "https://kenney.nl/media/pages/assets/ui-audio/e19c9b1814-1677590494/kenney_ui-audio.zip" $kenneyZip) {
    Write-Host "Extracting Kenney UI pack..." -ForegroundColor Yellow
    Expand-Archive -Path $kenneyZip -DestinationPath $kenneyExtract -Force
    
    $uiMapping = @{
        "click1.wav" = "button_click.wav"
        "switch1.wav" = "button_hover.wav"
        "confirmation_001.ogg" = "notification.wav"
        "open_001.ogg" = "panel_open.wav"
        "close_001.ogg" = "panel_close.wav"
        "error_007.ogg" = "error.wav"
    }
    
    foreach ($srcFile in $uiMapping.Keys) {
        $destFile = $uiMapping[$srcFile]
        $srcPath = Get-ChildItem -Path $kenneyExtract -Recurse -Filter $srcFile -ErrorAction SilentlyContinue | Select-Object -First 1
        
        if ($srcPath) {
            $destPath = "$audioRoot\ui\$destFile"
            if ($srcFile.EndsWith(".ogg")) {
                Convert-ToWav $srcPath.FullName $destPath | Out-Null
            } else {
                Copy-Item $srcPath.FullName $destPath -Force
                Write-Host "Copied: $destFile" -ForegroundColor Green
            }
        } else {
            Write-Host "WARNING: $srcFile not found, using alternative..." -ForegroundColor Yellow
            $altFile = Get-ChildItem -Path $kenneyExtract -Recurse -Filter "*.wav" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($altFile) {
                Copy-Item $altFile.FullName "$audioRoot\ui\$destFile" -Force
                Write-Host "Used alternative: $destFile" -ForegroundColor Green
            }
        }
    }
}

# PHASE 2: ALERT SFX
Write-Host ""
Write-Host "[2/4] ALERT SFX (5 files)" -ForegroundColor Green

$alertSounds = @{
    "https://freesound.org/data/previews/320/320655_5260872-lq.mp3" = "lice_alert.wav"
    "https://freesound.org/data/previews/521/521660_8070608-lq.mp3" = "disease_alert.wav"
    "https://freesound.org/data/previews/171/171671_2394245-lq.mp3" = "harvest_ready.wav"
    "https://freesound.org/data/previews/415/415209_5121236-lq.mp3" = "financial_crisis.wav"
    "https://freesound.org/data/previews/270/270304_5123851-lq.mp3" = "achievement_unlock.wav"
}

foreach ($url in $alertSounds.Keys) {
    $filename = $alertSounds[$url]
    $tempMp3 = "$tempDownload\$([System.IO.Path]::GetFileNameWithoutExtension($filename)).mp3"
    $destWav = "$audioRoot\sfx\$filename"
    
    if (Download-File $url $tempMp3) {
        Convert-ToWav $tempMp3 $destWav | Out-Null
    }
}

# PHASE 3: MUSIC
Write-Host ""
Write-Host "[3/4] MUSIC (6 files)" -ForegroundColor Green
Write-Host "Note: Manual download required from Incompetech.com" -ForegroundColor Yellow
Write-Host "Visit: https://incompetech.com/music/royalty-free/music.html" -ForegroundColor Yellow

# Create music directory for manual placement
New-Item -ItemType Directory -Force -Path "$tempDownload\music" | Out-Null

$musicTempDir = "$tempDownload\music"
if (Test-Path $musicTempDir) {
    $mp3Files = Get-ChildItem -Path $musicTempDir -Filter "*.mp3" -ErrorAction SilentlyContinue
    
    if ($mp3Files.Count -gt 0) {
        Write-Host "Found $($mp3Files.Count) music file(s) for conversion" -ForegroundColor Green
        
        $trackNames = @("main_theme", "calm_sea", "spring", "summer", "autumn", "winter")
        for ($i = 0; $i -lt [Math]::Min($mp3Files.Count, 6); $i++) {
            $srcFile = $mp3Files[$i]
            $destOgg = "$audioRoot\music\$($trackNames[$i]).ogg"
            Convert-ToOgg $srcFile.FullName $destOgg | Out-Null
        }
    } else {
        Write-Host "No music files found. Please download manually." -ForegroundColor Yellow
    }
}

# PHASE 4: AMBIENT
Write-Host ""
Write-Host "[4/4] AMBIENT SOUNDS (10 files)" -ForegroundColor Green

$ambientSounds = @{
    "https://freesound.org/data/previews/197/197714_1015240-lq.mp3" = "ocean_waves.ogg"
    "https://freesound.org/data/previews/466/466772_5674468-lq.mp3" = "seagulls.ogg"
    "https://freesound.org/data/previews/387/387468_4890395-lq.mp3" = "wind_light.ogg"
    "https://freesound.org/data/previews/456/456973_3088389-lq.mp3" = "wind_strong.ogg"
    "https://freesound.org/data/previews/390/390618_3179483-lq.mp3" = "rain_light.ogg"
    "https://freesound.org/data/previews/263/263026_3905909-lq.mp3" = "rain_heavy.ogg"
    "https://freesound.org/data/previews/442/442774_5674468-lq.mp3" = "storm.ogg"
    "https://freesound.org/data/previews/353/353927_5121236-lq.mp3" = "harbor_ambience.ogg"
    "https://freesound.org/data/previews/366/366159_5858296-lq.mp3" = "underwater.ogg"
    "https://freesound.org/data/previews/146/146419_2481301-lq.mp3" = "ice_cracking.ogg"
}

foreach ($url in $ambientSounds.Keys) {
    $filename = $ambientSounds[$url]
    $tempMp3 = "$tempDownload\$([System.IO.Path]::GetFileNameWithoutExtension($filename)).mp3"
    $destOgg = "$audioRoot\ambient\$filename"
    
    if (Download-File $url $tempMp3) {
        Convert-ToOgg $tempMp3 $destOgg | Out-Null
    }
}

# SUMMARY
Write-Host ""
Write-Host "=== INTEGRATION SUMMARY ===" -ForegroundColor Cyan

$uiCount = (Get-ChildItem "$audioRoot\ui" -Filter "*.wav" -ErrorAction SilentlyContinue).Count
$sfxCount = (Get-ChildItem "$audioRoot\sfx" -Filter "*.wav" -ErrorAction SilentlyContinue).Count
$musicCount = (Get-ChildItem "$audioRoot\music" -Filter "*.ogg" -ErrorAction SilentlyContinue).Count
$ambientCount = (Get-ChildItem "$audioRoot\ambient" -Filter "*.ogg" -ErrorAction SilentlyContinue).Count
$totalFiles = $uiCount + $sfxCount + $musicCount + $ambientCount

Write-Host "UI Sounds:     $uiCount / 6"
Write-Host "Alert SFX:     $sfxCount / 5"
Write-Host "Music:         $musicCount / 6"
Write-Host "Ambient:       $ambientCount / 10"
Write-Host ""
Write-Host "Total:         $totalFiles / 27"
Write-Host ""
Write-Host "Downloads:     $downloadCount"
Write-Host "Conversions:   $convertCount"
Write-Host "Failures:      $failCount"

if ($totalFiles -eq 27) {
    Write-Host ""
    Write-Host "SUCCESS: All 27 audio files integrated!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "INCOMPLETE: Manual intervention required" -ForegroundColor Yellow
}
