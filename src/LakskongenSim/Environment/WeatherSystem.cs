using System;

using LakskongenSim.Calibration;
using LakskongenSim.Core;
namespace LakskongenSim.Environment
{
    /// <summary>
    /// Norwegian coastal weather system with Markov chain state transitions and
    /// the shared sea-temperature anomaly (S2-02). Each production area's sea
    /// temperature is its own climatology (<see cref="ProductionAreas"/>) plus
    /// one anomaly all areas share; <see cref="CurrentTemperatureC"/> reports the
    /// reference (home) area. Simulation-day driven on <see cref="SimCalendar"/>,
    /// never the real-world clock.
    /// </summary>
    public class WeatherSystem
    {
        // ─── State ───
        public double CurrentTemperatureC { get; private set; } = 8.0;
        public double CurrentWindSpeedMs { get; private set; } = 5.0; // m/s
        public double CurrentWaveHeight { get; private set; } = 0.5; // meters
        public WeatherState CurrentState { get; private set; } = WeatherState.Moderate;
        public int SimulationDay { get; private set; } = 0;

        // ─── Sea temperature (S2-02) ───
        // Before S2-02 one national monthly curve on 30-day months, plus daily
        // white noise of 1 °C, served every zone. The climatologies now live per
        // production area; this system owns only what all areas share.

        /// <summary>
        /// Production area whose sea temperature <see cref="CurrentTemperatureC"/>
        /// reports: the player's home area, set by the engine every day from the
        /// game state (so not saved here); the national reference curve until then.
        /// </summary>
        public int ReferenceProductionArea { get; private set; } = ProductionAreas.NationalReference;

        /// <summary>Slow shared anomaly (°C): a seeded AR(1) process, warm and cold seasons and years.</summary>
        public double SeaAnomalyC { get; private set; }

        /// <summary>Storyteller anomaly (°C, heatwave or cold snap), decaying over weeks.</summary>
        public double EventAnomalyC { get; private set; }

        /// <summary>Today's short-term fluctuation (°C) around the slow anomaly.</summary>
        public double DailyFluctuationC { get; private set; }

        /// <summary>Today's storm cooling of the cage layer (°C, April-September only).</summary>
        public double StormCoolingC { get; private set; }

        /// <summary>What every area's climatology moves by today (°C).</summary>
        public double SeaTemperatureOffsetC => SeaAnomalyC + EventAnomalyC + DailyFluctuationC + StormCoolingC;

        private SeaTemperatureCalibration _seaCalibration = CalibrationRegistry.Default.SeaTemperature;

        // ─── Markov chain transition probabilities ───
        // State: Calm, Moderate, Storm, Severe
        // Transition matrix (row = current state, col = next state)
        private static readonly double[,] TransitionMatrix =
        {
            // Calm→     Moderate→  Storm→     Severe→
            { 0.60,      0.30,      0.08,      0.02 },   // From Calm
            { 0.25,      0.50,      0.20,      0.05 },   // From Moderate
            { 0.10,      0.30,      0.45,      0.15 },   // From Storm
            { 0.05,      0.20,      0.40,      0.35 },   // From Severe
        };

        // ─── Storm season modifier (Oct-Mar = higher storm probability) ───
        private const double StormSeasonMultiplier = 1.5;
        private const int WeatherStateCount = 4;
        // Storm season: October through March on the sim calendar (S2-02; it was
        // day 270 through day 90 of the 30-day-month calendar).
        private const int StormSeasonFirstMonthIndex = 9;  // October
        private const int StormSeasonLastMonthIndex = 2;   // March
        // Storm cooling of the cage layer in the stratified half of the year (S1-02).
        private const double StormDayCoolingC = -0.5;
        private const double SevereDayCoolingC = -1.5;
        // The daily fluctuation scales up on storm days (as the old noise did).
        private const double StormFluctuationScale = 2.0;
        private const double SevereFluctuationScale = 3.0;
        /// <summary>Days ahead <see cref="StormRiskNext3Days"/> looks.</summary>
        public const int StormForecastDays = 3;

        private readonly SimRngStream _rng;

        internal SimRngStream RngForSnapshot => _rng;

        // ─── Events ───
        public event Action<double>? OnTemperatureChanged;
        public event Action<WeatherState>? OnWeatherStateChanged;
        public event Action<string>? OnWeatherAlert; // Severe weather alerts

