# F2-20 evidence: the facility presenter

Farm-types spec item F2-20 (§4, §5, §6.3, §7.2), branch `wave4b/gfx-farm-1`.
The company's land sites and offshore units drawn in the 3D world from the
engine's own units, phase by phase as their construction projects run, with
a click card and the spec's framings. Frames are downscales in `f2_20/`
(1280 px, construction 960 px); numbers are `f2_20_facilities_metrics.json`
(one windowed run of all 32 presenter shots, 1920 x 1080, seed 20261012), the
perf A/B is `perf/perf_f2_20_presenter_ab.{json,md}`, the golden traces
`f2_20_golden_diff.md`. The owner's land (L1, L2) and offshore (O1-O3)
references exist only as descriptions (`docs/art/OWNER_REFERENCE_BRIEF.md`),
so the frames are judged against those words; no reference image is
committed.

## What was built

| Piece | Where |
|---|---|
| The engine's read: units, lots and tanks in tank order (role, diameter, handed over, fish), each lot's F2-14 project phase, an offshore unit's F2-07 project phase and waiting reason; legacy units from their size and progress | `src/LakskongenSim/Core/FacilityVisualViews.cs`, bridge `GameBridge.FacilityVisuals.cs` → `SimBridge.get_facility_visuals()` |
| The presenter (sync by unit id, pads painted, lights, picking, framing) | `godot/scripts/visual/FarmFacilityVisualManager.gd` (GameScene, under `FjordEnvironment`) |
| A land campus: the engine's lots on the carved pad, per-lot construction, LOD bands, draws | `godot/scripts/visual/LandCampusBuilder.gd` |
| An offshore unit and its fleet per phase | `godot/scripts/visual/OffshoreUnitVisual.gd` |
| The anchored card | `godot/scripts/ui/facilities/FacilityWorldCard.gd`, `FACILITY_CARD_*` keys |
| Kit shader: per-instance build progress, lid, band, rotor (`INSTANCE_CUSTOM`), per-node `build_cut_m`, the roof-light glow; a double-sided twin for shells under construction | `godot/shaders/facility_kit.gdshader`, `facility_kit_build.gdshader`, `include/facility_kit_body.gdshaderinc`, `assets/models/facility_kit/LK_FacilityKitBuild.tres` |
| Camera focus targets (both clamps take the targets' discs) | `godot/scripts/environment/FjordCamera.gd` (`register_focus_target`, `focus_target`, `frame_position`) |
| QA staging (never the player's UI) | bridge `QaPlaceFarmTypeSet`, `QaStartLandBuild`, `QaExpandLandSite`, `QaStageConstruction`, `QaContractOffshore`; engine seam `SimulationEngine.QaContractOffshoreUnit` (`Core/SimulationEngine.QaStaging.cs`) |
| Capture scene and shots | `godot/qa/scenes/FacilityPresenterCapture.tscn`, `scripts/qa/FacilityPresenterCapture.gd`, shots `32_land_construction_*`, `44_facilities_*` |
| Perf harness | `PerfHarness.gd --facilities` (a 4-lot campus, 2 post-smolt modules, an OF1-class and a Havfarm-class unit) with `--sea-sites=12` |
| Tests | `FacilityVisualViewsTests.cs` (9), `UiFlowTests._test_facilities_render` (33 checks), `tests/test_facility_presenter.py` (13), `tests/test_land_kit_assets.py` (the kit shader through its include; the build twin) |

## The QA scenario

`po3_red_sone`. The QA set is built through the engine's verbs: an HFS lot on
pad_a (`CommissionLandSite`, or, for the construction shots,
`ApplyLandPermits` + `BuildLandSite` with the filed cases approved today), a
post-smolt site of two modules on pad_delta (`CommissionLandSite`), an
OF1-class unit on off_1 (`QaContractOffshoreUnit`: `BuildOffshoreUnit`'s
contract with the unlock gates waived; its project then stood complete, the
engine commissions it on its next day). The full campus grows lot by lot
(`ExpandLandSite`; one grid project raises pad_a from 40 to 50 MW, the four
lots need 43 MW).

## Acceptance

| # | Criterion | Measured | Result |
|---|---|---|---|
| 1 | Rendered tank and unit counts equal the engine's (1 HFS lot, 1 post-smolt site, 1 OF1 unit) | Every built shot: HFS 14/14 tanks drawn and whole = the engine's 14 ready; post-smolt 4/4; full campus 56/56; OF1 drawn, operational, service vessel alongside. UiFlowTests: three units, three drawn | Pass |
| 2 | The visible state differs per construction phase; `32_land_construction` differs by pHash Δ ≥ 9 between phases 2 and 4 | Tank rings (`p2`) vs sea works (`p4`): Δ 14. UiFlowTests: the six land phases and the handover, and the six offshore phases and operation, each draw a different state; in the completion phase exactly the engine's 6 handed-over tanks stand whole | Pass |
| 3 | Clicking a tank or unit opens its anchored card and focuses the camera (UI input) | UiFlowTests: a click on tank 1 opens the card ("Kar 1 · Byggetrinn 1 · 50,0k fisk") and frames its lot at 380 m with the focus on the lot's centre; a click on the unit opens its card and frames it at 220 m; Esc closes it | Pass |
| 4 | Against the 12-site P0-04 view a full 4-lot campus adds ≤ 20 opaque + ≤ 16 shadow draws, one OF1 + vessel ≤ 8 + 6; LOD2 casts no shadow, of LOD1 only the tanks | Home view: campus +10 / +0, OF1 + vessel +7 / +0. Shadow rule holds for every unit in every shot. Recorded, not budgeted: at the land framing the full campus +11 / +6, at the OF1 framing (LOD0) the unit + vessel +19 / +25 | Pass |
| 5 | Added p95 ≤ 1.5 ms at FJORD with a campus, 2 units and 12 sites; absolute p95 ≤ 16.7 ms | Paired A/B, 3 repeats, presenter shown vs `hide.FarmFacilityVisualManager`: FJORD p95 15.26 vs 15.26 ms (+0.00), p50 +0.05 ms (ratio 0.999). Machine load 63 → 7 during the run (other agents) | Pass |
| 6 | Removing a facility leaves the others' positions unchanged | UiFlowTests: the engine cancels the pad_b build (`cancel_land_construction`): its campus goes, pad_b turns back to heath, the HFS campus, the modules and the unit keep their nodes and positions; a lot more re-lays only its own campus (28 tanks) | Pass |
| 7 | No feed barge or fjord pen grid for land or offshore units | UiFlowTests: no unit id among the fjord sites or barges; the fjord's sites, pens and barges stand where they stood | Pass |
| — | The mirror pass layers | The OF1 net on `PlanarReflection.NO_MIRROR_LAYER` at every LOD; the kits lay their submerged parts on the water plane (`mirror_pass.gdshaderinc`) | Pass |
| — | Graphics leave the games identical | `record_golden_traces.sh --base e98944d`: 27 of 27 traces identical | Pass |

Full campus LOD0 triangles: 155,712 (budget ≤ 250 k).

The committed metrics and frames are the run on `279f4c1`. A second run of all 32 shots on `af45167` (after the per-day write skipping) gave every unit, tank, phase, fleet and draw count identical, ΔE within 0.3 (pad_a 22.46, pad_b 19.34, pad_delta 21.69, home 13.11 / 10.56) and pHash tank rings vs sea works 18: the water and clouds move between runs, the structure does not.

Mutations: `tank_progress` always 1 → the land-phase and handover checks
fail; gone units kept → the cancel and freed-pad checks fail; the offshore
phase ignored → the offshore-phase check fails; in the engine read every tank
ready, the offshore phase index + 1, a stream draw in the view → one xUnit
test each fails; a SimBridge write in the presenter, a QA verb in a UI script
→ the pytest source rules fail.

## Perf (FJORD, land and hav framings; `perf_f2_20_presenter_ab.md`)

| Segment | p95 shown | p95 hidden | Δ p95 | p50 ratio hidden/shown | Draws shown / hidden |
|---|---|---|---|---|---|
| fjord (the P0-04 view, 12 sites) | 15.26 | 15.26 | +0.00 | 0.999 | 510 / 477 |
| land (520 m, −40°, sweep) | 13.26 (best) | 12.97 (best) | +0.29 | 0.896 | 198 / 173 |
| hav (220 m, −18°, sweep) | 12.78 | 12.36 | +0.42 | 0.953 | 166 / 126 |

Per sim day (the presenter syncs on every state update), measured headless,
best of 3 x 300 updates: 6.4 µs with no farm-type unit (the read 4.8 µs);
422 µs with the QA set, of which the bridge read (`get_facility_visuals`,
the C# view and its Godot dictionaries) 374 µs. A day that changes nothing in
a campus or unit writes nothing to the renderer.

U2-07's "30×: p95 of the UI side of a day" check failed in three of five
full UI-flow runs on this branch, at load averages of 11-67 from other
agents; it passed in the first (p95 10.1 ms). A/B of that check alone (the
presenter in GameScene vs removed, 6 pairs in alternating order, load 11-17):
p50 4.56 vs 4.72 ms, p95 10.95 vs 24 ms (medians): no effect from the
presenter; the failures follow the load (RUN_LOG §8).

The window came up at 1920 x 1147 (the display's limit, not 2560 x 1440);
two land repeats were hit by load spikes (p95 25-26 ms on both sides), so the
land row reads the least disturbed repeat. The Havfarm-class unit (F2-29) has
no model yet, so "2 units" draws one.

## Frames and the look

| Frame | Judged against the brief |
|---|---|
| `44_facilities_campus_day` | L1: a grid of dark lids on pale banded walls between low pale hall bays, the dark harvest hall with its roof lights, the orange truss bridge from the concrete quay, roads and an asphalt car park on the graded granite yard. Reads as a land farm on a blasted pad. |
| `44_facilities_campus_dusk` (sun +1.8°) | The windows and roof lights come up (light level 0.53); calm, cooling water. |
| `44_facilities_campus_dusk_hero` (sun −0.3°) | L2's wow: pink-violet sky over a mirror sea, the banded tanks and the orange bridge in silhouette, the hall's windows and the quay lamps lit, work-lights on the lids. |
| `44_facilities_campus_close` | LOD0: walkways, rails, rotor feeders, LOX tanks, gensets, the bridge over the lot road; tank shadows ground the rows. |
| `44_facilities_campus_full` | L1's ~70 tanks: four lots, 56 tanks in eight rows with halls between, the hall and bridge at the quay. |
| `44_facilities_postsmolt` | Two Ø16 m modules under their bays on the delta, the river intake; sparse by design (two of four modules). |
| `44_facilities_of1_winter_sun` (sun 10°, February), `_hero` | O2/O3: the safety-yellow 12-gon with its gantry, control block, silos and cranes, the net below, the orange DP service vessel at the walk-to-work landing; the coast low on the horizon in the hero. Hull-yellow pixels 14 % / 18 % of the unit's box (mean sRGB 134,115,43 / 172,142,66: the swatch #E6BE2E in low sun and shade). |
| `44_facilities_of1_yard_contract` … `_commissioning` | Survey buoys; pre-laid mooring pick-ups; the unit approaching behind a tug; three tugs bringing it in; tugs at the anchor bearings; the crew boat at the landing. |
| `32_land_construction_p0` … `p6` | Blast spreading in patches; ring foundations and roads; walls rising open-topped (the inside of the far wall drawn); hall bays and harvest hall rising; quay, apron and bridge legs; utilities; the first six tanks handed over with lids and feeders. |
| `44_facilities_home` | The campus as a landmark at the mouth, the unit a yellow sliver with its vessel beyond. |

Seen and left: the dark harvest-hall cladding (#3A4046) takes a navy cast
from the clear sky's ambient light on its shaded walls (it is the world's
lighting, not the swatch); the OF1 yellow reads mustard in low winter sun, as
the lookdev row of 2026-10-20 found under AgX.

## Not done here (RUN_LOG §8)

- The F2-31 ghost (`FarmGhostLayout` on `LandCampusLayout`) does not yet use
  the presenter's layout, so a ghost on pad_a and the campus built there
  differ in detail.
- A pilot lot's expansion to a full lot draws no construction of its own
  (the pilot stands as it is until the engine makes it a full lot).
- `OffshoreUnitModel` (F2-26) keeps 8 m visibility-range margins on its LOD
  pair, the pattern that hid the harvest hall here.
