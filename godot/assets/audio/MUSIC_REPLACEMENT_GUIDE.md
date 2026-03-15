# Music Replacement Guide

**Current Status:** Placeholder sine wave files (testing only)  
**Target:** 6 proper music tracks from CC-BY/CC0 sources  
**Estimated Time:** 1-2 hours

---

## Quick Start: Incompetech Method (Recommended)

### Step 1: Visit Incompetech

1. Go to https://incompetech.com/music/royalty-free/music.html
2. Use filters: Genre = "Ambient" or "Calm"
3. Listen to previews and download 6 MP3 files

### Step 2: Download Suggested Tracks

Search for these specific tracks (or similar):

| File | Suggested Track | Search Terms | ISRC |
|------|----------------|--------------|------|
| main_theme.ogg | "With the Sea" | calm, ocean | USUAN1100008 |
| calm_sea.ogg | "Meditation" | ambient, water | - |
| spring.ogg | "Comfortable Mystery" | light, optimistic | - |
| summer.ogg | "Bright Wish" | warm, relaxed | - |
| autumn.ogg | "Meditation Impromptu 01" | reflective, mellow | - |
| winter.ogg | "Winter Chimes" | cold, sparse | USUAN1100009 |

### Step 3: Convert to OGG

**Option A: PowerShell Script (Automated)**

Save this as `convert_music.ps1` in `C:\Users\freha\AppData\Local\Temp\lakskongen_audio\music\`:

```powershell
$ffmpeg = "C:\Users\freha\AppData\Local\Temp\ffmpeg_dl\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"
$musicDir = "salmon-sim\godot\assets\audio\music"
$sourceDir = "$PSScriptRoot"

$trackNames = @("main_theme", "calm_sea", "spring", "summer", "autumn", "winter")

$mp3Files = Get-ChildItem -Path $sourceDir -Filter "*.mp3" | Select-Object -First 6

for ($i = 0; $i -lt [Math]::Min($mp3Files.Count, 6); $i++) {
    $srcFile = $mp3Files[$i]
    $destFile = "$musicDir\$($trackNames[$i]).ogg"
    
    Write-Host "Converting: $($srcFile.Name) -> $($trackNames[$i]).ogg"
    & $ffmpeg -i $srcFile.FullName -c:a libvorbis -q:a 6 -ar 44100 $destFile -y
}

Write-Host "Conversion complete!"
```

Run: `powershell -File convert_music.ps1`

**Option B: Manual FFmpeg Commands**

```bash
# Navigate to music download folder
cd C:\Users\freha\AppData\Local\Temp\lakskongen_audio\music

# Convert each file (replace with actual filenames)
ffmpeg -i "With the Sea.mp3" -c:a libvorbis -q:a 6 "..\..\..\..\salmon-sim\godot\assets\audio\music\main_theme.ogg"
ffmpeg -i "Meditation.mp3" -c:a libvorbis -q:a 6 "..\..\..\..\salmon-sim\godot\assets\audio\music\calm_sea.ogg"
# ... repeat for remaining 4 tracks
```

### Step 4: Update Attribution

Add to `salmon-sim/CREDITS.txt`:

```
MUSIC ATTRIBUTION

Tracks by Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/

