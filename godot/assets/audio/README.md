# Lakskongen audio

What ships, where it comes from and how loud it is:

- `AUDIO_SOURCES.md`: every file with its licence, source and level, the
  level targets with their sources, and what each generated sound models.
- `audio_manifest.json`: the same, machine-readable (kind, length, channels,
  BS.1770 loudness and true peak, SHA-256, licence, source). CI reads it:
  `tests/test_audio_floor.py` (no unlisted or missing file, no loop under
  30 s, every licence recorded in AUDIO_SOURCES.md, THIRD_PARTY.md and
  `godot/data/credits.json`).

```
audio/
├── ambient/  60 s ambience loops, generated (tools/audio/gen_audio.py)
├── sfx/      verb cues (generated), the harvest stinger (generated), alerts (Kenney, Freesound CC0)
├── ui/       UI cues (Kenney CC0); the click plays on every Button
└── music/    Kevin MacLeod, CC BY 4.0 (notice in THIRD_PARTY.md and the credits)
```

To add or change a sound: generate it with `python3 tools/audio/gen_audio.py`
(needs Blender 5.2 for the Ogg step; `--check` rebuilds and compares bytes),
or, for a third-party file, add it to `THIRD_PARTY_FILES` in that script with a
THIRD_PARTY.md row and a credits.json entry, then re-run it to refresh the
manifest. Name it in `scripts/MusicLibrary.gd`.

The game plays audio through `scripts/AudioController.gd` (music, cues, the
UI click, verb cues, the engines as 3D emitters) and
`scripts/audio/AmbienceMixer.gd` (the ambience bed from the weather and the
camera on screen).

The T65/T69 documents and the `download_*.ps1` scripts in this folder are
history from before Au2-01 (2026-03): they describe files that no longer ship.
Do not use the download scripts; anything they fetch would be unrecorded, and
the CI audio check fails on an unlisted file.
