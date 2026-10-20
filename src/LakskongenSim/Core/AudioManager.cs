using System;
using LakskongenSim.Environment;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Pure C# state tracker for ambient audio. Zero Godot dependencies.
    /// Derives an ambience profile from weather state, season, location context,
    /// and any active alert.  Consumed by GameBridge → SimBridge → AudioController.gd.
    ///
    /// Ambience base tracks:  calm_sea | storm | wind | rain | harbor | processing_plant
    /// Weather overlay tracks: "" | wind | rain | storm
    /// Alert one-shots:        "" | disease_outbreak | market_crash | achievement |
    ///                            harvest | lice_warning
    /// </summary>
    public class AmbienceProfile
    {
        public string BaseTrack    { get; set; } = "calm_sea";
        public string WeatherLayer { get; set; } = "";
        public string AlertLayer   { get; set; } = "";
        public float  Volume       { get; set; } = 1.0f;
    }

    /// <summary>Player location context — influences base ambience track.</summary>
    public enum AudioContext
    {
        SeaSite,          // Open water / sea cage view (default)
        Harbor,           // Harbour/docking area
        ProcessingPlant,  // Processing facility interior
    }

    /// <summary>
    /// Simulation-aware audio state machine. Updated once per simulated day
    /// via <see cref="Update"/>; queried any time via <see cref="GetAmbienceProfile"/>.
    /// </summary>
    public class AudioManager
    {
        // ── Public read-only state ────────────────────────────────────────────

        public float        MasterVolume    { get; private set; } = 1.0f;
        public AudioContext Context         { get; private set; } = AudioContext.SeaSite;
        public string       ActiveAlert     { get; private set; } = "";
        public string       CurrentSeason   { get; private set; } = "summer";
        public string       CurrentWeatherKey { get; private set; } = "calm";

        // ── Private ───────────────────────────────────────────────────────────

        private WeatherSystem? _weather;

        // ── Constructor ───────────────────────────────────────────────────────

        public AudioManager(WeatherSystem? weather = null)
        {
            _weather = weather;
        }

        // ── Configuration ─────────────────────────────────────────────────────

        public void SetWeatherSystem(WeatherSystem weather) => _weather = weather;

        public void SetMasterVolume(float v) => MasterVolume = Math.Clamp(v, 0f, 1f);

        public void SetContext(AudioContext ctx) => Context = ctx;

        // ── Alert API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Queue an alert sound to be played once.
        /// Valid keys: disease_outbreak, market_crash, achievement, harvest, lice_warning.
        /// </summary>
        public void TriggerAlert(string alertKey) => ActiveAlert = alertKey;

        /// <summary>Clear the pending alert (called by AudioController after playback).</summary>
        public void ClearAlert() => ActiveAlert = "";

        // ── Per-day update ────────────────────────────────────────────────────

        /// <summary>
        /// Sync state from the current simulation day and weather system.
        /// Called by SimulationEngine.AdvanceOneDay() after Weather.AdvanceDays().
        /// </summary>
        public void Update(int day)
        {
            CurrentSeason = DayToSeason(day);

            if (_weather != null)
            {
                CurrentWeatherKey = _weather.CurrentState switch
                {
                    WeatherState.Calm     => "calm",
                    WeatherState.Moderate => "wind",
                    WeatherState.Storm    => "storm",
                    WeatherState.Severe   => "storm",
                    _                     => "calm",
                };
            }
        }

        // ── Profile query ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current ambience profile consumed by AudioController.gd.
        /// <list type="bullet">
        ///   <item><b>BaseTrack</b>  — primary looping ambient (location + weather-derived)</item>
        ///   <item><b>WeatherLayer</b> — additive weather overlay (empty when calm)</item>
        ///   <item><b>AlertLayer</b>  — one-shot alert key, empty when none pending</item>
        ///   <item><b>Volume</b>      — master volume scalar 0–1</item>
        /// </list>
        /// </summary>
        public AmbienceProfile GetAmbienceProfile()
        {
            // Base track: context overrides weather
            string baseTrack = Context switch
            {
                AudioContext.Harbor          => "harbor",
                AudioContext.ProcessingPlant => "processing_plant",
                _ => CurrentWeatherKey switch
                {
                    "storm" => "storm",
                    "wind"  => "wind",
                    _       => "calm_sea",
                },
            };

            // Weather layer: additive overlay (independent of context)
            string weatherLayer = CurrentWeatherKey switch
            {
                "storm" => "storm",
                "wind"  => "wind",
                _       => "",
            };

            return new AmbienceProfile
            {
                BaseTrack    = baseTrack,
                WeatherLayer = weatherLayer,
                AlertLayer   = ActiveAlert,
                Volume       = MasterVolume,
            };
        }

        /// <summary>
        /// Returns the background music track name for the current season.
        /// Used by AudioController.gd to select which BGM to crossfade to.
        /// </summary>
        public string GetSeasonalTrack() => CurrentSeason switch
        {
            "spring" => "spring_theme",
            "summer" => "summer_theme",
            "autumn" => "autumn_theme",
            "winter" => "winter_theme",
            _        => "main_theme",
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string DayToSeason(int day)
        {
            int doy = SimCalendar.DayOfYear(day);
            // Norwegian calendar: Winter Dec–Feb (0–59, 335+), Spring Mar–May (60–151),
            // Summer Jun–Aug (152–243), Autumn Sep–Nov (244–334)
            if (doy < 60 || doy >= 335) return "winter";
            if (doy < 152) return "spring";
            if (doy < 244) return "summer";
            return "autumn";
        }
    }
}
