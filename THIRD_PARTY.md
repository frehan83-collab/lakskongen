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
| Barlow Condensed | Display typeface (headings, KPI figures) | The Barlow Project Authors, Jeremy Tribby (github.com/jpt/barlow), via Google Fonts | SIL OFL 1.1 | `godot/assets/fonts/BarlowCondensed-Medium.ttf`, `BarlowCondensed-SemiBold.ttf`, licence in `godot/assets/fonts/OFL-BarlowCondensed.txt` |
| Kenney UI Audio | Interface and alert sounds | Kenney (kenney.nl) | CC0 1.0 | `godot/assets/audio/ui/*.wav`, `godot/assets/audio/sfx/disease_alert.wav` |
| Freesound sounds | Alert sounds | Freesound.org contributors | CC0 1.0 | `godot/assets/audio/sfx/lice_alert.wav`, `financial_crisis.wav`, `achievement_unlock.wav` |
| Mixkit sound effects | Ambient sound | Mixkit (mixkit.co) | Mixkit Sound Effects Free License | `godot/assets/audio/ambient/*.ogg` |
| Music by Kevin MacLeod | Music | Kevin MacLeod (incompetech.com) | CC BY 4.0 | `godot/assets/audio/music/*.ogg`: "Lotus", "Soaring", "Morning", "Dreamer", "Evening", "Bittersweet" |
| Kartverket map data | National and regional base maps, coastline, sea depth, place names, the baseline | Kartverket (kartverket.no / geonorge.no) | CC BY 4.0 | `godot/data/geo/basemap/*`, `coastline.national.geojson`, `suitability/*`, `candidates.json` (N1000, N250, DTM 50, depth WCS) |
| Fiskeridirektoratet map data | Aquaculture sites and areas, production areas, Havbruk til havs areas, national salmon fjords, coral closures | Fiskeridirektoratet (fiskeridir.no) | NLOD 2.0 | `godot/data/geo/localities.geojson`, `locality_areas.geojson`, `mooring_areas.geojson`, `production_areas.*`, `offshore_areas.*`, `exclusions.*` |
| Miljødirektoratet map data | Protected areas and salmon-river mouths (Naturbase, Lakseregisteret) | Miljødirektoratet (miljodirektoratet.no) | NLOD 2.0 | `godot/data/geo/exclusions.*`, `constraint_points.geojson` |
| Kystverket fairways | Main and secondary fairways | Kystverket (kystverket.no) | NLOD 2.0 | `godot/data/geo/fairways.*` |
| Havforskningsinstituttet coral reefs | Coral reefs (MAREANO) | Havforskningsinstituttet (MAREANO) (hi.no) | NLOD 2.0 | `godot/data/geo/constraint_points.geojson` |
| EMODnet Bathymetry | Sea depth where Kartverket's grid has gaps, and the sea outside Norway | EMODnet Bathymetry Consortium (emodnet-bathymetry.eu) | CC BY 4.0 | `godot/data/geo/basemap/*`, `suitability/*` |
| Copernicus Marine Service | Currents, waves and oxygen in the site suitability score | E.U. Copernicus Marine Service (marine.copernicus.eu) | Copernicus Marine Service Licence | `godot/data/geo/ocean_climatology.json`, `suitability/*`, `candidates.json` |

## Attribution notices

- **Music:** "Lotus", "Soaring", "Morning", "Dreamer", "Evening" and
  "Bittersweet" by Kevin MacLeod (incompetech.com), licensed under Creative
  Commons: By Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/).
  Converted to Ogg Vorbis; otherwise unchanged.
- **Inter:** Copyright the Inter Project Authors, under the SIL Open Font
  License 1.1 (full text in `godot/assets/fonts/OFL-Inter.txt`).
- **Barlow Condensed:** Copyright 2017 The Barlow Project Authors
  (https://github.com/jpt/barlow), under the SIL Open Font License 1.1 (full
  text in `godot/assets/fonts/OFL-BarlowCondensed.txt`). Unmodified.
- **Map data** (`godot/data/geo`, built by `tools/geodata`; every layer's
  source, licence, URL, fetch date and attribution is in
  `godot/data/geo/LICENCES.md`):
  - © Kartverket (N1000, N250, DTM 50, depth data), Creative Commons
    Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/);
    generalised and restyled, not for navigation.
  - Contains data under the Norwegian Licence for Open Government Data
    (NLOD) made available by Fiskeridirektoratet, Miljødirektoratet,
    Kystverket and Havforskningsinstituttet (https://data.norge.no/nlod/no/2.0).
  - EMODnet Bathymetry Consortium (2022): EMODnet Digital Bathymetry (DTM
    2022), https://doi.org/10.12770/ff3aff8a-cff1-44a3-a2c8-1910bf109f85,
    CC BY 4.0; resampled.
  - Generated using E.U. Copernicus Marine Service Information;
    https://doi.org/10.48670/moi-00007, https://doi.org/10.48670/moi-00006,
    https://doi.org/10.48670/moi-00008.
  - No owner or operator names are taken from the aquaculture register
    (roadmap D17).
- **Godot Engine:** Copyright (c) 2014-present Godot Engine contributors;
  Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur. MIT licence.

## Not listed here

- Tools that do not ship (FFmpeg converted the audio files).
- Textures in `godot/assets/textures` and models in `godot/assets/models` carry
  no third-party licence record; roadmap P0-08 audits their provenance before
  release, and any third-party source found is added above. The terrain
  layers in `godot/assets/textures/terrain` are generated by
  `tools/gen_terrain_textures.py` and the trees in
  `godot/assets/models/vegetation` and `godot/assets/models/shore` by
  `blender/build_vegetation.py` and `blender/build_shore_props.py`: in-house,
  no third-party images or meshes.
- The harvest stinger `godot/assets/audio/sfx/harvest_stinger.wav` (U2-05) is
  generated by `tools/gen_harvest_stinger.py` from sines and seeded noise: an
  original work of the project, no third-party sample. It replaced Kenney's
  `harvest_ready.wav`.
- Sources: `CREDITS.txt` and `godot/assets/audio/AUDIO_SOURCES.md` (audio).
