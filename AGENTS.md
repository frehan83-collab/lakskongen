# AGENTS.md — LakskongenSim Agent Instructions

## Project Overview
LakskongenSim is a realistic Norwegian salmon-farming simulation with Godot 4.7.2 (Mono) frontend and C# .NET 10 backend. The engine is deterministic, save/load round-trips exactly, and all subsystems implement `ITickSystem` + `ISnapshotable`.

**Key Architectural Principles:**
- **Determinism first**: `SimRng` SplitMix64 with frozen streams; `DeterminismBanTests` enforces no `Random`, `DateTime.Now`, `Guid.NewGuid()` in src/
- **Calibration SSOT**: All numeric constants in `CalibrationRegistry` / `BalancingConfig`; grep test fails on literals in tick code
- **No clamps to hit realism**: If a band is missed, fix the model
- **Visual claims need screenshot diffs**: Headless "green" ≠ evidence
- **Player-path tests required**: Feature not "done" until `PlayerScriptProbe` shows outcome change

## Repository Structure
```
lakskongen/
├── src/LakskongenSim/           # Core simulation engine (.NET 10)
│   ├── Core/                    # SimulationEngine, GameState, SimRng, Tick systems
│   ├── Finance/                 # CompetitorAI, CrisisCapitalism, LicenseAuctionSystem
│   ├── Events/                  # CrisisCapitalismSystem, Event types
│   ├── Farm/                    # SeaSite, FishPopulation, Biology
│   ├── Market/                  # MarketSimulation, PriceCouncil
│   └── Tests/                   # xUnit tests (667 passing)
├── godot/                       # Godot 4.7.2 Mono project
│   ├── scripts/
│   │   ├── autoload/            # GameBridge partials (Core, Farm, Market, Depth, Economy, Licence, Content)
│   │   ├── bridge/              # SimProxies.gd, SimActions.gd, SimFormatters.gd, SimState.gd
│   │   └── SimBridge.gd         # Facade forwarding to proxies
│   └── data/                    # calibration.json, scenarios.json, tutorial_steps.json
├── tools/ci.sh                  # 14-step CI pipeline
├── docs/NEXT_LEVEL_PLAN.md      # Phase 4+ roadmap
└── docs/REALISM_CHECKLIST.md    # Calibration bands
```

## Development Commands
```bash
# Full CI (run before any commit)
./tools/ci.sh

# Build only
dotnet build LakskongenSim.sln

# Run all tests
dotnet test src/LakskongenSim.Tests

# Run specific test filter
dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~LicenseAuctionTests"

# Capture screenshots (visual regression)
./tools/capture_screenshots.sh

# Godot headless (for bridge contract tests)
cd godot && dotnet run -- --headless --quit
```

## Phase Status (from NEXT_LEVEL_PLAN.md)

### ✅ Phase 4.1 — Licence Economy (COMPLETE)
- Green-zone growth-licence auctions with 150M NOK reserve, 7-day window
- AI rivals bid from `CashReserves` with appetite/lobbying-scaled ceilings
- Player bid gate: ≥5% over current, rounded to whole millions
- Settlement: highest solvent bidder wins (player pays from balance, rival from CashReserves)
- Rival-initiated openings (p=0.02/day when no auction open)
- Build gate: first site free, new zone requires unconsumed won licence
- Build cost: 6M NOK (was 30M hardcoded)
- Save/load round-trip via single eager `license_auction` RNG stream
- **Tests**: 17 new tests in `LicenseAuctionTests.cs` (calibration, bidding, settlement, determinism, save/load, 400-day rival opener)
- **Bridge**: `GameBridge.Licence.cs` partial with `GetLicenseCost`, `ApplyForLicense`, `PlaceLicenseBid`, `GetLicenseAuctions`, `HasGrowthLicense`, `CanBuildSeaSite`

