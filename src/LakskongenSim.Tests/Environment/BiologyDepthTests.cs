using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// BIOLOGY depth: post-smolt/"SuperSmolt" carryover tiers and precision-feeding
    /// (feed spreading) model. All deltas are research-grounded; see SmoltQuality.cs.
    /// </summary>
    public class BiologyDepthTests
    {
        // ─── Smolt quality tiers ───

        [Fact]
        public void SuperSmolt_Deltas_AreMonotonicAcrossTiers()
        {
            var std = SmoltQualityRegistry.Standard;
            var large = SmoltQualityRegistry.Large;
            var super = SmoltQualityRegistry.SuperSmolt;

            Assert.True(std.MortalityMultiplier > large.MortalityMultiplier);
            Assert.True(large.MortalityMultiplier > super.MortalityMultiplier);

            Assert.True(std.GrowthMultiplier < large.GrowthMultiplier);
            Assert.True(large.GrowthMultiplier < super.GrowthMultiplier);

            Assert.True(std.TimeToHarvestMultiplier > large.TimeToHarvestMultiplier);
            Assert.True(large.TimeToHarvestMultiplier > super.TimeToHarvestMultiplier);

            Assert.True(std.Quality01 < large.Quality01);
            Assert.True(large.Quality01 < super.Quality01);
        }

        [Fact]
        public void SuperSmolt_Carryover_MatchesChileTrial()
        {
            var super = SmoltQualityRegistry.SuperSmolt;

            // 4-month mortality 1.02% vs 1.53% baseline → multiplier ≈ 0.67.
            Assert.InRange(super.MortalityMultiplier, 0.60, 0.72);
            // Harvest cumulative 5.69% vs 8.6% absolute mortality.
            Assert.InRange(super.ApplyMortality(0.086f), 0.050, 0.060);
            // Growth from median 7-month weight 2,055g vs 1,781g → ≈ 1.13.
            Assert.InRange(super.GrowthMultiplier, 1.10, 1.16);
            // 51% harvested by month 13 vs 17-28% → faster time-to-harvest.
            Assert.InRange(super.TimeToHarvestMultiplier, 0.65, 0.75);
            Assert.True(super.ApplyDaysToHarvest(600.0) < 600.0);
        }

        [Fact]
        public void Helpers_AreDeterministic_AndClamped()
        {
            Assert.Equal(
                SmoltQualityRegistry.SuperSmolt.ApplyMortality(0.05f),
                SmoltQualityRegistry.SuperSmolt.ApplyMortality(0.05f));

            // QualityFromTransferWeightKg is monotonic non-decreasing.
            double q1 = SmoltQualityRegistry.QualityFromTransferWeightKg(0.05);
            double q2 = SmoltQualityRegistry.QualityFromTransferWeightKg(0.30);
            double q3 = SmoltQualityRegistry.QualityFromTransferWeightKg(2.00);
            Assert.True(q1 <= q2 && q2 <= q3);
            Assert.Equal(SmoltQualityRegistry.Standard.Quality01, q1);
            Assert.Equal(SmoltQualityRegistry.SuperSmolt.Quality01, q3);

            Assert.Equal(
                SmoltQualityTier.SuperSmolt,
                SmoltQualityRegistry.TierFromTransferWeightKg(0.80));
        }

        // ─── Cohort carryover hook ───

        [Fact]
        public void Cohort_DefaultsToStandard_AndCanBeStamped()
        {
            var cohort = new FishPopulation.Cohort(365, 1000, 0.10, LifeStage.Smolt);
            Assert.Equal(SmoltQualityTier.Standard, cohort.Quality);

            SmoltQualityRegistry.SuperSmolt.Stamp(cohort);
            Assert.Equal(SmoltQualityTier.SuperSmolt, cohort.Quality);
        }

        [Fact]
        public void Hatchery_ReleaseSmolt_CarriesQualityTier()
        {
            var hatchery = new Hatchery();
            FishPopulation.Cohort? released = null;
            hatchery.OnNewCohort += c => released = c;

            hatchery.ReleaseSmolt(5000, SmoltQualityTier.SuperSmolt);

            Assert.NotNull(released);
            Assert.Equal(SmoltQualityTier.SuperSmolt, released!.Quality);

            hatchery.ReleaseSmolt(5000);
            Assert.Equal(SmoltQualityTier.Standard, released!.Quality);
        }

        // ─── Precision feeding ───

        [Fact]
        public void PrecisionFeeding_LowersFCR_AsQualityRises()
        {
            Assert.True(FeedSystem.PrecisionFCRMultiplier(0.0) > FeedSystem.PrecisionFCRMultiplier(0.5));
            Assert.True(FeedSystem.PrecisionFCRMultiplier(0.5) > FeedSystem.PrecisionFCRMultiplier(1.0));
            Assert.Equal(1.0, FeedSystem.PrecisionFCRMultiplier(1.0), 6);
            Assert.Equal(1.20, FeedSystem.PrecisionFCRMultiplier(0.0), 6);

            var poor = Feed(pop: BuildPopulation(), spreadQuality01: 0.0);
            var perfect = Feed(pop: BuildPopulation(), spreadQuality01: 1.0);
            Assert.True(poor.ActualFCR > perfect.ActualFCR,
                $"Poor spread FCR ({poor.ActualFCR:F3}) should exceed perfect ({perfect.ActualFCR:F3})");
        }

        [Fact]
        public void PrecisionFeeding_WasteAndVariance_AreMonotonic()
        {
            Assert.True(FeedSystem.CalculateFeedWasteFraction(0.0) > FeedSystem.CalculateFeedWasteFraction(0.5));
            Assert.True(FeedSystem.CalculateFeedWasteFraction(0.5) > FeedSystem.CalculateFeedWasteFraction(1.0));

            Assert.True(FeedSystem.CalculateSizeVarianceCv(0.0) > FeedSystem.CalculateSizeVarianceCv(0.5));
            Assert.True(FeedSystem.CalculateSizeVarianceCv(0.5) > FeedSystem.CalculateSizeVarianceCv(1.0));

            var poor = Feed(pop: BuildPopulation(), spreadQuality01: 0.0);
            var perfect = Feed(pop: BuildPopulation(), spreadQuality01: 1.0);
            Assert.True(poor.FeedWasteFraction > perfect.FeedWasteFraction);
            Assert.True(poor.FeedWasteKg > perfect.FeedWasteKg);
            Assert.True(poor.SizeVarianceCv > perfect.SizeVarianceCv);
        }

        private static FishPopulation BuildPopulation()
        {
            var pop = new FishPopulation();
            pop.Cohorts.Add(new FishPopulation.Cohort(180, 10000, 2.0, LifeStage.Seawater));
            return pop;
        }

        private static FeedResult Feed(FishPopulation pop, double spreadQuality01)
        {
            var feed = new FeedSystem();
            return feed.Feed(pop, 1, 11.0, 25.0, 1.0, spreadQuality01);
        }
    }
}