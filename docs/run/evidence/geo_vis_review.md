# GEO-VIS: the windowed review of geo stages 2-3

This is the integration close of `docs/geo/STAGE23_PLAN.md`, §4 steps 4-5, on `geo/int`, dated 2026-10-21. Windowed Godot ran under the owner's "you can use godot as you want now" (2026-10-20).

The frames were captured from the real GameScene by `tools/capture_screenshots.sh --scene res://qa/scenes/GeoVisCapture.tscn`, at 1280x720 and 1920x1080. The map frames ran with the UI at 100 % and 125 % (`content_scale_factor`). The game lays out a 1920x1080 canvas, so at 1280x720 and 100 % every UI text draws at two thirds of its size: this is the game's scale, not the map's.

Each frame was judged against the Kyst & Stål look and the owner references in `docs/run/evidence/geo/`. The JPEGs (1280 px wide) are in `docs/run/evidence/geo_vis/`.

The fixes were made in two commits, each with checks and killed mutants:
- `58a5b64`: the national map's badges and plate anchors;
- `7c5666a`: hav_1's route into the Hav lens, and "Nærmeste havområde".

## Frames (one line each)

### National map (po3_red_sone seed 91, day 0, PO3 picked)

| Frame | Verdict |
|---|---|
| `before_geovis_map_national_1280x720_100` | **Off, fixed.** 57 clusters. The grown badges (numerals kept at 11 screen px, C2's fix) stack over each other ('16'/'3'/'18', '61'/'4'/'73'), and the city names drown under them. |
| `geovis_map_national_1280x720_100` | Pass after `58a5b64`. 25 clusters; no badge covers another badge or a single site. Every numeral is readable except PO3's, which sits under the company's farms, and the plate stems end at the badges' rims. The city names are small (8.7 px, the UI's 1280x720 scale) and Hammerfest grazes a badge's arc. |
| `geovis_map_national_1280x720_125` | Pass. 19 clusters; the key folds into 'Tegnforklaring'. Bodø, Trondheim, Bergen and Oslo are named. PO3's badge sits under the company's two farms, the candidate star and the ring: they draw on top by design. |
| `geovis_map_national_1920x1080_100` | Pass. 47 clusters (33 badges); all 13 plate anchors lie on a badge, so the badge marks the place. A candidate star sits on PO6's '107' and the company's discs on PO3's '319' (follow-up below). |
| `geovis_map_national_1920x1080_125` | Pass. 25 clusters; Tromsø is named. The plates sit east and west of the coast with short stems, and none covers another. Candidate stars touch the numerals of '231', '190' and '254' (follow-up below). |

### Region level (PO3)

| Frame | Verdict |
|---|---|
| `geovis_map_region_1280x720_100` | Pass. The suitability heat, the constraint layers, the rivals in owner bands, the 12 stars and the two Havbruk til havs hexagons all show. The detail column holds 'Dine steder i området', the three largest owners and the ranked places; all legible at the UI's scale. |
| `geovis_map_region_1280x720_125` | Pass. The layer chips and the key sit over the north-east corner and cover no star; the readout is at the bottom right. |
| `geovis_map_region_1920x1080_100` | Pass. The breadcrumb reads 'Tilbake · Norge › PO3 Karmøy til Sotra'. The unbound hexagon at the south-west edge carries its off-map pointer. |
| `geovis_map_region_1920x1080_125` | Pass. |

### Site card (PO03-01 'Hille', the suggested place)

| Frame | Verdict |
|---|---|
| `geovis_map_site_1280x720_100` | Pass. The card shows 'Kandidat 1 av 12', score 0,944, 'Trekker mest ned: Strøm' and the weighted terms, then the place's measures. 'Bygg her' is on. The honest note is one scroll below, and the card's scrollbar shows it. |
| `geovis_map_site_1280x720_125` | Pass. The card scrolls from 'Stedet'; the breadcrumb stays clear of the card. |
| `geovis_map_site_1920x1080_100` | Pass. The crop's heat, the 2.5 km farm ring and the 5 km river ring are drawn. |
| `geovis_map_site_1920x1080_125` | Pass. |

