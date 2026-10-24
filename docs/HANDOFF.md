# Lakskongen handover — 2026-10-24 (rev 4)

Read only this file first, then open **only** the task-specific file in §4.
**Graphics/Blender work starts at `docs/GRAPHICS_HANDOVER.md` §1.**

## 1. Project & workspace state (read before running anything)

- **Stack:** deterministic C# .NET 10 simulation (`src/LakskongenSim`) + Godot 4.7.2 Mono frontend (`godot/`).
- **Main checkout warning:** `/Users/fredrik.j.hansen/lakskongen` is on a stale branch (`review-remediation`) with the owner's Godot editor open. **Never edit it or read its stale docs.**
- **Always start from `origin/main` in a dedicated worktree:**
  ```bash
  git fetch --all --prune && gh pr list
  git worktree add -b <track>/<item> .claude/worktrees/<item> origin/main
  ```
- Merge `origin/main` again before committing. The owner merges PRs.
- **Open PRs (do not push to other workers' branches):**
  - **PR #22 (`art/f2-30`):** F2-30 closed-pen kit & fjord containment presenter — **DONE, all gates green** (`UI_FLOW_RESULT checks=2340 failures=0`).
  - **PR #21 (`docs/graphics-handover-2026-10-24`):** this handover update.
  - **PR #20 (`art/f2-29`):** F2-29 Havfarm-class & mooring kit — done, owned by another worker.
  - **PR #17 (`gfx/weather-drama`):** F2-24 weather drama — owned by another worker.
  - **PR #16 (`ui/inbox-polish`):** live inbox age label — owned by another worker.

## 2. Non-negotiable rules

- **Determinism:** seeded `SimRng` streams only; no wall clock, `Random`, `Guid.NewGuid()`, network I/O or process-random hashing in `src/LakskongenSim`.
- **Calibration SSOT:** numeric constants in `CalibrationRegistry` / `BalancingConfig`; never clamp to hit realism bands.
- **Tests are add-only:** never weaken checks or lower `MIN_CHECKS` (currently `2340` on `art/f2-30`, `2334` on `origin/main` before PR #22 merges). Keep both sides of manifest / `RUN_LOG.md` rows on merges.
- **Visual & art rules:** no real company/brand names on models (D17); append trim-atlas rows only (rows 0–27 on `origin/main`, 28–29 appended in PR #22); never run `python -m visual_qa.capture` or `baseline_update`; ask before any windowed Godot run (`game_start`, `capture_screenshots.sh`, `perf_smoke.sh`).
- **Commits:** `run: <item> - <outcome>`, evidence in body, no attribution/co-author lines, plus one `docs/RUN_LOG.md` §7 row.

## 3. Current graphics scope (owner direction 2026-10-24)

Buildings, slaughterhouses, land-based farms and pens are to be player-placed.
Focus on **ocean, fjords, offshore units, the land-farm kit and land slaughterhouses**.
Spend zero tokens/effort on trees, shore props (`naust`, `shore_quay`), terrain textures, snow, `08a` land contrast or species models.
Do not edit `src/` for graphics tasks; player placement of pens/slaughterhouses is a Tier-1 sim feature (proposal first).

## 4. Token-economical reading map (open ONLY what your task needs)

| Task | Read (do not read whole large files; grep headings) |
|---|---|
| Graphics / Blender queue & tool safety | `docs/GRAPHICS_HANDOVER.md` (120 lines, self-contained) |
| Farm-type item `F2-xx` | `docs/FARM_TYPES_SPEC.md`: grep `\| F2-xx \|` in §7, then only the section it cites |
| Art palette & references | `docs/art/OWNER_REFERENCE_BRIEF.md` (~55 lines) |
| Blender build & verify pipeline | `.claude/skills/ecc/blender-asset-pipeline/SKILL.md` |
| Renderer / water / shaders | `docs/FJORD_RENDERER.md` (grep the subsystem name) |
| Sim architecture & bridge | `AGENTS.md` (grep the subsystem heading) |
| Historical rulings | `docs/RUN_LOG.md` §7 (grep item ID); open findings in §8 |

## 5. Standard verification commands (run inside your worktree)

Single commands time out at 30 s in some agent runners: background long commands (`pytest`, `run_ui_flow_tests.sh`) to `/tmp/<name>.log` and poll. Run `--import` once alone in a fresh worktree; never overlap two headless imports.

```bash
dotnet build LakskongenSim.sln && dotnet build godot/LakskongenGame.csproj
python3.12 -m pytest tests/ -q
bash tools/validate_scenes.sh
bash tools/run_ui_flow_tests.sh   # 5–7 min; exact MIN_CHECKS required
```

