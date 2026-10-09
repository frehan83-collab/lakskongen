# Lakskongen: code review and next-level plan

Date: 2026-10-08. Reviewed at commit `b56ff2a`. The review covers the C# simulation, the 3D/visual/audio layer, and tests/CI/data/repo hygiene. The GDScript UI/bridge review pass did not finish (it ran out of credits), so the UI/UX items in Phase 4 still need a dedicated pass.

## Execution status (2026-10-08, commits 6286df7 / ec7710a / d30bd94)

- **Phase 0 DONE.** CI is 9/9 green locally with nothing skipped: builds both
  C# assemblies, runs dotnet tests + pytest, perf gate, token sync, JSON
  validation, scene validation, GDScript parse scan. Test count honest: 648
  (was "727" with ~92 triplicates). GitHub CI installs Godot 4.7.2 mono
  headless. Root decluttered, `.import` tracked, MCP autoload removed.
- **Phase 3 quick wins DONE** (medium/bigger items open). Fjord renders:
  blue depth-graded water, floating pens + boats + fish, real storm/night
  frames, sim toasts in 3D, harness log error-free. Remaining known issue:
  distant channel-wall silhouettes render as thin grey streaks at FJORD zoom
  (needs the FastNoiseLite terrain + wider valley — medium task).
- **Phase 1 core DONE** (S1/S2/S3/S5/S8/S9/S10 + 9 regression/ban tests).
- **Phase 2.1 DONE** (commit 6670aa1): per-site lice models with
  farm-to-farm infection, weekly counting (0.5 / 0.2 spring limit),
  Mattilsynet orders + forced treatment, stage-specific efficacy,
  resistance decay, DelousingHarmModel wired into treatments.
  653 tests pass, harness log error-free.
- **Blender pipeline LIVE.** MCP server wired (.mcp.json + opencode.json),
  official Blender Lab addon, `blender/` sources, `tools/blender_mcp_call.py`
  helper, `08b_fjord_farm` asset-QA closeup in the harness. First asset:
  procedural low-poly salmon (284 tris, skin/belly/fin; `blender/
  build_salmon.py` -> `salmon_v2.glb`) driving the pen MultiMesh with
  tangential heading. Orientation verified against the binary (nose -Z).
  Next assets: feed barge, wellboat, pen rails.
  **Deferred with reason:** price-clamp removal (needs the Phase 2
  supply-driven market first — removing it now just moves the band problem);
  PlayerScriptProbe (needs the Sim.Commands refactor); ISnapshotable +
  save round-trip hash test; ITickSystem split of AdvanceOneDay; dead-code
  removal (~29% of library still unwired); SimRng named streams (seed is now
  threaded + persisted, but Random.Shared/DateTime.Now remain in tick-adjacent
  code paths, grandfathered in DeterminismAllowlist.txt).
- **Phase 1 wave 1 DONE — Sim.Commands (player path).**
  `SimulationEngine.Commands.cs` now owns `StockSmolts`, `HarvestSite`,
  `TakeLoan`, `SetInsurancePolicy` with a typed `CommandResult` (stable
  error keys: `site_not_found` / `site_empty` / `insufficient_funds` /
  `invalid_loan_params` / `unknown_kind` / `engine_not_ready`). GameBridge
  is a thin adapter with the same GDScript surface (bool/Dictionary/double).
  The load path got one owner: `SimulationEngine.AdoptSavedState` took over
  the hand-copied field list from `GameBridge.LoadGame` verbatim — the
  documented gaps (research/achievements/scenario/tutorial/RAS/offshore) are
  still gaps until the ISnapshotable rewrite. `FinanceManager.AddLoanPrincipal`
  books loans as liquidity, not revenue (S5 residual).
  **PlayerScriptProbe** (`src/LakskongenSim.Tests/Integration/`) runs
  scripted `po3_red_sone` careers through these commands with events ON,
  zones ON and a mid-run save/load through `AdoptSavedState` (the identical
  adoption path the game uses). 2 career tests + 8 command-contract tests
  green; suite honest count now 663. Honest band caveat: the probe measures
  the wide band (FCR 0.9–2.6, price 70–96) because zones+events shift the
  tight SimProbe bands; probe uses the LIBRARY serializer (PascalCase/enum
  strings) vs the game's camelCase/enum-ints — adoption unified, formats not.
  **Deferred by design this wave:** OrderDelousing (needs the treatment-RNG
  stream), bridge tick logic (wellboat ETA, CheckWeatherImpacts, temperature
  mortality — still bridge-owned, so "two paths" persists partially until
  they move), BuildSeaSite/ApplyForLicense (double 30M charge + license
  model decision), the cash-vs-equity scenario win check is equity-based so
  AddLoanPrincipal can't be gamed day 1.