### ✅ Phase 4.2 — Competitors That Compete (COMPLETE)
- **CrisisCapitalismSystem determinism fixes**: 
  - `DateTime.Now` → `StartDay` (simulation day)
  - `Guid.NewGuid()` → deterministic IDs (`crisis_{id}_{type}_{day}`, `asset_{crisisId}_{type}_{idx}`)
  - `IsActive(int currentDay)` instead of property
- **Distress wiring**: `CompetitorAI.OnCompetitorDistressed` event → `CrisisCapitalism.TriggerCrisis` with competitor roster
- **Distressed assets linked to actual competitor state**: 
  - `SourceCompetitorId`, `SiteId`, `LicenseCount`, `MABTonnes`
  - Values derived from competitor's actual `SiteCount`, `LicenseCount`, `TotalMABTonnes`
- **Player acquisition transfers real assets**: `AcquireDistressedAsset` → adds `SeaSite`, `LicenseRecord`, reduces competitor holdings
- **Save/load**: `CrisisEventState` + `DistressedAssetState` in `GameState`, restored via reflection on private `_availableAssets`
- **Bridge**: `GetDistressedAssets()`, `AcquireDistressedAsset()` in `GameBridge.Licence.cs`, proxied via `SimProxies.gd`/`SimBridge.gd`

### ✅ Phase 4.3 — Corporate Finance (COMPLETE)
- **Annual closing & periods**: Quarterly AdvanceQuarter() with year-end AnnualClose() resetting P&L accounts
- **22% corporate tax + 25% resource rent tax**: TaxSystem.ComputeTotalTax() combines both on net income and resource rent base (norm price × volume - 65% standard deduction)
- **Loss carry-forward**: 80% annual limitation, tracked in TaxSystem.LossCarryForward, auto-utilized against future profits
- **Covenants**: Leverage ≤4.0x, Interest coverage ≥3.0x, Equity ratio ≥30%, Liquidity ≥50M NOK; tested quarterly + annually; breaches tracked with resolution state
- **IPO system**: Private → Preparing → Roadshow → Listed stages; quarterly guidance with share price impact on miss/beat; market drift applied daily
- **Biomass inventory**: FinancialStatements.UpdateBiomassInventory(biomassKg, normPricePerKg) tracks live fish value as asset
- **Save/load**: All new state (TaxSystem loss carry-forward, CovenantSystem breaches, IPOSession stage/price/guidance history, FinancialStatements biomass/period) persisted via Core.CorporateFinanceSystemState
- **Bridge**: TaxSystem, CovenantSystem, IPOSession accessible via SimulationEngine properties for future UI integration
- **Tests**: All 667 existing tests pass; determinism gates (ExactSaveHashTests, EngineTwinRunDeterminismTests, PlayerScriptProbe) pass

### 🔄 Phase 4.4 — Research & Vertical Integration (PENDING)

### ✅ Phase 4.4 — Research & Vertical Integration (COMPLETE)
- **Research effects now consumed at simulation points:**
  - `growth_rate_bonus` → GrowthModel SDE growth rate multiplier
  - `fcr_reduction` → FeedSystem FCR multiplier  
  - `feed_waste_reduction` → FeedSystem waste fraction multiplier
  - `mortality_reduction` → MortalityModel additive reduction to total daily rate
  - `disease_resistance_bonus` → MortalityModel disease rate multiplier
  - `market_price_bonus` → MarketSimulation price multiplier (combined with VerticalIntegration quality premium)
  - `sustainability_score_bonus` → SustainabilityManager score additive
- **Vertical Integration made mechanical:**
  - Monthly OPEX from owned assets charged to FinanceManager
  - Cost savings per kg from completed tiers → reduces effective FCR
  - Quality premium multiplier → increases market price
  - Construction timer framework (30-day interval)
- **Determinism:** All new parameters flow through engine properties, no new RNG
- **Tests:** All 667 pass; determinism gates (ExactSaveHashTests, EngineTwinRunDeterminismTests, PlayerScriptProbe) pass

