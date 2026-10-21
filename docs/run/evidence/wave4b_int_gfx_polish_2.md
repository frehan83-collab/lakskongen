# Wave 4b integration: `wave4b/gfx-polish-2` merged into `wave4b/int`

The G3-04 stage (the underwater view: the sim's water clarity, the surface
seen from below, generated caustics, light shafts, marine snow, the net's
fouling from the sim's net condition) merged into `wave4b/int` on
2026-10-21 (`--no-ff`, `b302b14`), on top of `f414134` (gfx-farm-2 merged).
The stage's own evidence is `g3_04/README.md`, `g3_04/` and
`g3_04_golden_diff.md`. This file is the merge: the conflicts, what the gates
found, the review's findings and how each was handled, the gates on the
merged tree, and the frames looked at. Frames are
`wave4b_int_gfx_polish_2/` (JPEG, at most 1280 px); the golden traces
`wave4b_int_gfx_polish_2_golden_diff.md`; the REALTIME sky's cost
`perf/perf_wave4b_int_uw_sky_ab.md`.

Commits: `b302b14` the merge, `f2e03d5` the underside's swell the gates
found, `6b91900` findings 1-3, 5-7 and 9 with their UI checks, `56a1ab1`
finding 4's harness checks, finding 8, the snow's hitch and the cost
measurement, then this document.

## Merge

| File | Conflict | Resolution |
|---|---|---|
| `godot/scripts/environment/EnvironmentDirector.gd` | In `_apply_water`, int's open-sea lines (`exposed_hs` from `OpenSea`, `swell_hs`, `open_sea_plot`) and its `set_target_sea` and `drawn_components` against the stage's `_apply_underside` call and function | Both: int's three lines, then the stage's call; the three functions side by side |
| `godot/scripts/environment/FjordCamera.gd` | `_snap`: int's snapped pitch (the offset and the top-down blend, F2-23's hand-back) against the stage's underwater field of view and turn reset | Both: the stage's field of view and `_turning` reset with int's pitch (`solve_rig` ignores the pitch under water) |
| `tools/run_ui_flow_tests.sh` | MIN_CHECKS 1905 (int) against 1825 (the stage) | Measured on the merged tree: 1936 = 1905 + the stage's 31; then 1945 with the review's 9 |
| `docs/RUN_LOG.md`, `docs/BALANCE.md`, `docs/FJORD_RENDERER.md` | §7 rows; int's teaser-day subsection against §1ab; int's open-sea subsection against "Under the surface" | Both sides, int's first |
| `UiFlowTests.gd`, `project.godot` | Auto-merged | Read: `_run()` runs UnderwaterChecks after GfxPolishChecks and int's three checks files after `_test_facilities_render`; `[shader_globals]` gains `lk_uw*` beside int's `camera_bookmark` action |

**Found by the gates:** `tests/test_underwater.py::test_the_underside_moves_with_the_water`
failed on the merge commit ("displacement: the underside's copy drifted from
the water's"): F2-21 gave the water's vertex stage the open sea's swell
(`water_swell.gdshaderinc`), and the stage had copied the vertex stage before
it. The underside includes the swell now and takes its slope in the window's
normal; the director copies `swell_k`, `swell_amp` and `swell_peak` to it
(`f2e03d5`; shader-check: 37 shaders compiled under Metal 3.2).

## Review findings

All nine confirmed on the merged tree, with a scratch probe (untracked,
removed) that re-runs the review's (`ReviewCross2`, `ReviewDive`,
`ReviewGhost`, `ReviewProbe`, `ReviewFrame`: a stepped dive and surfacing,
the ghost hides, a storm, land and a framing), or by code read; none
rejected. Two of the review's numbers were reproduced exactly: the dive's
lower frame from (70,93,98) to (103,133,136) with the pop to (19,77,73) at
frame 14 and a step at 22; the bloom water at (17,81,31), hue 133 degrees.

