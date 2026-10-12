# TECH_GRAPHICS — Open-World Fjord Rendering for LakskongenSim

Research date: 2026-10-12. Scope: Godot engine state (Oct 2026), technique shortlist with screenshot gates, competitor teardown, Blender→Godot asset pipeline, stay-vs-migrate verdict. Confidence: [high] = verified on official/first-party source, [medium] = inferred or secondary source, [low] = planner estimate, to be validated in-repo. No numbers are invented; estimates are labelled.

## Executive summary (10 lines)

1. **Godot stable is 4.7.2** (4.7 on 18 Jun 2026; 4.7.2 on 18 Aug 2026); **4.8 is at feature freeze** (dev 7, 29 Sep 2026) — repo already runs 4.7 + Forward+ [high].
2. Forward+ remains the only renderer with SDFGI, volumetric fog, TAA, SSR; it runs on Vulkan/D3D12/Metal — all relevant features for the fjord are Forward+ features [high].
3. macOS defaults to the native **Metal** driver (Apple Silicon only; Intel falls back to Vulkan/MoltenVK); **D3D12 is the default on Windows since 4.6** [high].
4. 4.8 brings screen-space contact shadows for directional lights and animated decals; feature-freeze status makes an upgrade window plausible late 2026 [medium].
5. Steam Deck: Godot exports native Linux (no Proton needed for that build); a 4.8-dev7 **known issue breaks Steam Deck built-in controls in Linux export** — pin 4.7.2 until fixed [high].
6. Biggest cheap wins for the fjord: **shore/foam water pass, sky/atmosphere + HDR output, terrain blending, GI quality tiers** — all days-to-weeks, not months [medium].
7. Skip FFT ocean for now: the top-down camera limits its payoff; current 4-wave Gerstner + foam textures already carry the look [medium].
8. Competitors cut corners loudly: FS25 iterates an old in-house engine; fishing sims ship "advanced water shader + RTX" as marquee features; Dredge sells *style over fidelity* — the bar is presentation, not simulation [high where sourced].
9. **Ships At Sea (Misc Games, EA since May 2024)** is the direct competitor — UE5, NVIDIA WaveWorks ocean, Lofoten map, fish-farming systems; watch its v0.8.8 farm rework [high].
10. Verdict: **stay on Godot**; migrating costs several engineer-months and buys nothing the current 4.7.2 + custom shaders cannot deliver for a management-sim camera [medium].

---

## 1. Godot state as of October 2026 [high]

| Item | Status | Source |
|---|---|---|
| Latest stable | **4.7.2** (maintenance, 18 Aug 2026); 4.7.1 on 14 Jul 2026; 4.7 feature release 18 Jun 2026 | godotengine.org blog/releases |
| Next version | **4.8 in development**: dev 7 (29 Sep 2026) = feature freeze; beta/RC expected next; stable plausibly late 2026–early 2027 [low] | 4.8 dev 7 post |
| Repo status | `godot/project.godot`: features `("4.7","C#","Forward Plus")`, MSAA 3D = 4× | repo |

**Renderer features relevant to an open-world fjord** (all Forward+ unless noted):