- **Phase 1 wave 2 DONE — SimRng + delousing + wellboat + storm/temp.**
  `SimRng` (SplitMix64, FNV-1a named streams, per-identity suffix
  overloads, engine-owned lifecycle) now feeds AI-feeding noise, hire
  morale and cohort transfers; the determinism ban's allowlist shrank
  accordingly (dead unseeded lines deleted, incl. DelousingSystem's
  never-drawn `new Random()`). `DelouseSite` moved into the library with
  the gill fix (site-level hit once per site — the bridge multiplied the
  dose scalar by pen count) and `DelousingSystem.TryParseMethodName` owns
  the name table. Wellboat booking is a command; due jobs execute inside
  `Advance` AFTER `State.Day++` (no off-by-one), freshwater baths use the
  site lice model with kill-once. Storm/temperature impacts moved into
  `AdvanceOneDay` (edge-triggered per episode, one insurance claim per
  episode via PayInsurance) — the tick/skip asymmetry is dead; the bridge
  now only projects a per-advance `DayNote` ledger onto legacy signals.
  **Calibration:** the bridge's storm loss factor 0.02/fish/episode had
  never run in any measured probe and pushed disease-stress mortality to
  ~67% vs the 14–16% band; recalibrated to 0.0003, all BalanceReportTests
  back in band (model bug, bands untouched).
  Division note: `CheckDiseaseOutbreaks` remains bridge-side as a UI
  observer (now reading `GetCompanyLiceAverage`); `CheckGameOverConditions`
  stays bridge-side (EndGame is Godot lifecycle). Suite 686/1 skip.
  **Phase 1 wave 3 DONE — ITickSystem + shared delousing kernel + P&L truth.**
  `ITickSystem` contract (Tick(engine), no data-bag, registration order =
  execution order) with three post-tick phases: StormImpacts →
  WellboatExecution → BalanceMirror; the ad-hoc advance wiring is gone,
  the mirror re-syncs daily, and the contract is designed so in-day
  AdvanceOneDay blocks can migrate without a contract change. The
  delousing core is now shared: `ApplySiteDelousing` + the crew/gill
  multiplier drive BOTH the player verb and Mattilsynet enforcement
  (whole-site treatment, force-pay, honest temperature cancellation).
  Storm loss factor moved into CalibrationRegistry (EnvironmentCalibration,
  v1.0.4) with a provisional corridor guard. P&L truth: harvest/sludge
  book revenue, feed books COGS, and GameBridge Revenue/Expenses/Profit
  read the statements — IPO gating now keys on real profitability
  (deliberate, documented). Suite 693/1 skip.
  **Phase 1 wave 4 DONE — in-day ITickSystem migration.**
  The 614-line AdvanceOneDay is now a pure pipeline loop: 25 in-day
  phases (EnvironmentAndWeather → HealthPrep → Biology → FarmOpex →
  CarbonLedger → EventsAndTime → Market → Finance → StockMarket →
  Sustainability → Sludge → TrafficLight → Brand → Competitor →
  Empire → Crisis → VerticalIntegration → TaxAndResourceRent →
  Investor → BloomDeck → MarketIntelligence → ContractExpiry →
  ProductMix → TutorialAnalytics → NightShift) in
  `InDayTickSystems.cs`, registration order = simulation order.
  Every interval clock is system-owned and rebuilt per Initialize —
  the `_quarterStartDay` leak across New Games is dead (S9, regression-
  tested). Shared-clock couplings preserved (sludge on its own 7-day
  clock, tax+resource-rent in one system for the same-day `== 0`
  dependency). Verbatim migration: all 17 balance-band tests, soaking,
  campaign smoke, SimProbe determinism identity, wellboat/storm/
  enforcement pass unchanged (695/1). Shared engine surface widened
  minimally (internal helpers + EventsEnabled/LegacyEventRng/FireEvent/
  TickFeedCostPerKgNOK accessors).
  **Phase 1 wave 5 DONE — one serializer + runtime cadence persistence.**
  `SaveJson.Options` is the single save format (PascalCase, enum NAMES,
  case-insensitive reads, IncludeFields): the Godot SaveLoadManager and
  the headless SaveLoadManager/PlayerScriptProbe share it, so "game
  saves" and "probe saves" can no longer drift. Backward compatibility
  is a property of the OPTIONS — legacy v1/v2 saves (camelCase, numeric
  enums) deserialize unchanged; the save version bumped to 3 with a
  2→3 migrator case. `EngineRuntimeSnapshot` rides the save payload:
  all 16 in-day interval clocks (via ITickClock on the tick systems),
  the storm-impact edge state, and the events flag — a loaded game now
  CONTINUES its weekly/quarterly cadence and cannot re-settle a
  mid-episode storm insurance claim (both regression-tested; legacy
  saves without the snapshot align the storm edge with current weather).
  RNG POSITIONS stay a documented gap: the hash-equivalence test is
  impossible until the legacy System.Random subsystems migrate onto
  SimRng streams (the bit-equivalence wave). Suite 700/1 skip.
  **Phase 1 wave 6 DONE — exact save/load continuation (the hash gate).**
  `SimRandom` (System.Random-compatible Knuth core incl. the net5+ `ii==0→55`
  remap and `inextp=21`, verified bit-identical by an oracle test) replaced
  `System.Random` in 11 subsystems + the engine without changing one drawn
  value (all 700 pre-existing tests green through the swap). Every live
  state the instrument found is now snapshotted: RNG stream positions
  (subsystems + SimRng streams), interval clocks (identity-keyed),
  storm edge, weather live state, market trajectory + intelligence +
  shock window, financial statements, feed adaptivity, AI-feeding learning
  state, price-council history, currency (+policy rate), storyteller
  cooldowns, traffic-light colors, tutorial/achievement/research progress
  rehydration, sustainability score rehydrate, the full GameState field
  list in AdoptSavedState. `ExactSaveHashTests`: save at day N, load,
  advance M — bit-identical serialization (3/3 green, incl. events-on).
  Real bugs the instrument exposed and fixed: `[JsonIgnore]`d site lice
  models, private-setter state dropped by STJ (`Zone.ElapsedDays`, full
  `SeaLiceModel` surface), getter-only carbon journals, `FinanceManager`
  instance replacement orphaning achievement/storyteller references
  (now reset-in-place), tutorial/achievement double-pay on load, research
  effects dead after load, analytics reading live-only subsystems.
  Model fix (rule: fix the model, not the band): zone water chemistry is
  now volume-consistent (`Zone.WaterVolumeM3`, O₂ flushing as exchange
  toward inflow) — dissolved oxygen no longer pins at 0 under any real
  biomass; careers recover toward reference (mort 67.6→61.2, price
  42.7→68.5+in-band at harvest).
  **Still open:** legacy seeded subsystems' full migration onto SimRng
  *named streams* (bit-equivalence golden tests), BuildSeaSite/
  ApplyForLicense double-charge, the episodic storm model, scenario
  ActiveScenario rehydration, contract persistence.
  Suite 713/1 skip.
  **Review follow-ups (from the post-impl adversarial pass, accepted):**
  (1) `StormFishLossBaseFraction = 0.0003` is a PROVISIONAL single-variable
  band fit — it must become an episodic storm model (NVI-attributed storm
  mortality, net-integrity exposure in ApplyStormHazards, magnitude band
  test 0.5–3%) and move into CalibrationRegistry; (2) `_lastWeatherState`
  resets on load, so loading mid-storm re-settles that episode's insurance
  claim — weather persistence is an ISnapshotable item; (3) economy
  properties `Revenue`/`Expenses`/`Profit` in GameBridge.Economy read the
  stale Balance mirror and need the P&L fix (S4); (4) wellboat/storm
  execution currently hangs on the `Advance` loop — ITickSystem must make
  them first-class tick phases with the mirror sync in a structured
  post-tick slot.