| # | Severity | Finding | Handling | Proof |
|---|---|---|---|---|
| 1 | high | Every dive showed a pale wall over the water for 13 frames, then popped | The fog's colour under water is the sky's radiance map (aerial perspective 1), which the scene's sky rebuilds a layer a frame (incremental: the roughest layer, the fog's, comes last, and a change waits for the cycle under way). Under the surface the sky now runs REALTIME (the whole map every frame, fast filter); above it the scene's mode comes back, rebuilt whole on the switch (`EnvironmentDirector._apply_sky_mode`). The fog, the ambient light and every reflection read the water from the first frame | Stepped dive (harness check 7): the lower frame's luminance within 4.8 levels of the settled view in the 20 frames after the crossing, 2.9 a step (the stale map: 56.7 and 57.2). UnderwaterChecks: REALTIME under, the scene's mode above. `dive_crossing_before_after.jpg`. Cost +0.34 ms p50 under water (`perf/perf_wave4b_int_uw_sky_ab.md`) |
| 2 | medium | Fogged geometry showed as ghosts (a pale float 18 m off) and a seam at the horizon | Two causes. The water's sky had a clamp at the deep and a cos^4 glow, which no blur keeps in shape, and its pre-sharpening was the incremental filter's (0.62). Now it is an exact linear function (horizontal, a slope in height, a slope toward the refracted sun: the glow by its first two spherical harmonics), which an isotropic blur keeps linear: the radiance pass draws the slopes steeper by 1/0.93, the REALTIME filter's factor, measured, and the director keeps the steepened function over 5 % of its mean. And the surface from below faded its dark mirrored water at 0.45 of the fog's attenuation, so a fully fogged float in front of it stood pale: the mirrored water now fades at the fog's own (`underside_fog`), only Snell's window keeps the 0.45 veil. Under water the far plane stands 20x out (the map level the fog reads sharpens toward it) | Calibration: a black sphere 12, 30 and 300 m round the camera in a fog that hides it wholly, against the sky alone: mean \|dL\| 0.37 of 255 at 0.93 (0.62 left 3.46). Harness check 8: hiding the terrain and the 94 meshes past the fog's reach changes p99 1.43 levels, none over 3 (the stage: p99 5.72, 18 % of the pixels over 2); hiding the centre float changes its region by 0.45 levels (the review measured the pale float at 4.4). `ghosts_before_after.jpg` |
| 3 | medium | The dive cut through the bird net (its threads as giant green bars for a frame); the way up showed two frames of a flat blue underside | While the view dives, is under or is on its way out (3 s), the bird net of the pen it dives into dissolves by the camera's place: gone within 30 m of the centre and under 8.5 m, whole beyond 45 m or over 25 m, the same both ways and whole again before the site's far LODs take over (`UnderwaterView.bird_net_shown`, `PenCage.set_bird_net_fade`). The lens's veil comes up over the last 0.8 m below 0.6 m on the way up, in the water's colour as the screen shows it, both ways | UnderwaterChecks (the dive stepped at 60 Hz): inside the net under 7 m, the crossing within 14 degrees of vertical (0.097 m across per metre down), no frame inside the pen under 8.5 m with the bird net showing, the band veiled, whole again after. `dive_bird_net_before_after.jpg`, `surfacing_before_after.jpg` |
| 4 | medium | The dive's path and the underside's runtime uniforms had no failing test; the clean-net caustic bar was half the measured value | UnderwaterChecks +9 (below); the harness's checks 7 and 8; `MIN_CAUSTIC_CHANGE` 0.002 to 0.007 | The review's mutants (a) no hover and (b) `UNDERSIDE_COPIED := []` each fail now; (b) only after the check read the shared uniforms from the two shaders: its first version iterated the director's list, which the mutant empties (it passed 40 of 40) |
| 5 | medium | Rain and snow fell under water | `WeatherController.set_submerged`, set by the director every frame: both emitters stop and hide under the surface, restart full in the air | Storm probe: the particles changed up to 88 levels under water, 0 now. UnderwaterChecks: under a storm none under the surface, both back above. `land_and_storm_before_after.jpg` |
| 6 | medium | The UNDERWATER view went into land (the wheel at a land point: 1 m under 249 m of ground; a framing while under water kept the view and solved it on land) | `FjordCamera.can_dive_at`: in a pen, or over at least 7 m of water; the wheel and the API stop at FARM elsewhere; in open water the camera comes in toward the focus over shallows. `frame_position` and `hold_view` leave the underwater view first. The ambience follows the camera itself below the surface | UnderwaterChecks: over a land pad the view does not dive (the camera 173 m over the ground) and into a pen it does; a framing and a hold from under the water surface first. `land_and_storm_before_after.jpg` |
| 7 | medium | The clean net's caustics moved about one level of 255 (the stage's disclosed partial) | Captain's art call under the owner's mandate: under water a thread's sun-facing top takes the caustics at an albedo of at least 0.05 (the coated net is 0.016; the mean light unchanged, above water off) | 11d, time stopped: clean 0.0083 (0.0039 before, 0.0040 with the mutant), fouled 0.0088, held off 0 |
| 8 | low | The bloom water is emerald, not olive | Confirmed (hue 133 degrees, saturation 0.79). The optics' answer: the water over the camera has taken the red (0.40 /m), which a bloom cannot give back; olive is the bloom from above. The docs and `UnderwaterOptics` say so now; the constants stay | `FJORD_RENDERER.md` "The light", `g3_04/README.md` |
| 9 | low | Offshore units took the 6 m fallback water | Under water away from the fjord's sites the view reads `GetAllSeaSiteDetails` at most once a second and takes the unit's water round its plot's slot (a fast game pays nothing above water). The sim's season at a unit is still the fjord's (RUN_LOG §8) | UnderwaterChecks: an OF1 placed through the engine, the dive at its slot takes its water from the sim (10.62 m), and under the open sea's swell (peak 0.07 m) the underside carries all 19 uniforms it shares with the water |