### 'Why here' (the map's 'Bygg her' at Hille: the build dialog's Sjø step)

| Frame | Verdict |
|---|---|
| `geovis_map_why_here_1280x720_100` | Pass. The ranked list (depth, score bar, 'Trekker ned' chip) sits beside 'Hvorfor her': the binding sentence, each term's weight, points and measure, the river factor, the zone and the note. The confirm 'Bygg for 6,00 MNOK' is in view. |
| `geovis_map_why_here_1280x720_125` | Pass. The dialog fills the window; four and a half places and the binding sentence show, and the rest scrolls. |
| `geovis_map_why_here_1920x1080_100` | Pass. The Sjø card repeats 'Valgt' as a chip and as a button (F2-31's card, not geo). The diagnosis strip speaks of Land and Hav while Sjø is chosen (F2-31). |
| `geovis_map_why_here_1920x1080_125` | Pass. |

### Hav lens (forste_sesong seed 7, a year on with the research: the offshore unit may start)

| Frame | Verdict |
|---|---|
| `before_geovis_map_hav_lens_1920x1080_100` | **Off, fixed.** hav_1's 'Bygg her (hav)' opened Hav with no unit chosen: 'Velg en åpen variant', the plot step and its lens hidden. Choosing a unit then cleared hav_1. |
| `geovis_map_hav_lens_1280x720_100` | Pass after `7c5666a`. Hav opens on the OF1-class unit with 'Havområde · havområde' chosen. The lens (Indrebakken, PO 3; Tilrådd 2019; 85 km; 252 m; waves; Hs100 13,0 m; lice) and the climate note are in view. The verdict, 'Bølgene her er større enn enheten er bygget for', is honest (follow-up below). |
| `geovis_map_hav_lens_1280x720_125` | Pass. 166 of the lens's 263 px are in view, and the rest scrolls. |
| `geovis_map_hav_lens_1920x1080_100` | Pass. |
| `geovis_map_hav_lens_1920x1080_125` | Pass. |

### hav_1 at the far clamp (barekraft_mester seed 7, home PO4: hav_1 binds Indrebakken 249 km off, Z -9000)

| Frame | Verdict |
|---|---|
| `geovis_hav_far_lens_1280x720` | Pass after `7c5666a`. 'Nærmeste havområde: Indrebakken (PO 3)', 249 km, as BALANCE §1ab asks; it said 'Havområde' before. |
| `geovis_hav_far_lens_1920x1080` | Pass. |
| `geovis_hav_far_ghost_1280x720` | Pass. The OF1 ghost ring and its mooring footprint float on open sea at Z -9000, with no seabed or terrain near; distant islands stand on the horizon. |
| `geovis_hav_far_ghost_1920x1080` | Pass. |

### 3D fjord: the rivals in GetCompetitorCompanies' order (the first rival takes the far slot, nearest this camera; 620 m)

| Frame | Verdict |
|---|---|
| `geovis_rivals3d_po3_red_sone_1280x720` | Pass. Mowier ASA (57 PO3 sites), Herøy Seafood AS (45), then Greed Gregarious ASA (31), in the band colours; the labels are legible inside their 1 km range. |
| `geovis_rivals3d_po3_red_sone_1920x1080` | Pass. January haze, moderate wind. |
| `geovis_rivals3d_laksebaronene_1280x720` | Pass. SalMax Group ASA (113 PO6 sites), Offshore Optimists AS (12), then Mowier ASA (1). |
| `geovis_rivals3d_laksebaronene_1920x1080` | Pass. |
| `geovis_rivals3d_lofoten_eventyr_1280x720` | Pass. Kongsemne Havbruk AS (44 PO9 sites; the D17 rename reads well in the polar night), Mowier ASA (16), then SalMax Group ASA (2). |
| `geovis_rivals3d_lofoten_eventyr_1920x1080` | Pass. |

## Fixed in this review

1. **The national map's badges** (`58a5b64`).
   - Fix: `GeoClusterer.declutter` merges clusters that would cover each other. `GeoSiteLayer.gap_between` sets the clearance: two badges keep their discs, halos and the majors' arcs apart, and a single site keeps off a badge's disc. `MapMarker.set_cover`: a plate whose anchor lies on a badge draws no diamond, starts its stem at the badge's rim and rings the badge.
   - Checks: GeoMapReviewChecks +2, at 1920x1080 and 1280x720, 100 % and 125 %.
2. **hav_1's 'Bygg her (hav)'** (`7c5666a`).
   - Fix: the route chooses the family as the Hav card does (the first unit the company may start), then hav_1, then `BuildSiteDialog.show_plot_step`.
   - Check: GeoVisChecks.
3. **'Nærmeste havområde'** (`7c5666a`).
   - Fix: `GetGeoPlotBinding` gains the additive key `home_po`; the lens names a borrowed area with GEO_PLOT_AREA_NEAREST.
   - Check: GeoVisChecks, for PO4 and PO3.

## Follow-ups found here (not changed)

- On the national map, a candidate star or the company's farm discs can sit on a badge's numeral (PO6's '107' and PO3's '319' at 1920x1080 and 100 %; '231', '190' and '254' at 125 %). They draw on top by design; an offset or a ring would keep the number readable.
- City names are optional furniture and may still graze a badge's arc. A crowded name is left out (Tromsø at 1920x1080 and 100 %).
- hav_1's card offers 'Bygg her (hav)', but the first unit the company may start (OF1-class) cannot take hav_1's sea (Hs100 13 m). The dialog says so; the card could say first which unit hav_1 needs.
- In a home area with no Havbruk til havs area of its own (PO1, 4, 5, 7, 9, 10), hav_1's hexagon lies on another area's map. The home region's 'Dine steder' has no row for it; the route is the national hexagon (double-click), which drills into its own area.
- The guide card of the first season sits over the home area's plates when the map is opened at step 1 (`41_expansion_map`, `41_expansion_map_small`). At step 16 the inbox panel and three toasts cover the zone card's 'Bygg lokalitet' and its ring (`43_first_season_step16_build`). Both belong to U2-02 and its capture.

## Visual QA baselines: the owner's approval is needed (never promoted here)

`baseline_update` was never run; `godot/screenshots/baseline/` is unchanged.

- **`05_map_overlay`**: pHash distance 34 to the baseline of 2026-10-13 (threshold 25). The frame now shows the three-level map's national level, and the oxygen overlay's selector is below the fold of the detail column. The baseline predates waves 1-4: on the same harness run, 03_dashboard is at 30, 04_telemetry at 34 and 06_finance at 30. The owner should re-approve the set as a whole.
- **`41_expansion_*`** (8 shots, `qa/scenes/ExpansionFlowCapture.tscn`): no baseline exists. `41_expansion_map` and `41_expansion_map_small` show the new map with the first season's guide card over the home plates. `41_expansion_build_refused` shows the Sjø places and 'why here' above the preview, so the purpose in `godot/qa/shots.json` ("the preview … 8 pens drawn") now sits a scroll below.
- **`43_first_season_*`** (10 shots, `qa/scenes/FirstSeasonCapture.tscn`, 10 of 10 frames, 0 check failures): no baseline exists. In `43_first_season_step16_build` the inbox panel covers the ring on 'Bygg lokalitet' (above).
- The same harness run failed only on findings that predate this work (HANDOFF §5 item 3, handled on `wave4b/int`):
  - 08a land contrast is 0.106 against 0.12;
  - the mirror gain in shot 18;
  - the Midnattssol water is at delta E 10.0.
