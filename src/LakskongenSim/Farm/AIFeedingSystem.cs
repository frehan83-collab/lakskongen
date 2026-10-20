using System;
using System.Collections.Generic;
using LakskongenSim.Core;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// AI-Driven Precision Feeding System.
    /// Based on real systems: AKVA Group CCS/SmartFeeder, ScaleAQ FeedStation,
    /// Mowi SmartFeed, Cermaq iFarm camera systems.
    /// 
    /// Key features:
    /// - Camera-based appetite detection (reduces feed waste 10-20%)
    /// - Pellet size/speed optimization per biomass stage
    /// - FCR improvement 5-10% through precision timing
    /// - Daily feed plans based on: temperature, fish size, appetite, O2 levels
    /// </summary>
    public class AIFeedingSystem
    {
        private readonly SimRngStream? _cameraStream;

        public AIFeedingSystem() { }

        /// <summary>
        /// Deterministic variant: camera-reading noise comes from a named
        /// SimRng stream (SimRngStreams.AiFeeding). The parameterless
        /// constructor keeps a fixed neutral reading for callers without
        /// an RNG service.
        /// </summary>
        public AIFeedingSystem(SimRngStream cameraStream) => _cameraStream = cameraStream;

        /// <summary>Persistence (Phase 1 wave 6): the learning/counter state
        /// drives daily license economics — must survive a save.</summary>
        internal (bool Installed, double Calibration, int Days, double CameraUptime, double Appetite,
            double Waste, decimal Savings, double FcrMult, System.Collections.Generic.List<double> History) ExportState()
            => (IsInstalled, CalibrationLevel, DaysSinceInstallation, CameraUptime, AppetiteScore,
                FeedWasteRate, DailyFeedSavings, FCRMultiplier,
                new System.Collections.Generic.List<double>(AppetiteHistory));

        internal void ImportState((bool Installed, double Calibration, int Days, double CameraUptime, double Appetite,
            double Waste, decimal Savings, double FcrMult, System.Collections.Generic.List<double> History) s)
        {
            IsInstalled = s.Installed;
            CalibrationLevel = s.Calibration;
            DaysSinceInstallation = s.Days;
            CameraUptime = s.CameraUptime;
            AppetiteScore = s.Appetite;
            FeedWasteRate = s.Waste;
            DailyFeedSavings = s.Savings;
            FCRMultiplier = s.FcrMult;
            AppetiteHistory.Clear();
            AppetiteHistory.AddRange(s.History);
        }

        // ─── Configuration ───
        /// <summary>Is the AI system installed and active?</summary>
        public bool IsInstalled { get; set; } = false;
        
        /// <summary>System calibration level (0-1). Improves over time with data.</summary>
        public double CalibrationLevel { get; set; } = 0.5;
        
        /// <summary>Days since installation. AI improves with more data.</summary>
        public int DaysSinceInstallation { get; set; } = 0;
        
        /// <summary>Number of cameras installed per site.</summary>
        public int CameraCount { get; set; } = 8; // 1-2 per pen
        
        /// <summary>Camera uptime (0-1). Biofouling reduces vision over time.</summary>
        public double CameraUptime { get; set; } = 1.0;

        // ─── Appetite Detection ───
        /// <summary>Current appetite score (0-1). Detected by underwater cameras.</summary>
        public double AppetiteScore { get; set; } = 0.7;
        
        /// <summary>Historical appetite scores for trend analysis.</summary>
        public List<double> AppetiteHistory { get; set; } = new();
        
        /// <summary>Feed waste detected (fraction of pellets not eaten, 0-1).</summary>
        public double FeedWasteRate { get; set; } = 0.05; // AI target: <5% waste

        // ─── Pellet Optimization ───
        /// <summary>Current optimal pellet size (mm). Adjusted per fish size.</summary>
        public double OptimalPelletSizeMm { get; set; } = 9.0;
        
        /// <summary>Feed delivery rate (kg/minute).</summary>
        public double FeedRateKgPerMin { get; set; } = 5.0;
        
        /// <summary>Number of feeding sessions per day.</summary>
        public int FeedingSessionsPerDay { get; set; } = 4;

        // ─── Performance Metrics ───
        /// <summary>FCR improvement factor (0.90 = 10% better).</summary>
        public double FCRMultiplier { get; private set; } = 1.0;
        
        /// <summary>Feed cost savings compared to manual feeding (NOK/day).</summary>
        public decimal DailyFeedSavings { get; private set; } = 0;

        // ─── Costs ───
        /// <summary>Installation CAPEX range: 2-5M NOK per site.</summary>
        public const decimal CAPEX_MIN = 2_000_000m;
        public const decimal CAPEX_MAX = 5_000_000m;
        
        /// <summary>Annual license/maintenance cost.</summary>
        public const decimal ANNUAL_LICENSE_COST = 500_000m; // Software + camera maintenance
        
        /// <summary>Camera cleaning interval (days).</summary>
        public const int CAMERA_CLEANING_INTERVAL = 30;

        /// <summary>
        /// Generate optimal daily feed plan based on environmental conditions.
        /// </summary>
        public FeedPlan GenerateFeedPlan(
            double waterTempC,
            double avgFishWeightG,
            double oxygenMgL,
            int fishCount,
            double currentDaylight)
        {
            if (!IsInstalled)
                return FeedPlan.ManualDefault(avgFishWeightG, fishCount);

            var plan = new FeedPlan();

            // ─── Temperature-based feeding rate ───
            // Salmon appetite peaks at 12-14°C, drops below 4°C and above 18°C
            double tempFactor;
            if (waterTempC < 2.0) tempFactor = 0.1;
            else if (waterTempC < 6.0) tempFactor = 0.3 + (waterTempC - 2.0) * 0.1;
            else if (waterTempC < 12.0) tempFactor = 0.7 + (waterTempC - 6.0) * 0.05;
            else if (waterTempC <= 14.0) tempFactor = 1.0;
            else if (waterTempC <= 18.0) tempFactor = 1.0 - (waterTempC - 14.0) * 0.1;
            else tempFactor = 0.6 - (waterTempC - 18.0) * 0.15;
            tempFactor = Math.Clamp(tempFactor, 0.05, 1.0);

            // ─── Fish size-based daily ration ───
            // Smaller fish eat more relative to body weight
            double bodyWeightPct;
            if (avgFishWeightG < 200) bodyWeightPct = 2.5;      // Smolt: 2-3% BW/day
            else if (avgFishWeightG < 1000) bodyWeightPct = 1.8; // Growing: 1.5-2%
            else if (avgFishWeightG < 3000) bodyWeightPct = 1.2; // Sub-adult: 1-1.5%
            else bodyWeightPct = 0.8;                             // Harvest size: 0.6-1%

            // ─── Oxygen limitation ───
            double oxygenFactor = oxygenMgL >= 7.0 ? 1.0 : Math.Max(0.3, oxygenMgL / 7.0);

            // ─── Appetite correction (AI camera data) ───
            double appetiteFactor = CalibrationLevel > 0.7 ? AppetiteScore : 0.8; // Fallback if poorly calibrated

            // ─── Daylight adjustment (less feeding in dark months) ───
            double daylightFactor = 0.7 + (currentDaylight / 24.0) * 0.3; // 70-100%

            // ─── Calculate total daily feed ───
            double biomassKg = fishCount * avgFishWeightG / 1000.0;
            double baseFeedKg = biomassKg * (bodyWeightPct / 100.0);
            double adjustedFeedKg = baseFeedKg * tempFactor * oxygenFactor * appetiteFactor * daylightFactor;

            plan.TotalFeedKg = Math.Max(0, adjustedFeedKg);
            plan.SessionCount = CalculateOptimalSessions(waterTempC, currentDaylight);
            plan.FeedPerSessionKg = plan.TotalFeedKg / plan.SessionCount;
            
            // ─── Pellet size optimization ───
            plan.PelletSizeMm = avgFishWeightG switch
            {
                < 50 => 2.0,    // Smolt
                < 200 => 3.0,   // Post-smolt
                < 500 => 5.0,   // Growing
                < 1500 => 7.0,  // Sub-adult
                < 3000 => 9.0,  // Pre-harvest
                _ => 11.0       // Harvest size
            };

            plan.TemperatureFactor = tempFactor;
            plan.AppetiteFactor = appetiteFactor;
            plan.OxygenFactor = oxygenFactor;

            return plan;
        }

        /// <summary>
        /// Daily tick: update AI calibration, camera status, performance metrics.
        /// </summary>
        public AIFeedTickResult TickDay(double biomassKg, decimal feedPricePerKg)
        {
            var result = new AIFeedTickResult();

            if (!IsInstalled) return result;

            DaysSinceInstallation++;

            // ─── AI Learning Curve ───
            // System gets better over 180 days of data collection
            if (CalibrationLevel < 0.95)
            {
                CalibrationLevel = Math.Min(0.95, 0.5 + ((double)DaysSinceInstallation / LakskongenSim.Core.SimCalendar.DaysPerYear) * 0.45);
            }

            // ─── Camera Degradation (biofouling) ───
            int daysSinceCleaning = DaysSinceInstallation % CAMERA_CLEANING_INTERVAL;
            CameraUptime = 1.0 - (daysSinceCleaning * 0.015); // Loses ~1.5%/day
            CameraUptime = Math.Max(0.3, CameraUptime);

            if (CameraUptime < 0.5)
            {
                result.CameraMaintenanceNeeded = true;
            }

            // ─── FCR Improvement ───
            // AI reduces FCR by 5-10% when well-calibrated
            double aiImprovement = CalibrationLevel * CameraUptime * 0.10; // Max 10% improvement
            FCRMultiplier = 1.0 - aiImprovement;
            result.FCRMultiplier = FCRMultiplier;

            // ─── Feed Waste Reduction ───
            // Manual feeding: ~15% waste. AI feeding: 3-8% waste
            double manualWaste = 0.15;
            FeedWasteRate = manualWaste * (1.0 - CalibrationLevel * CameraUptime * 0.7);
            double wasteSaved = manualWaste - FeedWasteRate;
            
            // ─── Cost Savings ───
            decimal dailyFeedCost = (decimal)biomassKg * 0.015m * feedPricePerKg; // ~1.5% BW/day baseline
            DailyFeedSavings = dailyFeedCost * (decimal)wasteSaved;
            result.DailyFeedSavings = DailyFeedSavings;

            // ─── License/Maintenance cost ───
            result.DailyLicenseCost = ANNUAL_LICENSE_COST / LakskongenSim.Core.SimCalendar.DaysPerYear;

            // ─── Appetite tracking ───
            // Simulated camera reading; noise from the named stream when
            // wired (engine), fixed neutral reading otherwise.
            AppetiteScore = 0.5 + (_cameraStream?.NextDouble() ?? 0.5) * 0.5;
            if (AppetiteHistory.Count > 30) AppetiteHistory.RemoveAt(0);
            AppetiteHistory.Add(AppetiteScore);

            return result;
        }

        /// <summary>Clean cameras (reset biofouling).</summary>
        public decimal CleanCameras()
        {
            CameraUptime = 1.0;
            return 25_000m; // Diver/ROV cleaning cost per session
        }

        /// <summary>Get installation cost based on site size.</summary>
        public static decimal GetInstallationCost(int penCount)
        {
            return CAPEX_MIN + (decimal)penCount * 300_000m; // Base + per-pen camera cost
        }

        private int CalculateOptimalSessions(double waterTemp, double daylight)
        {
            // More sessions in warm water + long days
            if (waterTemp > 12.0 && daylight > 16.0) return 6;
            if (waterTemp > 8.0 && daylight > 12.0) return 4;
            if (waterTemp > 4.0) return 3;
            return 2; // Cold water: fewer, larger meals
        }
    }

    public class FeedPlan
    {
        public double TotalFeedKg { get; set; }
        public int SessionCount { get; set; }
        public double FeedPerSessionKg { get; set; }
        public double PelletSizeMm { get; set; }
        public double TemperatureFactor { get; set; }
        public double AppetiteFactor { get; set; }
        public double OxygenFactor { get; set; }

        /// <summary>Manual feeding fallback when AI not installed.</summary>
        public static FeedPlan ManualDefault(double avgWeightG, int fishCount)
        {
            double biomassKg = fishCount * avgWeightG / 1000.0;
            double feedKg = biomassKg * 0.015; // Simple 1.5% BW/day
            return new FeedPlan
            {
                TotalFeedKg = feedKg,
                SessionCount = 3,
                FeedPerSessionKg = feedKg / 3.0,
                PelletSizeMm = avgWeightG < 500 ? 5.0 : 9.0,
                TemperatureFactor = 1.0,
                AppetiteFactor = 1.0,
                OxygenFactor = 1.0
            };
        }
    }

    public class AIFeedTickResult
    {
        public double FCRMultiplier { get; set; } = 1.0;
        public decimal DailyFeedSavings { get; set; }
        public decimal DailyLicenseCost { get; set; }
        public bool CameraMaintenanceNeeded { get; set; }
    }
}
