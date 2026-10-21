# Wave 4b frame review (integration close, 2026-10-21)

The close's look at every frame: each shot in `godot/qa/shots.json` (115 shots, 9 QA
scenes) and, beside them, the default screenshot harness (49 frames, `ScreenshotHarness`,
not in `shots.json`). One line per shot: what is good, what is off. Judged against the
owner's references (refs 2 and 3 on disk; refs 1, 4, 5, L1, L2 and O1-O3 through their
descriptions in `docs/art/OWNER_REFERENCE_BRIEF.md`).

- **Tree:** `wave4b/int` at `c86851d` (all five wave-4b merges). The close's only code change,
  the land-kit preview's carve (`6bb9ff2`, finding 1), was captured again on its own tree.
- **Capture:** windowed Godot 4.7.2 on the M1 Pro at 1920x1080 (the 41/42 `_small` shots at
  1280x720), 2026-10-21 08:49-09:00, one run at a time, load average 3.7-8.7:
  `bash tools/capture_screenshots.sh` (the harness) and
  `bash tools/capture_screenshots.sh --scene res://qa/scenes/<Scene>.tscn` for each QA scene;
  `ShowcaseCapture` twice, the second with `-- --only 34_teaser` (its scenario is
  `forste_sesong`). Every run exited 0 except the harness (exit 1: 08a only, below).
- **Looked at:** every frame at 1280 px, the findings at full resolution.
- **Evidence:** `docs/run/evidence/wave4b_frame_review/`: five contact sheets (every frame,
  in shot order; the land kit's after the fix), the fix's before/after and three crops of the
  findings. The full frames are not committed.

## Measured on the frames

- **Harness content checks:** one failure, `08a_fjord_live_sync` land RMS contrast 0.107 < 0.12
  (open since gfx-polish-1, RUN_LOG §8). Moods: closest pHash pairs 10 bits (min 9), no
  failure. 18: mirror SSIM 0.646, structure 0.399 against 0.044 for the no-mirror twin (gain
  0.355, bar 0.2). 09: aurora 10.4 % of the frame, mirrored on 2.7 %. 19: foam on 19.5 % of
  the pixels within 5 m of the shore, 0 % at 15-40 m. Under water: fish 1.34 % of shot 11; the
  dive within 4.74 levels of the settled view, steps of at most 2.99; the fog's ghosts p99 1.43,
  the float 0.46; 11d's caustics move 0.0084 (clean), 0.0088 (fouled), 0 held off. The run
  printed the 12 known 'indexing did not unpair geometries from light' engine errors.
- **QA metrics against their tests:** the fresh `f2_20_facilities_metrics.json` and the merged
  `f2_23_showcase_metrics.json` (the full run's shots, the teaser run's four) pass
  `tests/test_facility_presenter.py` and `tests/test_showcase_shots.py` (28 passed); the fresh
  `36_graybox_metrics.json` and `20_moods.json` pass `tests/test_farm_type_graybox.py` and
  `tests/test_moods.py` (36 passed), each copied over the committed file for the run and
  restored after, and so do the fresh `40_offshore_metrics.json` (`tests/test_offshore_assets.py`,
  39 passed). They match the committed measurements within run-to-run noise (shot 33's hull
  share 1.552 % against the committed 1.551 %), so the committed files stand. The fresh
  `39_landkit_metrics.json` (before and after the fix alike) fails
  `test_land_framing_reads_dark_lids_on_pale_walls` at dusk: the sample tank's lid/wall dL is
  20.1 against F2-25's bar of 30 (committed: 30.65). The committed file predates F2-23's lid
  work-lights, which light the lid (its L 11.95 -> 19.86), and gfx-polish-1's sun (the dusk
  sun at -0.98 deg, was 0.23); by day dL is 32.05 (bar met). The committed file stands; RUN_LOG
  §8 records the drift (finding 10).
- **Run logs:** no SCRIPT ERROR in any run. `LandKitPreview` logged 8 "Invalid array format for
  surface" errors (fixed, below); `ExpansionFlowCapture` logged one "Lambda capture at index 0
  was freed" from `AudioStreamCache._deliver` (finding 9). The rest are exit-time leak reports.

## What reads well

- **Land (refs L1, L2):** the 4-lot campus on pad_a from G's framing (`30_land_campus_day`,
  `46_facilities_campus_full`) reads as ref L1: a grid of dark lids between service halls, the
  dark harvest hall, the orange truss bridge from the quay. The blue hour (`31_land_dusk`,
  `46_facilities_campus_dusk_hero`, `34_first_sight_land_*`) has ref L2's wow: lit hall windows,
  lid work-lights, the bridge against a pink afterglow and its mirror. Construction reads phase
  by phase (`32_land_construction_p0`-`p6`), and pad_b's bridge now runs clear of the hall.
- **Offshore (refs O1-O3):** the safety-yellow 12-gon with its control block, silos, cranes and
  net, the orange service vessel and the crew boat (`40_offshore_*`); the fleet at scale
  (`40_offshore_lineup`); the build at sea phase by phase (`46_facilities_of1_*`); the unit as a
  yellow sliver from the home view and its Hs chip when the weather hides it (`36_home_*`).