        public WeatherSystem(SimRngStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            // Day 0 reads the reference area's 1 January climatology (it read a flat 8 °C).
            CurrentTemperatureC = SeaTemperatureAt(ReferenceProductionArea);
        }

        /// <summary>
        /// Force a weather state transition from a storyteller event.
        /// The Markov chain will naturally revert to normal probabilities over subsequent days.
        /// </summary>
        public void ForceWeatherState(WeatherState state)
        {
            // A calm-weather scenario caps forced weather too.
            CurrentState = state > MaxState ? MaxState : state;
            state = CurrentState;
            OnWeatherStateChanged?.Invoke(CurrentState);

            if (state >= WeatherState.Storm)
                OnWeatherAlert?.Invoke($"⛈️ Severe event forcing {state} conditions");
        }

        /// <summary>
        /// Apply a temperature anomaly (°C offset) from a storyteller event. It
        /// reaches every production area and fades with
        /// <see cref="SeaTemperatureCalibration.EventAnomalyEFoldingDays"/>; it
        /// used to last until the next day's temperature overwrote it.
        /// </summary>
        public void ApplyTemperatureAnomaly(double deltaC)
        {
            EventAnomalyC += deltaC;
            CurrentTemperatureC = SeaTemperatureAt(ReferenceProductionArea);
            OnTemperatureChanged?.Invoke(CurrentTemperatureC);
        }

        /// <summary>Use the calibration the engine loaded (the registry default otherwise).</summary>
        public void ApplySeaTemperatureCalibration(SeaTemperatureCalibration calibration)
        {
            _seaCalibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        }

        /// <summary>
        /// Report the sea temperature of <paramref name="productionArea"/> as
        /// <see cref="CurrentTemperatureC"/> (the player's home area; 0 = the national
        /// reference). Numbers outside 0-13 are ignored.
        /// </summary>
        public void SetReferenceProductionArea(int productionArea)
        {
            if (!ProductionAreas.HasClimatology(productionArea) || productionArea == ReferenceProductionArea)
                return;
            ReferenceProductionArea = productionArea;
            CurrentTemperatureC = SeaTemperatureAt(productionArea);
        }

        /// <summary>
        /// Today's sea temperature (°C, cage depth) in a production area: its
        /// climatology on <see cref="SimulationDay"/> plus the shared offset. The
        /// only floor is the freezing point of seawater (the old 1.5-19 °C clamp
        /// is gone; no climatology comes near either bound).
        /// </summary>
        public double SeaTemperatureAt(int productionArea)
            => Math.Max(ProductionAreas.SeawaterFreezingPointC,
                ProductionAreas.ClimatologyC(productionArea, SimulationDay) + SeaTemperatureOffsetC);

        /// <summary>
        /// Advance weather simulation by given days.
        /// Updates temperature, wind, and weather state.
        /// </summary>
        public void AdvanceDays(int days)
        {
            for (int d = 0; d < days; d++)
            {
                SimulationDay++;
                UpdateTemperature();
                UpdateWeatherState();
                UpdateWind();
            }
        }

        /// <summary>
        /// Advance the shared sea-temperature offset one day and report the
        /// reference area's temperature. Draws the same two uniforms a day as the
        /// old noise did (Box-Muller turns them into two independent normals), so
        /// the weather-state chain that shares this stream is unchanged.
        /// </summary>
        private void UpdateTemperature()
        {
            double u1 = Math.Max(1e-10, _rng.NextDouble());
            double u2 = _rng.NextDouble();
            double radius = Math.Sqrt(-2.0 * Math.Log(u1));
            double slowShock = radius * Math.Cos(2.0 * Math.PI * u2);
            double dailyShock = radius * Math.Sin(2.0 * Math.PI * u2);

            // Slow anomaly: AR(1) with the calibrated stationary SD and e-folding time.
            double phi = Math.Exp(-1.0 / Math.Max(1.0, _seaCalibration.AnomalyEFoldingDays));
            double innovationSd = _seaCalibration.AnomalyStdDevC * Math.Sqrt(1.0 - phi * phi);
            SeaAnomalyC = phi * SeaAnomalyC + innovationSd * slowShock;

            // A storyteller anomaly fades over a few weeks.
            EventAnomalyC *= Math.Exp(-1.0 / Math.Max(1.0, _seaCalibration.EventAnomalyEFoldingDays));

            // Short-term fluctuation (wind mixing, upwelling), larger on storm days.
            double fluctuationScale = CurrentState switch
            {
                WeatherState.Storm => StormFluctuationScale,
                WeatherState.Severe => SevereFluctuationScale,
                _ => 1.0
            };
            DailyFluctuationC = dailyShock * _seaCalibration.DailyFluctuationStdDevC * fluctuationScale;

            // Storm cooling: wind mixing brings colder water up to the cages
            // while the column is stratified with a warm surface (Apr-Sep). In
            // the storm season the coastal column is mixed or colder at the
            // surface than below (Helland-Hansen & Nansen, "The Norwegian Sea",
            // 1909: in March the surface is colder than at 100 fathoms), so a
            // winter storm does not cool the cage layer.
            StormCoolingC = IsStormSeason(SimulationDay) ? 0.0 : CurrentState switch
            {
                WeatherState.Storm => StormDayCoolingC,
                WeatherState.Severe => SevereDayCoolingC,
                _ => 0.0
            };

            CurrentTemperatureC = SeaTemperatureAt(ReferenceProductionArea);
            OnTemperatureChanged?.Invoke(CurrentTemperatureC);
        }