- **Phases 2/4/5 NOT STARTED.** Phase 4 still needs the UI/bridge review pass.

## Verdict

The game has a large feature surface (about 45k LOC of sim and 38k of Godot scripts), but many core loops are not actually connected. Three problems hide this:

1. **Tests use a different path from the game.** The headless tests and `SimProbe` use their own stocking path, have events off and skip zones. The player's main actions (stock, harvest, delouse, loans, insurance) are implemented in `GameBridge`, so headless tests never run them.
2. **Some realism bands come from clamps, not from the model.** Realized price ~88 NOK/kg is produced by a ±8 NOK clamp (`MarketSimulation.cs:361-377`). The FCR of 1.20 is circular: growth is defined as feed/FCR (`FeedSystem.cs:264,279`).
3. **The quality signals are overstated.** About 92 of the "727 tests" are triplicates from the `tests/` layout. The "CI 7/7" result skips the Godot steps on GitHub, and `verify.yml` is red. `LakskongenSim.sln` does **not** build `godot/LakskongenGame.csproj`.

**Next level means:** first make the existing systems real, connected and tested through the same path the player uses. Then build a signature depth layer on top: per-site lice, production areas and the traffic light, licences, and an endogenous market. Then make the 3D fjord a live readout of the simulation. New feature breadth comes last.