- **The fjord:** ref 1's sunny deep blue (08), ref 3's grey (08a, 20 gråvær), ref 2's calm
  mirror (18, the menu), the aurora, the eight moods, and an underwater view of its own (11-11e).
- **The cinematics:** the land and offshore first sights and the teaser are letterboxed, calm
  and skippable, and end on their cards.
- **UI:** the end screens' reports, the auction flow, the save slots with pictures, the
  settings in nb and en, high contrast.

## What is off, ranked

1. **Fixed at the close: the land-kit preview drew 8 terrain chunks as nothing.**
   `LandKitPreview._carve_terrain` (QA only) rebuilt the terrain chunks under the pads from their
   arrays without the custom formats; since gfx-polish-1 the chunks carry an RGBA-float CUSTOM0,
   so `add_surface_from_arrays` failed ("Invalid array format for surface", 8 times) and each
   carved chunk drew nothing in `39_landkit_*`. `carve_chunk` now passes the surface's custom
   formats; GfxPolishChecks +1 (a real chunk carved keeps one surface and its CUSTOM0 value for
   value; the mutation without the flags fails it); `6bb9ff2`, `landkit_carve_before_after.jpg`.
2. **A false "Høy dødelighet: Dødelighet 100,0 % siste døgn" after a harvest**
   (`43_first_season_step09_report`, `step13`; `finding_first_season.jpg`). The warning reads
   `GameBridge.MortalityRateToday` = the analytics tracker's day-over-day drop in fish count
   (`AnalyticsTracker.cs`, "compare to previous day's fish count"), which counts harvested and
   transferred fish as deaths, so any harvest or transfer of more than 1 % of the fish in a day
   raises it. Pre-existing (U2-06's alert reads a metric from before S2-05's ledger). RUN_LOG §8.
3. **The first season's guide loses its targets** (`43_first_season_*`, the deferred U2-02 check):
   in steps 3 and 7 the target is scrolled out of view and only the chevron shows; in step 16 the
   toast stack and the open inbox drawer cover the ringed "Bygg lokalitet"; in step 2 the card
   sits on the pen list and the site's KPIs; from step 6 the inbox drawer stays open beside the
   card (16-18 warnings by the harvest); step 1 shows a "Bærekraft under kravet: 0/100" warning
   toast on day 0; the skip dialog's title is Godot's default "Please Confirm...". RUN_LOG §8.
4. **An empty chip: the pill without its text** over PO3 Nord's barge in 14, 15 and 16
   (`finding_chip_and_fog.jpg`). The barge chip fades out between 700 and 770 m
   (`FeedBargeVisualManager.CHIP_DISTANCE`, `VesselChip` visibility range, FADE_SELF): the
   Label3D fades, the `world_chip.gdshader` pill does not, and the barge stood 764 m away.
   Pre-existing (A3-01). RUN_LOG §8.
5. **Fish schools show through the sea fog** (`36_graybox_home_fog`): the collars vanish into the
   fog layer while the schools stay dark swirls, because `fish_shoal.gdshader` fogs itself with
   the distance exponential only (`fog_disabled`). Pre-existing (G3-01). RUN_LOG §8.
6. **Known and still open** (RUN_LOG §8): whitecaps in rectangular patches in every storm (10,
   20 storm, 35, the graybox rain and storm; `finding_whitecap_patches.jpg`); 08a's land
   contrast; the navy harvest hall and the mustard OF1 yellow; the OF1 "winter sun" frames light
   flat and cool, not O3's low sun, and the offshore first sight sits under a big snow wall; the
   clean net's caustics read as moving light; the bloom's emerald; boats without wakes.
7. **The carved pads read as flat rectangles** from the home view (08, 13-16, `45_pads_home_*`):
   straight edges and right angles at the mouth, the heath texture repeating; the blasted state
   is two pale slabs. F2-19's own shot purpose asks for "not as flat rectangles". Art direction.
8. **The walls' snow** runs in long parallel diagonal streaks (01, 08a, 18, 20); ref 2 shows
   patchy snow on dark rock. Seen from above (19) the wall ledges draw stacked contour lines.
9. **Small:** the "Ø konomi" title gap (06, 42_bank_offer); emoji in tabs and headers (U3-02);
   toasts over cards (03, 44_hud_high_contrast); the post-smolt pad's road reads as an airstrip;
   the teaser's news card has no panel and names its goal "Havområde" (`site_plots.json`);
   `AudioStreamCache._deliver` called a lambda whose node was freed (logged, harmless).
10. **A stale evidence file:** `docs/run/evidence/f2_25_land_kit_metrics.json` (F2-25, wave 2)
    predates F2-23's lid work-lights and gfx-polish-1's sun; a fresh capture's dusk lid/wall dL is
    20.1 against the test's 30 (above). Whether the bar holds for a lit lid at dusk, or the sample
    should take the lid's centre, is F2-25's owner's call before the file is re-recorded.

## Visual-QA baselines: what needs the owner's approval

