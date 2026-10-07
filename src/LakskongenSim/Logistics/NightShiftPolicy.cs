using System;
using System.Collections.Generic;

namespace LakskongenSim.Logistics
{
    /// <summary>
    /// Night-shift ROC policy (Phase 5.2): how the ROC handles 00-06 events.
    /// </summary>
    public enum NightShiftPolicy
    {
        Standard,       // Default: watch crew on standby, normal response
        ScanOnly,       // Detect only, queue for 06:00 digest (no action at night)
        AutomatedROC    // Full auto: ROC fires cheapest countermeasure instantly
    }

    /// <summary>
    /// Policy effects on night event handling (Phase 5.2).
    /// </summary>
    public static class NightShiftResolver
    {
        public const decimal AutomatedPremiumMonthly = 85_000m; // crew premium
        public const double AutomatedMisfireRate = 0.12; // 12% wrong action
        public const double ScanOnlyEfficacy = 0.7; // delayed response
        public const int FatiguePerNightAuto = 6; // crew fatigue % per night
        public const int FatigueRecoveryPerDay = 8; // recovery if not auto

        /// <summary>
        /// Response time multiplier for night events (00-06).
        /// </summary>
        public static double ResponseMult(NightShiftPolicy policy) =>
            policy switch
            {
                NightShiftPolicy.AutomatedROC => 0.15, // instant
                NightShiftPolicy.ScanOnly => 0.4,      // queued, 40% speed
                _ => 1.0                              // standard watch
            };

        /// <summary>
        /// Night misfire chance (wrong countermeasure / wrong site).
        /// </summary>
        public static double MisfireChance(NightShiftPolicy policy, int avgCrewSkill) =>
            policy == NightShiftPolicy.AutomatedROC
                ? AutomatedMisfireRate * (1.0 - avgCrewSkill / 10.0)
                : 0.0;

        /// <summary>
        /// Detection chance for night events.
        /// </summary>
        public static double DetectionChance(NightShiftPolicy policy) =>
            policy switch
            {
                NightShiftPolicy.ScanOnly => 0.85, // detect but no action
                NightShiftPolicy.AutomatedROC => 0.95, // auto-detect
                _ => 0.7 // standard watch
            };

        /// <summary>
        /// Crew fatigue delta for the night (+ve = more tired).
        /// </summary>
        public static int FatigueDelta(NightShiftPolicy policy) =>
            policy == NightShiftPolicy.AutomatedROC ? FatiguePerNightAuto : 0;

        /// <summary>
        /// Monthly premium cost.
        /// </summary>
        public static decimal MonthlyCost(NightShiftPolicy policy) =>
            policy == NightShiftPolicy.AutomatedROC ? AutomatedPremiumMonthly : 0m;
    }
}