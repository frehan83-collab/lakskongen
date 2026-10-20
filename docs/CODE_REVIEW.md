# Lakskongen — Full Code Review

_Generated 2026-10-13 by a 51-reviewer multi-agent review (30 code shards, 9 cross-cutting reviewers, 12 follow-up probes). Critical/High findings were each checked by 3 independent verifiers, Medium by 1; 13 findings were refuted and dropped. Scope: the whole repository at HEAD 1652043 plus the uncommitted RealData work. Read-only: no source was modified. Medium/Low items were deduplicated but Low items were not individually verified._

### Summary

**Overall assessment:** The simulation core is well engineered (named deterministic RNG streams, an ordered tick pipeline, typed player commands, a green HEAD baseline of 760 tests), but the player-facing seam is badly broken: manual save/load is unreachable from the UI, many Godot panels call methods the bridge never exposes, and the economy the player actually runs (tax, feed, P&L, smolt cost) diverges from the calibrated one the tests certify. The determinism and save/load guarantees in AGENTS.md hold only in same-process, passive-run tests (F453, F1073).

**Risk level of merging as-is:** **Critical.** The uncommitted RealData work drives the immediate block, because `LakskongenSim.Tests` no longer compiles and no gate can run (F197). The committed repository drives the severity: the shipped game loses progress on every normal session (F928) and its core economy is wrong in the default career (F1128).

**Top 3 highest-priority issues**

1. **F197** `src/LakskongenSim.Tests/Data/RealDataLayerTests.cs:11` - the uncommitted RealData refactor breaks the test-project build (`dotnet build LakskongenSim.sln` fails), so none of the 688 tests, including DeterminismBanTests and ExactSaveHashTests, can run and CI cannot show any regression; HEAD itself is green (760 passed, 1 skipped).
2. **F928** `godot/scripts/ui/SaveLoadMenu.gd:67` - the menu calls snake_case names (`get_save_list`) that Godot never exposes, behind `has_method` guards, so manual save, list and resume (autosave included) silently do nothing and all progress is lost on quit; fixing it will immediately expose the persistence gaps in F1069, F409, F243 and F1071.
3. **F1128** `src/LakskongenSim/Core/SimulationEngine.cs:2083` - the monthly tax re-taxes the cumulative year-to-date base every month and never refunds, giving a measured effective rate of 39% to over 300% instead of 22%; measured on seed 42, events off, including the default 10M NOK career (year 2: 8.60M tax on 2.51M pre-tax profit).

**Stats:** 800 findings: critical 3, high 55, medium 369, low 373. By category: correctness 334, code-quality 117, testing 111, architecture 78, error-handling 59, docs 34, performance 29, observability 24, security 14. 51 reviewers; 13 findings refuted by verification (0 failed verification chains).

### Critical Issues

Three findings, each independently verified by reading the code. F928 and F1069 cause guaranteed loss of player progress or paid assets on a normal play path. F521 breaks the project's determinism-first invariant on a normal path. The verifiers split on its severity (two High, one Medium), so treat it as the lowest-severity item here.

#### [F928] Save/Load menu calls snake_case methods Godot never exposes, so manual save and load is dead — `godot/scripts/ui/SaveLoadMenu.gd:67`

**Problem:** `SaveLoadMenu.gd` guards every call with `has_method("get_save_list")`, `has_method("save_game")` and `has_method("load_game")` (lines 67-68, 124-130, 142-144). `godot/scripts/autoload/SaveLoadManager.cs` only defines PascalCase `SaveGame` (`:89`), `LoadGame` (`:183`) and `GetSaveFiles` (`:286`). Godot's generated `HasGodotClassMethod` / `InvokeGodotClassMethod` compare the `StringName` exactly, so there is no snake_case alias and all three guards are false. Renaming to `GetSaveFiles` would not help either: it returns `List<SaveFileInfo>`, which is not Variant-marshalable, so the generator skips it. A scratch build with Godot.NET.Sdk 4.7.2 and `EmitCompilerGeneratedFiles=true` confirmed the exposed method list (`SaveGame/1`, `QuickSave/0`, `AutoSave/0`, `LoadGame/1`, and so on, but no lister). The project's own bridge already uses the right convention: `SimActions.gd:345,355` call `SaveGame`/`LoadGame` in PascalCase.

No other manual path works:
- `PauseMenu` is not instanced in `Main.tscn` and has the same snake_case bug (`PauseMenu.gd:200`).
- `MainMenu` "Last Spill" emits `load_game_requested`, which has no listener.
- `SimBridge.save_game` / `load_game` (`SimBridge.gd:356-357`) are correct but have zero callers.
- `project.godot` has no quicksave input action, and C# `QuickSave()` has no GDScript caller.
- `SaveLoadMenu` has no `on_panel_shown`, so slots are never refreshed on open.

**Why it matters:** A player cannot save manually, list saves or resume any save, including the autosave. `_refresh_slots` always shows "Ingen lagrede spill funnet", and "Nytt Lagringspunkt" (`:142-147`) silently does nothing. The only working persistence is the C# `AutoSave` that runs every `day % 30 == 0` in `OnSimulationTicked` (`autosave_latest.json` exists in the user data dir), and nothing in the UI can load it. All progress is lost on quit. The failure is invisible because of the `has_method` guards. `_on_slot_action` (`:120-133`) would also emit `game_saved` / `game_loaded` and show a success toast without calling C#, so even a half-fix gives false feedback.

**Suggested fix:** Add a Variant-safe lister in C#, route the UI through the `SimBridge` facade (as `AGENTS.md` prescribes), and branch on the returned `bool` before toasting.

```csharp
// godot/scripts/autoload/SaveLoadManager.cs
public Godot.Collections.Array<Godot.Collections.Dictionary> GetSaveList()
{
    var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    foreach (var s in GetSaveFiles())
        list.Add(new Godot.Collections.Dictionary {
            ["file"] = s.FileName, ["name"] = s.SaveName, ["day"] = s.GameDay,
            ["cash"] = s.Cash, ["time"] = s.SaveTime.ToString("o") });
    return list;
}
```

```gdscript
# godot/scripts/bridge/SimActions.gd + SimBridge.gd: add get_save_list(), next to save_game/load_game
func get_save_list() -> Array:
	var m := _save_mgr()
	return m.GetSaveList() if m and m.has_method("GetSaveList") else []

# godot/scripts/ui/SaveLoadMenu.gd
saves = SimBridge.get_save_list()
...
if SimBridge.load_game(info.file):   # info.file from the dictionary above
	game_loaded.emit(info.file)      # toast SUCCESS only on true, ERROR otherwise
```

Also add an `on_panel_shown()` that calls `_refresh_slots()`, wire an entry point (PauseMenu instance, MainMenu "Last Spill", a quicksave input action), and add a regression test that asserts every method name `SaveLoadMenu` / `PauseMenu` / `MainMenu` call exists on the autoload (a contract test over `has_method` targets).

#### [F1069] RAS, offshore and post-smolt facilities are never saved, so building one then save/load deletes it — `src/LakskongenSim/Core/SimulationEngine.cs:109`

**Problem:** Two parallel stores exist for the same data. The game builds facilities into the engine's own lists (`SimulationEngine.RASFacilities` at `:109`, `OffshoreFarms` at `:110`, `PostSmoltFacilities` at `:112`): `GameBridge.Farm.cs:1562` does `SimulationEngine.RASFacilities.Add(facility)` and `:1714` does the same for `OffshoreFarms`. The save path serializes only `SimulationEngine.State`, and `State.RASFacilities` / `OffshoreFarms` / `PostSmoltFacilities` are never populated from the engine lists. `PrepareForSave` (`:3229`) and `RestoreFromLoad` (`:3607`) contain no copy in either direction. `AdoptSavedState` (`SimulationEngine.Commands.cs:~606-611`) only copies snapshot to `State.*`, and those lists are always empty. `LoadGame` then calls `Initialize`, which resets the engine lists to `new()` (`:442-443`, `:475`). `AdoptSavedState`'s doc comment and `docs/NEXT_LEVEL_PLAN.md` list this as a "documented gap" pending the `ISnapshotable` rewrite, so it is known but not defended as a design choice.

**Why it matters:** The player pays 0.5-1.8B NOK for these assets (build costs in `GameBridge.Farm.cs`) and loses them on the first reload, with no refund and no warning. A harness run confirmed it: after adding one RAS facility and one offshore farm, the live engine reports RAS=1 / offshore=1 while `State.RASFacilities`=0, and the resumed engine reports 0 / 0. The two lists also disagree within one session, since `DaySummary` and `SaveLoadManager` read `State.*` while `Dashboard` and the bridge read `engine.*`. The `SuperSmoltStocked` achievement is unreachable because it reads the empty `State.PostSmoltFacilities`. Per-facility RNG streams are dropped too. It is reachable from the F8/F9 tabs, gated only by the tutorial.

**Suggested fix:** Make `State` the single owner. Either alias the engine properties to the state lists or copy at the save/load boundary. Aliasing is simpler and removes the drift:

```csharp
// SimulationEngine.cs: replace the three auto-properties
public List<RASFacility> RASFacilities => State.RASFacilities;
public List<OffshoreFarm> OffshoreFarms => State.OffshoreFarms;
public List<PostSmoltFacility> PostSmoltFacilities => State.PostSmoltFacilities;

// and delete the `RASFacilities = new();` / `OffshoreFarms = new();` /
// `PostSmoltFacilities = new();` resets at :442, :443, :475 in favour of State.*.Clear()
```

If aliasing is too invasive, copy in `PrepareForSave` (`State.RASFacilities = new(RASFacilities)`, and likewise for the other two) and at the end of `RestoreFromLoad` (`RASFacilities = new(loadedState.RASFacilities)`). Either way, re-seed each facility's RNG stream from the captured `SimStreamPositions`. Add a player-path test that builds one of each facility through the bridge path, saves, loads, and asserts the counts, that OPEX continues, and that `SuperSmoltStocked` can unlock.

```csharp
[Fact]
public void Facilities_SurviveSaveLoad()
{
    var engine = TestEngine.WithBuiltRasAndOffshore();      // built via the GameBridge.Farm path
    engine.PrepareForSave();
    var loaded = TestEngine.RoundTripThroughJson(engine.State);
    Assert.Single(loaded.RASFacilities);
    Assert.Single(loaded.OffshoreFarms);
}
```

#### [F521] Twin-lite projection draws from the live growth RNG stream, so UI reads change the simulation — `src/LakskongenSim/Optimization/ForecastTwin.cs:57`

**Problem:** `ForecastTwin.Project` calls `growth.ApplySDEGrowthOneDay(...)` on the `GrowthModel` it is given, and `GameBridge.Farm.cs:388, 440, 1095` pass the live `engine.Growth`. That is the same model the daily tick uses: `SimulationEngine.cs:240` builds it from `Rng.Stream(SimRngStreams.Growth)`, and `InDayTickSystems.cs:159` ticks it. `ApplySDEGrowthOneDay` calls `NextDoubleLocked()` for the K spread, alpha spread, sigma spread, `u1` and `u2` on every call (`GrowthModel.cs:119, 134, 135, 153, 154`), and it does so even with `individualVariance = 0.0`. The K and alpha spreads also add random +/-7.5% / +/-5% jitter, so the "deterministic drift, sigma 0" projection is not deterministic. The class doc says it "NEVER mutates live state"; that claim is false.

**Why it matters:** `GetInterventionRanking` (no cache), `GetAttentionItems` (inbox open or refresh) and `GetForecast` (once per day while the dashboard is up) each consume 5 x cohorts x 7 draws from the saved growth stream. Two runs with the same seed and the same player commands therefore diverge depending on which panels were open. Replays and `PlayerScriptProbe`-style scripts stop being reproducible once a UI is attached, the divergence is persisted in the save, and the displayed forecast jitters between calls. Headless determinism gates never open panels, and `ForecastTwinTests` use a fresh `GrowthModel` with an empty state and never check RNG position, so nothing catches it. Impact is bounded (saves still round-trip, only the draw sequence shifts), which is why verifiers rated it High or Medium. It is listed here because it violates the `AGENTS.md` "determinism first" rule on a normal path.

**Suggested fix:** Give the twin an RNG-free step: a pure overload on `GrowthModel` that applies only the deterministic drift (no spreads, no noise), called from `ForecastTwin`. Ideally the tick path calls the same drift function so the two cannot diverge.

```csharp
// GrowthModel.cs
public double ProjectGrowthOneDay(double w, double tempC, double k, double alphaMax,
    LifeStage stage, double envMult, double bonus = 0.0)
{
    // same stageModifier / alphaT / combinedMultiplier / deterministicGrowth as
    // ApplySDEGrowthOneDay, with NO NextDoubleLocked() calls
}

// ForecastTwin.cs:57
double w = growth.ProjectGrowthOneDay(weight, tempC, SdeK, SdeAlpha, stage, envMult);
```

Regression test, using populated cohorts so the growth path actually runs:

```csharp
var before = engine.Rng.CaptureStates();
var a = ForecastTwin.Project(engine.State, engine.Growth, engine.SeaLice, 10, engine.Analytics);
var b = ForecastTwin.Project(engine.State, engine.Growth, engine.SeaLice, 10, engine.Analytics);
Assert.Equal(before, engine.Rng.CaptureStates());   // no stream advanced
Assert.Equal(a.BiomassKg, b.BiomassKg);             // repeatable
```

### High Priority

55 findings, grouped by category: Correctness (47), Architecture (2), Error Handling (1), Testing (5). Each was traced or reproduced by at least one verifier unless marked "not independently reproduced". Where verifiers corrected the original claim, the corrected facts are used below.

---

**Category: Correctness (47)**

#### [F1001] Shipped JSON research tree replaces the built-in tree with effect keys the engine never reads — `godot/data/research_nodes.json:1`

**Problem:** `SimulationEngine.LoadResearchNodes` calls `ResearchSystem.PopulateFromJson` (`ResearchSystem.cs:169-219`), which clears `_nodes` and installs the 25 JSON nodes. They use 33 distinct effect keys, but the engine reads only 7 (`SimulationEngine.Licence.cs:507-543`): `growth_rate_bonus`, `fcr_reduction`, `feed_waste_reduction`, `mortality_reduction`, `disease_resistance_bonus`, `market_price_bonus`, `sustainability_score_bonus`. The JSON uses `growth_rate`, `fcr_improvement`, `disease_resistance`, `price_premium`, `sustainability_bonus` and others. With all 25 nodes unlocked, five of the seven live keys sum to 0. `feed_waste_reduction` (0.15) and `mortality_reduction` (0.33) are produced, but `mortality_reduction` is itself dead (see F1027). The built-in tree that does use the live keys is unreachable whenever `godot/data` resolves (editor, tests, macOS bundle). `TechTreePanel.gd:450` also multiplies every effect by 100 and prints `%`, so point-valued effects render as +500% / +300%.

**Why it matters:** Research costs 0.4-5 M NOK and 20-120 days per node, yet 24 of 25 nodes have no simulation effect. Balance and "no dominant strategy" sweeps are measured against a tree that is not the shipped one. 19 built-in ids vanish, so unlocked ids in old saves are silently dropped by `ForceUnlock`.

**Suggested fix:** Make the live key set a single registry, rename the JSON keys, warn on unknown keys, and add a contract test.

```csharp
public static class ResearchEffectKeys
{
    public static readonly IReadOnlySet<string> Live = new HashSet<string>
    {
        "growth_rate_bonus", "fcr_reduction", "feed_waste_reduction", "mortality_reduction",
        "disease_resistance_bonus", "market_price_bonus", "sustainability_score_bonus",
    };
}
// PopulateFromJson / AddModNodes
foreach (var key in node.Effects.Keys)
    if (!ResearchEffectKeys.Live.Contains(key))
        log.Warn($"research node '{node.Id}': unknown effect key '{key}'");
```

Rename in the JSON: `growth_rate` to `growth_rate_bonus`, `fcr_improvement` to `fcr_reduction`, `disease_resistance` to `disease_resistance_bonus`, `price_premium` to `market_price_bonus`, `sustainability_bonus` to `sustainability_score_bonus`. Add an xUnit test that loads `godot/data/research_nodes.json` and asserts every effect key is in `Live` and every `Live` key has a consumer. In the tooltip, format by key kind (fraction as %, score as `+N`).

#### [F056] GameScene always starts a fresh default game, discarding the scenario chosen in the main menu — `godot/scripts/Main.gd:53`

**Problem:** `MainMenu._on_scenario_selected` (`MainMenu.gd:166-167`) calls `sim.start_new_game("Player", 10000000, scenario_id)` and then loads `GameScene.tscn`. That scene's root script is `Main.gd`, whose deferred `_start_game` calls `_sim.start_new_game(DEFAULT_PLAYER_NAME)` unconditionally (`_game_started` is per instance). `GameBridge.StartNewGameInternal` runs `SimulationEngine.Initialize()`, which calls `Reset()` and rebuilds `Scenario`, so the chosen scenario and its starting state are discarded. Nothing re-applies them (`GameController.auto_start = false`). Traced statically; Godot was not launched.

**Why it matters:** Every scenario, campaign and tutorial start from the menu silently becomes a default 10 M NOK game. The QA `ScreenshotHarness` boots `GameScene` directly and starts the scenario afterwards, so no existing check exercises the menu flow.

**Suggested fix:** Make `Main._start_game` skip when a game is already running, and add a scene-flow test.

```gdscript
# Main.gd
func _start_game() -> void:
	if _sim.is_game_running():   # new accessor over GameBridge state
		_game_started = true
		return
	_sim.start_new_game(DEFAULT_PLAYER_NAME)
```

Test: select a scenario in `MainMenu`, load `GameScene`, assert `get_active_scenario_id()` equals the selected id.

#### [F949] Scenario goal tracker is never initialised in the player flow, so the goal HUD is stuck at 0%; the `GetTotalDebt` stub would also auto-satisfy debt goals — `godot/scripts/ScenarioManager.gd:151`

**Problem:** Not independently reproduced. `MainMenu` starts scenarios through `SimBridge.start_new_game` to `GameBridge.StartScenario` (`SimActions.gd:28-31`). `ScenarioManager.apply_scenario`, the only setter of `current_scenario`, has zero callers, and `GameBridge.ScenarioLoaded` (`GameBridge.Depth.cs:115`) has no GDScript listener. `_recompute_goal_progress()` and `_on_bridge_scenario_won/lost` early-return on `current_scenario.is_empty()`. The baseline `06_finance.png` shows "0% — 3.0 år igjen" at day 14. Latent second bug: the `debt_and_profit` goal (3 scenarios in `godot/data/scenarios.json`, for example `po3_red_sone`) reads debt from `GetTotalDebt()`, a TODO stub returning 0.0 (`GameBridge.Economy.cs:349`), while `TakeLoan` appends to `State.ActiveLoans`.

**Why it matters:** The campaign shows no goal progress and never shows achieved or failed. Wiring the tracker naively would mark "survive 3 years" achieved on the first update, because debt reads as 0 and elapsed days are ignored.

**Suggested fix:** Connect `ScenarioManager` to `GameBridge.ScenarioLoaded` (or call `apply_scenario` from `SimActions.start_new_game`), and prefer exposing goal progress from the C# `ScenarioSystem` over re-deriving it in GDScript. Implement the loan getters from `State.ActiveLoans`, and make `debt_and_profit` require `day >= time limit` with a positive balance.

```csharp
public double GetTotalDebt() =>
    (double)SimulationEngine.State.ActiveLoans.Sum(l => l.Principal);
```

#### [F762] Alerts are level-triggered: re-emitted on every refresh, flooding NotificationCenter, repeating the lice SFX and re-pausing the game — `godot/scripts/bridge/SimState.gd:349`

**Problem:** `_refresh_state()` clears `active_alerts` and calls `_check_alerts()`, and `_push_alert` emits `alert_raised` for every condition that still holds, with no edge detection or stable id. `GameBridge.Core.cs` emits both `SimulationTicked` and `GameStateUpdated` per tick and each triggers `_refresh_state()`, so each persistent alert is emitted twice per tick. `NotificationCenter.add_entry` has no de-duplication (cap 20) and calls `show_panel()` for every critical alert. `_check_alerts` also emits `audio_alert_triggered("lice_warning")` on every refresh while lice > 0.5, and `AudioController` plays `lice_alert.wav` with no throttle. `Main.gd:75` de-dupes auto-pause by full message text, but the text embeds the changing value (`'Lus KRITISK: %.2f / fisk'`), so each new daily value re-triggers the pause. Event-driven alerts pushed by `SimBridge._on_weather_event/_on_disease_outbreak` are wiped by the next refresh's `clear()`. The SFX repeats rather than loops (the clip is 1.1 s), and the player can still resume one day at a time.

**Why it matters:** Lice above 0.5 is a normal state in salmon farming, so on a realistic run the notification panel fills with duplicates, pops open repeatedly and pushes real events out, the alert sound repeats and the game keeps auto-pausing. `ConsequenceDirector` already needed per-day de-dupe to compensate.

**Suggested fix:** Give each alert a stable key and emit only on transitions. Make `Main`'s auto-pause dedupe on key, and keep event-driven alerts in a separate list with a day TTL so refresh does not wipe them.

```gdscript
var _live_prev: Dictionary = {}
var _live: Dictionary = {}

func _raise(key: String, level: String, msg: String) -> void:
	active_alerts.append({"key": key, "level": level, "message": msg})
	if _live_prev.get(key, "") != level:      # new or escalated
		alert_raised.emit(level, msg, key)
	_live[key] = level

# end of _refresh_state():
_live_prev = _live
_live = {}
```

#### [F738] Clicking a 3D pen does nothing in GameScene: `pen_clicked` is emitted on the wrong node and the manager's handler is never connected — `godot/scripts/environment/PenCage.gd:54`

**Problem:** `PenCage._on_input_event` emits `pen_clicked` on `get_parent()`. `FishPenManager` parents every cage under `markers_parent`, a plain `Node3D` named `PenMarkers`, which has no such signal, so `has_signal` is false and the click is dropped. `FishPenManager._on_pen_input_event` (`:495`) is the only code that calls `select_pen()` and emits `pen_clicked`, and it is never connected anywhere. The only other click wiring, `VisualAdapter3D._add_pen_instance`, is dead because its spawner is a no-op.

**Why it matters:** The documented flow (click pen, `FjordVistaBridge._on_pen_clicked`, pen detail tab, fly-to-farm) cannot run. `select_pen()` is never called, so the active-pen indicator and `pen_selection_changed` (consumed by `FjordCameraHint`) are dead too. The Pens tab list still works as a workaround, which is why this is not critical.

**Suggested fix:** Connect the cage's `ClickArea` in the manager and drop the parent-emit branch.

```gdscript
# FishPenManager._add_pen_internal, after markers_parent.add_child(cage)
var area := cage.get_node_or_null("ClickArea") as Area3D
if area:
	area.input_event.connect(_on_pen_input_event.bind(pen_id))   # match handler arg order
# _on_pen_input_event: guard first
if not _pens.has(pen_id):
	return
```

Add a player-path test that emits `input_event` on a cage's `ClickArea` and asserts `pen_selection_changed` fires.

#### [F944] Analytics tab (F10) never renders data: `Engine.has_singleton("SimBridge")` is false for autoloads and the API names are PascalCase on a GDScript node — `godot/scripts/ui/AnalyticsPanel.gd:182`

**Problem:** `_refresh()` starts with `if not Engine.has_singleton("SimBridge"): return`. `SimBridge` is an autoload node (`project.godot` line 30), nothing calls `Engine.register_singleton`, and autoload nodes are not Engine singletons, so every call returns immediately (initial, 30 s timer, period buttons). Past that gate it would call `sim.GetAnalyticsData/GetAnalyticsMovingAverage/GetAnalyticsTrend`, but `SimBridge` is GDScript and only defines snake_case `get_analytics_*` (`SimBridge.gd:384-388`). The panel is a `GameUI` tab (`GameUI.gd:127`) and a direct child in `GameScene.tscn:134`.

**Why it matters:** All five charts (biomass, P/L, DO, lice, feed cost) stay blank all session on a tab advertised in the tab bar. No baseline screenshot covers this tab, so the visual gate cannot catch it. The same defect pattern exists in `DataExportPanel.gd:258,329` and `CrisisPanel.gd:24`.

**Suggested fix:** Use the autoload node and the snake_case proxies, drop the duplicate instance, and add a screenshot shot for `analytics`.

```gdscript
var sim := get_node_or_null("/root/SimBridge")
if sim == null:
	return
var series: Array = sim.get_analytics_data(key, days_param)
var avg: Array = sim.get_analytics_moving_average(key, days_param, 7)
var trend = sim.get_analytics_trend(key, mini(days_param, 14))
```

#### [F943] Finance tab loan UI is broken three ways: wrong arity, nonexistent repay/info methods, and `bool(Dictionary)` as success — `godot/scripts/ui/FinancePanel.gd:727`

**Problem:** `_on_take_loan` calls `_sim.take_loan(5_000_000.0)`, but `SimBridge.take_loan(amount, term_months, rate)` (`SimBridge.gd:483`) needs three arguments, so the call fails and the handler aborts. `has_method("get_loan_info")` (`:605`) and `has_method("repay_loan")` (`:734`) refer to methods that exist nowhere, so the loan card never updates and `BtnRepayLoan` stays at its scene default `disabled = true`. Even with correct arity, `take_loan` returns a `Dictionary`, and `bool(dict)` is true for any non-empty dictionary, so a refused loan would show the "Lån tatt opp" toast.

**Why it matters:** The only loan entry point in the UI cannot take a loan, show debt or repay. The sim side works (`SimulationEngine.TakeLoan` appends to `State.ActiveLoans`), so a player-facing economy lever is unreachable. The `has_method` guards hide the failure from the contract test.

**Suggested fix:** Pass all arguments, branch on the `success` key, and implement the missing read and repay verbs.

```gdscript
var res: Dictionary = _sim.take_loan(5_000_000.0, 60, 0.06)   # term/rate from calibration or a dialog
if bool(res.get("success", false)):
	_toast("Lån tatt opp")
else:
	_toast(str(res.get("message", "")))
```

Add `GameBridge.GetActiveLoans/GetTotalDebt/GetMonthlyPayment` (from `State.ActiveLoans`) and a `RepayLoan` verb, expose them in `SimBridge`, and add a UI-level test that presses the button against a headless engine.

#### [F618] HUD `_input` swallows `ui_accept` (Space/Enter) globally, breaking text fields and keyboard activation — `godot/scripts/ui/HUD.gd:62`

**Problem:** `HUD._input` treats every `ui_accept` press as "toggle pause" and marks it handled. `_input` runs before GUI focus handling, so a focused `LineEdit` or `Button` never receives Space/Enter. `GameController._unhandled_input` (`GameController.gd:92-95`) binds the same action to the same toggle and is dead behind the HUD. `AttentionInbox.gd:440-442` shows the project's own guard for this class of bug (skip when the focus owner is a `LineEdit`/`TextEdit`); HUD has none.

**Why it matters:** Typing a space in the Encyclopedia search field (`EncyclopediaPanel.gd:361`) toggles pause and drops the character. A keyboard user who tabs to a panel button and presses Enter or Space pauses the sim instead of activating the button. This is a keyboard-accessibility regression with a silent sim state change.

**Suggested fix:** Move the shortcut to `_unhandled_input` under a dedicated `toggle_pause` action (Space only), add the focus-owner guard, and delete the duplicate in `GameController`.

```gdscript
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_pause"):
		_toggle_pause()
		get_viewport().set_input_as_handled()
```

#### [F616] Main menu "Last Spill" (Load) and "Innstillinger" (Settings) buttons are inert — `godot/scripts/ui/MainMenu.gd:95`

**Problem:** `_on_load_pressed` and `_on_settings_pressed` only emit `load_game_requested` / `settings_requested`. `MainMenu` is `run/main_scene`, so no parent script exists, `MainMenu.tscn` has no `[connection]` entries, and a repo-wide grep finds no listener for either signal. `MainMenu.tscn` instances hidden `SettingsMenu` and `SaveLoadMenu` nodes, but `MainMenu.gd` never references or shows them. This is a regression: `logs/Menus-2026-03-13.md` records these buttons opening the sibling menus before the scene-based rewrite.

**Why it matters:** A returning player cannot load a save or change settings from the first screen. Both buttons look enabled and give no feedback. The only route to a save is to start a new game and use the in-game load tab.

**Suggested fix:** Show the embedded menus directly and restore the main column when they close.

```gdscript
@onready var settings_menu: Control = %SettingsMenu
@onready var save_load_menu: Control = %SaveLoadMenu

func _on_settings_pressed() -> void:
	center.visible = false
	settings_menu.show()

func _on_load_pressed() -> void:
	center.visible = false
	save_load_menu.show_load()
```

Re-show `center` when each menu closes (and handle ESC). Add a headless scene test that presses each button and asserts the target menu becomes visible. See also F928 in Critical Issues for the broken save list this menu calls.

#### [F617] Cancelling the scenario picker leaves New Game/Load/Settings/Quit permanently disabled — `godot/scripts/ui/MainMenu.gd:169`

**Problem:** `_on_new_game_pressed` (`:89`) calls `_disable_all()`, which sets `disabled = true` on `btn_new_game`, `btn_load`, `btn_settings` and `btn_quit`, before opening the scenario panel. `_on_scenario_close()` (X button or ESC) only frees the panel and sets `center.visible = true`. No `disabled = false` exists anywhere in `godot/`.

**Why it matters:** New Game, then ESC leaves the menu visible with every standard button greyed out, including Quit. Only the programmatic Mods button works, and the player has to kill the process.

**Suggested fix:** Do not disable the menu to open a sub-panel. If `_disable_all` stays, pair it with a re-enable.

```gdscript
func _on_scenario_close() -> void:
	if scenario_panel:
		scenario_panel.queue_free()
		scenario_panel = null
	_enable_all()          # set disabled = false on the four buttons
	center.visible = true
```

Better: call `_disable_all()` only in `_on_scenario_selected` and `_on_quit_pressed`.

#### [F620] Transparent full-row Button overlay sits above the enable CheckBox and swallows its clicks — `godot/scripts/ui/ModManagerPanel.gd:250`

**Problem:** Each mod row is a `PanelContainer` whose children all fill the rect. The last child is a flat `click_area` Button (default `mouse_filter` STOP, `z_index = 1`) covering the whole row, hit-tested above the margin/hbox that holds the `CheckBox` toggle (`:197`). A click on the checkbox reaches `click_area`, which only calls `_on_row_clicked`. The file never sets `mouse_filter`. The drag-handle label is covered the same way. Inferred from node order and filters; Godot was not run.

**Why it matters:** Enabling or disabling a mod is the panel's core action (Apply sends only id + enabled). If the checkbox cannot be clicked, mouse users cannot change any mod state, and only Tab + Space reaches it.

**Suggested fix:** Remove the overlay and make the row itself handle selection clicks; interactive children keep consuming their own events.

```gdscript
row.mouse_filter = Control.MOUSE_FILTER_STOP
row.gui_input.connect(func(ev: InputEvent) -> void:
	if ev is InputEventMouseButton and ev.pressed and ev.button_index == MOUSE_BUTTON_LEFT:
		_on_row_clicked(mod_id))
# delete the click_area Button
```

Add a UI test that toggles the CheckBox via a synthesized mouse click.

#### [F641] First ESC press permanently hides the NotificationCenter root and swallows the key — `godot/scripts/ui/NotificationCenter.gd:99`

**Problem:** `_input()` does `if visible and ui_cancel: hide(); set_input_as_handled()`. `visible` is the always-true flag of the root `Control` (neither `NotificationCenter.tscn:26` nor `GameScene.tscn:121` sets `visible = false`; only the inner Panel is hidden in `_ready`). The node comes after `GameUI` in `UILayer`, so it receives `_input` first, and the first ESC hides the whole root. Nothing calls `show()` on the root again: `show_panel()` only calls `panel.show()`, so the N key and critical auto-open act on a child of a hidden parent.

**Why it matters:** The notification history and critical-alert surface is dead for the rest of the session after the first ESC. That ESC also never reaches `GameUI`'s "ESC returns to Dashboard".

**Suggested fix:** Guard on the panel state and close through the same path as the X button. Never `hide()` the root; keep it visible with `mouse_filter = IGNORE`.

```gdscript
if _is_open and event.is_action_pressed("ui_cancel"):
	hide_panel()
	get_viewport().set_input_as_handled()
```

#### [F647] Pen list is rendered into a zero-height ScrollContainer (list invisible in the accepted baseline) — `godot/scripts/ui/PenManagement.gd:177`

**Problem:** `_refresh_pen_list()` fills `%PenList`, but in `scenes/ui/PenManagement.tscn` `LeftBody` (a `VBoxContainer` inside `LeftOuter`) has no `size_flags_vertical`, so it gets only its minimum height. Its child `ListScroll` (a `ScrollContainer`, minimum height 0) collapses to 0 px. The committed baseline `07f_pen_management.png` shows the left "Merder" card empty while `po3_nord` is selected and its details are populated.

**Why it matters:** With more than one pen the player cannot pick a pen from the list, the primary navigation of the Merder tab. The visual baseline was promoted with this state. First-pen auto-selection and 3D selection via `select_pen()` still work, so the impact stops short of critical.

**Suggested fix:** Give both containers vertical expand and `ListScroll` a floor height.

```ini
[node name="LeftBody" type="VBoxContainer" parent="..."]
size_flags_vertical = 3

[node name="ListScroll" type="ScrollContainer" parent="..."]
size_flags_vertical = 3
custom_minimum_size = Vector2(0, 120)
```

Add a harness assertion that `PenList.get_parent().size.y > 0` when pens exist, then re-promote `07f_pen_management.png`.

#### [F1004] 17 of 25 JSON research nodes are invisible and unresearchable: the UI knows 5 branches, the data and engine have 10 — `godot/scripts/ui/TechTreePanel.gd:254`

**Problem:** `TechTreePanel.gd:37` hardcodes `BRANCH_ORDER = [Biology, Technology, Sustainability, Processing, Expansion]`, and `_build_node_panel` returns early when the branch is not found (`:254-255`). The active JSON tree has Genetics 5, Environment 5, Automation 5, Nutrition 4, Health 3 and Processing 3 nodes. `PopulateFromJson` maps only Genetics to Biology (`ResearchSystem.cs:174-182`), so Nutrition, Health, Environment and Automation reach the panel under raw enum names and are skipped. Only 8 of 25 nodes are displayable, and `TechTreePanel` is the only caller of `start_research`. `research.schema.json` allows all 10 branch names, so mod nodes hit the same wall.

**Why it matters:** Two thirds of the shipped tech tree cannot be researched, prerequisite chains such as `closed-containment` are dead ends, and mod authors who follow the schema get silently hidden content. The 8 visible nodes also carry no live effect (F1001).

**Suggested fix:** Collapse to the five UI branches in `PopulateFromJson`/`AddModNodes`, or extend `BRANCH_ORDER`/`BRANCH_META` to all ten values and size the canvas from `BRANCH_ORDER.size()`.

```csharp
static ResearchBranch ToUiBranch(ResearchBranch b) => b switch
{
    ResearchBranch.Genetics or ResearchBranch.Nutrition or ResearchBranch.Health
        => ResearchBranch.Biology,
    ResearchBranch.Environment => ResearchBranch.Sustainability,
    ResearchBranch.Automation  => ResearchBranch.Technology,
    _ => b,
};
```

Add a contract test: every `node.branch.ToString()` in the bridge payload must be in `BRANCH_ORDER`.

#### [F957] Tilt-shift divides 0/0 when `blur_amount == 0` (set by FjordCamera underwater), so the top and bottom ~35% of the frame renders black — `godot/shaders/tilt_shift.gdshader:34`

**Problem:** `blur_sample()` computes `exp(-0.5 * fi * fi / (amount * amount))` with no epsilon. `FjordCamera.gd:259-260` writes `blur_amount = 0.0` whenever the camera is at `ZoomLevel.UNDERWATER` (reachable by mouse-wheel zoom). The only guard (`:62`, `blur_factor >= 0.001`) tests `blur_factor`, not the product, so for every pixel outside the focus band `amount = 0 * blur_factor = 0` and the `fi == 0` term is 0/0 = NaN, which propagates through color, saturation and contrast. The committed baseline `11_fjord_underwater.png` is solid black for y <= 360 and y >= 720 (of 1080) at x = 960, i.e. outside `0.5 +/- focus_width`.

**Why it matters:** The underwater view, a headline camera level, is unusable for roughly 70% of the screen. The defect was blessed into the baseline set, so the visual-QA diff never flags it. `docs/NEXT_LEVEL_PLAN.md` (V5) records the same NaN root cause with a `+1e-4` guard that was applied only for the focus-band case.

**Suggested fix:** Guard the divisor and the product, not just `blur_factor`.

```glsl
float w = exp(-0.5 * fi * fi / max(amount * amount, 1e-4));
// ...
if (blur_amount * blur_factor >= 0.05) {
    blurred = blur_sample(/* ... */);
}
```

Optionally hide `TiltShiftRect` underwater instead of writing `blur_amount = 0`. Add a harness assertion that no more than 30% of the frame is pure black for `11_fjord_underwater`, then re-promote the baseline.

#### [F1027] Research `mortality_reduction` is computed and then overwritten, so the research effect (and `engine.MortalityReduction`) never changes mortality — `src/LakskongenSim/Biology/MortalityModel.cs:143`

**Problem:** In `ApplyMortality` (`:143-147`) and `ApplyMortalityWithEnvironment` (`:229-233`), `totalRate` is first reduced by `mortalityReduction`, then recomputed from `baseRate * stageMultiplier + diseaseRate`, discarding the reduction. Only `diseaseResistanceBonus` survives. The parameter comes from `engine.MortalityReduction = GetResearchEffect("mortality_reduction")` (`SimulationEngine.Licence.cs:525`) via `InDayTickSystems.cs:137`, and the effect sits on at least 8 research nodes (`godot/data/research_nodes.json:44,110,132,253`, `ResearchSystem.cs:307,333,346,377`). The trailing `Math.Max(0.0, ...)` floor hides the dead write. The unit is also wrong: effects are 0.02-0.10 while the sea hazard is about 3e-4 per day.

**Why it matters:** Players spend research points on mortality reduction and get nothing. A careless one-line fix (delete the overwrite) would subtract 0.02-0.10 per day from a 3e-4 hazard and zero all mortality.

**Suggested fix:** Treat the research value as a fraction of the baseline hazard and apply it once.

```csharp
double baseHazard    = baseRate * stageMultiplier * (1.0 - Math.Clamp(mortalityReduction, 0.0, 0.9));
double diseaseHazard = diseaseRate * (1.0 - diseaseResistanceBonus);
double totalRate     = Math.Clamp(baseHazard + diseaseHazard + environmentalMortality,
                                  0.0, MaxTotalDailyMortalityRate);
```

Delete the first `totalRate` computation. Add a test: same seed with and without a 0.10 `mortality_reduction` effect must differ by about 10% of the base hazard.

#### [F1006] Balance-based "revenue" achievements fire on day 1 from starting capital and pay out up to 750 k NOK — `src/LakskongenSim/Core/AchievementSystem.cs:203`

**Problem:** `MillionRevenue` (`:203`), `FiveMillionRevenue` (`:234`), `TenMillionRevenue` (`:254`), `SpeedrunMillionaire` (`:300`) and `HundredMillionNetWorth` (`:264`) test `gs.Balance` against 2 M / 6 M / 11 M / 2 M / 105 M. The thresholds assume a ~1 M start, but the default start is 10 M and scenarios start at 0.3-60 M. Reproduced after `Initialize` + `Advance(1)`: the default game unlocks `speedrun-millionaire`, `five-million-revenue` and `million-revenue` on day 1 (balance 10,000,000 to 10,750,000). `po3_red_sone`, `isa_utbrudd`, `algekatastrofe` and `ressursrente` also unlock `ten-million-revenue`. The config text says "cumulative revenue of 1M NOK" and "Reach 1M NOK profit in under 500 days", which the code does not test.

**Why it matters:** Every new game hands out free cash (rewards are 50 k-5 M NOK each, 29.8 M in total in `achievements_config.json`) and trophies before the player acts, which inflates the economy and makes the achievement list meaningless.

**Suggested fix:** Track cumulative revenue on `GameState` (set from `FinanceManager.AddRevenue`) and test that, or subtract starting capital.

```csharp
{ "MillionRevenue",      gs => gs.TotalRevenueNok >= 1_000_000m },
{ "SpeedrunMillionaire", gs => gs.TotalRevenueNok >= 1_000_000m && gs.Day < 500 },
```

Add a test that no achievement is unlocked after `Advance(1)` in any shipped scenario.

#### [F054] Zone fish biomass is overwritten per pen instead of summed, and goes stale after a harvest — `src/LakskongenSim/Core/InDayTickSystems.cs:115`

**Problem:** `BiologyTickSystem` loops over populations (one per pen, several pens per zone) and does `zone.CurrentFishBiomassKg = biomassKg`, so the zone holds only the last pen's biomass. `HarvestSite` removes the populations (`Commands.cs:250`) without resetting the zone, so the last value persists. `ScenarioSystem.cs:345` uses `+=`, which shows the intended semantics. Reproduced with three pens in one zone (10k, 20k, 30k kg): the zone read 30,000 against 60,038 kg real; after removing all populations and running 30 days it still read 30,987 kg with 0 kg real. Reported on `po3`: 6.01x under-count in `po3_nord`.

**Why it matters:** `Zone.AdvanceDays`, the eutrophication load, `PlanktonBloom` nutrients (`PlanktonBloom.cs:156`) and `Storyteller` all read this field. Oxygen, nutrient, bloom, mortality, FCR and lice dynamics see 1/N of the real load, and a phantom load remains after harvest. Calibration bands may have been tuned against this, so the fix needs recalibration, not clamps.

**Suggested fix:** Reset zone biomass at the start of the tick and accumulate.

```csharp
// top of BiologyTickSystem.Tick: foreach zone: zone.CurrentFishBiomassKg = 0.0;
if (zone != null)
    zone.CurrentFishBiomassKg += biomassKg;
```

Add a test with two pens in one zone and one with a zone after harvest, then rerun the SimProbe bands.

#### [F1003] Research `market_price_bonus` can never raise the price: a fraction is multiplied and then gated with `> 1.0` — `src/LakskongenSim/Core/InDayTickSystems.cs:319`

**Problem:** `marketPriceMultiplier = engine.MarketPriceBonus * engine.VerticalIntegrationQualityMultiplier` is applied only if `> 1.0` (`:319-321`). `MarketPriceBonus` is a sum of fractional bonuses (0.69 with all nine built-in nodes unlocked; 0 in the shipped JSON tree) and the VI multiplier is about 1.0, so the product stays below 1.0 and `ApplyResearchPriceMultiplier` (`MarketSimulation.cs:517`, which itself floors at 1) is never called. With a zero research term the VI quality premium collapses to 0 as well.

**Why it matters:** Nine built-in nodes (`premium-products`, `organic-certification`, `sashimi-grade`, ...) advertise +4..16% price and deliver nothing. In the shipped game the JSON tree has no `market_price_bonus` entries, so what is actually lost today is the player-purchasable vertical-integration quality premium; the research bonus is dead in the fallback tree and in mods.

**Suggested fix:** Combine as multipliers and drop the gate (the callee already floors at 1.0).

```csharp
double marketPriceMultiplier =
    (1.0 + engine.MarketPriceBonus) * engine.VerticalIntegrationQualityMultiplier;
engine.MarketSimulation.ApplyResearchPriceMultiplier(marketPriceMultiplier);
```

Add a `PlayerScriptProbe`-style test: unlock `premium-products` and assert `PricePerKg` rises about 5%.

#### [F243] Active scenario, start day and AI progress are lost on save/load, so win/loss never fires again — `src/LakskongenSim/Core/ScenarioSystem.cs:602`

**Problem:** `ScenarioSystem` keeps `ActiveScenario`, `IsWon`/`IsLost`/`LossReason`, `_scenarioStartDay` and `_aiProgress` in memory only. Nothing writes them into `GameState.ActiveScenarioId/ScenarioStartDay/ScenarioIsWon/ScenarioIsLost`; the only touch is the load-side copy in `SimulationEngine.Commands.cs:514-518`. `LoadGame` calls `Initialize`, which builds a fresh `ScenarioSystem` (`SimulationEngine.cs:549`), and nothing re-activates it. Probe: `StartScenario(po3_red_sone)`, `Advance(10)`, `PrepareForSave` gave `State.ActiveScenarioId = null`; after load `Scenario.ActiveScenario = null`. `docs/NEXT_LEVEL_PLAN.md:165` lists "scenario ActiveScenario rehydration" as still open.

**Why it matters:** After loading a 730/1095-day Survival or Competition run, `CheckWinConditions` returns at its first line, so the scenario can no longer be won or lost and rival AI progress is gone. `SaveLoadManagerTests` only sets these `GameState` fields by hand, which hides the gap.

**Suggested fix:** Add export/import for the scenario state and call it from the save and load paths.

```csharp
internal ScenarioSaveState ExportState() =>
    new(ActiveScenario?.Id, _scenarioStartDay, IsWon, IsLost, LossReason, _aiProgress);

internal void ImportState(ScenarioSaveState s) { /* re-resolve scenario by id, restore fields */ }
// SyncEmpireSystemsToState: state.ActiveScenarioId = Scenario.ExportState().Id; ...
// AdoptSavedState: Scenario.ImportState(...)
```

Derive Competition AI progress from `daysElapsed` instead of per-call accumulation, and add an engine-level scenario save/load test.

#### [F244] Shipped scenario start omits the engine argument, so insurance, resource-rent flag and starting disease are never applied — `src/LakskongenSim/Core/ScenarioSystem.cs:369`

**Problem:** `ApplyStartingState` wires insurance (`RestoreInsuranceFromState`), the 25% resource-rent flag and the starting-disease seed only when `engine != null`. The game path `GameBridge.StartScenarioCore` (`GameBridge.Depth.cs:107`) calls `scenario.ApplyStartingState(SimulationEngine.State)` with no engine. Tests (`PlayerScriptProbe`, `ExactSaveHashTests`, `EngineTwinRunDeterminismTests`) pass the engine, so they exercise a different path. Probe: `isa_utbrudd` via the bridge path gave `State.InsurancePolicies = 2` but live `Insurance.Policies = 0`, and `PrepareForSave` then overwrote the state value with 0; `ressursrente` gave `State.EnableResourceRentTax = True` but `engine.EnableResourceRentTax = False`. The tax half is mostly cosmetic: nothing in the tick reads the engine flag.

**Why it matters:** The ISA, algae-bloom and Ressursrente campaign scenarios run without their defining mechanics (no cover, no starting outbreak seed), and the mirrored state is erased on the first save. The "Utbrudd" event marker still appears without a real outbreak. The tested overload is not the one players hit.

**Suggested fix:** Make the engine parameter required so no caller can skip it.

```csharp
public void ApplyStartingState(GameState state, SimulationEngine engine) { /* no null branch */ }
// GameBridge.Depth.cs:107
scenario.ApplyStartingState(SimulationEngine.State, SimulationEngine);
```

Delete the one-argument overload or mark it test-only. Add a bridge-contract test that starts each campaign scenario through the same entry point as `GameBridge` and asserts `engine.Insurance.Policies.Count` and the disease seed.

#### [F1005] Scenario "ressursrente" is won on day 1: `profit_target` (25 M) is below the 60 M starting capital — `src/LakskongenSim/Core/ScenarioSystem.cs:1160`

**Problem:** Profit scenarios are judged as `equity >= profit_target` with absolute equity (`ScenarioSystem.cs:689-693`). `ressursrente` starts with 60,000,000 NOK (`:1131`, `scenarios.json:361`) and sets `profit_target` to 25,000,000 (`:1160`, `scenarios.json:381`), while the objective text says "nå 25 MNOK i overskudd". Reproduced through the `GameBridge.StartScenarioCore` path: `IsWon = True` after `Advance(1)` with balance 61.6 M. The other profit scenarios are consistent with absolute equity (lofoten 2 M to 5 M, barekraft_mester 50 M to 100 M), so this one is a data error.

**Why it matters:** An unlocked campaign scenario shows a victory screen on the first tick, defeating the tax-regime lesson it exists for, and any "win unlocks next" logic is bypassed.

**Suggested fix:** Set the target to starting capital plus 25 M in both places, or make the check relative.

```csharp
["profit_target"] = "85000000",   // ScenarioSystem.cs:1160 and godot/data/scenarios.json:381
```

Add a data test: for every `WinCondition.Profit/Competition` scenario assert `profit_target > StartingCapitalNOK`.

#### [F1072] Scenario-start populations get a private unseeded `GrowthModel` and an unconfigured `MortalityModel`; load swaps them, so save/load changes mortality — `src/LakskongenSim/Core/ScenarioSystem.cs:324`

**Problem:** `ApplyStartingState` builds each starting pen with `new FishPopulation(new GrowthModel(), new MortalityModel())` (OS-entropy RNG, code defaults). The game calls it without the engine (F244), and even with an engine nothing injects the shared models. The SIR disease system injects rates into the engine's shared `Mortality`, never into these private instances, and `ConfigureSeaMortality` is applied only to the engine model. `RestoreFromLoad` (`SimulationEngine.cs:3628`) then re-injects the shared, configured models into every population. Reproduced with events on: live pens use a 0.03009%/day baseline with sea disease off, loaded pens use 0.02054%/day with disease rates injected. A save at day 10/30/60 diverges on the next day (11716 vs 11715 per cohort), and 300 days later the runs differ by 77 to 800 fish.

**Why it matters:** Live and loaded runs use different mortality models, and disease outbreaks have almost no mortality effect on scenario-start fish. `ExactSaveHashTests` passes only because it checks 45 days after a day-90 save, before the models diverge. The unseeded `GrowthModel` is latent nondeterminism for any SDE use on those pens.

**Suggested fix:** Create scenario populations with the engine's models and never construct defaults in production code.

```csharp
var pop = new FishPopulation();
pop.InjectDependencies(engine.Growth, engine.Mortality);   // as StockSmolts does, Commands.cs:119
```

Make the parameterless `GrowthModel()` constructor test-only. Add a twin test that saves at day 10 and 60 with events on and compares hashes at +30 days.

#### [F268] `HarvestSite` applies the certification price multiplier on top of a spot price that already contains it — `src/LakskongenSim/Core/SimulationEngine.Commands.cs:216`

**Problem:** `MarketSimulation.PricePerKg` already includes `_certificationMultiplier` (`MarketSimulation.cs:400/506/520`), fed by `OnCertificationChanged` (`SimulationEngine.cs:2282`) with the same `SustainabilityManager.GetPriceMultiplier(level)`. `HarvestSite` (`:214-218`) then multiplies the whole revenue, spot part included, by it again. The spot clamp (`LongTermMean + 6`) trims the compounding, so verified magnitudes are about +16% for ASC (not +32%) and about -23% for NonCompliant. The initial NonCompliant level fires no change event, so only the harvest 0.85 applies there.

**Why it matters:** Certified farms get roughly twice the intended premium on spot sales and non-compliant farms a double penalty, so every certification decision is mis-valued on the main income stream. No test references `GetPriceMultiplier` or `CurrentCertification`.

**Suggested fix:** Apply the multiplier to the contract portion only (if contract prices should carry a premium) and use the spot price as-is.

```csharp
decimal contractRevenue = /* sum of per-contract payments */;
revenue = contractRevenue * certMultiplier
        + (decimal)remainingKg * MarketSimulation.PricePerKg;
```

Add a test: set `ASCCertified`, harvest N kg with no contracts, assert `revenue == N * PricePerKg`.

#### [F269] `StockSmolts` bypasses calibrated `SmoltCostPerFishNOK` and charges about 54.75 NOK per smolt — `src/LakskongenSim/Core/SimulationEngine.Commands.cs:107`

**Problem:** The stocking cost is `Hatchery.CalculateRearingCost(count, MaturationDays) * strain.CostMultiplier`, i.e. 0.15 NOK x 365 d = 54.75 NOK per fish. The calibrated value (`calibration.json` `smoltCostPerFishNOK = 12`, guard-tested to 8-20 NOK) is copied into `_balancing.SmoltCostPerFish` (`SimulationEngine.cs:1213`) but nothing reads it. `Hatchery.RearingCostPerFishPerDay = 0.15m` (`Hatchery.cs:15`) is a hard-coded default that nothing assigns. The only stocking test asserts the balance goes down.

**Why it matters:** The player pays about 4.6x the calibrated smolt price on every stocking, while `SimProbe.cs:652` validates EBIT/kg using the 12 NOK value. The band test therefore certifies an economy the player path does not run, defeating the calibration single-source-of-truth rule in `AGENTS.md`. See F1132: smolt cost also never reaches the P&L, so the two errors partly hide each other.

**Suggested fix:** Price from calibration.

```csharp
decimal stockingCost = count * _balancing.SmoltCostPerFish * (decimal)strain.CostMultiplier;
```

Or initialise `Hatchery.RearingCostPerFishPerDay` from the registry so cost over 365 d equals 12. Add a `PlayerScriptProbe` assertion that 1,000 smolts cost 12,000 NOK x multiplier.

#### [F1132] Smolt purchases, site build and about 10 other cash outflows never reach the P&L, so every figure derived from `FinancialStatements` is overstated — `src/LakskongenSim/Core/SimulationEngine.Commands.cs:143`

**Problem:** Only feed (COGS) and a subset of OpEx are recorded. `StockSmolts` (`Commands.cs:143`), `BuildSeaSite` (`Licence.cs:178`), wellboat hire (`Commands.cs:450`), escape fines (`SimulationEngine.cs:1710`), licence fees (`:524`), autopilot feed spill (`InDayTickSystems.cs:214`), O2/bath consumables (`:1915`, `:1519`), contract penalties (`InDayTickSystems.cs:722`) and storm repairs (`:1803`) call `Spend`/`ForceSpend` with no `Record*` call. Biological inventory is never capitalised: `UpdateBiomassInventory/AddAsset/AddLiability` have no callers outside tests. Insurance payouts are booked as harvest revenue (`SimulationEngine.cs:3169`, default category). Measured on a 5-year default run: 25.8 M of smolts plus 6.0 M of site build have no P&L entry, so a booked pre-tax +16.96 M becomes -14.84 M, yet 35.3 M of tax was charged on the first number.

**Why it matters:** Tax, EBITDA, covenants and the IPO/empire gates all read `ComputeNetIncome()`, so they run on a P&L that omits the largest cash outflow. On a 6-site run the omitted smolts alone are 82 M against 22 M of "profit".

**Suggested fix:** Capitalise smolts as inventory and release them to COGS at harvest, or at minimum record them at stocking. Route every spend through one helper that takes a P&L category.

```csharp
FinanceManager.Spend(stockingCost);
FinancialStatements?.RecordCOGS(stockingCost, "smolts");   // minimum fix
// preferred: Pay(amount, PnlCategory.Opex, PayPolicy.AllowDebt) used by all 12 sites
```

Book insurance payouts under a separate "insurance" revenue category. Add a cash-versus-P&L reconciliation test over a 2-year career.

#### [F292] `Initialize()` keeps the old `MarketSimulation` (and its stale RNG stream) on New Game and Load — `src/LakskongenSim/Core/SimulationEngine.cs:307`

**Problem:** `Initialize` calls `Reset()` and rebuilds almost every subsystem, but uses `MarketSimulation ??= new Market.MarketSimulation(...)`, so a second `Initialize` on the same engine keeps the previous game's object. That object holds a `readonly SimRngStream _rng` from the previous `SimRng`, plus old price history, shocks, supply/demand indices and Intelligence RNG. `Reset()` also leaves `PenPriceCouncil`, `ResourceRentPaid`, `EnableResourceRentTax` and `StormEscapeEvents` untouched. `GameBridge` creates one engine and calls `Initialize` on every `StartNewGame` and `LoadGame` (`GameBridge.Core.cs:86,157,198`). Nothing assigns `engine.MarketSimulation` externally.

**Why it matters:** Game #2 starts with game #1's market state, and a fixed QA seed no longer replays. On Load, `Rng.ImportStates` repositions the new `Rng`'s Market stream, which the retained `MarketSimulation` never reads, so the price path after load diverges from the uninterrupted run. `ExactSaveHashTests` and the twin-run tests build a fresh engine each time, so CI cannot see it.

**Suggested fix:** Create the market per `Initialize` and reset the other leaked state.

```csharp
MarketSimulation = new Market.MarketSimulation(
    Rng.Stream(SimRngStreams.Market),
    Rng.Stream(SimRngStreams.Intelligence, "market_inner"));
```

Reinitialise `PenPriceCouncil`, `ResourceRentPaid`, `EnableResourceRentTax` and `StormEscapeEvents` in `Reset()`. Add a regression test: `Initialize(seed=1)`, `Advance(30)`, `Initialize(seed=2)` on the same engine must serialize identically to a fresh engine with seed 2, plus a save then `AdoptSavedState` hash comparison on a reused engine.

#### [F1071] `CompetitorAI` is only partly persisted: acquired rivals come back and AI state resets, so loaded runs diverge from quarter 3 onward — `src/LakskongenSim/Core/SimulationEngine.cs:3786`

**Problem:** The save DTO `CompetitorCompanyState` (`SyncEmpireSystemsToState`, about `:3462-3481`) stores 14 financial and size fields. `RestoreEmpireSystemsFromState` overwrites a freshly spawned roster but never removes live competitors that were absent from the save (acquired at `CompetitorAI.cs:779/871`), and it does not restore `FinancialHealth`, `QuartersSinceLastAcquisition`, `RecentActions`, `_pendingActions`, `_actionHistory`, `_currentQuarter` or `_currentYear`. Reproduced across 8 seeds x 4 save days (540-day horizon): the roster diverges in 18 of 32 cases, and in 10 of them an acquired rival returns (5 rivals before the save, 6 after). Copying those fields from the pre-save engine made all 32 runs identical.

**Why it matters:** The first divergence is at the next quarterly evaluation (days 181/271/361). It changes rival licence bids (`MaxRivalPrice` uses `ExpansionAppetite`), market supply, and double-counts licences the acquirer absorbed. `ExactSaveHashTests` ends at 180 days, so the gate misses it.

**Suggested fix:** Persist the whole `CompetitorCompany` (or a DTO of every mutable field) plus the quarter/year counters and action lists, and rebuild the roster from the save.

```csharp
CompetitorAI.ReplaceRoster(state.CompetitorCompanies.Select(CompetitorCompany.FromState));
CompetitorAI.ImportCounters(state.CompetitorQuarter, state.CompetitorYear, state.CompetitorActionHistory);
```

Add an 8-seed x 4-save-day matrix test with a 540-day horizon (see F1073).

#### [F1128] Monthly "legacy" tax re-taxes the cumulative YTD base every month and never refunds, so effective tax ranges from 39% to over 300%, not 22% — `src/LakskongenSim/Core/SimulationEngine.cs:2083`

**Problem:** `ProcessDailyFinance` charges 22% of `FinancialStatements.ComputeNetIncome()` on every `day % 30 == 0`. That figure is year-to-date and net of taxes already booked, and the P&L only resets at the 360-day annual close. Each month therefore re-taxes the same already-taxed profit (`T_k = T_k-1 + 0.22 * max(0, P_k - T_k-1)`), and when later months add cost the base shrinks but nothing is credited back. The closed form gives 96.0% (month-1 lump), 78.1% (even monthly profit) and 39.2% (month-12 lump). Measured (seed 42, events off): default career Y2 pre-tax +2.51 M vs tax 8.60 M (343%); 6-site Y2 +8.32 M vs 25.14 M (302%); an event-on year with pre-tax -0.27 M still paid 7.74 M.

**Why it matters:** `docs/ECONOMIC_SYSTEM.md` states an effective rate of 22%. Lumpy harvest economies are taxed at several times their profit, which makes profitable play unwinnable. See also F1131 and F1137.

**Suggested fix:** Make the monthly path a provisional true-up against cumulative pre-tax profit, with refunds, and let the annual close settle the remainder.

```csharp
var pretaxYtd = fs.Revenue - fs.COGS - fs.OperatingExpenses - fs.InterestExpense;  // excludes fs.Taxes
var dueYtd    = Math.Max(0m, pretaxYtd) * rate;
var delta     = dueYtd - fs.Taxes;                // negative => provisional-tax credit
if (delta > 0) PayTax(delta); else if (delta < 0) CreditTax(-delta);
```

Feed `ExecuteAnnualClose` the pre-tax income and charge only the difference to provisional tax paid. Add a `PlayerScriptProbe` career test asserting total tax is at most 22% of positive cumulative pre-tax profit.

#### [F1130] Feed (the largest cost) is waived whenever cash is below one day's feed bill: COGS is booked, cash never leaves — `src/LakskongenSim/Core/SimulationEngine.cs:376`

**Problem:** The `OnFeedCostIncurred` handler calls `Spend(cost)`; on refusal it only fires a warning event (a TODO comment says "Could trigger bankruptcy event here") and then calls `RecordCOGS(cost)` anyway. `Spend` refuses the whole amount when `Balance < amount`, including when the balance is already negative. Every other daily cost uses `if (!Spend) ForceSpend` (wages, OpEx, fines, interest, and even the feed-spill cost at `InDayTickSystems.cs:214`). Measured: default 10 M career over 5 years had 979 refusals, 45.3 M NOK of 66.8 M booked COGS never paid; the tutorial scenario waived 99.6% of its COGS.

**Why it matters:** At cash below about 50 k NOK a career-mode player gets free feed indefinitely (deliberate debt is a dominant strategy), because `ScenarioSystem` checks bankruptcy only when a scenario is active (`:648`). The P&L tax deduction still counts the unpaid feed and cash-flow reconciliation never closes.

**Suggested fix:** Treat feed like every other operating cost.

```csharp
if (!FinanceManager.Spend(cost))
{
    FinanceManager.ForceSpend(cost);
    RaiseOnce("insolvent", "Cash below zero: feed paid on credit");
}
FinancialStatements?.RecordCOGS(cost);
```

If starvation is the intended consequence, make it explicit (reduce feeding rate) instead of waiving the cost. Add a probe: with cash forced to 0, 30 days of feed must change `Balance` by `-sum(COGS)`.

#### [F1133] A Mattilsynet lice order is never closed by compliance: the player delouses, lice drop, and the engine still force-delouses at the deadline — `src/LakskongenSim/Core/SimulationEngine.cs:2584`

**Problem:** `TreatmentOrderDueDay` is cleared only inside `EnforceLiceTreatmentOrder` (`:2661/2708`). `DelouseSite` never touches it, and the census branch has no case for "count back under the limit": it issues when `ConsecutiveLiceBreaches >= 2 && TreatmentOrderDueDay < 0` and enforces when `State.Day > TreatmentOrderDueDay`. Reproduced on an `EnforcementTests` setup (seed 18): order on day 224 (due 238), player calls `DelouseSite("hydrolicer")` on day 225 and the count falls to 0.195 against a 0.5 limit, yet on day 245 a Thermolicer treatment of 27,483 fish (82,449 NOK) is forced and logged as "ordered delousing ... was ignored".

**Why it matters:** Compliant play is strictly worse than ignoring the order: the player pays for their own treatment and then for the forced one, with extra handling mortality and gill damage when lice are far below the limit. No test covers the compliance path.

**Suggested fix:** Add a compliance branch before enforcement.

```csharp
if (site.TreatmentOrderDueDay >= 0 && site.LastLiceCount <= limit)
{
    site.TreatmentOrderDueDay = -1;
    site.ConsecutiveLiceBreaches = 0;
    LogOrderSatisfied(site);
}
else if (site.TreatmentOrderDueDay >= 0 && State.Day > site.TreatmentOrderDueDay)
{
    EnforceLiceTreatmentOrder(site, temperature);
}
```

Add a `PlayerScriptProbe` case: order, then `DelouseSite`, then advance past the deadline, and assert no "Forced delousing" event.

#### [F1138] Loan principal repayments (and bond interest) are booked as operating expense; `RecordInterest` has no callers, so interest expense is permanently 0 — `src/LakskongenSim/Core/SimulationEngine.cs:2059`

**Problem:** Not independently reproduced. The amortisation loop records the whole daily slice (principal plus interest) with `RecordOperatingExpense`, and the bond-interest branch (`:2037`) does the same. `FinancialStatements.RecordInterest` (`:107`) is never called. Reported for a 4 M / 36-month loan: principal of 1.337 M / 1.333 M / 1.330 M per year is deducted as tax-deductible OpEx and reduces EBITDA, while interest of 231 k / 138 k / 46 k is also OpEx, so `statements.InterestExpense` is 0 in all 72 covenant tests and interest coverage can never fail. The bond branch is dormant (`IssueBond` is reachable only from `InvestmentManager`, which nothing instantiates); the loan branch is live via `GameBridge.TakeLoan`.

**Why it matters:** Interest-coverage covenants always pass, EBITDA is understated (day 720 quarterly: 7.13 M coded vs 8.85 M corrected), and principal repayment shelters tax as if it were a cost.

**Suggested fix:** Split the daily slice into interest and principal.

```csharp
var principal = dailySlice - dailyInterest;
FinancialStatements?.RecordInterest(dailyInterest);
FinancialStatements?.RepayLiability(principal);
loan.Principal -= principal;
```

Call `AddLiability` in `TakeLoan`, and use interest and pre-financing EBITDA in the covenants.

#### [F1014] Tutorial chain can stall: "first sale" requires balance above 10 M NOK and step 6 only triggers at lice >= 0.8 — `src/LakskongenSim/Core/TutorialSystem.cs:205`

**Problem:** Not independently reproduced. `CheckProgress` is strictly sequential (`if (!triggered) break;`). Step 9 (`first-sale`, `tutorial_steps.json:91`) completes on `first_sale_completed`, implemented as `ProcessingSnapshots.Count >= 1 && LiveBalance > 10_000_000m` (`:205`). The tutorial scenario `forste_sesong` starts at 300,000 NOK with smolts at 12 NOK, so at most about 25,000 smolts: roughly 21,250 survivors x 5.2 kg x 70 NOK/kg is about 7.7 M gross before feed, below the threshold after the first cycle. Step 6's trigger (`lice_detection_acknowledged`) is `SeaLicePerFish >= 0.8` (`:198`), which a player who delouses early never reaches.

**Why it matters:** Progress depends on an arbitrary balance, not on the action named in the JSON, so the tutorial can hang indefinitely. Later steps cannot advance, the Tutorial win condition is blocked, and about 1.06 M NOK of step rewards are unreachable.

**Suggested fix:** Key the trigger on the action and give step 6 a time fallback.

```csharp
"first_sale_completed" => t => t.State.ProcessingSnapshots.Count >= 1 && t.State.TotalHarvestRevenue > 0,
"lice_detection_acknowledged" => t => t.State.SeaLicePerFish >= 0.8 || t.State.Day >= 150,
```

Add a `PlayerScriptProbe` test that plays the tutorial scenario through step 18 with a minimal strategy.

#### [F1024] Dissolved-oxygen update is numerically unstable (gain about -9); the [0,14] clamp turns it into a daily 0/14 mg/L square wave that feeds growth, FCR and appetite — `src/LakskongenSim/Environment/WaterChemistry.cs:105`

**Problem:** The oxygen term is `(8.0 - O2) * waterFlowRate * 0.01` with `waterFlowRate` in m3/day (500 + speed x 3000, about 650-2000), so the explicit-Euler exchange factor is 6.5-20 per day instead of a fraction. Any factor above 2 diverges, with multiplier about (1-k) = -9. Line 118 clamps to [0,14], which hides the divergence: replicating `UpdateOneDay` in Python gave raw pre-clamp O2 of 8.05, 7.67, 10.88, -16.4, then +76.05 / -42.95 forever, and the stored value alternates between the clamps from day 5. In a full-engine run it alternated 12.5 / 0.00 from day 3. The same `flow * 0.01` dilution is applied to NH4/NO2/NO3/PO4 (`:90-99`). Downstream, `InDayTickSystems.cs:185-187` turns it into a `waterPenalty` alternating between 0.2 and 1.0, scaling growth, FCR (`FeedSystem.cs:223`, `fcr *= 2.0 - waterQualityPenalty`) and appetite. The sibling `WaterQuality.UpdateDailyDO` (`:181-193`) repeats the defect but has no production caller.

**Why it matters:** The clamp is the only reason the sim does not overflow, and it makes every zone's oxygen, ammonia and pH meaningless day to day while all tests pass. Players get alternating hypoxia penalties regardless of stocking, and balance tuning is fitted to a broken signal. The tests (`SDEGrowthModelTests.cs:545-547`) assert only `0 <= O2 <= 14`, which the clamp guarantees.

**Suggested fix:** Make the exchange a bounded fraction of the mixed volume and drive O2 toward temperature-dependent saturation, then re-tune reaeration.

```csharp
double exchange = Math.Clamp(waterFlowRate / Math.Max(1.0, waterVolumeM3), 0.0, 1.0); // 950/500000 = 0.0019/day
OxygenO2 += OxygenReaerationRate - oxygenConsumptionFish - oxygenConsumptionNitrification
          + (WaterQuality.CalculateOxygenSaturation(_temperature) - OxygenO2) * exchange;
```

Apply the same `exchange` to NH4/NO2/NO3/PO4. Add tests that run 400 days and assert the raw pre-clamp value stays in [0,14] and |dO2| per day stays below about 1 mg/L. Delete or rewrite `WaterQuality.UpdateDailyDO` and its vacuous tests.

#### [F456] Storm-season multiplier is applied without renormalizing, so Severe weather becomes unreachable in storm season (Oct-Mar) — `src/LakskongenSim/Environment/WeatherSystem.cs:174`

**Problem:** In `UpdateWeatherState` (`:169-183`), columns 2 and 3 of the transition row are multiplied by 1.5 in storm season, but the cumulative roll is never normalized (the "Normalize" comment at `:185` has no code behind it). Row sums become 1.05, 1.125, 1.30 and 1.375, so a roll in [0,1) is exhausted by the Storm column before Severe is reached. Only Severe to Severe survives, at 0.15 instead of 0.35. Iterating the effective chain gives a stationary Severe share of 9.3% off-season and exactly 0% in storm season. `StormRiskNext3Days` (`:228-241`) does normalize rows, so the displayed forecast disagrees with the sampled process.

**Why it matters:** Severe weather (`CanOperate` false, operations suspended, highest hazard) occurs only in the calm half of the year, the opposite of the intent. Storm+Severe share is also lower in winter (31% vs 34%), and expected wear is about 44% lower in winter.

**Suggested fix:** Normalize once and share the effective row between sampling and forecasting.

```csharp
double rowSum = 0.0;
for (int j = 0; j < 4; j++) rowSum += EffectiveProb(row, j, isStormSeason);
double roll = _rng.NextDouble() * rowSum;
```

Put `EffectiveProb` in one private static helper used by `StormRiskNext3Days` too. Add a test that samples 100k days per season and asserts the Severe frequency is higher in storm season. Expect seeded weather sequences and calibration baselines to shift.

#### [F453] `Zone.UpdateCurrents` uses `HashCode.Combine(day, Id)`, which is randomized per process, so the sim is not reproducible across launches — `src/LakskongenSim/Environment/Zone.cs:136`

**Problem:** The per-day weather noise on current speed comes from `HashCode.Combine(day, Id)`. .NET seeds `HashCode` and `string.GetHashCode` with a random per-process value, so the same (day, zone id) gives different `CurrentSpeed`, `WaterFlowRate` and downstream chemistry, nutrient and bloom values in each process. Verified by running the formula for `"zone-1"` in three processes (day-1 noise 0.8048, 1.0696, 0.9708). The call is on the live tick path (`EnvironmentModel.AdvanceDays`, `Zone.AdvanceDays`, `UpdateCurrents`), and `DeterminismAllowlist.txt` only grandfathers it ("fix entries by threading the engine seed through").

**Why it matters:** `AGENTS.md` promises exact save/load and seeded replay. A save made in process A and loaded in process B (quit and relaunch) diverges from the uninterrupted run, and two same-seed runs launched separately differ. `ExactSaveHashTests` and the twin-run tests share one process and so one hash seed, which gives false assurance.

**Suggested fix:** Use a stable pure hash, or draw from a persisted `SimRng` stream.

```csharp
private static double DayNoise01(int day, string id)
{
    ulong h = 14695981039346656037UL;
    foreach (char c in id) { h ^= c; h *= 1099511628211UL; }
    h ^= (uint)day; h *= 0x9E3779B97F4A7C15UL; h ^= h >> 29;
    return (h % 1000UL) / 1000.0;
}
```

Remove the `Zone.cs` line from `DeterminismAllowlist.txt`. Add a pinned-constant test (for example `new Zone("zone-1").UpdateCurrents(100)` must give an exact `CurrentSpeed`) plus a two-process hash test (see F1073).

#### [F499] CrisisCapitalism is never triggered: `TriggerCrisis` has zero callers, `OnCompetitorDistressed` zero subscribers, and the only test is a tautology — `src/LakskongenSim/Events/CrisisCapitalismSystem.cs:252`

**Problem:** A grep over `src/`, `godot/` and tests finds `TriggerCrisis(` only at its declaration, and `CompetitorAI.OnCompetitorDistressed` (declared `:202`, raised `:532`) has no `+=` subscriber. `AGENTS.md:74` lists the Phase 4.2 distress wiring as COMPLETE, and `docs/NEXT_LEVEL_PLAN.md:212` flags "never triggered" as a defect. The distressed-asset pipeline (`GenerateDistressedAssets`, `PurchaseDistressedAsset`, `SimulationEngine.AcquireDistressedAsset`, `GameBridge.GetDistressedAssets`) therefore always sees empty lists. The sole engine test (`EmpireSystemsIntegrationTests.cs:103`) asserts `activeCrises.Count() >= 0`. No Godot UI script calls `get_distressed_assets`, so the "UI behind it" is bridge/proxy only; other acquisition paths still work.

**Why it matters:** A feature documented as complete is unreachable in play and its green test gives false assurance. Latent bugs in the same file (asset expiry cadence, minted assets, incomplete save/load) have never been exercised and will surface together when it is wired up.

**Suggested fix:** Subscribe in `SimulationEngine.Initialize` and map the distress cause to a `CrisisType`.

```csharp
CompetitorAI.OnCompetitorDistressed += (c, day) =>
    CrisisCapitalism.TriggerCrisis(c.Id, c.Name, CrisisType.BANKRUPTCY, day, CompetitorAI.Competitors);
```

Replace the `>= 0` assertions with `Assert.NotEmpty(...)` after forcing a distressed competitor. Add tests for distress to crisis plus 1-5 assets, same-seed determinism, a save round trip, and purchase transferring exactly the advertised site and licences. Correct the `AGENTS.md` Phase 4.2 claim until this lands.

#### [F382] Transfer into an empty site creates a `FishPopulation` with no Growth/Mortality injected; the next tick throws — `src/LakskongenSim/Farm/CohortTransferSystem.cs:159`

**Problem:** When the destination site has no pen, `ExecuteTransfer` does `new FishPopulation { ZoneId = ... }`, stocks it and adds it to `state.Populations` without `InjectDependencies(Growth, Mortality)`. `_growth`/`_mortality` are declared `null!`, and `InjectDependencies` is called only from the load path (`SimulationEngine.cs:3628`) and `StockSmolts` (`Commands.cs:119`). `BuildSite` creates sites with no pens (`Licence.cs:170`), `GetValidDestinations` lists them, and `OffshorePanel.gd` offers them. On the next day `BiologyTickSystem` calls `population.AdvanceDays(...)`, which dereferences `_mortality` (`FishPopulation.cs:147`).

**Why it matters:** Transferring to a newly built empty site in the same zone is a normal player path. `Advance()` throws before `State.Day++`, and `GameBridge.AdvanceDays`/`OnSimulationTick` only log, so time silently freezes with the fish already moved and the cost already charged. A save and reload repairs it (`RestoreFromLoad` re-injects dependencies), so the save is not permanently soft-locked, but progress since the last save is lost. There is no test coverage.

**Suggested fix:** Pass the models into `ExecuteTransfer` (it currently takes a `Random`) and inject them.

```csharp
public static TransferResult ExecuteTransfer(TransferPlan plan, GameState state, FinanceManager finance,
    Random rng, GrowthModel growth, MortalityModel mortality)
// ...
destPop = new FishPopulation { ZoneId = destSite.ZoneId, SeaLiceLoad = srcPop.SeaLiceLoad };
destPop.InjectDependencies(growth, mortality);
destSite.Stock(destPop);
state.Populations.Add(destPop);
```

Better, move population creation into an engine command that already owns the models, as `StockSite` does. Add a test that transfers into an empty site and ticks 2 days.

#### [F386] Cohort rebuild/merge paths silently drop `SmoltQualityTier` (and Genetics on merge), so premium cohorts revert to Standard growth — `src/LakskongenSim/Farm/DelousingSystem.cs:218`

**Problem:** `Treat()` applies deaths by replacing each cohort with `new Cohort(AgeDays, Count - take, MeanWeightKg, Stage, Genetics)`. The constructor has no Quality parameter, so `Quality` resets to Standard on every cohort touched while deaths remain, even when `take` is 0. `CohortTransferSystem` does the same when creating the destination cohort (`:180`), its merge predicate ignores `Quality`, and its genetics merge is an empty `if` ("Simple average for now") that discards the source genetics. Nothing re-stamps the tier afterwards.

**Why it matters:** `BiologyTickSystem` multiplies growth by `SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier` (`InDayTickSystems.cs:171`; SuperSmolt 1.13). After the first delousing that kills a fish, a bought SuperSmolt or Large cohort grows like Standard for the rest of the cycle. Merged transfers also mix tiers and lose breeding values.

**Suggested fix:** Mutate in place instead of replacing, and carry the tier through transfers.

```csharp
cohort.Count -= take;     // Cohort is mutable; the bloom code already does this
// CohortTransferSystem: copy Quality on create, and match on it when merging
var match = dest.Cohorts.FirstOrDefault(c => c.Stage == src.Stage && c.Quality == src.Quality);
```

Implement a count-weighted genome merge or refuse to merge when both genomes are present. Add a test that a SuperSmolt cohort keeps its tier after `Treat()` and after `ExecuteTransfer`.

#### [F383] RAS / Offshore / PostSmolt facilities can never become operational: `StartConstruction` has no caller — `src/LakskongenSim/Farm/RASFacility.cs:216`

**Problem:** `IsOperational` is `ConstructionProgress >= 1.0`, and `TickDay` returns early unless `ConstructionStartDay >= 0`. `StartConstruction` exists on `RASFacility` (`:216`), `OffshoreFarm` (`:204`) and `PostSmoltFacility` (`:190`), but nothing in `src/`, `godot/` or tests calls it. `GameBridge.BuildRASFacility` (`GameBridge.Farm.cs:1548-1563`) and `BuildOffshoreFarm` (`:1706-1715`) add the object to the engine lists and `Spend` 0.8-1.0 B NOK without starting construction. Nothing adds fish to `RASFacility.Tanks` or `OffshoreFarm.Pens`, `PostSmoltFacility` has no build verb, and bridge CAPEX disagrees with the class (Medium: 1.0 B in the bridge, 1.2 B in `RASFacility.GetCAPEX`). The carbon ledger skips non-operational facilities.

**Why it matters:** About 750 lines of facility model are unreachable in production, the `SuperSmoltStocked` achievement is unobtainable, and the build buttons take cash for a facility that never produces or charges OPEX. In practice the harm is bounded because costs (0.5-1.8 B) exceed any starting cash (at most 60 M), so the buttons mostly fail on insufficient funds.

**Suggested fix:** Start construction in the bridge and take the price from the class.

```csharp
var facility = new RASFacility(/* ... */);
engine.RASFacilities.Add(facility);
facility.StartConstruction(SimulationEngine.State.Day);
FinanceManager.Spend(facility.CAPEX);     // drop the duplicated switch in the bridge
```

Add a real stocking verb that creates populations with injected dependencies and registers them in `State.Populations`. Add a `PlayerScriptProbe`-style test: build, advance `ConstructionDays`, assert OPEX flows and `IsOperational`.

#### [F1129] MAB enforcement is dead on every realistic path: `CurrentBiomassTonnes`/`IsOverMAB` read a stocking-time density cache that the engine tick never refreshes — `src/LakskongenSim/Farm/SeaSite.cs:20`

**Problem:** `CurrentBiomassTonnes` is derived from `CurrentStockingDensity`, written only by the private `UpdateStockingDensity()` (`:175`) via `Stock()` and `SeaSite.AdvanceDays()`. The engine ages fish with `population.AdvanceDays` directly (`InDayTickSystems.cs:137`) and never calls `SeaSite.AdvanceDays`, so the cache stays at smolt weight. `StockSmolts` (`Commands.cs:96-143`) has no MAB gate. Measured: 500k smolts on a 780 t site reached 1,198 t real biomass (154% of MAB) while `site.CurrentBiomassTonnes` stayed at 50.0 and `IsOverMAB` was false at every sample over 480 days. `IssueMattilsynetOverMabOrders` (`SimulationEngine.cs:2801`) requires `IsOverMAB`.

**Why it matters:** The regulatory MAB order and forced-harvest feature cannot fire on a normal run, and Advisor density warnings (`IntelligentAdvisor.cs:401`) are inert. `MattilsynetOrderTests.cs:85` masks this by calling `site.AdvanceDays(0)` and invoking `ProcessMattilsynetOverMabOrders()` directly, so the tests pass while the shipped tick path never fires.

**Suggested fix:** Remove the cache and compute from live pens.

```csharp
[JsonIgnore] public double CurrentBiomassTonnes => Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
```

Derive `CurrentStockingDensity` the same way. Reject or clip `StockSmolts` above MAB the way `CohortTransferSystem.cs:112` already does with live biomass. Rewrite `MattilsynetOrderTests` to drive `engine.Advance` and drop the manual `AdvanceDays(0)`.

#### [F1131] Resource-rent tax can never be collected: three stacked defects (1000x unit error, standing-biomass volume, double deduction) — `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:107`

**Problem:** `harvestValue = totalBiomassKg / 1000m * normPricePerKg` is tonnes x NOK/kg, 1000x too small. The caller (`InDayTickSystems.cs:604-605`) passes `engine.GetTotalBiomassKg()`, the standing biomass at the year-end tick, not the volume harvested in the year. The base then applies a 65% standard deduction and also subtracts actual COGS plus 50% of OpEx (double deduction), floored at 0. Measured over 20+ annual closes in 6 runs: base 0 and rent tax 0 every time (D1 Y3: `harvestValue` = 19,538 NOK for 222,027 kg standing x 88). Fixing only the `/1000` does not help: 35% x 19.5 M = 6.8 M is below COGS 15.9 M plus half of OpEx. `EnableResourceRentTax` and `ResourceRentPaid` are never read to charge tax, and `ResourceRentTax.Compute` (25%, 70 M deduction) is called only from tests.

**Why it matters:** The "dominant late-game tax" described in `BALANCE.md` and enabled by the Ressursrente scenario flag does not exist at runtime. A harvest-based base would have collected 0.2 M / 1.9 M / 2.0 M in D1 Y2-Y4. The soak test asserts only `ResourceRentPaid >= 0`, which gives false assurance.

**Suggested fix:** Track harvested kg per tax year and compute the base from that, with no extra 65% deduction.

```csharp
var rentBase = Math.Max(0m, harvestedKgYtd * normPricePerKg - deductibleCosts);
var rentTax  = rentBase * ResourceRentRate;   // gate on EnableResourceRentTax
```

Increment `harvestedKgYtd` in `HarvestSite` and the forced-harvest path, and reset it at the annual close. Add a test: 100 t harvested at 90 NOK/kg with 5 M costs must yield a positive rent charge.

#### [F1137] Loss carry-forward: offset uses 80% of the loss stock rather than 80% of income, utilisation is capped by post-offset income, and it is ignored by the monthly path and not persisted — `src/LakskongenSim/Finance/TaxSystem.cs:57`

**Problem:** Not independently reproduced. Line 57 offsets `_lossCarryForward * 0.80`, a share of the stock, instead of a share of income. Line 72 consumes `min(stock * 0.8, taxableIncome)` where `taxableIncome` is already post-offset, so most of the loss survives. Lines 66-69 add `Math.Abs(netIncome)` where `netIncome` is after monthly tax, so a profitable but over-taxed year grows the carry-forward. The monthly path never reads it. `TaxSystem.ExportState/ImportState` (`:88/91`) have no callers, `SimulationEngine.cs:3596` calls the tax system "stateless", and `AdvanceQuarter` (`:36`) is never called. Unit check after a -1000 loss and five +1000 years: code tax 44.0 / 79.2 / 142.6 / 204.5 / 216.9 against 44 / 176 / 220 / 220 / 220 under the 80%-of-income rule. In engine runs the carry-forward only ever increases (default 12.7 M to 18.8 M to 20.1 M).

**Why it matters:** Year-1 start-up losses never shelter later profit in practice, and loading a save changes future tax because the carry-forward and period counter reset.

**Suggested fix:** Offset against income, accrue losses only from pre-tax negative income, and persist the state.

```csharp
var offset  = Math.Min(_lossCarryForward, netIncome * LossCarryForwardLimit);
var taxable = netIncome - offset;
_lossCarryForward -= offset;
```

Call `TaxSystem.ExportState/ImportState` from `PrepareForSave`/`AdoptSavedState`, and wire `AdvanceQuarter` or remove the period counter. See also F1128 for the monthly path.

#### [F478] Per-population disease prevalence is not snapshotted, is keyed by object reference, and is never pruned — `src/LakskongenSim/Health/DiseaseSystem.cs:38`

**Problem:** `DiseaseSystem` keeps live SIR state in `Dictionary<FishPopulation, double> _infected` (reference identity). Nothing exports or imports it: `AdoptSavedState`/`RestoreFromLoad` restore mortality rates and RNG streams but not this dictionary, and the legacy company-wide `SeaLiceModel` that `DiseaseSystem` reads for `liceEffect` is not persisted either. After a load every population is a new object, so `TryGetValue` misses and prevalence restarts at `InitialPrevalence`. Reproduced (events on, seed 7): after `DelouseSite("po3_nord","thermolicer")` at day 90, prevalence in the treated pens drops to 0.196 and evolves 0.218, 0.241, 0.730 over the next 30 days, but after a save and load every population jumps back to 0.75 immediately and the serialized state differs after 30 days. `docs/NEXT_LEVEL_PLAN.md:216` lists this as an open gap.

**Why it matters:** Save then load then continue is not bit-exact after any treatment or lice change, which breaks the project's headline guarantee. Reloading resets prevalence up to equilibrium, so it penalises the player rather than offering a save-scum lever. `ExactSaveHashTests` passes only because it never treats. The unpruned strong references to harvested populations are a minor leak.

**Suggested fix:** Key by `pop.Id` and persist the dictionary next to `MortalityState`.

```csharp
private readonly Dictionary<string, double> _infected = new();
public Dictionary<string, double> ExportState() => new(_infected);
public void ImportState(Dictionary<string, double> s) { _infected.Clear(); foreach (var kv in s) _infected[kv.Key] = kv.Value; }
public void Forget(string popId) => _infected.Remove(popId);   // call from the harvest/remove paths
```

Persist the company lice model, or stop reading it. Add a test that treats, saves, loads, advances 30 days and compares serialized state with the uninterrupted run.

#### [F480] Treatment efficacy is capped by the model's base table: Laser 0.90 becomes 0.50, Thermolicer 0.95 becomes 0.82 — `src/LakskongenSim/Health/SeaLiceModel.cs:249`

**Problem:** `DelousingSystem.Treat` (`:185-186`) normalises profile efficacy by `GetBaseEfficacy(treatmentType)` and clamps the dose to 1.0. The model's table knows only pyrethroid, organophosphate, h2o2 and mechanical/freshwater/thermal; `laser` and `cleaner_fish` fall to `_ => 0.5`, so the maximum kill is 0.5 (Laser: dose 0.90 / 0.5 = 1.8, clamped to 1.0). Thermolicer (0.95) and Hydrolicer (0.85) map to base 0.82 and are capped there, and H2O2 (0.70) at 0.65. The same string tables are duplicated across `GetBaseEfficacy`, `ApplyTreatment` and the chalimus-factor switch. The treatment cards (`GameBridge.GetTreatmentProfiles`) and the `DelouseSite` event log show the profile efficacy, not the effective kill. LiceSkirts ("preventive") is not distorted.

**Why it matters:** The "premium" Laser method (8 NOK/fish) removes the same 50% of lice as cleaner fish, and Thermolicer, also the Mattilsynet forced-treatment method, loses about 13 points. Method choice, cost-benefit and dominant-strategy sweeps run on wrong numbers, and no test checks per-method kill.

**Suggested fix:** Make the contract explicit with an absolute pre-resistance kill, and replace the three string switches with one table.

```csharp
// SeaLiceModel
public double ApplyTreatment(string type, double efficacy) { /* kill = efficacy * (1 - resistance[type]) */ }
// DelousingSystem.cs:186
efficacyResult = liceModel.ApplyTreatment(treatmentType, actualEfficacy);
```

Back it with a `TreatmentSpec` table (base efficacy, resistance slot, chalimus factor) that includes laser, cleaner_fish and preventive. Add a parametrised test that kill is approximately `profile.Efficacy * (1 - resistance)` for every `Data.DelousingMethod`.

#### [F409] Signed contracts, completed contracts and the contract week counter are never saved — `src/LakskongenSim/Market/ContractSystem.cs:68`

**Problem:** `ContractSystem` has no `ExportState/ImportState`. `ActiveContracts`, `_completedContracts` and `_currentWeek` are not part of `SaveJson`/`GameState` (only the unrelated bool `ContractActive` is), while the `ContractExpiryTickSystem` clock is saved. `LoadGame` calls `Initialize`, which builds a fresh `ContractSystem` with empty lists and `_currentWeek = 0` (`SimulationEngine.cs:310`). `ActiveContracts` is a public mutable `List<Contract>`, ids come from `Guid.NewGuid()`, and `_completedContracts` grows without bound. `docs/NEXT_LEVEL_PLAN.md:165` lists contract persistence as still open.

**Why it matters:** Every save/load silently cancels the player's contracts, including the locked price and the expiry penalty, which also makes reload a way to dodge penalties. There is no guard or test, and any career probe that saves mid-run is affected.

**Suggested fix:** Add state export and import wired into the runtime save block, like `MarketSimulation`.

```csharp
internal ContractSaveState ExportState() =>
    new(ActiveContracts.ToList(), _completedContracts.TakeLast(50).ToList(), _currentWeek);
internal void ImportState(ContractSaveState s) { /* replace lists, restore _currentWeek */ }
```

Replace `Guid.NewGuid()` with a counter or an id drawn from `_rng` so persisted ids and hashes are stable, and make `ActiveContracts` an `IReadOnlyList` with mutators on the system. Add a round-trip test next to `DepthSystemsSaveRoundTripTests`.

#### [F457] Escape score compares a lifetime fish count against incident thresholds, so one escape zeroes the category for the rest of the game — `src/LakskongenSim/Sustainability/SustainabilityManager.cs:243`

**Problem:** `SeaSite.RecordEscape(fishCount)` adds the number of escaped fish to `EscapesThisYear` (`SeaSite.cs:152-154`), and `ResetAnnualCounters` (`:159`) has no callers in `src`, `godot` or tests, so the counter is cumulative. `CalculateEscapeScore` adds it into `totalEscapes` and applies thresholds written for event counts (0 gives 20 points, 1-10 gives 10, more than 10 gives 0). The escape event record sets no Id, so the "escape" Id filter matches nothing and the score rests entirely on this counter. `docs/SUSTAINABILITY_SYSTEM.md` says "per year".

**Why it matters:** A single storm escape of 11 or more fish (typical storm escapes are hundreds to thousands) pins `EscapePrevention` at 0/20, or 5/20 with the closed-containment bonus, permanently and across saves. That caps the best score at 80, so Organic (85, +15% price) is unreachable and the ASC (70) tier is at risk.

**Suggested fix:** Count incidents, not fish, in a rolling 365-day window.

```csharp
// SeaSite
public int EscapeIncidentsThisYear { get; private set; }
public void RecordEscape(int fishCount) { EscapesThisYear += fishCount; EscapeIncidentsThisYear++; }
public void ResetAnnualCounters() { EscapesThisYear = 0; EscapeIncidentsThisYear = 0; }
// SustainabilityManager: totalEscapes += site.EscapeIncidentsThisYear;
```

Call `ResetAnnualCounters` from the year-rollover tick, and optionally add a fish-count severity term. Add a test that an escape of 5,000 fish at day 100 no longer zeroes the score at day 500.

---

**Category: Architecture (2)**

#### [F177] Tracked `project.godot` depends on a git-ignored addon; a clean checkout fails the autoload audit — `godot/project.godot:40`

**Problem:** `project.godot` registers the autoload `MCPRuntimeServer="*res://addons/godot_mcp_toolkit/runtime/mcp_runtime_server.gd"` (`:40`) and enables the editor plugin `godot_mcp_toolkit` (`:54`). `.gitignore:35` excludes `godot_mcp_toolkit/`, and nothing under it is tracked (`godot/addons/` in HEAD holds only an empty `gd-retroterrain` gitlink). CI step 12 (`autoload_audit.py`) adds an error for every autoload whose script file is missing and succeeds only when `errors` is empty, so `tools/ci.sh` aborts on a fresh checkout. `docs/NEXT_LEVEL_PLAN.md:233` lists the dangling autoload as a known defect.

**Why it matters:** A fresh clone cannot pass the project's own quality gate, and contributors get editor errors on open. Impact is limited to CI and contributor setup: the addon's `_ready` self-disables outside the editor (`OS.has_feature("editor")`), so an export only logs a "Can't autoload" error. The verifiers disagreed on severity (two High, one Medium).

**Suggested fix:** Pick one. Vendor the addon (it ships `LICENSE` and `ATTRIBUTIONS.md`) and drop the ignore rule, or remove the entries from the tracked file.

```diff
# .gitignore:35
-godot_mcp_toolkit/
# or, project.godot: delete line 40 (MCPRuntimeServer) and line 54 (enabled plugin)
```

Add a clean-checkout CI job that runs `git clone` then `tools/ci.sh` so this cannot regress.

#### [F945] `SimBridge` facade silently drops 10 `SimProxies` methods and 1 `SimActions` method; `has_method`-guarded live UI becomes inert — `godot/scripts/SimBridge.gd:345`

**Problem:** The facade claims to forward every method (`SimProxies.gd:5`) but never forwards `get_sludge_status`, `toggle_sludge_hub`, `valorize_sludge_now`, `plan_cohort_transfer`, `execute_cohort_transfer`, `get_transfer_destinations`, `get_carbon_ledger`, `get_carbon_intensity`, `is_carbon_neutral_year`, `processing_tier` (`SimProxies.gd:37,445-505`) or `SimActions.set_night_shift_policy` (`SimActions.gd:313`). Live consumers call them on `/root/SimBridge` behind `has_method()`, which is therefore always false: `OffshorePanel` (tab F9) returns early at `:272` and `:360`, `CarbonLedgerPanel` and `ProcessingFacilityPanel` do nothing, and `Dashboard.gd:814` night-shift policy does nothing.

**Why it matters:** Phase 5.2-5.4 features exist in C# and tests but are unreachable by the player. The promoted baseline `07d_offshore.png` shows exactly this empty UI (blank "Slam-valorisering" card, empty dropdowns), so the screenshot gate now enshrines it as correct.

**Suggested fix:** Forward the missing functions, one line each, as at `:345`, and add a parity check.

```gdscript
func get_sludge_status() -> Dictionary: return _proxies.get_sludge_status()
func set_night_shift_policy(policy) -> Variant: return _actions.set_night_shift_policy(policy)
```

Add a GDScript or Python check that every non-underscore `func` in `SimProxies`/`SimActions` has a facade counterpart. Replace `has_method` guards on required APIs with direct calls or `push_error`, so absence is visible, and re-promote the `07d` baseline.

---

**Category: Error Handling (1)**

#### [F342] Annual tax payment ignores the `Spend()` result: tax is booked as paid even when cash never leaves — `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:58`

**Problem:** `FinanceManager.Spend` returns false without deducting when funds are insufficient, and its doc says callers must check. `ExecuteAnnualClose` discards the result, then calls `RecordTax(totalTax)` and sets `result.TaxPaid = totalTax`. The monthly path (`SimulationEngine.cs:2087-2092`) uses `if (!Spend) { warn; ForceSpend }`. The method also dereferences `financeManager` before later null-guarding it (inert today: the only caller passes `engine.FinanceManager!`). Other sites also discard `Spend` results (`Commands.cs:143,399`, `Licence.cs:178`), so "every other path checks" is overstated. `AnnualClose()` zeroes `Taxes` right after, so the P&L mismatch is wiped.

**Why it matters:** A cash-short player escapes the tax bill while the YEAR CLOSE event and `TaxPaid` claim it was paid, so books and cash disagree. Verifiers rated the real impact (dodged tax plus a misleading event) nearer Medium.

**Suggested fix:** Force the payment and surface the shortfall, and validate arguments up front.

```csharp
ArgumentNullException.ThrowIfNull(financeManager);
ArgumentNullException.ThrowIfNull(statements);
if (totalTax > 0m)
{
    if (!financeManager.Spend(totalTax))
        financeManager.ForceSpend(totalTax);   // and emit a shortfall event
    statements.RecordTax(totalTax);
    result.TaxPaid = totalTax;
}
```

---

**Category: Testing (5)**

#### [F197] Solution does not build: test project fails to compile (CI step 1 and `verify.yml` go red) — `src/LakskongenSim.Tests/Data/RealDataLayerTests.cs:11`

**Problem:** `dotnet build LakskongenSim.sln` fails with `RealDataLayerTests.cs(11,26): error CS0246: 'CurrentLayer' could not be found`. The working-tree rewrite of `CurrentLayer.cs` moved the class from namespace `LakskongenSim.Data.RealData.Layers` to `Lakskongen.Data.RealData.Layers` and dropped its parameterless constructor and `GetCurrentSpeed` API, while the old test still imports the former. With that file removed, the untracked `RealDataLayerTests_v2.cs` adds 11 more errors: CS0246 for `DataCache`, `IDataLoader`, `RealDataConfig` and `RealDataOrchestrator` (lines 27, 28, 128, 129) and CS1503 for the `List<IDataLoader>` arguments. It has only `using Lakskongen.Data.RealData.Layers;`. This is uncommitted W3 work (`docs/RUN_LOG.md`: "in progress"); HEAD builds cleanly.

**Why it matters:** No test in the repo can run (684 existing tests, `ExactSaveHashTests`, `EngineTwinRunDeterminismTests`, `DeterminismBanTests`). `verify.yml` runs `dotnet build LakskongenSim.sln` first, so this blocks merge and CI could not show any regression.

**Suggested fix:** Restore a distinct name for the zone-based class (or update the old test), add the missing usings, and build the whole tree before reporting progress.

```csharp
// RealDataLayerTests_v2.cs
using Lakskongen.Data.RealData;
using Lakskongen.Data.RealData.Cache;
using Lakskongen.Data.RealData.Layers;
```

Then run `dotnet build LakskongenSim.sln && dotnet test LakskongenSim.sln` on the full tree. With the two usings added, all 10 v2 tests pass in a scratch copy.

#### [F835] "Live state untouched" test passes an empty state and cannot see that `Project()` consumes the live growth RNG stream — `src/LakskongenSim.Tests/Environment/ForecastTwinTests.cs:12`

**Problem:** `ForecastTwin.Project` calls `growth.ApplySDEGrowthOneDay` per cohort per day, and each call draws 3-5 values from the model's `SimRngStream` even when sigma is 0 (`GrowthModel.cs:119,134,135`, plus Box-Muller at `153-154` when the temperature is in range). The Godot bridge passes the live `engine.Growth` (`GameBridge.Farm.cs:388,440,1095`), the same instance `InDayTickSystems.cs:159` uses for real growth, and the stream position is saved. The test builds `new GameState()` with no populations (zero draws), uses a fresh unseeded `GrowthModel`, and asserts `Assert.Empty(state.Populations)` (trivially true), checking only the lice model. `GetForecast` is cached per day, so the practical effect is that the growth stream differs between GUI runs (one `Project` per day) and headless runs (none), and probably between continuous play and a save on the same day.

**Why it matters:** The same seed plus the same player script no longer gives the same outcome, and saves capture the shifted position. The test's name promises exactly the property that is broken. See also F521 in Critical Issues.

**Suggested fix:** Use an isolated generator in `Project()` and strengthen the test.

```csharp
// ForecastTwin.Project
var iso = new GrowthModel(new SimRng(state.RandomSeed ^ state.Day).Stream("forecast"));
// test: stock a cohort first
var before = growth.RngForSnapshot.ExportState();
ForecastTwin.Project(state, growth, lice, 10.0, analytics);
Assert.Equal(before, growth.RngForSnapshot.ExportState());
```

Also assert that all cohort weights are unchanged (internals are visible via `InternalsVisibleTo("LakskongenSim.Tests")` in `SaveJson.cs`).

#### [F1073] Determinism and save gates are same-process and shallow, so they pass while 48 of 64 save/load combinations and all cross-process runs diverge — `src/LakskongenSim.Tests/Integration/ExactSaveHashTests.cs:97`

**Problem:** `ExactSaveHashTests` and `EngineTwinRunDeterminismTests` run both engines in one process (so process-randomized `HashCode` seeds match) and cover only seeds 7/13 with 120+60 or 90+45 days from a single save point. They never reach quarter 3, never mask-check competitor, facility or contract state, and never run a second OS process. All 7 shipped tests pass in a scratch run. The wider matrix (8 seeds x save days 45/100/200/300 x events on/off, H=540) diverges in 48 of 64 combinations. On unmodified code, 3 separate processes with the same seed diverge from day 1 in 18 of 18 runs (`Zone.WaterFlowRate`, F453). `FirstDifference` also skips `Balance`/`Runtime`, hiding fields that diverge.

**Why it matters:** `AGENTS.md` names these tests as the determinism gates, so green CI gives false assurance about lost facilities, contracts and competitors (F243, F409, F1071, F478). After the persistence fixes 128 of 128 combinations at H=1100 were identical, so the matrix is achievable and cheap (about 20 s).

**Suggested fix:** Widen the matrix, add a subprocess test and a completeness check.

```csharp
[Theory]
[MemberData(nameof(SeedSaveDayEventMatrix))]   // seeds 1..8 x {45,100,200,300,356} x {events on, off}
public void ResumedRunMatchesUninterrupted(int seed, int saveDay, bool events) { /* H >= 540 */ }
```

Add a test that runs seed x 365 days in 2-3 child processes and compares the GUID-masked save hash, plus a reflection deep-diff (live vs resumed, with an allowlist of derived fields) so a new unsaved field fails CI. Remove the `Balance`/`Runtime` skip from `FirstDifference`.

#### [F884] Probe's mid-run save/load is checked only by counts; a resumed career diverges from the uninterrupted one — `src/LakskongenSim.Tests/Integration/PlayerScriptProbe.cs:115`

**Problem:** After the day-300 round trip, `ResumeAfterSaveLoadRoundTrip` asserts only site and loan counts and day equality; it never compares the resumed world with an uninterrupted twin. Its header still says RNG positions are not persisted, although they are (`SimulationEngine.cs:3716`, `Rng.ImportStates`). Measured with seed 7 in one process: `RunCareer(7, true)` gives 446,606 kg harvested and 27.58% sea mortality against 443,030 kg and 28.16% for `RunCareer(7, false)`, stable across runs. A state diff on `po3_red_sone` (events on, no player verbs) is clean at day 120 and 180 but diverges for saves at day 240 and 300: four `SeaSites[1]` pens end with 7340 vs 7341 fish, which then moves the `Scope3_Feed` `feed_total` emission. The cause is a persistence gap, not the emission calculation.

**Why it matters:** "Save/load round-trips exactly" is proven only for short, verb-free runs. A player saving late in a grow-out resumes into slightly different feed and economics, and the one test with a realistic long career cannot see it because its bands are wide.

**Suggested fix:** Run a non-round-tripped twin and assert exact continuation.

```csharp
var resumed  = RunCareer(seed, roundTrip: true);
var straight = RunCareer(seed, roundTrip: false);
Assert.Null(StateDiff.FirstDifference(
    SaveJson.Serialize(resumed.Engine), SaveJson.Serialize(straight.Engine)));
```

Move `FirstDifference` from `ExactSaveHashTests` to a shared test utility. Expect this to fail until the post-day-240 state gap is fixed. Update the `ResumeAfterSaveLoadRoundTrip` doc comment.

#### [F906] Disease tests pass with the entire disease model removed — `tests/Health/DiseaseTests.cs:32`

**Problem:** Both tests assert only vacuous properties. `p > 0.0` is satisfied by the 1e-6 `InitialPrevalence` seed alone, `total < 10000` by baseline `MortalityModel` mortality alone, and `afterP <= beforeP` also passes when the two values are equal. No control population is compared, so `...IncreasesPrevalenceAndCausesDeaths` and `...TreatmentReducesPrevalenceAndLice` verify nothing. Reproduced on a scratch copy with three mutations (remove the SIR update `p += dp`, remove `SetDiseaseMortalityRate`, remove `p *= (1.0 - totalReduction)` in `ApplyTreatment`): all 5 Health tests still passed. With `Beta = 0` the first test reads prevalence 7.4e-7 and 9,892 survivors; with disease on it reads 9,889, a 3-fish difference out of 10,000. `CampaignScenarioTests` (`:218`) also asserts only `> 0.0` for prevalence.

**Why it matters:** `DiseaseSystem` is core biology used in production (`engine.Disease`) and these are its only direct unit tests, so a regression there would go unnoticed.

**Suggested fix:** Compare against a disease-off control and assert strict, bounded effects, starting from a prevalence large enough to give signal.

```csharp
// start both runs from InitialPrevalence = 0.05; control: Beta = 0, ExtraMortalityRatePerDay = 0
Assert.True(p > 100 * ds.InitialPrevalence);          // or compare with ds.EquilibriumPrevalence()
Assert.True(totalWithDisease < totalControl);
// treatment test
Assert.True(beforeP > 1e-3);
Assert.True(afterP < beforeP * 0.6);                  // strict bound derived from reduction/efficacy
```

### Medium Priority

369 findings, grouped by category. IDs are stable for cross-reference. Where independent verifiers narrowed the reach of a finding (latent, dormant, UI-only, unreachable today), that qualifier is kept in the text. Paths are repo-relative.

#### Correctness & Logic

- **[F1121] design-token-sync capture/diff use the wrong repo root and an invalid Godot script; visual_diff renames tracked baselines** — `.claude/skills/ecc/design-token-sync/design_token_sync/capture_panels.py:85` (correctness). Four `.parent` hops resolve to `<repo>/.claude/skills`, so Godot gets a non-project `--path`; scene paths point at nonexistent `res://ui/dashboard/…`; the generated script `extends Node`, calls `OS.exit` and `get_tree().process_frame()`, and stderr lines containing `push_error` are filtered out. `visual_diff.py:33` also renames the tracked `screenshots/baseline` dirs to `.bak`, so a CI kill leaves the 28-shot baseline moved aside. *Fix:* Import `REPO_ROOT` from `config.py`, fix the scene paths, generate an `extends SceneTree` script using `await process_frame`/`quit(code)`, stop filtering stderr, and pass baseline/current dirs as arguments instead of moving tracked directories.
- **[F1012] Scenario `starting_research` ids reference nodes that do not exist in the active research tree** — `godot/data/scenarios.json:218` (correctness). Four of nine scenarios grant ids such as `sensor-networks` and `deep-sea-farming` that exist only in the discarded built-in tree; `ApplyStartingState` appends them to `UnlockedResearchIds` unvalidated and `ForceUnlock` ignores unknown ids. Players are told they own research that has no node, effect or UI row. *Fix:* Validate `starting_research` against `engine.Research.Nodes` (log and skip), correct the ids to the shipped tree, and add a cross-file test that every scenario research id exists in `research_nodes.json`.
- **[F961] ScenarioGoalHUD is declared before GameUI, so the full-rect GameUI panel paints over it** — `godot/scenes/GameScene.tscn:81` (correctness). Sibling order is draw order; GameUI (`offset_top=56`, opaque) hides all but a 50 px strip, so every committed UI baseline shows the goal label cut off mid-sentence with the progress bar and timer hidden. The scenario objective is effectively unreadable. *Fix:* Move the `ScenarioGoalHUD` node after `GameUI` (or give it its own CanvasLayer / z_index), then regenerate the baselines.
- **[F1061] W/M hotkeys call `advance_week()`/`advance_month()`, which do not exist, and D/W steal the camera's WASD keys** — `godot/scripts/GameController.gd:98` (correctness). `SimBridge`/`SimActions` only define `advance_day(s)`, so pressing W/M raises "Nonexistent function"; the handler has no echo guard, so holding D to pan advances one sim day per key-repeat. *Fix:* Add `advance_week/month` as `advance_days(7/30)` or route through `advance_days`, ignore `event.echo`, and rebind or remove the letter shortcuts that collide with WASD pan.
- **[F064] Per-session bridge fields are never reset on New Game or Load** — `godot/scripts/autoload/GameBridge.Core.cs:876` (correctness). `_lastDiseaseCheckDay`, `_offerCache` and `_offerCacheDay` survive a restart, so reloading an earlier save suppresses the weekly lice warning until the day passes the old high-water mark and can serve a stale offer cache. Contract-offer RNG is also drawn from UI getters, so panel opening changes the saved stream. *Fix:* Reset these in one `ResetSessionState()` called from both start paths, and generate daily offers inside the engine tick so the getter is read-only.
- **[F694] `EndGame` is not idempotent, so one loss can emit `GameEnded` twice with different reasons** — `godot/scripts/autoload/GameBridge.Core.cs:921` (correctness). `HandleScenarioLost` ends the game inside `Advance`, then the bridge's `CheckGameOverConditions()` runs again (-1M threshold vs the scenario's -2M) and emits a second misleading reason; `SimState` raises two critical alerts. *Fix:* Guard with `if (!_gameRunning) return;` at the top of `EndGame`, skip the post-advance check when not running, and read the bankruptcy threshold from one calibrated source.
- **[F697] Lice "treatment cost" uses an invented 50 NOK per fish, 6x to 100x the engine's delousing costs** — `godot/scripts/autoload/GameBridge.Core.cs:893` (correctness). `totalFish * 50m` is emitted as `treatmentCost`, whereas `DelousingSystem` costs are 0.5 to 8 NOK per fish, so a 2M-fish farm is quoted ~100M NOK for a treatment costing a few million. *Fix:* Ask the engine for the estimate (`GetProfile(method).CostPerFish * fish`) or drop the figure from the signal.
- **[F698] `GetWelfareAlerts` still reads the legacy company lice model and uses thresholds inconsistent with `GetWelfareBreakdown`** — `godot/scripts/autoload/GameBridge.Core.cs:1124` (correctness). Line 879 says the per-site models are authoritative, yet this tests `SeaLice?.AverageLicePerFish > 2.0`, and the same fish condition is "high stress" in the breakdown but "CRITICAL" in the alerts. *Fix:* Use `GetCompanyLiceAverage()` and derive stress level, risk and alerts from one banding table.
- **[F1059] Multi-day skip (`AdvanceDays`) is not the same path as the daily timer tick** — `godot/scripts/autoload/GameBridge.Core.cs:266` (correctness). Skips never call `CheckDiseaseOutbreaks` or the alert emit, check bankruptcy only after the whole skip, and swallow exceptions then continue on a half-advanced engine. *Fix:* Extract one `StepOneDay()` used by both the timer and the skip loop, or move the lice warning into the engine as a day note.
- **[F689] Bubble curtain and O2 diffuser are charged 1.8M/2.4M while the catalog advertises 250k/200k** — `godot/scripts/autoload/GameBridge.Depth.cs:930` (correctness). `DeployCountermeasure` hard-codes the old prices and `InstallBloomMitigation` delegates to it, so a player clicking Install on a "250 000 NOK" row is charged 7x to 12x (and silently refused if cash is between). *Fix:* Charge `BloomMitigation.FixedCostNok(kind)` in `DeployCountermeasure` and add a probe asserting the balance delta equals the catalog `fixed_cost`.
- **[F690] Cohort transfer trusts a UI-typed mean weight, which feeds the destination MAB check and mortality multiplier** — `godot/scripts/autoload/GameBridge.Depth.cs:1161` (correctness). `meanWeightKg` from OffshorePanel is passed straight to `PlanTransfer`, so a mistyped weight changes the capacity check and the weight-tiered mortality, while the 3-day ETA shown does not match the instant execution. *Fix:* Derive count and weight on the C# side from the source site's cohorts, and book a real wellboat job or drop the ETA.
- **[F692] Mod scenarios listed in the campaign menu cannot be started on a fresh launch** — `godot/scripts/autoload/GameBridge.Depth.cs:89` (correctness). `StartScenarioCore` validates against `Scenario ?? new ScenarioSystem(...)` which lacks mod scenarios until after `Initialize`, so the click returns false and `SimActions.gd` silently starts the default game; it works only after one game has been played. *Fix:* Build the probe roster with `RegisterModScenarios` for both listing and starting, and return a reason when a scenario is unavailable.
- **[F696] `GetCarbonIntensity` estimates harvested kg as revenue / 80 NOK from a capped event log** — `godot/scripts/autoload/GameBridge.Depth.cs:1358` (correctness). `EventLog` is trimmed to 1000 entries and the per-kg price varies, so the denominator shrinks and the shown kg CO2e per kg harvested drifts upward. *Fix:* Persist a `GameState.TotalHarvestedKg` counter incremented in the harvest command and divide by it.
- **[F947] `ValorizeSludgeNow` returns `decimal`, which Godot cannot marshal, so the button reports "no sludge"** — `godot/scripts/autoload/GameBridge.Depth.cs:1056` (correctness). The generator omits the method, so `SimProxies.valorize_sludge_now` always takes the `push_warning` branch and returns 0.0, and the UI text claims there is nothing to valorize. *Fix:* Return `double` like the other money getters and add a generated-exposure check to the contract test.
- **[F716] `AttemptAcquisitionTarget` mixes NOK and NOK-millions and ignores `Spend` failure** — `godot/scripts/autoload/GameBridge.Economy.cs:564` (correctness). `AskingPrice` is in NOK millions but compared to raw NOK cash, so affordability always passes; the bridge discards `Spend`'s result, removes the target and logs a debit that never happened. Dormant today (no UI caller). *Fix:* Move it into an engine command in decimal NOK that checks `Spend()` first, transfers the asset and increments `AcquisitionCount`.
- **[F717] `GetTotalDebt`/`GetActiveLoans`/`GetMonthlyPayment` are TODO stubs although loans are tracked** — `godot/scripts/autoload/GameBridge.Economy.cs:347` (correctness). `ActiveLoans` is amortized daily but the bridge returns 0, so the HUD goal tracker's `debt_and_profit` goal reads "debt cleared" from tick one and a taken loan never shows as debt (`starting_debt` is also read by nothing). Engine-side win/loss uses real equity and is unaffected. *Fix:* Compute outstanding principal from `ActiveLoans`, return real entries, and apply `starting_debt` through `TakeLoan`.
- **[F725] Finance readouts bypass `FinancialStatements`/`TaxSystem`: tax "burden" is 22% of cash, P&L revenue is the cash balance** — `godot/scripts/autoload/GameBridge.Economy.cs:425` (correctness). Two P&L sources disagree and one labels the bank balance as revenue, with a literal 0.22 ignoring the modelled 22%+25% rent and loss carry-forward. *Fix:* Source revenue, costs, interest and tax from `FinancialStatements`/`TaxSystem` and delete the balance-based estimate.
- **[F718] Pumping and deep-feeding bloom mitigations charge cash but have no simulation effect** — `godot/scripts/autoload/GameBridge.Farm.cs:345` (correctness). They are recorded only in the bridge's in-memory set; `BloomMitigation.IsInstalled` returns false for them, yet the panel advertises "−45% død." and a repeated click silently uninstalls with no refund. *Fix:* Model the effect in `ChooseBloomMitigation` or remove the two kinds from the catalog, and split install from remove.
- **[F723] Post-smolt and offshore-repair commands charge money but change nothing** — `godot/scripts/autoload/GameBridge.Farm.cs:1925` (correctness). `StartSmoltBatch`, `VaccinateBatch`, `TransferBatchToSea` and `RepairOffshore` bill NOK and log events without calling the domain methods (`StartNewBatch`, `RepairStructure`). No UI caller found. *Fix:* Call the domain methods, validate ids, readiness and capacity, or remove the verbs from `SimBridge`.
- **[F724] Placeholder values are returned as live data (genetics, empire, competitors, IPO)** — `godot/scripts/autoload/GameBridge.Farm.cs:1270` (correctness). `GetInbreedingCoefficient` always returns 0.05, breeding values are 0.0, and empire/competitor/IPO getters return constants although the engine now has those systems, so the >15% warning can never fire. *Fix:* Wire to the engine systems or return `{"available": false}` that panels render as unavailable.
- **[F726] Gill-health alert skips a site at the worst possible score (`GillScore == 0`)** — `godot/scripts/autoload/GameBridge.Farm.cs:477` (correctness). `GillScore > 0 && < 30` excludes a clamped 0, so the inbox goes silent exactly when gills have collapsed. *Fix:* Use `< 30` for critical with no lower bound and test 0, 29.9, 30 and 59.9.
- **[F734] `AcquireDistressedAsset` never returns the `asset` dictionary on success** — `godot/scripts/autoload/GameBridge.Licence.cs:204` (correctness). The asset is removed from the list by the engine call, then re-queried, so `result["asset"]` and `price_paid` are always missing. *Fix:* Look the asset up before calling the engine and build the dictionary from the captured instance.
- **[F714] Market getters draw from live sim RNG streams** — `godot/scripts/autoload/GameBridge.Market.cs:225` (correctness). `GetMarketReport`/`GetPriceForecast` consume the Intelligence stream and `RefreshOfferCache` consumes the contracts stream, whose positions are saved, so which news/offers appear depends on how often panels were opened. The twin-run gates never call the bridge, and price is driven by a separate instance. *Fix:* Derive forecast noise statelessly from `hash(seed, day, weeks)` or a non-persisted fork, cache per day, and test that getters leave `CaptureStates()` unchanged.
- **[F727] Price forecast hard-codes mean 70 / volatility 5 instead of the calibrated market values** — `godot/scripts/autoload/GameBridge.Market.cs:225` (correctness). The model uses long-term mean 82 and initial 88, so forecasts are pulled ~12 NOK/kg too low; the literal 70 fallback and forecast mapping are also duplicated. *Fix:* Pass `MarketSimulation.LongTermMean`/`VolatilityWeekly` and share one forecast-to-dictionary helper.
- **[F934] `SaveGame(name)` and `LoadGame(fileName)` use different identifiers** — `godot/scripts/autoload/SaveLoadManager.cs:185` (correctness). `SaveGame` appends `.json` but `LoadGame` uses its argument as given, so loading by slot name (`quicksave`) fails with "Save file not found". *Fix:* Normalise the extension and sanitising in `LoadGame` and add a `SaveGame("a")`/`LoadGame("a")` round-trip test.
- **[F1124] Autosave only fires when the emitted day is a multiple of 30; rotation is a no-op** — `godot/scripts/autoload/SaveLoadManager.cs:640` (correctness). 7-day skips land on a multiple of 30 only every 210 days, and every autosave writes `autosave_latest`, so `MAX_AUTOSAVES` never triggers. *Fix:* Track `_lastAutoSaveDay` and save when 30+ days have elapsed (and on game over/exit), with timestamped names or remove the dead rotation.
- **[F763] Vaccination "window open" warning fires from day 0 all year** — `godot/scripts/bridge/SimState.gd:325` (correctness). `WindowDaysLeft` returns 30 (non-negative) for days 0..274, so the HUD shows "Vaksinasjonsvindu åpent" for ~10 months while `VaccinateSite` refuses outside October. The `current_day > 300` branch is harmless today but becomes wrong once the producer is fixed. *Fix:* Return -1 outside `[WindowStartDoy, WindowEndDoy]`, add boundary tests, and use day-of-year in the winter branch.
- **[F740] UNDERWATER zoom never goes underwater and the underwater fog is never undone** — `godot/scripts/environment/FjordCamera.gd:243` (correctness). With a terrain assigned the camera is clamped 10 m above water while `entered_underwater` still fires; on exit `set_weather` returns early so the teal `fog_light_color` persists for the session. *Fix:* Skip the over-water search/min-height clamp for UNDERWATER, cache and restore the fog parameters, and assert `camera.y < 0` in the harness.
- **[F741] `look_at()` is called with the camera directly above the focus when the focus is inland** — `godot/scripts/environment/FjordCamera.gd:241` (correctness). The fallback places the camera at `(focus.x, y, focus.z)`, so view direction is parallel to UP and Godot logs an error every frame while the camera keeps a stale rotation. *Fix:* Keep a minimum horizontal back-off in the fallback or pick the up vector from the view direction.
- **[F742] Camera pan polls raw keys and `ui_*` actions regardless of UI focus** — `godot/scripts/environment/FjordCamera.gd:180` (correctness). Typing a/s/d/w in the Encyclopedia search `LineEdit` pans the 3D camera, and arrow-key list navigation does too. *Fix:* Use dedicated `cam_pan_*` actions (WASD only) and skip when a `LineEdit`/`TextEdit` has focus.
- **[F739] `set_season()` never reaches the terrain shader because `find_child()` skips nodes without an owner** — `godot/scripts/environment/FjordEnvironment.gd:156` (correctness). `TerrainMesh` is added at runtime without `set_owner`, so the lookup returns null and the terrain stays summer all year. *Fix:* Expose `FjordTerrain.set_season()` that stores and applies the value, or use `find_child("TerrainMesh", false, false)` with a warning when missing.
- **[F750] Terrain noise parameters look mis-scaled; baseline frames show needle-like, disconnected shards** — `godot/scripts/environment/FjordTerrain.gd:162` (correctness). `domain_warp_amplitude = 30` over a ~6-unit input span plus `(noise - 0.5) * 2 * 0.15` gives ±120-180 m jitter per 15.6 m cell; the cause is inferred from parameters and screenshots, not measured. *Fix:* Use warp amplitude of 0.2-0.5 and a noise term of ~0.02-0.05, gate on `terrain_stats.gd` roughness, and cross-check against `TerrainGenerator.cs`.
- **[F745] Sim3DSync reads a property that does not exist (`day`) and uses truthiness checks that treat 0.0 as missing** — `godot/scripts/environment/Sim3DSync.gd:62` (correctness). `SimBridge` exposes `current_day`, so the once-per-day sync runs only once; `if val:` turns calm wind 0.0 into 5.0 and year-start season 0.0 into summer. *Fix:* Read `current_day`, test `!= null` / `in` instead of truthiness, or consume a single typed snapshot.
- **[F963] TimeOfDay overwrites `cloud_coverage` and `fog_density` every frame, undoing WeatherController** — `godot/scripts/environment/TimeOfDay.gd:169` (correctness). Storm/rain/fog presets revert one frame after their 10 s transition ends. *Fix:* Single owner per parameter: let WeatherController compose weather factors over TimeOfDay's base values, or drop the writes from TimeOfDay.
- **[F774] MultiplayerHUD `_input` polls an InputMap action that does not exist** — `godot/scripts/multiplayer/MultiplayerHUD.gd:77` (correctness). `toggle_multiplayer_hud` is not in `project.godot`, so once mounted it logs an error on every input event, and the documented Tab binding would swallow focus navigation. *Fix:* Add the action (not Tab), guard with `InputMap.has_action`, and use `_unhandled_input`.
- **[F771] `_set_lobby_phase` assigns `.disabled` on a `LineEdit`, aborting the function** — `godot/scripts/multiplayer/MultiplayerLobby.gd:340` (correctness). `LineEdit` has `editable`, not `disabled`, so the loop errors at the third element and `start_button.visible = true` never runs. *Fix:* Type the arrays, set `capital_edit.editable` separately and `disabled` on buttons.
- **[F772] Lobby start gate is not re-evaluated when players join or leave** — `godot/scripts/multiplayer/MultiplayerLobby.gd:201` (correctness). `_check_all_ready()` is called only on ready toggles, `_on_start_pressed` trusts the button state, and "All players ready!" spams the log. *Fix:* Re-run it in `add_remote_player`/`remove_remote_player`, log on the false→true transition, and re-validate in `_on_start_pressed`.
- **[F790] `_env` and `_ui_layer` are assigned at the tail of an un-awaited coroutine, behind early returns** — `godot/scripts/tools/ScreenshotHarness.gd:289` (correctness). If pens do not appear, `_env` stays null and all fjord shots are captured unstaged with the UI visible, exit code 0. *Fix:* Resolve both nodes in `_boot_game()` right after `add_child`, `await` the focus function and record a failure when `_env == null`.
- **[F568] Per-tick refresh overwrites the player's unsaved feed-recipe slider edits** — `godot/scripts/ui/AIFeedingPanel.gd:63` (correctness). `refresh()` calls `_sync_recipe_sliders()` with `set_value_no_signal()` from the applied recipe on every `state_updated`, so a draft snaps back about once per second at 1x unless the game is paused. *Fix:* Sync sliders only in `on_panel_shown()` or after Apply, and have `_on_state_updated` refresh just the status labels.
- **[F572] "FCR Besparelser" is always 0.0% because the bridge never returns `fcr_multiplier`** — `godot/scripts/ui/AIFeedingPanel.gd:153` (correctness). `GetAIFeedingStatus` has no `fcr_multiplier`/`daily_savings`, so the `1.0` default always wins and the KPI reads 0.0% after paying for the system. *Fix:* Expose the real effect (e.g. `fcr_improvement`) from C#, render it, and delete the legacy-shape fallback dict.
- **[F582] Analytics chart interaction bugs: sticky pan, tooltip never redraws, mixed coordinate spaces, period-button styling corrupts itself** — `godot/scripts/ui/AnalyticsPanel.gd:253` (correctness). Release outside a chart leaves `_is_panning` true, hover never calls `queue_redraw()`, hit-testing mixes global and local coordinates and applies `_pan_offset` twice, and the inactive period buttons inherit the tinted active style. Masked today because the panel loads no data. *Fix:* Handle release before the hit test, redraw on hover change, use `get_local_mouse_position()`, capture the normal StyleBox once, and apply the zoom factor only when zoom changed.
- **[F984] 48 translation keys used in code are missing from `translations.csv`; fallbacks are English in a Norwegian-default game** — `godot/scripts/ui/AttentionInbox.gd:52` (correctness). `UI_ATTENTION_*` (incl. all C# call sites), `UI_HUD_TIP_*`, `UI_BRIEFING_*` and 171 literal-key `tr()` calls have no rows, and placeholder styles (`%s`, `{0}`, `{pct}`) are mixed. *Fix:* Add the rows for nb/en, standardise on printf placeholders via one helper, and back it with a CSV/code diff test.
- **[F1041] Three panels subscribe to GameBridge signals that do not exist, silently disabling live updates** — `godot/scripts/ui/CompetitorDashboard.gd:17` (correctness). `CompetitorDataUpdated`, `MarketDataUpdated` and `ProcessingDataUpdated` are declared nowhere in C#, so the `has_signal` guards are always false and the panels never update. *Fix:* Declare and emit the signals in the C# partials or refresh from `state_updated`/`day_advanced`, and extend the contract test to fail on guarded names absent from C#.
- **[F575] ConsequenceDirector keeps per-run dedup state across New Game / Load** — `godot/scripts/ui/ConsequenceDirector.gd:38` (correctness). `_seen_event_keys` and `_events_primed` are never reset, so a second New Game with the same seed suppresses identical events and a mid-session Load treats the save's last 10 events as new and fires a burst of toasts. *Fix:* Connect `GameBridge.GameStarted` to a `_reset()` and key on a monotonic event sequence id instead of `day|name|desc`.
- **[F580] Pulse and warning-flash feedback target `/root/HUD` and `/root/GameUI`, which never exist** — `godot/scripts/ui/ConsequenceDirector.gd:318` (correctness). Neither is an autoload, so `_pulse_feedback()` and `_flash_warning()` are permanent no-ops with no log. *Fix:* Resolve the nodes from the current scene (`find_child`) or have HUD/GameUI register themselves.
- **[F571] Markets and Trade panel presents invented statistics as live market data** — `godot/scripts/ui/ContractsPanel.gd:128` (correctness). 12-week Lo/Avg are `spot*0.83`/`spot*0.91`, the trend is always "▲ Stigende", the 12 price bars are never written, and region prices and volumes use fixed multipliers. Real series exist (`get_analytics_data`, `get_export_markets`). Display-only. *Fix:* Compute lo/avg/hi and trend from `get_analytics_data("market_price_nok", 84)`, bind the bars, and drive regions from the export-markets API or label them illustrative.
- **[F577] Crew panel shows hard-coded skill levels and a UI-computed "Effektivitet" even with zero staff** — `godot/scripts/ui/CrewPanel.gd:274` (correctness). The six skill tiles are a literal list, the bar is a 4 px rect, and efficiency is `min(96, 60 + 12*roles)` computed in the view. *Fix:* Remove the skills grid until per-worker skills exist, expose `crew_efficiency` from the sim, and show "--" with no crew.
- **[F585] CrisisPanel can never initialise and has latent display bugs** — `godot/scripts/ui/CrisisPanel.gd:24` (correctness). `Engine.has_singleton("CrisisResponseManager")` is false (no such autoload), so the modal never connects; if fixed, `advisor_hint` shows literal `[i]` tags, the fade-out tween leaves alpha 0 so the second crisis is invisible, and `crisis_data` keys are hard-indexed. *Fix:* Use `get_node_or_null` (and create the manager or remove the panel), reset `modulate` at the start of the handler, and use `.get()` with defaults.
- **[F593] Per-panel ESC handlers hide cached tab panels and desync GameUI** — `godot/scripts/ui/Dashboard.gd:113` (correctness). Dashboard/FinancePanel/EncyclopediaPanel call `hide()` and consume `ui_cancel` before `GameUI._input` runs, so `_active_tab_id` stays set and clicking the same tab returns early, leaving a blank content area until another tab is opened. Recoverable, UI-only. *Fix:* Make GameUI the single owner of ESC, delete the per-panel handlers, and harden `_switch_to_tab` to re-show when the panel is hidden.
- **[F603] Dashboard "Revenue" KPI shows annual-reset revenue while its tooltip, trend, sparkline and flash describe cash balance** — `godot/scripts/ui/Dashboard.gd:443` (correctness). `get_revenue()` is zeroed by `AnnualClose()`, so the value collapses at year-end (and flashes red) while the arrow and sparkline show a healthy balance. *Fix:* Show balance under a "Saldo" label, or keep revenue and switch trend/sparkline/tooltip to a revenue series with a year-end note.
- **[F951] Night-shift policy `OptionButton` is wired to `pressed` with a handler needing an argument** — `godot/scripts/ui/Dashboard.gd:806` (correctness). Each click raises an arity error, and the handler targets `set_night_shift_policy`, which the facade does not define, so the selection is discarded. *Fix:* Connect `item_selected`, forward `set_night_shift_policy` in `SimBridge`, and re-select from `get_night_shift_status`.
- **[F969] Dashboard title renders the literal string `%s — Oversikt`** — `godot/scripts/ui/Dashboard.gd:239` (correctness). `UI_DASHBOARD_TITLE` contains a `%s` that is never substituted, and the defect is baked into baselines 03/03b/03c. *Fix:* `tr("UI_DASHBOARD_TITLE") % company_name` (or drop `%s`) and add a test that no label text contains an unsubstituted `%s`.
- **[F602] Selected encyclopedia entry is dark teal on dark teal (1:1 contrast)** — `godot/scripts/ui/EncyclopediaPanel.gd:603` (correctness). `BG_SELECTED` and `TEXT_ACCENT` are both `#0A4D68`, so the auto-selected first entry is invisible; related-link text is ~1.8-2:1 and hover ~2.3:1. The a11y audit does not cover these local constants. *Fix:* Use a light font colour on the selected background and pull the colours from design tokens so `a11y_audit.py` covers them.
- **[F595] Finance "Inntekter vs Kostnader (6 mnd)" chart and P&L statement are never bound to data** — `godot/scripts/ui/FinancePanel.gd:532` (correctness). `%ChartRev*/%ChartCost*/%Pl*` are collected but never written, so the chart shows fixed scene bars under static month labels and every P&L row is `--`. *Fix:* Bind both from existing data (reuse the Dashboard bucketing or a bridge call on `FinancialStatements`), or hide the cards until wired.
- **[F605] Story-card share bars fill their whole track regardless of share** — `godot/scripts/ui/FinancePanel.gd:512` (correctness). The fill `ColorRect` in a `PanelContainer` only gets a `custom_minimum_size` (a minimum), so Container layout stretches it to 100%. *Fix:* Use a `ProgressBar` or an anchored rect with `SIZE_SHRINK_BEGIN` and a width set after layout.
- **[F621] `screen_shake` is a silent no-op: it only shakes a `Camera2D` and the game has none** — `godot/scripts/ui/JuiceEffects.gd:19` (correctness). The only camera is the `Camera3D` `FjordCamera`, so storm/escape/bankruptcy shake never happens; the shared static `_original_camera_offset` would also corrupt overlapping shakes. *Fix:* Shake the `Camera3D` (`h_offset`/`v_offset`) or the root UI, store the base offset per target, and warn when no camera is found.
- **[F622] Juice tweens snapshot the current (possibly mid-animation) state as "original" and never kill the previous tween** — `godot/scripts/ui/JuiceEffects.gd:95` (correctness). Rapid F-key tab switches can leave a panel offset by up to 14 px, and repeated flashes can leave a label tinted. *Fix:* Keep one tween per node/property, kill the old one, restore a base captured once, and animate `.from(rest + 14)` to rest.
- **[F619] MapPanel as a GameUI tab: ESC/Close/Confirm hide it behind GameUI's back; `region_selected` has no listener** — `godot/scripts/ui/MapPanel.gd:414` (correctness). The tab goes blank while GameUI still thinks it is active, and Confirm emits to nobody. Recoverable (a second ESC or another tab). *Fix:* Connect `close_requested` to `_switch_to_tab("dashboard")`, replace the ESC `hide()` with `_on_close_pressed()`, and wire `region_selected` to the placement flow or hide Confirm.
- **[F627] Region markers use a legacy vertical inset, so dots land 20-40 px off their lon/lat** — `godot/scripts/ui/MapPanel.gd:456` (correctness). The coastline uses `y * h`, markers use `y * (h - 80) + 30` computed once with no re-layout on resize. *Fix:* Use the same transform as the drawer and reposition markers on `_map_container.resized`.
- **[F628] Region markers are mouse-only: no focus, no keyboard/gamepad selection** — `godot/scripts/ui/MapPanel.gd:717` (correctness). `_RegionMarker` is a plain `Control` with default `FOCUS_NONE`, which is an accessibility regression in the panel that gates site placement. *Fix:* Make markers flat `Button`s (or `FOCUS_ALL` with `ui_accept`), draw a focus ring, and add tooltips.
- **[F629] Mod Manager drag-to-reorder is dead code, order is never sent to the engine, and Apply gives no feedback** — `godot/scripts/ui/ModManagerPanel.gd:360` (correctness). `_drag_index` can never become ≥ 0, `ApplyModSettings` sends only `{id, enabled}`, its result is ignored, and an empty list inserts a fake `no_mods_found` mod. *Fix:* Implement the drag (or remove it), send ordered ids, show success/error, and render the empty state as a label.
- **[F624] Morning briefing shows fabricated or mis-scaled numbers (oxygen 92%, reputation 70, fatigue +600%)** — `godot/scripts/ui/MorningBriefing.gd:319` (correctness). The `/root/SimulationCore` and `/root/GameState` autoloads do not exist, so the "fallback" constants are the only branch; fatigue is multiplied by 100 although the resolver already returns percent points. *Fix:* Read `oxygen_mgl`, `brand_score` and real night-shift values from `SimBridge`, and omit lines when data is missing.
- **[F642] Notification history is flooded by per-tick alert re-emission and mis-maps event severity** — `godot/scripts/ui/NotificationCenter.gd:220` (correctness). `_refresh_state` re-pushes every standing alert twice per tick with no dedupe, filling the 20-entry cap within a day and re-opening the panel on every critical entry; `_category_to_severity` matches names that do not exist in `EventCategory`. *Fix:* Keep a seen-key set cleared on New Game, dedupe alerts by level/message with edge detection, and map real enum names.
- **[F657] Keyboard accessibility: focus indicators removed or never reachable (NotificationCenter, pen list, PauseMenu)** — `godot/scripts/ui/NotificationCenter.gd:359` (correctness). Entry buttons override `focus` with `StyleBoxEmpty`, `link_vertical_focus` links nothing, PenManagement rebuilds its list every tick dropping focus, and `PauseMenu.show_menu()` never grabs focus. *Fix:* Keep the theme focus ring, link the buttons themselves, update pen buttons in place, and `grab_focus()` on Resume.
- **[F648] Containment and sludge rows use code-built labels with no font colour; site names are invisible in the baseline** — `godot/scripts/ui/OffshorePanel.gd:193` (correctness). Only a font size is set, and the default theme is not reaching controls under the `CanvasLayer`, so the player cannot tell which site each upgrade button belongs to. *Fix:* Set explicit colours/fonts on code-built labels or apply the theme to the `UILayer` subtree, and add a harness check for visible text.
- **[F952] Pen detail card ignores the per-site payload and shows farm-wide values plus hard-coded mock text** — `godot/scripts/ui/PenManagement.gd:265` (correctness). The bridge emits per-site `lice_per_fish`, `oxygen_mgl` and `mean_weight_kg`, but the card reads farm-wide values and prints literals ("8.5 m", "1.2%", genetics grades). *Fix:* Read the selected pen's fields (guard NaN) and remove or mark the placeholder rows.
- **[F983] Repair-nets toast uses a format string containing an unescaped `%`** — `godot/scripts/ui/PenManagement.gd:508` (correctness). `"…(100%)"` passed through `%` errors in GDScript's sprintf, aborting the success handler so no toast shows (both CSV and fallback). Static analysis, not run. *Fix:* Escape as `(100%%)` or drop the percent, and lint formatted CSV values.
- **[F655] Scenario picker shows a lock on every card, overflows its panel, and hard-codes catalogue and lock state** — `godot/scripts/ui/ScenarioSelectPanel.gd:230` (correctness). `tr('🔒')` is used for all cards, 5×310 px cards overflow the 1050 px panel (clipped at 125% UI scale), and 4 scenarios in the data are unreachable. *Fix:* Use the per-scenario icon, size the panel from the card total or use a `ScrollContainer`, and build cards from `scenarios.json`.
- **[F656] Scenario cards fail WCAG contrast and encode difficulty by colour only** — `godot/scripts/ui/ScenarioSelectPanel.gd:241` (correctness). Title/win text is 1.5-1.65:1, "Startkapital" 2.04:1, the Start button 2.92:1, and difficulty is five identical stars differing only in colour; the audit's curated pair list omits these surfaces. *Fix:* Use token text colours with ≥4.5:1, add "3/5" text, and add these pairs to `a11y_audit.py`.
- **[F650] Most persisted settings have no consumer: graphics, high contrast, autosave, notifications, default speed do nothing** — `godot/scripts/ui/SettingsMenu.gd:112` (correctness). Only audio, `ui_scale`, `auto_pause_critical` and `tutorial_enabled` have readers; resolution/fullscreen/vsync/high-contrast toggle and persist but change nothing. *Fix:* Implement the consumers or hide the controls, and add a test that every `DEFAULT_SETTINGS` key has a reader.
- **[F666] Oxygen/lice overlays use company-global values: 0.0 (no data) paints every site critical red** — `godot/scripts/ui/SiteOverlayLegend.gd:270` (correctness). `oxygen_mgl` defaults to 0.0 and the formula clamps to 1.0, so the baseline shows both sites red at "0.0 mg/L"; per-site `oxygen_mgl`/`lice_per_fish` exist but are ignored. Display-only. *Fix:* Use the per-pen fields with a no-data state (grey swatch) and skip negative intensities.
- **[F678] Stakeholder and stock-market panels invent data with unseeded `randf()`** — `godot/scripts/ui/StakeholderPanel.gd:401` (correctness). No bridge methods exist, so the `has_method` fallbacks return random scores re-rolled each refresh, and the IPO is a local panel field that never reaches the sim. *Fix:* Return `{}` and render "not available" until real bridge methods exist, or gate a mock behind a debug flag with a seeded RNG.
- **[F673] Research effect tooltip formats every effect as a fractional percentage** — `godot/scripts/ui/TechTreePanel.gd:450` (correctness). `sustainability_score_bonus` is in points (3-12) but shown as "+300%…+1200%", and negatives render as "+-5%". *Fix:* Format by unit (points vs fractions, use `%+`) or ship a display string per effect from the bridge.
- **[F674] Research nodes are mouse-only and node text contrast is very low** — `godot/scripts/ui/TechTreePanel.gd:373` (correctness). Hit buttons use `FOCUS_NONE`, prerequisites appear only in a hover popup, and `modulate`-based text colours give ~1.1-1.3:1 on available/unlocked/active nodes. *Fix:* Make nodes focusable controls with tooltip text, chain focus neighbours, set explicit font colours, and add the state pairs to the audit.
- **[F675] Telemetry panel title and every section heading render near-invisible (no font colour)** — `godot/scripts/ui/TelemetryPanel.gd:798` (correctness). `_style_label()` never sets `font_color`, and the committed baseline `04_telemetry.png` shows ghost-white headings that were promoted as correct. *Fix:* Set `TEXT_PRIMARY` in the helper and re-promote the baseline.
- **[F665] ToastNotification swallows every unhandled Escape and permanently hides the toast layer** — `godot/scripts/ui/ToastNotification.gd:366` (correctness). `_input` handles `ui_cancel` and calls `hide()` on the `CanvasLayer`; nothing re-shows it, so after one or two ESC presses critical toasts are invisible for the session. *Fix:* Remove the handler (or use `_unhandled_input` only while toasts are active) and never hide the layer.
- **[F667] Toast eviction always drops the oldest toast regardless of severity** — `godot/scripts/ui/ToastNotification.gd:108` (correctness). The queue is drained on every call, so `SEVERITY_RANK` never chooses between waiting items and three later INFO toasts can evict a CRITICAL one. *Fix:* Evict only a lower-severity victim and leave the rest queued.
- **[F669] Completion flash mutates the theme's shared `PanelContainer` StyleBox** — `godot/scripts/ui/TutorialUI.gd:226` (correctness). Tweening `border_color` on the shared resource flashes every default card, and a killed tween leaves the green border globally. *Fix:* Duplicate the StyleBox and add it as a local override, using a separate tween variable.
- **[F671] Tutorial progress dots are sized for 23 steps (the track has 12) and never update** — `godot/scripts/ui/TutorialUI.gd:130` (correctness). `TOTAL_STEPS = 23` is hard-coded, `_update_progress_dots` is only called at completion, and the completion animation has reset/tween-kill bugs. *Fix:* Build dots from `tutorial_total_steps`, update in `_refresh_display()`, and reset `reward_label.modulate` in both branches.
- **[F679] Treatment rows show made-up effectiveness/cost and the order result is silently ignored** — `godot/scripts/ui/WeatherDiseasePanel.gd:493` (correctness). Four legacy treatments with fixed efficacy and cost multipliers replace `GetTreatmentProfiles()`; `OrderDelousing`'s false return and the other bool results are discarded, and alert expiry uses wall-clock timers. Panel is currently unreachable. *Fix:* Fill rows from `GetTreatmentProfiles()`, toast failures, clear the list after success, and key alert lifetime off `day_advanced`.
- **[F744] Wellboat, competitor-farm and boat-colour visuals are inert because they call APIs that do not exist** — `godot/scripts/visual/WellboatVisualManager.gd:44` (correctness). `get_all_wellboat_jobs`/`GetCompetitorCompanies` are not on `SimBridge`, `due_day` vs `eta_days` mismatch, and the code behind the guards has latent errors (`Object.get("day", 0)`, `["epoch"]`, ShaderMaterial cast, `global_position` before `add_child`). *Fix:* Bind to `SimBridge.wellboat_jobs`/`GameBridge.GetCompetitorCompanies`, add `push_warning` on every guard's else branch, and fix the Hull path and material duplication.
- **[F743] Feeding, harvest and bloom particles can never render or trigger** — `godot/scripts/visual/particles/ParticleEffects.gd:48` (correctness). No `draw_pass_1` mesh or material is set, the pen-manager path resolves to null, `_disable_feeder` fires on the next frame, and zone keys `PO01..` do not match scenario ids. *Fix:* Set `draw_pass_1` and the material, assign `fish_pen_manager = self` from `FishPenManager._ready`, and use a timer or `one_shot`.
- **[F964] Gerstner steepness divides by `freq*amp*N`: zero wind/amplitude/frequency produces inf/NaN and the water plane vanishes** — `godot/shaders/water_shader.gdshader:109` (correctness). `hint_range` allows 0 for the three uniforms and `FjordVistaBridge`/`SimState` can write `wind_strength = 0` at startup or headless. *Fix:* `max(freq * amp * float(wave_count), 1e-4)` and clamp the writers to a positive minimum.
- **[F787] `create_stat()`/`create_label()` defaults are unreadable on the dark panels from `create_styled_panel()`** — `godot/theme/default_theme.gd:768` (correctness). Dark text (`#2E3440`/`#4C566A`) on `PANEL #3B4252` is ~1.2-1.4:1, so DailySummary stats are effectively invisible. *Fix:* Make helper defaults surface-aware (new `TEXT_ON_PANEL_MUTED` token) or make the styled panel use `SURFACE`, and add PANEL pairs to the audit.
- **[F794] SuccessButton, DangerButton and MutedLabel variations fail WCAG AA, are unused, and sit outside the audit** — `godot/theme/default_theme.gd:554` (correctness). SuccessButton is 2.16:1, DangerButton 3.55:1, MutedLabel 3.66:1 at 11 px, and GhostButton's white 10% hover is invisible on the light theme. *Fix:* Use passing token pairs, a dark-alpha ghost overlay, add the pairs to `a11y_audit.py`, or delete the unused variations.
- **[F1008] The five mod JSON Schemas contradict `ModLoader` and the shipped example pack, and CI never runs them** — `schemas/mods/mod.schema.json:40` (correctness). The example manifest fails the file-name pattern (`scenarios_` vs `scenario_`) and both example events fail the category enum (`Weather` not allowed); the schema category list shares only `Market`/`Other` with the real `EventCategory`, and `effects` is a free object. `validate_mods.py` is a third, looser contract. *Fix:* Derive schema enums from the C# enums, fix the file pattern, restrict `effects` keys, and validate `godot/mods/*` with `jsonschema` in `step_validate_mods`.
- **[F808] Source flags and `dataConfidence` are computed from truthy dicts of `None`, so an offline run claims two live sources at 0.80 confidence** — `scripts/update_aquaculture_snapshot.py:202` (correctness). Failed fetches become `{'aquacultureRegister': None, …}` which are truthy, so `activeSourceFlags` and the fixed `sourceSummary` assert sourced data. Metadata-only (values are plausible defaults). *Fix:* Count a source active only if it holds real data, derive confidence from `active_sources`, and set `fallbackUsed` accordingly.
- **[F810] Snapshot index derivation relies on first-key-anywhere search over unvalidated payloads, proxy metrics and an SSB call that cannot return a price** — `scripts/update_aquaculture_snapshot.py:384` (correctness). `_search_numeric()` returns the first matching key at any depth, regional utilisation is a locality-count proxy, the SSB table call is a plain GET of metadata, and shape assumptions can raise uncaught `TypeError`. The real path was never run against live payloads. *Fix:* Read documented field paths explicitly, validate shapes, rename proxy metrics, use a PxWeb POST query, and cover with recorded fixtures.
- **[F078] `verify.sh` runs `git clean -fdq godot/` and permanently deletes untracked work** — `scripts/verify.sh:47` (correctness). When `godot-mono` is on PATH the script reverts the csproj and cleans all untracked, non-ignored files under `godot/` (new scenes, scripts, shaders) with stderr discarded; this contradicts `docs/MAC_SETUP.md`'s own warning. The script is a manually run orphan not wired into CI. *Fix:* Delete both lines, or run the boot smoke in a throwaway worktree and remove only known generated paths after a `git clean -n` preview.
- **[F858] No test for compliance on the deadline tick; production would liquidate the whole stock** — `src/LakskongenSim.Tests/Integration/MattilsynetOrderTests.cs:76` (correctness). In `SimulationEngine.cs:2849-2899` the deadline branch is evaluated before the compliance check; for an under-MAB site `harvestT` is negative, falls back to `biomassT`, and the entire stock is sold at 70% as `forced_harvest`. Needs the site to become compliant after the last pre-deadline tick. *Fix:* Add `ComplyOnDeadlineDay_NoPenaltyHarvest`; in production test `!site.IsOverMAB` first and harvest `CurrentBiomass - MAB` in the deadline branch.
- **[F896] Feed/growth accumulators double-count the pre-save segment after the round trip** — `src/LakskongenSim.Tests/Integration/PlayerScriptProbe.cs:120` (correctness). The harness assumes the resumed engine's counters start at 0, but `RestoreFromLoad` restores `TotalFeedUsedKg/TotalGrowthKg`, so ~474 t of feed is counted twice (EconomicFcr moves only 1.395→1.385). *Fix:* After resume set `lastFeedUsed = engine.Feed.TotalFeedUsedKg` and `lastGrowth = engine.Feed.TotalGrowthKg`, or read engine totals directly.
- **[F500] `harvest-window` fires on absolute farm biomass > 500 t, unreachable in shipped scenarios** — `src/LakskongenSim/Advisor/InterventionAdvisor.cs:108` (correctness). The farm-wide `> 500_000 kg` threshold ignores fish weight and market price: small scenarios (≤ ~240 t) never fire and large ones fire at ~1.6-2.8 kg (below `MinHarvestWeightKg`); gross revenue at a flat 80 NOK/kg is shown as `DeltaNok`. Advisory UI only. *Fix:* Decide readiness per fish against the calibrated min/target harvest weight, take price from the market, and report net or incremental gain.
- **[F364] Oxygen saturation table holds freshwater values labelled as 35 PSU seawater** — `src/LakskongenSim/Biology/WaterQuality.cs:38` (correctness). 14.6/11.3/9.0 mg/L at 0/10/20 C are freshwater (real seawater ≈ 11.5/9.0/7.4), so 100% saturation at 10 C is ~10.9 instead of ~9.0 and the 60% growth-penalty trips at ~6.5 mg/L. The effect is halved by the combined-multiplier damping. *Fix:* Use a Garcia-Gordon/Weiss seawater solubility with the salinity argument, drop the ad hoc 0.001 factor, and add a 10 C / 35 PSU ≈ 9.0 test.
- **[F1029] `StormEpisodeCalibration` is a dead group absent from the JSON, and its declared 0.5-3% band contradicts the active storm loss (0.03%)** — `src/LakskongenSim/Calibration/CalibrationRegistry.cs:408` (correctness). All 11 values exist only as code defaults and nothing reads them; the live loss comes from `Environment.StormFishLossBaseFraction = 0.0003` read via `Default`. *Fix:* Delete the group until the episodic model exists, or add it to the JSON, wire it, and add the band test.
- **[F1030] Calibration unit conventions are inconsistent and a live value sits outside its own band** — `src/LakskongenSim/Calibration/CalibrationRegistry.cs:264` (correctness). `…Percent` keys mix fractions and percents, `MortalityRateBasePercent = 0.025` documented as a daily fraction, `volatilityWeekly = 5.0 < volatilityMin = 5.5`, an oxygen unit mismatch, and a 360-day seasonality vs 365-day weather/finance calendar. *Fix:* Rename with explicit units, add `Validate()`, and use one calendar helper across market, weather and finance.
- **[F219] Four achievements are unreachable: they depend on GameState fields that gameplay never writes** — `src/LakskongenSim/Core/AchievementSystem.cs:265` (correctness). `BestFCR`, `BestGrowthBreedingValue` and `MinimumBalanceEver` are only assigned on load, so PerfectFCR, PrecisionFCR, GeneticSuperiority and BankruptcyRecovery (1.8M NOK of rewards) can never unlock. *Fix:* Update the records in the daily tick (`Math.Min(State.BestFCR, Feed.CurrentFCR)` etc.) and add one unlock test per achievement.
- **[F220] Several achievement conditions do not implement their descriptions** — `src/LakskongenSim/Core/AchievementSystem.cs:201` (correctness). "First Harvest"/"100K harvested" test `Stage == Harvest` (harvest-ready at age 730), not cumulative harvested; `ZeroEscapes` (2M NOK) and `SurvivedYearOne` are true for a farm that never stocked fish. The FCR 300-day rule is just "all days". *Fix:* Add cumulative harvest counters to `GameState`, require stocked days, and track last-escape/disease day as scalars.
- **[F221] "MortalityRate" is net head-count decline: harvests count as mortality** — `src/LakskongenSim/Core/AnalyticsTracker.cs:175` (correctness). Measured baseline ~0.05%/day jumps to 64% after a harvest and restocking hides real deaths; the value feeds the pen UI, alerts above 1% and exports. *Fix:* Record deaths explicitly (`DeathsToday`) and snapshot `deaths / start-of-day fish`, and move the snapshot to a final post-tick phase.
- **[F1056] `SludgeValorizationTickSystem` never advances its clock, so the weekly gate is dead** — `src/LakskongenSim/Core/InDayTickSystems.cs:412` (correctness). Unlike its siblings it never sets `_lastSludgeValorizationUpdate`, so valorisation ran on three consecutive days once 500 kg DS had accrued, paying fixed opex/revenue daily instead of weekly. *Fix:* Set the clock after `ProcessSludgeValorization()` and add a cadence test.
- **[F1009] Mod events apply only `balance_change`; `lice_change`, `mortality_change`, `sustainability_change` are accepted and ignored** — `src/LakskongenSim/Core/ModLoader.cs:418` (correctness). The schema, doc and example pack advertise four keys but `Apply` reads one, so a mod storm only costs cash. *Fix:* Implement the other keys or remove them from schema/doc/example and have the loader report unsupported keys.
- **[F1007] Mod scenarios silently drop `starting_fish_count`, `initial_events`, `starting_capital_nok`; the shipped example is won on day 1** — `src/LakskongenSim/Core/ScenarioSystem.cs:1189` (correctness). `RegisterModScenarios` maps only seven fields, and the example's `initial_balance_nok` equals its default 5M profit target, so it reports `IsWon` after `Advance(1)`. *Fix:* Map the dropped fields (starting site, events), add optional rule fields, and make the example's target exceed its balance.
- **[F1081] `ImportStates` silently drops positions of streams that do not exist yet** — `src/LakskongenSim/Core/SimRng.cs:80` (correctness). Lazily created streams (hire_worker, cohort transfer, RAS/offshore) restart at position 0 after load: a stream advanced three draws returns its first value again after import. *Fix:* Keep unmatched states in a pending map applied when `Stream()` first creates the stream, and log leftovers.
- **[F270] `HarvestSite` books full revenue but never charges the calibrated harvest cost** — `src/LakskongenSim/Core/SimulationEngine.Commands.cs:237` (correctness). `HarvestCostPerKgNOK` (4.0) is copied into `_balancing` but never read; the direct UI path is free and instant and the 150k wellboat hire is a flat fee, inflating profit by ~4 NOK/kg while `SimProbe` subtracts it. *Fix:* Spend `kg * HarvestCostPerKgNOK` (with the `Spend`→`ForceSpend` pattern), record COGS, and test cash delta = revenue − cost.
- **[F271] `AcquireDistressedAsset` charges the full price but delivers nothing for most asset types** — `src/LakskongenSim/Core/SimulationEngine.Licence.cs:425` (correctness). EQUIPMENT and PROCESSING_PLANT transfer nothing, licences append unread rows, farm sites land in a free-text zone id, and `Spend` runs even when the competitor is gone. Unreachable in live play today (nothing calls `TriggerCrisis`). *Fix:* Validate deliverability before charging, implement real effects, set a valid `ZoneId`, mutate first and `Spend` last.
- **[F279] `VerticalIntegrationCostSavingPerKg` has no consumer and its production estimate is dimensionally wrong** — `src/LakskongenSim/Core/SimulationEngine.Licence.cs:549` (correctness). It multiplies standing biomass by 365/elapsed days with a magic 10M kg fallback, nothing applies the saving while monthly OPEX is paid, and completion tests use `DateTime.Now`. *Fix:* Consume it in the COGS path using trailing harvested kg, and replace wall-clock completion with sim days.
- **[F037] Unchecked `Spend` lets costs vanish while P&L, events and insurance still book them** — `src/LakskongenSim/Core/SimulationEngine.cs:1803` (correctness). The storm repair bill is skipped when broke yet insurance is claimed on it; night-shift O2/premium spends ignore the result; `ExecuteAnnualClose` books `TaxPaid` without checking `Spend(totalTax)`. *Fix:* Use the `Spend`-else-`ForceSpend` pattern (or one `Pay` helper), insure only what was charged, and charge the AUTO-ROC misfire.
- **[F296] Weather-consequence switch forces a Storm for any unlisted event, including `perfect-conditions`** — `src/LakskongenSim/Core/SimulationEngine.cs:2382` (correctness). `jellyfish-swarm` and the positive `perfect-conditions` hit the default branch and trigger storm repair bills, a Storm insurance claim and net wear. *Fix:* Make the default a logged no-op, list every weather id explicitly, and add a table-driven coverage test.
- **[F299] Emptied (fallow) sites keep frozen lice that infect neighbours and are re-seeded while empty** — `src/LakskongenSim/Core/SimulationEngine.cs:2489` (correctness). Sites with no fish are skipped, so their last adult-female count keeps feeding neighbours at coupling 0.4 and restocking starts with the frozen load. *Fix:* Decay or clear infection on empty sites (keeping resistance), exclude them from inflow, and test fallow behaviour.
- **[F301] Freshwater-bath execution ignores `TreatmentResult.Success`, `Cost` and `GillDamage`** — `src/LakskongenSim/Core/SimulationEngine.cs:1514` (correctness). Outside 4-16 C `Treat` returns `Cancelled`, yet the engine charges 60k, logs "Ferskvannsbehandling" and reports success; gill damage and the gill-aware mortality multiplier are skipped. *Fix:* Route through `ApplySiteDelousing` and report `CancelReason`.
- **[F305] Player "market share" divides standing biomass by national weekly harvest** — `src/LakskongenSim/Core/SimulationEngine.cs:2453` (correctness). A stock is compared with a flow, so ~5,000 t standing reads as ~21% and milestones/victory gates (21%/30%/50%) are far too easy. *Fix:* Track a rolling 52-week harvested tonnage against annual national volume and move 1.25M t into the registry.
- **[F314] AutomatedROC night policy charges money and claims mitigation that does nothing** — `src/LakskongenSim/Core/SimulationEngine.cs:1915` (correctness). 12k per site-night is spent and "diffuser startet" logged without any O2 or mortality change; the bloom branch only logs detection and `severity` is unused. *Fix:* Implement the effect and charge only when acting, or log honestly as detection-only and remove the charge.
- **[F1033] Temperature mortality uses hard-coded 4/16 degC thresholds and is 100x weaker than its comment** — `src/LakskongenSim/Core/SimulationEngine.cs:1834` (correctness). `tempStress*0.005` yields 0.005%/degC/day, not "0.5% per degree", and the weather clamp at 19 C makes the registry's 22 C lethal limit unreachable. *Fix:* Read bounds from the calibration, make the magnitude explicit, and widen the clamp.
- **[F1075] Sustainability score/certification are saved stale** — `src/LakskongenSim/Core/SimulationEngine.cs:3727` (correctness). `State.SustainabilityScore` is refreshed weekly while a regulatory penalty changes the live manager immediately, so a penalised player's certification reverts on load and a spurious change fires. *Fix:* Re-sync from `SustainabilityManager` at the top of `PrepareForSave` and make the certification price update use the full formula.
- **[F1093] Schema grew inside save version 3 without a bump; "Runtime != null" is treated as "complete"** — `src/LakskongenSim/Core/SimulationEngine.cs:3732` (correctness). A real save from commit 264c205 loaded with current code resets `FinancialStatements` to zero with no warning, and a newer enum name on an older build is reported as corrupted JSON. *Fix:* Bump `CurrentSaveVersion` for each additive runtime change, add per-section nullable payloads, and test historic golden saves.
- **[F1140] Storm insurance pays against a repair bill that was skipped because the player could not afford it** — `src/LakskongenSim/Core/SimulationEngine.cs:1801` (correctness). The repair `Spend` is guarded by `Balance >= cost` but the claim includes `repairCost` unconditionally (also for unpaid Storyteller disease losses). *Fix:* Insure only the amount actually debited, or `ForceSpend` the repair.
- **[F319] `Skip()` is not persisted, so a save/load after skipping resurrects the tutorial** — `src/LakskongenSim/Core/TutorialSystem.cs:287` (correctness). It sets only the private index, not `State.TutorialCurrentStepIndex`/`CompletedTutorialStepIds`, so reload returns to the pre-skip step and its NOK rewards pay for the first time. *Fix:* Make `GameState` the single source of truth and write both fields (or a `TutorialSkipped` flag) in `Skip()`.
- **[F077] DeterminismBanTests fails on the working tree: 8 new wall-clock reads in RealData** — `src/LakskongenSim/Data/RealData/Layers/CurrentLayer.cs:146` (correctness). Seven layers set `LatestData = DateTime.UtcNow.AddDays(-n)` and the orchestrator stamps `RetrievalDate = DateTime.UtcNow`, so the central guard fails and 9 allowlist lines are stale. The values are metadata only, so no sim state is affected. *Fix:* Take an injected clock/`asOf` date or fixed constants, and delete the stale allowlist lines.
- **[F203] RealData seasonality is applied twice, giving implausible magnitudes and large month-boundary steps** — `src/LakskongenSim/Data/RealData/Layers/WindLayer.cs:205` (correctness). The monthly table already encodes season and a second sinusoid is added: Jan wind ~13.8 m/s, a step from 13.03 to 5.80 across 31 Mar/1 Apr; Waves/Chlorophyll/Sst repeat the pattern. Nothing consumes the stack today. *Fix:* Remove the second term, interpolate cyclically with the already-instantiated `TemporalInterpolator`, and assert absolute bands.
- **[F432] `DataPoint.Location` is silently lost on every disk-cache round trip** — `src/LakskongenSim/Data/RealData/Models/DataPoint.cs:13` (correctness). `GeoCoordinate` is a get-only readonly struct without `[JsonConstructor]`, so a point served from disk comes back at (0, 0). Latent (no loader populates the cache yet). *Fix:* Add `[JsonConstructor]` to the struct constructor or store flat lat/lon/depth, with a `DataCache` round-trip test.
- **[F201] S4 deliverable is a facade: no snapshot data, no loaders, and no consumer** — `src/LakskongenSim/Data/RealData/RealDataOrchestrator.cs:31` (correctness). Every layer gets an empty loader list, no snapshot JSON exists, nothing outside `Data/RealData` references the stack, yet RUN_LOG and the v2 test header claim offline snapshots. *Fix:* Deliver the snapshot loader plus an engine adapter and a player-path test, or rename the layers `Climatology*` and re-scope the progress claim.
- **[F431] Provenance and source labels claim CMEMS/ERA5/BarentsWatch data and licences for hard-coded climatology** — `src/LakskongenSim/Data/RealData/RealDataOrchestrator.cs:124` (correctness). `GetProvenance` returns `Available = true`, `RetrievalDate = UtcNow` and licence text although nothing is fetched, and the test asserts the false claim; synthetic values get `QualityFlag` 1. *Fix:* Add a `Kind` (Live/Cached/ClimatologyFallback/Synthetic) driven by what the layer did, and move target-source text to an "intended source" field.
- **[F458] `BloomEvent.Id` defaults to `Guid.NewGuid()`, and the engine spawns blooms without setting it** — `src/LakskongenSim/Environment/BloomEvent.cs:12` (correctness). Same-seed runs diverge in `ActiveBlooms[0].Id` whenever a bloom spawns in a save-hash window (the shipped windows pass only because none does). Id affects UI/dedupe, not sim values. *Fix:* Set `Id = $"bloom_{State.Day}_{site.Id}_{kind}"` in `TrySpawnBloom`, drop the allowlist entry, and add an events-on hash test covering a spawn.
- **[F460] Zone water temperature is never fed from the Weather system** — `src/LakskongenSim/Environment/EnvironmentModel.cs:25` (correctness). Zones stay at the 10 C default, so chemistry kinetics, bloom temperature factor and the DO-saturation growth multiplier ignore seasons while growth uses weather temperature. *Fix:* Pass sea temperature into `EnvironmentModel.AdvanceDays` and call `z.SetTemperature` before stepping.
- **[F461] `Interpolate` drops the derived stress indices, so `EnvironmentalQualityScore` is 0 on nearly every day** — `src/LakskongenSim/Environment/EnvironmentalDataPoint.cs:167` (correctness). Only the first day of a season has indices, so the Mareano growth multiplier floors at 0.5 and the risk multiplier saturates at 2.0. Reach limited to the advisor/demo. *Fix:* Call `CalculateDerivedIndices()` on the interpolated result and test mid-season quality.
- **[F464] Domain warp is uncorrelated white noise, so terrain heights are speckled** — `src/LakskongenSim/Environment/FastNoise.cs:57` (correctness). `GetWarpNoise` hashes integer cells with no interpolation (lag-1 autocorrelation ≈ 0.03 vs ≈ 0.98 without warp), and the ridged output is never negative, adding a ~25 m lift. *Fix:* Use smooth noise for the warp at a small amplitude, remap the ridged signal to zero-mean, and regenerate baselines.
- **[F469] Fish oxygen uptake is 4-5 orders of magnitude too small** — `src/LakskongenSim/Environment/WaterChemistry.cs:84` (correctness). `biomass * 0.1 / volumeL` lacks the g→mg factor used for ammonia, so biomass has no effect on DO. *Fix:* Use the calibrated per-kg constant with the same unit normalisation (noting the registry's own 95 mg/kg/day is also low) and test DO falls with biomass.
- **[F459] `UpdateWaveHeight` is never called, so wave height is frozen at 0.5 m** — `src/LakskongenSim/Environment/WeatherSystem.cs:283` (correctness). Offshore wave stress and the UI readout never reflect storms. *Fix:* Make wave height a pure function of wind and state (no RNG) and call it after `UpdateWind`.
- **[F470] `StormRiskNext3Days` returns the day-3 state probability, not the probability within 3 days** — `src/LakskongenSim/Environment/WeatherSystem.cs:244` (correctness). From Calm it gives 0.26 vs 0.36 for "at least one storm day", understating the forecast shown to the player. *Fix:* Compute first-passage probability over the transient states and share the row builder with the sampler.
- **[F455] Nutrient load has wrong units and magnitude, so any zone above ~700 kg of fish ends in a permanent algal bloom** — `src/LakskongenSim/Environment/Zone.cs:88` (correctness). `biomass * 0.05 / flow` is added directly to mg/L (≈1000x too high); 10 t blooms by day 4. Downstream damage is muted (the penalty method has no callers), but the O2 draw and plankton nutrient factor saturate. *Fix:* Use the same mass balance as `WaterChemistry` in mg/L and derive dissipation from exchange rate.
- **[F513] `competitor-bankruptcy` charges 1.2M and increments `AcquisitionCount` without acquiring anything** — `src/LakskongenSim/Events/NorwegianEvents.cs:854` (correctness). It is the only writer of the counter that feeds empire milestones, the AcquisitionSpree achievement and `pe-buyout-offer`. *Fix:* Turn it into a notification that opens a real distressed-asset offer and increment the counter only in the real acquisition command.
- **[F514] Equity/exit events narrate dilution and choices but just credit 1.5-12M NOK cash** — `src/LakskongenSim/Events/NorwegianEvents.cs:944` (correctness). esg-fund, angel, pe-buyout, ma-offer and ipo-opportunity auto-settle large positive balances with no shares issued or decision offered. *Fix:* Route through `InvestorSystem` or a player choice, and scale payouts to valuation.
- **[F509] Default events record `BalanceChange` before `Spend` succeeds** — `src/LakskongenSim/Events/Storyteller.cs:169` (correctness). On insufficient funds the log shows a loss that never happened and disease insurance may pay on it. *Fix:* Record only what was settled (`paid ? -cost : 0m`) or `ForceSpend`, via one shared helper with a low-cash test.
- **[F520] No production code calls `CheckMilestone`, so no visual milestone can be achieved** — `src/LakskongenSim/Events/VisualMilestones.cs:146` (correctness). The method also awards any type unconditionally and stamps `DateTime.Now`. *Fix:* Evaluate thresholds from statements/state in the daily tick, store `AchievedDay`, and add a first-harvest test.
- **[F545] `MakeOffer` compares a NOK-millions price against NOK cash, so the affordability gate never trips** — `src/LakskongenSim/Expansion/AcquisitionSystem.cs:101` (correctness). 225 > 10,000,000 is false, and the bridge ignores `Spend`'s result, removing the target and logging a debit that never happened. No UI caller yet. *Fix:* Make units explicit (`offeredPriceMNOK * 1_000_000m > cashNOK`) and own the transaction in the engine.
- **[F040] Vertical integration is unreachable in the live game and would use wall-clock time** — `src/LakskongenSim/Expansion/VerticalIntegrationSystem.cs:178` (correctness). `LoadAssetsFromData` is only called by tests, so `PurchaseAsset` always returns "not found"; construction uses `DateTime.Now`, `RestoreOwnedTier` leaves completion null, and AGENTS.md calls Phase 4.4 complete. *Fix:* Load the catalogue in `Initialize`, store sim-day start/complete and persist them, and correct AGENTS.md.
- **[F387] AI feeding's FCR improvement, waste savings and feed plans are computed but never applied** — `src/LakskongenSim/Farm/AIFeedingSystem.cs:209` (correctness). `FCRMultiplier`, `DailyFeedSavings` and `GenerateFeedPlan` have no consumers; the only live effect is `spreadQuality + 0.1` (~2% FCR) against a 100M NOK install. *Fix:* Apply the multipliers in the feeding call (without double counting), delete unused plan code, and add a probe asserting the FCR drop.
- **[F390] Camera cleaning is overwritten next tick and the maintenance alert is unreachable** — `src/LakskongenSim/Farm/AIFeedingSystem.cs:198` (correctness). `CameraUptime` is recomputed from `DaysSinceInstallation % 30`, minimum 0.565, so `< 0.5` never fires; no UI calls `clean_ai_cameras`. *Fix:* Persist `DaysSinceCleaning`, derive uptime from it, make the threshold reachable and fix the `/360` vs 180-day comment.
- **[F391] `ExecuteTransfer` validates nothing before mutating** — `src/LakskongenSim/Farm/CohortTransferSystem.cs:111` (correctness). The MAB check uses the caller-declared weight, cost is charged after the move with `ForceSpend`, fish are taken from the first cohorts regardless of grading, and `arrived <= 0` leaves the cohort unremoved. *Fix:* Validate inputs and funds first, compute actual biomass from moved cohorts, select by weight, and drop the unused `Random`.
- **[F403] `FeedSystem.SustainabilityRating` never reaches the sustainability score; appetite curve has a 2x cliff at 2 C** — `src/LakskongenSim/Farm/FeedSystem.cs:80` (correctness). Recipe choices don't move the score, `CalculateAppetite` jumps from 0.3 to 0.6 at 2.0 C, and `CalculateFCR` mutates state and raises events. *Fix:* Push the rating into `State` each tick, make the appetite curve continuous, and make `CalculateFCR` pure.
- **[F392] Red-zone MAB credit is a bool, so "exact revert" drifts after a containment upgrade** — `src/LakskongenSim/Farm/SeaSite.cs:33` (correctness). Upgrading while red skips the credit, then exit divides by the new level's credit, permanently losing ~2.8% MAB; `EnvironmentalFlexibility.MabMultiplier` models a different rule and has no production caller. *Fix:* Store the applied factor and revert with it, and delete or align the unused API.
- **[F1136] Annual close taxes income that the monthly path already taxed, then resets the P&L** — `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:48` (correctness). `ComputeNetIncome()` (after monthly taxes) feeds `ComputeTotalTax`, so one measured profitable year paid 39.2% of profit; in six other closes the annual charge was 0 only because net income was ≤ 0 or the loss stock covered it. *Fix:* Use one tax regime: compute the annual liability from pre-tax income minus provisional payments and drop one of the two charges.
- **[F1139] Covenant tests run after `AnnualClose()` zeroed the P&L, use net income as "EBITDA", and the equity-ratio covenant always fails** — `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:64` (correctness). Interest and taxes read 0, `netDebt` omits loans, `Assets/Liabilities` are never populated, so Equity Ratio failed 23/23 tests and Leverage 14/23; breaches grow ~9/year. They have no gameplay consequence yet. *Fix:* Compute inputs before `AnnualClose`, define EBITDA properly, maintain assets/liabilities or drop the covenant, and de-duplicate by name.
- **[F343] Covenant breaches accumulate without dedupe or resolution; `BreachDay` is never set** — `src/LakskongenSim/Finance/CovenantSystem.cs:78` (correctness). Every failed test appends a record, `ResolveBreach` has no callers, and "N active" only grows and is serialised. *Fix:* Keep one open breach per stable covenant id, set the day, and close on pass.
- **[F1079] Culture-dependent strings are persisted into saved state** — `src/LakskongenSim/Finance/FinancialStatements.cs:89` (correctness). `AddEvent($"COGS -{amount}")` and EventLog text use the current culture, so the same seed hashes differently under de_DE vs en_US. Numeric values are unaffected. *Fix:* Format persisted/log strings with `CultureInfo.InvariantCulture` and add a two-culture determinism test.
- **[F1057] Stateful sub-models are outside the save snapshot: tax loss carry-forward, market price window, disease prevalence** — `src/LakskongenSim/Finance/TaxSystem.cs:88` (correctness). `TaxSystem.ExportState` has no callers (a 33.3M loss carry-forward is 0 after load), `_priceHistory` restarts with the new game's price, and `DiseaseSystem._infected` has no export. *Fix:* Add these to `EngineRuntimeSnapshot` and extend the save-hash test to compare observable outputs.
- **[F483] Treatment-harm compounding never reaches mortality, and prior load is decayed twice** — `src/LakskongenSim/Health/DelousingHarmModel.cs:169` (correctness). The live path uses stateless `EvaluateFor`, so repeat treatments kill the same fraction; `CurrentMortalityRate` has no production callers and `CompoundInjury` decays the already-decayed load again. *Fix:* Pass prior state into the evaluation, feed `CurrentMortalityRate` into the mortality model, and remove the second decay.
- **[F484] Treatment, turbidity and current can never be reported as the dominant gill-risk reason** — `src/LakskongenSim/Health/GillHealthWarning.cs:93` (correctness). `best` starts at 0.2 and the maximum treatment/turbidity/current contributions are 0.20/0.15/0.10, so the player sees "No significant gill-health risk" next to a blended warning. *Fix:* Track the true maximum from 0 and apply the threshold relative to total risk.
- **[F479] `SetExternalDiseasePressure` has no effect on the live sim, and outbreak events cannot raise prevalence** — `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:391` (correctness). The multiplier is read only in private never-called fidelity methods, and `ApplyDiseaseConsequence`'s `Update(…, 3)` adds SIR steps at the equilibrium. The ISA scenario's 1.70 pressure is inert. *Fix:* Add an external multiplier to `DiseaseSystem.Tick`, forward the setter, and add a `SeedOutbreak` method.
- **[F481] Two independent `DiseaseSystem` instances: scenario starting-disease seeds the one nothing reads** — `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:58` (correctness). `ScenarioSystem` seeds `engine.Disease` while the pipeline ticks the private `_baseSystem`; the shipped bridge also calls `ApplyStartingState(state)` without the engine. `CampaignScenarioTests:218` passes against the orphan. *Fix:* Expose seeding on the integrated system, remove or alias `engine.Disease`, and point the test at `IntegratedDisease.GetPrevalence`.
- **[F1077] Two more allowlisted per-process-random `HashCode.Combine` sites produce zone distances that differ every run (dormant)** — `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:337` (correctness). The po3 pair distance was 24.67/10.57/6.96 km across processes; it is unreachable today, and `Math.Abs(hash)` can throw on `int.MinValue`. *Fix:* Derive distance from coordinates or a stable FNV hash, delete the dead fidelity code, and remove the allowlist lines.
- **[F482] `WindowDaysLeft` reports 30 days left for the whole pre-October period** — `src/LakskongenSim/Health/VaccinationProgram.cs:22` (correctness). Only `doy > 304` returns -1, so the HUD says the window is open for ~75% of the year while `VaccinateSite` refuses. *Fix:* Return -1 outside `[start, end]`, add boundary tests, and document whether the DOY constants are 0- or 1-based.
- **[F546] `GetROIAnalysis`, `GetCashFlowForecast` and `GetScenarioComparison` always throw `InvalidCastException`** — `src/LakskongenSim/Integration/DashboardDataProvider.cs:149` (correctness). Money is stored as boxed `double` but unboxed as `(decimal)`, so three of eight dashboard methods can never succeed; the panels are not instantiated by any scene yet. *Fix:* Keep decimals in typed records and convert once at the dictionary boundary, plus a smoke test per public method.
- **[F954] Dashboard bridge getters return synthetic values under real-sounding keys** — `src/LakskongenSim/Integration/DashboardDataProvider.cs:38` (correctness). `revenue` is the cash balance, history is `sin()`-generated, tax is a literal 0.22, FCR is a placeholder 1.2, and scenarios are fixed multipliers. Latent while the panels are unreachable. *Fix:* Source P&L from `FinancialStatements`, history from analytics, rates from calibration, or drop the getters.
- **[F522] `MisfireChance` gets morale (0-100) where it expects skill (0-10), so misfires never happen** — `src/LakskongenSim/Logistics/NightShiftPolicy.cs:41` (correctness). At morale 80 the chance is 0.12·(1−8) = −0.84, so the AUTO-ROC misfire (−25k NOK, −5 morale) can fire only with an empty crew; the UI shows a negative percentage. *Fix:* Take a normalised 0-1 morale and pass `Average(Morale)/100.0` from both call sites.
- **[F525] AutomatedROC fatigue is applied every night but never recovers; `ResponseMult`, `ScanOnlyEfficacy` and `FatigueRecoveryPerDay` are dead** — `src/LakskongenSim/Logistics/NightShiftPolicy.cs:25` (correctness). Nothing ever raises `Morale`, so 12 nights pin the crew at the floor of 10 even after switching back. *Fix:* Add recovery for non-auto policies using the constant, or delete the unused members.
- **[F417] `GenerateOffers` reputation bonus has an undefined unit; the real call site makes it a no-op** — `src/LakskongenSim/Market/ContractSystem.cs:119` (correctness). `brandReputation / 200.0` receives 0..1 from the bridge (≤ +0.5%) but would give +50% for a 0..100 score. *Fix:* Take an explicit 0..1 value and compute `rep01 * MaxReputationOfferBonus` with the cap in the registry.
- **[F406] Supply/demand offset is integrated into the persisted spot every day** — `src/LakskongenSim/Market/MarketSimulation.cs:384` (correctness). A leaky accumulator (steady state 10x its input) is added to the stored base price, pinning the spot to the 74/90 band edges on ~23% of days (vs ~15% when transient) and crashing to the 42 floor after a material shock, then snapping back in one day. Outside shocks the price stays in band. *Fix:* Keep the offset out of persisted state (`Base = clamp(current + drift + diffusion)`, realized = base + transient offset) and add band-occupancy and step-size tests.
- **[F523] Save/restore drops half of `BrandReputation`** — `src/LakskongenSim/Processing/BrandReputation.cs:46` (correctness). `BrandReputationState` omits Reliability, Innovation, Awards, History and markets, so `ReliabilityReputation` (which the weekly tick drives to ~100) resets to 50 and `OverallScore` drops up to ~10 points on load. Only UI and one scenario goal consume it. *Fix:* Persist the missing fields (cap `History`) and add a reflection-based "every settable property is persisted" test.
- **[F527] `ProcessingSnapshots` is never populated, so tutorial steps gated on it can never complete** — `src/LakskongenSim/Processing/ProcessingSnapshot.cs:4` (correctness). First-harvest, first-sale and P&L-report steps require `ProcessingSnapshots.Count >= 1`, which nothing creates, so the tutorial stalls at step 8 (Skip still works). *Fix:* Create a snapshot on harvest sale or base the conditions on real events.
- **[F530] `ProductMixOptimizer` sums per-kg profit as "NOK/day" and allocates capacity to loss-making products** — `src/LakskongenSim/Processing/ProductMixOptimizer.cs:126` (correctness). Totals ignore kg, allocation ignores sign, `DemandMultiplier` is unused (the replica showed ≈63 printed vs ≈−0.5M true). Nothing in production calls it. *Fix:* Sum `profit * kg`, exclude non-positive products, cap by line capacity and demand, and share the cost function with `ProductionLine`.
- **[F467] Score bonuses key on EventLog id substrings and a legacy flag nothing sets consistently** — `src/LakskongenSim/Sustainability/SustainabilityManager.cs:218` (correctness). No `EventRecord` id contains "mechanical"+"delous" or "fallow", and `IsClosedContainment` is set only at ClosedFloat and above, so those bonuses never apply. *Fix:* Use explicit state (`LastMechanicalDelouseDay`, fallow day, `ContainmentTech.IsClosedContainment`) and add one player-path test per bonus.
- **[F468] The research sustainability bonus never reaches the score** — `src/LakskongenSim/Sustainability/SustainabilityManager.cs:166` (correctness). Both call sites use `UpdateScore(State)` with the default 0.0 and `SustainabilityScoreBonus` is never read, so eight +3 to +12 research nodes do nothing. *Fix:* Pass `SustainabilityScoreBonus` at both sites and add a test that unlocking a node raises the score.
- **[F816] Muted text is audited against the 3:1 "large text" threshold although the UI renders it at 11 px** — `tools/a11y_audit.py:125` (correctness). `text-muted` on surface passes at 3.66:1 though `AttentionInbox.gd:324` uses it at `FONT_XS`; on the app background it is 3.18:1. *Fix:* Audit it at 4.5:1 against both backgrounds and darken the token (e.g. `#5F6B7A`).
- **[F817] CVD simulation applies its matrices to gamma-encoded sRGB although the comment says linear** — `tools/a11y_audit.py:92` (correctness). The matrices are the Coblis-style ones, and a linear-space Machado pass would drop success/warning ΔE from 26.5 to ~12.9 (below the 15 threshold; from memory of the published tables, so verify). WARNs do not fail the run. *Fix:* Linearize first, apply a cited matrix, convert to Lab, and test that greys are invariant.

#### Security

- **[F071] Visual-QA and design-token workflows cannot run as written, and run PR code on a self-hosted Mac** — `.github/workflows/visual-qa.yml:32` (security). `pip install -r requirements.txt` has no root requirements file, `visual_qa`/`design_token_sync` are not on `sys.path`, the design-tokens comment step reads `payload.pull_request` on a dispatch-only event, and the `createComment` calls are not awaited. The repo is currently private, so fork-PR exposure is theoretical; `ci.yml` also lacks a `permissions:` block and pins actions by tag. *Fix:* Fix the requirements/PYTHONPATH and awaits, restrict self-hosted jobs to same-repo PRs or use hosted runners, and set `permissions: contents: read` with SHA-pinned actions.
- **[F1123] MCP servers run via unpinned `npx -y` and from an out-of-repo `uv` checkout; configs hard-code a personal absolute path** — `godot/.mcp.json:6` (security). `@npgamedev/godot-mcp-server` is launched with no version or integrity pin and inherits the shell environment (including `BARENTSWATCH_TOKEN`), and `opencode.json`/`tools/blender_mcp_call.py` run unreviewed code from `/Users/fredrik.j.hansen/blender_mcp`. A compromised package would run with developer credentials on the next editor start. *Fix:* Pin an exact version with a lockfile (`npm ci --ignore-scripts`) or vendor it, scrub the child environment, and reference the Blender checkout via `${HOME}`/env var with a pinned ref.
- **[F965] macOS export preset ships dev tooling: MCP toolkit, `ziva_agent`, 31 MB of screenshots and `extension_api.json` inside the .pck** — `godot/export_presets.cfg:11` (security). With `export_filter="all_resources"` and empty excludes the 84 MB pck contains 127 `godot_mcp_toolkit` entries, `ziva_agent.gdextension` (libraries absent) and 56 screenshot PNGs; `MCPRuntimeServer` self-destructs outside the editor, so protection rests on one guard. *Fix:* Set `exclude_filter="addons/godot_mcp_toolkit/*, addons/ziva_agent/*, screenshots/*, extension_api.json, opencode.json, .mcp.json"`, track the preset, and add a CI check that lists the PCK for those paths.
- **[F773] Untrusted chat text and player names are parsed as BBCode** — `godot/scripts/multiplayer/MultiplayerLobby.gd:291` (security). `_log_chat` feeds remote text to `RichTextLabel.append_text()`, so a peer can inject `[url]`, `[img]`, size/effect tags or forged `[SYSTEM]` lines; remote colours go to `Color.html()` and chat length is uncapped. The network layer is not yet wired. *Fix:* Append untrusted text with `add_text()` (or escape `[`), cap to 500 chars, validate colours with `Color.html_is_valid`, and trim history.
- **[F812] Mod validator does not enforce that manifest `files` stay inside the mod folder** — `tools/validate_mods.py:115` (security). `mod_path / file_path` lets `..` and absolute paths pass ("All mods valid!"); `ModLoader.cs:562` uses the same `Path.Combine`, so a hostile manifest can make the game read and parse arbitrary local JSON. *Fix:* Reject absolute paths and any path whose resolved form is not under the mod root, in both the validator and the C# loader, with a negative fixture.

#### Performance & Scalability

- **[F1122] Parallel balance-probe workers rebuild the project per career and race on a shared runner directory** — `.claude/skills/ecc/sim-balance-probe/sim_balance_probe/runner.py:112` (performance). Up to eight workers each run `dotnet build` and rewrite the same `artifacts/balance_probe/runner` files, so MSBuild lock collisions surface as failed seeds that skew CI bands, and each 1200-day career pays about two builds. *Fix:* Build the runner once in the parent and have workers only `dotnet exec <dll>`, or give each worker a unique runner dir, and add a parallel-run test.
- **[F732] Listing saves deserialises every full `GameState` just to show name, day and cash** — `godot/scripts/autoload/SaveLoadManager.cs:536` (performance). `GetSaveFileInfo` parses the whole `SaveGameData` per slot on the main thread each time the menu opens, growing with save size. *Fix:* Read only a header via `Utf8JsonReader` or write a `.meta.json` sidecar at save time.
- **[F132] Every simulated day triggers four full UI refresh rounds across ~50 subscribers; hidden panels rebuild too** — `godot/scripts/bridge/SimState.gd:181` (performance). Both `GameStateUpdated` and `SimulationTicked` run `_refresh_state` (59 reads plus 11 collection-building bridge calls), which emits two signals, and panels without a visibility guard rebuild; ~20 rounds/s at 5x speed (unmeasured). *Fix:* Emit one notification per advance with a dirty flag, gate panels on `is_visible_in_tree()`, and return a single snapshot dictionary or count properties.
- **[F776] Refresh builds full Godot arrays only to call `.size()`, polls stub accessors, and `get_all_pens()` is rebuilt by many panels per tick** — `godot/scripts/bridge/SimState.gd:282` (performance). About ten empire polls return constants and `get_all_pens()` is uncached (Dashboard calls it twice per refresh). *Fix:* Add count properties in C#, poll empire state only when enabled, and cache `get_all_pens()` by a refresh serial.
- **[F135] Per-frame GDScript animation of up to 2,880 fish transforms** — `godot/scripts/environment/FishPenManager.gd:454` (performance). `_animate_fish_school` runs for every visible instance with no LOD, distance or frustum gating (code-size estimate, not profiled; unstocked pens cost nothing). The `TutorialState` per-frame poll is deliberate and cheap. *Fix:* Move swimming to a vertex shader driven by `TIME`, or update at 20-30 Hz and skip off-screen pens.
- **[F748] Terrain build still hitches the main thread; unused collision shape; blocking join on exit** — `godot/scripts/environment/FjordTerrain.gd:194` (performance). Mesh construction (65k vertices) and a ~390k-vertex `ConcavePolygonShape3D` run on the main thread, nothing queries the collider, `_exit_tree` blocks on `wait_to_finish()`, and rebuild frees all children including authored ones. *Fix:* Build arrays in the worker, drop or coarsen the collider, add a cancel flag, and free only own nodes.
- **[F576] Interactive lists are rebuilt wholesale on every state tick, including while hidden, and clicks can be lost** — `godot/scripts/ui/ContractsPanel.gd:61` (performance). Contracts, Crew and AttentionInbox free and recreate all children on every `state_updated` (2-4x per tick), so a press spanning a rebuild never fires. *Fix:* Gate on `is_visible_in_tree()`, coalesce with a dirty flag, and rebuild only when the data signature changes.
- **[F597] Every cached panel runs a full refresh twice per sim update, including while hidden** — `godot/scripts/ui/Dashboard.gd:101` (performance). Dashboard and FinancePanel subscribe to both `state_updated` and `day_advanced` with no visibility check, doing ~10 analytics calls and node rebuilds each time. *Fix:* Subscribe once, mark dirty, refresh only when visible and on `on_panel_shown()`, and cache per-tick series.
- **[F672] Tech tree is destroyed and rebuilt on every `state_updated` and every ~2 s from `_process`, even while hidden** — `godot/scripts/ui/TechTreePanel.gd:117` (performance). ~250 controls churn constantly, so hover/click state is lost and the shared tooltip can stay stuck. *Fix:* Drop the `_process` poll, gate on visibility, and update node panels in place keyed by id.
- **[F676] Telemetry panel rebuilds fully four times per sim tick and also while hidden** — `godot/scripts/ui/TelemetryPanel.gd:36` (performance). Both signals call `refresh()`, which clears the root, rebuilds 9 sections and calls `get_all_pens()` 3-4 times. *Fix:* Connect only `state_updated`, coalesce with a dirty flag, skip when hidden, and hoist `get_all_pens()`.
- **[F670] Tutorial settings file is read, parsed and not null-checked on every `state_updated`** — `godot/scripts/ui/TutorialUI.gd:302` (performance). `_tutorial_setting_enabled()` does synchronous file I/O and JSON parsing several times per tick and dereferences a possibly-null `FileAccess`. *Fix:* Read once in `_ready()`, refresh on a settings-changed signal, and guard `open()`.
- **[F681] `show_empty_state()` leaks an orphan `Label` on every call when a theme is passed** — `godot/scripts/ui/UIPolish.gd:92` (performance). A plain label is created before the themed branch and never added or freed; Dashboard re-renders its empty state on every refresh. *Fix:* Create the plain label only in the fallback branch.
- **[F971] Always-on full-screen tilt-shift costs up to 35 texture fetches per pixel** — `godot/shaders/tilt_shift.gdshader:27` (performance). A 17-iteration loop with 2 non-LOD `texture()` reads runs per pixel inside a per-pixel divergent branch on a mipmapped screen copy, even when an opaque GameUI tab covers the view. *Fix:* Use 2-3 `textureLod()` taps (lod from amount), or a half-res separable blur, and hide the rect behind full-screen tabs.
- **[F796] ScenarioGoalHUD marshals the whole scenario roster across the C# bridge on every sim tick** — `godot/ui/ScenarioGoalHUD.gd:134` (performance). `current_scenario` is always empty, so each `_refresh()` iterates `get_available_scenarios()`. *Fix:* Cache the resolved scenario by active id and update only the time label on ticks.
- **[F095] BarentsWatch fetch blocks engine initialisation with sync-over-async** — `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs:147` (performance). `GetAwaiter().GetResult()` with a new `HttpClient` per call runs from `InitializeBarentsWatchPipeline` when `BARENTSWATCH_TOKEN` is set (default timeout 20 s, errors caught). Opt-in dev path only. *Fix:* Make ingestion an explicit async refresh with a static client, response cap, user:// snapshots and payload-derived freshness.
- **[F136] Carbon journals are append-only and unbounded** — `src/LakskongenSim/Farm/CarbonLedger.cs:137` (performance). Saves grew 0.8 → 6.7 MB over 10 years (25k entries), and UI getters scan the whole journal with LINQ. Impact is moderate (queries are on-demand and cheap at this size). *Fix:* Keep running totals per (year, scope) plus a bounded detail window and persist only buckets.
- **[F922] Benchmark scenarios 1-4 are unvalidated, unseeded and non-gating; Scenario 3 does no mortality work** — `src/PerformanceBenchmark/Program.cs:136` (performance). 500,000 fish stay 500,000 after 375 days because `MortalityModel.ApplyMortality` rounds survivors each day (cohorts of ≤ ~1,400 never die when stepped daily), so timings are optimistic. Route the rounding bug to Biology as wrong sim output. *Fix:* Seed all scenarios, assert sanity, carry fractional fish or use stochastic rounding, and drop the "T56 / 37%" claim or add a baseline.

#### Architecture & Design

- **[F798] Retired GDScript tutorial stack is still shipped: ~700 lines of dead scripts, an orphan scene and a no-op autoload** — `godot/project.godot:39` (architecture). `TutorialOverlay.gd`, `TutorialHighlight.gd`, `TutorialTests.gd` and `ui/TutorialOverlay.tscn` are unreferenced, and `TutorialManager` prints "DEPRECATED" on every launch with seven never-emitted signals. *Fix:* Delete the stack and the autoload line and note it in the CHANGELOG.
- **[F970] 22 scenes and ~39 scripts (~5.9k lines) are unreachable from the shipped game; stale duplicates under `ui/` are broken** — `godot/scenes/Main.tscn:1` (architecture). IPO, M&A, stakeholder and carbon-ledger panels have no entry point despite CHANGELOG claims, and `ui/Dashboard.tscn`/`PenManagement.tscn` lack the `%Unique` names their scripts require. *Fix:* Delete or archive dead scenes/scripts, add GameUI tabs for product-relevant panels or mark them unwired, and add a CI reachability check.
- **[F986] Most user-visible text bypasses i18n: 415 scene literals (5 match a key), 236 GDScript literal assignments** — `godot/scenes/ui/SettingsMenu.tscn:74` (architecture). English chrome shows in the nb UI and Norwegian in en (upper bound, some in dormant panels). *Fix:* Migrate live screens first with `UI_*` keys in `.tscn` and `tr()` in code, and add a CI literal check.
- **[F1035] Legacy pre-C# GDScript sim (7 scripts + `data/Fish.gd`) is unreachable dead code that still registers global classes** — `godot/scripts/GameManager.gd:6` (architecture). It carries contradicting constants (optimal temp 14 vs 11, feed 1.5 vs 22.3 NOK/kg), a latent one-argument call bug, and the Python tests mirror it. *Fix:* Delete the cluster (or move to `archive/`), remove the `.uid` files and stale docs references.
- **[F1042] Two achievement systems exist; the autoloaded GDScript one is dead** — `godot/scripts/autoload/AchievementManager.gd:17` (architecture). 38 snake_case ids vs the C# 34 kebab ids with different meanings (`first_harvest`, `industry_leader`), a global `user://` unlock file, and `SaveLoadManagerTests` persists a non-existent id. *Fix:* Remove the autoload, `achievements.json` and the two template scripts, and use real ids in tests.
- **[F702] AudioManager.cs is a dead second audio system that still rewrites bus volumes at boot** — `godot/scripts/autoload/AudioManager.cs:124` (architecture). `SetupAudioBuses` sets Master 1.0/Music 0.7/SFX 0.8/Ambient 0.5 over the bus layout, its paths point at missing assets, and the class name collides with `LakskongenSim.Core.AudioManager`. *Fix:* Remove the autoload and class (or at least the volume writes) and rename to avoid shadowing.
- **[F955] Dead signal surface: 35 of 36 EventBus signals are never emitted, `NotificationPosted` has no listener** — `godot/scripts/autoload/EventBus.cs:14` (architecture). "Game Saved/Loaded" notices never appear, `EventLog.gd` is unreachable and uses snake_case names, and `SustainabilityChanged` is never emitted. *Fix:* Delete EventBus/EventLog or route toasts through it, and null-check in `SaveLoadManager`.
- **[F729] Game rules, price tables and state mutation live in the Godot bridge instead of engine commands and calibration** — `godot/scripts/autoload/GameBridge.Farm.cs:1533` (architecture). RAS/offshore capex, AI feeding, processing tiers and others are hard-coded and several verbs mutate `GameState` directly, with drift (RAS Small 5,000 t vs the enum's 3,000 t) and no idempotence guard on `VaccinateSite`. *Fix:* Move each verb into `SimulationEngine.Commands` returning `CommandResult` with costs from the registry.
- **[F992] Number and currency formatting is not culture-aware and is duplicated ~20 times** — `godot/scripts/bridge/SimFormatters.gd:9` (architecture). The same amount renders four ways across panels and the `no` catalog mixes separators. *Fix:* One locale-aware formatter (`num`, `nok`, `thousands`) in `SimFormatters` replacing the copies.
- **[F767] Alert thresholds are hard-coded copies of simulation constants and are already stale** — `godot/scripts/bridge/SimState.gd:293` (architecture). The lice limits mirror 0.2/0.5 while calibration is 0.15, and FCR/oxygen/net/storm literals have no drift check. *Fix:* Expose thresholds from C# or read `calibration.json` and add a drift pytest.
- **[F768] Alert text is both the API and the UI string: hard-coded, mixed language, and parsed by substring** — `godot/scripts/bridge/SimState.gd:342` (architecture). `ConsequenceDirector` classifies by Norwegian substrings (`lus`, `oksygen`), so rewording or translating silently breaks routing. *Fix:* Add a stable `key`/`category` to `alert_raised` and translate with `tr()`.
- **[F753] About 1,300 lines of prototype and unreachable code are carried alongside the live path, with latent crashes** — `godot/scripts/environment/NordlandFjordController.gd:1` (architecture). Nordland prototype set, `PenLODManager` (bool `or` bug), `VisualAdapter3D`/`IVisualAdapter` and `FishAgeVariant` are unreferenced. *Fix:* Delete or archive them outside export and CI.
- **[F746] Three overlapping sim-to-3D sync layers with divergent mappings and cross-node private calls** — `godot/scripts/environment/Sim3DSync.gd:60` (architecture). Each calls `get_all_pens()`, the weather mapping has different unknown fallbacks, both write the same water uniforms, and components poke each other's private methods by string. *Fix:* One `EnvironmentSync` node reads a snapshot per `state_updated` and emits a typed signal, with public methods.
- **[F953] 45 of 80 UI scripts are unreachable from autoloads/main scene, and the ones inspected have broken bridge bindings** — `godot/scripts/ui/CompetitorDashboard.gd:17` (architecture). Empire, Stakeholder, StockMarket, Genetics, CarbonLedger and others bind to undeclared signals, camelCase keys and missing autoloads; guards hide the dead wiring. *Fix:* Decide per panel to wire (and fix bindings) or archive, and fail CI on new unreachable scripts.
- **[F581] Alert routing classifies localized free-text with substring keyword lists** — `godot/scripts/ui/ConsequenceDirector.gd:20` (architecture). Short stems (`lus`, `ila`) cause false positives and a "storm" early-out. *Fix:* Carry a stable category in the signal and match on it.
- **[F584] Contract negotiation outcome is decided in the UI with unseeded `randf()`** — `godot/scripts/ui/ContractDialog.gd:211` (architecture). The roll is off the SimRng streams, so it breaks determinism-first, can be re-rolled by reloading, and the chance label shows 140% at the slider end. *Fix:* Emit a negotiate intent, resolve it in the sim on a seeded stream, and clamp the displayed chance.
- **[F626] GeneticsPanel is unreachable from the game UI and its only action is a print stub** — `godot/scripts/ui/GeneticsPanel.gd:243` (architecture). The Breed button only prints and two of four trait columns are zeros. *Fix:* Register it as a tab once breeding and values are real, otherwise disable the button.
- **[F630] HUD keeps its own pause/speed state that is never read back from the sim; ESC branch targets a nonexistent node** — `godot/scripts/ui/HUD.gd:223` (architecture). After pausing with Space and resuming via another path the HUD icon is stale and Space needs two presses. *Fix:* Expose `is_paused/time_speed` on `SimBridge` with a state signal and delete the dead branch.
- **[F985] No panel re-translates on locale change** — `godot/scripts/ui/MainMenu.gd:31` (architecture). `_apply_localization()` runs once from `_ready` with pre-translated text, defeating auto-translate, so switching language leaves menus in the old language. *Fix:* Handle `NOTIFICATION_TRANSLATION_CHANGED` in a shared helper or assign raw keys.
- **[F623] MorningBriefing is never instantiated, and its `_input` would swallow ESC even while hidden** — `godot/scripts/ui/MorningBriefing.gd:507` (architecture). PLAN.md still lists wiring as a TODO; once mounted, ESC and the pause menu break. *Fix:* Instance it under the `UILayer`, guard with `if not visible: return`, and add a smoke test.
- **[F649] PauseMenu cannot initialise against `PauseMenu.tscn` and is never instantiated, so the game has no pause/ESC menu** — `godot/scripts/ui/PauseMenu.gd:136` (architecture). `btn_load`/`btn_quit` are null and deref in `_setup_visuals()`, focus paths match no node, and `HUD` calls a nonexistent `/root/GameUI.toggle_pause`. *Fix:* Align the scene and script (or null-guard), instantiate it, implement `GameUI.toggle_pause()`, and add confirmations.
- **[F680] Three finished panels are unreachable from the running game** — `godot/scripts/ui/WeatherDiseasePanel.gd:8` (architecture). Stakeholder, StockMarket and WeatherDisease (~1,450 lines) are not in `_register_tabs()` although PLAN/T93 list bloom mitigation, vaccination and delousing UI as delivered. *Fix:* Register them as tabs with a reachability test or archive and correct the docs.
- **[F795] `format_number/format_currency` add yet another unlocalised money formatter next to 17 existing ones** — `godot/theme/default_theme.gd:810` (architecture). *Fix:* Delegate to one locale-aware formatter in `SimFormatters` and replace the private copies.
- **[F811] Snapshot is written to a non-existent top-level `game/data` directory that the sim reads cwd-relatively** — `scripts/update_aquaculture_snapshot.py:14` (architecture). An untracked local file can change tick results and exports never find it. *Fix:* Choose one deliberate home (e.g. `godot/data`), load it only via explicit config, and record its hash in save metadata.
- **[F861] ~40% of this test unit (2,062 of 5,188 lines) tests classes the engine never constructs** — `src/LakskongenSim.Tests/Farm/MultiFarmManagerTests.cs:8` (architecture). `MultiFarmManager`, `FeedConversionModel`, `IndividualFish`, and Optimization/* inflate counts, and `docs/BALANCE.md:364` cites a stale assertion. *Fix:* Move the legacy classes and tests to an excluded project (per NEXT_LEVEL_PLAN step 6) or wire them in, and update BALANCE.md.
- **[F993] Core emits player-visible Norwegian prose with culture-sensitive number formatting** — `src/LakskongenSim/Advisor/InterventionAdvisor.cs:99` (architecture). `{delta:N0}` / `{currentLice:F2}` use the thread culture (≈222 such interpolations in `src/LakskongenSim`), so output is host-dependent. *Fix:* Return structured codes plus args and translate in the Godot layer, or format with `InvariantCulture`.
- **[F365] All cohorts share one growth RNG stream consumed in list order, contrary to the SimRng contract** — `src/LakskongenSim/Biology/GrowthModel.cs:119` (architecture). Stocking or harvesting one pen re-randomises another's growth noise, because `FishPopulation.Id` is a random Guid and no stable identity exists. *Fix:* Assign deterministic population ids and derive per-cohort streams.
- **[F371] About 1.9k lines of Biology code are unwired and duplicate the live growth model with diverging constants** — `src/LakskongenSim/Biology/SDEGrowthModel.cs:34` (architecture). `SDEGrowthModel`, `IndividualFish`, `FeedConversionModel` etc. are only used by tests (max growth temp 18 vs 17), and the ±5% harvest check validates the unwired model. *Fix:* Wire behind a flag with a probe or move to an experimental namespace, and delete the duplicate formula.
- **[F1080] `WaterQuality` is a public static class with 10 mutable statics rewritten by every engine `Initialize`** — `src/LakskongenSim/Biology/WaterQuality.cs:16` (architecture). Engine A's balance changed (4,096,257 → 4,216,201) when a second engine with different calibration was created in-process. *Fix:* Make it an instance configured from the engine's registry.
- **[F228] Calibration SSOT is violated by tick-path literals, and the AGENTS.md "grep test" guard does not exist** — `src/LakskongenSim/Core/CrewMember.cs:46` (architecture). Crew multipliers, `AquacultureCalibrationEngine` coefficients and crisis tiers are inline. *Fix:* Add `Staff`/`ExternalData` registry sections and implement the promised literal-scan test or correct AGENTS.md.
- **[F1031] Parallel hard-coded copies of calibrated numbers exist and disagree with the registry** — `src/LakskongenSim/Core/GameConstants.cs:45` (architecture). Delousing 250k vs 280k, TypicalFCR 1.20 vs 1.15, harvest weights 3.5/7.0 vs 3.8/7.5, tax rates, MAB 780, and ten `?? 70m` spot fallbacks. *Fix:* One owner per number; delete the GameConstants/`FeedConversionModel` copies and read `_calibration`.
- **[F1092] "One save format" is only one set of JSON options: the core and Godot managers write incompatible envelopes** — `src/LakskongenSim/Core/SaveLoadManager.cs:134` (architecture). The core loader returns a blank Day-0 game for a Godot-envelope save with no exception and never falls back to `.bak`. *Fix:* Share the `SaveGameData` envelope and migration in Core, reject structurally empty states, and centralise naming.
- **[F1010] Engine keeps its own cached `ModLoader`, so toggling mods in the Mod Manager does not affect later New Games** — `src/LakskongenSim/Core/SimulationEngine.Mods.cs:78` (architecture). The bridge's loader writes `mods_config.json`, the engine's loader read it once in its constructor. *Fix:* Use a single instance or re-read configuration at the start of `DiscoverAndLoad`.
- **[F306] Calibration SSOT bypassed: escape fine, static `Default` registry and stale workforce constants** — `src/LakskongenSim/Core/SimulationEngine.cs:182` (architecture). `EscapeFineNOKPerFish = 100m` vs registry 5,000/fish (50x), storm loss read from `.Default`, and employee counts 8/12/25 vs 7/10/20. *Fix:* Use `_calibration` values everywhere and add a test that fails on `CalibrationRegistry.Default` use in the engine.
- **[F311] God class: `SimulationEngine.cs` is 3,901 lines with a ~338-line `Initialize`** — `src/LakskongenSim/Core/SimulationEngine.cs:30` (architecture). Cross-cutting rules are re-implemented ad hoc, causing the Spend/event-log inconsistencies. *Fix:* Extract finance postings, lice regulation, storm hazards, bloom service and snapshot export/import behind tick systems or collaborators.
- **[F1013] `scenarios.json` is a second source of truth that the sim ignores** — `src/LakskongenSim/Core/SimulationEngine.cs:1022` (architecture). All 9 ids are built-in so the loader adds nothing, JSON `starting_sites` never create sites, three keys are unread, and `SCENARIOS_README.md` describes a retired schema. *Fix:* Generate one roster from the other, test field-by-field equality, and rewrite the README.
- **[F1023] Calibration SSOT is mostly not wired: 54 of 82 JSON-backed registry values drive nothing** — `src/LakskongenSim/Core/SimulationEngine.cs:1199` (architecture). "This logs the values; subsystems use their own defaults": only 28 leaves have consumers, so editing e.g. `harvestCostPerKgNOK` or `loanInterestRate` changes nothing while guard tests pass. *Fix:* Inject the registry into subsystems, wire or remove each dead leaf, and add an "every leaf is read" reflection test.
- **[F1058] Two `MarketIntelligenceSystem` instances: the UI reads one, price follows the other, and the UI one is not persisted** — `src/LakskongenSim/Core/SimulationEngine.cs:489` (architecture). The active-event lists differed on all 112 active days over 400 days. *Fix:* Delete the engine-level instance, point the bridge at `MarketSimulation.Intelligence`, and pass the calibrated mean/volatility.
- **[F1143] `EnableResourceRentTax` is a no-op flag; two further resource-rent models exist but are unreachable** — `src/LakskongenSim/Core/SimulationEngine.cs:71` (architecture). *Fix:* Pick `ResourceRentTax.Compute`, delete the others, and gate the annual close on the flag.
- **[F207] About 75% of each RealData layer is copy-pasted across 9 layers; SST and Temperature are the same model twice** — `src/LakskongenSim/Data/RealData/Layers/SstLayer.cs:40` (architecture). Drift is already visible (dead `latCorrection` in two layers). *Fix:* Introduce `ClimatologyLayerBase` with per-layer metadata and an `Evaluate` method, and retire one of Sst/Temperature.
- **[F471] Three competing oxygen/chemistry models exist and several are dead code** — `src/LakskongenSim/Environment/WaterColumnPhysics.cs:10` (architecture). `WaterColumnPhysics`, `WaterQuality.UpdateDailyDO`, `AddNitrogenLoad` and the Mareano stack have only test or demo callers, so the live `Zone.AdvanceDays` path is the least tested (and the docs still describe Mareano as in the tick loop). *Fix:* Delete or mark `[Obsolete]`, and make `Zone.AdvanceDays` the single chemistry path.
- **[F110] About 8,100 lines in 38 sim files, plus a dashboard UI stack, are never used by the game** — `src/LakskongenSim/Finance/InvestmentManager.cs:122` (architecture). Tracked as debt in NEXT_LEVEL_PLAN step 6; six orphan GDScript panels (891 lines) and `DashboardDataProvider` feed nothing. *Fix:* Delete in small PRs starting with zero-reference files and add a CI listing of production types with zero non-test references.
- **[F111] Parallel competing implementations of the same concept (IPO, resource-rent tax, Market vs Markets, save managers, RNG, real data)** — `src/LakskongenSim/Finance/StockMarket.cs:127` (architecture). Two IPO models, three resource-rent models (the flag and `ResourceRentPaid` are dead), and `SaveLoadManagerTests` exercising a manager the game does not ship. *Fix:* Pick one owner per concept, delete the rest, and test the shipped save logic through a shared library class.
- **[F488] Disease and climate couplings read or write the legacy company-wide lice model, not the per-site models** — `src/LakskongenSim/Health/DiseaseSystem.cs:76` (architecture). Site treatment never reaches the company model that drives disease, and `ThermalLiceCoupling` warms only the company model, so the documented warming trend never reaches managed lice (small effect, calibration-coupled). *Fix:* Compute the lice term from per-site lice, apply thermal coupling to `site.Lice`, and keep the company model as a pure rollup.
- **[F526] About two thirds of the Processing/Logistics unit is not wired into the game** — `src/LakskongenSim/Processing/ProcessingPlant.cs:36` (architecture). The fleet stack, four optimizers, `ProcessingPlant`/`ProductionLine`/`YieldCalculator` have only test callers and conflict with the bridge's 1M/5M/15M plant table; tracked as dead weight in NEXT_LEVEL_PLAN. *Fix:* Wire through the engine or move to an experimental folder, and keep one plant price table.
- **[F465] `SustainabilityConfig` is stored but never read, so the config-driven tables do nothing** — `src/LakskongenSim/Sustainability/SustainabilityManager.cs:44` (architecture). Thresholds and price multipliers are literals and the JSON is not copied to output. *Fix:* Read thresholds and multipliers from the config/registry with identical defaults and add tests.
- **[F989] Translation outputs are gitignored but neither CI nor the release script runs an import or checks they exist** — `tools/ci.sh:211` (architecture). A clean runner may export with no catalogs, showing raw `UI_*` keys with all 15 steps green (clean export unverified). *Fix:* Run `godot --headless --path godot --import` before the scene, parse and export steps and fail on missing `translations.nb.translation`.
- **[F814] Hand-copied C# enum lists and a second unused contract (JSON Schemas) with no drift guard** — `tools/validate_mods.py:39` (architecture). They match today but nothing enforces it. *Fix:* A pytest that parses the C# enums and asserts set equality, or validate with the schemas.

#### Error Handling & Resilience

- **[F695] Inconsistent fault handling: skip swallows exceptions with no UI signal, the tick ends the game with a raw exception message** — `godot/scripts/autoload/GameBridge.Core.cs:278` (error-handling). `AdvanceDays` only `GD.PrintErr(ex.Message)` and continues on a half-advanced engine, while `OnSimulationTick` ends the game with the raw .NET message as the reason; both drop the stack trace. *Fix:* One `FailSimulation(ex)` that logs `ex.ToString()` via `GD.PushError` and ends with a localized reason, with the try around `Advance` only.
- **[F1111] Command failure reasons are dropped at the bridge and several panels ignore failure entirely** — `godot/scripts/autoload/GameBridge.Depth.cs:726` (error-handling). Engine commands return `ErrorKey`/`Message` but the bridge reduces them to a bool, so BuildSeaSite, wellboat, RAS, offshore, crew hire, loans and save/load show a generic failure or nothing (two report success after failure). *Fix:* Return `{ok, error_key, message}` across the boundary, map keys to `tr()` strings in one toast helper, and add UI-path probes.
- **[F182] Autosave rotation and backup recovery are effectively non-functional** — `godot/scripts/autoload/SaveLoadManager.cs:168` (error-handling). Autosave always writes `autosave_latest` so rotation never triggers, the backup is refreshed from an unvalidated file, `TryLoadBackup` only toasts, and `GetSaveFiles` hides corrupt and `.backup` files. Mitigated by the temp-file + rename write. *Fix:* Rotate N autosaves, validate before refreshing the backup, fall back to `.backup` automatically, and sanitise paths in every public entry point.
- **[F720] Load is not transactional: the live game is reset before the snapshot is validated** — `godot/scripts/autoload/SaveLoadManager.cs:248` (error-handling). A save with a null collection or bad enum throws mid-adopt after `Initialize`, and `ValidateAndFixGameState` runs after adoption so value-typed repairs never reach live state. *Fix:* Normalise and validate the snapshot before touching the engine and swap on success (or restore the previous state on exception).
- **[F721] Write results are never checked before the temp file replaces the good save** — `godot/scripts/autoload/SaveLoadManager.cs:121` (error-handling). `StoreString`'s result and `GetError()` are ignored, so a disk-full truncated file replaces the save and still reports "Game Saved"; `CreateBackup` truncates the old backup before checking the source. *Fix:* Check write errors, re-parse the temp file before `File.Move`, rotate the backup only if the current file parses, and move the notification out of the `try`.
- **[F935] Save and load results are unobservable: no GDScript listens to `SaveFailed`, `LoadFailed`, `GameSaved`, `GameLoaded`** — `godot/scripts/autoload/SaveLoadManager.cs:93` (error-handling). UI callers ignore the bool and show SUCCESS toasts unconditionally, so a failed save reads as success. *Fix:* Connect the signals to `ToastNotification` in one place and branch on the return value.
- **[F1094] No generic numeric/semantic validation on load** — `godot/scripts/autoload/SaveLoadManager.cs:328` (error-handling). NaN/Infinity/negative values load "successfully" then crash the first tick (`OverflowException`) or poison state; `{"GameState":{}}` replaces a running career with a blank game. `AllowNamedFloatingPointLiterals` lets NaN be persisted. *Fix:* Add a finite/range/duplicate-id validation on the snapshot that rejects with a precise `LoadFailed`.
- **[F793] SceneValidator passes with zero scenes for a mistyped or absolute root** — `godot/scripts/tools/SceneValidator.gd:61` (error-handling). `res://` + an absolute path matches nothing and prints "PASS — all 0 scene(s)". *Fix:* Use `ProjectSettings.localize_path`, fail on a missing root and on an empty scene list.
- **[F789] Screenshot harness reports success (exit 0) even when boot failed or shots were skipped** — `godot/scripts/tools/ScreenshotHarness.gd:106` (error-handling). Boot failure and per-shot errors only log, and the capture script's `*.png` check can pass on stale files. *Fix:* Track failures, fail on boot/expected-count mismatch, clean the output dir first, and fail on non-zero Godot exit.
- **[F573] Costly or irreversible actions ignore the bridge's failure result (install AI, hire, accept offer)** — `godot/scripts/ui/AIFeedingPanel.gd:156` (error-handling). A 100M NOK install happens in one unconfirmed click and failures only reach the console. *Fix:* Add a confirmation showing the cost, check the result and toast the reason, and disable the button once installed.
- **[F654] One-click 800M NOK build with no cost or confirmation; results of money-spending actions are ignored** — `godot/scripts/ui/OffshorePanel.gd:164` (error-handling). *Fix:* Show the bridge-supplied cost, confirm above a threshold, and toast failures through a shared helper.
- **[F651] Settings IO has no error handling, and apply/close semantics are inconsistent** — `godot/scripts/ui/SettingsMenu.gd:92` (error-handling). `FileAccess.open` is dereferenced unchecked, writes are non-atomic, `_dirty` is set only by audio sliders, and the speed label is not live. *Fix:* Null-check and validate `is Dictionary`, write to a temp file then rename, and mark `_dirty` from every widget.
- **[F847] Calibration guards validate whatever `Load()` resolves; `Load()` silently swallows malformed files** — `src/LakskongenSim.Tests/Calibration/CalibrationGuardTests.cs:22` (error-handling). A broken `calibration.json` makes the suite assert the in-code defaults, and `Version_IsPinned` cannot detect a value change. *Fix:* Add a strict test deserializing `godot/data/calibration.json` directly and pin the version literally.
- **[F1028] Calibration loading swallows every error silently, probes CWD-relative paths ahead of the embedded copy, and the engine ends up with two registries** — `src/LakskongenSim/Calibration/CalibrationRegistry.cs:143` (error-handling). Bare `catch` blocks hide malformed/partial files; the engine uses a loaded `_calibration` for `BalancingConfig` but `Default` for market/feed/mortality. Today code defaults equal the JSON so fallbacks are value-neutral. *Fix:* Return diagnostics and log the winning path, validate after parse, and make one registry authoritative.
- **[F1118] Config-load failures follow three different policies: silent fallback, hard crash, and silent never-completes** — `src/LakskongenSim/Calibration/CalibrationRegistry.cs:143` (error-handling). Calibration falls back silently, `AchievementSystem`'s constructor throws on malformed JSON and aborts New Game, and `TutorialSystem.ParseCondition` has an empty `catch`. *Fix:* One `ConfigLoader.TryLoad` that always logs path and exception, and wrap `RegisterAllAchievements`.
- **[F1044] Achievement config failures are inconsistent and misleadingly logged** — `src/LakskongenSim/Core/AchievementSystem.cs:100` (error-handling). A missing file yields zero achievements with only `Console.Error`, a malformed file aborts `Initialize`, and `LoadAchievementsMetadata` logs a non-existent "built-in" fallback. *Fix:* Wrap load in try/catch with `SimLog.Error`, fall back deliberately, and fix the log text.
- **[F250] Vertical-integration OPEX ignores `Spend()` failure but still books the expense** — `src/LakskongenSim/Core/InDayTickSystems.cs:560` (error-handling). A cash-strapped player pays nothing while P&L and the event log show it paid; the empty loop and 30-day vs 365/12 cadence add 1.4% drift. *Fix:* `if (!Spend) ForceSpend`, charge `annual*30/365`, and add a low-cash test.
- **[F308] Optional JSON configs are CWD-probed, uncopied, and the Sustainability loader can crash `Initialize`** — `src/LakskongenSim/Core/SimulationEngine.cs:315` (error-handling). `Data/SustainabilityConfig.json` is read from the CWD with no `try/catch`, no csproj copies the three JSONs, and the camelCase JSON would not bind anyway; effectively dead today. *Fix:* Embed the files, resolve through one helper with a logged fallback, and test the loaded config against disk.
- **[F1060] A mid-day exception leaves a torn day and the next `Advance` re-runs the early phases** — `src/LakskongenSim/Core/SimulationEngine.cs:1426` (error-handling). After a fault injected after `MarketTickSystem` the retry produced feed 363.5 vs 161.5 kg and `State.Day` disagreeing with `Weather.SimulationDay`. *Fix:* Mark the engine faulted and refuse further `Advance` until reload, or run the day transactionally.
- **[F1141] Five more unguarded `Spend()` calls and five Storyteller events silently waive costs; `Spend` has three incompatible caller conventions** — `src/LakskongenSim/Core/SimulationEngine.cs:1959` (error-handling). 12 of 42 call sites discard the result and 6 check-and-waive (Storyteller waived 745k-915k in 4-year runs). *Fix:* Replace the bool `Spend` with an explicit `Pay(amount, PayPolicy)` API and add an analyzer for unused results.
- **[F1114] Throwing event effects are swallowed, leave partial mutations, and are shown to the player as an exception message** — `src/LakskongenSim/Events/Storyteller.cs:120` (error-handling). The record keeps `Event application threw: {ex.Message}`, the cooldown is set and nothing is logged. *Fix:* `SimLog.Error` the exception, do not log/notify the failed event, and apply effects atomically.
- **[F809] Generator always overwrites the snapshot with fallback data and exits 0 even when every source failed** — `scripts/update_aquaculture_snapshot.py:488` (error-handling). The two output files are written non-atomically, so an offline re-run replaces a good snapshot and wrappers cannot detect failure. *Fix:* Write temps then `os.replace`, exit non-zero (3) unless `--allow-fallback`, and keep the previous file when confidence is lower.
- **[F813] Mod validator masks the first of two same-ID mods, crashes on wrong-typed JSON, and passes when no mods directory exists** — `tools/validate_mods.py:279` (error-handling). A dict overwrite hides the first mod, `['x']` raises `AttributeError`, and a missing directory returns 0 so the CI gate silently disables. *Fix:* Keep a list per id, guard element types with a numeric helper, catch `UnicodeDecodeError`, and return 1 for a missing directory.
- **[F818] A parse failure of the shared `projects.json` silently resets it to `{}` and rewrites it** — `tools/mcp_bridge_alias.py:178` (error-handling). All other projects' entries are dropped and the write races with the editor's own writes. *Fix:* Treat an unreadable file as an error, back up before replacing, and clean up the temp file.
- **[F819] Tool-level errors from Blender are printed but the process exits 0, and the MCP call has no timeout** — `tools/blender_mcp_call.py:42` (error-handling). `CallToolResult.isError` is never checked, so the asset pipeline treats failures as success and a hung server blocks forever. *Fix:* Return 1 on `isError`, add a read timeout, and handle bad JSON args with exit 2.

#### Observability

- **[F769] Action failures carry no reason; messages guess the cause** — `godot/scripts/bridge/SimActions.gd:299` (observability). `pen_action_completed(action, id, success)` loses the C# `result.Message`, so GDScript prints guesses like "(over budget?)" and callers like `_on_delouse` ignore the result. *Fix:* Return the reason with the flag (a `{success, message}` dictionary or a `reason` signal argument).
- **[F752] Silent guards hide broken wiring across the environment unit** — `godot/scripts/environment/FjordVistaBridge.gd:94` (observability). Missing nodes and `has_method` guards return without a log while 21 routine `print()` calls drown the log, so pen click, FX, wellboats, season and water quality are non-functional with an empty log. *Fix:* Add a one-time `_validate_wiring()` that warns per unresolved dependency, `push_warning` in guard else-branches, and demote the prints.
- **[F1115] SimLog's redirect/level filter is never wired, and 118 `Console.*` calls plus a per-day DiseaseLOD print bypass it** — `src/LakskongenSim/Core/SimLog.cs:64` (observability). `OnMessage` has no subscriber, `LogPerformanceMetrics()` prints every simulated day, and C# never uses `GD.PushWarning/PushError`. *Fix:* Subscribe in `GameBridge._Ready` to route to Godot with `MinLevel` from `OS.IsDebugBuild()`, replace `Console.*`, and add a ban test.
- **[F347] Competitor distress is never surfaced** — `src/LakskongenSim/Finance/CompetitorAI.cs:530` (observability). The news action goes to an unread `_pendingActions`, `OnCompetitorDistressed` has no subscriber (and `TriggerCrisis` no caller) despite AGENTS.md, the day passed is fabricated, and three rivals are "distressed" after the first quarter at 70 NOK/kg. *Fix:* Return the action in `quarterActions`, pass the real day, wire the event or fix AGENTS.md, and calibrate starting financials.

#### Documentation & DX

- **[F185] Audio provenance is contradictory and unverifiable** — `CREDITS.txt:30` (docs). `disease_alert.wav` and `harvest_ready.wav` are credited to Kenney while `download_audio_assets.ps1` fetches them from Freesound; three "Freesound CC0" files lack author or id, Mixkit clips restrict standalone redistribution, and T65/T69 docs still describe sine-wave placeholders. *Fix:* Record per-file provenance (file, author, URL/id, licence, date), swap restricted clips, and delete placeholder-era docs.
- **[F255] Scenario special rules and objectives are UI labels only; the sim never enforces them** — `src/LakskongenSim/Core/ScenarioSystem.cs:187` (docs). `harsh_storms`, `calm_weather`, `easy_market`, `single_site` etc. only build display badges, and `red_zone`, `mab_reduction`, `min_contained_sites` have no reader; Survival wins on equity > 0. *Fix:* Implement the rules or remove them from data/UI, and add a test that every defined rule key has a consumer.
- **[F991] Four docs contradict the code and `docs/LOCALIZATION.md`** — `godot/translations/README.md:5` (docs). README/LOCALIZATION_GUIDE claim six languages and a Language tab, Architecture_DeepDives says CSVs auto-load, and CONTRIBUTING references a missing `extract_strings.py`. *Fix:* Rewrite or delete the stale docs to point at `docs/LOCALIZATION.md`.

#### Code Quality & Style

- **[F706] Player-facing text is hard-coded in the bridge in mixed Norwegian and English, baked into EventLog and saves** — `godot/scripts/autoload/GameBridge.Depth.cs:212` (code-quality). Culture-dependent formatting and `FormatCapital`'s `Replace(",", " ")` make saved logs differ by machine. *Fix:* Return keys plus args, store them in `EventRecord`, and format persisted text with `InvariantCulture`.
- **[F730] 2,059-line Farm partial with a ~180-line `GetAttentionItems` and copy-pasted advisor prelude** — `godot/scripts/autoload/GameBridge.Farm.cs:428` (code-quality). Magic thresholds and duplicated forecast/FCR fallbacks. *Fix:* Split into Crew/Genetics/Facilities/Attention partials and move aggregation to a tested library class.
- **[F733] Attention-inbox strings are untranslatable and event-log text is hard-coded in mixed languages** — `godot/scripts/autoload/GameBridge.Farm.cs:484` (code-quality). The `UI_ATTENTION_*` keys exist in no CSV, persisted event text mixes Norwegian and English with process-culture numbers, and `string.Format` on a translated template can throw. *Fix:* Add the keys, store event keys plus parameters, and use a safe format helper.
- **[F736] LocalizationManager is an orphan: no callers, `ui.lang.*` keys do not exist, and de/fr/es/ja are not registered** — `godot/scripts/autoload/LocalizationManager.gd:9` (code-quality). It rewrites `user://language.cfg` on every launch and prints debug lines. *Fix:* Wire it into SettingsMenu with endonym labels and register the locales, or delete it.
- **[F583] Six copies of currency/number formatters, and the ones used for deltas mishandle negatives** — `godot/scripts/ui/AttentionInbox.gd:416` (code-quality). A loss shows as `-2500000 NOK` while a gain shows `2.5M`. *Fix:* One shared helper that scales on `abs()` with tests for 0/negative/boundaries.
- **[F608] Localization is only partial: most `tr()` calls use Norwegian literals with no translation entry** — `godot/scripts/ui/Dashboard.gd:240` (code-quality). All 21 Dashboard literals and 31 EmpirePanel ones have no row, `UI_FARM_STATE_*` keys do not exist, and the Encyclopedia is hard-coded English. *Fix:* Move to key-based `UI_*` strings, add a CI check that every key exists, and put Encyclopedia content in a data file.
- **[F631] HUD localization bypassed: raw C# enum names, English fallback tooltips with no keys** — `godot/scripts/ui/HUD.gd:138` (code-quality). `weather_value.text` shows the enum name and the six `UI_HUD_TIP_*` keys are missing. *Fix:* Map the enum to `tr('UI_WEATHER_' + state.to_upper())`, add the keys, and return a stable season id.
- **[F625] Nine legacy UI scripts (~1,340 lines) are orphaned and target nonexistent autoloads, bridge APIs and Godot 3 APIs** — `godot/scripts/ui/MnADashboard.gd:14` (code-quality). *Fix:* Delete or archive them, or rebuild against the real `GameBridge` API with a contract test.
- **[F658] 12 of 23 files in the processing/regulation/ROI UI unit (1,528 of 4,477 lines) are orphaned dead code with broken contracts** — `godot/scripts/ui/ProcessingMixPanel.gd:47` (code-quality). Missing autoloads, unimplemented bridge calls, four divergent `_format_currency` copies. *Fix:* Delete or archive them and use the shared formatter.
- **[F677] Status colours are used as text colours on light surfaces (1.6-2.0:1) and some state is colour-only** — `godot/scripts/ui/TelemetryPanel.gd:754` (code-quality). SUCCESS/WARNING/DANGER text and the overlay legend captions fail contrast, and the audit's pair list omits them. *Fix:* Keep hues on swatches and render text with AA-safe variants, dual-encode state, and add the pairs to the audit.
- **[F682] User-visible strings bypass localization and mix languages and decimal separators** — `godot/scripts/ui/TelemetryPanel.gd:115` (code-quality). Telemetry has no `tr()` at all and several dialogs use literals though keys exist. *Fix:* Route through `tr()` with `UI_*` keys and centralise numeric formatting.
- **[F990] Legacy `strings*.csv` set is dead, corrupt and still documented as the source of truth** — `godot/translations/strings.csv:1` (code-quality). Invalid UTF-8, destroyed Japanese cells, 130 duplicated keys, and a bogus locale `value` import. *Fix:* Delete the files and `.import` outputs (recover content from history if wanted) and update the docs.
- **[F987] Half of the translation catalog is dead and code/CSV key names have drifted** — `godot/translations/translations.csv:124` (code-quality). 277 of 552 rows are unreferenced and rows 120/124 hold English in the `no` column. *Fix:* Delete or wire the orphans, enforce one naming scheme, and fix `HUD.gd:240` to use the keys.
- **[F797] ScenarioGoalHUD strings bypass localization and mix English and Norwegian** — `godot/ui/ScenarioGoalHUD.gd:79` (code-quality). "Goal:", "mislyktes" and "år igjen" are literals with `%.1f` decimals. *Fix:* Add keys and use a locale-aware formatter.
- **[F370] Calibration SSOT is violated by tick-code literals in Biology, and the guard test AGENTS.md describes does not exist** — `src/LakskongenSim/Biology/MortalityModel.cs:121` (code-quality). Stage multipliers are duplicated in `MortalityModel` and `GrowthModel` (3x). *Fix:* One `CalibrationRegistry.Biology` stage table and the promised literal-scan test with an allowlist ratchet.
- **[F1032] Literal audit: ~2,075 inline model literals across the tick path, duplicated stage tables, and a 1.2 stage multiplier that rescales the calibrated sea-mortality anchor** — `src/LakskongenSim/Biology/MortalityModel.cs:128` (code-quality). The realised sea hazard is 20% above `SeaMortalityPerCycle` for the Seawater stage. *Fix:* One `StageTable` for all five switches, normalise the anchor by stage exposure, and ratchet via the literal-allowlist test.
- **[F472] Numeric literals in tick code throughout the Environment unit** — `src/LakskongenSim/Environment/WaterChemistry.cs:80` (code-quality). The excretion, uptake and exchange constants most in need of calibration are inline, which is how the unit errors survived. *Fix:* Move them into `CalibrationRegistry` sections and add the literal-scan guard incrementally.
- **[F516] Hundreds of numeric literals bypass `CalibrationRegistry` in events, and the "grep test fails on literals" guard does not exist** — `src/LakskongenSim/Events/NorwegianEvents.cs:71` (code-quality). About 55 cash literals plus thresholds, cooldowns and Storyteller tunables. *Fix:* Load event tables from data via the `ModLoader` path, read tunables from the registry, and add the scan test.
- **[F399] Calibration SSOT is violated across the Farm unit** — `src/LakskongenSim/Farm/OffshoreFarm.cs:182` (code-quality). Facility OPEX, delousing tables, camera decay and CAPEX are inline and duplicated with disagreeing values (RAS Medium 1.0B vs 1.2B). *Fix:* Move tables into calibration sections and add the source-scan test with an allowlist.
- **[F820] Machine-specific absolute path to the Blender MCP server is hard-coded** — `tools/blender_mcp_call.py:19` (code-quality). `SERVER_DIR = "/Users/fredrik.j.hansen/blender_mcp/mcp"` fails opaquely for anyone else. *Fix:* Read `BLENDER_MCP_DIR` and exit with a clear message when unset.

#### Testing

- **[F958] CI "Godot MCP: Shader check" can never fail: it only `load()`s each `.gdshader` under `--headless`** — `.claude/skills/ecc/godot-mcp-ops/headless_runner.py:198` (testing). The dummy renderer compiles nothing and `load()` returns a non-null `Shader`, so CI prints "Shader check passed" while `fjord_sky/fjord_water.gdshader` use the nonexistent `hint_default_noise` (the two shaders are currently unreferenced). *Fix:* Render two frames in a non-headless session with the shader on a material and fail on `SHADER ERROR|Parse Error|Unknown hint` in stderr, plus a static hint allow-list check.
- **[F1110] Visual diff gate passes when screenshots are missing, and the calibrated threshold is never applied** — `.claude/skills/ecc/visual-qa/visual_qa/diff.py:55` (testing). Only `FAIL` is counted, so an empty capture prints "All 28 shots within threshold ✓" and exits 0; `config.THRESHOLD = 25` is never read (manifest and CLI default to 8, and `--threshold 0` is ignored). *Fix:* Treat any `MISSING_*` as a failure, use `args.threshold if not None else manifest.get('threshold', THRESHOLD)`, and add tests for an empty dir and distance 20 at 25.
- **[F176] GitHub CI has never run, and its pytest steps cannot pass as written** — `.github/workflows/ci.yml:50` (testing). `origin/main` is 64 commits behind with no `.github/`, the Actions API reports 0 workflows, and neither `ci.yml` nor `verify.yml` installs pytest (no requirements file) so `python -m pytest` fails with "No module named pytest". The unpushed state is a deliberate human gate. *Fix:* Add `requirements-dev.txt` and an install step, fail fast with a clear message in `ci.sh`, open a PR from a branch to see the first run, and set `permissions`/`timeout-minutes`.
- **[F1144] Documented "grep test fails on literals in tick code" does not exist, and the economy tick is full of literals** — `AGENTS.md:8` (testing). Only `DeterminismBanTests` scans source; `ProcessDailyFinance` hard-codes 5_000m, 0.0001m, 2_000m/1_000m, 60_000m, 12_000m, 50000 and 0.015. *Fix:* Add a Roslyn/regex test over the tick files failing on literals other than 0/1/2/30/365 and move these into `CalibrationRegistry`.
- **[F178] `export_presets.cfg` is git-ignored, so the CI export-verify step passes vacuously** — `godot/.gitignore:5` (testing). The nested ignore overrides the root negation, an empty preset list returns `success: True` with a warning, there are no Windows/Linux presets (Phase 5 is documented as pending), and AGENTS.md:137 claims it "already passes in CI". *Fix:* Remove the ignore line, commit macOS/Windows/Linux presets, fail `export_verify` on zero presets under `CI=true`, and correct AGENTS.md.
- **[F700] Bridge-only logic is untested, and the contract test is name-based** — `godot/scripts/autoload/GameBridge.Depth.cs` (testing, file-level). No test references `DeployCountermeasure`, `InstallBarrier`, `ToggleSludgeHub`, `ValorizeSludge*`, `GetCarbonIntensity` or `CheckDiseaseOutbreaks`, and the contract test exempts `has_method`-guarded names. *Fix:* Fail the contract test on non-Variant signatures and `event_bus.` signal names, and move logic into the engine so probes can drive it.
- **[F063] The shipped save/load path is untested; the tested `SaveLoadManager` is not the one the game uses** — `godot/scripts/autoload/SaveLoadManager.cs:15` (testing). The Godot autoload is a separate implementation (single `.backup`, envelope, migration) covered only by string-grep tests. *Fix:* Make the autoload a thin wrapper around the library manager so tested code is shipped code.
- **[F770] No automated tests for bridge logic; the contract test has blind spots** — `godot/scripts/bridge/SimState.gd:292` (testing). It checks only `game_bridge.` receivers, with no facade-parity, arity, `SaveLoadManager` or shape checks, and `SimFormatters`/`_check_alerts` have no tests. *Fix:* Add facade-parity/arity pytests, a headless GDScript test for `_check_alerts`, boundary tests for `SimFormatters`, and widen `ACCESS_RE`.
- **[F968] "Scene load validation" instantiates scenes without adding them to the tree, so `_ready`/`@onready` wiring errors pass** — `godot/scripts/tools/SceneValidator.gd:92` (testing). `ui/Dashboard.tscn`, `ui/PenManagement.tscn` and `ui/AchievementNotification.tscn` pass despite missing `%Name`/`$path` nodes. *Fix:* Add each scene to the root, await two frames, free, and scrape engine errors; optionally add a static wiring audit.
- **[F791] A11y target audit has inflated counts and covers only part of the UI** — `godot/scripts/tools/ScreenshotHarness.gd:133` (testing). The tab list covers 9 of ~17 tabs, counts are incremented before the visibility check and duplicate across cached panels, only `BaseButton`s are measured, and only at scale 1.0. "221 controls, 0 under 44 px" is therefore not unique visible controls. *Fix:* Enumerate tabs from GameUI, count unique visible controls, include sliders/inputs, test at 0.8, and make violations fail the run.
- **[F959] Harness shoots tab id `carbon` that GameUI does not have: baseline `07e_carbon_ledger.png` is the Offshore panel** — `godot/scripts/tools/ScreenshotHarness.gd:377` (testing). `show_tab("carbon")` hits an unknown-tab warning, so the carbon-ledger panel has no visual or a11y coverage while the baseline claims it. *Fix:* Register the tab in GameUI or drop it, assert `get_active_tab() == tab_id` in `_show_tab`, and re-promote 07e.
- **[F792] TutorialTests targets an API that no longer exists, aborts silently and always exits 0** — `godot/scripts/tutorial/TutorialTests.gd:86` (testing). It expects the pre-unification `TutorialManager`, a `/root/TutorialHighlight` autoload and a 23-step JSON, and prints "ALL TESTS PASSED" while 6 of 7 tests never ran. *Fix:* Delete it with the retired stack, or count runtime errors as failures and exit 1.
- **[F607] Bridge contract test cannot see these breakages and there are no GDScript tests for any panel in this unit** — `godot/scripts/ui/FinancePanel.gd:605` (testing). Snake_case EventBus access, `Engine.get_singleton` use, `take_loan` arity and guarded calls to nonexistent methods all pass; the CI parse scan is a 3-frame boot. *Fix:* Check calls on `SimBridge`/`_sim` against signatures (name and arity) and add a headless panel smoke script.
- **[F632] No behavioural tests for the high-traffic UI scripts** — `godot/scripts/ui/MainMenu.gd:89` (testing). Calls via `GameBridge.X`, `bridge.X` or `call_deferred("GetIPOEligibility")` are never checked. *Fix:* Add a headless test runner for MainMenu, GameUI tab switching and HUD pause, and broaden `ACCESS_RE` to `call(_deferred)?("…")`.
- **[F686] No automated coverage for any file in the research/sustainability/toast/tutorial unit; the one telemetry baseline locks in a defect** — `godot/scripts/ui/TechTreePanel.gd:1` (testing). *Fix:* Add headless tests or harness steps for tech-tree node rects, ESC/toast consistency and the zero-oxygen overlay, plus research/regulation/tutorial screenshots.
- **[F914] `ConcurrentSavesToSameSlot` asserts that a race must fail, and it can flake** — `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:575` (testing). `Assert.NotEmpty(exceptions)` punishes a safe `SaveGame`, flaked ~0.5% on one CPU, and `FileNotFoundException` shows concurrent saves can destroy backup history. *Fix:* Serialise per slot with temp-file + atomic rename and assert all calls succeed and backups are valid, or drop the test.
- **[F915] Load-hardening tests cover null collections but not null elements or invalid values** — `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:262` (testing). `{"Populations":[null]}`, negative day/count and `null` payload all load as success. *Fix:* Add those cases and decide whether to reject (so fallback kicks in) or repair.
- **[F843] `EscapesPerYear_WithinBound` is structurally unable to fail** — `src/LakskongenSim.Tests/Balance/BalanceReportTests.cs:188` (testing). The Baseline runs with events off and `ApplyStormHazards` returns early, so escapes are always 0; the failure message also mislabels P90 as "median". *Fix:* Delete the Baseline test (Storm covers it) or enable events, and give `AssertWithin` a `stat` parameter.
- **[F844] PeakLice test name promises "reaches the 0.5 limit" but the assertion accepts medians down to 0.25** — `src/LakskongenSim.Tests/Balance/BalanceReportTests.cs:182` (testing). The band `[0.30, 0.65] ± 0.05` was re-baselined to the measured 0.67. *Fix:* Assert `InRange(Median, LiceMax, LiceMax*1.6)` or rename to "stays below the runaway guard".
- **[F839] Growth "calibration" tests assert positivity only and cannot fail on a wrong model** — `src/LakskongenSim.Tests/Biology/GrowthCalibrationTests.cs:14` (testing). Clamping to 0.01 then asserting `> 0`, `avgOptimal`/`avgCold` both just positive, out-of-range temperatures accepting [0.5, 2.0] for an input-preserving value, and a 1.5-10 kg median band. *Fix:* Assert the property named (`avgOptimal > avgCold * 1.5`, `Assert.Equal(1.0, …)`, batch vs day-by-day ratio, calibrated band).
- **[F840] Stochastic tests build `GrowthModel`/`MarketSimulation` without a seed** — `src/LakskongenSim.Tests/Biology/GrowthCalibrationTests.cs:10` (testing). The parameterless constructors draw from a crypto RNG, so results differ per run and failures cannot be reproduced; tolerances were widened to hide it. *Fix:* Use the seeded constructors everywhere and tighten the ranges.
- **[F841] "±5% vs Norwegian reference" test has a tautological checkpoint and validates a model that is off the live growth path** — `src/LakskongenSim.Tests/Biology/GrowthCalibrationValidation.cs:175` (testing). The d0 row is 0.00% by construction and the SDE/VB rows validate `SDEGrowthModel`/`GameConstants.VB_*`, not the engine's `GrowthModel`. *Fix:* Drop the d0 row and add a seeded sigma=0 checkpoint against `GrowthModel` at d180/360/540.
- **[F1025] Calibration guards and the promised literal-grep test give false assurance: they pin dead values** — `src/LakskongenSim.Tests/Calibration/CalibrationGuardTests.cs:96` (testing). 54 of the guarded values are never read by the sim, docs/CALIBRATION.md is stale (1.0.0 vs 1.0.4, missing `stormEpisode`), and two pytest files assert bounds on local constants. *Fix:* Add the literal ratchet, the "every registry leaf is read" test, behavioural per-domain tests, delete the tautological Python tests, and refresh the doc.
- **[F838] Determinism ban regex misses the real escape hatches** — `src/LakskongenSim.Tests/Core/DeterminismBanTests.cs:18` (testing). `DateTime.Today`, `SimRng.CreateUnseeded()`, `RandomNumberGenerator`, `DateTimeOffset` and `TickCount` are not matched though `src/LakskongenSim` uses `DateTime.Today` and unseeded fallbacks. *Fix:* Extend the pattern, strip `/* */` comments, and re-baseline the allowlist once.
- **[F846] All 16 `DifficultyTests` cover a subsystem nothing in the game calls** — `src/LakskongenSim.Tests/Core/DifficultyTests.cs:8` (testing). They pin preset literals for dead code and `CustomDifficultyBuilder` accepts any double. *Fix:* Wire difficulty into engine init with a probe, or mark it experimental, and add range validation.
- **[F845] ModLoader error and hostile-input paths are untested** — `src/LakskongenSim.Tests/Core/ModWiringTests.cs:216` (testing). Invalid manifest, duplicate id, cycles, missing deps/files and `../` path traversal have no tests, and `LoadMod` does an unchecked `Path.Combine`. *Fix:* Add tests asserting `LoadErrors` for each branch and a containment check.
- **[F837] Trade tests cover only the happy path and hide a missing solvency check and responder authorization** — `src/LakskongenSim.Tests/Core/MultiplayerTests.cs:75` (testing). `RespondToTradeOffer(offerId, accept)` takes no responder id and does `buyer.Balance -= price` unchecked. *Fix:* Require `responderId == offer.ToPlayerId` and sufficient balance, then test non-recipient, insufficient balance, reject and expiry.
- **[F834] No known-answer vectors pin the "frozen" SimRng stream derivation** — `src/LakskongenSim.Tests/Core/SimRngTests.cs:19` (testing). Every assertion is relative, so changing a SplitMix constant or FNV offset would invalidate saves while the suite stays green. *Fix:* Add known-answer tests (`Stream_Output_IsFrozen`, `Mix(0) == 0xE220A8397B1DCDAF`) and a named-stream capture/import round trip.
- **[F211] v2 RealData tests are shape-only and miss the risky paths** — `src/LakskongenSim.Tests/Data/RealDataLayerTests_v2.cs:27` (testing). No tests of `GetValueAsync`, cache, snapshot, errors or provenance correctness; the constructor writes `test_cache` into the CWD; the dropped latitude term passes `north >= south * 0.9`. The file also does not compile as written (missing usings). *Fix:* Rename, use temp dirs, add absolute-range, async, cache and error tests and a player-path probe.
- **[F842] Vacuous assertions: `IsFinite` on a decimal, a 3-value enum `is` check, a pure-function self-comparison** — `src/LakskongenSim.Tests/Environment/CampaignScenarioTests.cs:295` (testing). A decimal cast to double is always finite, and `EnsureRunnable` injects zones/populations that mask a scenario failing to materialise. *Fix:* Assert solvency/bankruptcy flags, test the clamp with an out-of-range multiplier, and make `EnsureRunnable` assert.
- **[F849] Thin happy-path tests for contracts, crew, mitigation and remote ops hide a latent partial-delivery penalty bug** — `src/LakskongenSim.Tests/Environment/ContractLifecycleTests.cs:22` (testing). `FulfillContract` compares `toApply < c.QuantityKg` instead of cumulative remaining, so staged deliveries return a shortfall penalty (currently discarded by the caller). *Fix:* Add partial-delivery, exact-expiry-amount and per-role crew tests and fix the comparison.
- **[F503] InterventionAdvisor tests only prove an action name appears, at a scale no shipped scenario reaches** — `src/LakskongenSim.Tests/Environment/InterventionAdvisorTests.cs:55` (testing). 100,000 fish at 5.5 kg sits just above the 500,000 kg constant, hiding the unreachable harvest rule and weight-dependent delousing suppression. *Fix:* Add table-driven cases (including the 5,000-fish tutorial farm), ordering/Take(3) checks and a PlayerScriptProbe.
- **[F848] `Rank()` tests only check `Contains`; ordering, the top-3 cap and negative cases are untested** — `src/LakskongenSim.Tests/Environment/InterventionAdvisorTests.cs:20` (testing). *Fix:* Add `NoSuggestions_WhenLiceLow…`, sorted `DeltaNok`, at-most-three and breach-after-day-5 cases.
- **[F869] Score, synergy and quality tests use bounds that the documented formulas do not need** — `src/LakskongenSim.Tests/Expansion/VerticalIntegrationTests.cs:141` (testing). Expected 0.70/100,000/0.393 but asserting `>= 0.45`, `>= 80,000` (the no-synergy value) and `< 0.5`. *Fix:* Assert the computed values and add a no-synergy case.
- **[F870] Construction-time setup is overwritten by `BuildAsset`; the under-construction and unlock paths have no tests** — `src/LakskongenSim.Tests/Expansion/VerticalIntegrationTests.cs:125` (testing). Tests pass only because `constructionMonths: 0` and a later `DateTime.Now` happens to be ≥ it. *Fix:* Remove the dead pre-sets, add under-construction exclusion and `IsUnlocked` tests, and inject a clock.
- **[F872] License auction save round-trip and twin-run tests are weaker than their names** — `src/LakskongenSim.Tests/Finance/LicenseAuctionTests.cs:435` (testing). No settled auction (`won_player`) is saved and reloaded, `Run()` ignores `Ok` flags, and the rival-bid test is conditional on a bid occurring. *Fix:* Round-trip a settled auction and assert `BuildSeaSite` consumes it after load, assert `Ok`, and make the rival test deterministic.
- **[F864] "Finite" checks on decimal values are tautologies; "StaysFiniteAndSolvent" never asserts solvency** — `src/LakskongenSim.Tests/Integration/CampaignSmokeTests.cs:76` (testing). *Fix:* Assert `Balance > 0` / the bankruptcy flag, mirror equality and plausible ranges.
- **[F865] Scenario smoke skips the engine-coupled starting state and never starts the scenario** — `src/LakskongenSim.Tests/Integration/CampaignSmokeTests.cs:70` (testing). `ApplyStartingState(engine.State)` omits the engine, so disease, insurance and rent tax are not exercised, and `EnsureRunnable` fabricates a site. *Fix:* Pass the engine, call `StartScenario`, and assert scenario-specific facts.
- **[F874] Misnamed, tautological and wall-clock-dependent integration tests** — `src/LakskongenSim.Tests/Integration/EconomicEnvironmentalIntegrationTests.cs:41` (testing). `ColdZonesHigherCost` asserts cold < warm, the seasonality test feeds `DateTime.Today`, and the P&L test asserts COGS it just recorded. *Fix:* Rename, fix the clock, and use the real models.
- **[F873] Empire save round-trips skip the serializer and omit crisis, corporate-finance and vertical-integration state** — `src/LakskongenSim.Tests/Integration/EmpireSaveRoundTripTests.cs:33` (testing). They hand the live `State` to `RestoreFromLoad` and never reference `CrisisEventState`, `CorporateFinanceSystemState` or `VerticalIntegrationState`. *Fix:* Add a `RoundTripViaJson` helper and cases with loss carry-forward, an active crisis, IPO stage and owned tiers.
- **[F862] Most empire integration tests assert only non-null or tautologies** — `src/LakskongenSim.Tests/Integration/EmpireSystemsIntegrationTests.cs:121` (testing). `Count() >= 0`, "score in [0,100]", unused locals. *Fix:* Assert deltas (score increase, competitor harvest change, phase transition).
- **[F863] Tests labelled deterministic run on an unseeded engine or unseeded models** — `src/LakskongenSim.Tests/Integration/EmpireSystemsIntegrationTests.cs:27` (testing). `Initialize(enableEvents:false)` takes the `CreateUnseeded()` path, absorbed by very loose bounds. *Fix:* Pass fixed seeds everywhere and tighten bounds.
- **[F867] Flagship whole-site-cost invariant is behind an `if` and can be skipped** — `src/LakskongenSim.Tests/Integration/EnforcementTests.cs:59` (testing). Cost assertions sit inside `if (log.BalanceChange < 0m)`, so free enforcement passes. *Fix:* Assert unconditionally against the exact profile cost.
- **[F868] `EngineInitialize_BindsAllNamedStreams` omits two eagerly bound streams** — `src/LakskongenSim.Tests/Integration/EngineTwinRunDeterminismTests.cs:122` (testing). `license_auction` and `corporate_finance` are not in the required list. *Fix:* Add them, or derive the set from `SimRngStreams` with an explicit lazy allowlist.
- **[F859] "FCR" assertion divides NOK by kg and the realism bands are too wide to fail** — `src/LakskongenSim.Tests/Integration/FullCycleEconomicTests.cs:150` (testing). `0 < fcr < 20` admits 15 NOK/kg × 1.15; weight 1-20 kg, margin −50..99%. *Fix:* Track feed kg separately, assert `InRange(fcr, 1.1, 1.6)` and tighten the other bands.
- **[F860] PO04-vs-PO12 test compares temperature-blind biology and two independent unseeded markets** — `src/LakskongenSim.Tests/Integration/FullCycleEconomicTests.cs:187` (testing). Biology ignores temperature, so assertions 1-2 hold trivially and the margin check can flake. *Fix:* Delete it or rebuild it on `SimulationEngine` with two seeded sites in zones of different temperature.
- **[F871] Growth band widened to admit the model's output; P&L and zone tests assert their own setup** — `src/LakskongenSim.Tests/Integration/FullCycleTests.cs:139` (testing). 3.0-7.0 kg was set just under the observed 3.2 kg (AGENTS.md says fix the model, not the band); the P&L test credits revenue itself. *Fix:* Assert the calibrated band, use the engine's harvest verb, and test zone independence by mutation.
- **[F857] S9 interval-clock reset test and weekly-boundary test cannot fail** — `src/LakskongenSim.Tests/Integration/InDayPipelineTests.cs:31` (testing). `Taxes` stays 0 in an empty engine regardless of clocks, and `EventLog.Count >= earlier` is monotone. *Fix:* Observe a clock-dependent effect (quarterly corporate-finance event on day 90) or expose `ITickClock` values and assert 0 after `Initialize`.
- **[F889] MAB test passes without MAB logic: the over-MAB fixture lands in the density "Warning" branch** — `src/LakskongenSim.Tests/Integration/OptimizerIntegrationTests.cs:275` (testing). The optimizer derives `WelfareStatus` from density only, and the fixture is exactly 25.0 kg/m3. *Fix:* Add the MAB check (CRITICAL) and assert exact strings for both cases.
- **[F885] BalanceMirror test passes with the post-tick mirror phase deleted** — `src/LakskongenSim.Tests/Integration/PostTickPipelineTests.cs:36` (testing). The in-day mirror on the 6th tick re-syncs `State.Balance` anyway (confirmed by mutation). *Fix:* Advance exactly to the execution day, assert the harvest executed and add an order test over `_postTickPhases`.
- **[F886] Mid-storm load / single-claim tests cannot fail: the first storm found lasts one day** — `src/LakskongenSim.Tests/Integration/RuntimePersistenceTests.cs:104` (testing). Mutating the persisted edge state to Calm still passes. *Fix:* Find a seed where the storm persists across the save or force it with `ImportState`, and add a negative control.
- **[F894] Wall-clock assertions run in the default parallel test collection with no category or isolation** — `src/LakskongenSim.Tests/Integration/SoakLongRunTests.cs:69` (testing). The 3-year soak (<10 s), PerfGate (5 ms/day) and Sleep-based FPS tests flake under CPU contention. *Fix:* Put timing tests in a non-parallel collection or a Perf trait filtered into a dedicated CI job.
- **[F897] No negative-path tests for wellboat booking; an unknown kind is accepted and later harvests the whole site** — `src/LakskongenSim.Tests/Integration/WellboatExecutionTests.cs:36` (testing). `RequestWellboat("site-1", "harvset")` returns Ok and 6 days later harvests all 500 fish; the ROV `>=` check can't fail. *Fix:* Validate kind and return `unknown_kind`, add insufficient-funds/engine-not-ready tests, and start the ROV test at integrity 60.
- **[F891] ObjectPool test "ReducesAllocations_ByOver50Percent" measures nothing and hides a broken `GetPoolStats`** — `src/LakskongenSim.Tests/Performance/PerformanceTests.cs:425` (testing). `kvp.Value is ConcurrentQueue<object>` never matches `ConcurrentQueue<T>`, so `totalObjects` is always 0. *Fix:* Count via a non-generic interface and measure allocation with `GC.GetAllocatedBytesForCurrentThread()` or rename.
- **[F893] "Full economic calculation under 5 ms" reads three properties; the load-scaling test is skipped with a false reason** — `src/LakskongenSim.Tests/Performance/PerformanceTests.cs:717` (testing). *Fix:* Benchmark real work, unskip the scaling test with a seeded engine and `(double)` division, or remove them.
- **[F895] Unseeded RNG in tests makes pass/fail non-reproducible (measured 0.135% flake in `Breed_100Offspring_AllValid`)** — `src/LakskongenSim.Tests/Phase2Tests.cs:126` (testing). `new Genetics()`, `new GrowthModel()` and unseeded `Initialize` use crypto seeds (27 of 20,000 runs failed, min 0.394). *Fix:* Seed everything and loop over seeds for real invariants.
- **[F887] `CreateEngine` ignores its seed; the "same seed produces same results" test compares 0.0 with 0.0** — `src/LakskongenSim.Tests/PlanktonBloomTests.cs:24` (testing). `AdvanceZoneBloom` never draws the RNG and intensity is 0 after 30 winter days. *Fix:* Pass the seed to `Initialize` and advance into bloom season before comparing.
- **[F888] Five of eleven plankton tests assert nothing meaningful; a crash observability bug is hidden** — `src/LakskongenSim.Tests/PlanktonBloomTests.cs:96` (testing). `Assert.True(true)`, `>= 0` and unused locals; `TriggerCrash` sets `IsCrashing` true and false in the same call so it can never be observed. *Fix:* Force the path with an explicit config and assert the DO drop; fix or remove `IsCrashing`.
- **[F890] Playtest suite contains tautologies, an in-test clamp, and duplicates of OptimizerIntegrationTests** — `src/LakskongenSim.Tests/Playtest/AutomatedPlaytestSuite.cs:19` (testing). The weight clamp conflicts with "no clamps to hit realism" and the harvest-date check is true by construction. *Fix:* Seed the model, delete the clamp, assert a model-derived band, and drop the duplicates.
- **[F901] Assertions far looser than the test names** — `src/LakskongenSim.Tests/SDEGrowthModelTests.cs:528` (testing). `0 <= O2 <= 14`, an unasserted `earlyGrowthRate`, `weight > 0.5` when the minimum is 1.8, price `InRange(30, 200)`. *Fix:* Tighten to model-derived values with the fixed seed.
- **[F898] 644-line TutorialSystem has a single `Skip()` test; the reward re-pay regression is untested** — `src/LakskongenSim.Tests/TutorialSystemTests.cs:8` (testing). *Fix:* Add step progression/payout-once, mid-tutorial save/`AdoptSavedState`, idempotent `Skip`, and JSON condition parsing tests.
- **[F1043] AchievementSystem, which pays out up to 29.76M NOK, has zero tests** — `src/LakskongenSim/Core/AchievementSystem.cs:48` (testing). The post-load re-pay regression (`ResyncFromState`), day-1 balance-threshold payouts and config-key/condition-map consistency are unpinned. *Fix:* Add tests that every config key exists in the map, that load + resync leaves balance unchanged, that day-1 rewards don't fire, and per-condition positive/negative cases.
- **[F264] No tests cover the tick systems or negative paths that hide the defects above** — `src/LakskongenSim/Core/InDayTickSystems.cs` (testing, file-level). Nothing references `SludgeValorization`, `StockMarketTickSystem` or `VerticalIntegrationTickSystem`; ModLoader has 3 facts; scenario tests call `ApplyStartingState(state, engine)` while the bridge does not. *Fix:* Add player-path tests: start each scenario via the bridge entry point, advance, save/load and compare; cadence tests for every `ITickClock`; research unlock/effect tests; RAS/offshore persistence; ModLoader negatives.
- **[F291] Tests give false assurance for distressed assets, the player path, and logging** — `src/LakskongenSim/Core/SimulationEngine.Licence.cs:404` (testing). `AcquireDistressedAsset` has no direct test (only `Count() >= 0`) and no test references `SimLog`. *Fix:* Test `AcquireDistressedAsset` per `AssetType`, boundary tests for `StockSmolts`/`TakeLoan`, a save/load twin run with RAS and crisis, and `SimLog` sink tests.
- **[F309] `RestoreFromLoad` mixes `State` and `loadedState`; the 3-year soak test passes vacuously** — `src/LakskongenSim/Core/SimulationEngine.cs:3607` (testing). The soak sets only `Day` on the loaded engine, so 595 days run on an empty farm. Production reaches it via `AdoptSavedState`, so the half-restore risk is mostly theoretical. *Fix:* Make `RestoreFromLoad` internal or use `loadedState` only, and have the soak use `AdoptSavedState` and assert population counts match.
- **[F310] No tests reference night-shift policy, sludge valorization, carbon ledger or weather consequences** — `src/LakskongenSim/Core/SimulationEngine.cs:1859` (testing). AutomatedROC/ScanOnly branches never run under test. *Fix:* Add focused tests for the weather table, night-shift cost/effect, sludge threshold/revenue, carbon emissions per scope and bloom week, plus a Mattilsynet player-path probe.
- **[F397] Seven farm types (about 1,600 lines) have zero tests** — `src/LakskongenSim/Farm/CohortTransferSystem.cs:16` (testing). `CohortTransferSystem`, `AIFeedingSystem`, `CarbonLedger`, `SludgeValorization`, `RASFacility`, `OffshoreFarm`, `PostSmoltFacility` are only smoke-executed by the soak. *Fix:* Add transfer, build-and-persist, AI-install gain, carbon round-trip and sludge economics tests.
- **[F340] Phase 4.3 corporate-finance stack ships with almost no behavioural tests** — `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:12` (testing). No test references `CovenantSystem`, `IPOSession`, `TaxSystem` or `LossCarryForward`; soak tests cross year-end but only assert finiteness, and no save-hash window crosses a year-end. *Fix:* Add tests for cumulative tax, default-start covenants, resource-rent known answers, carry-forward bounds, save at day 359 across year-end, and a 12-quarter competitor twin run.
- **[F449] RealData range tests are tautological against the layer's own `Math.Clamp`; most of the unit has no tests** — `src/LakskongenSim/Data/RealData/Layers/OxygenLayer.cs:42` (testing). The clamps never engage, and `DataCache`, interpolators, `GeoCoordinate`, `EmodnetDataLoader` and the async path have no tests. *Fix:* Replace range asserts with known-value and continuity checks, and add `DataCache` round-trip and interpolator edge-case tests.
- **[F202] Lice and biomass latitude terms are computed then dropped; the latitude-gradient test passes vacuously** — `src/LakskongenSim/Data/RealData/Layers/SeaLiceLayer.cs:197` (testing). `latCorrection` is absent from the return, so `Lice_NorthernRegions_HigherPressure` (`north >= south * 0.9`) passes on equality. *Fix:* Add the term (or drop the claim) and assert `north > south + 0.05f`.
- **[F473] No direct tests for the live environment dynamics** — `src/LakskongenSim/Environment/WaterChemistry.cs:70` (testing). `UpdateOneDay`/`Zone.AdvanceDays` stability, the Markov distribution, sustainability scoring, terrain and profile interpolation are untested, and existing tests assert `>= 0`. *Fix:* Add stability, Markov-frequency, score-boundary, interpolation and terrain-smoothness tests.
- **[F426] Most of the Market unit has no tests, including live code paths** — `src/LakskongenSim/Market/MarketIntelligence.cs:434` (testing). `CurrencySystem`, `MarketIntelligenceSystem`, `QualityGrader`, `InternationalMarketSystem` and export/import round trips are untested, though soak and calibration tests give partial coverage. *Fix:* Add property tests for market invariants, round trips for market/intelligence/contracts, and `GenerateOffers`/currency tests.
- **[F559] No tests for `DashboardDataProvider` or `AcquisitionSystem`** — `src/LakskongenSim/Integration/DashboardDataProvider.cs:15` (testing). A single smoke test per public method would have caught the `InvalidCastException`, and a `MakeOffer` test the unit mismatch. *Fix:* Add dashboard smoke tests, acquisition affordability tests and an injected clock for TTL tests.
- **[F524] Live night-shift resolver has no tests, and several unit tests only pass vacuously** — `src/LakskongenSim/Logistics/NightShiftPolicy.cs:19` (testing). `ForecastTwinTests` uses an empty `GameState`, `VAPProcessingTests` asserts nothing about `IsOperational`, and the lethal-oxygen assertion is a disjunction. *Fix:* Add resolver range/ordering tests, a populated `ForecastTwin` RNG-purity test, a `BrandReputation` round trip, and boundary tests for `IsOperational`.
- **[F946] Bridge contract test passes while 15+ contract breaks exist** — `tests/test_bridge_contract.py:22` (testing). `ACCESS_RE` matches only `game_bridge|_game_bridge`, members are collected from source (not generated exposure), and `has_method("X")` anywhere in a file exempts every use of X. Within its stated scope it has no false passes today. *Fix:* Resolve receivers by autoload, fail on guards for absent names, check arity, non-Variant signatures, dictionary keys and signal arity.
- **[F988] No automated check of the localization subsystem anywhere** — `tests/test_bridge_contract.py` (testing, file-level). CSV validity, key coverage, placeholder parity and catalog loading are untested while the sim is heavily tested. *Fix:* Add `tests/test_i18n_catalog.py` (3 columns, unique keys, sprintf grammar, referenced ⊆ defined, en/no placeholder parity) and a headless catalog-load smoke.
- **[F1038] Five Python test files never touch project code** — `tests/test_growth.py:3` (testing). `test_growth/feed/genetics/environment/constants` re-implement formulas locally and mirror the dead GDScript, so they cannot fail when the real sim changes; CI labels the step "bridge contract" and CONTRIBUTING shows non-existent imports. *Fix:* Delete them (real coverage belongs in xUnit) or load `calibration.json`, and fix the CI label and CONTRIBUTING.
- **[F815] None of the Python tools have automated tests, and the accessibility audit is not run by CI** — `tools/a11y_audit.py:17` (testing). The snapshot normalizer, validator edge cases, WCAG/CVD maths and `sync_tokens` are untested. *Fix:* Add `tests/test_tools_*.py` (contrast known values, validate_mods fixtures, all-None payload flags) and an `a11y_audit.py` step in `ci.sh` once the muted-text policy is fixed.
- **[F907] BarentsWatch tests cover one branch of the crisis engine and no failure paths** — `tests/Health/BarentsWatchIntegrationTests.cs:98` (testing). Mutating the lice-surge branch, thresholds and repository ordering left all 5 tests green, and `BuildReport(null)` throws. *Fix:* Add table-driven scenario/boundary cases, no-token/malformed/empty-area negatives, a two-snapshot ordering test and exact-value assertions.
- **[F909] Several "integration" tests assert on their own local arithmetic** — `tests/Processing/IntegrationTests.cs:31` (testing). The value-chain, market-event and spot-vs-contract tests compute and compare local variables, and `NotEqual(110m, 90m)` cannot fail. *Fix:* Call production pricing/hedge code and assert on its output, or delete the tests.
- **[F910] IPO/earnings test covers only an enormous beat; the miss path hides a no-op price floor** — `tests/Processing/IntegrationTests.cs:141` (testing). `Math.Max(SharePrice * 0.5m, SharePrice)` is a no-op, and a −10B quarter recorded a negative `NewPrice` (−4275.7). *Fix:* Add seeded miss/in-line tests asserting `NewPrice >= old * 0.5m`, and fix to `Math.Max(oldPrice * 0.5m, SharePrice)`.
- **[F908] `ProductionLine_MaintenanceImpactsUptime` never asserts any uptime impact** — `tests/Processing/VAPProcessingTests.cs:214` (testing). `IsOperational`'s `> 60` branch is unreachable behind `> 30`, so a 61-day overdue line stays operational. *Fix:* Assert `IsOperational()` is false at 61 days with uptime 0.7 and reorder the production branches.
- **[F1037] The "Headless GDScript parse scan" only sees scripts loaded during a 3-frame editor boot** — `tools/ci.sh:211` (testing). Orphan scripts are never checked, and docs record real errors found only by `--check-only --script`, which CI never runs (engine behaviour inferred from those docs). *Fix:* Run `--check-only --script` for every `.gd` outside addons or `load()` them all in `SceneValidator.gd`.

### Low / Style / Nits

Grouped by category. "unwired" or "orphaned" marks code with no production caller today: fix when wiring it in, or delete it. Bullets with several IDs share one nit; each ID carries its own location.

#### Correctness

- [F1022] `godot/scripts/ScenarioManager.gd:239` — `debt_and_profit` goal reads "achieved" on day 0 for debt-free survival scenarios; derive progress from days elapsed or trust only bridge `ScenarioWon`.
- [F707] `godot/scripts/autoload/GameBridge.Depth.cs:1414` — `GetGillRisk` feeds constants (O2 8.0, turbidity 5.0, current 0.2) and double-counts gill score; pass live `ZoneOxygenMgL`/`SiteFlowMs` and drop the blend.
- [F994] `godot/scripts/bridge/SimFormatters.gd:13` — `format_nok`/`format_biomass` round across unit boundaries ("1000 kNOK", "-0 NOK"); round first, then pick the unit.
- [F973] `godot/scripts/environment/FishAgeVariant.gd:17` — looks for Body/fins among its own empty children, so age scaling is a no-op; resolve via `get_parent()`.
- [F754] `godot/scripts/environment/FishPenManager.gd:195` — pen slot chosen by list index; keep `_slot_by_pen` and allocate the first free slot.
- [F1045] `godot/scripts/ui/AchievementPopup.gd:85` — handler takes 4 args, `achievement_unlocked` emits 5 (scene never instanced); add the `id` param or `.unbind(1)` and delete the unused popup/duplicate scene.
- [F589] `godot/scripts/ui/AttentionInbox.gd:143` — `int(cat[0])` is 0 for letters so the category tie-break is a no-op; use `casecmp_to`.
- [F586] `godot/scripts/ui/CashFlowForecastPanel.gd:107` — best-case runway shows a warning and margin <= 0 reads "Well Above Break-Even" (orphaned); return a sentinel runway and treat margin <= 0 as never.
- [F588] `godot/scripts/ui/CreditsScreen.gd:86` — white names on #ECEFF4, reversed scroll, unreachable stop (orphaned); use theme tokens and scroll up from 0.
- [F587] `godot/scripts/ui/DailySummary.gd:184` — bars sized `abs(profit/100)` px and ESC hides the layer for good (orphaned); scale to the week max and drop the handler.
- [F609] `godot/scripts/ui/Dashboard.gd:243` — finance card titled "(6 mnd)" but plots 30 days; retitle and clear bars on the early return.
- [F610] `godot/scripts/ui/Dashboard.gd:533` — pen cards use farm-wide lice/temperature and silently drop sites past six; read per-site fields and show "+N flere".
- [F600] `godot/scripts/ui/DataExportPanel.gd:258` — `Engine.has_singleton("GameBridge")` is always false, so exports fail while "Export all" reports success (panel unreachable); call the autoload node and count failures.
- [F601] `godot/scripts/ui/EmpirePanel.gd:711` — fabricated fallback competitors/certifications and TODO IPO/Buy buttons (orphaned); show "ikke tilgjengelig" and hide inert buttons.
- [F612] `godot/scripts/ui/EncyclopediaPanel.gd:635` — search filters current category by title only and leaves a stale article; search all categories and descriptions, add a no-hit state.
- [F614] `godot/scripts/ui/FarmStateBanner.gd:166` — O2 0.0 treated as "no data" and balance exactly 0 falls in a gap; use a NaN sentinel and `balance <= BALANCE_CRITICAL`.
- [F633] `godot/scripts/ui/MapPanel.gd:487` — "Key operators" row always blank (`tr("\n")`); `", ".join(r.get("key_operators", []))`.
- [F055] `godot/scripts/ui/PauseMenu.gd:228` — back-to-menu un-pauses the sim, which keeps ticking and autosaving (menu currently unwired); add `GameBridge.StopGame()` before the scene change.
- [F977] `godot/shaders/sky_shader.gdshader:120` — half-rotation sun disc disagrees with `TimeOfDay` light; pass the sun direction as a uniform and refresh the reflection probe.
- [F976] `godot/shaders/terrain_shader.gdshader:184` — `use_textures=0` still applies default-white normals (~55 degree tilt); `mix(vec3(0,0,1), mixed_normal, use_textures)`.
- [F996] `godot/translations/translations.csv:8` — version label "v0.2.0 Alpha" baked into catalog and scene; read `application/config/version` and format it.
- [F826] `scripts/vb_fit.py:29` — "fit" assumes W-inf = 1.05 x max and t0 = -30, silently drops bad rows and accepts n=1; fit all parameters, require a minimum point count, raise instead of the 0.0025 fallback.
- [F501] `src/LakskongenSim/Advisor/InterventionAdvisor.cs:89` — pre-emptive delouse suppressed below ~2.9 kg by an invented ROI gate; always emit on forecast breach and take costs from the registry.
- [F376] `src/LakskongenSim/Biology/FeedConversionModel.cs:275` — length-weight coefficient ~4x too short (unwired); use `L = (100*W/K)^(1/3)`.
- [F363] `src/LakskongenSim/Biology/FishPopulation.cs:177` — `GrowthPenalty` is written by the disease engine but never read; apply it in `BiologyTickSystem` or delete it.
- [F374] `src/LakskongenSim/Biology/GeneticEngineering.cs:187` — `AssessDeployment` ignores reputation and defaults `hasSterilityContainment` to true; make it required and use or drop the reputation parameter.
- [F377] `src/LakskongenSim/Biology/IndividualFish.cs:187` — unwired model double-counts environment, duplicates the stage rule and loses the initial multiplier; pass raw env once and share `DetermineStage`.
- [F362] `src/LakskongenSim/Biology/MortalityModel.cs:162` — daily `Math.Round` zeroes deaths below ~1,400 fish; carry a fractional remainder per cohort.
- [F366] `src/LakskongenSim/Biology/PlanktonBloom.cs:98` — an active bloom can only grow until it crashes and `IsCrashing` is always false; add a loss term and persist the flag one tick.
- [F375] `src/LakskongenSim/Biology/SDEGrowthModel.cs:484` — response curves are discontinuous or inverted at thresholds; interpolate from the adjacent band boundary and add monotonicity tests.
- [F367] `src/LakskongenSim/Biology/WaterQuality.cs:168` — `UpdateDailyDO` is unwired, mis-scaled and unstable (its tests pass via clamp); delete it or divide by pen volume and bound the exchange fraction.
- [F224] `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs:227` — `SourceFreshnessDays` is always ~0 (age of the fetch); derive from snapshot year/week with an injected clock.
- [F225] `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs:214` — risk index averages lice count with unitless indices, so neutral data gives 0.75; normalise lice to an index first.
- [F223] `src/LakskongenSim/Core/CrewMember.cs:14` — persisted `Id` is `Guid.NewGuid()`; assign a deterministic id at hire and drop the allowlist line.
- [F237] `src/LakskongenSim/Core/CrewMember.cs:27` — half the hire candidates are unreachable (`FirstOrDefault` by role) and a duplicate role is pure cost; hire by candidate name.
- [F232] `src/LakskongenSim/Core/DataExporter.cs:280` — nb-NO `N` format emits U+2212 minus and NBSP separators; use `F` with an ASCII hyphen and no group separator.
- [F113] `src/LakskongenSim/Core/GameState.cs:232` — `DistressedAssets` never saved or restored (crises themselves are dormant); add `ExportAssets`/`RestoreAssets`.
- [F1064] `src/LakskongenSim/Core/InDayTickSystems.cs:356` — "quarterly/annual" consumers read YTD P&L, fiscal year is 360 days vs 365 elsewhere; [F1145] `src/LakskongenSim/Finance/CorporateFinanceSystem.cs:40` — year label off by one and tax period never advances; snapshot P&L per quarter, use one calendar helper, read the year before `AdvanceQuarter`.
- [F1065] `src/LakskongenSim/Core/InDayTickSystems.cs:768` — snapshot, achievements and scenario check run before NightShift and post-tick settlement; move them after balance mirror.
- [F1083] `src/LakskongenSim/Core/InDayTickSystems.cs:317` — `PenPriceCouncil` fed daily for a 4-week average, never reset on `Initialize`, output unused; feed weekly and reset or delete.
- [F252] `src/LakskongenSim/Core/ModLoader.cs:467` — mod order is filesystem-dependent and id-less events get a Guid; sort ordinally and require ids.
- [F256] `src/LakskongenSim/Core/MultiplayerManager.cs:74` — composite score weights make one sustainability point worth 500 MNOK; normalise terms and move weights to calibration.
- [F276] `src/LakskongenSim/Core/SimulationEngine.Licence.cs:167` — site ids not unique (`BuildSeaSite`, `AcquireDistressedAsset`); dedupe or `Fail("site_name_taken")`.
- [F282] `src/LakskongenSim/Core/SimulationEngine.Mods.cs:79` — active mod set not recorded in saves and registration order is filesystem-dependent; sort manifests and persist `ActiveMods`.
- [F045] `src/LakskongenSim/Core/SimulationEngine.cs:315` — results depend on cwd probes, env vars and an opt-in blocking HTTP call; embed configs, stamp calibration/snapshot version in saves.
- [F302] `src/LakskongenSim/Core/SimulationEngine.cs:2861` — deadline check precedes the compliance check, so a just-compliant or sub-100 kg overage site is force-harvested whole; check compliance first.
- [F303] `src/LakskongenSim/Core/SimulationEngine.cs:2287` — non-compliance fine fires only when a transition lands on `Day % 90 == 0`; charge from a periodic tick.
- [F312] `src/LakskongenSim/Core/SimulationEngine.cs:3506` — share counts truncated through `int` DTOs and unparsable enums skipped silently (IPO unreachable); store decimals and `SimLog.Warn` on skip.
- [F313] `src/LakskongenSim/Core/SimulationEngine.cs:953` — plankton-crash event bypasses bloom mitigation/insurance and the temperature comment is 100x off; route via the bloom system and fix the rate.
- [F332] `src/LakskongenSim/Core/TimeManager.cs:14` — `CurrentDay` is a second clock never restored on load; remove it or add `Restore(int)`.
- [F323] `src/LakskongenSim/Core/TutorialSystem.cs:88` — restore is positional and loses completion days; derive the index from completed ids and clamp it.
- [F324] `src/LakskongenSim/Core/TutorialSystem.cs:121` — tutorial track and rewards depend on cwd-relative file discovery with silent fallback; embed the JSON and fail loudly.
- [F327] `src/LakskongenSim/Core/TutorialSystem.cs:310` — skipping the tutorial wins the tutorial scenario; add a persisted `WasSkipped` flag.
- [F1015] `src/LakskongenSim/Core/TutorialSystem.cs:225` — placeholder completion names pay rewards on elapsed time, UI hardcodes 23 dots for 18 steps; fire from bridge events and drive dots from step count.
- [F441] `src/LakskongenSim/Data/RealData/Interpolation/IdwInterpolator.cs:106` — one NaN neighbour poisons the result and nearest-neighbour has no distance cap; filter `float.IsFinite`, add `maxDistanceKm` (unwired).
- [F442] `src/LakskongenSim/Data/RealData/Interpolation/TemporalInterpolator.cs:59` — seasonal method is a stub, extrapolation is flat, smoother is O(n^2) and null uncertainty becomes 0 (unwired); remove the stub, use a sliding window.
- [F434] `src/LakskongenSim/Data/RealData/Layers/BathymetryLayer.cs:64` — longitude used as offshore distance in the wrong direction (Oslofjord 3000 m); use a zone depth table (unwired).
- [F204] `src/LakskongenSim/Data/RealData/Layers/BiomassLayer.cs:216` — adds a lice term to tonnes and ignores position; drop `liceFactor`, derive magnitudes from calibration.
- [F435] `src/LakskongenSim/Data/RealData/Layers/CurrentLayer.cs:210` — depth correction jumps back up at 50 m; use slope `-0.0025` so it is continuous.
- [F437] `src/LakskongenSim/Data/RealData/Layers/CurrentLayer.cs:170`, [F210] `src/LakskongenSim/Data/RealData/Layers/SstLayer.cs:119` — `GetStatisticsAsync` throws when `end < start` and `BySeason` is hard-coded; `GetGridAsync` OOMs at resolution 0 and accepts inverted bboxes; validate arguments and compute stats from samples.
- [F443] `src/LakskongenSim/Data/RealData/Layers/CurrentLayer.cs:40`, [F216] `src/LakskongenSim/Data/RealData/Layers/SstLayer.cs:42`, [F357] `src/LakskongenSim/Finance/FinancialStatements.cs:78` — culture-sensitive float/decimal formatting in cache keys, OGC URLs and persisted event text; use `CultureInfo.InvariantCulture`.
- [F436] `src/LakskongenSim/Data/RealData/Layers/TemperatureLayer.cs:205` — monthly step function gives +3 degC overnight jumps and a mis-phased "daily" term; interpolate between month mid-points.
- [F086] `src/LakskongenSim/Data/RealData/Models/GeoCoordinate.cs:97` — epsilon `Equals` vs exact-bits `GetHashCode`; quantise to the 1e-4 grid and hash the ints.
- [F439] `src/LakskongenSim/Data/RealData/Models/GeoCoordinate.cs:79` — `GetProductionZone` returns "PO9" but tables use "PO09" (no callers); emit `D2` ids from the single zone table or delete.
- [F462] `src/LakskongenSim/Environment/MareanoEnvironmentalDataProvider.cs:94` — forecast refill appends from today with stale entries queued, giving duplicates and a 2-day lag; refill from the queue end (advisor only).
- [F463] `src/LakskongenSim/Environment/NorwegianZoneEnvironmentalProfile.cs:152` — seasonal values anchored at season start, so warmest day is ~1 June; anchor at season midpoints.
- [F504] `src/LakskongenSim/Events/CrisisCapitalismSystem.cs:624` — asset expiry decremented once per 7-day call; store `ExpiresDay` (dormant until `TriggerCrisis` is wired).
- [F506] `src/LakskongenSim/Events/CrisisCapitalismSystem.cs:409` — assets ignore real holdings and can mint sites or licences; generate from the competitor's inventory and re-validate at purchase.
- [F515] `src/LakskongenSim/Events/NorwegianEvents.cs:74` — ISA "fallow" is a permanent compounding x0.1 capacity cut on `Zones.First()`; resolve the culled population's zone and model a timed state.
- [F511] `src/LakskongenSim/Events/Storyteller.cs:368` — disease event invents an infected count and population that nothing uses, with a literal `3.0` bloom nutrient; tie to the seeded population and read the bloom threshold.
- [F512] `src/LakskongenSim/Events/Storyteller.cs:61` — `RegisterEvent` accepts empty/duplicate ids and negative weights; throw or skip with a log and clamp with `Math.Max(0, w)`.
- [F553] `src/LakskongenSim/Expansion/AcquisitionSystem.cs:206` — completed acquisitions transfer nothing and targets reappear after load; persist acquired ids and apply the purchase.
- [F550] `src/LakskongenSim/Expansion/VerticalIntegrationSystem.cs:213` — savings mix kg with NOK and production is a placeholder 10000f (catalogue unloaded); base savings on a calibrated cost per kg.
- [F402] `src/LakskongenSim/Farm/BloomMitigation.cs:103` — `MatchMultiplier` gives 0.5 when the best match is `None`; return 0.0.
- [F389] `src/LakskongenSim/Farm/DelousingSystem.cs:216` — reported deaths differ from fish removed and the `Delouse` rng is test-only; allocate exactly `deaths` across cohorts or drop the stream.
- [F400] `src/LakskongenSim/Farm/OffshoreFarm.cs:146` — engine reads only OPEX from tick results, so escapes, lockdown and CO2 flags have no effect; consume them in the engine.
- [F394] `src/LakskongenSim/Farm/PostSmoltFacility.cs:120` — survival spread over a hard-coded 180 days and `(int)` truncation; derive hazard from residence time and carry a remainder (unwired).
- [F393] `src/LakskongenSim/Farm/RASFacility.cs:151` — DO is constant and CO2 alarm is always on at design capacity (unwired); model per-tank kg/m3 with calibrated constants.
- [F395] `src/LakskongenSim/Farm/SludgeValorization.cs:34` — biogas value ~23x below its docs and fertilizer mass priced as nitrogen; express as kWh x price and kg x N-content x price in the registry.
- [F345] `src/LakskongenSim/Finance/CompetitorAI.cs:489` — competitor harvest compounds with no MAB coupling and AI takeovers skip antitrust; cap by MAB utilisation and apply the check.
- [F346] `src/LakskongenSim/Finance/CompetitorAI.cs:645` — "swallows X" headlines never transfer assets; execute the merge or drop the headline, and guard an empty roster in `GenerateQuarterlyReport`.
- [F355] `src/LakskongenSim/Finance/EmpireProgression.cs:351` — phase is non-monotonic, unlocks duplicate and `RequiredPhase` is ignored; promote only via `max`, dedupe unlocks via `HashSet`.
- [F359] `src/LakskongenSim/Finance/FinancialStatements.cs:139` — `UpdateBiomassInventory` cancels itself so Assets never change; subtract the previous inventory (no caller yet).
- [F350] `src/LakskongenSim/Finance/IPOSession.cs:110` — "beat guidance" branch unreachable, `(int)sharesIssued` can overflow, guidance not saved, `Enum.Parse` throws on load; compute signed surprise, use `TryParse` (IPO stubbed).
- [F354] `src/LakskongenSim/Finance/InvestmentManager.cs:338` — `ExecuteInvestment` can credit cash without shares or spend and opex is double-deducted in analysis; validate amounts and fix the fallback (unwired).
- [F1086] `src/LakskongenSim/Finance/LicenseAuctionSystem.cs:306` — `List.Sort` has no tiebreak for equal bids; use `OrderByDescending(Amount).ThenBy(seq)`.
- [F348] `src/LakskongenSim/Finance/StockMarket.cs:569` — "floor at 50% drop" is a no-op and a tiny consensus gives 40x jumps; `Math.Max(oldPrice*0.5m, ...)` and bound the surprise (IPO stubbed).
- [F349] `src/LakskongenSim/Finance/StockMarket.cs:326` — consensus seeded with annual EPS but compared to quarterly, and `RestoreState` drops `_lastEPS` and listing state; seed per quarter and persist it.
- [F358] `src/LakskongenSim/Finance/TrafficLightSystem.cs:123` — MAB consequence mutates capacity with no baseline, so colour flapping ratchets it; store a base capacity and derive.
- [F492] `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:87` — `ConfigureDiseaseShare` sentinels (540 d, endemic = 1.0) can start every pen 100% infected; set 0 and warn instead.
- [F490] `src/LakskongenSim/Health/SeaLiceModel.cs:153` — no exogenous lice source, so a few treatments make the model extinct; add a wild/neighbour source term.
- [F491] `src/LakskongenSim/Health/SeaLiceModel.cs:188` — new-infection term goes negative above `MAX_LICE_PER_FISH`; `Math.Max(0, 1 - lice/MAX)` and validate seed arguments.
- [F497] `src/LakskongenSim/Health/StochasticDiseaseEngine.cs:184` — zone pressure never decays, resistance ignores `zoneId`, `HasRecentTreatment` never resets (unwired); add decay and per-zone keys or delete.
- [F538] `src/LakskongenSim/Logistics/Inventory.cs:30` — `CreateReorder` places an order every call, `Consume` hides shortfall, `RoutePlanner` skips `TripCost` (unwired); track pending orders and return unmet kg.
- [F419] `src/LakskongenSim/Market/ContractSystem.cs:220` — `FulfillContract` returns per-call shortfall penalty, accepts negative `deliveredKg`, negotiation can chain; return payment only and validate inputs.
- [F414] `src/LakskongenSim/Market/CurrencySystem.cs:149` — absolute-NOK O-U sigma pins JPY to its clamp ~68% of days; use relative sigma per pair.
- [F425] `src/LakskongenSim/Markets/InternationalMarketSystem.cs:30` — float money, stale 71.44 base price shown on the export screen, entry fee never charged (unwired); use decimal and drive from `MarketSimulation`.
- [F427] `src/LakskongenSim/Market/MarketIntelligence.cs:397` — forecast clamps 50/40/170 vs band 42/150 and the bridge passes mean 70 vs 82; use `GetPriceForecasts()` and registry bounds.
- [F410] `src/LakskongenSim/Market/MarketSimulation.cs:426` — seasonality uses a 360-day year while the rest uses 365; share one calendar helper.
- [F416] `src/LakskongenSim/Market/MarketSimulation.cs:668` — `SellFish` credits reputation before validation (no caller); call `RecordDelivery` only when `result.VolumeKg > 0`.
- [F1066] `src/LakskongenSim/Market/MarketSimulation.cs:520` — certification/research recompute skips the realised-price clamp (92.3 vs 88 ceiling); compute the price in one clamped function.
- [F1084] `src/LakskongenSim/Market/MarketSimulation.cs:111` — price window, accessible markets, contract week and ESG discount are not rehydrated on load; persist or recompute at end of `RestoreFromLoad`.
- [F532] `src/LakskongenSim/Optimization/FeedingOptimizer.cs:67` — "lethal oxygen: stop feeding" is overridden by the 0.2% minimum clamp; return 0.0 before the clamp.
- [F533] `src/LakskongenSim/Optimization/FeedingOptimizer.cs:137` — temperature multiplier jumps to 0.3 outside [2,18] C, so hotter feeds more; make it continuous and zero beyond the growth window.
- [F544] `src/LakskongenSim/Optimization/HarvestOptimizer.cs:151` — weight-premium step at 4.0 kg creates cliff artefacts and out-of-range scenarios still rank first; smooth the curve and sort valid scenarios first.
- [F536] `src/LakskongenSim/Optimization/PenManagementOptimizer.cs:71` — computes the MAB limit but never compares biomass; also throws when `penIdsPerSite` is short; add an over-MAB branch and validate lengths.
- [F555] `src/LakskongenSim/Performance/EconomicCalculationCache.cs:62` — expired-key refresh leaks LRU nodes and later evicts live entries; remove the old node before inserting.
- [F558] `src/LakskongenSim/Performance/ObjectPoolManager.cs:72` — `GetPoolStats` always reports 0 and double `Return` aliases an instance; use a non-generic pool interface and reject double returns.
- [F563] `src/LakskongenSim/Performance/SimulationProfiler.cs:80` — nested `BeginSystem` overwrites the outer and `TotalCalls` is capped at 300; use a scope stack and a long counter.
- [F487] `src/LakskongenSim/Politics/StakeholderFactionSystem.cs:240` — `FulfillDemand`/`IgnoreDemand` ignore `Remove`'s result, so repeats stack; return NotApplicable when absent and check expiry.
- [F531] `src/LakskongenSim/Processing/ProcessingPlant.cs:172` — `ApplyUpgrade` bills before validating and can downgrade the tier (no callers); validate then charge last and use a typed upgrade record.
- [F529] `src/LakskongenSim/Processing/ProductionLine.cs:145` — the `> 60` branch is unreachable and `IsOperational` can never be false; order branches high-to-low and add an outage rule.
- [F528] `src/LakskongenSim/Processing/YieldCalculator.cs:80` — clamp allows yield > 1.0 (FreshWhole 1.045) and the 0.05 floor exceeds Roe's cap; model tech as loss recovery and assert `0 < y <= 1`.
- [F466] `src/LakskongenSim/Sustainability/SustainabilityManager.cs:123` — regulatory penalty is overwritten by the next weekly recompute; keep dated penalties and subtract them in `CalculateScore`.
- [F822] `tools/gen_textures.py:170` — normal maps use green-down (`ny = -dy`) while Godot imports with `invert_y=false`; flip the sign or set the import flag, then verify with a lit render.

#### Error handling

- [F704] `godot/scripts/autoload/AchievementManager.gd:93`, [F519] `src/LakskongenSim/Advisor/RecommendationHistory.cs:218`, [F233] `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs:265`, [F1104] `src/LakskongenSim/Core/SaveLoadManager.cs:85` — in-place file writes and silent corrupt-file handling (also `Average()` on empty sequences in F519); write temp then rename, validate shape, quarantine as `.corrupt` and try the next candidate.
- [F709] `godot/scripts/autoload/GameBridge.Core.cs:424` — `Enum.TryParse` accepts numeric strings and `(int)decimal` casts can overflow outside the try; add `Enum.IsDefined` and clamp or reject out-of-range capital.
- [F1100] `godot/scripts/autoload/SaveLoadManager.cs:130` — absent `EventBus` turns a completed save/load into `SaveFailed`; use `GetNodeOrNull(...)?.PostNotification` outside the try.
- [F777] `godot/scripts/bridge/SimActions.gd:40`, [F639] `godot/scripts/ui/MainMenu.gd:166` — missing scenario silently starts the default game and returns true, and the menu ignores the result; return false on non-default failure and toast.
- [F783] `godot/scripts/multiplayer/MultiplayerLobby.gd:267` — host settings unvalidated (`int("abc")` = 0), max players is the map count, join flips phase with no failure path; validate and gate on a connected signal.
- [F606] `godot/scripts/ui/Dashboard.gd:809` — refused watch-mode change gives no feedback; check the bool and toast "over budget".
- [F663] `godot/scripts/ui/NorwayCoastline.gd:31` — load failures uncached (warning every draw), unguarded `data["projection"]`, `NotificationCenter` coroutine never resumes after tween kill; cache a sentinel, use `.get`, connect to `finished`.
- [F755] `godot/scripts/visual/FeedBargeVisualManager.gd:145` — tweens not stored or killed, so stale callbacks can remove a new barge; store and `kill()` the tween.
- [F802] `godot/theme/default_theme.gd:173` — saved `ui_scale` applied unbounded; `clampf(scale, 0.8, 1.25)`.
- [F803] `godot/theme/default_theme.gd:301` — `Color(raw)` turns invalid strings into black and reload keeps stale values; use `Color.from_string` and reset fields before reload.
- [F879] `src/LakskongenSim.Tests/FinanceTests.cs:29` — finance tests mirror the implementation formula and skip zero/negative inputs; use independent literals and add invalid-input cases.
- [F892] `src/LakskongenSim.Tests/Performance/PerformanceTests.cs:612` — test pins swallowed exceptions in `LazyEvaluator` and misses the re-entrant `Schedule` crash; log failures and add a nested-schedule test.
- [F378] `src/LakskongenSim/Biology/GrowthModel.cs:161` — NaN is silently turned into weight 0 (biomass wipe) and configs/genomes are unvalidated; guard non-finite values at the boundary, validate configs, use `TryGetValue`.
- [F226] `src/LakskongenSim/Core/AquacultureCalibrationEngine.cs:92` — `ToDictionary` on external `RegionId` throws on duplicate or empty ids outside any try; build tolerantly and wrap the call with a `Default()` fallback.
- [F257] `src/LakskongenSim/Core/MultiplayerManager.cs:324` — `AdvanceDay` divides by zero with no players and turn order uses unstable dictionary order; keep an ordered list and guard empty (dormant).
- [F263] `src/LakskongenSim/Core/ResearchSystem.cs:147` — unknown research ids and active research dropped silently on restore; log, keep a pending set and refund the dropped cost.
- [F093] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:290`, [F277] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:288`, [F278] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:98` — `StockSmolts(count <= 0)`, NaN/huge `TakeLoan` amounts, colliding `loan_<day>` ids and undefined `InsuranceKind` pass validation; add guard clauses, `double.IsFinite`, `Enum.IsDefined`.
- [F326] `src/LakskongenSim/Core/SteamWorkshopManager.cs:236` — installed mod deleted before `Directory.Move` with no cleanup (simulated backend); stage, swap with a `.bak`, return a result type.
- [F322] `src/LakskongenSim/Core/TutorialSystem.cs:248` — bare `catch`, always-false numeric stub and per-tick unknown-condition spam; validate conditions at load and log once.
- [F088] `src/LakskongenSim/Data/RealData/Cache/DataCache.cs:105`, [F209] `src/LakskongenSim/Data/RealData/Layers/SstLayer.cs:67` — unhandled cache writes, swallow-all catches and `GeoCoordinate` losing its location on round-trip (loaders unwired); catch `IOException`/`JsonException` with logging, write via temp file, add a `[JsonConstructor]`.
- [F474] `src/LakskongenSim/Environment/WaterChemistry.cs:114` — silent end-of-step clamps, unknown-zone fabricated data, unchecked terrain resolution and unvalidated `ImportState`; warn on cap hits, validate enums and `res` range.
- [F508] `src/LakskongenSim/Events/CrisisCapitalismSystem.cs:262` — `TriggerCrisis` uses raw severity, can emit duplicate ids and empty crises silently; clamp once, guard duplicates and log unknown competitors.
- [F562] `src/LakskongenSim/Expansion/VerticalIntegrationSystem.cs:104` — `IsUnlocked` fails open on unknown keys and `Convert.ToSingle` breaks on `JsonElement`; use a typed condition record.
- [F540] `src/LakskongenSim/Logistics/WellboatJob.cs:16` — free-form `Kind` string; unknown kinds are charged 150k and executed as harvest; use an enum with `TryParse` at the bridge.
- [F412] `src/LakskongenSim/Market/MarketSimulation.cs:54` — historical price path never resolves, `LoadFromMarkdown` fails silently and the parser accepts non-weekly rows; embed the file, log, validate `YYYY Week N` rows.
- [F557] `src/LakskongenSim/Performance/LazyEvaluator.cs:51` — scheduling a new priority from inside an action throws "collection modified" and the bare catch hides failures (no callers); snapshot the queue and log.
- [F019] `tools/validate_scenes.sh:78` — missing result sentinel counts as success; fail when it is absent and add `SCRIPT ERROR` to the failure pattern.

#### Security

- [F708] `godot/scripts/autoload/GameBridge.Depth.cs:783` — public `SpawnQaBloom`/`StartQaScenario` and achievement debug calls have no build gate; guard with `OS.IsDebugBuild()`/`HasFeature("qa")`.
- [F613] `godot/scripts/ui/DataExportPanel.gd:592`, [F634] `godot/scripts/ui/ModManagerPanel.gd:276`, [F683] `godot/scripts/ui/TooltipSystem.gd:13` — dynamic text (paths, mod descriptions, tooltips) is concatenated into BBCode; escape with `replace("[", "[lb]")`. F683 is dead code that also cannot compile (`set_max_size`, `is_visible`): delete it.
- [F919] `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:49` — no tests for traversal, rooted or empty save names; validate in the library and add `[Theory]` cases.
- [F828] `scripts/update_aquaculture_snapshot.py:20`, [F234] `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs:143` — base and token URLs unvalidated while secrets are attached (F234: `BearerToken` also serialisable); require https and an allow-listed host, mark `[JsonIgnore]`.
- [F097] `src/LakskongenSim/Core/ModLoader.cs:562` — manifest paths escape the mod folder, `Enabled` settable from the manifest, effect values unchecked; require `GetFullPath(...).StartsWith(modFolder)`, `[JsonIgnore]`, range-check.
- [F258] `src/LakskongenSim/Core/MultiplayerManager.cs:416` — trade offers lack caller identity, balance and counterparty checks (multiplayer deferred); add acting ids and validation.

#### Performance

- [F598] `godot/scripts/ui/FinancePanel.gd:762` — `_kpi_tweens` grows forever; do not store tweens, or keep one per label and `kill()` it.
- [F636] `godot/scripts/ui/FjordCameraHint.gd:38` — `_process` polls with `tr()` and allocates the pen list each frame; add a `selection_changed` signal and cache strings.
- [F660] `godot/scripts/ui/ProductionChainPanel.gd:122` — hidden cached panels refresh on every `state_updated` with ~20 bridge calls; skip when `not is_visible_in_tree()` and mark stale.
- [F917] `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:410`, [F866] `src/LakskongenSim.Tests/Integration/CampaignSmokeTests.cs:91`, [F851] `src/LakskongenSim.Tests/Balance/BalanceReportTests.cs:29` — wall-clock budgets under default parallelism (5 s limits vs ~100 ms reality) and untagged slow sweeps; use `Stopwatch`, a non-parallel collection and `[Trait("Category","Slow")]`.
- [F133] `src/LakskongenSim/Core/InDayTickSystems.cs:103`, [F290] `src/LakskongenSim/Core/SimulationEngine.Licence.cs:507` — per-population `FirstOrDefault` closures and uncached research LINQ in the daily loop; resolve zone/site once per site and cache effect totals.
- [F446] `src/LakskongenSim/Data/RealData/Loaders/EmodnetDataLoader.cs:198` — synthetic generation unbounded (937k points) and loops forever at resolution <= 0 (unwired); validate, cap cell count, iterate by index.
- [F477] `src/LakskongenSim/Environment/FastNoise.cs:127` — trig per corner per octave and per-call 4x4 arrays in `StormRiskNext3Days`; use a 4-entry gradient table and cache by state.
- [F564] `src/LakskongenSim/Integration/DashboardDataProvider.cs:546` — `GetCostBreakdown()` re-run 12 times per forecast; hoist out of the loop and cache per sim day.
- [F543] `src/LakskongenSim/Optimization/RemoteOpsCentre.cs:25` — culture-sensitive allocating `ToLower()` in per-tick helpers and mutable public tables; use an `OrdinalIgnoreCase` dictionary or enum and frozen tables.
- [F556] `src/LakskongenSim/Performance/EnvironmentalDataCache.cs:87` — `Invalidate` ignores its bounds and flushes everything, expired entries never evicted (no callers); decode key bounds, sweep on `Store`, cap size.

#### Architecture

- [F011] `.claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py:155` — CI steps try a live editor bridge first (only `autoload-audit` actually changes); pass `--no-mcp` from `tools/ci.sh`.
- [F187] `.gitignore:1` — history holds ~125 MB of bin/obj and screenshots, no `.gitattributes`/LFS; `git gc`, add `.gitattributes` (`*.sh text eol=lf`, LFS patterns); purge only if a force-push is acceptable.
- [F735] `godot/scripts/autoload/MultiplayerBridge.cs:157` — `GetLeaderboard()` re-emits its signal, `Enum.Parse` throws, decimal args are not Variant-callable, no consumer; add a non-broadcasting getter, `TryParse`, double/string money, drop the autoload.
- [F781] `godot/scripts/data/Fish.gd:48` — unreachable GDScript growth model duplicates C# `FishPopulation` with wrong formulas; delete `Fish.gd`, `Hatchery.gd`, `GameManager.gd`.
- [F574] `godot/scripts/ui/AIFeedingPanel.gd:86`, [F604] `godot/scripts/ui/Dashboard.gd:575`, [F661] `godot/scripts/ui/PriceGraph.gd:8`, [F684] `godot/scripts/ui/TelemetryPanel.gd:13`, [F785] `godot/scripts/bridge/SimProxies.gd:413`, [F758] `godot/scripts/environment/FishPenManager.gd:450`, [F999] `godot/translations/translations.csv:543` — sim thresholds, recipes, costs and stocking sizes copied into UI scripts/strings and already drifting (e.g. O2 5.0 vs banner 6.0, lice 0.2 vs 0.15); expose them from the bridge or calibration and read them in one place.
- [F599] `godot/scripts/ui/DecisionPanel.gd:20`, [F518] `src/LakskongenSim/Advisor/IntelligentAdvisor.cs:28`, [F230] `src/LakskongenSim/Core/DaySummary.cs:127`, [F396] `src/LakskongenSim/Farm/MultiFarmManager.cs:68`, [F904] `src/LakskongenSim.Tests/Performance/PerformanceTests.cs:21`, [F1085] `src/LakskongenSim/Performance/EconomicCalculationCache.cs:11` — unreachable UI panels and sim modules (~4,000 lines) kept alive by their own tests and docs; execute the plan in `docs/NEXT_LEVEL_PLAN.md` (delete or archive) and trim allowlist lines.
- [F485] `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:148`, [F489] `src/LakskongenSim/Health/FishWelfare.cs:14`, [F415] `src/LakskongenSim/Market/CurrencySystem.cs:222` — LOD/stochastic tiers, welfare score and currency are ticked or constructed but nothing consumes them (currency headlines announce non-existent effects); wire them with probe tests or stop ticking.
- [F372] `src/LakskongenSim/Biology/GrowthModel.cs:61`, [F284] `src/LakskongenSim/Core/SimRng.cs:41`, [F353] `src/LakskongenSim/Finance/EconomicEngine.cs:52`, [F535] `src/LakskongenSim/Optimization/HarvestOptimizer.cs:56` — `CreateUnseeded()`, target-typed `= new()` Random and `DateTime.Today` evade `DeterminismBanTests`; require an injected stream or sim date and extend the regex.
- [F373] `src/LakskongenSim/Biology/PlanktonBloom.cs:239` — `GetAllBloomStates` leaks live mutable state that the spring-bloom event depends on; return clones and add `ForceBloom(...)`.
- [F368] `src/LakskongenSim/Biology/WaterQuality.cs:16` — config held in process-global mutable statics defined in four places; inject an immutable record from `CalibrationRegistry`.
- [F551] `src/LakskongenSim/Calibration/CalibrationRegistry.cs:126` — `Load(dataPath)` vs `Default` split (market, storm read `Default`); pass the engine's registry instance in.
- [F108] `src/LakskongenSim/Core/GameState.cs:12` — 12 folders form one dependency cycle through `Core`, namespace roots mixed (`Lakskongen.*`/`LakskongenSim.*`); extract a Kernel namespace, normalise RealData namespaces and correct Architecture.md.
- [F241] `src/LakskongenSim/Core/GameState.cs:93` — no schema version and event-log trimming by convention; add `AppendEvent` and `SchemaVersion`.
- [F068] `src/LakskongenSim/Data/RealData/RealDataConfig.cs:13`, [F205] `src/LakskongenSim/Data/RealData/RealDataConfig.cs:23` — CWD-relative cache directory breaks in a Finder-launched app, memory cache unbounded, and `UseLiveSources`/`OfflineMode`/`ApiTimeoutSeconds` are unread; default to a user dir, add `Validate()`, drop dead options.
- [F208] `src/LakskongenSim/Data/RealData/RealDataOrchestrator.cs:48` — fake-async getters, stringly layer ids and hand-synced switch tables that already drift; use ids/metadata on the layer and synchronous methods until loaders exist.
- [F423] `src/LakskongenSim/Market/ExportMarket.cs:8` — duplicate `ExportMarket`/`ProductType`/`QualityGrade` types and split `Market`/`Markets` namespaces; delete unused ones and keep one export-market identity.
- [F404] `src/LakskongenSim/Farm/SeaSite.cs:25` — `IsClosedContainment` bool and `Containment` enum disagree on `SemiClosed`; derive the bool from the enum.
- [F534] `src/LakskongenSim/Optimization/HarvestOptimizer.cs:29` — advisor price/treatment tables contradict the sim's calibrated data (Jan 0.90 vs +3.2 NOK); read from `CalibrationRegistry`/`DelousingSystem` profiles.
- [F924] `src/PerformanceBenchmark/Program.cs:14` — perf budgets duplicated with different workloads in `PerfGateTests`; keep one gate with shared constants.

#### Testing

- [F008] `.claude/skills/ecc/godot-mcp-ops/mcp_client.py:14` — module-scope `import websockets` with no installed dependency kills the CI steps before headless fallback; install it or import lazily.
- [F804] `godot/tools/terrain_stats.gd:16` — QA script always exits 0 and breaks without `--headless`; `quit(1)` on non-determinism and hash bytes.
- [F921] `src/LakskongenSim.LogisticsTests/LogisticsTests.cs:51` — tautological "IsDeterministic" test on a module the sim never uses; add haversine/no-vehicle cases or delete the module.
- [F927] `src/LakskongenSim.SaveLoadTests/CohortQualitySaveTests.cs:99` — pins Standard quality although the scenario stocks postsmolt/robust; decide the intent and use a purpose-built fixture.
- [F836] `src/LakskongenSim.Tests/Advisor/IntelligentAdvisorTests.cs:126`, [F902] `src/LakskongenSim.Tests/Integration/OptimizerIntegrationTests.cs:130` — tests depend on the wall-clock date (rare flake days, regime never reached); inject a fixed date and force the condition.
- [F852] `src/LakskongenSim.Tests/Core/ModLoaderTests.cs:23` — `Dispose` never runs (no `IDisposable`), leaking temp dirs; implement the interface.
- [F330] `src/LakskongenSim.Tests/Core/SteamWorkshopTests.cs:105`, [F853] `src/LakskongenSim.Tests/Core/SteamWorkshopTests.cs:141` — tautological update assertion, masked rescan, no failure paths, ~6 s of sleeps and lexicographic version compare untested; assert on a copy, inject latency, add `IsNewer` tests.
- [F876] `src/LakskongenSim.Tests/Environment/TrafficLightTests.cs:25` — "AppliesOncePerChange" calls the consequence once and string-compares enums; drive Green-Yellow-Green through the engine.
- [F875] `src/LakskongenSim.Tests/IndividualFishTests.cs:336` — assertions wrapped in `if (Length > 0)` and a `Thread.Sleep(10)`; assert non-empty first and use a clock abstraction.
- [F877] `src/LakskongenSim.Tests/Integration/CareerDivergenceProbe.cs:143` — probe parses localized event text and never checks the control arm; read numeric fields and assert zero forced harvests in B.
- [F899] `src/LakskongenSim.Tests/Integration/PnLBookkeepingTests.cs:54` — OpEx test passes without the loan; compare loan vs no-loan engines.
- [F012] `src/LakskongenSim.Tests/LakskongenSim.Tests.csproj:8` — 64.5% line coverage, no collector or gate; add coverlet to all test projects and a ratcheting floor.
- [F142] `src/LakskongenSim.Tests/Performance/PerfGateTests.cs:15` — hard limit ~16x looser than measured and soft target never asserted; assert closer to the AGENTS.md target and add allocation checks.
- [F900] `src/LakskongenSim.Tests/Phase2Tests.cs:15` — "SaveLoadRoundTrip" uses default `JsonSerializer`, not `SaveJson.Options`; use the real serializer or delete as duplicate.
- [F494] `tests/Health/DiseaseTests.cs:40` — vacuous range check and no tests for `VaccinationProgram`, `ConfigureDiseaseShare`, per-method lice kill or external pressure; add them.
- [F025] `tests/Health/LakskongenSim.Health.Tests.csproj:6`, [F926] `tests/Persistence/SaveLoadTests.cs:245` — xunit 2.4.2/2.9.2 mix and duplicated save tests across two projects; central package management, merge the suites.
- [F911] `tests/Processing/IntegrationTests.cs:87` — unseeded `CompetitorAISystem`/`StockMarketSystem`, acquisition test passes only because no rival is rich enough; use seeded constructors.
- [F913] `tests/Processing/ProcessingTests.cs:23` — over-wide bands (e.g. `InRange(0,1)` for 1.5 giving 0.85) and display-name lookups; use exact values and stable ids.
- [F016] `tools/ci.sh:382` — no gdlint/gdformat, ruff, `dotnet format` or dependency automation; add the steps, `.editorconfig` and Dependabot.
- [F411] `src/LakskongenSim/Market/MarketSimulation.cs:631` — calibration-fit harness scores a different process than the live model and the test only asserts finiteness; share the daily step and compare distribution statistics.

#### Code quality

- [F023] `.github/workflows/ci.yml:35` — duplicate `verify.yml`, tag-pinned action, no `permissions`/`timeout-minutes`/artifacts and `cancel-in-progress` on main; merge workflows, pin by SHA, upload logs.
- [F015] `godot/LakskongenGame.csproj:4`, [F711] `godot/scripts/autoload/GameBridge.Core.cs:51` — nullable annotations used but `Nullable` not enabled (29 warnings), debug prints through the bridge; enable it via `Directory.Build.props`, fix warnings, route prints through a debug logger.
- [F184] `godot/addons/gd-retroterrain:0` — orphaned gitlink with no `.gitmodules`; `git rm --cached` it.
- [F975] `godot/assets/models/pen_cage/pen_net_material.tres:8` — stale `shader_parameter` keys the shaders do not declare; remove and add a CI uniform check.
- [F1020] `godot/data/encyclopedia_entries.json:1` — 81 entries parsed and discarded, mojibake in 11 and 30 dangling `relatedIds`; wire it in or delete, re-encode and test link resolution.
- [F980] `godot/scenes/environment/FjordEnvironment.tscn:205` — hand-written UIDs, stale `node_paths` and wrong `load_steps`; re-save scenes in the editor.
- [F710] `godot/scripts/autoload/GameBridge.Depth.cs:1216` — 1,434-line partial and a 130-line `GetCarbonLedger` repeating one block three times; extract `Aggregate(...)` and move carbon/sludge/transfers to their own partials.
- [F998] `godot/scripts/autoload/LocalizationManager.gd:34` — rewrites its file on every boot, leftover prints, missing language-name keys, second settings file; save only on change.
- [F778] `godot/scripts/bridge/SimFormatters.gd:62` — ~75 unused public helpers and colour thresholds that contradict `SimState`; delete or source from tokens.
- [F784] `godot/scripts/bridge/SimFormatters.gd:11` — shadowed built-ins (`sign`, `seed`, `ready`), debug prints, unused constants, ordinal smolt tier; rename, drop prints, pass tier by id.
- [F995] `godot/scripts/bridge/SimFormatters.gd:40` — season display strings double as `SEASON_ICONS` keys; return stable ids and translate only for display.
- [F1127] `godot/scripts/bridge/SimState.gd:292` — alert text hard-coded in mixed languages and `ConsequenceDirector` classifies by substring; use `{id, level, params}`.
- [F756] `godot/scripts/environment/FishPenManager.gd:550` — double `pen_added`, stale selection after remove, collar sunk to 30% depth, NaN for `max_pens == 1`; fix each.
- [F759] `godot/scripts/environment/FjordCamera.gd:1` — `@tool` camera fights the editor, discrete zoom, frame-rate-dependent lerp, mouse mode not restored; guard `is_editor_hint()` and use `1 - exp(-k*dt)`.
- [F940] `godot/scripts/environment/Sim3DSync.gd:153` — `_sync_bloom` queries a PascalCase method the facade lacks; delete it (FjordVistaBridge owns `algae_density`).
- [F800] `godot/scripts/tools/ScreenshotHarness.gd:600` — black-band diagnostics and shots 14-16 left in production harness; remove or gate behind `--diag`.
- [F801] `godot/scripts/tools/ScreenshotHarness.gd:152` — 762 lines with copy-pasted audit loops and unrestored `content_scale_factor`; extract `_measure_targets` and restore state.
- [F591] `godot/scripts/ui/ContractsPanel.gd:214` — `link_vertical_focus` on non-focusable rows is a no-op plus dead symbols; link the actual buttons and delete unused members.
- [F590] `godot/scripts/ui/CrewPanel.gd:126`, [F662] `godot/scripts/ui/NotificationCenter.gd:49` — hard-coded NB/EN strings and `tr()` on format strings or sentences; move to `UI_*` keys with `_tr_or` fallbacks.
- [F611] `godot/scripts/ui/Dashboard.gd:132` — dead `_juice` node, unused `NIGHT_WATCH_*`, toasts promise work that only routes to a tab, typo; delete and reword.
- [F615] `godot/scripts/ui/Dashboard.gd:1` — 856 lines, 50+ line builders, 315 lines of data in `EncyclopediaPanel`, copied hex palettes, stale comments; split sections and use theme tokens.
- [F997] `godot/scripts/ui/HUD.gd:162` — `_tr_or` copy-pasted into 15 scripts (plus C#) and `t != key` conflates missing with identical; move to one helper.
- [F638] `godot/scripts/ui/JuiceEffects.gd:190` — no reduced-motion opt-out; add `accessibility.reduce_motion` and snap to end state.
- [F637] `godot/scripts/ui/GameUI.gd:53` — debug `print()`s, scattered palettes and 801-line `MapPanel`; use `push_warning`, tokens and a JSON for regions.
- [F635] `godot/scripts/ui/MainMenu.gd:70` — `attach_press_feedback(btn)` on a leaf Button is a no-op and hover tween uses top-left pivot; use the BaseButton variant and set `pivot_offset`.
- [F685] `godot/scripts/ui/StockMarketPanel.gd:321` — `_format_currency`, `_clear_container` and Esc handler copied across panels, dead constants; move to shared helpers.
- [F979] `godot/scripts/visual/generate_normal_maps.gd:13` — stray root-level ocean PNGs, generator saves to cwd, and audio/scene paths that do not exist; delete, write to `res://assets/textures/water/`, guard loads.
- [F974] `godot/shaders/fjord_sky.gdshader:13` — three orphan shaders, two with the non-existent `hint_default_noise`; delete them.
- [F978] `godot/shaders/sky_shader.gdshader:80` — reversed-edge `smoothstep`, dead assignments, unused uniforms, Vulkan-only depth math; rewrite as `1.0 - smoothstep(...)` and remove dead code.
- [F805] `godot/theme/default_theme.gd:326` — 811 lines, a 200-line `_build_theme`, `Variant`-overloaded `create_button`, duplicate colour consts; split the file and type the API.
- [F806] `godot/ui/Dashboard.gd:4`, [F1036] `scripts/Environment.gd:4`, [F825] `scripts/fix_ui_T66.py:7`, [F194] `src/LakskongenSim/warnings.log:1` — orphan redirect stub, Godot-3 relic, finished codemods and tracked build logs; `git rm` them and add `*.log` to `.gitignore`.
- [F829] `scripts/update_aquaculture_snapshot.py:18` — `mkdir` at import, clock-derived year/week and timestamps; create the log dir lazily and add `--as-of`.
- [F830] `scripts/update_aquaculture_snapshot.py:326` — unnamed baselines/bands and 50+ line functions; hoist constants into a documented dataclass and split `build()`.
- [F918] `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:91` — backup content checked with JSON substrings (`"Day": 10` matches 100); deserialize the backup.
- [F854] `src/LakskongenSim.Tests/Balance/SimProbe.cs:240` — 861 lines with five copies of the harvest-cycle loop; extract `RunCycleLoop` and a generic `Sweep`.
- [F856] `src/LakskongenSim.Tests/Environment/AppetiteTests.cs:4` — mismatched namespaces (`StormPhase12`, `DataLayers`) and duplicate growth tests; align with folders and merge.
- [F855] `src/LakskongenSim.Tests/Environment/InterventionAdvisorTests.cs:40` — assertions on localized display text; assert on stable `Id`/enum.
- [F881] `src/LakskongenSim.Tests/Environment/StormForecastTests.cs:25` — clamp-masked and DTO-echo tests, `>=` where `>` is meant; assert pre-clamp rows and strict ordering.
- [F905] `src/LakskongenSim.Tests/PlanktonBloomTests.cs:127` — reflection on private `_events`, unused locals, console prints and swallowed cleanup; use `GetEventDefinitions()`, `ITestOutputHelper`.
- [F502] `src/LakskongenSim/Advisor/InterventionAdvisor.cs:32` — `Rank()` takes four unused parameters and mixes locale formats; drop them and use `nb-NO` consistently.
- [F381] `src/LakskongenSim/Biology/GrowthModel.cs:153` — Box-Muller copied four times, duplicated FCR overloads and an overload trap on `dt`; add `NextGaussian()` and share the penalty code.
- [F567] `src/LakskongenSim/Calibration/CalibrationRegistry.cs:499` — "immutable" registry exposes a mutable array and `*Percent` fields mix fractions and percents; use `IReadOnlyList` and rename.
- [F1046] `src/LakskongenSim/Core/AchievementSystem.cs:125` — tier/hidden/category/icon plumbed with no data or consumer; add tier to config and expose metadata, or drop the enum.
- [F239] `src/LakskongenSim/Core/AnalyticsTracker.cs:230` — moving-average doc contradicts code, O(n*w) slices, typo `relativeSope`, magic fallbacks; fix doc and use a running sum.
- [F235] `src/LakskongenSim/Core/AquacultureDataIntegration.cs:85` — two inconsistent composite-risk formulas and a neutral value on the "Favorable" boundary; one weighted function and exclusive thresholds.
- [F238] `src/LakskongenSim/Core/AudioManager.cs:71` — alert "queue" is one slot, unvalidated keys, season calendar differs from other systems; use a queue and the shared calendar.
- [F262] `src/LakskongenSim/Core/InDayTickSystems.cs:190`, [F315] `src/LakskongenSim/Core/SimulationEngine.cs:1678`, [F328] `src/LakskongenSim/Core/TutorialSystem.cs:198`, [F356] `src/LakskongenSim/Finance/CompetitorAI.cs:545`, [F493] `src/LakskongenSim/Health/SeaLiceModel.cs:159`, [F537] `src/LakskongenSim/Processing/ProcessingManager.cs:30`, [F287] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:311`, [F560] `src/LakskongenSim/Integration/DashboardDataProvider.cs:437` — numeric literals in tick/helper code bypass `CalibrationRegistry` and the AGENTS.md literal-scan guard does not exist; move values to registry sections and add the scan test.
- [F266] `src/LakskongenSim/Core/InDayTickSystems.cs:99` — dead Stopwatches, empty loops, a no-op tick system and reflection for `Balance`; delete and use a `FinanceManager` setter.
- [F1034] `src/LakskongenSim/Environment/Zone.cs:225` — stacked, inert clamps on external-pressure multipliers; keep one named band per value and `Debug.Assert` the rest.
- [F172] `src/LakskongenSim/Core/SimulationEngine.cs:227` — 3,901-line engine with 340-line `Initialize`, dead types, `Assert.True(true)`, stray absolute paths; split by concern and remove dead code.
- [F286] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:483` — `AdoptSavedState` doc stale, 175-line hand copy list and undocumented seed precondition; add a round-trip reflection test and guard the seed.
- [F289] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:247` — `HarvestSite` leaves `MortalityModel._environmentalStress` entries; call `ClearEnvironmentalStress(pop.Id)`.
- [F316] `src/LakskongenSim/Core/SimulationEngine.cs:1735` — unused Stopwatch/constants, loops that never run, enum-as-string compares, culture-formatted persisted text; delete and use `CultureInfo.InvariantCulture`.
- [F1018] `src/LakskongenSim/Core/SimulationEngine.cs:823` — dead `EnvironmentalEvents` array and sections, `Data/*.json` not copied to output, `PlanktonBloom_LoadsConfigFromJson` only asserts `NotNull`; embed the JSON and assert a non-default value.
- [F329] `src/LakskongenSim/Core/SteamWorkshopManager.cs:163` — `UploadModAsync` mutates the caller's `WorkshopItem` and lets null manifest fields in; build a new item and default `Version`/`Description`.
- [F334] `src/LakskongenSim/Core/TutorialSystem.cs:243` — unreachable day-condition branches, stub parser, 230-line legacy track, culture-sensitive id formatting; delete and use `InvariantCulture`.
- [F566] `src/LakskongenSim/Data/Enums.cs:10` — duplicate `Season`/`DelousingMethod`/`ProductType` enums and 13 unused enums plus a stale `GameConstants`; delete.
- [F450] `src/LakskongenSim/Data/RealData/Interpolation/IdwInterpolator.cs:101` — unused interpolators, fields and cache metadata, fake-async orchestrator getters; delete or wire.
- [F213] `src/LakskongenSim/Data/RealData/Layers/SstLayer.cs:219` — unused `_spatialInterpolator`/`_temporalInterpolator` and a mislabeled "daily variation" annual harmonic; remove and use `float[12]`.
- [F217] `src/LakskongenSim/Data/RealData/Loaders/EmodnetDataLoader.cs:213` — orphaned loader stamps synthetic points as "emodnet", 30 s timeout, no retry or User-Agent; label as synthetic or delete.
- [F451] `src/LakskongenSim/Data/RealData/Loaders/EmodnetDataLoader.cs:192` — seeded `System.Random`, inline climatology tables, 100-line IDW method, split namespaces; use `SimRng`, move tables to calibration.
- [F507] `src/LakskongenSim/Events/CrisisCapitalismSystem.cs:334` — dead private overload and `GenerateAssetValue` plus stale allowlist lines; delete and make the ban test fail on stale entries.
- [F517] `src/LakskongenSim/Events/NorwegianEvents.cs:1` — 1,019 lines of 52 near-identical registration blocks; use a `CashEvent(...)` factory or JSON.
- [F565] `src/LakskongenSim/Expansion/AcquisitionSystem.cs:60` — dead pricing/antitrust methods, RNG draw inside a quote, float money; remove or wire with decimal and calibration thresholds.
- [F122] `src/LakskongenSim/Expansion/VerticalIntegrationSystem.cs:24` — NOK as float/double/decimal across subsystems; use decimal for all `*NOK` values.
- [F405] `src/LakskongenSim/Farm/DelousingSystem.cs:290` — culture-sensitive `ToLower`/`N0`, unguarded `(decimal)double`, copy-pasted construction logic, unseeded default RNG; use invariant APIs, `double.IsFinite`, a shared tracker.
- [F424] `src/LakskongenSim/Market/MarketChannel.cs:194` — ~790 lines with no production caller and per-transaction slippage; wire it or delete and compute slippage from cumulative volume.
- [F422] `src/LakskongenSim/Market/MarketSimulation.cs:316` — 106-line `AdvanceDays`, Box-Muller copied four times, dead constants; split into helpers and share `NextGaussian()`.
- [F475] `src/LakskongenSim/Sustainability/SustainabilityManager.cs:154` — duplicate `CalculateScore` overloads, stranded doc, truncating `(int)`, mutable static zone table; merge, round, make read-only.
- [F542] `src/LakskongenSim/Optimization/ForecastTwin.cs:22` — growth constants copied and engine multipliers omitted from the projection; reference the engine constants and share the factor helper.
- [F923] `src/PerformanceBenchmark/Program.cs:89` — `Zone("PO-3", 65.0)` passes 65 as temperature inside a swallow-all catch, 127-line function; remove the try/catch and name constants.
- [F024] `tools/ci.sh:140` — copy-pasted Python resolution whose "not found" guard is unreachable under `set -e`; use `$PYTHON` and a helper.
- [F824] `tools/gen_store_art.py:551` — `verify()` failure exits 0, dead assignments and unclosed images; return a bool and exit 1.
- [F821] `tools/mcp_bridge_alias.py:164` — no real CLI parsing (`--help` writes the registry), inverted failure label, doc references a non-existent `--repair`; use `argparse` and fix the label.
- [F285] `src/LakskongenSim/Core/SimRng.cs:55` — `Stream("x")` and `Stream("x", "")` share a key, `NextSingle()` can return 1.0f, `Next(min,max)` span overflows, `ImportState` accepts bad cursors, `SimRandom` is test-only; reject empty suffixes, scale `NextSingle`, compute spans in `long`, validate cursors.
- [F495] `src/LakskongenSim/Health/SeaLiceModel.cs:243` — culture-sensitive `ToLower`, reflection for resistance, unreachable salinity branch and a wrong `ExternalPressureMultiplier` doc; use an enum/`OrdinalIgnoreCase` table, a `ref double` and fix the docs.

#### Documentation

- [F120] `AGENTS.md:4`, [F170] `README.md:8`, [F193] `README.md:48`, [F171] `CONTRIBUTING.md:83` — test counts, CI step counts, versions (.NET 8/Godot 4.3), non-existent guards, broken links and stale API reference contradict the code; regenerate counts from CI, rewrite the quick start from `tools/ci.sh`, add a LICENSE, `[Unreleased]` and `config/version`.
- [F941] `CONTRIBUTING.md:214` — GDScript example calls `GameBridge.DayAdvanced`/`HarvestFish`, which do not exist; rewrite against `SimBridge`.
- [F212] `docs/TODO_REGISTRY.md:25` — stale RealData TODO lines and unbundled `.mcp.json`/runbook changes; update the registry and split commits.
- [F757] `godot/scripts/environment/FishPenManager.gd:237` — pen health is colour-only and the 3D label is tiny, unbillboarded and unlocalised; add a non-colour cue and `BILLBOARD_ENABLED`.
- [F782] `godot/scripts/bridge/SimProxies.gd:4` — header claims "pure queries/mocks" but holds ~25 mutators and no mocks; reword or move mutators into `SimActions`.
- [F807] `godot/scripts/tutorial/README.md:3` — six docs describe the retired 23-step GDScript tutorial; delete and point to `TutorialSystem.cs` and `tutorial_steps.json`.
- [F760] `godot/scripts/visual/generate_normal_maps.gd:50` — generator writes to cwd, stale `node_paths`, shadowed `smoothstep`/`name`, unguarded connects; save to `res://assets/textures/water/` and guard with `is_connected`.
- [F186] `godot/translations/strings_de.csv:1` — four byte-identical empty locale stubs and a `strings_no.csv` that is English, while the checklist says "6 languages"; delete or generate them and fix `RELEASE_CHECKLIST.md`.
- [F189] `godot/scripts/autoload/SaveLoadManager.cs:471` — game version has four sources and saves are stamped "1.0.0"; set `application/config/version` and read it everywhere.
- [F1105] `godot/scripts/autoload/GameBridge.Core.cs:193`, [F903] `src/LakskongenSim.Tests/Integration/RuntimePersistenceTests.cs:14`, [F380] `src/LakskongenSim/Biology/GrowthModel.cs:35` — comments claim RNG positions are not persisted and describe wrong thread-safety/equations; rewrite to the current wave-6 behaviour.
- [F878] `src/LakskongenSim.Tests/Integration/EngineTwinRunDeterminismTests.cs:14` — header says Guid-into-state fails the gate but GUIDs are scrubbed, and `FirstDifference` is duplicated; reword and extract a helper.
- [F099] `src/LakskongenSim.Tests/Core/DeterminismBanTests.cs:19` — ban is weaker than documented (81 grandfathered, `new Random(42)` passes), plus oversized files and swallowed bridge exceptions; align docs, widen the regex, log `ex.ToString()`.
- [F280] `src/LakskongenSim/Core/SimRng.cs:121` — "delouse" stream documented as driving deaths but never used; wire it or delete the constant.
- [F288] `src/LakskongenSim/Core/SimulationEngine.Commands.cs:26` — stringly-typed error/status keys with stale docs and culture-formatted event text; add constants and `InvariantCulture`.
- [F240] `src/LakskongenSim/Core/AchievementSystem.cs:12` — "50+ achievements" vs 34 and stale `GameConstants` comments; correct them.
- [F333] `src/LakskongenSim/Core/TutorialSystem.cs:45` — "12-step" docs, stale Balance note and class-wide pragma; update summaries and narrow the pragma.
- [F452] `src/LakskongenSim/Data/RealData/Layers/BathymetryLayer.cs:55` — depth sign contradicts its own doc and `TODO_REGISTRY` is stale; pick one convention.
- [F214] `src/LakskongenSim/Data/RealData/Layers/WindLayer.cs:150` — layers advertise unprovided variables (`wind_direction`, `current_direction`) and free-text units; trim and use unit constants.
- [F476] `src/LakskongenSim/Environment/EnvironmentalDataPoint.cs:95` — several comments contradict the code (Gaussian vs linear, missing normalisation); fix them.
- [F498] `src/LakskongenSim/Farm/DelousingSystem.cs:194` — comments claim compounding delousing harm, trackers list fixed debts as open, faction docs imply a live system; update docs and mark inert features.
- [F541] `src/LakskongenSim/Processing/ProductCatalog.cs:40` — `*Percent` fields hold fractions, dead members and stale comments; rename `*Fraction`.
- [F352] `src/LakskongenSim/Finance/StockMarket.cs:127` — AGENTS.md reports Phase 4.3/4.4 complete while IPO/stock surface is stubbed and ~1,500 lines are unreachable; mark as engine-only and delete legacy `CostModel`/`RevenueModel`.
- [F428] `src/LakskongenSim/Market/ContractSystem.cs:108` — documented literal-scan guard does not exist and docs name APIs that do not exist; add the scan or correct AGENTS.md.
- [F925] `src/PerformanceBenchmark/Program.cs:21` — "37% target" banner and stale comments/test names overclaim; remove or implement.
- [F823] `tools/gen_textures.py:311` — banner prints an unused `SEED` and PNG bytes differ from committed assets; derive per-texture seeds and compare pixels.
- [F831] `tools/gen_store_art.py:32` — Pillow/mcp/websockets undeclared and unpinned; add `tools/requirements.txt` and mention it in CONTRIBUTING.

#### Observability

- [F1125] `godot/scripts/autoload/GameBridge.Core.cs:120` — catch sites log only `ex.Message`; log `ex.ToString()` and keep user text separate.
- [F1103] `godot/scripts/autoload/SaveLoadManager.cs:216` — valid-but-unknown content reported as "corrupted (invalid JSON)"; distinguish parse from binding failure and report newer versions.
- [F799] `godot/scripts/tools/ScreenshotHarness.gd:237` — unseeded fallback and failed `BuildSeaSite`/`stock_smolts` logged as success; check results and `push_warning`.
- [F592] `godot/scripts/ui/BreakEvenAnalysisPanel.gd:46`, [F687] `godot/scripts/ui/ToastNotification.gd:80` — per-tick `push_warning`s, leftover `print()`s and stale doc comments; warn once, gate on `OS.is_debug_build()`.
- [F236] `src/LakskongenSim/Core/AchievementSystem.cs:66`, [F196] `src/LakskongenSim/Core/ModLoader.cs:466`, [F265] `src/LakskongenSim/Core/ModLoader.cs:229`, [F331] `src/LakskongenSim/Core/SteamWorkshopManager.cs:134` — library code writes to `Console` (118 sites) and bare-catches instead of `SimLog`; route through `SimLog` with levels and ban `Console.Write`.
- [F880] `src/LakskongenSim.Tests/Integration/FullCycleEconomicTests.cs:173` — `Console.WriteLine` lost under xUnit and a broken `{survivalRate:P1)}` spec; use `ITestOutputHelper` and fix it.
- [F1146] `src/LakskongenSim/Core/SimulationEngine.cs:1999` — every in-debt day emits ~8 log lines and 4 `OnSpendFailed`; add a one-shot insolvency transition.
- [F379] `src/LakskongenSim/Biology/MortalityModel.cs:173`, [F1068] `src/LakskongenSim/Health/IntegratedDiseaseSystem.cs:265` — per-tick `Debug.WriteLine`/`SimLog.Debug` with interpolation and no death-cause tracking; remove, log on change, default `MinLevel` to Info in release.
- [F920] `src/LakskongenSim.SaveLoadTests/SaveLoadManagerTests.cs:312` — `TryLoadWithFallback` rollback is silent; return the source and the swallowed exceptions.
- [F413] `src/LakskongenSim/Market/MarketSimulation.cs:225` — `OnMarketEvent`/`OnMarketAccessChanged` have no subscribers and `CanAccessMarket` no callers; subscribe to `OnEventTriggered` or delete.
- [F398] `src/LakskongenSim/Farm/OffshoreFarm.cs:84` — facility alert/milestone events unsubscribed, no `SimLog`, daily repeats; surface via tick results and edge-trigger.
- [F539] `src/LakskongenSim/Logistics/LogisticsManager.cs:62` — failures return `double.MaxValue`/null sentinels and the unit never logs; return a result type and validate.
- [F447] `src/LakskongenSim/Data/RealData/Loaders/EmodnetDataLoader.cs:34` — silent catch-alls, fabricated health metrics, per-loader `HttpClient`; inject a shared client, log and track real failures.
- [F215] `src/LakskongenSim/Data/RealData/RealDataOrchestrator.cs:104` — quality and source discarded by bare-float getters; return a `Sample(value, quality, source)`.
- [F827] `scripts/update_aquaculture_snapshot.py:428` — `fetch_optional` logs "Fetched <source>" even when no request was made; log "Skipped (no credentials)" and end with a summary of active sources and `fallbackUsed`.

### Positive Observations

- **Healthy, fast HEAD baseline.** A clean `git archive HEAD` builds and passes 760 tests (1 skipped) across six projects plus 21 pytest. The 688-test project runs in about 9 s, and four runs under 2x CPU oversubscription passed. The engine costs 0.27 ms per simulated day (about 18x under the hard budget), and `Advance(3650)` takes roughly 0.5 s.
- **Strict, dependency-free simulation library.** `src/LakskongenSim` builds with 0 warnings under `Nullable` + `TreatWarningsAsErrors` (a probe confirmed the gate bites), has zero third-party runtime packages, and has no `using Godot`. `dotnet list package --vulnerable` is clean for all seven projects.
- **SimRng is the right determinism foundation.** `Core/SimRng.cs` derives named streams with FNV-1a + SplitMix64, independent of .NET string hashing. It has per-identity suffix overloads (adding a site cannot re-randomise the world) and `CaptureStates`/`ImportStates`. `SimRandom` is pinned bit-for-bit to `System.Random` by an oracle test.
- **Determinism guard rails worth extending.** `DeterminismBanTests` with `DeterminismAllowlist.txt` is a two-way ratchet and caught the new RealData `DateTime.UtcNow` lines immediately. `ExactSaveHashTests` and `EngineTwinRunDeterminismTests` compare whole serialized state and report the first differing JSON path. Engine `Advance(n)` was verified step-size invariant (1/7/30-day steps).
- **Clean engine architecture.** The ordered `ITickSystem` pipeline makes registration order the execution order, and interval clocks are saved by system name (`ClocksBySystem`) so reordering cannot mis-restore. Player verbs return a typed `CommandResult` with stable `ErrorKey`s (`SimulationEngine.Commands.cs`), shared by `GameBridge` and `PlayerScriptProbe`. Load has a single owner, `AdoptSavedState`.
- **Careful save writer.** The Godot `SaveLoadManager` writes a temp file then `File.Move`s over the target (a simulated mid-write crash left the primary intact), refuses newer `SaveVersion`s, and migrates stepwise. The library `SaveLoadManager` rotates `.bak1-3` with `TryLoadWithFallback`. Real v0-v2 saves load via `SaveJson.Options` and keep running for 100 more days with no exceptions. The engine side is sound; the UI wiring (F928) is what is broken.
- **Cheap C#/GDScript seam guard.** `tests/test_bridge_contract.py` statically checks every `game_bridge.<member>` used from GDScript against the `GameBridge*.cs` partials, bans snake_case signal access, and asserts it is not vacuous. A manual arity check of 218 PascalCase call sites found 217 correct. It only needs its `has_method` exemption tightened (F945).
- **Well-built CI entry point.** `tools/ci.sh` is fail-fast with an `ERR` trap and a per-step PASS/FAIL/SKIP summary. It builds the Godot C# assembly that is outside the `.sln`, sandboxes the MCP token for headless editor runs, and wires the perf gate (soft 2 ms, hard 5 ms). `visual-qa.yml` takes its baseline from `origin/main`, so a PR cannot rewrite the reference.
- **Evidence-based calibration.** `CalibrationRegistry` is an embedded resource whose defaults equal `godot/data/calibration.json`, with cited references (Fiskeridir, NVI, Skatteetaten). `CalibrationGuardTests` pin each value with an explicit tolerance. `BalanceReportTests` run seeded sweeps against sourced bands, and the S10 `CareerDivergenceProbe` pre-registers its failure criterion with a vacuity guard.
- **Strong test craft in places.** Storm and persistence tests `Assert.Fail` when no storm is observed instead of passing silently. `NorwegianEventsFinanceTests` has a vacuous-pass guard. `GeneticsDepthTests` use analytic oracles (selection intensities 1.755/0.798/2.665), and `LicenseAuctionTests` and `InsuranceMitigationTests` pin exact-decimal boundaries.
- **Good security hygiene.** No hard-coded secrets in tracked files or the 60 most recent commits, and the BarentsWatch token comes from an environment variable. The vendored MCP runtime server self-disables outside editor launches, binds `127.0.0.1` and requires a token. Mods are data-only JSON with duplicate-ID, missing-dependency and cycle checks.
- **RealData refactor has the right shape.** The orchestrator is constructor-injected behind `IRealDataProvider`, replacing the static singleton and `Console.WriteLine` debugging. Layers are pure functions of (lat, lon, time) with offline-first defaults (`UseLiveSources = false`). `DataProvenance` introduces the right G-LIC concept, and the 10 v2 tests are deterministic and pass once the missing usings are added. `docs/AGENT_RUNBOOK.md` matches `tools/ci.sh` (15 steps).

#### Intentional / not flagged

- **MCP runtime server autoload** in `project.godot`: `mcp_runtime_server.gd` returns early unless `OS.has_feature("editor")`, binds loopback and authenticates, so exports expose nothing. Only the git-ignored addon dependency is flagged (F177).
- **Grandfathered nondeterminism** in `DeterminismAllowlist.txt` (`Guid.NewGuid` ids, `DateTime` stamps): a deliberate ratchet, normalised in the twin-run test and never used for ordering. The exceptions flagged are the runtime-affecting `HashCode.Combine` in `Zone.cs` (F453) and `VerticalIntegrationSystem`.
- **Live game seed** from `new System.Random().Next()` in `GameBridge.StartNewGameInternal`: documented entropy source, persisted in `GameState.RandomSeed`, and QA paths pass an explicit seed.
- **Debt via `if (!Spend(x)) ForceSpend(x)`**: the documented bankruptcy mechanic. Only the feed handler that waives the cost (F1130) and the annual tax that ignores `Spend()` (F342) are flagged.
- **Documented dormant features**: species mods parsed-not-consumed, `high_contrast` stored but not applied, `SteamWorkshopManager` as a labelled simulation behind `#if STEAMWORKS`, and the stubbed empire getters.
- **`MigrateSaveData` as a no-op version-stamp chain**: compatibility is carried by `SaveJson.Options` (case-insensitive binding, string-or-int enums), confirmed by loading real v0-v2 files.
- **`Math.Clamp` as physical or input bounds** (for example `Zone.CurrentSpeed` 0.05-0.5 m/s, sanitising BarentsWatch multipliers, positivity floors in climatology): not the realism-fitting clamps AGENTS.md forbids.
- **RealData placeholder models** (Temperature, Current, Bathymetry, Oxygen, Salinity) are labelled `climatology_only` / quality flag 2 with real loaders on the TODO registry, and `.mcp.json` `$HOME` to `${HOME}` is a correct fix. Only the labelling and logic errors are flagged.

### Suggested Next Steps

_Status 2026-10-14: steps done on branch `review-remediation`; see [CODE_REVIEW_RECHECK.md](CODE_REVIEW_RECHECK.md) for what is fixed, partial and open._

- [x] **Unblock the build (RealData, uncommitted).** Fix `LakskongenSim.Tests` so `dotnet build LakskongenSim.sln -c Debug` succeeds: add the missing usings (and resolve the `CurrentLayer` name clash) in `RealDataLayerTests.cs` and `RealDataLayerTests_v2.cs`, and reconcile the old and v2 test files against the new `IRealDataProvider` API (F197). The ~20 errors hidden behind the first one will surface only once it is fixed.
- [x] **Clear the RealData determinism ban.** Replace the new `DateTime.UtcNow` reads in the layers and orchestrator with an injected timestamp or clock, rather than adding allowlist lines, so `DeterminismBanTests` passes without growing the ratchet.
- [x] **Re-establish the green baseline before anything else.** Run `dotnet test LakskongenSim.sln` and `tools/ci.sh`, and require parity with HEAD (760 passed, 1 skipped, 0 failed; 21 pytest passed). Move the `.mcp.json` fix, `docs/RUN_LOG.md` and `docs/AGENT_RUNBOOK.md` into separate commits from the RealData code.
- [x] **Repair player save/load first.** Fix `SaveLoadMenu.gd` (F928: `GetSaveFiles`/`SaveGame`/`LoadGame` PascalCase through the bridge), the inert main-menu Load and Settings buttons (F616), the permanently disabled menu after cancelling the picker (F617), and the scenario discarded by `Main.gd` (F056). Then play new game -> save -> quit -> load by hand.
- [x] **Close the persistence gaps that fixing F928 exposes.** Persist RAS, offshore and post-smolt facilities (F1069), contracts (F409), the active scenario (F243), full CompetitorAI state (F1071), the shared mortality models for scenario populations (F1072), disease prevalence (F478) and loss carry-forward (F1137).
- [x] **Make determinism real across processes.** Replace `HashCode.Combine` in `Zone.UpdateCurrents` with a stable hash and drop it from the allowlist (F453), give `ForecastTwin` its own RNG stream or a cloned one so UI reads cannot advance the sim (F521, F835), and rebuild `MarketSimulation` in `Initialize` (F292).
- [x] **Widen the determinism gates.** Add a cross-process (subprocess) twin run and a save/load matrix (8 seeds x save days 45/100/200/300 x events on/off) to `ExactSaveHashTests`, and make `PlayerScriptProbe` compare full state rather than counts (F1073, F884). Keep the matrix as a CI step.
- [x] **Fix the economy ledger and add a reconciliation test.** Correct the monthly tax base (F1128), the waived feed cost (F1130), P&L omissions for smolts and site builds (F1132), loan principal booked as OpEx (F1138), resource-rent tax (F1131), the ignored `Spend()` in annual tax (F342), the double certification multiplier (F268) and the bypassed smolt price (F269). Add a test asserting cash deltas equal P&L entries over a 5-year player-path run.
- [x] **Remove the crash and dead-enforcement paths in the engine.** Inject Growth/Mortality on transfers into empty sites (F382), carry quality and genetics through delousing rebuilds (F386), refresh the MAB density cache so enforcement works (F1129), sum zone biomass per zone (F054), stabilise the dissolved-oxygen update (F1024), and close Mattilsynet orders on compliance (F1133). _(F1024 waits on the owner: its fix is on branch `review/f1024-water-chemistry`, RUN_LOG §8.)_
- [x] **Decide wire-or-remove for dead features.** Either connect or hide facility construction (F383), CrisisCapitalism (F499), the shipped research tree and its effect keys (F1001, F1004, F1003, F1027), and the scenario and tutorial chain (F244, F1005, F949, F1014, F1006). Do not leave paid UI actions with no simulation effect. _(Post-smolt still has no player verb, F383.)_
- [x] **Fix the broken Godot panels, then re-promote the visual baseline.** Restore the bridge facade methods and loan, analytics and pen-click wiring (F945, F943, F944, F738), the pen list height (F647), mod toggle clicks (F620), notification ESC handling (F641), Space/Enter capture (F618), level-triggered alert spam (F762) and the tilt-shift black frame (F957). Three committed baseline shots enshrine defects (07d_offshore, the pen list, 11_fjord_underwater), so re-run visual QA and review the diffs before promoting.
- [x] **Harden the tests that cannot fail.** Add mutation checks to the disease tests, which pass with the model removed (F906), replace the tautological CrisisCapitalism test, and widen `tests/test_bridge_contract.py` so `has_method`-guarded names are checked and arity is verified (F945).
- [x] **Make a clean checkout pass the Godot gates.** Vendor or conditionally load the git-ignored MCP addon so the autoload audit passes (F177), and run the Godot-dependent steps that review could not execute (scene validation, GDScript parse scan, shader check, export verify) on a machine with Godot. _(The addon is exempted as optional local tooling rather than vendored, RUN_LOG §7.)_
- [ ] **Run CI remotely for the first time.** `origin/main` is 64 commits behind and no workflow has ever run. Push a branch, fix the workflow-level gaps found statically (pytest install, missing `requirements.txt`, a vacuous export gate), and require all four workflows green before merge. _(2026-10-14: the static part is done in `44a0d89` and `ed688c4`; the push is a human gate, RUN_LOG §9.)_
- [x] **Re-review after fixes.** Re-run this review scoped to the changed files, and compare the severity counts against the baseline of 3 critical, 55 high and 369 medium findings.