Nothing was promoted. The visual-QA baseline (`godot/screenshots/baseline/`, 28 shots, threshold
25) was last promoted on 2026-10-14 (`ca7d8f8`), before waves 1-4's art. Read-only pHash
distances of this close's harness frames against it (`visual_qa.phash`, no capture, no
`baseline_update`):

- **Over the threshold (18; a new baseline needs the owner's approval):** 07_genetics 40,
  13b_fjord_bloom 36, 01_main_menu 34, 02_scenario_selection 34, 15_diag_no_underwater 34,
  04_telemetry 32, 08b_fjord_farm 32, 09_fjord_night_aurora 32, 12_fjord_lice_red_zone 32,
  03_dashboard 30, 06_finance 30, 08a_fjord_live_sync 30, 11_fjord_underwater 30,
  13_fjord_no_bloom 30, 14_diag_no_tilt 30, 16_diag_no_farm 30, 03b_scale80_dashboard 26,
  10_fjord_storm 26.
- **Within it (10):** 08_fjord_day 24, 07c_contracts 24, 07f_pen_management 24,
  03c_scale125_dashboard 20, 05_map_overlay 20, 07d_offshore 18, 06b_finance_trends 16,
  07b_crew 16, 07g_attention_inbox 16, 07e_carbon_ledger 14. The documented run-to-run noise is
  12-22, so 24 is near the edge.
- **What wave 4b itself changed** (the merge records; an A/B against `origin/main` was attempted
  and dropped, below): every fjord view (F2-19 carves the pads at the mouth: 08-16), the menu
  (01: gfx-polish-1's clouds), 09 (the clouds) and 11 (G3-04 replaced the underwater view
  outright). The UI shots' distances (02-07) are older: the baseline (`ca7d8f8`, 2026-10-13
  23:16) predates wave 1's Kyst & Stål theme (`cff44b0`, 2026-10-14).
- **No baseline at all:** the harness's 11b-11e, 18_low, 18_no_mirror, 19, 02b, 08c, 09b and
  20_* frames are not in `visual_qa/config.py`, and none of the 115 `shots.json` shots is in
  visual QA. Adding any of them is the owner's call.
- **The A/B that was dropped:** the harness on an export of `origin/main` (`git archive`, built
  and imported in the scratchpad) captured 01, then its menu loaded the day-30 autosave already
  in the shared `user://saves` (written 2026-10-20 20:49, before the close) and hung for 15
  minutes; it was stopped. `main` predates U2-08's by-path rules, so attributing each shot
  exactly needs that A/B with a clean `user://`.

## Per-shot review

### FarmTypeGrayBox (F2-18 read gate, `36_graybox_*`)

| Shot | Good | Off |
|---|---|---|
| 36_graybox_home_clear | The shipped home view with both campuses at the mouth reading as grids of circles (echoing the pens), the orange bridges, the OF1 unit a yellow sliver beyond | — (a read gate, not an art shot) |
| 36_graybox_home_overcast | The campuses and the unit still read at 20 km | — |
| 36_graybox_home_rain | Rain hides the mouth as it should (4.5 km); the sites stay readable | The whitecap patches in the foreground (as 10) |
| 36_graybox_home_snow | A white-out at 2.5 km; the chip and labels stay readable | Everything else is gone, as staged |
| 36_graybox_home_storm | Rain, dark sea, readable sites | Whitecaps in rectangular patches (as 10) |
| 36_graybox_home_fog | The sea-fog layer hides the water and the collars; the walls stand above it | The fish schools show through the fog as dark swirls with the net's hatch, much stronger than the fogged collars: `fish_shoal.gdshader` applies only the distance fog's exponential, not the sea-fog layer (pre-existing, G3-01) |
| 36_graybox_land_focus | The gray box at the campus framing: lid ellipses read, the orange bridge, the dark hall | Gray-box primitives by design; the pad's heath/granite camo pattern is busy at this framing |
| 36_graybox_of1_focus | The 12-gon gray box at 220 m, -18 deg: the ring fills the planned share of the frame | Gray box by design; the ring's shadow on the water is crisp and dark |
| 36_graybox_of1_focus_spec_r2 | Before-frame kept as evidence for the framing change | — (evidence) |
| 36_graybox_land_focus_no_tank_shadows | The A/B for the LOD1 shadow rule | — (evidence) |
| 36_graybox_home_clear_pad_a_east | The alternative pad position tried | — (evidence) |
| 36_graybox_home_clear_pad_a_spec_r2 | Before-frame: the campus behind the left wall, the reason pad_a moved | — (evidence) |

### LandKitPreview (`39_landkit_*`, the kit on the preview's own slab)