---

## Review: top findings (verified spot-checks marked ✔)

### Simulation correctness (P0)
| # | Finding | Evidence |
|---|---|---|
| S1 | Sea lice are effectively off in the live game. There is one global model; stocking through the bridge never seeds it; `AdvanceDay` returns early at 0 lice. Neighbour pressure is a multiplier, not an inflow. `SeaLiceLoad` is never updated, so traffic lights stay green. | `SeaLiceModel.cs:149` ✔, `SE:335`, `GB.Farm:48-119`, `SE:2655` |
| S2 | Loading a save breaks harvest. `FishPopulation` is serialized twice (`SeaSite.Pens` and `State.Populations`) and never re-linked. Biology then ticks one copy while harvest acts on the other. | `GSave:57-62` ✔ (no ReferenceHandler), `GB.Core:195-261` |
| S3 | The environment never advances. `EnvironmentModel.Zones` is never filled from `State.Zones`, so water chemistry is frozen. Blooms only deplete oxygen, so DO ratchets down and `plankton-crash` re-fires. | `EnvironmentModel.cs:11-17` ✔, `PlanktonBloom.cs:182-202` |
| S4 | The P&L is broken. Harvest revenue is not recorded and COGS is never recorded. Net income is cumulative and taxed both monthly and quarterly, so tax effectively never fires. The rent-tax deduction is applied every quarter. | `FinancialStatements.cs:106`, `SE:1887-1917, 2267` |
| S5 | Free money: loans are booked as revenue with no liability, and insurance premiums are never charged. Profit scenarios are won on cash, so one loan wins "Nordlands Utfordring" on day 1. | `GB.Economy:308-310` ✔, `GB.Depth:544-567`, `ScenarioSystem.cs:683` |
| S6 | Feed does not drive growth. `FeedSystem` outputs (expected growth, CV, N/P waste) are discarded, and the SDE growth model runs separately. Feeding decisions only change cost. | `FeedSystem.cs:231-297`, `SE:1605-1619` |
| S7 | MAB/MTB and traffic-light consequences have no effect. The capacity multiplier changes a field nobody reads, stocking never checks the cap, and licence cost is 30M in code against 150M in calibration. | `TrafficLightSystem.cs:108`, `GB.Depth:751` |
| S8 | Delousing hits the global lice model about 16 times for an 8-pen site (per pen × 2 paths) and applies efficacy twice. | `GB.Farm:810-818`, `DelousingSystem.cs:174`, `SeaLiceModel.cs:240-266` |
| S9 | New Game reuses the old `State`, so a new game can inherit sites and fish. Each `Initialize` stacks another `Storyteller`. `NorwegianEvents` is registered before `TrafficLightSystem` exists. After a load, many systems hold a dead `FinanceManager`. | `SE:33, 303, 306 vs 417, 3253` |
| S10 | The live game is never seeded, and many subsystems use `new Random()`, `Random.Shared` or `DateTime.Now` for game logic (vertical integration, crisis, factions). Hashes from `HashCode.Combine(string)` change per process. | `GB.Core:139,174`, `VerticalIntegrationSystem.cs:178…`, `Zone.cs:128` |

