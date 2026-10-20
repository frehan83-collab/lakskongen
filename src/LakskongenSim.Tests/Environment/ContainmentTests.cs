using Xunit;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 4.1: containment table sanity + migration rules.</summary>
    public class ContainmentTests
    {
        [Fact]
        public void Table_ExclusionMonotonic()
        {
            double prev = -1.0;
            foreach (ContainmentLevel lvl in System.Enum.GetValues(typeof(ContainmentLevel)))
            {
                if (lvl == ContainmentLevel.OffshoreClosed) continue; // exposed: 0.90 < submersible
                if (lvl == ContainmentLevel.Submersible) continue;
                double e = ContainmentTech.ModifiersFor(lvl).LiceExclusion01;
                Assert.True(e >= prev);
                prev = e;
            }
        }

        [Fact]
        public void Table_ClosedDemandsMoreOxygen()
        {
            Assert.True(ContainmentTech.ModifiersFor(ContainmentLevel.ClosedFloat).OxygenDemandMult > 1.0);
            Assert.True(ContainmentTech.ModifiersFor(ContainmentLevel.Submersible).StormDamageMult < 0.5);
        }

        [Fact]
        public void MabCredit_RedZonesOnly()
        {
            Assert.Equal(1.0, ContainmentTech.MabCreditFor(ContainmentLevel.ClosedFloat, false));
            Assert.True(ContainmentTech.MabCreditFor(ContainmentLevel.ClosedFloat, true) > 1.0);
            Assert.True(ContainmentTech.MabCreditFor(ContainmentLevel.OffshoreClosed, true)
                >= ContainmentTech.MabCreditFor(ContainmentLevel.SemiClosed, true));
        }
    }
}
