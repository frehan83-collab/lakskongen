# Handoff: where to start (updated 2026-10-17)

Read this first if you are a person or a model picking up Lakskongen.

## 1. The state of the branches

| Branch | What it holds |
|---|---|
| `review-remediation` (= `integration/wave2`) | **Start here.** The code-review remediation plus roadmap waves 1-3, integrated and green on the full local CI (`CI_REQUIRE_ALL=1 /bin/bash tools/ci.sh`, 18/18). It is draft PR #5 against `main`. |
| `main` | Older. It does not have waves 1-3 yet. Do not start from it until PR #5 is merged. |
| `wave4/*` | Wave 4, **paused** by the owner on 2026-10-17 to save weekly usage. Work in progress, some of it unverified (see §3). |

GitHub CI on `review-remediation` (`165fd6b`):
- `dotnet`, `python` and `golden-traces` pass.
- The Quality gate fails on one UI flow check on the Linux runner: U2-07's "next event: N during a run ends it and the clock keeps its 3×", a timing check on a slow runner. This is the first item to fix.
- `visual-qa` needs a self-hosted Mac runner, which the owner has to set up.

## 2. Before you change anything

1. **Wait for the owner's go.** Do not autostart or schedule work; the owner decides when work runs.
2. **Align with GitHub.** Other models may work on this repo. Run `git fetch --all --prune`, compare every branch you touch with `origin`, look at open PRs, and build on what others pushed.
3. **Read:**
   - `AGENTS.md`;
   - `docs/ROADMAP_TOP_TIER.md`: §3 resolved owner decisions, §9a farm types, §10 file-conflict map;
   - `docs/FARM_TYPES_SPEC.md`: land-based and offshore farming, items F2-01 to F2-35;
   - `docs/RUN_LOG.md` §7-8: every decision and open finding;
   - `docs/BALANCE.md`;
   - `docs/art/OWNER_REFERENCE_BRIEF.md`: the owner's art direction;
   - `docs/art/WAVE1_ART_REVIEW.md`.
4. **Rules that bite:**
   - Tests are add-only. Never weaken a check; replace one only with a stronger one and log it in RUN_LOG §7.
   - The simulation is deterministic, and the golden traces (F2-01, `tools/record_golden_traces.sh`) must stay byte-identical. A deliberate behaviour change re-records them, with a diff report explaining every change.
   - Prefix every Godot run with `env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t`.
   - Never run `python -m visual_qa.capture` or `baseline_update`.
   - The owner keeps a Godot editor open on the main checkout. Integrate and test in a separate git worktree, or the editor rewrites `godot/.godot/global_script_class_cache.cfg` mid-run.
   - No attribution or co-author lines in commits. No real company names in the game (D17).

## 3. Wave 4: where it stopped

Goal: land-based and offshore farms that are buildable, visible and operable end to end, plus the learn-by-doing tutorial and graphics polish. Each stage is one branch built from `4f927a1` (the wave-3 head).

| Stage (branch) | Items (spec) | State |
|---|---|---|
| `wave4/sim-land-1` | F2-12 engine wiring + F2-14 land permits, zoning, grid and phased construction | **Done** (`46a4a3f`, all gates green, golden traces 27/27 identical) |
| `wave4/sim-land-2` | F2-13 post-smolt bridge + F2-15 grow-out biology, competence, harvest planning (from sim-land-1) | WIP commit, unverified |
| `wave4/sim-sea-1` | F2-06 metocean wiring + F2-07 offshore unit build, operations, logistics | WIP commit, unverified |
| `wave4/gfx-farm-1` | F2-19 carved shore pads + F2-20 facility presenter (land campus and OF1-class unit placed from the engine) | WIP commit, unverified |
| `wave4/gfx-polish-1` | Perf on an idle machine, cloud stair-steps, wall banding, 08a land contrast, mood separation, mirror-calm shot 18 (gain bar), vessel chip clipping, whitecaps inside pens | WIP commit, unverified |
| `wave4/ui-core-1` | U2-02 learn-by-doing first season (tutorial) | WIP commit, unverified |

**Not started**, each with its dependencies:
- `sim-sea-2`: F2-08 offshore incidents + F2-09 development licences. After sim-sea-1.
- `ui-farm-1`: F2-31 family-first build dialog. Merge sim-land-1 and sim-sea-1 first.
- `gfx-farm-2`: F2-21 camera bookmarks and offshore swell + F2-23 first-sight sequence and showcase shots. After gfx-farm-1; merge both sim stage-1 branches.

**Deferred to the following wave:**
- G3-04: the underwater view.
- U2-08 + Au2-01: settings that apply, and the audio floor.

**Resuming a WIP stage:**
1. Check out the branch.
2. Review the WIP commit and keep what is good.
3. Finish the item to its acceptance list in the spec or roadmap.
4. Run the gates:
   - `dotnet test LakskongenSim.sln`;
   - `python3 -m pytest tests/ -q`;
   - `bash tools/validate_scenes.sh`;
   - `bash tools/run_ui_flow_tests.sh`, with MIN_CHECKS set to the exact count.
5. Merge the finished stage branches into the integration branch.
6. Run the full CI once, then push.

## 4. After wave 4 (roadmap order)

- **Remaining farm-type items:**
  - F2-16 land incidents and the RAS hall;
  - F2-10 closed pens;
  - F2-11 Havfarm-class;
  - F2-17 balance, reachability, scenarios and medals;
  - F2-22 logistics visuals;
  - F2-24 weather drama;
  - F2-27 to F2-30 art kits;
  - F2-32 to F2-35 land and offshore boards, alarms, reveal.
- **Phase 3:**
  - U3-01: world-first information architecture. It runs alone in the UI track.
  - U3-02: an icon system replacing emoji.
  - U3-03: the panel kit.
  - G3-02: one sim-to-3D presenter.
  - G3-03: lenses.
  - S3-01: the market model.
  - S3-02: the traffic-light rework.
- **Then** Phase 4 (elite simulation and decisions) and Phase 5 (quality tiers, accessibility, localization, release) from `docs/ROADMAP_TOP_TIER.md`.
