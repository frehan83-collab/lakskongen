using System.Collections.Generic;
using Xunit;
using LakskongenSim.Market;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 2.3: contract lifecycle sanity.</summary>
    public class ContractLifecycleTests
    {
        [Fact]
        public void AcceptAndFulfill_PaysLockedPrice()
        {
            var cs = new ContractSystem();
            var offer = cs.ProposeContract("TestBuyer", ContractType.Forward, 80m, 10_000);
            var contract = cs.AcceptOffer(offer);
            Assert.Single(cs.ActiveContracts);
            var (payment, _) = cs.FulfillContract(contract.Id, 10_000);
            Assert.Equal(800_000m, payment);
            Assert.True(cs.ActiveContracts[0].IsComplete);
        }

        [Fact]
        public void Expiry_ChargesShortfallPenalty()
        {
            var cs = new ContractSystem();
            var offer = cs.ProposeContract("TestBuyer", ContractType.Forward, 80m, 10_000,
                QualityGrade.Standard, 0.10m);
            cs.AcceptOffer(offer);
            // DurationWeeks = 4 → advance past expiry, collecting all.
            var allExpired = new List<(Contract, decimal)>();
            for (int w = 0; w < 20; w++)
                allExpired.AddRange(cs.AdvanceWeek());
            Assert.Empty(cs.ActiveContracts);
            Assert.NotEmpty(allExpired);
            Assert.True(allExpired[0].Item2 > 0m);
        }
    }
}
