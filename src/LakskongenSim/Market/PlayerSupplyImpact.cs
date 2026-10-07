using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// P15 — Player supply market impact (Fish-Pool-style tension).
    ///
    /// <para>
    /// When the player lands a large harvest, that volume competes with the rest
    /// of Norwegian/national supply and pushes the spot price down. This model
    /// tracks an accumulated, decayable <see cref="Pressure"/>
    /// (0 = no impact, 1 = saturated) relative to the national weekly baseline
    /// <see cref="NationalWeeklyBaselineTonnes"/> = 31,400 t/week — the sum of
    /// base weekly harvests already used by
    /// <see cref="MarketSimulation"/> (see its
    /// <c>NormalCompetitorSupplyTonnesPerWeek</c> constant).
    /// </para>
    ///
    /// <para>
    /// Impact is applied as a bounded, downward price multiplier
    /// <see cref="PriceMultiplier"/> = 1 − <see cref="MaxPriceImpact"/>·Pressure,
    /// so even a very large player harvest can move the price by at most −8%.
    /// A single harvest equal to <see cref="HarvestSaturationRatio"/> (5%) of the
    /// national weekly baseline saturates the cap; smaller harvests scale
    /// proportionally.
    /// </para>
    ///
    /// <para>
    /// Between harvests the pressure relaxes back toward zero
    /// (<see cref="DailyAbsorption"/> per day, i.e. the price multiplier gradually
    /// returns to 1.0). This mirrors how the existing supply/demand shock indices
    /// decay toward their neutral value, so repeated small harvests converge to a
    /// bounded steady state instead of compounding without limit.
    /// </para>
    /// </summary>
    public sealed class PlayerSupplyImpact
    {
        /// <summary>
        /// National weekly supply baseline (tonnes). Matches
        /// <c>MarketSimulation.NormalCompetitorSupplyTonnesPerWeek</c> (31,400 t/week).
        /// </summary>
        public const double NationalWeeklyBaselineTonnes = 31400.0;

        /// <summary>Maximum downward price effect at full pressure (−8%).</summary>
        public const double MaxPriceImpact = 0.08;

        /// <summary>
        /// Fraction of the national weekly baseline that saturates
        /// <see cref="Pressure"/> to 1.0 (5% = 1,570 t single-client harvest).
        /// </summary>
        public const double HarvestSaturationRatio = 0.05;

        /// <summary>Share of accumulated pressure absorbed per simulated day (15%).</summary>
        public const double DailyAbsorption = 0.15;

        /// <summary>Harvest volume (tonnes) that saturates pressure to 1.0.</summary>
        public static double SaturationTonnes => NationalWeeklyBaselineTonnes * HarvestSaturationRatio;

        /// <summary>
        /// Accumulated player-supply pressure in [0, 1]. 0 = no market impact,
        /// 1 = maximum impact (price multiplier pinned at the cap).
        /// </summary>
        public double Pressure { get; private set; }

        /// <summary>
        /// Downward price multiplier in [1 − <see cref="MaxPriceImpact"/>, 1.0]
        /// implied by the current <see cref="Pressure"/>. 1.0 means no effect.
        /// </summary>
        public double PriceMultiplier => 1.0 - MaxPriceImpact * Pressure;

        /// <summary>
        /// Register a player harvest of <paramref name="tonnes"/> metric tonnes.
        /// Non-positive or NaN volumes are ignored. Each call adds pressure
        /// proportional to the harvest relative to <see cref="SaturationTonnes"/>
        /// and clamps the total to 1.0, so calling it every harvest is safe and
        /// repeated small harvests cannot compound unboundedly.
        /// </summary>
        public void ApplyHarvest(double tonnes)
        {
            if (double.IsNaN(tonnes) || tonnes <= 0.0)
                return;

            var incremental = Math.Min(1.0, tonnes / SaturationTonnes);
            Pressure = Math.Clamp(Pressure + incremental, 0.0, 1.0);
        }

        /// <summary>
        /// Advance the impact model by <paramref name="days"/> simulated days,
        /// absorbing <see cref="DailyAbsorption"/> of the remaining pressure each
        /// day so the effect fades gradually rather than crashing and staying down.
        /// </summary>
        public void AdvanceDays(int days)
        {
            if (days <= 0)
                return;

            var retentionPerDay = 1.0 - DailyAbsorption;
            for (var d = 0; d < days; d++)
                Pressure *= retentionPerDay;
        }

        /// <summary>Clear all accumulated pressure (no market impact).</summary>
        public void Reset() => Pressure = 0.0;
    }
}
