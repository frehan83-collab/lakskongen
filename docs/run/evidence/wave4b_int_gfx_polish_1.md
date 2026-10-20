# Wave 4b integration: `wave4b/gfx-polish-1` merged into `wave4b/int`

The polish stage (clouds without slices, walls without blinds, the sun's
light at low elevations, chips that stay with their boat, pens that damp
the sea, the mirror to the screen edges, 18 re-staged) merged into
`wave4b/int` on 2026-10-21 (`--no-ff`, `480ae51`), on top of `1bea464`
(gfx-farm-1 merged). The stage's own evidence is `gfx_polish_1/` and
`gfx_polish_1_golden_diff.md`. This file is the merge: the conflicts, the
review's findings and how each was handled, the gates on the merged tree,
and the frames looked at. Frames are `wave4b_int_gfx_polish_1/` (JPEG, at
most 1280 px); the golden traces `wave4b_int_gfx_polish_1_golden_diff.md`;
the facility capture's numbers `f2_20_facilities_metrics.json`
(re-recorded on the merged tree, 36 shots).

Commits: `480ae51` the merge, `f658584` finding 1, `68137ae` findings 2
and 4-8, then this document.

## Merge

| File | Conflict | Resolution |
|---|---|---|
| `docs/RUN_LOG.md` | §7 and §8 rows on both sides | Both sides' rows kept (int's first); the stage's three §8 rows edited in place (moods, 08a, 18) auto-merged |
| `godot/scripts/environment/TerrainGeneratorBridge.cs` | int's `FjordGroundCover.Build` (F2-19 clearings) vs the stage's `GenerateHeightmap(out WallLedges)` | int's ground cover with the stage's ledges into `TerrainMesh.Build(..., ledges)` |
| `tools/run_ui_flow_tests.sh` | MIN_CHECKS 1778 (int) vs 1650 (stage) | Measured on the merged tree: 1794 = 1778 + the stage's 13 + the review's 3 |
| `src/LakskongenSim/Environment/TerrainGenerator.cs` | Auto-merged, wrong by meaning | The stage's `FillRow` read `Sample()`, so the grid lost F2-19's pads (finding 1, fixed in `f658584`) |
| `UiFlowTests.gd`, `terrain_shader.gdshader`, `FjordTerrain.gd`, `PerfHarness.gd`, `FJORD_RENDERER.md` | Auto-merged | Read: `_run()` calls `GfxPolishChecks` and `_test_facilities_render`; the pad splat and the CUSTOM0 macro sit side by side in the terrain shader |

## Review findings