**Found by the captain's measurement (not in the review):** every dive's
crossing frame took about 100 ms more (a stepped dive's crossing step 122-125
ms against 21): the marine snow's restart preprocesses its 12 s lifetime at 30
steps a second. It is the snow (without the preprocess 41 ms; with the sky
staged incremental still 123 ms). The snow is simulated at 10 steps a second,
interpolated: 56 ms, the box still full at once (UnderwaterChecks +1).

The UnderwaterChecks added (40 in the file, 31 the stage's): the dive's path
stepped at 60 Hz; the water's sky; the storm's particles; no dive over a land
pad; a framing and a hold surface first; an offshore unit's water; the
underside's shared uniforms under the swell; the scenario started again
after the offshore check; the snow's fill. Mutations on a scratch runner of
UnderwaterChecks alone, each caught: no hover, the underside copying nothing,
no REALTIME switch, rain under water, a dive anywhere, a framing that stays
under, a bird net that stays, no veil band, no offshore read, a clipping sky
(blur 0.62, no scaling), no underside fog. Harness mutations, each caught:
the sky left incremental (dive 56.7 levels, ghosts p99 2.64), the stage's
blur factor (ghosts p99 3.22), the mirrored water at the window's veil (the
float 1.82 levels), no thread caustic albedo (clean 0.0040).