| Feature | Notes | Relevance |
|---|---|---|
| Forward+ | Clustered lighting; requires Vulkan, D3D12 or Metal | Only desktop option with full 3D feature set |
| Mobile / Compatibility | No SDFGI, no volumetric fog; Compatibility = OpenGL fallback | Low-end fallback only |
| SDFGI | Infinite-world GI that follows the camera; no dynamic occluders/emitters; documented as "one of the most demanding" GI techniques | Optional high tier for cliffs/water bounce |
| Volumetric fog + FogVolumes | Single-scattering froxels, temporal reprojection; per-volume density add/subtract | Fjord mist, storm mood |
| SSR | Overhauled in 4.6 (better roughness, faster, full/half-res option) | Water and wet rock reflections |
| TAA / FSR2 2.2 | Forward+ only; FSR2 works as upsample **or** native-res TAA replacement | Cheapest huge AA win for ocean specular flicker |
| MSAA / FXAA / SMAA | MSAA all renderers; FXAA/SMAA Forward+ & Mobile | Quality options matrix |
| Mesh LOD (auto) | meshoptimizer-generated on import for glTF/.blend/FBX; automatic at runtime | Distant boats and props |
| Visibility ranges (HLOD) | Manual fade/swap ranges when auto-LOD is not enough | Far islands, lodges |
| Occlusion culling | CPU rasterization via Embree; best in enclosed spaces; depth prepass reduces benefit in Forward+ | Harbours, interiors — not the open fjord |
| MultiMesh | One draw primitive; **no per-instance frustum culling** — split by region | Fish, birds, moorings, boats |
| CompositorEffect | **Experimental**; rendering-thread callback at defined pipeline stages | Custom underwater/screen effects |
| Water shaders | No built-in ocean; custom spatial shader (repo: 4-wave Gerstner) | Core fjord surface |
| Sky | ProceduralSkyMaterial, PhysicalSkyMaterial, custom Sky shaders | Nordic sky + aurora |
| 4.7 additions | HDR output (Windows/macOS/iOS/visionOS/Linux Wayland); AreaLight3D | Neon harbours, dusk HDR grading |
| 4.8 additions | Directional-light contact shadows (dev 6); animated decals (dev 7) | Cliff contact; wake decals later |

**Platform notes.** macOS/iOS/visionOS default to the native **Metal** driver, limited to Apple Silicon (Intel Macs fall back to Vulkan/MoltenVK); early Metal results were "at least as fast as Vulkan and in many cases much faster on Apple hardware" (4.4 notes). New Windows projects default to **D3D12** since 4.6. Steam Deck: export native Linux (Vulkan) — Proton only matters for the Windows build; the maintainer notes `--rendering-driver vulkan` "might help with performance when running through Proton" [medium]. 4.8 dev 7 known issue: **Steam Deck built-in controls no longer work in the Linux export (GH-123704, workaround: Desktop Mode)** — do not upgrade before it is fixed.

---

## 2. Technique shortlist for Lakskongen [medium]

Baseline to beat (per `godot/scenes/environment/FJORD_RENDERER.md`): 256×256 terrain with height/slope blend, 128×128 water plane with 4 Gerstner waves + texture foam, procedural sky with aurora, tilt-shift + underwater post, rain/snow particles. In-repo perf target: 60 FPS @ 1080p GTX 1060; AGENTS.md target: 60 FPS @ 1440p M1 Pro.

**Screenshot-gate vocabulary** (repo screenshot-evidence skill + visual-qa): shots come from the non-headless harness (`tools/capture_screenshots.sh`, `ScreenshotHarness.tscn`); existing shots: `08a_fjord_live_sync`, `08_fjord_day`, `08b_fjord_farm`, `09_fjord_night_aurora`, `10_fjord_storm`, `11_fjord_underwater`, `12_diag_no_water`, `14/15/16_diag_*`. A gate passes only with **raw numbers**: repeat-capture md5 control + perceptual-hash distance (threshold 8; ≥9 "noticeable", ≥16 "major"); new shots register in `visual_qa/config.py SHOT_NAMES` and promote via `python -m visual_qa.baseline_update -m "<why>"`, with a RUN_LOG evidence line. Effort in working days.