| # | Severity | Finding | Handling | Proof |
|---|---|---|---|---|
| 1 | high | `FillRow` read `Sample()` while F2-19 carves the pads in `HeightAt`: the merged grid (mesh, collider, far field) lost the pads, and scaling nothing would leave the uncarved wall's terraces in the carved ground's macro | Confirmed on `480ae51`: 3 of 66 terrain-family tests fail (`TerrainPadTests.EveryPad_IsFlat...`, the golden hash, `FarFieldTests.FarField_IsTheHeightfieldInsideTheWorld`). Fixed: `TerrainGenerator.Ground(x, z)` (the sample with the pads carved) is the one function every grid path and `HeightAt` read. `CarvePads(h, x, z, out keep)` also returns the share of a change in h it passes on (1 uncarved, 0 on a pad and a shelf's fill, the cut face's or drop's smoothstep), and `Ground` scales the ledges' offset and strength by it: a pad's macro ground is the pad, a cut face's the cut face of the ground without its terraces. Zeroing the ledges wherever the carve changes the height (the review's suggestion) would step the macro at the cut face's outer edge. The height arithmetic is unchanged (golden `a7b58a2c48dd4a8f`) | `TerrainGroundTests` (7): every grid path is `HeightAt` bit for bit (seed 42 at 512², seeds 1-10 at 129²); no offset or strength on any pad vertex, macro ground 3.6 m (seed 7 at 512² has 145 pad vertices whose uncarved wall carries a terrace, up to 2.26 m; seed 42 has 11, up to 0.027 m); `keep` is the carve's slope in h by central differences round every pad (cut, drop, fill and untouched all seen). Mutations: the grid back on `Sample` fails 5 tests, the ledges unscaled 1, the cut face's or the drop's share dropped 1 each |
| 2 | medium | 18's gain over the Low twin passed only on High's depth of field | Confirmed: against the same tier with only the mirror off (DOF, scale, detail and the run's `--quality` overrides kept) the SSIM gain is 0.044-0.054 with DOF and 0.065 without. Offline replica: SSIM scores the twin's flat, bright sky reflection above the darker mirror image in the band's flat windows (the wave-3 finding and the stage's caveat named this). Ruling (§7): the gain is the mirror's structure, the mean windowed correlation where the band over the waterline varies by >= 0.01 (`mirror_scores`), over that twin, bar 0.2; SSIM >= 0.5 stays; the SSIM gain and the Low twin are printed as information | Windowed, 1920x1080: structure 0.390 vs 0.009 (gain 0.382) with DOF, 0.300 vs 0.030 (0.270) with `dof=false`, 0.401 vs 0.064 (0.336) in the full run; the replica on the first run's frames 0.394 vs 0.012. UI check on made frames: a flipped scene scores 1.000 at 0.4 of its brightness while its SSIM falls to 0.588, a flat reflection -0.000, an unrelated one 0.006. Frames `18_mirror_vs_no_mirror_vs_low.jpg`, `18_band_mirror_no_mirror_low.jpg` |
| 3 | medium | No test pins the generator's ledge offsets: a sign flip passed every check | Confirmed: the sign flip fails no test in the terrain family before the new test. Fixed: `TheGeneratorsLedges_TakeTheTerracesOutOfTheWalls` (seeds 42, 1, 7, 13 at 512²): down the walls' fall line (the grid's rows, ledges at least half strength) the slope's total variation of the heights minus the offsets is under 0.5 of the terraced heights' | Measured 0.38-0.43; offsets of the wrong sign give 1.83-1.87, half of them 0.60-0.63, none 1.0. Mutation (sign flip): all four cases fail |
| 4 | medium | The menu's hero sky (per-pixel clouds) costs frame time on the first screen, unmeasured | Confirmed in-run: +2.15 to +2.58 ms p50 (median +2.34, 6 of 6 alternating 4 s pairs; 1920x1080 High, scale 0.75, vsync off, load 8-18), 16.97-17.30 ms p50 without it; the review measured +3.7 to +4.1 ms at 1440p. Fixed: `MenuBackdrop` no longer asks for it (the mechanism stays for a hero view that does) | Frames at pinned drift: the half pass is soft with no visible texels at 1x (`01_main_menu_merged.jpg`, `01_clouds_hero_pass_vs_half_pass_2x.jpg`). UI check (replaced, stronger): the menu's clouds in the tier's pass for every tier, the hero mechanism one finer for every tier, underwater per pixel |
| 5 | low | The sun check multiplied by the director's own helper | Confirmed: the review's mutation (`var lin := srgb` in `sun_color_luminance`) passed. Fixed: the check decodes the light colour itself (IEC 61966-2-1, Rec. 709 weights) | Mutation run: fails, light 0.545 against energy 0.710 at 3.3 degrees |
| 6 | low | Sharp contours inside the menu's clouds | Confirmed (frames, contrast-stretched and true): nested arcs in the per-pixel pass. Fixed for the menu by finding 4 (the half pass draws none visible); the per-pixel pass itself (Ultra, underwater) keeps them (remainder below) | `01_clouds_hero_pass_vs_half_pass_2x.jpg` (top: per pixel, bottom: half pass) |
| 7 | low | A misspelt `--only` ran the full windowed harness | Confirmed by code: `only_groups` drops unknown names and an empty list is the full run. Fixed: `only_arg_error` stops the harness with exit 2 before it boots when an `--only` name is no group (a typo, or two arguments passed as one) | UI check: the glued, the misspelt and the empty `--only` stop, a valid one and none run. Mutation (always ""): fails |
| 8 | low | Comments described the wip's discarded UV2 macro | Confirmed. Fixed: `GfxPolishChecks` and `test_gfx_polish` describe the CUSTOM0 macro; `ShaderPrewarm` drops the `TEX_UV2` case only the wip used | Read. New UI check: the warm-up triangle carries the chunks' CUSTOM0 as RGBA float (UiFlowTests' layout check leaves CUSTOM0 out; mutation: CUSTOM0 out of the warm-up fails it) |

## Gates on the merged tree

On `68137ae` (the commit after it adds documents and the re-recorded
facility metrics):

- `dotnet build LakskongenSim.sln` and `godot/LakskongenGame.csproj`: 0 errors.
- `dotnet test LakskongenSim.sln`: LakskongenSim.Tests 2004 passed, 3
  skipped, 0 failed (int's 1985 + the stage's 12 `TerrainMacroTests` + 7
  `TerrainGroundTests`); 31, 4, 5, 10 and 25 in the other projects; load
  14-25.
- `python3.12 -m pytest tests/ -q`: 872 passed, 1 skipped (855 + the
  stage's 17; four assertions replaced by stronger ones).
- `tools/validate_scenes.sh`: 84 scenes, failures=0.
- `tools/run_ui_flow_tests.sh`: checks=1794 failures=0, PASSED (MIN_CHECKS
  1794 = 1778 + 13 + 3), load 4-10. An earlier full run of the same code
  (working tree, before the commit) also counted 1794 and 0 failures.
- `tools/record_golden_traces.sh --base 1bea464`: 27 of 27 traces
  identical, math fingerprints equal
  (`wave4b_int_gfx_polish_1_golden_diff.md`). The terrain fix and the
  stage's C# change no game.
- Mutation runs: five C# mutations (grid on `Sample`, ledges unscaled,
  offsets flipped, the cut face's and the drop's share) each fail their
  tests; five GDScript mutations at once (hero back in the menu, the sun's
  decode dropped, `only_arg_error` always "", the structure's
  normalisation dropped, CUSTOM0 out of the warm-up) fail exactly those 5
  UI checks (18 run).

## Windowed harness on the merged tree (1920 x 1080)

Full `tools/capture_screenshots.sh`: one content failure, 08a.

| Shot | Merged | Stage head (its report) | Bar |
|---|---|---|---|
| 08a land RMS contrast | 0.107 | 0.112 | >= 0.12 (open, below) |
| 18 SSIM / structure gain | 0.646 / 0.336 (0.401 vs 0.064) | 0.644 / SSIM gain 0.114 over Low | >= 0.5 / >= 0.2 |
| 09 the water mirrors the aurora | 2.6 % | 2.7 % | >= 1 % |
| 19 shore foam within 5 m / 15-40 m out | 27.7 % / 0.0 % | 25.2 % | lift >= 4 points |
| 08b the schools change | 23.46 % of the frame | 23.8 % | |
| Moods: closest pHash pair | 10 (Sommerdag-Høst) | 10 | >= 9 |
| Midnattssol water / sky ΔE | 8.39 / 8.76 | 8.9 / 8.8 | <= 10 |

08a, A/B with `--only=fjord_day` on this machine (load 11-21), the same
live state (day 0, Gråvær, drizzle, snow line 106 m): merged 0.107 twice
(2952 land samples), int head `1bea464` (the pads, no polish) 0.101 (2952),
stage head `d6362b1` (the polish, no pads) 0.112 (2681). The polish adds
+0.006 on either base; F2-19's carved pad_a and pad_b at the mouth add 271
samples of flat heath and take 0.005 off (`08a_merged_stage_int.jpg`: top
merged, middle stage, bottom int). The merge changes nothing else there.

`FacilityPresenterCapture` (36 shots): pads against heath ΔE 12.1 / 11.06
(home), 22.39 (land), 20.55 (pad_b), 21.18 (pad_delta), all a little over
int's; `tests/test_facility_presenter.py` 18 of 18 on the re-recorded
metrics. The OF1 ring in low winter sun averages (137, 118, 43), int's
evidence (132, 115, 46): the sun's light fix lifts it slightly; it still
reads mustard (RUN_LOG §8, F2-20).

Every full harness run printed 12 engine errors "indexing did not unpair
geometries from light" (`renderer_scene_cull.cpp`); the review's base and
head runs show the same 12, so they predate this merge.

## Frames looked at

| Frame | What it shows |
|---|---|
| `18_mirror_vs_no_mirror_vs_low.jpg` | 18 on High, the same frame without the mirror, the Low tier: the mirror holds both walls and the head; without it the water is flat slate; the Low tier also drops High's depth of field (its head is sharp) |
| `18_band_mirror_no_mirror_low.jpg` | The band the gate reads (the head's waterline +-130 px, the central half): the mirror's flipped head and walls against the twin's flat water |
| `01_main_menu_merged.jpg` | The menu in the tier's half pass: soft clouds, mirror-calm water holding both walls, the site and its boats (close to owner reference 2's mood) |
| `01_clouds_hero_pass_vs_half_pass_2x.jpg` | The menu's flat cloud at 2x, true contrast: per pixel (top) three nested arcs and a domed contour in the upper cloud; the half pass (bottom) soft, a faint step on the flat cloud's top edge |
| `08a_merged_stage_int.jpg` | 08a merged / stage / int: the carved pads at the mouth replace the stage's skerries |
| `merged_key_frames.jpg` | 01, 08, 08b, 10, 18, 19, 09, Midnattssol, Høst on the merged head: pads at the mouth, no caps inside the storm's pens, the aurora in the calm water |
| `facility_frames.jpg` | The pads at home, the full campus, the dusk hero, OF1 in winter sun and its hero, pad_b's campus: unchanged in layout from int, the pads carved and flat |

## Remainder

- 08a: 0.107 against 0.12 (RUN_LOG §8, the 08a row). The pads' flat heath
  is part of the live view now.
- Per-pixel clouds (Ultra's `cloud_pass` 0, underwater) still draw the
  contours of finding 6: a fractional last step into the cloud, or the
  remaining depth integrated when the march stops (the review's remedies).
- 18 at High blurs the head and its reflection by the depth of field at
  FJORD distance; owner reference 2 is crisp: an art-direction question
  for the depth of field's reach, not a gate.
- The stage's open items stand: P0-04 at 1440p (RUN_LOG §8), the D17 check
  of the competitor names in 18, the untracked `godot/data/geo` import
  files (the geo track's area, left alone).
