using System;

using LakskongenSim.Core;
namespace LakskongenSim.Environment
{
    /// <summary>
    /// Norwegian coastal weather system with Markov chain state transitions,
    /// seasonal temperature cycles, and stochastic daily variation.
    /// Temperature range: 2-18°C (Norwegian fjord sea surface temps).
    /// Simulation-day driven — never uses real-world clock.
    /// </summary>
    public class WeatherSystem
    {
        // ─── State ───
        public double CurrentTemperatureC { get; private set; } = 8.0;
        public double CurrentWindSpeedMs { get; private set; } = 5.0; // m/s
        public double CurrentWaveHeight { get; private set; } = 0.5; // meters
        public WeatherState CurrentState { get; private set; } = WeatherState.Moderate;
        public int SimulationDay { get; private set; } = 0;

        // ─── Seasonal parameters (Norwegian coast) ───
        // Mean sea surface temperature by month (°C) — Norwegian fjords
        // Based on Havforskningsinstituttet (Institute of Marine Research) data
        private static readonly double[] MonthlyMeanTemp =
        {
            4.5,  // Jan
            3.8,  // Feb — coldest
            4.2,  // Mar
            5.5,  // Apr
            8.0,  // May
            10.5, // Jun
            12.5, // Jul
            13.5, // Aug — warmest
            12.0, // Sep
            9.5,  // Oct
            7.0,  // Nov
            5.5   // Dec
        };

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
        // Storm season on the 30-day-month calendar: day 270 (1 Oct) through
        // day 90 inclusive (unchanged; S2-02 owns the calendar).
        private const int StormSeasonStartDayOfYear = 270;
        private const int StormSeasonEndDayOfYear = 90;
        private const int DaysPerYear = 365;
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
        /// Apply a temperature anomaly (°C offset) from a storyteller event.
        /// Clamped to the realistic Norwegian sea surface range.
        /// </summary>
        public void ApplyTemperatureAnomaly(double deltaC)
        {
            CurrentTemperatureC = Math.Clamp(CurrentTemperatureC + deltaC, 1.5, 19.0);
            OnTemperatureChanged?.Invoke(CurrentTemperatureC);
        }

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
        /// Calculate sea surface temperature for current simulation day.
        /// Uses sinusoidal seasonal cycle + stochastic daily variation.
        /// </summary>
        private void UpdateTemperature()
        {
            // Determine month from simulation day (day 0 = January 1st)
            int dayOfYear = SimulationDay % 365;
            int month = dayOfYear / 30; // Approximate month (0-11)
            month = Math.Clamp(month, 0, 11);

            // Interpolate between monthly means for smooth transition
            int nextMonth = (month + 1) % 12;
            double dayInMonth = (dayOfYear % 30) / 30.0;
            double baseMeanTemp = MonthlyMeanTemp[month] * (1.0 - dayInMonth)
                                + MonthlyMeanTemp[nextMonth] * dayInMonth;

            // Daily stochastic variation (±1.5°C normally, ±3°C during storms)
            double varianceMultiplier = CurrentState switch
            {
                WeatherState.Storm => 2.0,
                WeatherState.Severe => 3.0,
                _ => 1.0
            };

            double u1 = Math.Max(1e-10, _rng.NextDouble());
            double u2 = _rng.NextDouble();
            double noise = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double dailyVariation = noise * 1.0 * varianceMultiplier;

            // Storm cooling effect
            double stormCooling = CurrentState switch
            {
                WeatherState.Storm => -0.5,
                WeatherState.Severe => -1.5,
                _ => 0.0
            };

            double newTemp = baseMeanTemp + dailyVariation + stormCooling;

            // Clamp to realistic Norwegian sea surface temperature range
            CurrentTemperatureC = Math.Clamp(newTemp, 1.5, 19.0);
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
            int d = ((dayOfYear % DaysPerYear) + DaysPerYear) % DaysPerYear;
            return d >= StormSeasonStartDayOfYear || d <= StormSeasonEndDayOfYear;
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
        /// Honest 3-day storm forecast: the probability that the weather three
        /// days from now is Storm or Severe, from the current state through the
        /// same transition rows the sampler draws from (one per day ahead, each
        /// with that day's season and the scenario's rules). Pure function, safe
        /// for UI polling.
        /// </summary>
        public double StormRiskNext3Days()
        {
            var p = new double[WeatherStateCount];
            p[(int)CurrentState] = 1.0;
            for (int step = 1; step <= StormForecastDays; step++)
            {
                var m = TransitionMatrixFor(SimulationDay + step, StormFrequencyMultiplier, MaxState);
                var next = new double[WeatherStateCount];
                for (int from = 0; from < WeatherStateCount; from++)
                    for (int to = 0; to < WeatherStateCount; to++)
                        next[to] += p[from] * m[from, to];
                p = next;
            }
            return Math.Clamp(p[(int)WeatherState.Storm] + p[(int)WeatherState.Severe], 0.0, 1.0);
        }
        /// <summary>
        /// Update wind speed based on weather state.
        /// </summary>
        private void UpdateWind()
        {
            double baseWind = CurrentState switch
            {
                WeatherState.Calm => 3.0,
                WeatherState.Moderate => 7.0,
                WeatherState.Storm => 15.0,
                WeatherState.Severe => 25.0,
                _ => 5.0
            };

            // Add random variation
            double variation = (_rng.NextDouble() - 0.5) * 4.0;
            CurrentWindSpeedMs = Math.Max(0.5, baseWind + variation);
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
            int dayOfYear = SimulationDay % 365;
            int month = Math.Clamp(dayOfYear / 30, 0, 11);
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