### Dead or cosmetic systems (about 12.9k LOC, roughly 29% of the sim)
- **Never constructed:** `InvestmentManager`, `IntelligentAdvisor`/`AlertSystem`/`RecommendationHistory`, `EconomicAdvisor`, `IndividualFish` + `SDEGrowthModel` + epigenetics, `GeneticEngineering`, `DelousingHarmModel`, the `Data/RealData/*` stack, `WaterColumnPhysics`, `MonteCarloPriceEngine`, `FuturesContract`, all `Optimization/*` optimizers, `MultiFarmManager`, `DaySummary`, `DifficultyManager`.
- **Ticked, but nothing reads the result:** `StockMarketSystem` (the bridge returns hard-coded values), `CompetitorAI` (headlines only), `EmpireProgression`, `CrisisCapitalism` (never triggered), `VerticalIntegration` (savings never applied), `Acquisition`/`License`/`StakeholderFaction` (not ticked), `ResearchSystem` (18 of 25 nodes do nothing), sludge (unreachable branch at `SE:1802`).
- **Four overlapping advisors.**

### Save/load
- Two serializers with different formats: the game uses camelCase with enums as ints; the tests use PascalCase with enums as strings. Load copies a fixed list of fields by hand and misses research, achievements (which then re-pay their rewards), scenario, tutorial and RAS/offshore state. Weather, market, lice, disease, storyteller, RNG and contracts are not persisted at all.

### 3D, visuals and audio
| # | Finding | Evidence |
|---|---|---|
| V1 | **None of the environment's node references resolve at runtime.** The exports are typed (`@export var terrain: FjordTerrain`), but the `.tscn` has no `node_paths=` entry, so they are null. Day/night, weather, camera presets and set_time/season do nothing, and the harness "night/storm/underwater" shots are the same frame. | `FjordEnvironment.gd:8-12` ✔, `.tscn:200` ✔ (0 `node_paths`) |
| V2 | No pens appear. A fresh game has no zones. The harness requests a nonexistent `small_farm` scenario (`forste_sesong` and `po3_red_sone` exist). Building a site costs 30M against 10M QA cash. Pens would sit 5–27 m under water. The pen net shader doesn't compile (Godot 3 `hint_albedo`). Fish are 16 orange boxes. `VisualAdapter3D` is a second, broken spawner. | `ScreenshotHarness.gd:36`, `FishPenManager.gd:147,175,303`, `pen_net_shader.gdshader:6` |
| V3 | Terrain looks like snow. The heightmap's median is 312 m, so 77% is above the snow line (150 m). The `_fbm` "noise" is summed sines, which gives the diagonal stripes. Textures have no mipmaps. The camera looks straight down, so the horizon is never visible. | `FjordTerrain.gd:99-153`, `terrain_shader.gdshader:112` |
| V4 | Water looks pale and striped. `depth_diff` mixes view-space metres with [0,1] depth, so the whole surface becomes foam and shallow colour. Foam alpha is banded by column. Normal maps are flat and the z channel is lost. Waves alias against the 31 m mesh spacing. | `water_shader.gdshader:124,161,192` |
| V5 | The tilt-shift black band was a NaN (`exp(-0.5*0/0)`), not a Metal problem. It can be re-enabled with a `+1e-4` guard. | `tilt_shift.gdshader:34` |
| V6 | Performance: the ReflectionProbe uses `update_mode=1` (6 cube faces of the whole 4 km scene every frame). The underwater post-process samples the screen even when inactive. No anti-aliasing is configured. | `FjordEnvironment.tscn:242-249` |
| V7 | Audio: up to three `storm.ogg` layers can play at once; there is no underwater low-pass, no positional audio and no limiter; `AudioManager.cs` duplicates the audio system. | `AudioController.gd:103-164` |