| Technique | Impact (visual) | Effort | Perf budget note* | Screenshot gate (before/after must show) |
|---|---|---|---|---|
| **Ocean: shore blending + foam pass** (edge foam from depth/shore distance, crest foam from wave steepness, wet-line) | High — the shoreline is the most-read pixel band | 3–5 d | Shader-only; current water plane already 128×128; <0.5 ms added [low] | New `17_fjord_shore` shot (farm zoom on beach): phash Δ ≥16; `12_diag_no_water` control unchanged; foam band visible at 2 zooms |
| **Ocean: FFT (Tessendorf-style) displacement** | Medium-high at close zoom; low at current top-down camera | 4–6 wk | GPU compute + 2–4 render targets (256²–512²) + readback for buoyancy; 1–3 ms [low]; must not touch sim determinism | Only after blind-playtest evidence that wave detail is a top complaint; gate = `08b` Δ ≥16 + in-player bobbing check |
| **Underwater look** (depth-gradient fog, caustics projection, god rays, surface from below) | High — underwater is the sim's money shot (fish, lice, nets) | 4–6 d | Caustics: one extra texture sample; shafts: FogVolume cost only when enabled | `11_fjord_underwater` Δ ≥16; `15_diag_no_underwater` control unchanged |
| **Terrain material blending** (add macro variation + anti-tiling + triplanar on cliffs to existing height/slope blend) | High — kills obvious tiling on cliffs | 3–5 d | Shader-only; triplanar ≈2 extra samples on cliff pixels [low] | `08_fjord_day` Δ ≥9 and a cliff close-up Δ ≥16; repeat md5 control equal |
| **Sky/atmosphere** (PhysicalSkyMaterial or improved sky shader, sun/moon ramp, HDR output, exposure curve) | High — Nordic light sells the setting | 2–4 d | Sky shader is procedural today; HDR output is a project setting | `09_fjord_night_aurora` and `08_fjord_day` Δ ≥9; HDR verified manually (compatible display) |
| **GI quality tiers** High = SDFGI; Mid = SSAO/SSIL + half-res SSR; Low = no SDFGI/SSR | Medium-high (cliff/water bounce) | 5–8 d | SDFGI is documented as the engine's most demanding GI; tier it off by default in Low [high, official docs] | `10_fjord_storm` Δ ≥9 between tiers; difference visible in labeled gallery |
| **Instanced boats/vegetation/birds** (region-split MultiMesh; `visible_instance_count` for populations) | Medium — density sells "living fjord" | 3–5 d | One draw call; no per-instance culling → region split | `08b_fjord_farm` Δ ≥9 with density sweep; 3 population states captured |
| **Decals** (shore rocks, mooring stains, wakes; animated decals land in 4.8) | Low-medium now; medium later | 2–3 d static; +2 d animated (4.8) | Decal pass cheap when few; watch overdraw | New `18_fjord_decals` Δ ≥16; no regression on `08b` (Δ ≤8) |
| **AA / post polish** (enable TAA or FSR2; keep MSAA for UI-adjacent edges; tune tilt-shift) | Medium — ocean sparkle is the biggest artifact today | 1–2 d | TAA/FSR2 Forward+ only; FSR2 native-res costs more than TAA [high, docs] | `10` Δ ≥9 (shimmer reduced in labeled crops); M1 Pro 1440p ≥60 FPS |

\* Perf budgets are planner estimates in frame-time terms and must be validated with in-repo GPU timings on the M1 Pro target; only SDFGI/volumetric-fog costs are documented engine facts.

---

## 3. Competitor teardown — what ships, where they cut corners [high unless noted]

**Farming Simulator 25** (GIANTS Software, 12 Nov 2024; GIANTS Engine 10): its marquee engine change is "better weather effects and shadow rendering" — an in-house engine on an incremental path, not a tech leader (FS26 mobile followed 19 May 2026). Lesson: sim franchises win on systems depth + mod platforms while visuals stay mid-tier; out-present them on *place* (fjord identity), don't out-spend them on fidelity.

**Fishing: Barents Sea** (Misc Games, Stavanger, UE4, 7 Feb 2018; Metacritic 69): modest tech, praised atmosphere, console ports. Lesson: this audience tolerates mid-tier rendering when the *routine* (haul, gut, sell, weather) feels real.

**Fishing: North Atlantic** (Misc Games, Steam release 16 Oct 2020): the store pitch is "advanced watershader and weather system, with realistic weather data", "volumetric cloud system", "RTX support", 200×300 km map, 29 licensed boats. Lesson: **water + weather + big scale numbers are store-page hero features** — a marketing requirement, not a nice-to-have.

**Ships At Sea** (Misc Games, EA since 23 May 2024) — *the direct competitor*: Unreal Engine 5, **NVIDIA WaveWorks ocean simulation** + in-house buoyancy, 46×46 km Lofoten open world, co-op/dedicated servers, fish-farming (v0.8.8 rework: clearer farm management, storm-damaged nets, salmon jumping, seagulls), DLSS FG / FSR / XeSS. They win ocean fidelity; Lakskongen competes on management depth, Norwegian industry texture, determinism, and stylised-fjord readability.

