# Audio sources and licences

Every audio file the game ships, where it comes from and under which licence
(roadmap Au2-01, D24). The machine-readable record is
`audio_manifest.json` beside this file: one row per file with its kind,
duration, channels, level (ITU-R BS.1770: integrated and momentary-maximum
LUFS, true peak in dBTP; an Ogg is measured as decoded) and SHA-256.
`tests/test_audio_floor.py` is the CI audio check: no file the manifest does
not list, no listed file missing, no ambience loop under 30 s, the recorded
hash and duration are the file's, every licence is recorded here, in
`THIRD_PARTY.md` and in `godot/data/credits.json`, and every path the game's
catalogue (`scripts/MusicLibrary.gd`) names exists.

This file replaces the T65 sourcing guide (2026-03-15), which listed where
sounds could be found rather than what shipped; its ambience entries
(Mixkit or Freesound, "search ...") never named the files' real sources.
Au2-01 replaced every one of those ambience files with original ones.

## Licences in use

| Licence | Files | Recorded in |
|---|---|---|
| Original work of the project (no third-party material) | the ambience loops `ambient/*.ogg`, the verb cues `sfx/verb_*.wav`, `sfx/harvest_stinger.wav` | THIRD_PARTY.md "Not listed here"; credits.json team "UI_CREDITS_ROLE_SOUND" |
| CC0 1.0 (Kenney UI Audio, kenney.nl) | `ui/*.wav`, `sfx/disease_alert.wav` | THIRD_PARTY.md row "Kenney UI Audio"; credits.json |
| CC0 1.0 (Freesound.org contributors) | `sfx/lice_alert.wav`, `sfx/financial_crisis.wav`, `sfx/achievement_unlock.wav` | THIRD_PARTY.md row "Freesound sounds"; credits.json |
| CC BY 4.0 (Kevin MacLeod, incompetech.com) | `music/*.ogg` | THIRD_PARTY.md row "Music by Kevin MacLeod" with its notice; credits.json with UI_CREDITS_MUSIC_NOTICE |

Provenance gap (unchanged by Au2-01, for the P0-08 audit): the three
Freesound alerts carry CC0 by the T65 record, but no Freesound sound id or
author per file was kept. Replace them with generated cues, or find and
record their ids, before release.

## How the generated sounds are made

`tools/audio/gen_audio.py` builds every original file from seeded numpy
generators (`tools/audio/ambience.py`, `tools/audio/cues.py`,
`tools/audio/audio_dsp.py`); `python3 tools/audio/gen_audio.py --check`
rebuilds them in a temporary directory and compares bytes (16 of 16
identical on 2026-10-20). The loops are encoded to Ogg Vorbis (112 kbit/s
stereo, 80 kbit/s mono) by Blender 5.2's Audaspace (`tools/audio/blender_ogg.py`:
no other Ogg encoder is installed); FFmpeg's muxer picks a random stream
serial, which the script rewrites to a fixed one so a rebuild is
byte-identical. Every loop is exactly periodic (noise coloured in the
frequency domain, modulation in whole cycles per loop, events placed on a
circle), so it repeats without a seam.

Nobody listens in the pipeline, so levels are measured, not judged by ear:

- Ambience loops: -24 LUFS integrated, true peak at most -1 dBTP. -24 LUFS
  (+-2 LU) is the IESD mix reference for console games and -1 dBTP the
  EBU R 128 production ceiling: IESD, "Mix reference levels" v03.02 (2015),
  https://www.audiogang.org/wp-content/uploads/2015/04/IESD-Mix-Ref-Levels-v03.02.pdf ;
  EBU R 128 v5.0 (2023), https://tech.ebu.ch/publications/r128 . The game's
  AmbienceMixer sets each layer's level relative to that.
- Verb cues: -19 LUFS momentary maximum (400 ms), the harvest stinger's own
  level, so every verb's payoff reads alike; true peak at most -3 dBTP.
  For comparison the Kenney UI cues measure -19 to -24 LUFS momentary.
- The meter (`audio_dsp.py`) reproduces EBU Tech 3341's first case: a
  1 kHz sine at -23 dBFS in both channels reads -23.0 LUFS.

What each generated sound models (sources):

- Fjord wind: band-limited noise in six bands whose levels follow a gust
  curve (0.02-0.2 Hz), the upper bands rising more in a gust; the gale adds
  three narrow whistles (380, 540, 720 Hz) and buffeting below 70 Hz.
- Water lap: small waves slapping pen floats and a hull, clustered on the
  swell (4-12 s periods), each a short splash of noise with now and then a
  bubble "glug" (a damped sine at 320-900 Hz rising as the bubble surfaces).
