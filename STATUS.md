# Lakskongen — Development Status

**Last updated:** 2026-10-08
**Version:** `0.7.0-alpha`
**Roadmap / next actions:** see [PLAN.md](PLAN.md)
**Docs index:** see [docs/INDEX.md](docs/INDEX.md)

## Build, test & verification

| Check | Result |
|---|---|
| `dotnet build LakskongenSim.sln` | ✅ 0 errors, 0 warnings |
| Tests | ✅ **727 passed**, 1 skipped, 0 failed (incl. seeded 3-year soak + campaign smoke tests) |
| CI gate (`tools/ci.sh`) | ✅ **7/7** — build · tests · perf · token sync · JSON · scene validation · GDScript parse |
| Performance | ✅ ~0.13 ms/day best avg (3-site / ~207k fish; soft 2 ms/day, hard 5 ms/day) |
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

Phases 1–7 feature-complete, plus Waves 1–14:

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