        /// <summary>
        /// Scenario weather rule (set from the active scenario on start and on
        /// load, so not saved): above 1 scales transitions into Storm and Severe
        /// and renormalizes the row ("harsh_storms"). 1 leaves the chain as is.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double StormFrequencyMultiplier { get; private set; } = 1.0;

        /// <summary>
        /// Highest weather state the scenario allows (Moderate under "calm_weather";
        /// Severe otherwise). Not saved, like <see cref="StormFrequencyMultiplier"/>.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public WeatherState MaxState { get; private set; } = WeatherState.Severe;

        /// <summary>
        /// Apply a scenario's weather rule. A storm already raging is calmed at
        /// once when the cap forbids it.
        /// </summary>
        public void ApplyScenarioWeather(WeatherState maxState, double stormFrequencyMultiplier)
        {
            MaxState = maxState;
            StormFrequencyMultiplier = Math.Max(0.0, stormFrequencyMultiplier);
            if (CurrentState > MaxState)
            {
                CurrentState = MaxState;
                OnWeatherStateChanged?.Invoke(CurrentState);
            }
        }

        /// <summary>True on the days of the year the storm season covers (Oct-Mar).</summary>
        public static bool IsStormSeason(int dayOfYear)
        {
            int month = SimCalendar.MonthIndex(dayOfYear);
            return month >= StormSeasonFirstMonthIndex || month <= StormSeasonLastMonthIndex;
        }

        /// <summary>
        /// The one transition row of the weather chain (review F456): the base row
        /// for <paramref name="fromState"/>, Storm and Severe scaled by the storm
        /// season and by the scenario's storm multiplier, renormalized so the row
        /// sums to 1, then any state above <paramref name="maxState"/> folded into
        /// it (scenario "calm_weather"). Both the sampler and the forecast use it.
        /// The season multiplier used to be applied without renormalizing: in
        /// Oct-Mar the Storm column filled the roll before Severe, so Severe could
        /// only persist, never start, and the forecast (which did renormalize)
        /// disagreed with the weather that happened.
        /// </summary>
        internal static void FillTransitionRow(WeatherState fromState, bool stormSeason,
            double stormFrequencyMultiplier, WeatherState maxState, double[] row)
        {
            int from = (int)fromState;
            double stormScale = (stormSeason ? StormSeasonMultiplier : 1.0) * Math.Max(0.0, stormFrequencyMultiplier);
            double sum = 0.0;
            for (int to = 0; to < WeatherStateCount; to++)
            {
                double p = TransitionMatrix[from, to];
                if (to >= (int)WeatherState.Storm)
                    p *= stormScale;
                row[to] = p;
                sum += p;
            }
            for (int to = 0; to < WeatherStateCount; to++)
                row[to] /= sum;
            for (int to = (int)maxState + 1; to < WeatherStateCount; to++)
            {
                row[(int)maxState] += row[to];
                row[to] = 0.0;
            }
        }

        /// <summary>
        /// The transition matrix into <paramref name="dayOfYear"/> under the given
        /// scenario rules (rows from <see cref="FillTransitionRow"/>).
        /// </summary>
        internal static double[,] TransitionMatrixFor(int dayOfYear, double stormFrequencyMultiplier, WeatherState maxState)
        {
            var m = new double[WeatherStateCount, WeatherStateCount];
            var row = new double[WeatherStateCount];
            bool stormSeason = IsStormSeason(dayOfYear);
            for (int from = 0; from < WeatherStateCount; from++)
            {
                FillTransitionRow((WeatherState)from, stormSeason, stormFrequencyMultiplier, maxState, row);
                for (int to = 0; to < WeatherStateCount; to++)
                    m[from, to] = row[to];
            }
            return m;
        }

