using System;
using Xunit;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// Catastrophe insurance payout math (deductible / coverage / cap), premium
    /// totals, and bloom-mitigation effectiveness ordering + monotonicity.
    /// Pure-model assertions — no Godot, no simulation engine.
    /// </summary>
    public class InsuranceMitigationTests
    {
        // ─── Insurance payouts ──────────────────────────────────────────

        [Fact]
        public void Payout_NoPolicy_IsZero()
        {
            var ins = new InsuranceSystem();
            Assert.Equal(0m, ins.ComputePayout(InsuranceKind.Bloom, 1_000_000m));
        }

        [Fact]
        public void Payout_NonPositiveLoss_IsZero()
        {
            var ins = new InsuranceSystem();
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Bloom, 50_000m, 0.9, 100_000m));
            Assert.Equal(0m, ins.ComputePayout(InsuranceKind.Bloom, 0m));
            Assert.Equal(0m, ins.ComputePayout(InsuranceKind.Bloom, -5m));
        }

        [Fact]
        public void Payout_AppliesDeductibleThenCoverage()
        {
            var ins = new InsuranceSystem();
            // loss 1,000,000; deductible 100,000; coverage 80% → (1,000,000 − 100,000) × 0.8 = 720,000
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Bloom, 50_000m, 0.8, 100_000m));
            Assert.Equal(720_000m, ins.ComputePayout(InsuranceKind.Bloom, 1_000_000m));
        }

        [Fact]
        public void Payout_LossBelowDeductible_IsZero()
        {
            var ins = new InsuranceSystem();
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Storm, 40_000m, 0.9, 250_000m));
            Assert.Equal(0m, ins.ComputePayout(InsuranceKind.Storm, 200_000m));
        }

        [Fact]
        public void Payout_NeverExceedsLoss()
        {
            var ins = new InsuranceSystem();
            // Full cover, no deductible, plus a second policy → aggregate must cap at the loss.
            ins.Subscribe(new InsurancePolicy(InsuranceKind.ISA, 60_000m, 1.0, 0m));
            ins.Subscribe(new InsurancePolicy(InsuranceKind.ISA, 20_000m, 1.0, 0m));
            Assert.Equal(500_000m, ins.ComputePayout(InsuranceKind.ISA, 500_000m));
        }

        [Fact]
        public void Payout_CoverageIsClampedTo_0_1_OnSubscribe()
        {
            var ins = new InsuranceSystem();
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Mortality, 10_000m, 1.5, 0m));
            // Over-100% coverage is clamped to 100% and still capped at the loss.
            Assert.Equal(100_000m, ins.ComputePayout(InsuranceKind.Mortality, 100_000m));
        }

        [Fact]
        public void Payout_OnlyMatchingKind_IsPaid()
        {
            var ins = new InsuranceSystem();
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Storm, 30_000m, 0.9, 0m));
            Assert.Equal(0m, ins.ComputePayout(InsuranceKind.Bloom, 1_000_000m));
        }

        [Fact]
        public void Premium_Totals_SumAllPolicies_AndPerKind()
        {
            var ins = new InsuranceSystem();
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Bloom, 50_000m, 0.8, 100_000m));
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Storm, 40_000m, 0.9, 50_000m));
            ins.Subscribe(new InsurancePolicy(InsuranceKind.Bloom, 25_000m, 0.5, 0m));

            Assert.Equal(115_000m, ins.TotalAnnualPremium);
            Assert.Equal(75_000m, ins.TotalAnnualPremiumFor(InsuranceKind.Bloom));
            Assert.Equal(40_000m, ins.TotalAnnualPremiumFor(InsuranceKind.Storm));
            Assert.Equal(0m, ins.TotalAnnualPremiumFor(InsuranceKind.ISA));
        }

        [Fact]
        public void PayoutFor_Static_MatchesInstanceRule()
        {
            var policy = new InsurancePolicy(InsuranceKind.Bloom, 50_000m, 0.8, 100_000m);
            Assert.Equal(720_000m, InsuranceSystem.PayoutFor(policy, 1_000_000m));
        }

        // ─── Bloom mitigation ──────────────────────────────────────────

        [Fact]
        public void Mitigation_None_RemovesNothing_AndCostsNothing()
        {
            var outcome = BloomMitigation.MitigationEffectiveness(MitigationKind.None, 1.0);
            Assert.Equal(0.0, outcome.MortalityReductionFraction);
            Assert.Equal(0m, outcome.CostNok);
        }

        [Fact]
        public void Mitigation_Effectiveness_IsMonotonic_InSeverity()
        {
            foreach (MitigationKind kind in Enum.GetValues<MitigationKind>())
            {
                var low = BloomMitigation.MitigationEffectiveness(kind, 0.2);
                var high = BloomMitigation.MitigationEffectiveness(kind, 0.8);
                Assert.True(high.MortalityReductionFraction >= low.MortalityReductionFraction,
                    $"{kind}: reduction must be non-decreasing in severity");
                Assert.True(high.CostNok >= low.CostNok,
                    $"{kind}: cost must be non-decreasing in severity");
            }
        }

        [Fact]
        public void Mitigation_EffectivenessOrdering_IsRespected()
        {
            const double s = 0.5;
            double r(MitigationKind k) => BloomMitigation.MitigationEffectiveness(k, s).MortalityReductionFraction;

            Assert.True(r(MitigationKind.BubbleCurtain) > r(MitigationKind.O2Diffuser));
            Assert.True(r(MitigationKind.O2Diffuser) > r(MitigationKind.Pumping));
            Assert.True(r(MitigationKind.Pumping) > r(MitigationKind.DeepFeeding));
            Assert.True(r(MitigationKind.DeepFeeding) > r(MitigationKind.None));
        }

        [Fact]
        public void Mitigation_CostOrdering_FollowsEffectiveness()
        {
            const double s = 0.5;
            decimal c(MitigationKind k) => BloomMitigation.MitigationEffectiveness(k, s).CostNok;

            Assert.True(c(MitigationKind.BubbleCurtain) > c(MitigationKind.O2Diffuser));
            Assert.True(c(MitigationKind.O2Diffuser) > c(MitigationKind.Pumping));
            Assert.True(c(MitigationKind.Pumping) > c(MitigationKind.DeepFeeding));
            Assert.True(c(MitigationKind.DeepFeeding) > c(MitigationKind.None));
        }

        [Fact]
        public void Mitigation_Reduction_IsClampedAndBoundedByMax()
        {
            var over = BloomMitigation.MitigationEffectiveness(MitigationKind.BubbleCurtain, 2.0);
            var under = BloomMitigation.MitigationEffectiveness(MitigationKind.BubbleCurtain, -1.0);
            Assert.Equal(BloomMitigation.MaxReduction(MitigationKind.BubbleCurtain), over.MortalityReductionFraction, precision: 10);
            Assert.Equal(0.0, under.MortalityReductionFraction, precision: 10);
        }

        [Fact]
        public void BestFor_MapsBloomKinds_ToSpecialists()
        {
            Assert.Equal(MitigationKind.BubbleCurtain, BloomMitigation.BestFor("jellyfish"));
            Assert.Equal(MitigationKind.BubbleCurtain, BloomMitigation.BestFor("JELLY"));
            Assert.Equal(MitigationKind.O2Diffuser, BloomMitigation.BestFor("algae"));
            Assert.Equal(MitigationKind.O2Diffuser, BloomMitigation.BestFor("hypoxia"));
            Assert.Equal(MitigationKind.None, BloomMitigation.BestFor("storm"));
            Assert.Equal(MitigationKind.None, BloomMitigation.BestFor("unknown-kind"));
            Assert.Equal(MitigationKind.None, BloomMitigation.BestFor(null));
        }

        [Fact]
        public void Mitigation_TargetAware_IsMostEffectiveWhenMatched()
        {
            var matched = BloomMitigation.MitigationEffectiveness(MitigationKind.BubbleCurtain, 0.6, "jellyfish");
            var mismatched = BloomMitigation.MitigationEffectiveness(MitigationKind.BubbleCurtain, 0.6, "algae");
            var generic = BloomMitigation.MitigationEffectiveness(MitigationKind.BubbleCurtain, 0.6);

            Assert.Equal(generic.MortalityReductionFraction, matched.MortalityReductionFraction, precision: 10);
            Assert.True(mismatched.MortalityReductionFraction < matched.MortalityReductionFraction);
            Assert.True(mismatched.MortalityReductionFraction > 0.0);
        }

        [Fact]
        public void IsInstalled_ReflectsSeaSiteFlags()
        {
            var site = new SeaSite("SITE-1") { HasBubbleCurtain = true, HasO2Diffuser = false };
            Assert.True(BloomMitigation.IsInstalled(MitigationKind.BubbleCurtain, site));
            Assert.False(BloomMitigation.IsInstalled(MitigationKind.O2Diffuser, site));
            Assert.False(BloomMitigation.IsInstalled(MitigationKind.Pumping, site));
            Assert.False(BloomMitigation.IsInstalled(MitigationKind.BubbleCurtain, null!));
        }
    }
}
