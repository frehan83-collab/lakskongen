# F2-23 evidence: first sight, teaser, showcase shots, night lighting

Farm-types spec item F2-23 (§5.4, §5.5, §6.1 item 1, §6.4, §7.2), branch
`wave4b/gfx-farm-2` from `wave4b/int` `1bea464`. The company's first unit of a
variant comes into operation in one sequence of at most 20 s; a repeat gets a
toast; reduced motion gets a still. In Første sesong a rival's yellow hull is
towed past the mouth once (the demo's wishlist hook). Campuses light up at
dusk. Frames are 1280 px downscales in `f2_23/` from one windowed run of all
22 ShowcaseCapture shots (1920 x 1080, `po3_red_sone` seed 20261012 with the
QA facility set; the teaser's four shots in their own run in `forste_sesong`,
no farm-type unit), numbers in `f2_23_showcase_metrics.json`; the behaviour
runs headless in the UI flow suite (`godot/tests/FirstSightChecks.gd` 21
checks, `godot/tests/TeaserChecks.gd` 24) and pytest
(`tests/test_showcase_shots.py` 8). The owner's references L1, L2, O2 and O3
exist only as descriptions (`docs/art/OWNER_REFERENCE_BRIEF.md`); the photos
are not in the repo and were not looked for, so the checklist below judges
the frames against those words.

## What was built

| Piece | Where |
|---|---|
| The first sight: a variant's first unit in operation (the presenter's sync counts the engine's units of each variant that were running before), skippable (Esc, a click), the clock held and released exactly, a 4 s construction timelapse replayed through the presenter's own drawing (the land lane's phases in order, the tanks handed over through the last two; the offshore part groups), the camera on Catmull-Rom keys anchored to the unit (the quay, the bridge, the lots; the unit, its vessel) ending where the camera's own framing of the unit stands, the land's lights switched on in a wave from the quay, a wellboat coming in offshore, the end card ("Første i drift", variant and place, "Lagre bilde": the frame without the overlay to `user://screenshots`, "Fortsett"); a repeat gets a 3 s toast that flies the camera there; reduced motion: a still of the unit's framing cross-faded over 2 s | `godot/scripts/visual/FirstSightDirector.gd`, `FirstSightOverlay.gd`, `godot/data/first_sight.json`, keys `FIRST_SIGHT_*` |
| What a cinematic borrows and gives back: the clock hold, the presentation hour pinned to a sun elevation (evening or, `sun_rising`, morning), clear weather, the mood hook, the UI and toasts hidden, the camera rig stopped and its keys off | `godot/scripts/visual/CinematicStage.gd` |
| The mood hook (Blåtime; the low winter sun): the composed mood leans toward a named mood by a strength | `godot/scripts/environment/EnvironmentDirector.gd` (`set_mood_hook`) |
| The teaser: Første sesong, once, after day 150 while no tutorial step asks anything of the player, never from day 300; a parody rival's OF1-class hull at its transit draft (no net) towed by three anchor tugs on tow lines past the mouth toward hav_1, a 13 s telephoto shot in the morning sun, then a news card that offers "Fortsett" only; presentation only | `godot/scripts/visual/TeaserDirector.gd`, `first_sight.json` "teaser", keys `TEASER_*` |
| Night lighting: the kits' windows, roof lights and the lids' work-light pools keep their screen brightness as the eye adapts to the night (`night_comp`), switch on in a wave in a first sight (`light_wave_*`); the campus's lamps (one camera-facing glow per lamp: the tanks' four rail lamps, the quay masts, the hall's wall lamps, a lamp a bridge span, road masts) and warm pools of light on the ground (OmniLights whose energy the director writes, the only writer of light energy) | `godot/shaders/include/facility_kit_body.gdshaderinc`, `godot/shaders/campus_light_glow.gdshader`, `godot/scripts/visual/CampusLights.gd`, `LandCampusBuilder.gd`, `FarmFacilityVisualManager.gd` (`_update_lights`), `EnvironmentDirector.gd` (`LIGHT_POOL_GROUP`) |
| Landmarks at the home view: the hull and the pale walls keep a share of their own light through haze from 900-1,800 m (never in rain, snow or a storm, never at night), so the OF1 and the campus read in overcast (spec §5.5 rule 1) | `godot/shaders/offshore_kit.gdshader`, `facility_kit_body.gdshaderinc` (`landmark_lift`) |
| Shots and their content checks | `godot/qa/shots.json` (`30_land_campus_day`, `31_land_dusk`, `32_land_construction`, `33_offshore_of1_winter_sun`, `35_offshore_storm`, `36_home_{overcast,rain,snow,storm}_landmarks`, `34_first_sight_*`, `34_teaser_*`), `godot/qa/scenes/ShowcaseCapture.tscn`, `godot/scripts/qa/ShowcaseCapture.gd` |

## Acceptance

| # | Criterion | Measured | Result |
|---|---|---|---|
| 1 | The first completion plays one ≤ 20 s sequence that opens with a 3-5 s timelapse; it is skippable, and the sim day and stream positions are unchanged | The first HFS site the engine builds plays its sequence: 20.0 s, timelapse [0, 4] s from the bare pad (0 tanks whole, blast 0) to the engine's 14 handed-over tanks; the clock held (a clock running at 30× for 0.6 s moves no day); Esc skips it, the clock and UI come back and the camera stands on the campus's own framing (410 m); day and `qa_state_digest` unchanged. Offshore (34_first_sight_hav_*): the unit rises part by part, the camera from behind the service vessel to the framing, a wellboat in. A unit that starts in operation (commissioned at once, a loaded save) plays nothing | Pass |
| 2 | A second completion of the same variant shows a toast only | The second HFS site: no sequence, the toast "Landanlegg (hybrid) i drift på Skjærvika" | Pass |
| 3 | Reduced motion gives a still and a 2 s cross-fade | The offshore unit's first sight with reduced motion: the still's alpha 1.00 → 0.00 over 2.0 s, the camera holds, 7.0 s in all; a click skips it; digest unchanged | Pass |
| 4 | Shot 33: yellow-hull pixel fraction ≥ 1.5 %, mean water saturation ≤ 0.35 | 1.583 % of the frame; water saturation 0.252 (sun 4°, frostroyk) | Pass |
| 5 | Shot 36 (home, overcast): OF1 hull-hue pixels ≥ 200 and campus silhouette ΔL ≥ 15 against the strandflat; the status chip visible in rain, snow and storm | 299 hull-hue pixels; ΔL 17.4 (per pixel against the strandflat's mean; the difference of means 17.2); the chip visible and on screen in rain (fog transmittance at the unit 0.108), snow (0.036) and storm (0.027) | Pass |
| 6 | The teaser leaves the engine state hash unchanged, plays once in `forste_sesong` after day 150 outside any tutorial step, and never offers a build | Nothing through day 150; on day 151 the first step ("open_pens", a do step) holds it back; with no step active it plays on day 151; the clock held (30× for 0.6 s: no day), Esc gives back the clock, UI, camera view and keys; `qa_state_digest` unchanged; the card offers `["ContinueButton"]` only; the days after bring nothing (1 play); a game first seen on day 156 never plays it; `po3_red_sone` never; reduced motion: still + 2 s cross-fade. The rule's ten cases (`TeaserDirector.due`) include a wait step (plays), day 299 in a do step (waits), day 300 (never) | Pass |
| 7 | The art checklist against owner refs L1, L2, O2 and O3 is signed | Below | Pass (signed) |

Also measured: shot 30 (the land framing) −40.0° from 588 m with 8 of 8 campus
corners in frame and 1,108 pipe-bridge orange pixels; shot 31 (dusk, Blåtime,
sun −3.5°) adds 1,421 lit pixels with the lights on (A/B), sky hue 0.600;
shot 35 the drawn open sea at Hs 4.8 m with 2.81 m of swell, the chip red
with its square; at dusk the campus burns 79 lamps and 4 pools, by day none.

## Art checklist

Against `docs/art/OWNER_REFERENCE_BRIEF.md` (the owner's words; no photo).

| Ref | What the reference shows | In the game | Frames | Verdict |
|---|---|---|---|---|
| L1 (render, overcast) | ~70 round tanks in two sizes, pale grey walls, dark covered lids | 4 lots × (12 Ø28 m + 2 Ø16 m) = 56 tanks on pad_a, pale walls, dark lids; the post-smolt modules stand on the delta pad | 30, 34_land_t18 | Matches (56 + 4 at the QA set's build-out) |
| | Rows between long, low white/grey service buildings with roof vents | A process hall between each lot's two rows, roof lights in bands | 30 | Matches |
| | A large dark-grey harvest/processing hall | The harvest hall at the quay, #3A4046 | 30, 31 | Matches |
| | An orange steel truss bridge on legs from a concrete quay with a wellboat berth to the tank rows | The pipe bridge on legs from the quay over the first lot road | 30, 31, 34_land_t07 | Matches; the wellboat at the quay is F2-22's |
| | A curved intake/outfall basin at the shoreline | A rectangular intake basin beside the first lot row | 30 | Partly: not curved |
| | Parking, a road, a small orange substation; rocky heath, cliffs, calm grey sea | Car park, roads, the utilities row from the substation (#D9622B); the blasted pad on the strandflat under the cliffs | 30 | Matches |
| L2 (dusk photo) | ~12 big tanks with banded walls (grey over green/white) | White walls over a green band | 34_land_t11, 31 | Matches |
| | Flat white lids | Dark lids, the spec's default (§5.1: #2A3036 after L1; L2's #EDEFEF is the cosmetic variant the kit carries) | 34_land_t11 | Deliberate difference (spec) |
| | An orange truss conveyor bridge into a tall grey building | The bridge into the harvest hall | 31, 34_land_t07 | Matches |
| | A blasted granite pad, skerries | The pad's blasted granite; the strandflat's skerries | 34_land_t02 | Matches |
| | A pink-violet dusk sky over calm sea: the "wow" mood for land sites | Blåtime (sun −3.5°, mood hook 1.0): pink-violet afterglow over calm water, the lids' work-lights, windows and lamps on | 31, 34_land_t07, t11, t18 | Matches |
| O2 (render) | The yellow ring: columns and pontoons, diagonal bracing, a walkway gantry across the middle, a white accommodation block, grey feed silos, cranes, the net below the waterline | All present on the 12-gon (the owner's ruling), the net hanging in the ring | 33, 34_hav_t17 | Matches |
| O3 (photo, Frohavet, low winter sun) | Far from shore in deep blue open water | Off_1's deep water; spec §4 compresses the distance so the unit reads from the home view, so the coast stands closer than in O3 | 33, 34_hav_t17 | Partly: the isolation is compressed by design |
| | An orange-hulled service vessel alongside | The service vessel at the walk-to-work landing | 33, 34_hav_t06, t17 | Matches |
| | A fast-boat wake | No wakes on the offshore boats (the crew boat's runs are F2-22's) | — | Not shown |
| | Snowy low hills on the horizon | Snowy coast beyond the unit | 33, 34_hav_t06 | Matches (the hills are the fjord's, higher than O3's) |

Signed: Claude (the gfx-farm-2 agent), 2026-10-21, against the owner's
descriptions. The owner's own look at the frames against the photos is
welcome but is not asked for (the owner's mandate).

## Mutation checks

| Mutation | Caught by |
|---|---|
| The stage holds no clock | FirstSightChecks (the clock, the day at 30× moved to 18, the digest) and TeaserChecks (the same three) |
| The timelapse hands every tank over at once | FirstSightChecks: "the timelapse builds the campus from the bare pad (14 tanks whole …)" |
| The teaser ignores the tutorial's do steps | TeaserChecks: the rule's cases (2), "on day 151 a tutorial step … holds it back", and the camera check after it |
| The teaser never records that it played | TeaserChecks: "it plays once (day 154: 2 plays)" |
| The teaser advances the engine a day | TeaserChecks: the day at 30×, the hash, the reduced run's hash |

## Decisions (RUN_LOG §7, 2026-10-21)

- The teaser's "no tutorial step is active" is the tutorial not running or
  its step a wait for the world ("grow", "sale": the clock runs, the card asks
  nothing). Probe (the minimal player, 6 seeds): the tutorial runs to day
  283-318, so with "not running" alone the teaser would be skipped on 4 of 6
  seeds; with wait steps it plays on day 151 on all six (during "grow",
  days 0-257..290).
- "Once" is per timeline, with no save field: the director arms when it sees
  the game at or before day 150 and plays at most once; a save loaded after
  day 150 never plays it (nor a game first seen there); a load of an earlier
  day arms it again.
- The rival is "SalMax Group" (the roster's parody, D17) through
  `TEASER_RIVAL`; the Leksikon entry and the locked "Hav" card after the
  news card are F2-35's (`TeaserDirector.finished`).
- The teaser's light is the morning sun at 32° at the camera's back (it
  looks out west): at the afternoon's 24° the hull was a pale backlit
  silhouette (hull-yellow pixels 0, hull-hue pixels 1,193 at 6 s, against
  6,804 now).
- Night lights keep their screen brightness through the night's exposure
  (`night_comp` = 1.6 / exposure, clamped to 0.08-1), so windows do not
  bloom at the night's 16×.
- The pools of lamplight are OmniLights whose energy only the director
  writes (`LIGHT_POOL_GROUP`; the owner names it, the director sets it each
  frame): the G1-02 rule that one writer owns light energy.
- The landmark lift is gated to haze (transmittance 0.25-0.5 and up): in
  rain, snow and storm the chip carries the read (§5.5 rule 2), a glowing
  wall in the murk would be wrong.

## Remainder

- The teaser's Leksikon unlock and locked "Hav" card (F2-35).
- The offshore boats' wakes (O3's fast-boat wake) and the whitecaps' flat
  patches in the storm (`35_offshore_storm`, the G2-03 system).
- The fjord's floating gear does not ride the swell (the swell is exposed-sea
  only; `SiteFloat` reads the fjord's wind sea).