        /// <summary>
        /// Update weather state using Markov chain transitions.
        /// Storm probability increases in Oct-Mar.
        /// </summary>
        private void UpdateWeatherState()
        {
            double roll = _rng.NextDouble();
            var row = new double[WeatherStateCount];
            FillTransitionRow(CurrentState, IsStormSeason(SimulationDay), StormFrequencyMultiplier, MaxState, row);

            // Cumulative probability check; rounding can leave the last cumulative
            // a hair under 1, so a roll past it takes the last reachable state.
            double cumulative = 0.0;
            WeatherState newState = MaxState;
            for (int nextState = 0; nextState < WeatherStateCount; nextState++)
            {
                cumulative += row[nextState];
                if (roll < cumulative)
                {
                    newState = (WeatherState)nextState;
                    break;
                }
            }

            if (newState != CurrentState)
            {
                var previousState = CurrentState;
                CurrentState = newState;
                OnWeatherStateChanged?.Invoke(CurrentState);

                // Alert for severe weather
                if (CurrentState == WeatherState.Severe)
                {
                    OnWeatherAlert?.Invoke("⛈️ SEVERE WEATHER WARNING: Storm force winds and high seas expected. Operations suspended.");
                }
                else if (CurrentState == WeatherState.Storm && previousState != WeatherState.Severe)
                {
                    OnWeatherAlert?.Invoke("🌊 STORM WARNING: Rough conditions ahead. Monitor pen integrity.");
                }
            }
        }

        /// <summary>
        /// Restore live weather state after a load so temperature, wind, wave
        /// and the Markov state continue instead of restarting at January /
        /// Moderate (Phase 1 wave 6 — required for the exact save-hash test).
        /// </summary>
        internal void ImportState(int simulationDay, double temperatureC, double windMs, double waveM, WeatherState state)
        {
            SimulationDay = simulationDay;
            CurrentTemperatureC = temperatureC;
            CurrentWindSpeedMs = windMs;
            CurrentWaveHeight = waveM;
            CurrentState = state;
        }

        /// <summary>
        /// Restore the shared sea-temperature offset (S2-02 saves). A legacy save
        /// has none and resumes at a zero anomaly.
        /// </summary>
        internal void ImportSeaTemperatureState(double seaAnomalyC, double eventAnomalyC,
            double dailyFluctuationC, double stormCoolingC)
        {
            SeaAnomalyC = seaAnomalyC;
            EventAnomalyC = eventAnomalyC;
            DailyFluctuationC = dailyFluctuationC;
            StormCoolingC = stormCoolingC;
        }

        /// <summary>
        /// Honest 3-day storm forecast: the probability that the weather three
        /// days from now is Storm or Severe, from the current state through the
        /// same transition rows the sampler draws from (one per day ahead, each
        /// with that day's season and the scenario's rules). Pure function, safe
        /// for UI polling.
        /// </summary>
        public double StormRiskNext3Days()
        {
            var p = StateDistribution(StormForecastDays);
            return Math.Clamp(p[(int)WeatherState.Storm] + p[(int)WeatherState.Severe], 0.0, 1.0);
        }

        /// <summary>
        /// The exact distribution of the weather state <paramref name="leadDays"/>
        /// days from now (F2-06), indexed by <see cref="WeatherState"/>: the
        /// current state pushed through the same transition rows the sampler
        /// draws from, one per day ahead with that day's season and the
        /// scenario's rules (<see cref="StormRiskNext3Days"/> is its 3-day
        /// Storm + Severe mass). Lead 0 is the current state. Pure: no draw, no
        /// state change, safe for UI polling and the offshore forecast.
        /// </summary>
        public double[] StateDistribution(int leadDays)
            => StateDistribution(CurrentState, SimulationDay, leadDays, StormFrequencyMultiplier, MaxState);

