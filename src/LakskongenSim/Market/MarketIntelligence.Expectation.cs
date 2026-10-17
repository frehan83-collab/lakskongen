using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// The weekly market-intelligence tick's expected value (roadmap U2-05,
    /// read by <see cref="MarketSimulation.ExpectedPrices"/>). Pure: no draws.
    /// </summary>
    public partial class MarketIntelligenceSystem
    {
        /// <summary>
        /// What the weekly tick <paramref name="weeksAhead"/> weeks from now
        /// (1 = the next one) is expected to hand the market: the news events
        /// already running, faded and aged by then (a new event is not
        /// foreseen); the rivals' weekly harvest at that week's season with
        /// their random factor at its mean (1) and their disruptions as they
        /// stand; and the brand premium after that many weeks of decay.
        /// </summary>
        public (double SupplyFactor, double DemandFactor, decimal PriceShock, double CompetitorTonnes, decimal Premium)
            ExpectedWeek(int weeksAhead)
        {
            int n = Math.Max(1, weeksAhead);
            double supply = 1.0, demand = 1.0;
            decimal shock = 0m;
            foreach (var evt in _activeEvents)
            {
                int remaining = evt.RemainingWeeks - n;
                if (remaining <= 0) continue;
                double fade = Math.Min(1.0, remaining / (double)Math.Max(1, evt.DurationWeeks));
                supply *= 1.0 + (evt.SupplyImpact - 1.0) * fade;
                demand *= 1.0 + (evt.DemandImpact - 1.0) * fade;
                shock += evt.PriceShockNOK * (decimal)fade;
            }

            int week = _currentWeek + n;
            double seasonal = CompetitorSeasonalFactor(week);
            double tonnes = 0.0;
            foreach (var c in _competitors)
                tonnes += c.BaseWeeklyHarvestTonnes * seasonal * c.DisruptionFactor;

            double score = Math.Max(0.0, PlayerReputation.Score - MarketBrandReputation.WeeklyDecayPoints * n);
            decimal premium = 1.0m + (decimal)(score / 1000.0);
            return (supply, demand, shock, tonnes, premium);
        }
    }
}
