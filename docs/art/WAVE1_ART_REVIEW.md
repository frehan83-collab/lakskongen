# Wave 1 art review (merge captain, 2026-10-14, integration/wave1 @ 90a0cf0)

Frames: godot/screenshots/game/*.png in the main checkout (1920x1080), reviewed against
OWNER_REFERENCE_BRIEF.md refs 1-5 and the land/offshore set.

## What works now
- 08 FJORD day: one continuous U-trough fjord, no shards, horizon in frame, deep blue water with
  wind-driven wave texture (close to ref 1's sunny deep blue), skerries at the mouth.
- 08b FARM: the A2-01 pen reads as equipment (black HDPE collar, handrail, health ring on the water,
  dashed selection ring), workboat with yellow crane and the white feed barge read as real vessels.
- 09 night: the aurora is beautiful (curtains, stars, dark cloud silhouettes).
- 03 dashboard: Chart paper surfaces, Barlow headings, Norwegian text, readable KPIs.

## Gaps against the owner references (Wave 2 priorities)
1. **A site is one ring.** Ref 1 shows 2 rows of round pens in a mooring grid, white feed hoses from
   the barge to every pen, yellow buoys at grid corners and along hoses, bird-net poles (central tent
   or rim poles), a centre feed spreader. -> G2-04 + A2-01 completion (hoses, bird nets, spreader).
2. **Mountains are smooth clay.** No granite texture, strata, snow caps, birch/spruce on lower slopes,
   heath colours; the wet band at the waterline is missing. -> G2-02 (terrain material + vegetation).
3. **The world ends in flat ocean** at the fjord mouth: no far mountain silhouettes, reads as an island.
   -> G2-01 far field (and reserve an exposed sea area for offshore farms, buildable shore plots for
   land-based farms: see the farm-types spec).
4. **Night water is a black void**; calm water does not mirror sky/aurora/mountains (ref 2's slate
   mirror is the signature winter look). -> G2-03 planar reflection; G3-05 moods (sunny deep blue,
   calm-winter slate mirror, overcast grey) with the brief's hexes.
5. **Storm 10**: grey wash with rain streaks reads stormy, but whitecaps are scattered flecks, the
   mountains vanish entirely; keep the silhouette readable (fog curve), spray at pens.
6. **Main menu 01** is a flat dark panel with an emoji fish: no world, no wow. -> U1-09 live main menu:
   3D low-oblique winter calm fjord (ref 2), slow camera drift, logo lockup, no emoji.
7. **UI**: full-screen dashboard hides the world (U3-01 later); toasts cover "Hurtigvalg"; the weather
   alert repeats "8 ganger"; emoji icons in headers and tabs (U3-02); oversized bloom on the night label.
8. **Performance**: 1440p FJORD frame ~26-29.5 ms (target p95 16.7 ms): tier the sky raymarch,
   terrain shader, SSAO, DOF, AA (P0-04 harness first).
