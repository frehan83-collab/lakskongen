# Download Ambient and Music files from reliable CC0 sources
# Using Mixkit.co (CC0) and OpenGameArt (CC0/CC-BY)

$ffmpeg = "C:\Users\freha\AppData\Local\Temp\ffmpeg_dl\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"
$audioRoot = "salmon-sim\godot\assets\audio"
$tempDir = "$env:TEMP\lakskongen_audio"

Write-Host "Downloading Ambient Sounds from Mixkit (CC0)..." -ForegroundColor Cyan

# Mixkit has direct MP3 download links
$mixkitAmbient = @{
    "https://assets.mixkit.co/active_storage/sfx/112/112.wav" = "ocean_waves.ogg"
    "https://assets.mixkit.co/active_storage/sfx/419/419.wav" = "seagulls.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2010/2010.wav" = "wind_light.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2011/2011.wav" = "wind_strong.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2392/2392.wav" = "rain_light.ogg"
    "https://assets.mixkit.co/active_storage/sfx/26/26.wav" = "rain_heavy.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2008/2008.wav" = "storm.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2033/2033.wav" = "harbor_ambience.ogg"
    "https://assets.mixkit.co/active_storage/sfx/81/81.wav" = "underwater.ogg"
    "https://assets.mixkit.co/active_storage/sfx/2018/2018.wav" = "ice_cracking.ogg"
}

foreach ($url in $mixkitAmbient.Keys) {
    $filename = $mixkitAmbient[$url]
    $tempWav = "$tempDir\temp_$([System.IO.Path]::GetFileNameWithoutExtension($filename)).wav"
    $destOgg = "$audioRoot\ambient\$filename"
    
    try {
        Write-Host "Downloading: $filename" -ForegroundColor Yellow
        Invoke-WebRequest -Uri $url -OutFile $tempWav -UseBasicParsing -ErrorAction Stop
        
        Write-Host "Converting to OGG..." -ForegroundColor Yellow
        & $ffmpeg -i $tempWav -c:a libvorbis -q:a 5 $destOgg -y 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  SUCCESS: $filename" -ForegroundColor Green
        } else {
            Write-Host "  FAILED conversion" -ForegroundColor Red
        }
    } catch {
        Write-Host "  FAILED download: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Music files require manual download from Incompetech..." -ForegroundColor Cyan
Write-Host "Please visit: https://incompetech.com/music/royalty-free/music.html" -ForegroundColor Yellow
Write-Host "Search for calm/ambient tracks and download 6 MP3 files" -ForegroundColor Yellow
Write-Host "Place them in: $tempDir\music\" -ForegroundColor Yellow
