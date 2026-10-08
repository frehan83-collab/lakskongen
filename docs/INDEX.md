# Documentation Index

This is the entry point for Lakskongen's documentation. Start with the
canonical docs below, then browse the `docs/` reference. Root-level report
files from earlier sprints are kept for provenance but are **historical** — see
the bottom of this page.

## Canonical documents (repository root)

| Document | Description |
|---|---|
| [README.md](../README.md) | What the game is + quick start (build, test, run, CI, asset generators). |
| [STATUS.md](../STATUS.md) | Current build / test / perf snapshot and measured balance. |
| [PLAN.md](../PLAN.md) | Roadmap, current program of work and next actions. |
| [CHANGELOG.md](../CHANGELOG.md) | Version history (Keep a Changelog). |
| [SCENARIOS.md](../SCENARIOS.md) | Campaign chapters and sandbox scenarios. |
| [STEAM_STORE_ASSETS.md](../STEAM_STORE_ASSETS.md) | Steam capsule/asset production notes. |
| [STEAM_STORE_COPY.md](../STEAM_STORE_COPY.md) | Steam store page copy. |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Contribution guidelines. |
| [VISION.md](../VISION.md) | Design philosophy and market positioning. |
| [GDD.md](../GDD.md) | Game design document. |
| [MAC_SETUP.md](../MAC_SETUP.md) | Validated macOS (Apple Silicon) toolchain setup. |

## Reference — `docs/`

### Process, tooling & verification

| Document | Description |
|---|---|
| [CI.md](CI.md) | The 7-step quality gate (`tools/ci.sh`) and how to run it. |
| [SCENE_VALIDATION.md](SCENE_VALIDATION.md) | Headless `.tscn` load validation (`SceneValidator`, `tools/validate_scenes.sh`). |
| [SCREENSHOTS.md](SCREENSHOTS.md) | Display-gated visual QA harness and shot list. |
| [TELEMETRY_EXPORT.md](TELEMETRY_EXPORT.md) | Balance-telemetry and depth-snapshot CSV export. |
| [BALANCE.md](BALANCE.md) | Calibration knobs, measured distributions and reality bands. |
| [CALIBRATION.md](CALIBRATION.md) | How calibration data is sourced and reviewed. |
| [REALISM_CHECKLIST.md](REALISM_CHECKLIST.md) | Quarterly realism review checklist. |
| [LOCALIZATION.md](LOCALIZATION.md) | Localization workflow (English/Norwegian) and CSV format. |
| [Session summary](SESSION_SUMMARY.md) | Waves 1–8 session log (kept for context). |

### Architecture & API

| Document | Description |
|---|---|
| [Architecture.md](Architecture.md) | System architecture, data flow, module dependencies. |
| [Architecture_DeepDives.md](Architecture_DeepDives.md) | Deeper dives into selected subsystems. |
| [API_Reference.md](API_Reference.md) | SimBridge / GameBridge / EventBus APIs. |
| [Modding_API.md](Modding_API.md) | Mod contract and sample mod. |
| [LAKSKONGEN_OVERVIEW.md](LAKSKONGEN_OVERVIEW.md) | High-level overview of the game. |
| [diagrams/README.md](diagrams/README.md) | Mermaid system diagrams (architecture, data flow, biology, disease, market). |
| [tutorials/README.md](tutorials/README.md) | Player, developer and modder tutorials. |

### Game systems & design reference

| Document | Description |
|---|---|
| [ECONOMIC_SYSTEM.md](ECONOMIC_SYSTEM.md) | Finance, markets, contracts and optimization. |
| [ENVIRONMENTAL_DATA.md](ENVIRONMENTAL_DATA.md) | Norwegian environmental data integration (production zones). |
| [ACQUISITION_SYSTEM.md](ACQUISITION_SYSTEM.md) | Acquisition / expansion system. |
| [AUDIO_DESIGN.md](AUDIO_DESIGN.md) | Audio design specification. |
| [COMPETITOR_AI_DESIGN.md](COMPETITOR_AI_DESIGN.md) | Competitor AI design. |
| [CONTRACT_SYSTEM.md](CONTRACT_SYSTEM.md) | Contract warfare / contract system design. |
| [CONTRACT_SYSTEM_ARCHITECTURE.md](CONTRACT_SYSTEM_ARCHITECTURE.md) | Contract system architecture. |
| [CRISIS_CAPITALISM.md](CRISIS_CAPITALISM.md) | Distressed-asset / crisis system. |
| [ENHANCED_MAP_SYSTEM.md](ENHANCED_MAP_SYSTEM.md) | Enhanced map system. |
| [INTERNATIONAL_MARKETS.md](INTERNATIONAL_MARKETS.md) | International market expansion. |
| [PHOTO_MODE.md](PHOTO_MODE.md) | Photo mode. |
| [STAKEHOLDER_FACTIONS.md](STAKEHOLDER_FACTIONS.md) | Stakeholder faction system. |
| [SUSTAINABILITY_SYSTEM.md](SUSTAINABILITY_SYSTEM.md) | Sustainability score and green certification. |
| [VERTICAL_INTEGRATION.md](VERTICAL_INTEGRATION.md) | Vertical integration system. |
| [VAP-System-Architecture.md](VAP-System-Architecture.md) | Value-added processing chain architecture. |
| [VAP-Quick-Reference.md](VAP-Quick-Reference.md) | VAP processing quick reference. |
| [AQUACULTURE_DATA_INTEGRATION.md](AQUACULTURE_DATA_INTEGRATION.md) | Aquaculture data integration reference. |
| [barentswatch-fishhealth-integration.md](barentswatch-fishhealth-integration.md) | BarentsWatch fish-health integration. |

