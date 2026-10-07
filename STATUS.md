# Lakskongen — Development Status

**Last updated:** 2026-10-07
**Version:** `0.6.0-alpha`
**Roadmap / next actions:** see [PLAN.md](PLAN.md)
**Latest session detail:** see [docs/SESSION_SUMMARY.md](docs/SESSION_SUMMARY.md)

## Build & test

| Check | Result |
|---|---|
| `dotnet build LakskongenSim.sln` | ✅ succeeded |
| Tests | ✅ ~690 passing, 0 failures (incl. 3-year soak) |
| Godot editor scan | ✅ 0 GDScript parse errors |
| Godot headless boot | ✅ 0 script errors · 9 scenarios · ModLoader ready |
| Theme tokens | ✅ in sync (`tools/sync_tokens.py --check`) |
| Translation CSV | ✅ 552 rows · 3 cols each · 0 duplicate keys |
| Perf | ✅ ~0.27 ms/day day-advance benchmark |

## Where the project is

Phases 1–7 feature-complete, plus Waves 1–8 delivered this session:

- **Foundations** — audio truth pass, token-driven theme, data-viz, scenario/mod
  systems wired, C# build repaired.
- **Onboarding + consequence** — single `TutorialSystem`, progressive
  disclosure, `ConsequenceDirector`, severity-ranked notifications.
- **Depth** — health (delousing harm, thermal–lice, gill warning), biology
  (smolt-quality carryover, precision feeding), finance (resource-rent, binding
  price council, environmental flexibility), genetics (genomic selection,
  inbreeding, GE), player-supply price impact.
- **Insurance + mitigation + data overlays** — catastrophe policies, bloom
  mitigation catalog, live map overlays, explain-this-number tooltips.
- **Briefings + content + persistence** — morning depth briefing, night-digest,
  save round-trips.
- **Ship engineering** — perf gate, localization finalized
  ([docs/LOCALIZATION.md](docs/LOCALIZATION.md)).

## Remaining roadmap (short)

- **P8** flow-dependent barrier tech (Smoltscreen).
- **P14** audio polish pass.
- **P15** Steam page/demo/Next Fest, wishlist targets, CI perf budget gate.
- Finish wiring insurance/mitigation + overlays into all daily-loop/UI actions.

Full detail and priorities: [PLAN.md](PLAN.md).