**Dredge** (Black Salt Games, Unity, 30 Mar 2023; >1M copies by Oct 2023; 2025 Apple Design Award): low-poly art direction, muted palette, per-archipelago water shader tuning, and a **fog shader centred on the player, not the camera**. It cuts water/lighting fidelity and buys back silhouette, colour script, and fog. Lesson: style, composition, and player-centred effect placement are small-team levers.

**Two Point Museum** (Two Point Studios/Sega, Unity, 4 Mar 2025; Switch 2 Oct 2025; DLC through Oct 2026; TGA 2025 Best Sim/Strategy nominee): bright, readable, UI-first. Lesson: management games are read *through* their UI — fund legibility like shaders.

**Anno 117: Pax Romana** (Ubisoft Mainz, "Empire Engine", 13 Nov 2025; fastest-selling Anno, 50,484 CCU peak, 1.17M players by Apr 2026; German Computer Game Award for Best Innovation and Technology): reviews highlight lighting atmosphere, weather, detail and (per 4Players) ray tracing. Lesson: the genre's top end treats *atmosphere* as a headline feature — chase it with sky/fog/water craft, not full ray tracing.

---

## 4. Asset pipeline: Blender → GLB → CI [high for Godot behavior; medium for conventions]

**Source & export.** Godot recommends "Exporting glTF 2.0 files from Blender"; `.blend` imports directly when Blender is installed (avoid in CI — it shells out to Blender). One unit = one metre; author boats/pens at real scale (pens ~120 m circumference class); apply transforms (scale 1,1,1) — the glTF exporter converts Z-up to Godot's Y-up.

**Naming and import hints** (official): `-noimp` strips nodes/animations; `-col`/`-convcol`/`-colonly`/`-convcolonly` generate collision. Repo suggestion: `SM_`/`SK_`/`PEN_`/`BOAT_` prefixes, `LOD0/1/2` suffixes, collision only on gameplay meshes.

**LODs.** Use Godot's automatic mesh LOD (meshoptimizer, on import for glTF/.blend/FBX); use Visibility ranges (HLOD) only where auto-LOD pops (distant islands, lodges). Keep hero geometry (pens, boats) high-poly.

**Textures.** Author PNG/WebP; in the Import dock use **VRAM Compressed** for 3D (S3TC/ETC2 default; **High Quality** for BPTC/ASTC desktop), mipmaps on, normal maps auto-RGTC; **Basis Universal** is the small-file option. Note: 4.7 docs list `.ktx` import (2D only, no cubemaps/arrays) and do **not** list `.ktx2` — treat "KTX2/Basis" as "source PNG → Basis Universal mode" unless tested otherwise [medium]. HDR skies: `.hdr`/`.exr`.

**Starter GLB budget:** ≤2 materials/prop, ≤4 k tris for props, 512–1024 px prop textures, 2048 px max for hero assets, no embedded textures in shipped GLBs, vertex colors for low-poly props.

**CI validation** (fits the existing `tools/ci.sh` step pattern):
1. `godot --headless --import` — "starts the editor, waits for any resources to be imported, and then quits"; import errors fail the step [CLI docs].
2. **Khronos glTF-Validator** on every `.glb` (spec compliance + warning codes) — actively maintained (last push Oct 2026).
3. **glTF-Transform** (`inspect`/`optimize` CLI) for structural checks and size regression — actively maintained (last push Oct 2026).
4. Budget script: parse glTF JSON for tri/material/texture-size limits; fail on regressions (mirrors `validate_mods.py`).
5. Determinism guard: assets must not affect sim state — run twin-engine + save-hash tests after asset swaps (already in CI).

---

## 5. Verdict [medium]