- "With the Sea" (ISRC: USUAN1100008)
- "Meditation" 
- "Comfortable Mystery"
- "Bright Wish"
- "Meditation Impromptu 01"
- "Winter Chimes" (ISRC: USUAN1100009)
```

---

## Alternative Sources (If Incompetech Unavailable)

### Purple Planet Music
- URL: https://www.purple-planet.com/
- License: Free for YouTube/games with attribution
- Genres: Ambient, Calm, Ocean
- Format: MP3 download

**Suggested Albums:**
- "Water" (for main_theme, calm_sea)
- "Seasons" (for spring, summer, autumn, winter)

### Free Music Archive (FMA)
- URL: https://freemusicarchive.org/
- Filter: License = "CC BY" or "CC0"
- Search: "ambient", "calm", "seasonal"

### OpenGameArt.org
- URL: https://opengameart.org/art-search?keys=music
- Filter: Licenses = CC0, CC-BY
- Great for game-specific ambient music

---

## Music Selection Guidelines

### Duration
- **Minimum:** 2 minutes (for proper looping)
- **Optimal:** 3-5 minutes (avoids repetition)
- **Maximum:** 10 minutes (file size consideration)

### Style Requirements

**main_theme:**
- Calm, reflective
- Represents the game's overall mood
- Suitable for main menu and pause screens
- Instruments: Piano, ambient pads, gentle strings

**calm_sea:**
- Ocean/water theme
- Gentle, flowing
- Can include water sounds or wave-like textures
- Suitable for calm gameplay periods

**spring:**
- Light, optimistic
- New beginnings, growth
- Higher register, bright tones
- Instruments: Light piano, flute, acoustic guitar

**summer:**
- Warm, energetic (but not aggressive)
- Peak activity season
- Bright, major key
- Instruments: Upbeat piano, light percussion

**autumn:**
- Mellow, reflective
- Harvest theme, winding down
- Warm but slightly melancholic
- Instruments: Acoustic guitar, cello, soft piano

**winter:**
- Cold, sparse
- Minimal, space between notes
- Can include wind chime sounds
- Instruments: Bells, piano, ambient drones

### Technical Requirements

- **Format:** OGG Vorbis (converted from MP3/WAV)
- **Sample Rate:** 44.1kHz
- **Channels:** Stereo
- **Quality:** VBR q6 (good quality, reasonable file size)
- **Loop:** Should loop seamlessly (check for clicks/pops)

### Audio Quality Checklist

- [ ] No clipping or distortion
- [ ] Consistent volume across all tracks
- [ ] Seamless loop points (no clicks)
- [ ] Stereo width appropriate (not too wide)
- [ ] Peak normalized to -1dB or -2dB

---

## Testing After Replacement

1. **Godot Import:**
   - Open Godot project
   - Project > Reload Current Project
   - Check Output console for import errors

2. **Loop Point Test:**
   - Play each track in Godot audio preview
   - Let it loop 3-4 times
   - Listen for clicks or abrupt transitions

3. **In-Game Test:**
   - Trigger each music track in AudioController
   - Verify smooth transitions between tracks
   - Test volume levels relative to SFX/ambient

4. **Attribution Verification:**
   - Ensure CREDITS.txt includes all required attribution
   - Verify license compliance (CC-BY requires attribution)

---

## FFmpeg Quality Settings Explained

```bash
ffmpeg -i input.mp3 -c:a libvorbis -q:a 6 -ar 44100 output.ogg
```

- `-c:a libvorbis`: Use Vorbis codec (OGG format)
- `-q:a 6`: Quality level 6 (VBR, ~160-192 kbps equivalent)
  - q0 = highest quality (~500 kbps)
  - q6 = good quality (~160 kbps) **[RECOMMENDED]**
  - q10 = lowest quality (~80 kbps)
- `-ar 44100`: Resample to 44.1kHz (CD quality)

**File Size Estimates (per 3-minute track):**
- q4 (very high): ~7-9 MB
- q6 (recommended): ~4-6 MB
- q8 (acceptable): ~2-3 MB

---

## Quick Reference Commands

### Convert Single File
```powershell
$ffmpeg = "C:\Users\freha\AppData\Local\Temp\ffmpeg_dl\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"
& $ffmpeg -i "input.mp3" -c:a libvorbis -q:a 6 "output.ogg"
```

### Batch Convert All MP3s in Folder
```powershell
Get-ChildItem -Filter "*.mp3" | ForEach-Object {
    $dest = [System.IO.Path]::ChangeExtension($_.FullName, ".ogg")
    & $ffmpeg -i $_.FullName -c:a libvorbis -q:a 6 $dest
}
```

### Check OGG File Info
```powershell
& $ffmpeg -i "music_file.ogg" 2>&1 | Select-String "Duration|Stream"
```

---

## Troubleshooting

### Issue: Music doesn't loop smoothly
**Solution:** Add fade in/out at loop points with FFmpeg:
```bash
ffmpeg -i input.ogg -af "afade=t=in:st=0:d=0.5,afade=t=out:st=179.5:d=0.5" output.ogg
```

### Issue: File size too large
**Solution:** Lower quality setting to q7 or q8:
```bash
ffmpeg -i input.mp3 -c:a libvorbis -q:a 7 output.ogg
```

### Issue: Volume inconsistent between tracks
**Solution:** Normalize with loudnorm filter:
```bash
ffmpeg -i input.mp3 -af loudnorm -c:a libvorbis -q:a 6 output.ogg
```

---

**Last Updated:** 2026-03-15  
**Status:** Placeholder music active, replacement guide ready