| Shot | Good | Off |
|---|---|---|
| 39_landkit_day | Ref L1's grid of tanks with dark lids between the service halls, the dark harvest hall, the orange truss bridge to the quay; since 6bb9ff2 the slab's edges show the heath and the cut face (the carved chunks drew nothing before) | The preview's ground is one flat grey slab with large empty areas (the game's pads are carved terrain, see 46) |
| 39_landkit_dusk | Warm rims on the tanks (lid work-lights) and the hall's roof lights | Dark overall; no sky in the frame to sell ref L2's dusk; a fresh capture measures the sample tank's lid/wall dL at 20.1 against F2-25's bar of 30 (the committed 30.65 predates F2-23's lid work-lights, which light the lid, and gfx-polish-1's sun; RUN_LOG §8) |
| 39_landkit_lot | The one-lot framing with the LOD0/LOD1 hand-over invisible | Flat slab ground |
| 39_landkit_lot_pale_lids | The pale-lid swatch (ref L2) through the shader's swap | — |
| 39_landkit_close | LOD0 detail: green-banded tank walls, feeders and rotors, walkways and rails, the hall's windows, the truss bridge | Flat slab ground; the hall walls are plain |
| 39_landkit_home_overcast | Both campuses read as landmarks at the mouth on a grey day | — |

### OffshorePreview (`40_offshore_*`)