### ✅ Phase 4.5 — UI/UX Pass (COMPLETE)
- **AttentionInbox panel** — unified "what needs my attention" inbox driven by C# aggregator (`GetAttentionItems`) + market-drop item computed in GDScript; event-driven via SimBridge signals (`day_advanced`, `state_updated`, `alert_raised`); priority-based sorting; auto-refresh toggle
- **AttentionInbox scene** — `scenes/ui/AttentionInbox.tscn` (CanvasLayer layer=100, white DialogPanel 780×540, unique node names) + `scripts/ui/AttentionInbox.gd` with category grouping, priority badges (emoji+text CVD-safe), action buttons, auto-refresh toggle
- **HUD integration** — 🔔 InboxBtn in `scenes/ui/HUD.tscn` + `scripts/ui/HUD.gd`; `set_paused_state(paused)` public method; tooltips on balance/fish/temp/price/alerts/weather
- **Auto-pause on alerts** — `scripts/Main.gd` auto-pauses on NEW critical alerts (deduped via `_last_auto_pause_msg`); setting `gameplay.auto_pause_critical` (default true) in SettingsMenu
- **Input** — `toggle_attention_inbox` action (physical keycode 73 = 'I') in `project.godot`; `_input` guard for LineEdit/TextEdit focus + ESC
- **Bridge & signals** — `AttentionInboxRequested` signal in `GameBridge.Farm.cs`; `OpenAttentionInbox()` → `SimulationEngine?.OpenAttentionInbox()`; direct connect (no `has_signal` — bridge-contract rule); `_tr_or` helper using `TranslationServer.Translate` compare-then-fallback
- **Settings** — `accessibility.ui_scale` (80/90/100/110/125%) in SettingsMenu; applied via `content_scale_factor`; persisted to `user://settings.json`; `_apply_saved_ui_scale()` in `default_theme.gd` at boot
- **Known limitations** — `high_contrast` setting collected+persisted but NOT applied anywhere (documented decision); color-blind safety via emoji/text priority badges (not color-only)
- **Scene validation & CI** — all 54 scenes load cleanly; CI 15/15 passes; screenshots captured

### ✅ Phase 4.6 — Modding Parity (COMPLETE — species frozen as non-goal)
- **Mod events/research/scenarios flow into the engine** (species frozen — see below):
  - `SimulationEngine.LoadMods(dir)` configures the mods directory; every `Initialize()` re-applies mods onto the fresh systems (idempotent per engine state — events never double-register). The bridge resolves `user://mods` and hands it over; `SimulationEngine.ResolveModsDirectory()` is the headless fallback (`<exe>/mods`)
  - Mod events → `ModLoader.BuildEventDefinitions(FinanceManager)` → `Storyteller.RegisterEvent`
  - Mod research → `ModLoader.GetAllResearchNodes()` → `ResearchSystem.AddModNodes` (additive, core nodes win on ID collision)
  - Mod scenarios → `ModLoader.GetAllScenarios()` → `ScenarioSystem.RegisterModScenarios` (converts `ModScenario` → `ScenarioDefinition`, never overwrites core)
- **Single naming scheme** — `tools/validate_mods.py` enforces the ModLoader prefix-dispatch convention (`events_*`/`research_*`/`fish_*`/`scenario_*`) and mirrors the real `EventCategory`/`ResearchBranch`/`ProductionRegion`/`WinCondition` enums; fixed the example pack's free-text `win_condition` → enum value
- **JSON Schemas** — `schemas/mods/{mod,events,research,fish,scenarios}.schema.json`
- **CI cross-reference checks** — `step_validate_mods` (15th CI step) validates manifests, file references, dependency closure, duplicate IDs, scenario `initial_events` → known event IDs, enum values, and temperature-range sanity
- **Tests** — `ModWiringTests.cs` (8 tests: event registration, research add/no-overwrite, scenario register/no-overwrite, species load, dependency ordering)
- **`.gitignore`** — runtime-generated `mods_config.json` ignored
- **Species frozen (documented non-goal):** `ModFishSpecies` is parsed (`GetAllFishSpecies`) but NOT consumed by any simulation point — the engine hardcodes Atlantic salmon and there is no player-selectable species path. Decision (council, 2026-10-12): species wiring feeds neither the Phase 4 exit gates (blind playtest, no-dominant-strategy sweep) nor Phase 5, so it stays "parsed, not consumed" rather than faked green. Reference mod: `godot/mods/example_norwegian_pack/`.