**Stay on Godot.** Migrating to Unity/Unreal is a multi-engine-month project touching every layer here: GDScript UI + C# core split, the 14-step CI, 667 tests incl. determinism gates, mod schemas/loader, screenshot harness and baselines, the `GameBridge` partial/proxy convention, and Godot-specific MCP tooling. What migration buys (built-in ocean, AAA GI presets, editor VFX) is exactly where the repo has working custom replacements (Gerstner water, sky shader, tilt-shift/underwater post). Godot's cadence is healthy (4.7.2 two months after 4.7; 4.8 at feature freeze with contact shadows + animated decals). The honest gaps — no built-in ocean/FFT, SDFGI's static-only occluders, experimental CompositorEffect — are acceptable for a management-sim camera and testable via the harness.

**Ranked upgrade list** (impact first, then effort; budgets as in §2):

| # | Upgrade | Impact | Effort | Perf budget | Screenshot gate |
|---|---|---|---|---|---|
| 1 | Shore blending + foam + wet-line water pass | High | 3–5 d | <0.5 ms [low] | New `17_fjord_shore` Δ ≥16; `12_diag_no_water` control |
| 2 | Sky/atmosphere + HDR output + exposure | High | 2–4 d | ~0 ms [low] | `08`/`09` Δ ≥9; manual HDR check |
| 3 | Terrain anti-tiling + triplanar cliffs | High | 3–5 d | ~0.3 ms [low] | `08` Δ ≥9 + cliff close-up Δ ≥16 |
| 4 | GI/AA quality tiers (TAA, SSR half-res, SDFGI high tier) | Medium-high | 5–8 d | Tiered; SDFGI off by default [high] | `10` Δ ≥9 across tiers; M1 Pro 1440p ≥60 FPS |
| 5 | Instanced boats/vegetation/birds (region-split MultiMesh) | Medium | 3–5 d | <0.5 ms [low] | `08b` Δ ≥9; population-change control pair |
| 6 | Underwater upgrade (fog gradient, caustics, shafts) | High | 4–6 d | <1 ms [low] | `11` Δ ≥16; `15_diag_no_underwater` control |
| 7 | Decals (static now; animated after 4.8) | Low-medium | 2–3 d | small | New `18_fjord_decals` Δ ≥16; `08b` Δ ≤8 |
| 8 | FFT ocean | Medium (conditional) | 4–6 wk | 1–3 ms [low] | Only after blind-playtest evidence; `08b` Δ ≥16 + bobbing OK |

**Engine upgrade plan:** stay on 4.7.2 now; evaluate 4.8 stable after Steam Deck controls (GH-123704) is fixed; treat 4.8 contact shadows and animated decals as drop-in wins (shader/project settings only, no sim impact).

---

## Sources

**Godot engine & renderer**
- Godot blog index (4.7 announcement 18 Jun 2026; 4.7.1 14 Jul 2026; 4.7.2 18 Aug 2026; 4.8 dev 7 29 Sep 2026): https://godotengine.org/blog/
- Godot 4.7 release notes (HDR output, AreaLight3D): https://godotengine.org/releases/4.7/
- Godot 4.7.1 / 4.7.2 maintenance releases: https://godotengine.org/article/maintenance-release-godot-4-7-1/ , https://godotengine.org/article/maintenance-release-godot-4-7-2/
- Godot 4.8 dev 6 (screen-space contact shadows): https://godotengine.org/article/dev-snapshot-godot-4-8-dev-6/
- Godot 4.8 dev 7 (feature freeze; animated decals; Steam Deck controls issue GH-123704): https://godotengine.org/article/dev-snapshot-godot-4-8-dev-7/
- Godot 4.6 release notes (SSR overhaul; D3D12 default on Windows): https://godotengine.org/releases/4.6/
- Godot 4.4 release notes (native Metal backend; MetalFX; Apple Silicon limitation): https://godotengine.org/releases/4.4/
- Renderers overview (Forward+/Mobile/Compatibility; drivers): https://docs.godotengine.org/en/4.7/tutorials/rendering/renderers.html
- ProjectSettings driver defaults (metal default on macOS/iOS/visionOS): https://docs.godotengine.org/en/4.7/classes/class_projectsettings.html
- SDFGI: https://docs.godotengine.org/en/4.7/tutorials/3d/global_illumination/using_sdfgi.html
- Volumetric fog: https://docs.godotengine.org/en/4.7/tutorials/3d/volumetric_fog.html
- Occlusion culling (Embree; depth prepass note): https://docs.godotengine.org/en/4.7/tutorials/3d/occlusion_culling.html
- Mesh LOD (meshoptimizer): https://docs.godotengine.org/en/4.7/tutorials/3d/mesh_lod.html ; Visibility ranges: https://docs.godotengine.org/en/4.7/tutorials/3d/visibility_ranges.html
- MultiMesh (no per-instance culling): https://docs.godotengine.org/en/4.7/tutorials/performance/using_multimesh.html
- 3D antialiasing (MSAA/TAA/FSR2/FXAA/SMAA matrix): https://docs.godotengine.org/en/4.7/tutorials/3d/3d_antialiasing.html
- CompositorEffect (experimental) + Compositor tutorial: https://docs.godotengine.org/en/4.7/classes/class_compositoreffect.html , https://docs.godotengine.org/en/4.7/tutorials/rendering/compositor.html
- Decal class: https://docs.godotengine.org/en/4.7/classes/class_decal.html ; PhysicalSkyMaterial: https://docs.godotengine.org/en/4.7/classes/class_physicalskymaterial.html
- Steam Deck/D3D12–Proton discussion (GamingOnLinux, 26 Jan 2026): https://www.gamingonlinux.com/2026/01/the-free-and-open-source-godot-engine-4-6-is-out-now-with-major-upgrades/

