# Third-party material in Lakskongen

Every third-party file or library that ships with the game, with its licence.
The in-game credits (Medvirkende, reachable from the main menu and the pause
menu) list every row below; `godot/data/credits.json` is their data, and
`tests/test_credits.py` fails when a row here has no credits entry with the
same licence.

The game itself is proprietary (roadmap D24). Mod schemas, mod documentation
and the example mod pack are MIT.

| Component | Used for | Author / source | Licence | Files |
|---|---|---|---|---|
| Godot Engine | Game engine | Juan Linietsky, Ariel Manzur and Godot Engine contributors (godotengine.org) | MIT | the engine runtime in every export |
| .NET runtime | C# runtime | .NET Foundation and contributors (dotnet.microsoft.com) | MIT | the runtime bundled with C# exports |
| Inter | UI typeface | The Inter Project Authors, Rasmus Andersson (rsms.me/inter) | SIL OFL 1.1 | `godot/assets/fonts/Inter-*.otf`, licence in `godot/assets/fonts/OFL-Inter.txt` |
| Kenney UI Audio | Interface and alert sounds | Kenney (kenney.nl) | CC0 1.0 | `godot/assets/audio/ui/*.wav`, `godot/assets/audio/sfx/disease_alert.wav`, `godot/assets/audio/sfx/harvest_ready.wav` |
| Freesound sounds | Alert sounds | Freesound.org contributors | CC0 1.0 | `godot/assets/audio/sfx/lice_alert.wav`, `financial_crisis.wav`, `achievement_unlock.wav` |
| Mixkit sound effects | Ambient sound | Mixkit (mixkit.co) | Mixkit Sound Effects Free License | `godot/assets/audio/ambient/*.ogg` |
| Music by Kevin MacLeod | Music | Kevin MacLeod (incompetech.com) | CC BY 4.0 | `godot/assets/audio/music/*.ogg`: "Lotus", "Soaring", "Morning", "Dreamer", "Evening", "Bittersweet" |

## Attribution notices

- **Music:** "Lotus", "Soaring", "Morning", "Dreamer", "Evening" and
  "Bittersweet" by Kevin MacLeod (incompetech.com), licensed under Creative
  Commons: By Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/).
  Converted to Ogg Vorbis; otherwise unchanged.
- **Inter:** Copyright the Inter Project Authors, under the SIL Open Font
  License 1.1 (full text in `godot/assets/fonts/OFL-Inter.txt`).
- **Godot Engine:** Copyright (c) 2014-present Godot Engine contributors;
  Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur. MIT licence.

## Not listed here

- Tools that do not ship (FFmpeg converted the audio files).
- Textures in `godot/assets/textures` and models in `godot/assets/models` carry
  no third-party licence record; roadmap P0-08 audits their provenance before
  release, and any third-party source found is added above.
- Sources: `CREDITS.txt` and `godot/assets/audio/AUDIO_SOURCES.md` (audio).