        /// <summary>
        /// <see cref="StateDistribution(int)"/> from an explicit state, day and
        /// scenario rules (the offshore forecast and its tests).
        /// </summary>
        public static double[] StateDistribution(WeatherState current, int day, int leadDays,
            double stormFrequencyMultiplier, WeatherState maxState)
        {
            if (leadDays < 0)
                throw new ArgumentOutOfRangeException(nameof(leadDays), leadDays, "Lead must be zero or more days");
            var p = new double[WeatherStateCount];
            p[(int)current] = 1.0;
            for (int step = 1; step <= leadDays; step++)
            {
                var m = TransitionMatrixFor(day + step, stormFrequencyMultiplier, maxState);
                var next = new double[WeatherStateCount];
                for (int from = 0; from < WeatherStateCount; from++)
                    for (int to = 0; to < WeatherStateCount; to++)
                        next[to] += p[from] * m[from, to];
                p = next;
            }
            return p;
        }

        // ─── Wind (one table for the sampler and the forecast) ───
        private const double WindVariationRangeMs = 4.0;
        private const double MinWindSpeedMs = 0.5;

        /// <summary>The wind a weather state centres on (m/s); the day draws ± half <see cref="WindVariationRangeMs"/>.</summary>
        private static double BaseWindMs(WeatherState state) => state switch
        {
            WeatherState.Calm => 3.0,
            WeatherState.Moderate => 7.0,
            WeatherState.Storm => 15.0,
            WeatherState.Severe => 25.0,
            _ => 5.0
        };

        /// <summary>
        /// Probability that a day in <paramref name="state"/> has wind below
        /// <paramref name="limitMs"/> (F2-06: the helicopter's wind limit), from
        /// the same uniform spread the sampler draws (<see cref="UpdateWind"/>).
        /// Pure.
        /// </summary>
        public static double WindSpeedBelowProbability(WeatherState state, double limitMs)
        {
            if (limitMs <= MinWindSpeedMs) return 0.0;
            double share = (limitMs - BaseWindMs(state)) / WindVariationRangeMs + 0.5;
            return Math.Clamp(share, 0.0, 1.0);
        }

        /// <summary>
        /// Update wind speed based on weather state.
        /// </summary>
        private void UpdateWind()
        {
            double baseWind = BaseWindMs(CurrentState);

            // Add random variation
            double variation = (_rng.NextDouble() - 0.5) * WindVariationRangeMs;
            CurrentWindSpeedMs = Math.Max(MinWindSpeedMs, baseWind + variation);
        }

        /// <summary>
        /// Update wave height based on weather state and wind speed.
        /// </summary>
        private void UpdateWaveHeight()
        {
            double baseWaveHeight = CurrentState switch
            {
                WeatherState.Calm => 0.3,
                WeatherState.Moderate => 1.0,
                WeatherState.Storm => 3.0,
                WeatherState.Severe => 6.0,
                _ => 0.5
            };

            // Scale with wind speed, add variation
            double windFactor = CurrentWindSpeedMs / 10.0;
            double variation = (_rng.NextDouble() - 0.5) * 1.0;
            CurrentWaveHeight = Math.Max(0.1, baseWaveHeight + (windFactor * 0.5) + variation);

            // Max wave height based on weather state for realism
            CurrentWaveHeight = CurrentState switch
            {
                WeatherState.Calm => Math.Min(CurrentWaveHeight, 1.0),
                WeatherState.Moderate => Math.Min(CurrentWaveHeight, 2.5),
                WeatherState.Storm => Math.Min(CurrentWaveHeight, 7.0),
                WeatherState.Severe => Math.Min(CurrentWaveHeight, 15.0),
                _ => CurrentWaveHeight
            };
        }

        /// <summary>
        /// Get current weather status for display.
        /// </summary>
        public WeatherStatus GetStatus()
        {
            int dayOfYear = SimCalendar.DayOfYear(SimulationDay);
            int month = SimCalendar.MonthIndex(SimulationDay);
            string[] monthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            return new WeatherStatus
            {
                TemperatureC = CurrentTemperatureC,
                WindSpeedMs = CurrentWindSpeedMs,
                State = CurrentState,
                SimulationDay = SimulationDay,
                MonthName = monthNames[month],
                IsStormSeason = IsStormSeason(dayOfYear),
                CanOperate = CurrentState != WeatherState.Severe
            };
        }
    }

    /// <summary>Weather states for Markov chain.</summary>
    public enum WeatherState
    {
        Calm = 0,
        Moderate = 1,
        Storm = 2,
        Severe = 3
    }

    /// <summary>Weather status snapshot for UI.</summary>
    public struct WeatherStatus
    {
        public double TemperatureC;
        public double WindSpeedMs;
        public WeatherState State;
        public int SimulationDay;
        public string MonthName;
        public bool IsStormSeason;
        public bool CanOperate;
    }
}