**Asset pipeline**
- Importing images (compression modes; .ktx limits): https://docs.godotengine.org/en/4.7/tutorials/assets_pipeline/importing_images.html
- Import configuration (import hints `-col`, `-noimp`): https://docs.godotengine.org/en/4.7/tutorials/assets_pipeline/importing_3d_scenes/import_configuration.html
- Model export considerations ("glTF from Blender recommended"): https://docs.godotengine.org/en/4.7/tutorials/assets_pipeline/importing_3d_scenes/model_export_considerations.html
- Command line (`--headless`, `--import`): https://docs.godotengine.org/en/4.7/tutorials/editor/command_line_tutorial.html
- Khronos glTF-Validator: https://github.com/KhronosGroup/glTF-Validator ; glTF-Transform: https://github.com/donmccurdy/glTF-Transform (both last pushed 6 Oct 2026)

**Water technique references**
- Gerstner waves: GPU Gems 1, ch. 1 "Effective Water Simulation from Physical Models": https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models
- FFT ocean: Tessendorf, "Simulating Ocean Water": https://people.computing.clemson.edu/~jtessen/reports/papers_files/coursenotes2004.pdf

**Competitors**
- Farming Simulator (FS25 12 Nov 2024, GIANTS Engine 10; FS26 mobile 19 May 2026): https://en.wikipedia.org/wiki/Farming_Simulator
- Fishing: Barents Sea (UE4, 7 Feb 2018, Misc Games): https://en.wikipedia.org/wiki/Fishing:_Barents_Sea
- Fishing: North Atlantic (Steam, 16 Oct 2020; "advanced watershader", RTX, volumetric clouds, 200×300 km): https://store.steampowered.com/app/1264250/
- Ships At Sea (Misc Games, EA 23 May 2024; UE5; NVIDIA WaveWorks; Lofoten; fish-farm rework): https://store.steampowered.com/app/1266540/
- Dredge (Unity, 30 Mar 2023; low-poly; player-centred fog shader; >1M copies): https://en.wikipedia.org/wiki/Dredge_(video_game)
- Two Point Museum (Unity, 4 Mar 2025; Switch 2 Oct 2025; DLC cadence): https://en.wikipedia.org/wiki/Two_Point_Museum
- Anno 117 (Empire Engine, 13 Nov 2025; 50,484 CCU; 1.17M players; GCA Innovation and Technology): https://en.wikipedia.org/wiki/Anno_117

**Internal**
- `godot/scenes/environment/FJORD_RENDERER.md` (current fjord renderer), `godot/project.godot` (4.7, Forward+, MSAA 4×), `.claude/skills/ecc/lakskongen-screenshot-evidence` (gate workflow), `tools/capture_screenshots.sh` and `visual_qa` (diff thresholds).
