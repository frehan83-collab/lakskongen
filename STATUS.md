# Lakskongen — Development Status

**Last updated:** 2026-10-08
**Version:** `0.7.0-alpha`
**Roadmap / next actions:** see [PLAN.md](PLAN.md)
**Docs index:** see [docs/INDEX.md](docs/INDEX.md)

## Build, test & verification

| Check | Result |
|---|---|
| `dotnet build LakskongenSim.sln` | ✅ 0 errors, 0 warnings |
| Tests | ✅ **713 passed**, 1 skipped, 0 failed (honest count; the old "727" included ~92 triplicates, fixed in Phase 0. Includes seeded 3-year soak, campaign smoke, PlayerScriptProbe careers, exact save-hash round-trips) |
| CI gate (`tools/ci.sh`) | ✅ **9/9 locally** (nothing skipped) — builds both assemblies · dotnet tests · pytest · perf · token sync · JSON · scene validation · GDScript parse. GitHub CI runs the same gate headless |
| Performance | ✅ 0.19 ms/day best avg (3 sites / ~103k fish; soft 2 ms/day, hard 5 ms/day) |
| Godot editor scan | ✅ 0 GDScript parse errors (Godot 4.7) |
| Godot headless boot | ✅ 0 script errors · 9 scenarios load · ModLoader ready |
| Theme tokens | ✅ in sync (`python3 tools/sync_tokens.py --check`) |
| Textures | ✅ 14 generated fjord textures + normal maps |
| Screenshot harness | ✅ available (`tools/capture_screenshots.sh`, requires a display) |

## Measured realism (calibration v1.0.3, seeds 1–20)

All medians in band per [docs/BALANCE.md](docs/BALANCE.md):

| Metric | Measured |
|---|---|
| Economic FCR | 1.20 (band 1.10–1.30) |
| Sea mortality / cycle | ~14.9% (band 14–16%) |
| Realized price | 88 NOK/kg (band 74–90) |
| Peak adult-female lice | 0.49/fish (~0.5 action limit) |
| EBIT/kg | 16.4 (cyclical band 8–25) |

## Where the project is

Phases 1–7 feature-complete, plus Waves 1–14. **NEXT_LEVEL_PLAN Phase 1
(player path) is now in progress — Sim.Commands wave 1 landed (see
`docs/NEXT_LEVEL_PLAN.md`):**

- Player verbs `StockSmolts`, `HarvestSite`, `TakeLoan`,
  `SetInsurancePolicy` moved from GameBridge into
  `SimulationEngine.Commands.cs` with a typed `CommandResult`
  (stable error keys); the bridge is a thin adapter with the same
  GDScript surface.
- The load path got one owner: `SimulationEngine.AdoptSavedState`
  (the old hand-copied field list from `GameBridge.LoadGame` moved
  verbatim, gaps documented until the ISnapshotable rewrite).
- `FinanceManager.AddLoanPrincipal` — loans book as liquidity, not
  revenue (S5).
- New `PlayerScriptProbe`: scripted `po3_red_sone` careers driven by
  the commands, events ON, zones ON, save/load mid-run through the
  same adoption path the game uses. **2 career tests + 8 command
  contract tests, all green.**

Wave 2 (done): `SimRng` named streams wired in; `DelouseSite` moved to
the library (gill fix: site-level hit once per site); wellboat booking +
execution inside the engine's daily advance (day-anchor after
`State.Day++`); storm/temperature impacts moved into `AdvanceOneDay`
with per-episode insurance settlement via a `DayNote` ledger the bridge
projects — the tick/skip asymmetry is dead. Storm loss factor
recalibrated 0.02 → 0.0003 (was never probe-measured; 67% cycle
mortality vs the 14–16% band).

Wave 3 (done): `ITickSystem` post-tick pipeline (storm → wellboat →
balance mirror); one delousing kernel shared by the player verb and
Mattilsynet enforcement (whole-site, honest temperature cancellation);
storm factor in CalibrationRegistry (v1.0.4); P&L truth — harvest/feed
book revenue/COGS and the bridge reports real Revenue/Expenses/Profit.

Wave 4 (done): `AdvanceOneDay` is a pure pipeline loop — 25 in-day
ITickSystem phases (weather → biology → economy → regulation →
analytics) with system-owned interval clocks rebuilt per Initialize
(the `_quarterStartDay` leak is dead). Balance bands, soaks and
determinism identity verified unchanged.

Wave 5 (done): ONE serializer (`SaveJson`) shared by the game and the
library, reading legacy v1/v2 saves for free; save v3 carries the
runtime cadence (interval clocks, storm edge, events flag) so loaded
games continue without double-firing beats or re-settling storm
insurance.

Wave 6 (done): `SimRandom` bit-identical RNG swap across 11 subsystems;
  exact save/load continuation proven by hash test (save→load→advance ==
  uninterrupted); oxygen chemistry fixed to volume-consistent (DO no longer
  pinned at 0); one serializer; runtime cadence persistence.

Next on this track: SimRng named-stream migration with golden tests,
episodic storm model, license double-charge.

- **Foundations (W1–2)** — audio truth pass, token-driven theme, data-viz,
  scenarios/mods wired, unified onboarding, `ConsequenceDirector`.
- **Depth (W3–9)** — health (delousing harm, thermal–lice coupling, gill
  warning), biology (smolt-quality carryover, precision feeding), finance
  (resource rent, binding price council, environmental flexibility), genetics
  (genomic selection, inbreeding, GE), insurance + bloom mitigation, data
  overlays, and Smoltscreen barrier tech.
- **Visual identity & engineering (W10–14)** — 14 generated textures and
  shader work, `GameBridge` split into partials, CI gate, store art, balance
  probe wired into the live sim (disease, escapes, price, mortality, lice),
  telemetry panel + CSV export, screenshot harness, and campaigns exercising
  the depth systems.

## Remaining roadmap (short)

- **P8** barrier-tech polish; **P14** audio polish pass.
- **P15** launch engineering: Steam page + demo + Next Fest, wishlist targets.
- Visual QA on a real display (all automated verification is headless + tests).

Full detail and priorities: [PLAN.md](PLAN.md).