| Shot | Good | Off |
|---|---|---|
| 40_offshore_hero | The safety-yellow 12-gon with its control block, silos and cranes, the orange service vessel alongside, the crew boat, the net below | The light is flat grey-blue rather than O3's warm low winter sun; the vessel's reflection breaks into orange speckles on the chop |
| 40_offshore_framing | The spec's OF1 framing: the ring at the planned share of the frame | — |
| 40_offshore_close | LOD0 reads: the splash-zone columns, the zig-zag bracing, walkways with rails, the control block, cranes, the net wall | The net is a uniform grey sheet |
| 40_offshore_dusk | The control block's windows and the deck lights after sunset, a snowy coast under a violet sky | The stars are large dots |
| 40_offshore_overcast | On a grey day the yellow hull and orange vessel stay the only saturation (the brief's rule) | — |
| 40_offshore_yard | The yard reveal: ring and bracing done, the topsides rising, no net yet | — |
| 40_offshore_home | The unit as a yellow sliver at the mouth from the home view | — |
| 40_offshore_lineup | The fleet beside the ring: the 75 m vessel visibly shorter than the 110 m ring, the red tug variant, the crew boat | Boats float without wakes (RUN_LOG §8) |

### ExpansionFlowCapture (U2-01, `41_expansion_*`; the map and dialog are the geo track's on `geo/int`)

| Shot | Good | Off |
|---|---|---|
| 41_expansion_map | The region view on day 0: traffic-light dots, the capacity card (1 licence, 780 t), the home zone's build refused for cash as text, Bømlo's licence route | The first-season guide card covers the map's south-west: the home area's dot and ring and the legend sit under it |
| 41_expansion_build_refused | The family-first dialog: the bottleneck strip, Sjø chosen with Land and Hav locked, the variant cards, the refusal as text, the confirm waiting | The comparison rows are cut mid-line by the dialog's scroll area ("256 kNOK/år" sliced) |
| 41_expansion_auction_preview | Reserve, price per tonne, 7 days, the free opening, the state's 2022/2024 averages | — |
| 41_expansion_auction_live | Leading rival named (parody names, D17), the countdown, bids newest first with NOK/t, the bid field at the minimum | — |
| 41_expansion_auction_won | The payoff: licence won, MTB in PO 4, "Bygg lokalitet" next | — |
| 41_expansion_build_new_zone | The won zone's build dialog with the comparison bars and the confirm ready | The strip says "Din flaskehals: kapital" in green with 270 MNOK against a 110 MNOK floor (reads as a bottleneck that is not one); the third comparison row is cut by the scroll area |
| 41_expansion_built_fjord | "Vis den på fjorden" lands on the new locality, framed, with the camera hint bar and "Fly til merd" | In the storm the spray around the new pens draws as large soft white balls |
| 41_expansion_map_small | At 1280x720 the zone cards, verdicts and buttons stay whole | The guide card covers the home area and legend, as in 41_expansion_map |

### EndScreenCapture (U2-03, `42_*`)

| Shot | Good | Off |
|---|---|---|
| 42_end_won | Verdict, bronze medal with its three distinctions, the campaign note, "Neste" first; the Chart-paper report with the P&L story and three numbered turning points, where the money and the fish went, the event log | — |
| 42_picker_campaign | The won scenario wears its medal, two scenarios opened, the locked one's detail names the wins that open it, Start off | — |
| 42_end_bankrupt | "Konkurs", the credit line it fell through, the bank's warning and call days, no medal, "Prøv igjen" first, "Fortsett å spille" off | — |
| 42_bank_offer | The bank card at the call: stage, burn, days to the limit, overdraft interest, the rescue offer with its deadline and buttons | The title reads "Ø konomi"; a storm toast covers the cost bars' values |
| 42_end_lost | The scenario's own loss text, the rescue loan on the cash line, the missed distinction | — |
| 42_end_bankrupt_small | At 1280x720 the column and the report stay whole and readable | — |

### FirstSeasonCapture (U2-02, `43_first_season_*`; the deferred visual check, done here)

| Shot | Good | Off |
|---|---|---|
| 43_first_season_step01_pens | The guide card in its corner (overline, 18 dots, welcome, the "Gjør dette" callout, "Vis meg", "Hopp over"), the ring and chevron on the Merder tab, clear of the toasts | A "Bærekraft under kravet: 0/100" warning toast on day 0 of the tutorial |
| 43_first_season_step02_feed_plan | The ring round the Fôringsplan card with its dropdowns readable | The card sits top-left over the pen list, the site title and the fish count (the shot's purpose says it covers neither the plan nor the pen list) |
| 43_first_season_step03_lice_order | The card's text and the chevron that points the way | The "Avlus ved telling" switch is scrolled out of view above: the frame shows the chevron, not the ringed switch the shot is for |
| 43_first_season_step06_grow | The wait callout, the goal bar "Snittvekt nå 0,62 av 4,0 kg", the ring on the clock strip at 30× | The inbox drawer and three toasts are open beside the card: a busy screen for a wait step |
| 43_first_season_step07_harvest | The card explains the planner's horizons; the clock is held | The "Høst" button is scrolled out of view (chevron only); the inbox holds 1 critical and 16 warnings and three toasts are up |
| 43_first_season_step09_report | The ring on "Siste slakterapport", "Steg 8 fullført" on the card | A false "Høy dødelighet: Dødelighet 100,0 % siste døgn" warning on harvest day: the warning reads the analytics fish-count drop, which counts the harvested fish as deaths (finding) |
| 43_first_season_step13_funding | Clock at 1×, the goal bar "I banken 3,67 MNOK av 7,06 MNOK", the ring on the Økonomi tab | The inbox drawer is still open over the page; the false 100 % mortality warning is listed |
| 43_first_season_step16_build | The card at the top-left, the home area picked | The toast stack and the inbox drawer cover the home zone's "Bygg lokalitet", where the ring is (only its chevron peeks out) |
| 43_first_season_skip | The skip confirmation explains that a skip opens every tab but is no win | The dialog's title is Godot's default English "Please Confirm..." |
| 43_first_season_won | The season won by input: "Målet er nådd", silver medal, the report, no ring left | — |

### SettingsCapture (U2-08, `44_*`)

| Shot | Good | Off |
|---|---|---|
| 44_save_slots | The pause menu's save list: each slot's picture of the world without the UI, name, scenario, local ISO time, day and cash, "Siste autolagring" | The "Nytt lagringspunkt" button carries a floppy emoji (pre-existing key, U3-02's icon pass) |
| 44_settings_gameplay_nb | Innstillinger > Spill: the Språk row above Autolagring, every label from keys | — |
| 44_settings_gameplay_en | The menu redraws in English at once | — |
| 44_settings_high_contrast | High contrast applies at once: opaque panels, white text, bright borders | — |
| 44_hud_high_contrast | The HUD bar, tab rail and dashboard on the HC Ink and Chart surfaces, all text readable | An achievement toast covers the weather card; emoji icons |
| 44_main_menu_load_slots | The main menu's load list over the live fjord with the same pictures and times | The dialog keeps a tall empty band under three slots |
| 44_world_at_save | The fjord at the first save (day 40); the slot picture (44_picture_*) shows this view without the HUD | — |

### FacilityPresenterCapture (F2-19/F2-20, `45_pads_*`, `32_land_construction_*`, `46_facilities_*`)

| Shot | Good | Off |
|---|---|---|
| 45_pads_land_heath | pad_a carved flat at +3.6 m, heath with rock patches, a rock-fill rim to the sea, no tree or naust on it | Reads as a perfectly flat table with straight edges; the heath texture repeats |
| 45_pads_land_granite | Blasted granite on the pad and its cut faces, clearly apart from the heath | Uniform grey slab |
| 45_pads_b_heath | pad_b with its cut face into the hillside, islets beside it | As pad_a: flat with straight edges |
| 45_pads_b_granite | pad_b blasted | — |
| 45_pads_delta_heath | pad_delta raised on the head delta between the river and the rivals' sites (parody names) | Hard straight pad edges against the delta |
| 45_pads_delta_granite | The blast covers the pad and its slopes | The blasted rectangle drapes over the slopes like a blanket |
| 45_pads_home_heath | The carved pads at the mouth from the home view | They still read as flat rectangles (the shot's aim is "not as flat rectangles") |
| 45_pads_home_granite | The blasted pads from the home view | Two pale grey slabs: the most artificial shapes in the frame |
| 32_land_construction_p0_site_works_blast | Granite spreading in patches over the heath, nothing standing | The patches are rounded blobs (cow-print) more than blast fronts |
| 32_land_construction_p1_site_works_foundations | Ring foundations and the roads on the blasted yard | The ring foundations are faint thin outlines |
| 32_land_construction_p2_tank_rings | Open-topped walls half up, no hall, quay or bridge yet | — |
| 32_land_construction_p3_treatment_hall | Walls up without lids, the hall bays and the harvest hall's walls rising | — |
| 32_land_construction_p4_sea_works | Halls up, the quay and the intake basin | The pipe bridge is hard to see at this phase |
| 32_land_construction_p2_tank_rings_twin | The p2 state stood again; its campus hash matches p2 (the noise read) | — (measurement twin) |
| 32_land_construction_p5_first_tanks | Utilities rising, the orange bridge up | — |
| 32_land_construction_p6_completion | The first six grow-out tanks lidded with feeders, the rest open | — |
| 46_facilities_campus_day | The lot's grid of dark lids, the process hall, the dark harvest hall, the orange bridge from the quay, roads on blasted granite | The harvest hall's cladding reads navy under the clear sky (RUN_LOG §8 art call); a one-lot campus leaves half the pad bare |
| 46_facilities_campus_dusk | Low sun on the water, roof lights on | Dim; the lit windows barely read at this framing |
| 46_facilities_campus_dusk_hero | Ref L2's wow: the lit campus low against a pink-violet sky and its mirror, the bridge, the hall windows | The campus sits small and low behind a large dark foreground of heath |
| 46_facilities_campus_close | LOD0: green-banded tanks with walkways, rails and rotors, the process hall, the truss bridge over the lot road | — |
| 46_facilities_postsmolt | Two post-smolt modules with their lean-to bays on pad_delta, the river intake | The road is one long flat black strip that reads as an airstrip |
| 46_facilities_of1_yard_contract | Four yellow survey buoys mark the slot, the unit's Hs chip | — |
| 46_facilities_of1_hull | The pre-laid moorings' pick-up buoys in four pairs | — |
| 46_facilities_of1_voyage | The finished unit coming in from the open sea behind a tug | Small at the horizon; no wake |
| 46_facilities_of1_tow | Three anchor tugs bring the unit toward its slot | No tow lines and no wakes |
| 46_facilities_of1_mooring | The unit on station, the tugs at the anchor bearings | — |
| 46_facilities_of1_commissioning | The crew boat at the landing; control block, silos, cranes, the net | Flat grey light |
| 46_facilities_of1_winter_sun | The safety-yellow 12-gon on deep blue at the OF1 framing | The orange service vessel is hidden behind the ring (an orange smear inside it); the light reads overcast, not O3's low winter sun; the yellow is mustard (RUN_LOG §8) |
| 46_facilities_of1_hero | The unit and its vessel from the sun side, the snowy coast low on the horizon | The sun side does not read: flat light, no glare |
| 46_facilities_budget_of1_framing | Measurement frame (unit and vessel alone at 220 m) | — (as 46_facilities_of1_winter_sun) |
| 46_facilities_home | The campus a landmark at the mouth, the OF1 a yellow sliver beyond | — |
| 46_facilities_budget_of1_home | Measurement frame for the §5 budget | — |
| 46_facilities_campus_full | Four lots in two rows filling pad_a with the utilities behind: ref L1's campus | — |
| 46_facilities_budget_campus_home | Measurement frame for the §5 budget | — |
| 46_facilities_campus_pad_b | pad_b's 2-lot campus: the bridge runs from the quay between the basin and the hall, clear of both (the review's clash fixed) | — |
| 46_facilities_campus_pad_b_quay | The bridge's spans, the hall 10 m off it, the apron to the quay in afternoon sun | — |

### ShowcaseCapture (F2-21/F2-23, `30`-`36`, `34_first_sight_*`, `34_teaser_*`; the teaser run alone with `--only 34_teaser`)

| Shot | Good | Off |
|---|---|---|
| 30_land_campus_day | G's Land framing of the 4-lot campus: the lids' grid, the halls between the rows, the dark harvest hall, the orange bridge from the quay, the pens and the wall beyond (ref L1) | — |
| 31_land_dusk | Blåtime from low over the sound: the harvest hall's lit windows and roof lights, the lit lid rims, the bridge, snow peaks and a pink afterglow (ref L2) | The roof's white strips read like parking-lot markings |
| 32_land_construction | The pad_b site at 62 % tank rings on blasted granite, the cut face behind it | — |
| 33_offshore_of1_winter_sun | G's Hav framing: the yellow ring and the orange vessel on deep blue, the fjord mouth with the campus beyond | Hull share 1.552 % against the 1.5 % bar (thin, RUN_LOG §8); the light reads cool and flat rather than O3's low sun |
| 35_offshore_storm | A 4.8 m sea with rain, the unit heaving, its red Hs chip with the treatment window | The whitecaps are flat white sheets on the water (RUN_LOG §8, G2-03) |
| 36_home_overcast_landmarks | The campus a light band on the strandflat, the OF1 a yellow sliver, no chip | — |
| 36_home_rain_landmarks | The fog hides the unit and its chip shows in its place | — |
| 36_home_snow_landmarks | The chip shows in the snow | — |
| 36_home_storm_landmarks | The chip shows in the storm | The chip says "Hs 0,6 m" under a staged storm: the QA weather is staged, the unit's sea is the engine's calm day (a staging artefact) |
| 34_first_sight_land_t02 | Letterboxed timelapse: the campus rising on the bare pad in the blue hour, the skip hint | — |
| 34_first_sight_land_t07 | Low over the quay along the orange bridge, the lamps and the hall's windows on | — |
| 34_first_sight_land_t11 | Among the tanks as the lid work-lights come on, the OF1 lit far out | — |
| 34_first_sight_land_t18 | The -40 deg framing with the card "Første i drift", "Lagre bilde", "Fortsett" | — |
| 34_first_sight_hav_t02 | The unit rises part by part: the yellow columns first | — |
| 34_first_sight_hav_t06 | From behind the orange service vessel, the ring beyond | A bright clear day under a big snow wall, not O3's low sun and isolation (RUN_LOG §8) |
| 34_first_sight_hav_t11 | Up to the low oblique framing, the fjord mouth and the campus beyond | As t06 |
| 34_first_sight_hav_t17 | The card "Havmerd på Ytre kyst" with a wellboat coming in | As t06 |
| 34_first_sight_land_reduced | Reduced motion: the still with the card | — |
| 34_teaser_t02 | The rival's yellow hull over the horizon under tow, three tugs ahead | — |
| 34_teaser_t06 | The tow crossing beyond the mouth | — |
| 34_teaser_t10 | The news card ("Fortsett" only, no build offered) | The card has no panel: an outlined headline on bright water, and the destination reads "Havområde" |
| 34_teaser_reduced | Reduced motion: the still with the news card | As t10 |

### Default harness (`ScreenshotHarness`, 49 frames; not in `shots.json`)

| Shot | Good | Off |
|---|---|---|
| 01_main_menu | Live low-oblique fjord in ref 2's calm: mirror water holding both snow walls, a pen row with barge and red workboat, soft half-pass clouds; the Kyst & Stål rail with the logo and no emoji | The clouds are flat-bottomed blobs; the walls' snow runs in long parallel diagonal streaks; the menu's site is a single file of three pens, not ref 1's grid |
| 02_scenario_selection | Card grid with region, difficulty pips, locks and start capital; detail pane with tags and the start button | Lower half of the screen empty; the detail pane stands tall and mostly blank |
| 02b_briefing | The briefing card over the paused world: date, ISO week, goal, deadline, capital, the time keys; the world reads behind it (both sites, amber red-zone rings) | The barge chip at the left is cut by the card's edge ("fôrer" half under the panel) |
| 03_dashboard | Chart-paper KPIs with sparklines, the merdoversikt, water quality and weather cards readable | Emoji icons in the tab rail and card headers (U3-02 pending); the achievement toast covers the weather card; Merdoversikt half empty |
| 03b_scale80_dashboard | Everything fits at 80 %, the trend row shows | The toast overlaps the FCR trend |
| 03c_scale125_dashboard | 125 %: the rail folds into "Mer", text stays whole | The toast covers the weather card's figures |
| 04_telemetry | Lice line with its limit, the resistance bars, the FCR section, all readable | Emoji in every section header; four empty resistance bars take a lot of room |
| 05_map_overlay | The production-area map with traffic-light dots and the area card | Pre-geo map (geo/int replaces it): a crude coastline with a heavy stroke, labels crowd the west coast |
| 06_finance | The bank card, insurance rows and the "Hvorfor gikk det slik?" bars | The title reads "Ø konomi" (a gap after the Ø); emoji icons |
| 06b_finance_trends | Monthly bars, the P&L statement, loans and the 90-day trends | — |
| 07_genetics | The genetics table renders | The orphaned panel (RUN_LOG §8 owner decision): a plain full-screen sheet with no HUD, mostly empty |
| 07b_crew | KPI row and the three roles with their effects | An empty-state page: a large blank area |
| 07c_contracts | Spot price chart with low/mean/high and the buyer list | — |
| 07d_offshore | Lists the enclosure upgrades, sludge and cohort transfer | Pre-F2-33 form: the empty-state line sits on its box's border, raw inputs ("1.0 kg" with a decimal point), an empty second dropdown |
| 07e_carbon_ledger | Scopes 1-3, credits and net | Plain list, emoji subheaders, no chart |
| 07f_pen_management | Dense but readable pen view: KPIs, treatments, Driftsplan, stocking and the feeding plan | — |
| 07g_attention_inbox | The inbox modal with its empty state and the auto-refresh toggle | — |
| 08_fjord_day | Ref 1's sunny deep blue, both sites in grids with white hoses, barges, buoys and health rings, skerries at the mouth | The carved pads read as flat rectangles with straight edges at this distance (F2-19 wants low strandflat); the walls are smooth grey with little vegetation near the shore |
| 08a_fjord_live_sync | Ref 3's grey mood: grey water, cloud on the walls, muted colour | Land RMS contrast 0.107 < 0.12 (the known content failure since gfx-polish-1); the snow on the walls runs in parallel streaks; the pads are dark rectangles |
| 08b_fjord_farm | Pen anatomy reads (collar, stanchions, net, school, feeder), white hoses from the barge, buoys, the selection ring | The hoses bundle and fan out over pens 5-6 and run off the frame; the ripple pattern repeats visibly in the background |
| 08c_fjord_farm_feeding | The feeding pen with its spreading school and the hose | Buoys near the camera sit on dark blue discs (a halo round each buoy) |
| 09_fjord_night_aurora | Aurora curtains with stars, mirrored on the water (2.7 %), site lights | The curtains read as vertical bars (bar-code rays) |
| 09b_fjord_night_lights | Lit pens, barge and buoy lamps, the aurora behind | The pens' cyan underwater-light glows are strong, flat discs that dominate the frame |
| 10_fjord_storm | Grey storm, rain streaks, whitecaps, readable pens | Whitecaps gather into flat rectangular patches (RUN_LOG §8, the G2-03 system); the far horizon is a flat band |
| 11_fjord_underwater | Teal water from the sim, sun shafts, the school (1.34 % of the frame), Snell's window over the surface | The net is barely legible at this framing; the window's edge is a smooth blob, not a rippled circle |
| 11b_underwater_bloom | The bloom closes the view (fog 0.84), fish fade into it | Saturated emerald green (hue 133; the optics' answer, RUN_LOG §8 art call) |
| 11c_underwater_window | Snell's window with the sun and fish silhouettes against it | The window's rim reads as a soft blob more than a sharp circle |
| 11d_underwater_net | The net fills the frame with shafts through it; caustics move 0.0084 (clean) | The mesh is a perfectly regular flat diamond grid (no sag, knots or depth) and the caustics do not read as a network (RUN_LOG §8) |
| 11e_underwater_net_fouled | Fouled threads turn olive and thicker (0.0088) | A modest difference from 11d at a glance |
| 12_fjord_lice_red_zone | Red rings on the lice-heavy pens | The lice markers are large white blurred balls that read as foam or snow |
| 13_fjord_no_bloom | Clean blue water as the bloom pair's reference | — |
| 13b_fjord_bloom | The bloom tints the fjord teal-slate, clearly different from 13 | — |
| 14_diag_no_tilt | Same framing as 08 for the colour check | An empty chip (pill and dot, no text) over PO3 Nord's barge; also in 15 and 16 (see the finding) |
| 15_diag_no_underwater | Diagnostic frame, as 14 | The same empty chip |
| 16_diag_no_farm | Diagnostic frame without the farm | The same empty chip over the lone barge |
| 18_fjord_calm_dusk | Mirror-calm water holding the snow walls, lit pens, the barge and its chip (structure gain 0.355 over the no-mirror twin, SSIM 0.646) | The walls' snow runs in long parallel streaks (ref 2 has patchy snow on rock); the depth of field blurs the head (RUN_LOG §8 art call); the cyan pen glows are strong |
| 18_fjord_calm_dusk_low | Low tier without the mirror still reads as calm dusk | — (fallback) |
| 18_fjord_calm_dusk_no_mirror | The twin the gain is measured against | — (measurement twin) |
| 19_fjord_shore_foam | Foam hugs the shore (19.5 % within 5 m, 0 % at 15-40 m), the sun's glitter on deep blue | Seen from above the wall's ledges draw stacked zig-zag contour lines |
| 20_mood_blaatime | Violet dusk, mirror water, lit pens, snow on the pads | Cyan pen glows dominate |
| 20_mood_frostroyk | Sea smoke over dark water, snowy pads and walls | The smoke reads as blotchy cloud shadows on the water more than as low vapour |
| 20_mood_graavaer | Ref 3's grey: cloud on the walls, flat grey water | The pads are the frame's only hard-edged shapes |
| 20_mood_hoest | Autumn light, calm water reflecting the walls | Close to sommerdag and midnattssol in composition (closest pairs 10 bits, min 9) |
| 20_mood_midnattssol | Warm low sun on the walls and sky | A treeline shows as a horizontal band of dots on the left wall |
| 20_mood_nordlys | Aurora with its reflection, lit pens | Cyan pen glows as in 09b |
| 20_mood_sommerdag | Ref 1's summer day | As 08 |
| 20_mood_sommerdag_shadows_t0 | The first frame of the cloud-shadow pair | — (measurement pair) |
| 20_mood_sommerdag_shadows_t1 | The cloud shadows have moved since t0 | — (measurement pair) |
| 20_mood_storm | Dark storm deck, rain, whitecaps | The whitecap patches as in 10 |

## Evidence files (`docs/run/evidence/wave4b_frame_review/`)

| File | What |
|---|---|
| `sheet_graybox_landkit_offshore.jpg` | 36_graybox_*, 39_landkit_* (after the carve fix), 40_offshore_* |
| `sheet_ui_flows.jpg` | 41_expansion_*, 42_*, 43_first_season_*, 44_* |
| `sheet_facilities.jpg` | 45_pads_*, 32_land_construction_*, 46_facilities_* |
| `sheet_showcase.jpg` | 30-36, 34_first_sight_*, 34_teaser_* (the teaser from its own run) |
| `sheet_harness.jpg` | the default harness's 49 frames |
| `landkit_carve_before_after.jpg` | finding 1: 39_landkit_day before and after the carve fix |
| `finding_chip_and_fog.jpg` | finding 4 (the empty chip, 14) and finding 5 (schools through the sea fog) |
| `finding_first_season.jpg` | findings 2 and 3 (the false 100 % mortality warning, the toasts over step 16's target) |
| `finding_whitecap_patches.jpg` | the storm's whitecap patches (10) |
