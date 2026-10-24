# Lakskongen handover — 2026-10-24

This is the short project entry point. Read only the task-specific document it
names next. **Graphics/Blender work continues in `docs/GRAPHICS_HANDOVER.md`
§1; do not repeat its reconnaissance.** Detailed architecture and subsystem
rules live in `AGENTS.md`; historical decisions are in `docs/RUN_LOG.md`.

## 1. Project and current state

Lakskongen is a Norwegian salmon-farming management sim: deterministic C#
.NET 10 simulation (`src/LakskongenSim`) and Godot 4.7.2 Mono frontend
(`godot/`). Build branches from `origin/main`, never from the stale main
checkout. At this handoff `origin/main` is `466120c`; PRs #16, #17 and #20 are
open and owned by another worker.

The main checkout `/Users/fredrik.j.hansen/lakskongen` has the owner's Godot
editor open and contains unrelated changes. Do not edit it. Use:

```bash
git fetch --all --prune && gh pr list
git worktree add -b <track>/<item> .claude/worktrees/<item> origin/main
```

Merge `origin/main` again before the first commit. PRs target `main`; the owner
merges them. Do not push to another worker's branch.

## 2. Non-negotiable rules

- Determinism: seeded `SimRng` streams only; no wall clock, unseeded random,
  GUID IDs, network I/O or process-random hashing in the sim.
- Calibration SSOT: numeric constants belong in `CalibrationRegistry` /
  `BalancingConfig`; do not clamp a model to force a realism band.
- Tests are add-only. Keep both sides of appended manifest/RUN_LOG rows on
  merges; `MIN_CHECKS` must equal the exact observed UI check count, never a
  lower chosen value.
- A feature needs a player-path outcome test. Presenters read engine state and
  never write it. Save/RNG changes require exact round-trip and twin-run gates.
- Visual claims require committed frames; headless success alone is not visual
  evidence. Never run `python -m visual_qa.capture` or `baseline_update`.
- No real brands/company names on models. Append atlas rows, never insert.
- Commit format: `run: <item> - <outcome>`, evidence in the body, no
  attribution/co-author lines. Add the appropriate RUN_LOG §7 row.
- Ask before windowed Godot (`game_start`, `tools/capture_screenshots.sh`,
  `tools/perf_smoke.sh`) and before work explicitly marked owner-gated.

## 3. Current graphics assignment and owner scope

Owner direction, 2026-10-24: buildings, slaughterhouses, land-based farms and
pens should be player-placed. Focus on ocean, fjords, offshore units, the
land-farm kit and land slaughterhouses. Do not spend effort on trees, shore
props, terrain textures, snow, 08a land contrast or species models.

F2-30 is **unfinished, uncommitted WIP** in
`/Users/fredrik.j.hansen/lakskongen/.claude/worktrees/f2-30` on `art/f2-30`.
Its assets/static checks pass, but its targeted UI probe fails and the full UI
suite has not run. The next graphics model must resume that exact worktree,
not restart or create a competing branch. All file lists, measurements,
commands, the invalid 12M-NOK test path and remaining tasks are in
`docs/GRAPHICS_HANDOVER.md` §1–§3.

Pens and slaughterhouses are not player-placed by the sim today. Making that
real is a Tier-1 sim feature (save version, goldens, determinism, calibration,
bridge contract), not a presenter task. The current assignment permits models,
presenters and a placement proposal only; do not edit `src/` or start actual
placement without the owner's later approval.

## 4. Architecture reading map

Read only the rows needed by the task:

| Need | Source |
|---|---|
| Current graphics resume, tools, verification | `docs/GRAPHICS_HANDOVER.md` |
| Farm-type item | `docs/FARM_TYPES_SPEC.md`: its §7 row, then only cited sections |
| Sim systems and naming conventions | `AGENTS.md`: grep the subsystem heading |
| Why a decision exists | `docs/RUN_LOG.md` §7; open findings in §8 |
| Roadmap status/acceptance | `docs/NEXT_LEVEL_PLAN.md`, `docs/SHIP_PLAN.md` |
| Art direction | `docs/art/OWNER_REFERENCE_BRIEF.md`, `design/ART_DIRECTION.md` |
| Renderer/water detail | `docs/FJORD_RENDERER.md`: grep the system |
| Asset build process | `.claude/skills/ecc/blender-asset-pipeline/SKILL.md` |

Core seams: `SimulationEngine` partials and tick phases; `GameState` plus
`SaveJson`; `SimRng`/`SimRngStreams`; `GameBridge.*.cs` → `SimProxies.gd` →
`SimBridge.gd`; Godot presenters under `godot/scripts/visual/` and
`godot/scripts/environment/`. Do not infer engine behaviour from UI text.

## 5. Common verification

Use a separate worktree. Run Godot silently and give each headless process its
own token path. In a fresh worktree run `--import` once, alone; never overlap
another headless import. Commands longer than 30 s should be backgrounded and
polled; macOS has no `timeout` (use `perl -e 'alarm shift; exec @ARGV' N cmd`).

```bash
dotnet build LakskongenSim.sln
dotnet build godot/LakskongenGame.csproj
python3.12 -m pytest tests/ -q
bash tools/validate_scenes.sh
bash tools/run_ui_flow_tests.sh       # 5–8 min; exact MIN_CHECKS required
CI_REQUIRE_ALL=1 ./tools/ci.sh        # integration/full gate, roughly 25–30 min
```

For C# save pins use `LC_ALL=C LANG=C dotnet test LakskongenSim.sln`. Graphics
work normally runs only its asset/presenter tests plus scenes/UI flow; see the
graphics handover for the exact current subset and already-observed results.
