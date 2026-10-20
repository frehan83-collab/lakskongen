using Xunit;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>P8: permeable-barrier (Smoltscreen) model sanity.</summary>
    public class BarrierTechTests
    {
        [Fact]
        public void LiceBlocking_IsMonotonic_UpToFullFlowPlateau()
        {
            double prev = -1.0;
            for (double flow = 0.0; flow <= BarrierTech.FlowOptimalMaxMs + 1e-9; flow += 0.01)
            {
                double b = BarrierTech.LiceBlockingFraction(flow, 150);
                Assert.True(b >= prev - 1e-12, $"blocking dropped at flow {flow:F2}");
                prev = b;
            }
        }

        [Fact]
        public void LiceBlocking_FullWithinOptimalBand_DegradedBelowAndAbove()
        {
            double atFull = BarrierTech.LiceBlockingFraction(0.5, 150);
            double below = BarrierTech.LiceBlockingFraction(0.10, 150);
            double above = BarrierTech.LiceBlockingFraction(2.0, 150);

            Assert.True(atFull > below, "below optimal band should block less");
            Assert.True(atFull > above, "above tear flow should block less");
            Assert.True(atFull >= 0.90, "150 µm mesh should block ~97% at full flow");
        }

        [Fact]
        public void FinerMesh_BlocksAtLeastAsMuch()
        {
            double fine = BarrierTech.LiceBlockingFraction(0.5, 100);
            double coarse = BarrierTech.LiceBlockingFraction(0.5, 300);
            Assert.True(fine >= coarse);
        }

        [Fact]
        public void JellyfishBlocking_AlsoFlowDependent()
        {
            double full = BarrierTech.JellyfishBlockingFraction(0.5, 200);
            double weak = BarrierTech.JellyfishBlockingFraction(0.02, 200);
            Assert.True(full > weak);
            Assert.True(full > 0.5);
        }

        [Fact]
        public void CleaningDecay_ReducesBlocking_WhenOverdue()
        {
            var spec = BarrierTech.SpecFor(BarrierKind.Smoltscreen);
            Assert.Equal(1.0, BarrierTech.CleaningDecayFactor(BarrierKind.Smoltscreen, 0));
            Assert.Equal(1.0, BarrierTech.CleaningDecayFactor(BarrierKind.Smoltscreen, spec.CleaningIntervalDays));

            double slightlyOverdue = BarrierTech.CleaningDecayFactor(
                BarrierKind.Smoltscreen, spec.CleaningIntervalDays + 10);
            double veryOverdue = BarrierTech.CleaningDecayFactor(
                BarrierKind.Smoltscreen, spec.CleaningIntervalDays + 90);

            Assert.True(slightlyOverdue < 1.0);
            Assert.True(veryOverdue < slightlyOverdue);
            Assert.True(veryOverdue >= spec.MinCleaningFactor - 1e-12);
        }

        [Fact]
        public void EffectiveLiceBlocking_DropsWhenUncleaned_NoneIsZero()
        {
            double clean = BarrierTech.EffectiveLiceBlocking(BarrierKind.Smoltscreen, 0.5, 0);
            double dirty = BarrierTech.EffectiveLiceBlocking(
                BarrierKind.Smoltscreen, 0.5,
                BarrierTech.SpecFor(BarrierKind.Smoltscreen).CleaningIntervalDays + 60);

            Assert.True(clean > dirty);
            Assert.Equal(0.0, BarrierTech.EffectiveLiceBlocking(BarrierKind.None, 0.5, 0));
            Assert.Equal(0.0, BarrierTech.EffectiveJellyfishBlocking(BarrierKind.None, 0.5, 0));
        }

        [Fact]
        public void Smoltscreen_NotEligibleForEnvironmentalFlexibility_WithRationale()
        {
            Assert.False(BarrierTech.IsEligibleForEnvironmentalFlexibility(BarrierKind.Smoltscreen));
            Assert.False(BarrierTech.IsEligibleForEnvironmentalFlexibility(BarrierKind.None));

            var rationale = BarrierTech.EnvironmentalFlexibilityRationale(BarrierKind.Smoltscreen);
            Assert.False(string.IsNullOrWhiteSpace(rationale));
            Assert.Contains("tett", rationale);
        }

        [Fact]
        public void Smoltscreen_CapexIsAboutHalfOfClosedContainmentRetrofit()
        {
            decimal fraction = BarrierTech.CapexFractionOfClosedContainmentRetrofit(BarrierKind.Smoltscreen);
            Assert.True(fraction > 0m && fraction < 0.6m, $"expected ~half, got {fraction}");
            Assert.Equal(0m, BarrierTech.CapexFractionOfClosedContainmentRetrofit(BarrierKind.None));
        }
    }
}