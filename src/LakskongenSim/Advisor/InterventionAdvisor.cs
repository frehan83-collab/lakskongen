using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Health;
using LakskongenSim.Farm;
using LakskongenSim.Market;
using LakskongenSim.Optimization;
using LakskongenSim.Calibration;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Intervention advisor (Phase 5.3): produces ranked, dated suggestions
    /// from the 7-day twin-lite projection. Pure logic, headless-testable.
    /// </summary>
    public static class InterventionAdvisor
    {
        /// <summary>Suggestions are written in Norwegian Bokmål with its number format.</summary>
        private static readonly System.Globalization.CultureInfo Nb =
            System.Globalization.CultureInfo.GetCultureInfo("nb-NO");

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
            // The FCR target is the calibrated one (roadmap U1-03: one target
            // for the whole game; this branch used its own 1.25).
            double fcrTarget = CalibrationRegistry.Default.Biology.OptimalFCR;
            if (feedFcr > fcrTarget)
            {
                var delta = (decimal)(feedFcr - fcrTarget) * 500_000m; // rough estimate
                suggestions.Add(new Suggestion(
                    "recipe-tune",
                    "Justere fôrresept",
                    "",
                    1,
                    delta,
                    0.6,
                    string.Format(Nb, "FCR {0:F2} over målet {1:F2} — senk erstatningsandelen eller øk astaxanthin for −0,02 i FCR",
                        feedFcr, fcrTarget)));
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
                            string.Format(Nb, "Lusetallet {0:F2} passerer 0,50 om {1} d — behandling nå sparer om lag {2:N0} kr",
                                currentLice, breachDay, delta)));
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
                            string.Format(Nb, "{0:N0} t biomasse er klar om {1} d — høsting sikrer inntekten",
                                biomass[harvestDay] / 1000, harvestDay)));
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