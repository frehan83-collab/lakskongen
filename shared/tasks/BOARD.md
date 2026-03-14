# Task Board — Lakskongen

## ✅ Done
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T01 | Benchmark 4 local models | All | GLM = best local, report delivered |
| T02 | Implement 10 achievement TODOs | GLM Flash | 10/10, committed |
| T03 | NorwegianEvents — 10 TODOs + 4 categories | Sonnet | 20 new events, committed |
| T04 | SustainabilityManager — 10 TODOs | Sonnet | All placeholders replaced, committed |
| T05 | Zone.cs + Storyteller.cs + Hatchery.cs TODOs | Sonnet | 5/5, 0 TODOs remaining |
| T06 | GameBridge expansion — 6 API sections | Sonnet | 25 new methods, committed |
| T07 | JSON data: research, tutorial, balancing | Orchestrator | 3 files, committed |
| T08 | JSON data: encyclopedia, achievements, scenarios | Orchestrator | 3 files, committed |
| T09 | Wire GDScript UI panels to new bridge APIs | Sonnet | 39 new SimBridge methods, 4 panels wired, 2 new panels (Finance, Crew) |
| T10 | Create missing Godot scenes (SustainabilityPanel, FinancePanel) | Sonnet | 2 .tscn scenes + scripts, committed |
| T11 | SimulationEngine — load JSON configs on Initialize() | Sonnet | All 6 JSON configs loaded, committed |
| T12 | Save/Load — serialize new GameState properties | Sonnet | All properties verified, backward compat, committed |
| T13 | Compile fix — resolve 12 errors (Zone, Storyteller, NorwegianEvents) | Sonnet | 0 errors, committed |
| T14 | GDScript Dashboard.gd — wire to all new bridge data | Sonnet | Full dashboard: fish, finance, environment, sustainability, research, events |
| T15 | Audio assets placeholder + AudioManager wiring | DeepSeek | ✅ AudioController wired, committed |
| T16 | Tutorial flow — TutorialSystem ↔ TutorialUI.gd | GPT‑4o | ✅ Implemented, tutorial system complete |
| T18 | Real salmon price data — calibrate O‑U market model | GPT‑5.4 | ✅ Market calibration done (T23) |
| T19 | Norwegian aquaculture data scraping (Barentswatch) | GPT‑4o | ✅ Integrated via BarentsWatchFishHealthIntegration.cs |
| T20 | GitHub Issues sync — mirror BOARD.md to GitHub | GLM Flash | ✅ Not needed (local focus) |

## ✅ Done (Sprint 7)
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T21 | Research.PopulateFromJson — zero TODOs | Opus (direct) | ✅ Wired with branch mapping |
| T22 | Production chain panel (Anno-style) | Opus (direct) | ✅ Full chain Stamfisk→Eksport |
| T23 | Market O-U model calibration | DeepSeek | ✅ Real Norwegian prices |
| T24 | Expand to 13 zones (full coast) | DeepSeek | ✅ All PO 1-13 |
| T25 | Genetics breeding UI (CK3-style) | DeepSeek | ✅ Traits, inbreeding, heritability |
| T26 | Tutorial onboarding (23 steps) | Opus (direct) | ✅ Full Norwegian first-2-hours |
| T27 | Polish 5 campaign scenarios | DeepSeek | ✅ Familiegården→Lakskongen |
| T28 | Contracts panel + market depth UI | DeepSeek | ✅ Active contracts, offers, export markets |
| T29 | Encyclopedia expansion (51→65 entries) | DeepSeek | ✅ Industry, market, tech, Norwegian companies |
| T30 | Balance tuning — production economics | DeepSeek | ✅ Real Norwegian VB growth, FCR, costs, MAB |
| T31 | MapPanel — 13 Norwegian production zones (UI rewrite) | Sonnet | ✅ Full PO 1-13 display |
| T32 | Audio System — weather-reactive ambience + music crossfades | Sonnet | ✅ Dynamic soundscapes + tension music |
| T33 | Achievement system polish + notification UI | GPT-5.4 | ✅ Polished system + unobtrusive UI |

## ✅ Done (Sprint 8)
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T34 | Voice Lines for Notifications | GPT-5.4 | ✅ Code validated, build passes |
| T40 | SimulationEngine Orchestration | DeepSeek | ✅ Core loop integrated |
| T41 | Research Mod Conflict & Versioning | Gemini 2.5 Flash | ✅ Research complete |
| T42 | Add AudioController/MusicLibrary to Godot Autoloads | Gemini 2.5 Flash | ✅ `project.godot` updated |
| T43 | Implement Harvest Audio Alert | ds-coder | ✅ Implemented and committed |

## ✅ Done (Sprint 9 — Wave 1)
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T17 | Multiplayer — NetworkManager + lobby | DeepSeek V3.2 | ✅ Host migration, trade, ready-state, 4 tests |
| T35 | Mod support framework | DeepSeek V3.2 | ✅ Discovery, enable/disable, config, deps, 2 tests |
| T37 | UI polish & accessibility (Phase 1) | Qwen 3.5 Flash | ✅ 34 tooltips, keyboard nav, high-contrast (4/20 files) |

## ✅ Done (Sprint 9 — Wave 2)
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T36 | Steam Workshop integration | DeepSeek V3.2 | ✅ Upload/download/update/vote, ISteamWorkshop, 5 tests |

## ✅ Done (Sprint 9 — Wave 2 continued)
| ID | Task | Agent | Result |
|----|------|-------|--------|
| T44 | End-to-end integration test suite | DeepSeek R1 | ✅ 5 tests, found+fixed StochasticDisease bug, 4 blocked by JSON data |
| T45 | Difficulty settings system | DeepSeek V3.2 | ✅ 5 presets (Easy→Realistic+Custom), 7 multipliers, builder pattern, 17 tests |

## 📋 Inbox (Sprint 9 — Wave 3+)
| ID | Task | Estimated Agent | Priority |
|----|------|----------------|----------|
| T46 | Fix JSON data enum mismatches (ResearchBranch, ProductionRegion, relatedIds) | DeepSeek V3.2 | HIGH |
| T37b | UI polish Phase 2 (remaining 16 files) | Qwen Flash | MEDIUM |
| T38 | Sound design & voice recording | Audio team | MEDIUM |
| T39 | Localization (NO, EN, DE, FR, ES, JA) | Localization | LOW |

---
*Updated: 2026-03-14 21:40 CET — Sprint 9 Wave 2 complete!*
*Build: ✅ Green | Tests: ✅ 95 passed, 4 blocked (JSON data) | TODOs: 0*
*Routing: DeepSeek V3.2 primary → R1 → Qwen Flash → Gemini → Claude → Free tier*