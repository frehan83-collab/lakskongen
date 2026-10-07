using System;
using System.IO;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// P9/P11 save round-trip: insurance policies, bloom mitigations, the
    /// resource-rent tax opt-in/paid total and the Price Council pen price must
    /// all survive PrepareForSave + RestoreFromLoad (and a full JSON save file).
    /// </summary>
    public class DepthSystemsSaveRoundTripTests
    {
        private static SimulationEngine NewEngine()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);
            return engine;
        }

        [Fact]
        public void RoundTrip_PreservesInsuranceMitigationsResourceRentAndPenPrice()
        {
            var engine = NewEngine();
            engine.Insurance.Subscribe(new InsurancePolicy(InsuranceKind.Bloom, 250_000m, 0.8, 1_000_000m));
            engine.Insurance.Subscribe(new InsurancePolicy(InsuranceKind.Storm, 180_000m, 0.5, 500_000m));
            engine.State.BloomMitigations.Add(new BloomMitigationState { SiteId = "S1", Kind = "pumping" });
            engine.State.BloomMitigations.Add(new BloomMitigationState { SiteId = "S2", Kind = "deep_feeding" });
            engine.EnableResourceRentTax = true;
            engine.ResourceRentPaid = 1_234_567m;
            engine.PenPriceCouncil.Update(85.5m);

            engine.PrepareForSave();
            Assert.Equal(2, engine.State.InsurancePolicies.Count);
            Assert.Equal(2, engine.State.BloomMitigations.Count);
            Assert.True(engine.State.EnableResourceRentTax);
            Assert.Equal(1_234_567m, engine.State.ResourceRentPaid);
            Assert.Equal(85.5m, engine.State.PenPriceCouncilLastValue);

            string dir = Path.Combine(Path.GetTempPath(), "lakskongen-depth-" + Guid.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveLoadManager(dir);
                saves.SaveGame(engine.State, "depth");
                var reloadedState = saves.LoadGame("depth");

                var loaded = NewEngine();
                loaded.RestoreFromLoad(reloadedState);

                Assert.Equal(2, loaded.Insurance.Policies.Count);
                Assert.Contains(loaded.Insurance.Policies,
                    p => p.Kind == InsuranceKind.Bloom && p.PremiumNokPerYear == 250_000m && p.DeductibleNok == 1_000_000m);
                Assert.Contains(loaded.Insurance.Policies,
                    p => p.Kind == InsuranceKind.Storm && p.CoverageFraction == 0.5);
                Assert.Contains(loaded.State.BloomMitigations,
                    m => m.SiteId == "S1" && m.Kind == "pumping");
                Assert.Contains(loaded.State.BloomMitigations,
                    m => m.SiteId == "S2" && m.Kind == "deep_feeding");
                Assert.True(loaded.EnableResourceRentTax);
                Assert.Equal(1_234_567m, loaded.ResourceRentPaid);
                Assert.Equal(85.5m, loaded.PenPriceCouncil.MonthlyPenPrice);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void LegacySave_WithoutDepthFields_LoadsWithSafeDefaults()
        {
            var engine = NewEngine();
            engine.PrepareForSave();

            // Simulate a pre-depth save: explicitly null the additive lists.
            engine.State.InsurancePolicies = null!;
            engine.State.BloomMitigations = null!;

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);

            Assert.Empty(loaded.Insurance.Policies);
            Assert.False(loaded.EnableResourceRentTax);
            Assert.Equal(0m, loaded.ResourceRentPaid);
            Assert.NotNull(loaded.State.BloomMitigations);
            Assert.NotNull(loaded.State.InsurancePolicies);
        }
    }
}