### Historical documents inside `docs/`

These describe work from earlier sprints; keep for provenance, do not treat as
current:

| Document | Note |
|---|---|
| [AQUACULTURE_INTEGRATION_LOG.md](AQUACULTURE_INTEGRATION_LOG.md) | Integration log. |
| [SHIP_READINESS_REPORT.md](SHIP_READINESS_REPORT.md) | Point-in-time readiness report. |
| [TODO_REGISTRY.md](TODO_REGISTRY.md) | Historical TODO registry. |
| [T66-Completion-Checklist.md](T66-Completion-Checklist.md) | T66 EmpirePanel checklist. |
| [T66-EmpirePanel-Summary.md](T66-EmpirePanel-Summary.md) | T66 EmpirePanel summary. |
| [T78-VAP-Processing-Implementation.md](T78-VAP-Processing-Implementation.md) | T78 VAP implementation. |
| [T93_COMPLETION_REPORT.md](T93_COMPLETION_REPORT.md) | T93 stub/completion report. |

## Historical root-level reports

These files predate the current documentation set (`PLAN.md`, `STATUS.md`,
`CHANGELOG.md`, `README.md`, `docs/`, `design/ART_DIRECTION.md`) and are
superseded. They are **not maintained** and should be read as history, not
fact. They are deliberately not deleted.

**Sprint / wave reports**

- `WAVE4_COMPLETE.md`, `WAVE4_TASKS.md`
- `QC_REPORT_WAVE4.md`, `QC_REPORT_WAVE4_FINAL.md`
- `NIGHT_SPRINT_SUMMARY.md`
- `T60_COMPLETION_REPORT.md`, `T60_SUMMARY.md`, `T60_COMMIT_MESSAGE.txt`
- `T62_COMPLETION_REPORT.md`, `T66_COMPLETION_REPORT.md`
- `T67_FINAL_QA_REPORT.md`, `T73_COMPLETION_SUMMARY.md`,
  `T73_DELIVERABLES_INDEX.md`, `TASK_T71_COMPLETION_SUMMARY.md`
- `T76_FIX_REPORT.md`, `T78-COMPLETION-SUMMARY.md`, `T80_SUMMARY.md`
- `T90_INTEGRATION_REPORT.md`, `T90_QUICK_START.md`
- `REFACTORING_ROADMAP.md`, `TEST_RESULTS.md`, `UI_POLISH_STATUS.md`

**Audit / review / QA reports**

- `CODEBASE_REPORT.md`, `CODE_REVIEW_REPORT.md`
- `T47_INTEGRATION_TEST_FIXES_SUMMARY.md`
- `T53_SAVELOAD_STRESS_REPORT.md`
- `T55_MORTALITY_DEEP_DEBUG_REPORT.md`
- `T56_PERFORMANCE_OPTIMIZATION_REPORT.md`
- `T58_QA_SMOKE_TEST_REPORT.md`
- `T61_BENCHMARK_VALIDATION_REPORT.md`
- `T73_SCREENSHOT_AUTOMATION_REPORT.md`
- `T88_TEST_FIX_SUMMARY.md`
- `T80_TEST_CHECKLIST.md`
- `TECHNICAL_DEBT_TRACKER.md`, `TODO_REPORT.md`, `TEST_RESULTS.md`
- `UI_AUDIT_T60.md`, `UI_FIX_PLAN.md`, `RELEASE_CHECKLIST.md`

**Feature / phase notes**

- `AdvancedMechanicsBlueprint.md`
- `GAMEPLAY_ROADMAP.md` (superseded by [PLAN.md](../PLAN.md))
- `GAMEUI_INTEGRATION.md`, `HANDOFF_GameUI.md`
- `INTEGRATION_COMPLETE.txt`, `MERGE_CONFLICTS.md`
- `NORWEGIAN_EVENTS_IMPLEMENTATION.md`
- `PHASE1_NOTES.md`
- `PHOTO_MODE_DELIVERY.md`, `PHOTO_MODE_INTEGRATION.md`
- `QUICK_START_SCREENSHOTS.md`, `SCREENSHOT_GUIDE.md`
- `RESEARCH-FEATURES.md`, `STICKY-FEATURES.md`
- `SUSTAINABILITY_PHASE1_DELIVERABLES.md`, `SUSTAINABILITY_PHASE2_COMPLETE.md`
- `T09-WIRING-SUMMARY.md`, `T71_MUSIC_REPLACEMENT_REPORT.md`

**Loose report/scratch artifacts** (also historical): `build_output.txt`,
`test-results.txt`, `test_smoke_results.txt`, `ui_audit_results.txt`,
`temp_gamestate.txt`, `T66_translation_keys.csv`, `T66_additional_keys.csv`, and
the root-level one-off scripts (`capture_screenshots.ps1`, `audit_ui.py`,
`fix_ui_batch.py`, `add-xml-docs.ps1`, `analyze.ps1`, `calibrate.csx`,
`Hatchery.gd`).

> When in doubt, trust `PLAN.md`, `STATUS.md`, `CHANGELOG.md` and `docs/` over
> anything listed as historical above.