### Tests, CI, data and repo
- `verify.yml` is red: `tests/test_bridge_contract.py` only parses the 17-line anchor file. `ci.sh` doesn't run pytest.
- The Godot C# assembly isn't compiled by the sln or by CI.
- The three `tests/*.csproj` files share one folder, so tests run 3× and share `obj/`. The sln has two projects named `SaveLoadTests`. `LogisticsTests` is a console app.
- `.gitignore` ignores `*.import`, so music `loop=true` and normal-map import settings are lost on a fresh clone. `project.godot` autoloads the ignored `godot_mcp_toolkit`, so a fresh clone boots with an error, and an export would ship a dev MCP server.
- Data has no schemas. There are 30 broken encyclopedia `relatedIds`. There are two achievement systems with no shared IDs, two tutorial readers, and a dead `balancing.json`. Mods: only scenarios reach the game, and mod field names differ from the core files.
- Hygiene: about 80 report files at the root; 26 MB of screenshots tracked and rewritten on every capture; large binaries not in LFS.
- Docs disagree on the build scope, CI status, test counts, tutorial step counts and engine version.

---

## The plan

Each phase ends with a measurable exit gate. Phases 0–1 are mandatory before any new features.

### Phase 0: Truthful foundation (about 1 week)
Goal: green CI that really reflects the game.
1. Add `godot/LakskongenGame.csproj` to the CI build (sln or a separate step). Fix `test_bridge_contract.py` to glob `GameBridge*.cs`. Add pytest to `ci.sh`.
2. Fix the test layout: one folder per csproj, unique names, delete or convert `LogisticsTests`, move in the orphan sustainability test.
3. Fix `.gitignore`: commit `*.import`. Remove the MCP toolkit autoload and plugin from `project.godot`, or gate them by feature tag, and exclude them from exports. Ignore `godot/screenshots/` and `.pytest_cache/`. Move large binaries to LFS.
4. Archive or delete the root clutter (list in the review). Trim PLAN/STATUS to current state and make STATUS the only place that reports test counts.
5. Install Godot headless in GitHub CI (setup-godot) so scene validation and the parse scan really run.

**Exit:** GitHub CI is green with all 7 steps executed (none skipped), and the Godot assembly is compiled.

### Phase 1: Make the simulation honest (2–3 weeks)
Goal: every player verb goes through the library and is tested end to end.
1. **Move player verbs into the library** as `Sim.Commands` (`StockSmolts`, `Harvest`, `Delouse`, `TakeLoan`, `BuyInsurance`, `BuildSite`, …) and make `GameBridge` a thin adapter. Move the bridge-side tick logic (storm, temperature mortality, wellboats) into the engine so timer ticks and skip-days behave the same.
2. **Add a `PlayerScriptProbe` test harness** that plays scripted careers through the commands, with events on and zones on, plus a save/load round-trip mid-run. Replace `SimProbe`'s shortcuts. Balance bands must hold **without** the price clamp.
3. **Save/load rewrite:**
   - One serializer, with enums as strings.
   - A `ISnapshotable` interface per subsystem, holding its own state and RNG state.
   - Object references stored as IDs (populations referenced by ID from pens).
   - `SimulationEngine.Reset()` for New Game.
   - A property-based round-trip test: `hash(state after N days) == hash(save→load→N days)`.
4. **Determinism:** a single `SimRng` service with named streams, seeded from the save. Ban `Random.Shared`, `new Random()` and `DateTime.Now` in the library with an analyzer or a grep test in CI. Replace `HashCode.Combine` for layout with a stable hash. The live game picks a seed and stores it.
5. **Fix S1–S9:**
   - Environment zone wiring and oxygen reaeration.
   - Lice seeding at stocking, plus a `SeaLiceLoad` sync.
   - Delousing applied once per site.
   - P&L periods with revenue and COGS recorded.
   - Loans as liabilities with amortization.
   - Insurance premiums charged.
   - Scenario wins on equity or EBIT instead of cash.
   - Init-order fixes.
6. **Remove the dead weight.** Delete or move to `archive/` everything that won't be wired in Phase 2–3: `InvestmentManager`, `IntelligentAdvisor` stack, `EconomicAdvisor`, `MultiFarmManager`, `DaySummary`, `Performance/*`, and so on. Consolidate the four advisors into `InterventionAdvisor`. Unify achievements (C#) and the tutorial reader (C#), and delete `balancing.json`.
7. **Split `AdvanceOneDay`** (595 lines) into an ordered `ITickSystem` pipeline (`Order`, `Interval`, `Tick(ctx)`), so every system registers once and is saved and reset uniformly.

