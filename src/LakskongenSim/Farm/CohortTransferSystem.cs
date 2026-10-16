using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Finance;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Cohort transfer system (Phase 5.3).
    /// Enables moving fish between sea sites or pens (grading, fallowing, MAB mgmt,
    /// disease isolation, offshore stocking). Uses wellboat: 3-day lead time,
    /// 50 NOK/fish handling + 200k base hire. Mortality 0.2-0.5% depending on
    /// weight and stress. Requires available MAB at destination.
    /// </summary>
    public static class CohortTransferSystem
    {
        public enum TransferReason
        {
            Grading,          // Size grading: split cohorts by weight
            Fallowing,        // Move before mandatory fallow
            MabManagement,    // Redistribute biomass to stay under MAB
            DiseaseIsolation, // Move healthy fish away from outbreak
            OffshoreStocking, // Transfer post-smolt to offshore
            ContainmentUpgrade // Move during containment upgrade
        }

        public sealed record TransferPlan(
            string SourceSiteId,
            string DestSiteId,
            int FishCount,
            double MeanWeightKg,
            TransferReason Reason,
            int EtaDays,
            decimal EstimatedCostNok,
            double EstimatedMortalityPct,
            bool RequiresWellboat
        );

        public sealed record TransferResult(
            bool Success,
            int FishTransferred,
            int FishLost,
            decimal ActualCostNok,
            string Message
        );

        /// <summary>Base wellboat hire for transfers.</summary>
        public const decimal BaseWellboatHire = 200_000m;
        /// <summary>Per-fish handling cost (pumping, grading, counting).</summary>
        public const decimal PerFishHandlingCost = 50m;
        /// <summary>Standard lead time in days.</summary>
        public const int StandardLeadTimeDays = 3;

        /// <summary>
        /// Mortality rate % based on mean weight and reason.
        /// Smaller fish = higher stress mortality.
        /// </summary>
        public static double MortalityRatePct(double meanWeightKg, TransferReason reason, bool hasVeterinarian)
        {
            double baseRate = reason switch
            {
                TransferReason.Grading => 0.3,
                TransferReason.Fallowing => 0.2,
                TransferReason.MabManagement => 0.25,
                TransferReason.DiseaseIsolation => 0.5, // stressed fish
                TransferReason.OffshoreStocking => 0.15, // robust post-smolt
                TransferReason.ContainmentUpgrade => 0.2,
                _ => 0.3
            };
            // Weight factor: <0.5kg = 2x, 0.5-1kg = 1.5x, 1-3kg = 1x, >3kg = 0.8x
            double weightMult = meanWeightKg switch
            {
                < 0.5 => 2.0,
                < 1.0 => 1.5,
                < 3.0 => 1.0,
                _ => 0.8
            };
            double vetMult = hasVeterinarian ? 0.7 : 1.0;
            return baseRate * weightMult * vetMult;
        }

        /// <summary>
        /// Estimate cost and mortality for a proposed transfer.
        /// </summary>
        public static TransferPlan PlanTransfer(
            string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg,
            TransferReason reason, GameState state, bool hasVeterinarian)
        {
            var cost = BaseWellboatHire + fishCount * PerFishHandlingCost;
            double mortPct = MortalityRatePct(meanWeightKg, reason, hasVeterinarian);
            return new TransferPlan(
                sourceSiteId, destSiteId, fishCount, meanWeightKg, reason,
                StandardLeadTimeDays, cost, mortPct, true);
        }

        /// <summary>
        /// Execute a transfer immediately (used when wellboat arrives).
        /// Returns result with actual losses and cost. The cost is paid through
        /// <c>payCost</c> (the engine ledger's PayCost) so it is booked as OpEx.
        /// </summary>
        public static TransferResult ExecuteTransfer(
            TransferPlan plan, GameState state, Action<decimal> payCost, Random rng,
            GrowthModel growth, MortalityModel mortality)
        {
            ArgumentNullException.ThrowIfNull(payCost);
            ArgumentNullException.ThrowIfNull(growth);
            ArgumentNullException.ThrowIfNull(mortality);
            var sourceSite = state.SeaSites.FirstOrDefault(s => s.Id == plan.SourceSiteId);
            var destSite = state.SeaSites.FirstOrDefault(s => s.Id == plan.DestSiteId);
            if (sourceSite == null || destSite == null)
                return new TransferResult(false, 0, 0, 0m, "Site not found");
            // F2-03 (site-kind audit): transfers run between the fjord sites of
            // one zone (GetValidDestinations); an offshore unit's zones are
            // stocked and emptied through its own verbs, and a fish moved into
            // one here would sit in none of its zones.
            if (sourceSite.Kind != SiteKind.Fjord || destSite.Kind != SiteKind.Fjord)
                return new TransferResult(false, 0, 0, 0m, "An offshore unit takes no cohort transfer");

            // Check destination MAB capacity
            double destBiomass = destSite.Pens.Sum(p => p.GetTotalBiomassKg());
            double incomingBiomassKg = plan.FishCount * plan.MeanWeightKg;
            if (destBiomass + incomingBiomassKg > destSite.MABTonnes * 1000.0)
                return new TransferResult(false, 0, 0, 0m, "Destination MAB exceeded");

            // Find source population(s) with enough fish
            int remaining = plan.FishCount;
            var movedCohorts = new List<(FishPopulation Pop, FishPopulation.Cohort Cohort, int Count)>();

            foreach (var pop in sourceSite.Pens)
            {
                foreach (var cohort in pop.Cohorts)
                {
                    if (cohort.Count <= 0) continue;
                    int take = Math.Min(cohort.Count, remaining);
                    movedCohorts.Add((pop, cohort, take));
                    remaining -= take;
                    if (remaining <= 0) break;
                }
                if (remaining <= 0) break;
            }

            if (remaining > 0)
                return new TransferResult(false, 0, 0, 0m, "Insufficient fish at source");

            // Apply mortality
            double mortPct = plan.EstimatedMortalityPct;
            int totalLost = 0;
            int totalMoved = 0;

            foreach (var (srcPop, srcCohort, count) in movedCohorts)
            {
                int lost = (int)Math.Round(count * mortPct / 100.0);
                int arrived = count - lost;
                if (arrived <= 0)
                {
                    // Every fish of the slice died in handling. They used to be
                    // reported lost but stay in the source pen (S2-05).
                    totalLost += srcPop.RemoveDead(srcCohort, count, MortalityCause.Handling);
                    continue;
                }

                // Remove from source: the handling deaths, then the fish that move
                totalLost += srcPop.RemoveDead(srcCohort, lost, MortalityCause.Handling);
                srcPop.RemoveAlive(srcCohort, arrived, FishRemoval.TransferOut);
                totalMoved += arrived;

                // Add to destination (create or merge cohort)
                var destPop = destSite.Pens.FirstOrDefault(p => p.ZoneId == destSite.ZoneId);
                if (destPop == null)
                {
                    // A fresh pen gets the engine's shared models, like StockSmolts:
                    // without them the next biology tick dereferenced null (F382).
                    destPop = new FishPopulation
                    {
                        ZoneId = destSite.ZoneId,
                        SiteId = destSite.Id,
                        SeaLiceLoad = srcPop.SeaLiceLoad
                    };
                    destPop.InjectDependencies(growth, mortality);
                    destSite.Stock(destPop);
                    state.Populations.Add(destPop);
                }

                // Merge only into a cohort of the same stage, size, smolt quality
                // tier and genome; anything else stays a separate cohort so the
                // tier and breeding values survive the move (F386).
                var existingCohort = destPop.Cohorts.FirstOrDefault(c =>
                    c.Stage == srcCohort.Stage
                    && Math.Abs(c.MeanWeightKg - srcCohort.MeanWeightKg) < 0.1
                    && c.Quality == srcCohort.Quality
                    && ReferenceEquals(c.Genetics, srcCohort.Genetics));
                if (existingCohort != null)
                {
                    // Merge: weighted average weight
                    double totalW = existingCohort.Count * existingCohort.MeanWeightKg + arrived * srcCohort.MeanWeightKg;
                    existingCohort.Count += arrived;
                    existingCohort.MeanWeightKg = totalW / existingCohort.Count;
                }
                else
                {
                    destPop.Cohorts.Add(new FishPopulation.Cohort(
                        srcCohort.AgeDays, arrived, srcCohort.MeanWeightKg, srcCohort.Stage, srcCohort.Genetics)
                    {
                        Quality = srcCohort.Quality
                    });
                }
            }

            // Clean up empty source cohorts
            foreach (var pop in sourceSite.Pens)
                pop.Cohorts.RemoveAll(c => c.Count <= 0);

            // Charge cost (on credit when cash is short)
            payCost(plan.EstimatedCostNok);

            // Log event
            string reasonText = plan.Reason switch
            {
                TransferReason.Grading => "Størrelsessortering",
                TransferReason.Fallowing => "Fallowing-flytting",
                TransferReason.MabManagement => "MAB-håndtering",
                TransferReason.DiseaseIsolation => "Sykdom-isolering",
                TransferReason.OffshoreStocking => "Offshore-setting",
                TransferReason.ContainmentUpgrade => "Containment-oppgradering",
                _ => "Overføring"
            };

            state.EventLog.Add(new Events.EventRecord
            {
                Day = state.Day,
                Name = "Flytting av fisk",
                Category = Events.EventCategory.Equipment,
                Description = Core.SimText.Nb($"{reasonText}: {totalMoved:N0} fisk ({plan.MeanWeightKg:F1} kg) {LabelOf(state, plan.SourceSiteId)} → {LabelOf(state, plan.DestSiteId)}, {totalLost:N0} tapt ({mortPct:F1} %)"),
                BalanceChange = -plan.EstimatedCostNok
            });
            state.TrimEventLog();

            return new TransferResult(true, totalMoved, totalLost, plan.EstimatedCostNok,
                $"{totalMoved:N0} fisk overført, {totalLost:N0} tap");
        }

        /// <summary>A site as the player reads it, for the event log.</summary>
        private static string LabelOf(GameState state, string siteId) =>
            state.SeaSites.FirstOrDefault(s => s.Id == siteId)?.Label ?? SiteNames.Humanize(siteId);

        /// <summary>
        /// Get all possible destination sites for a transfer (same zone, MAB headroom).
        /// </summary>
        public static List<SeaSite> GetValidDestinations(string sourceSiteId, GameState state, double incomingBiomassKg)
        {
            var source = state.SeaSites.FirstOrDefault(s => s.Id == sourceSiteId);
            if (source == null) return new();
            return state.SeaSites
                .Where(s => s.Id != sourceSiteId && s.ZoneId == source.ZoneId)
                .Where(s => s.Pens.Sum(p => p.GetTotalBiomassKg()) + incomingBiomassKg <= s.MABTonnes * 1000.0)
                .ToList();
        }
    }
}