Replaced by stronger tests (RUN_LOG §7): the ambience check moved the
camera's zoom name, it now moves the camera under the surface and holds the
UNDERWATER view in the air (the dive's hover: no bed); the single-writer
lists follow the renamed underwater uniforms and gain `uw_blur_slope` and
`underside_fog`; `MIN_CAUSTIC_CHANGE` 0.002 to 0.007.

## Gates on the merged tree

On `56a1ab1` (the code; the commit after it adds documents, frames and
MIN_CHECKS only), the scratch probes removed from the tree:

- `dotnet build LakskongenSim.sln` and `godot/LakskongenGame.csproj`: 0 errors
  (53 and 16 warnings, as before).
- `dotnet test LakskongenSim.sln`: LakskongenSim.Tests 2026 passed, 3
  skipped, 0 failed of 2029 (int's 2019 + the stage's 10 `WaterClarityTests`;
  load 9.6 -> 26); 31, 4, 5, 10 and 25 in the other projects. The merge
  commit's own run: the same.
- `python3.12 -m pytest tests/ -q`: 895 passed, 1 skipped (the merge commit
  failed `test_the_underside_moves_with_the_water` before `f2e03d5`).
- `tools/validate_scenes.sh`: 85 scenes, failures=0.
- `tools/run_ui_flow_tests.sh`: checks=1945 failures=0, PASSED (6:50, load
  4 -> 10); on the merged tree before the review's checks 1936 / 0.
  MIN_CHECKS 1945.
- `tools/record_golden_traces.sh --base f414134`: 27 of 27 traces identical,
  math fingerprints equal (`wave4b_int_gfx_polish_2_golden_diff.md`); the
  merge commit alone, too. The merge and the review change no game.
- `godot_mcp_ops.py shader-check` (windowed): 37 shaders compiled under
  Metal 3.2 Forward+, no errors.

## Windowed captures (1920 x 1080)

`--only=underwater` on the code head (`11_underwater.json`, failures []):

| Check | Bar | Merged |
|---|---|---|
| 11: the camera 3-8 m down inside the net | depth 3-8, < 24.95 m | 5.0 m, 17.96 m |
| 11c: Snell's window over the TIR ring, tinted | >= 1.8x, green over red | 2.12x (the stage's 2.37-2.42: the mirrored water now converges on the fog's colour, so the ring outside the window is lighter) |
| 11b: the bloom's fog over the clear water's | denser | 0.839 /m against 0.269 /m (2.35 m against 11.53 m) |
| 11: fish | >= 1 % | 1.53 % (the stage's 2.92-3.31 %: its distant fogged fish stood out against a mismatched background; now they vanish into the water like every fogged thing, and the near school reads as before) |
| 11d: caustics on the net, time stopped | clean >= 0.007, fouled >= 0.005, off <= 0.0005 | 0.0083 / 0.0088 / 0.0 |
| 11e: fouling | >= 10 % | 40.2 % |
| 7: the dive's frames after the crossing | <= 12 from settled, <= 8 a step | 4.8 / 2.9 |
| 8: the fog's ghosts | p99 <= 2, <= 0.2 % over 3, float <= 1.5 | 1.43 / 0 % / 0.45 |

The default harness, run twice on the head (49 frames): the first run
failed 08a (0.107 against 0.12, as on int since gfx-polish-1: RUN_LOG §8)
and the moods' pHash (sommerdag and hoest 8 bits, min 9); the second only
08a, the moods' closest pair 10 bits. The moods alone (twice), after the
underwater group and after the mirror and foam groups all read 10: in the
full run sommerdag's frame carries the lice shot's red rings and the water's
and the cloud shadows' phase at that moment, which moved its hash 2 bits
(run 1 `c090d4f5bdbf3980`, run 2 `c090d4f53dbf39c0`). A thin margin on a
pre-existing check, not this merge (RUN_LOG §8). The rest: 18 SSIM 0.646,
structure gain 0.351 (min 0.2); 09 aurora 10.4 %, mirrored 2.7 %; 19 foam
19.5 % within 5 m of the shore, 0 % out at 15-40 m. Above the water the
frames read as on int (08b, 10, 12, 18 looked at: the merge and the review
change nothing there; the far plane, the sky's mode, the particles and the
bird net are under-water or dive-only).

## Frames looked at

| Frame | What it shows |
|---|---|
| `dive_crossing_before_after.jpg` | Frames 2, 8, 12, 14 and 22 after a stepped dive's crossing: before, the pale wall (the day's sky as the fog's colour) over the lower frame, then the pop at 14; after, the water's teal from the first frame under the fading veil |
| `ghosts_before_after.jpg` | 11's framing with the fish and effects off and time held: before, the pale centre float, the net's ring and a seam where the fogged seabed meets the background; after, the water's smooth gradient under Snell's window. Below, \|dL\| x 20 with the terrain hidden: before the whole seabed lights up, after a faint banding (p99 1.43 levels) |
| `dive_bird_net_before_after.jpg` | Before: the dive at 7.8 m over the RIM roof and at 5.5 m inside it (the roof's threads as green bars; the event card is the probe's own QA bloom from its previous mode). After: the roof dissolving as the camera comes down (0.71 at 18 m, 0.12 at 12 m, gone under 8.5 m) |
| `surfacing_before_after.jpg` | 0.58 and 0.11 m down and 0.27 m up on the way out: before, the water material's flat blue underside, then the air's bright fog as the veil; after, the water's colour veiling the band and fading into the air |
| `land_and_storm_before_after.jpg` | Before: the wheel over land puts the view 1 m under the mountain (Snell's window inside it); after, over a land pad the wheel stops at FARM. Before: a storm's rain streaks under water; after, none |
| `underwater_shots_merged.jpg` | 11, 11c, 11b, 11d and 11e on the code head, and 11d's caustics: \|dL\| x 10 for 0.5 s on their clock with time stopped. 11: Snell's window, the shafts, the school near and fading with distance, the snow; 11c: the sun and its glow inside the window, the teal ring outside; 11b: the bloom's deep green murk; 11d: the dark net with the shafts behind; 11e: the olive growth closing the mesh |

## Remainder

RUN_LOG §8, "Remainder (wave4b int, gfx-polish-2 merge)": an offshore unit's
water uses the fjord's season (an open-sea clarity profile is a sim item);
the caustics' cells are larger than the 11d close-up, so the clean net's
shimmer reads as moving light more than as a network; the bloom's deep green
is saturated (the optics' answer); the stage's followups (the owner's
baselines for 11-11e, the fouling read from `NetIntegrityPct`, the harness's
winter water under a summer noon, FARM p95 at 1440p, the load-sensitive 30x
check); the mood goldens' thin pHash margin in a full harness run (8 once,
10 once; 10 in every partial run).