**Exit:**
- The `PlayerScriptProbe` 10-year career is in band, with no clamp.
- The save round-trip hash test passes.
- The determinism ban is enforced in CI.
- Library LOC goes down.

### Phase 2: Signature depth, "regulatory chess" (3–5 weeks)
These are the features that would make Lakskongen distinct from other management sims. Each one builds on code that already exists.
1. **Per-site lice with farm-to-farm infection:**
   - Lice tracked per site by stage.
   - Copepodid inflow between sites from a seaway connectivity matrix.
   - Weekly counting at ≥4 °C.
   - Limit of 0.5 adult females (0.2 in spring weeks 16–21 south / 21–26 north).
   - Mattilsynet orders with deadlines.
   - Stage-specific treatment efficacy.
   - Resistance that decays over time.
   - `DelousingHarmModel` wired in, so treatments cause welfare harm and mortality.
2. **Production areas PO1–PO13 and a real traffic light:**
   - Biennial assessment from modelled wild-smolt lice mortality (<10 / 10–30 / >30%).
   - ±6% MTB per licence.
   - Per-licence MTB enforced (780 t; 945 t in Troms and Finnmark).
   - Synchronized fallowing.
   - `EnvironmentalFlexibility` as the escape route out of red.
3. **Feed-limited growth (TGC) with a weight distribution:**
   - Growth comes from feed eaten and biological FCR; economic FCR becomes an output, not an input.
   - A tracked CV, grading, and maturation risk.
   - Turn on `QualityGrader` with price per weight class and grade (superior/ordinary/production) at harvest.
   - Charge harvest, processing and wellboat costs, all from calibration.
