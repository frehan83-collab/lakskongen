using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Health;
using LakskongenSim.Farm;
using LakskongenSim.Market;
using LakskongenSim.Optimization;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Intervention advisor (Phase 5.3): produces ranked, dated suggestions
    /// from the 7-day twin-lite projection. Pure logic, headless-testable.
    /// </summary>
    public static class InterventionAdvisor
    {
        public sealed record Suggestion(
            string Id,
            string Action,
            string SiteId,
            int DaysOut,
            decimal DeltaNok,
            double Confidence,
            string Reasoning);

        /// <summary>
        /// Rank 0-3 candidate interventions for the next 7 days.
        /// Returns empty list if no meaningful actions found.
        /// </summary>
        public static List<Suggestion> Rank(
            GameState state,
            ForecastTwin.ForecastResult forecast,
            double currentLice,
            double gillScore,
            double feedFcr,
            List<Contract> activeContracts,
            int crewCount,
            int day)
        {
            var suggestions = new List<Suggestion>();
            var lice = forecast.LicePerFish ?? Array.Empty<double>();
            var biomass = forecast.BiomassKg ?? Array.Empty<double>();

            // Site is OPTIONAL — suggestions are farm-wide when no site is stocked,
            // so the advisor still helps between stocking cycles.
            var site = state.SeaSites.FirstOrDefault(s => s.Pens.Any(p => p.TotalCount > 0))
                       ?? state.SeaSites.FirstOrDefault();
            var siteId = site?.Id ?? "";

            int fish = 0;
            foreach (var pop in state.Populations)
                foreach (var c in pop.Cohorts) fish += c.Count;
            if (fish == 0 && site != null)
                foreach (var pop in site.Pens)
                    foreach (var c in pop.Cohorts) fish += c.Count;

            // 1. Feed recipe optimisation — evaluated FIRST and independently of
            //    site/population state (it depends only on FCR). Previously this
            //    branch was unreachable whenever Population.Count == 0.
            if (feedFcr > 1.25)
            {
                var delta = (decimal)(feedFcr - 1.25) * 500_000m; // rough estimate
                suggestions.Add(new Suggestion(
                    "recipe-tune",
                    "Justere fôrresept",
                    "",
                    1,
                    delta,
                    0.6,
                    $"FCR {feedFcr:F2} > 1.25 — senk erstatningsandel / øk astaxanthin for -0.02 FCR"));
            }

            if (state.Populations.Count > 0 || state.SeaSites.Count > 0)
            {
                // 2. Preemptive delousing if lice trajectory breaches 0.5 within 5 days.
                var breachDay = -1;
                for (int d = 0; d < lice.Length; d++)
                {
                    if (lice[d] > 0.5) { breachDay = d; break; }
                }
                if (breachDay >= 0 && breachDay <= 5 && fish > 0 && biomass.Length > breachDay)
                {
                    var cost = fish * 35m; // mechanical delousing, NOK/fish
                    // Avoided loss = a fraction of the fish value exposed by a breach
                    // (biomass_kg × 80 NOK/kg × 15% of value at risk). The old code
                    // divided biomass by 1000, which made delta always negative.
                    var valueAtRisk = (decimal)biomass[breachDay] * 80m * 0.15m;
                    var delta = valueAtRisk - cost;
                    if (delta > 0)
                        suggestions.Add(new Suggestion(
                            "delouse-preempt",
                            "Preemptivt avlus",
                            siteId,
                            breachDay,
                            delta,
                            0.8,
                            $"Luseprosjektet {currentLice:F2}→>0,5 om {breachDay}d — behandla nå sparer ~{delta:N0} NOK"));
                }

                // 3. Harvest timing vs biomass readiness.
                if (biomass.Length > 0)
                {
                    var harvestDay = -1;
                    for (int d = 0; d < biomass.Length; d++)
                    {
                        if (biomass[d] > 500_000) { harvestDay = d; break; }
                    }
                    if (harvestDay >= 0 && harvestDay <= 6)
                    {
                        // Gross harvest value (NOK): biomass_kg × 80 NOK/kg.
                        var revenue = (decimal)biomass[harvestDay] * 80m;
                        suggestions.Add(new Suggestion(
                            "harvest-window",
                            "Høsting i vindu",
                            siteId,
                            harvestDay,
                            revenue,
                            0.7,
                            $"Biomasse {biomass[harvestDay]/1000:N0}t klar dag {harvestDay} — høsting sikrer inntekt"));
                    }
                }
            }

            // Sort by NOK impact desc, then confidence
            suggestions.Sort((a, b) =>
            {
                var c = b.DeltaNok.CompareTo(a.DeltaNok);
                if (c != 0) return c;
                return b.Confidence.CompareTo(a.Confidence);
            });

            var result = suggestions.Take(3).ToList();
            return result;
        }
    }
}