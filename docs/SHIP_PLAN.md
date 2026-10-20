# SHIP_PLAN — from roadmap waves to playable builds

Owner directive, 2026-10-20: *"vi looper på ting som ikke er viktigst i prosjektet"* — stop looping on
non-essentials and take the project to a shippable game. This plan replaces the **process** around the
product (waves, parallel writers, gate ceremony for presentation work). It does **not** replace the
sim core's engineering rules, the roadmap's content (now an icebox), or the data already built.
Execution owner: the next integrator session (Opus). The owner can override any default in §2 and §7
with one sentence; everything here is chosen so work never waits on a question.

## 1. Diagnosis — why the old loop could not terminate

1. **Goodhart's gap.** Every gate measures invariance (tests, traces, budgets, check counts). No gate
   measures player value. Nothing fails when the game is confusing or silent, so effort follows the
   measurable gradient. Evidence: the audio floor was deferred repeatedly while 2,037 sim tests grew;
   a Visual QA job that can never pass (no Mac runner) sits in every PR; the repo contains no playtest
   artifact of any kind — no session has ever been played end to end and judged as a player.
2. **Iteration tax on judgment layers.** A presentation change pays the full tax (two builds, ~7 min
   dotnet, ~6 min UI suite, golden check, RUN_LOG ceremony, review, captain merge, gates again).
   Quality in judgment-driven layers (look, feel, sound, UX) grows with iteration count; the tax
   divides that count by 3–5×. The sim sits far right on its quality curve; art/UX sit far left —
   exactly where the owner perceives the gap.
3. **Superlinear integration cost.** m parallel writers on shared hot files cost ~m² in integration
   (bespoke merge tools had to be built to survive it) while yielding <m speedup. A planning run alone
   cost 3.1M tokens across 9 agents. Fewer, longer-lived agents now beat the wave machine.
4. **Unbounded objective.** "Top tier" has no stop condition, and every wave's findings spawn new
   roadmap items. The system is not stuck — it is *stable in orbit*, which is worse. Only a definition
   of done (§2) terminates it.

## 2. Product spine (defaults — owner-overridable)

- **Fantasy:** build a salmon empire along a living Norwegian coast — weather, biology, regulators and
  rivals push back; you out-plan them.
- **Core loop (20 min):** inspect sites → react to events (lice, storms, inspections, prices) → feed,
  treat, harvest → invest the margin into expansion via the national map.
- **Arc:** Første sesong (onboarding, 45–60 min) → the Lakskongen campaign (8–12 h) → national-map
  sandbox.
- **v1.0 = Steam Early Access quality bar:**
  - [ ] The three scenarios above polished; everything else ships disabled or as mods.
  - [ ] A fresh player finishes Første sesong with no external help; first hour rated ≥4/5 in playtests.
  - [ ] Sound on every core verb + ambience; nb and en complete; no emoji; D16/D17 hold.
  - [ ] p95 ≤ 16.7 ms on M1 High tier; loads < 10 s; no critical bug across three 2 h sessions.
  - [ ] mac + win build artifacts produced repeatably.

## 3. Stop doing (effective when the in-flight work lands, §8)

1. No new waves and no parallel writers on hot files — one integrator, serial focused tracks.
2. The roadmap is an icebox. The backlog comes from playtest findings, ranked by player impact.
3. Golden traces are **path-gated**: run/record only when `src/LakskongenSim` or calibration data
   changes. UI/art/audio commits skip them.
4. The UI-check count is frozen at the merged value. New checks only to pin a fixed playtest finding.
   Stop the MIN_CHECKS arithmetic ceremony; keep the suite green.
5. RUN_LOG ceremony: one row per cycle for presentation work. Sim-core changes keep full rows.
6. Byte-identical asset builds are checked **at freeze only** (end of a cycle), not per iteration.
7. No adversarial review panels for presentation work; a single review pass for sim-core changes.
8. No CI meta-work (calibrations, runner quirks) unless a cycle is actually blocked by it.

What does NOT change: the sim core keeps determinism, seeded RNG, add-only tests, golden traces,
save-compat and the calibration SSOT. The sim is the moat; this plan protects it by not spending it.

## 4. The cycle (the experience gate)

Each cycle ends in a build someone played. Steps:

1. **Sync** main, build windowed.
2. **Play** — `godot/qa/PlaytestHarness.gd` runs a per-scenario act list with real input at human
   pacing, captures frames on a cadence and on every dialog/toast, and writes crash-safe evidence
   (frames + `timeline.jsonl` + `summary.json`). The owner plays whenever available; the agent plays
   every cycle.
3. **Judge** — read the evidence *as a player*: confusion, dead time, silent actions, unreadable
   screens, no reason to care. Findings P0–P3 by player impact (not by code severity).
4. **Fix the top 5** findings, whatever layer they live in.
5. **Gate** — Tier 1 gates iff sim files changed; otherwise builds + UI smoke + judged frames.
6. **Ship** — PR to main (owner merges), one RUN_LOG row, next cycle.

Evidence lives under `docs/run/evidence/playtests/<nnnn>/` (curated; keep frames ≤1280 px JPEG).
The harness never passes/fails the game — exit 0 with findings; the judge decides. Exit 1 only when
the harness itself cannot run.

## 5. Engineering physics — two tiers

| | Tier 1: sim core (`src/`, calibration, bridge C#) | Tier 2: presentation (UI, shaders, scenes, audio, assets) |
|---|---|---|
| Determinism | Full (traces, twins, bans) | Only "reads engine state"; no sim writes |
| Tests | Add-only + mutation checks | Smoke + checks that pin fixed findings |
| Golden traces | Required on change | Skipped (path-gated) |
| Review | One reviewer pass | Judged frames in the cycle |
| Iteration budget | Low churn expected | High churn expected — keep it cheap |

## 6. MCP policy (cost-effective use)

- **Godot MCP** = interactive surgery, not pipeline: attach to a *dedicated QA instance*, never the
  owner's open editor. Use `runtime_screenshot`, `input_simulate`, scene queries to diagnose a finding
  live — many cheap calls beat rebuild cycles. Repeatable sessions belong to the CLI harness, not MCP.
- **Blender MCP** = look-dev first, freeze second: iterate live in the owner's Blender, judge with
  `render_viewport_to_path` (small res), and only when approved port the result into the `blender/`
  build scripts (byte-identical applies at freeze), rebuild headless, run the asset tests. The live
  session is never the source of a committed asset.
- Both: one long-lived session + many small calls; reduced-scale screenshots for judging; full-res
  only for final evidence.

## 7. First three cycles

- **Cycle 1 — Onboarding.** Wire and first-run the PlaytestHarness (it is committed but has not yet
  executed a session); author `qa/playtests/forste_sesong.json`; play it; judge; fix the top 5
  onboarding findings. Deliverable: the repo's first playtest report.
- **Cycle 2 — Sound and feedback.** Wire wave 4b's audio floor into moments: every core verb audible,
  a storm is an *event* (camera + sound + aftermath), not a toast. Judge by playtest; LUFS and
  event-coverage checks stay headless.
- **Cycle 3 — The map decision.** With geo stages 2–3 merged: play "expand via the map" end to end;
  fix drill-down friction; make rival pressure legible. Stop geo data work after this — the player
  needs reasons to care about the Norway that exists, not more Norway.

## 8. Landing the in-flight work (do this first)

Two workflows are finishing (started before this plan; let them run): **geo stages 2–3** on `geo/int`
and **wave 4b graphics** on `wave4b/int`. When each lands: push, open its PR, let the owner merge.
Merge them sequentially if translations/UiFlowTests conflict (use `tools/merge/`). Salvage from the
workflow journals if one stalls; do not start replacement waves. This plan's branch is disjoint from
both. After both merge, §3 takes effect.

## 9. Working agreement + kickoff prompt

One integrator session with persistent context; subagents only for read-heavy fan-out (research,
audits) or a genuinely isolated build task. Decide, don't ask (owner mandate); log decisions in the
cycle row. PR merges belong to the owner. Headless-only returns if the owner says windows disturb.

Kickoff prompt for the next session:

> Read `docs/SHIP_PLAN.md`, then `docs/HANDOFF.md` TL;DR. You are the single integrator.
> First land the in-flight work (SHIP_PLAN §8): check `gh pr list` and branches `geo/int`,
> `wave4b/int`. Then start Cycle 1 (§7). Decide rather than ask; one RUN_LOG row per cycle.