- Gulls: herring-gull-like calls, a long first note and a run of shorter
  ones, or a "kek-kek" alarm, as harmonic voices whose first harmonic note
  sits around 1-1.5 kHz (yellow-legged and Armenian gull long calls peak at
  1.05-1.47 kHz: gull-research.org, https://gull-research.org/armenicus/5cy/lh23.html),
  from 30 to 250 m away (air absorption and level by distance).
- Barge engine (`feed_barge.ogg`) and boat engine (`boat_engine.ogg`): a
  four-stroke diesel fires at rpm x cylinders / 120 per second (a two-stroke
  at rpm x cylinders / 60: Kozaczka, Hydroacoustics vol. 3,
  https://yadda.icm.edu.pl/baztech/element/bwmeta1.element.baztech-858b6b42-d1dd-4aa7-9886-f936773ca8d3/c/Kozaczka_hydroacoustics-vol3.pdf ).
  The feed barge's generator set: 6 cylinders at 1500 rpm (75 Hz, a 50 Hz
  grid set), its feed blower's blade-pass tone at 580 Hz and pellets
  rattling through the pipes in dosing rounds. The wellboat: 8 cylinders at
  900 rpm (60 Hz), a 4-blade propeller on a 200 rpm shaft (13.3 Hz) and the
  bow wash. Both mono, for the 3D emitters on the boats.
- Rain on the water: every drop an impact click; larger drops add a bubble
  "plink" at 2-10 kHz and small ones a faint ring near 15 kHz (Medwin et al.,
  via NASA Earth Observatory, https://earthobservatory.nasa.gov/features/Rain/rain_2.php ),
  over a hiss bed; light rain 450 drops/s, heavy 2400 drops/s.
- Storm: breaking waves every 6-11 s (a rising crest, a crash, foam), the
  sea's roar and the gale.
- Harbour: water at the pilings, a boat idling across the harbour, mooring
  lines working (friction tones gliding in pitch), a far gull now and then.
- Underwater: a low rumble on the swell, the muffled wash above, bubble
  streams, a far engine's hum.
- Verb cues: stocking (the delivery hose's rush, fish splashing in), the
  feeding plan (a lever's tick, the blower spinning up, pellets, a soft
  two-note tag), delousing (the treatment pump, rushing water, bubbles, a
  soft tag), the auction's verdict (two gavel strikes on a sounding block in
  a hall), the storm warning (distant thunder rolling, a rising gust).

## Every file

| File | Kind | Length | LUFS I / M max / dBTP | Licence | Source | What it is |
|---|---|---|---|---|---|---|
| `ambient/boat_engine.ogg` | loop | 60.00 s, 1 ch | -24.1 / -23.1 / -9.2 | Original work of the project | generated: tools/audio/gen_audio.py (seed 402) | boat engine: a wellboat's main engine, propeller and bow wash (3D emitter) |
| `ambient/feed_barge.ogg` | loop | 60.00 s, 1 ch | -24.1 / -23.2 / -9.1 | Original work of the project | generated: tools/audio/gen_audio.py (seed 401) | barge engine: the feed barge's generator set, feed blower and pellets (3D emitter) |
| `ambient/fjord_wind_light.ogg` | loop | 60.00 s, 2 ch | -24.1 / -18.9 / -8.7 | Original work of the project | generated: tools/audio/gen_audio.py (seed 101) | fjord wind: a breeze, gusting softly |
| `ambient/fjord_wind_strong.ogg` | loop | 60.00 s, 2 ch | -24.1 / -15.0 / -5.5 | Original work of the project | generated: tools/audio/gen_audio.py (seed 102) | fjord wind: a gale, with whistles and buffeting |
| `ambient/gulls.ogg` | loop | 60.00 s, 2 ch | -24.2 / -18.6 / -7.6 | Original work of the project | generated: tools/audio/gen_audio.py (seed 301) | gulls: herring-gull-like calls, near and far |
| `ambient/harbor_ambience.ogg` | loop | 60.00 s, 2 ch | -24.2 / -15.8 / -3.5 | Original work of the project | generated: tools/audio/gen_audio.py (seed 701) | harbour: water at the pilings, an idling boat, mooring lines, a far gull |
| `ambient/rain_heavy.ogg` | loop | 60.00 s, 2 ch | -24.0 / -21.6 / -10.6 | Original work of the project | generated: tools/audio/gen_audio.py (seed 502) | rain: heavy rain on the water |
| `ambient/rain_light.ogg` | loop | 60.00 s, 2 ch | -24.2 / -21.8 / -3.0 | Original work of the project | generated: tools/audio/gen_audio.py (seed 501) | rain: light rain on the water |
| `ambient/storm.ogg` | loop | 60.00 s, 2 ch | -24.2 / -19.2 / -9.3 | Original work of the project | generated: tools/audio/gen_audio.py (seed 601) | storm: breaking waves, the sea's roar and a gale |
| `ambient/underwater.ogg` | loop | 60.00 s, 2 ch | -24.1 / -20.4 / -9.3 | Original work of the project | generated: tools/audio/gen_audio.py (seed 801) | underwater: low rumble, the muffled wash above, bubble streams |
| `ambient/water_lap.ogg` | loop | 60.00 s, 2 ch | -24.3 / -17.3 / -3.7 | Original work of the project | generated: tools/audio/gen_audio.py (seed 201) | water lap: small waves at pen floats and a hull on a calm day |
| `music/autumn.ogg` | music | 186.25 s, 2 ch | -13.3 / -9.3 / -0.9 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | autumn |
| `music/calm_sea.ogg` | music | 380.06 s, 2 ch | -17.3 / -10.6 / -1.8 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | calm sea |
| `music/main_theme.ogg` | music | 328.31 s, 2 ch | -17.7 / -13.8 / -0.2 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | main theme (menu) |
| `music/spring.ogg` | music | 153.31 s, 2 ch | -18.0 / -13.5 / -4.3 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | spring |
| `music/summer.ogg` | music | 204.07 s, 2 ch | -19.3 / -11.5 / -1.4 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | summer |
| `music/winter.ogg` | music | 202.06 s, 2 ch | -16.2 / -11.0 / -2.6 | CC BY 4.0 | third party: Music by Kevin MacLeod (THIRD_PARTY.md) | winter |
| `sfx/achievement_unlock.wav` | cue | 0.62 s, 1 ch | -24.0 / -22.0 / -1.0 | CC0 1.0 | third party: Freesound sounds (THIRD_PARTY.md) | achievement (medal) |
| `sfx/disease_alert.wav` | cue | 0.51 s, 2 ch | -22.8 / -22.8 / -1.2 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | alert: disease outbreak |
| `sfx/financial_crisis.wav` | cue | 1.79 s, 2 ch | -14.3 / -11.7 / -2.0 | CC0 1.0 | third party: Freesound sounds (THIRD_PARTY.md) | alert: market crash |
| `sfx/harvest_stinger.wav` | cue | 1.90 s, 2 ch | -22.1 / -19.0 / -11.3 | Original work of the project | generated: tools/gen_harvest_stinger.py | verb cue: the harvest's payoff stinger |
| `sfx/lice_alert.wav` | cue | 1.10 s, 2 ch | -13.2 / -10.8 / -1.9 | CC0 1.0 | third party: Freesound sounds (THIRD_PARTY.md) | alert: lice warning |
| `sfx/verb_auction.wav` | cue | 1.30 s, 2 ch | -21.7 / -19.0 / -7.2 | Original work of the project | generated: tools/audio/gen_audio.py (seed 1104) | verb cue: the auctioneer's gavel |
| `sfx/verb_delouse.wav` | cue | 2.30 s, 2 ch | -20.1 / -19.0 / -9.9 | Original work of the project | generated: tools/audio/gen_audio.py (seed 1103) | verb cue: a delousing treatment started |
| `sfx/verb_feed_plan.wav` | cue | 1.90 s, 2 ch | -21.0 / -19.0 / -11.0 | Original work of the project | generated: tools/audio/gen_audio.py (seed 1102) | verb cue: a feeding plan set (blower, pellets, a tag) |
| `sfx/verb_stock.wav` | cue | 1.90 s, 2 ch | -20.4 / -19.0 / -9.3 | Original work of the project | generated: tools/audio/gen_audio.py (seed 1101) | verb cue: smolts pumped into a pen |
| `sfx/verb_storm.wav` | cue | 3.80 s, 2 ch | -23.5 / -19.0 / -8.5 | Original work of the project | generated: tools/audio/gen_audio.py (seed 1105) | verb cue: distant thunder and a rising gust |
| `ui/button_click.wav` | ui | 0.09 s, 2 ch | -20.8 / -20.8 / -1.4 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | the global button click (every Button press) |
| `ui/button_hover.wav` | ui | 0.22 s, 2 ch | -20.3 / -20.3 / -0.0 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | button hover |
| `ui/error.wav` | ui | 0.20 s, 2 ch | -19.1 / -19.1 / -0.6 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | error cue (critical event) |
| `ui/notification.wav` | ui | 0.33 s, 2 ch | -23.3 / -23.3 / -0.6 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | notification cue (warning event) |
| `ui/panel_close.wav` | ui | 0.25 s, 2 ch | -20.9 / -20.9 / 0.3 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | panel close |
| `ui/panel_open.wav` | ui | 0.39 s, 2 ch | -24.0 / -24.0 / -0.8 | CC0 1.0 | third party: Kenney UI Audio (THIRD_PARTY.md) | panel open |

The game plays them through `scripts/MusicLibrary.gd` (the catalogue),
`scripts/AudioController.gd` (music, cues, the UI's click on every button,
the verb cues, the engines as 3D emitters) and
`scripts/audio/AmbienceMixer.gd` (the ambience bed from the weather and the
camera on screen).