4. **Endogenous market:**
   - Remove the clamp.
   - Spot price driven by national standing biomass and harvest (competitors' `WeeklyHarvestTonnes` plus the player).
   - Real seasonality (spring high, autumn low, ±20–30 NOK).
   - Fish Pool forwards and hedging using the existing `FuturesContract` and `MonteCarloPriceEngine`.
   - `PriceCouncil` norm price feeding the resource-rent tax.
5. **Mortality attribution and welfare.** Cause-of-death breakdown in NVI style (treatment, infection, gill disease, oxygen, temperature) via `FishWelfare.ModifyCondition`. Welfare affects quality grade and inspection pressure.

**Exit:** In each of three scripted careers, the player faces at least one forced lice treatment, at least one traffic-light change and at least one price cycle. Each decision type measurably changes the outcome (sensitivity tests).

### Phase 3: Living fjord, 3D as a readout of the simulation (2–3 weeks, can run in parallel with Phase 2)
**Quick wins (days):**
- Add `node_paths` to `FjordEnvironment.tscn` (and to TimeOfDay and WeatherSystem).
- Harness: use `po3_red_sone` as the QA scenario.
- Fix the pen net shader.
- Put pens at y=0 (collar floating at the surface).
- Delete `VisualAdapter3D`'s spawner.
- Water: fix the depth calculation, use `.xy` for the foam normal term, rebuild normal z, and apply the colour values in the review.
- Terrain: `snow_height` 400, `grass_height` 240, `cliff_slope` 0.38, mipmaps, rock tint.
- Camera orbits the focus point (FJORD preset 380 m / −26° / 45° FOV) and starts on the first site.
- Environment: soft-light glow, ambient 0.6, AgX tonemap, low fjord mist.
- ReflectionProbe `update_mode=0`; hide the underwater post-process when inactive; turn on MSAA 2× (or MetalFX).
- Re-enable tilt-shift with the NaN guard.

**Medium:**
- `FastNoiseLite` ridged terrain with domain warp; generate it in C# or on a thread.
- Sim→3D sync node reading sim state at about 1 Hz:
  - Weather state → waves, foam and fog.
  - Season → snow line and grass colour.
  - Lice per pen → collar tint via instance uniforms.
  - Fish MultiMesh with `visible_instance_count` proportional to biomass, scaled by mean length, schooling animated in the vertex shader.
  - Feeding particles at daylight feeding times.
  - Wellboats travelling along a `Path3D` according to `eta_days`.
  - Bloom tint around affected sites.
- Feed barge and mooring layout per site; ghost "available site" rings and grey competitor farms so a new game isn't empty.
- Audio: underwater low-pass, seagulls at FARM zoom, positional barge and boat loops, a limiter on Master, wind layers instead of `storm.ogg` ×3.

**Bigger:** a real Norwegian fjord heightmap (Kartverket DEM), proper pen/barge/wellboat/salmon assets, and opaque water with refraction.

**Exit:** A screenshot harness run on `po3_red_sone` shows pens, fish, a barge, a boat and distinct day/night/storm/underwater frames. The harness diffs the frames and fails if any two are identical. The game holds 60 fps at 1440p on the M1 Pro.

### Phase 4: Mid/late game and UX (3–4 weeks)
1. **Licence economy:** licence auctions for green-zone growth, with AI rivals bidding from `CashReserves`; calibration's 150M licence cost.
2. **Competitors that compete:** competitor actions move supply and price. Distress → `CrisisCapitalism.TriggerCrisis` → acquisition targets that transfer sites, licences and MTB.
3. **Corporate finance:** periods with annual closing, 22% corporate tax plus 25% resource-rent tax on the norm price, loss carry-forward, biomass held as inventory, covenants, an IPO with quarterly guidance pressure.
4. **Research and vertical integration made mechanical:** research `GetTotalEffect` keys read at the matching simulation points, vertical-integration savings applied, game-day construction timers.
5. **UI/UX pass.** This needs its own review, since this pass didn't complete.
   - Find GDScript → C# call-name mismatches, and generate a typed bridge or a contract test from the C# signatures.
   - Event-driven refresh instead of polling.
   - Theme tokens and `tr()` everywhere.
   - A single "what needs my attention" inbox driven by `InterventionAdvisor`.
   - Time controls that auto-pause on alerts.
   - Explanations for every number shown ("why did my price drop?").
   - Accessibility: UI scaling, colour-blind palette, gamepad.
6. **Modding parity.** Mod events, research and species flow into the engine; one naming scheme for core and mod files; JSON Schemas with cross-reference checks in CI.

**Exit:**
- A blind playtest of 5 people × 2 hours: everyone completes `forste_sesong`, and at least 3 say they understand why they made or lost money.
- No dominant strategy found by the `PlayerScriptProbe` strategy sweep.

### Phase 5: Ship (2–3 weeks)
- Windows/Linux/macOS export presets, with a CI export job and artefact upload.
- Save fuzzing; weekly playtest builds.
- Decide the licence; finish the Steam page, the demo build (`forste_sesong` plus `po3_red_sone`) and the Next Fest plan.
- Update VISION/GDD to match reality (no Lua; multiplayer deferred).

---

## Cross-cutting rules (adopt now)
1. **No feature without a player-path test.** A feature is only "done" when `PlayerScriptProbe` shows it changing the outcome.
2. **No clamps to hit realism bands.** If a band is missed, fix the model.
3. **Every subsystem implements `ITickSystem` + `ISnapshotable`,** or it doesn't ship.
4. **Calibration SSOT is enforced:** a grep test fails if `BalancingConfig` fields go unread or numeric literals appear in tick code.
5. **Visual claims need a non-identical screenshot diff.** Headless "green" is not evidence of how the game looks.

## Suggested first 10 tasks (in order)
1. Make CI compile `LakskongenGame.csproj`; fix the bridge contract test; add pytest to `ci.sh`.
2. Fix the `tests/` layout and remove the triplicated tests.
3. Fix `.gitignore` (`*.import`, MCP toolkit) and remove the screenshots from tracking.
4. Add `node_paths` in `FjordEnvironment.tscn`; set the harness scenario to `po3_red_sone`; fix the pen shader and pen y.
5. Water depth/normal/foam fixes, terrain thresholds and the camera orbit.
6. Save: store population IDs and re-link on load (S2), and add `SimulationEngine.Reset()`.
7. Book loans as liabilities and charge insurance premiums; scenario wins on equity (S5).
8. Wire `EnvironmentModel` to `State.Zones` and add oxygen reaeration (S3).
9. Seed lice at stocking through the bridge, sync `SeaLiceLoad`, apply delousing once per site (S1, S8).
10. Build `PlayerScriptProbe` and re-measure balance with the price clamp removed.