### 🔄 Phase 5 — Ship (PENDING)
- Windows/Linux/macOS export presets + CI export job + artefact upload (macOS export-verify already passes in CI)
- Save fuzzing; weekly playtest builds
- Decide licence; finish Steam page + demo build (`forste_sesong` + `po3_red_sone`) + Next Fest plan
- Update VISION/GDD to match reality (no Lua; multiplayer deferred)

### Remaining Phase 4 exit gates (human/empirical — NOT code-verifiable)
The Phase 4 "Exit" in `NEXT_LEVEL_PLAN.md` has two gates an agent cannot close autonomously. Treat them as explicit, separately-scheduled items, not as implicit "CI is green = done":
1. **Blind playtest** — 5 people × 2 hours: everyone completes `forste_sesong`, ≥3 say they understand why they made/lost money. *A calendar item only the owner can schedule.*
2. **No-dominant-strategy sweep** — `PlayerScriptProbe` strategy sweep. **Must define the failure criterion before trusting a pass** (a sweep that finds no dominant strategy can just be too shallow). Note the tension: "no clamps to hit realism bands" collides with "no dominant strategy" — a real dominant strategy needs a model fix, not a clamp.

## Key Systems Reference

### RNG & Determinism
- `SimRng` (SplitMix64) + `SimRngStreams` (frozen enum)
- Streams created EAGERLY in `Initialize` so `ImportStates` finds them on load
- `DeterminismBanTests` scans for banned patterns
- Twin-engine test: `EngineTwinRunDeterminismTests` advances 2 engines same seed → identical state

### CompetitorAI
- 7 parody competitors (Mowier, SalMax, Herøy, Greed Gregarious, Offshore Optimists, NordLaks Nord)
- Quarterly `AdvanceQuarter` (every 90 days) with financial updates, distress check, strategic actions
- Actions: Harvest, Acquisition, LicenseBid (legacy, now separate from auction), PriceWar, Lobbying, International
- Distress triggers: `FinancialHealth < 0.30` || `LeverageRatio > 5.0` || `CashReserves <= 0`
- AI-vs-AI acquisitions in `SimulateAIAcquisitions` (5%/quarter)

### CrisisCapitalismSystem
- Crisis types: `DISEASE_OUTBREAK`, `BANKRUPTCY`, `REGULATORY_SHUTDOWN`, `MARKET_CRASH`, `ENVIRONMENTAL_SCANDAL`
- Asset types: `LICENSE`, `FARM_SITE`, `PROCESSING_PLANT`, `EQUIPMENT`
- Discounts: 20-80% based on crisis type + severity
- Assets expire after `TimeRemainingMonths` (decremented monthly in `Update`)
- Player purchase: `PurchaseDistressedAsset` → `AcquisitionResult` with integration risk + reputation impact

### Licence Auctions (Phase 4.1)
- `LicenseAuctionSystem` owns `license_auction` RNG stream
- `ApplyForGrowthLicence` → opens auction (playerEntered=true)
- `BidOnLicenseAuction` → validates min bid, balance
- `TickLicenseAuctions` (post-tick, before balance mirror):
  1. Rival raises (one per auction per day, p=0.3+0.5×appetite, bid ≤ maxPrice)
  2. Settle closed auctions (highest solvent bidder)
  3. Rival opener if no open auction (p=0.02, eligible = not Red + no player site + no player licence)
  4. Prune settled >90 days old (keep unconsumed won_player)

### Save/Load
- `GameState` serialized via `SaveJson.Options` (System.Text.Json)
- `AdoptSavedState` clears & restores all collections including `LicenseAuctions`, `ActiveCrises`, `DistressedAssets`
- `RestoreFromLoad` imports RNG stream positions (critical for determinism)
- `ExactSaveHashTests` + `EngineTwinRunDeterminismTests` + `PlayerScriptProbe` are the determinism gates

