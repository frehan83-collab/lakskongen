# F2-06 offshore sea: notes for the wiring stage

_Branch `wave3/sim-lanes-2`, 2026-10-16. Stage 2 of 2 of the sim-lanes track, F2-06 standalone
(docs/FARM_TYPES_SPEC.md §3.3 "Metocean", the operability table, point 6 "Forecast"; §7 F2-06).
The offshore sea, its forecast and the operability windows ship as standalone components; the
engine wiring (`OffshoreTickSystem`, `OffshoreHooks.OperationPossible`, `GameState.Metocean`, the
bridge's `GetSeaStateForecast`) follows sim-core's F2-02/F2-03. Measurements: docs/BALANCE.md §1n.
Decisions: docs/RUN_LOG.md §7 (2026-10-16 F2-06 rows); open findings: §8._

## 1. What exists

| File | What it is |
|---|---|
| `src/LakskongenSim/Environment/OffshoreMetocean.cs` | `OffshoreMetocean` (the saved state: `LastTickDay`, `ResidualX`, `Events`, `NextSerial`, `WeatherToday`, `BaseHsToday`, `SwellHsToday`, `SwellTpToday`, `VisibilityKmToday`; `TickDay(day, weather, stream)` draws exactly 8 uniforms; `ReferenceHsToday()`, `HsAt(scale)`, `TpAt(scale)`; `ApplyCalibration`), `SwellEvent` (serial, created, onset, plateau days, peak, Tp, estimate error; `ShapeOn`, `LastActiveDay`), `OffshoreMetoceanModel` (the pure pieces the tick, the forecast and the fit share, `CombineHs` the one place base, background and swell meet). |
| `src/LakskongenSim/Environment/MetoceanForecast.cs`, `MetoceanForecast.DayModel.cs`, `SeaStateForecast.cs` | `MetoceanForecast.Issue(state, weather, plotScale, limits, withQuantiles)` → `SeaStateForecast` (10 `SeaStateForecastDay`s: p10/p50/p90 and P(Hs ≤ limit) per limit, `UnresolvedProbability`; `KnownSwell` as `SwellOutlook`s with the peak's p10/p50/p90; `ProbabilityAtOrBelow(lead, limit)`, `Quantile(lead, p)`, `WeatherDistribution(lead)` on demand); `ProbabilityAtOrBelow(state, weatherAtLead, lead, scale, limit)` for one number; the day model (terms and their integration) in the `.DayModel` partial. Pure: never draws, never writes. |
| `src/LakskongenSim/Environment/OperabilityWindows.cs` | `OffshoreOp`, `VesselClass`; `HsLimitM(op, vessel)`, `DaysNeeded` (the tow's 3), `IsPossible(op, vessel, hs, windMs, visibilityKm)`, `WellboatCanStayAlongside`, `AppetiteFactor(hs)`, `Probability(forecast, lead, op, vessel)`, `Outlook`, `FirstWindowLead`, `LongestSpellWithoutWindow`. |
| `src/LakskongenSim/Environment/MetoceanMath.cs` | Normal CDF/quantile in double precision, the sea transform (`UpperBoxCox`: ln below 1 m, Box-Cox above), Gauss-Legendre and Gauss-Hermite rules. |
| `src/LakskongenSim/Environment/WeatherSystem.cs` (pure additions) | `StateDistribution(k)` and its static form (the weather state's exact distribution k days ahead under the scenario's rules; `StormRiskNext3Days` now delegates to it, bit for bit) and `WindSpeedBelowProbability(state, limit)` over the sampler's one wind table. |
| `src/LakskongenSim/Calibration/OffshoreCalibration.cs` | `OffshoreCalibration { Metocean, Operability, Plots }`, every leaf with unit, `Range:` and one `Source:`/`Game balance:`; `Parse(json)`; `OffshorePlotsCalibration.For("off_1" / "off_2" / "hav_1")` → `Hs100M`, `HsScale`. |
| `godot/data/calibration/facilities_offshore.json` | The draft's `metocean`, `operability` and `plots` sections, generated from the C# defaults, keys sorted; F2-07..F2-11 add the unit, incident, licence and cost sections. |

## 2. Where it plugs in

| Engine seam | What the wiring does |
|---|---|
| `SimRngStreams` | Add `OffshoreMetocean = "offshore_metocean"` (no suffix) and create it eagerly in `Initialize` so `ImportStates` finds it on load; replace `OffshoreMetocean.StreamScope` with it. |
| `GameState.Metocean` (F2-03, additive) | One `OffshoreMetocean` per game; it serializes with `SaveJson.Options` as is (acceptance 9 saves and reloads it bit for bit). A pre-F2 save gets a fresh one (`LastTickDay = -1`); it then ticks from the load day. After every load call `ApplyCalibration(registry.Facilities.Offshore.Metocean)` (the calibration is not saved). |
| In-day phase **Metocean**, right after Weather (spec §3.3) | `state.Metocean.TickDay(day, engine.Weather.CurrentState, rng.Stream(OffshoreMetocean))` every day, whether or not an offshore unit exists: the stream moves 8 a day always, and the fjord stream is never touched (the shipped goldens stay identical only if no shipped scenario's other streams depend on it; they do not). The tick must run after `WeatherSystem.AdvanceDays` so `weather.SimulationDay == metocean.LastTickDay` when the forecast is issued. |
| `SitePlot.PlotHsScale` (F2-03) | From `OffshoreCalibration.Plots.For(plot.Id).HsScale` (fitted; never hand-authored), with `Hs100M` from the same record; an area-round plot (stage 3) needs its own fitted scale (re-run `OffshoreMetoceanFit` with the new Hs100). |
| `OffshoreUnit.CurrentWaveHeight` | `metocean.HsAt(plot scale)` each day (today's Hs at the plot). |
| `OffshoreHooks.OperationPossible(site, op, day, vessel)` (gates `ProcessDueWellboatJobs`, `SimulationEngine.cs:1559`, and S4-06's weather gate) | `OperabilityWindows.IsPossible(op, vessel, metocean.HsAt(scale), weather.CurrentWindSpeedMs, metocean.VisibilityKmToday, calibration.Operability)` for today; a tow also needs `DaysNeeded` days, checked against the forecast when it is booked. A wellboat already alongside stays to 5.0 m (`WellboatCanStayAlongside`) and pauses pumping above the handling limit. |
| Appetite (F2-07 adds Hs to `AppetiteFactorFor`) | Multiply by `OperabilityWindows.AppetiteFactor(HsAt(scale))` (1.0 to 4 m, 0.7 from 6 m). |
| `CohortEnvironment.From(..., waveHeight)` | `HsAt(scale)` for offshore pens (today the wave height argument). |
| Bridge `GetSeaStateForecast(siteId)` (`GameBridge.Offshore.cs`) | `today`: `{ hs = HsAt, tp = TpAt, swell_hs = SwellHsToday × scale, visibility_km }`; `days[k]`: `{ day, hs_p10, hs_p50, hs_p90, p_ok: { op → OperabilityWindows.Probability(...) } }`; `known_swell`: the `SwellOutlook`s (onset, plateau, peak p10/p50/p90, Tp). F2-21's Hs chip reads `today.hs` (± 0.05 m acceptance). Issue once per plot per day and cache; the UI never issues per frame. |
| Event bus `StormSwellForecast` (warning, ≥ 4 days ahead; spec's alarm list) | Publish when a `SwellEvent` whose `PeakHsM × scale` reaches the unit's handling limit enters `Events` (its creation day: 4-7 days before onset), with `DeadlineDay = OnsetDay`; quick actions "Fyll siloer", "Avlus nå", "Slakt nå". |
| F2-21 `EnvironmentDirector` wind/swell split | `BaseHsToday` (plus the 0.34 m background) is the wind sea, `SwellHsToday` and `SwellTpToday` the long swell; all × the plot scale. |
| Driftsplan (F2-05) and booking (F2-07) | Ask `ProbabilityAtOrBelow` lead by lead from tomorrow and stop at the first lead ≥ the policy's 0.8; leads 1-4 cost 3-7 µs, 5 ~56 µs, 6-10 ~250 µs (BALANCE §1n). A tow's "P(the window holds 3 days)" is a joint probability over consecutive leads: not in this stage (the forecast gives marginals per day); F2-07 needs a joint (e.g. the same terms with the AR's covariance across the three days) or a conservative product. |

## 3. Things the wiring must keep true

- **Forecast polls never move a stream** and never write the state (acceptance 6): the forecast
  only reads `OffshoreMetocean` and calls `WeatherSystem.StateDistribution`.
- **The forecast is the model's own conditional distribution**: anything that changes the sea
  outside `TickDay` (a storyteller storm forcing the fjord weather through `ForceWeatherState`,
  a scenario changing the weather rules mid-game) is a surprise the forecast did not include.
  That is honest; do not add hidden forcing to the sea itself.
- **8 uniforms a day**: keep the tick's draw count fixed whatever happens (no extra draws for a
  unit, a plot or a forecast).
- **One calibration per game**: the engine applies the registry's calibration after load and at
  start; tests that construct an `OffshoreMetocean` get the code defaults.
- **Goldens**: wiring the tick adds a stream (8 draws a day) and a saved object to every game.
  The golden day line hashes every SimRng stream's position, so the traces change by design:
  the diff report (`tools/record_golden_traces.sh --base <merge base> --report ...`) must show
  only the new stream's draws and the save drift, with every other group (env, market, pop,
  lice, mtb, mort, ops, ledger, cash, rivals) identical, then `--update`.
