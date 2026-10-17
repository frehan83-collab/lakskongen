using System;
using System.Collections.Generic;

namespace LakskongenSim.Market
{
    /// <summary>
    /// The market's expected path (roadmap U2-05, the harvest planner): what
    /// the spot can be expected to pay on each of the coming days, read off
    /// the daily step's deterministic part. Pure: it copies the state it reads
    /// and draws no random numbers.
    /// </summary>
    public partial class MarketSimulation
    {
        /// <summary>
        /// The realized price (NOK/kg, the price a harvest sells at) expected on
        /// each of the next <paramref name="days"/> simulated days (index 0 = the
        /// next day): the daily step without its noise and its random shocks.
        /// The base price reverts toward the month's seasonal target, the
        /// supply/demand offset fades as the indices relax, a running shock
        /// window keeps its wide band until it closes, and the premiums
        /// (certification, research, reputation) hold. The player's own
        /// harvests press the price (P15): the pressure fades by its daily
        /// absorption, and <paramref name="playerTonnesSold"/> (gutted tonnes
        /// sold on each of those days, after that day's price) adds to it, as
        /// <see cref="ApplyPlayerHarvest"/> does. The weekly tick hands on its
        /// expected value (<see cref="MarketIntelligenceSystem.ExpectedWeek"/>):
        /// the news already running fades, the rivals harvest at the season's
        /// level, the brand premium decays; news yet to break is not foreseen.
        /// </summary>
        public decimal[] ExpectedPrices(int days, IReadOnlyList<double>? playerTonnesSold = null)
        {
            days = Math.Max(0, days);
            var prices = new decimal[days];
            double pressure = PlayerSupply.Pressure;
            double supply = GlobalSupplyIndex;
            double demand = GlobalDemandIndex;
            double offset = _supplyDemandOffsetNok;
            int shockDays = _shockDaysRemaining;
            decimal basePrice = BasePricePerKg;
            double thetaDaily = (double)MeanReversionSpeedWeekly / _daysPerWeek;
            double retention = 1.0 - PlayerSupplyImpact.DailyAbsorption;
            decimal premiums = _certificationMultiplier * _researchPriceMultiplier * Intelligence.PlayerReputation.PremiumMultiplier;
            double bandLow = (double)LongTermMean - NormalBandHalfWidthNok;
            double bandHigh = (double)LongTermMean + NormalBandHalfWidthNok;
            decimal realizedHigh = LongTermMean + (decimal)RealizedBandUpperHalfWidthNok;

            int weeksAhead = 0;
            for (int i = 0; i < days; i++)
            {
                pressure *= retention;
                if ((_simulationDay + i + 1) % _daysPerWeek == 0)
                {
                    // The weekly tick: running news fades, rivals harvest at the
                    // season's level, the brand premium decays (AdvanceDays).
                    var week = Intelligence.ExpectedWeek(++weeksAhead);
                    if (Math.Abs(week.SupplyFactor - 1.0) > 0.001 || Math.Abs(week.DemandFactor - 1.0) > 0.001)
                    {
                        supply = supply * EventBlendRetention + (supply * week.SupplyFactor) * EventBlendNewWeight;
                        demand = demand * EventBlendRetention + (demand * week.DemandFactor) * EventBlendNewWeight;
                    }
                    if (week.PriceShock != 0m)
                        basePrice = ClampPrice(basePrice + week.PriceShock * ShockAbsorptionFactor);
                    supply *= 1.0 + (week.CompetitorTonnes - NormalCompetitorSupplyTonnesPerWeek)
                        / NormalCompetitorSupplyTonnesPerWeek * CompetitorSupplySensitivity;
                    premiums = _certificationMultiplier * _researchPriceMultiplier * week.Premium;
                }
                supply = Math.Clamp(supply + (1.0 - supply) * IndexMeanReversionDaily, IndexClampMin, IndexClampMax);
                demand = Math.Clamp(demand + (1.0 - demand) * IndexMeanReversionDaily, IndexClampMin, IndexClampMax);

                decimal target = LongTermMean + GetSeasonalOffsetForSimulationDay(_simulationDay + i + 1);
                double current = (double)basePrice;
                double drift = thetaDaily * ((double)target - current);
                offset = offset * SupplyDemandOffsetDecay
                    + Math.Clamp((demand - supply) * SupplyDemandPriceGain, -MaxSupplyDemandEffectNok, MaxSupplyDemandEffectNok);
                double next = current + drift + offset;

                bool shock = shockDays > 0;
                if (shock) shockDays--;
                if (!shock) next = Math.Clamp(next, bandLow, bandHigh);
                basePrice = ClampPrice((decimal)next);

                decimal raw = basePrice * premiums * (decimal)(1.0 - PlayerSupplyImpact.MaxPriceImpact * pressure);
                if (!shock) raw = Math.Clamp(raw, (decimal)bandLow, realizedHigh);
                prices[i] = raw;

                if (playerTonnesSold != null && i < playerTonnesSold.Count && playerTonnesSold[i] > 0.0)
                    pressure = Math.Clamp(pressure + Math.Min(1.0, playerTonnesSold[i] / PlayerSupplyImpact.SaturationTonnes), 0.0, 1.0);
            }
            return prices;
        }
    }
}