## Adding New Features Checklist
1. **Determinism**: Use `SimRng` streams, no `DateTime.Now`/`Guid`/`Random`
2. **Calibration**: All numbers in `BalancingConfig` / `CalibrationRegistry`
3. **Tick System**: Implement `ITickSystem` + register in `_inDayPhases` or `_postTickPhases`
4. **Snapshotable**: Implement `ISnapshotable` for save/load
5. **Player-Path Test**: Add to `PlayerScriptProbe` or dedicated test showing outcome change
6. **Bridge**: Add C# partial in `GameBridge.*.cs` + GDScript proxy in `SimProxies.gd` + forwarder in `SimBridge.gd`
7. **Tests**: Unit + integration + determinism (twin engine, save round-trip)
8. **Screenshots**: `./tools/capture_screenshots.sh` must produce non-identical frames

## Common Patterns

### Engine Command
```csharp
public CommandResult MyCommand(string arg)
{
    if (State == null || FinanceManager == null) 
        return CommandResult.Fail("engine_not_ready", "Engine not initialized");
    // ... logic ...
    return CommandResult.Success(message, value: 123.0, id: "optional-id");
}
```

### Bridge Partial
```csharp
public Godot.Collections.Dictionary MyBridgeMethod(string arg)
{
    var result = new Godot.Collections.Dictionary();
    if (SimulationEngine == null) { result["success"] = false; result["message"] = "Not initialized"; return result; }
    var outcome = SimulationEngine.MyCommand(arg);
    result["success"] = outcome.Ok;
    result["message"] = outcome.Message;
    if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
    return result;
}
```

### SimProxies Proxy
```gdscript
func my_bridge_method(arg: String) -> Dictionary:
    return game_bridge.MyBridgeMethod(arg) if game_bridge else {"success": false, "message": "Not initialized"}
```

### SimBridge Forwarder
```gdscript
func my_bridge_method(arg: String) -> Dictionary: return _proxies.my_bridge_method(arg)
```

## Godot / C# Interop Notes
- Godot resolves autoload by filename: `GameBridge.cs` must exist as anchor
- Partials: `GameBridge.Core.cs`, `GameBridge.Farm.cs`, `GameBridge.Market.cs`, `GameBridge.Depth.cs`, `GameBridge.Economy.cs`, `GameBridge.Licence.cs`, `GameBridge.Content.cs`
- `InternalsVisibleTo("LakskongenSim.Tests")` in `SaveJson.cs` allows tests to call `internal` methods (`TickLicenseAuctions`, `DrainDayNotes`, etc.)
- `Nullable enable`, `ImplicitUsings`, `TreatWarningsAsErrors` in csproj

## Debugging Tips
- **Determinism failure**: Run `EngineTwinRunDeterminismTests` or `ExactSaveHashTests` — they print divergent stream positions
- **RNG divergence**: Check `SimRng.ImportStates` only imports keys already in `_streams` — streams MUST be created eagerly in `Initialize`
- **Bridge contract test fails**: `tests/test_bridge_contract.py` parses all `GameBridge*.cs` — new methods must be in partials
- **Save hash changes**: Any new serializable field in `GameState` changes hash — update golden files or accept comparative test

## Performance Targets
- 60 FPS at 1440p on M1 Pro (Metal 3.2 Forward+)
- Headless `Advance(3650)` < 2s
- `dotnet test` < 10s for 667 tests

## References
- `docs/NEXT_LEVEL_PLAN.md` — full phase roadmap
- `docs/AUTONOMOUS_TOP_TIER_RUN.md` — master plan for the top-tier commercial run (waves, research briefs, gates)
- `docs/RUN_LOG.md` — normative run protocol + status ledger (read every autonomous session)
- `docs/REALISM_CHECKLIST.md` — calibration bands
- `docs/VISION.md` — product vision
- `CLAUDE.md` (repo root) — environment, main project pointer