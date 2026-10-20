using System;
using System.Linq;
using Xunit;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Save round-trip: PrepareForSave + RestoreFromLoad must preserve the
    /// empire systems that used to be nulled/cleared on save.
    /// </summary>
    public class EmpireSaveRoundTripTests
    {
        private static SimulationEngine NewEngine()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);
            return engine;
        }

        [Fact]
        public void RoundTrip_PreservesVisualMilestonesAndWelfare()
        {
            var engine = NewEngine();
            engine.VisualMilestones.CheckMilestone(Events.VisualMilestones.MilestoneType.FirstHarvest);
            engine.FishWelfare.ModifyCondition(0.25);

            engine.PrepareForSave();
            Assert.NotEmpty(engine.State.AchievedVisualMilestones);
            Assert.Equal((decimal)engine.FishWelfare.ConditionScore, engine.State.FishWelfareCondition);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            Assert.True(loaded.VisualMilestones.IsMilestoneAchieved(Events.VisualMilestones.MilestoneType.FirstHarvest));
            Assert.Equal(engine.FishWelfare.ConditionScore, loaded.FishWelfare.ConditionScore, precision: 5);
        }

        [Fact]
        public void RoundTrip_PreservesBrandAndInvestorAggregates()
        {
            var engine = NewEngine();
            engine.BrandReputation.BrandName = "Test Laks";
            engine.BrandReputation.Certifications.Add("ASC");
            engine.InvestorSystem.IssueBond(5_000_000m);

            engine.PrepareForSave();
            Assert.Equal("Test Laks", engine.State.BrandReputation?.BrandName);
            Assert.Equal(5_000_000m, engine.State.InvestorSystem?.OutstandingBondPrincipal);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            Assert.Equal("Test Laks", loaded.BrandReputation.BrandName);
            Assert.Contains("ASC", loaded.BrandReputation.Certifications);
            Assert.Equal(5_000_000m, loaded.InvestorSystem.OutstandingBondPrincipal);
        }

        [Fact]
        public void RoundTrip_PreservesFactionRelations()
        {
            var engine = NewEngine();
            var faction = engine.StakeholderFactionSystem.GetAllFactions().First();
            engine.StakeholderFactionSystem.UpdateRelationship(faction.Key, 0.3f, "test");

            engine.PrepareForSave();
            Assert.NotEmpty(engine.State.FactionRelations);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            var restored = loaded.StakeholderFactionSystem.GetFaction(faction.Key);
            Assert.NotNull(restored);
            Assert.Equal(faction.Value.RelationshipScore, restored.RelationshipScore, precision: 5);
        }

        [Fact]
        public void RoundTrip_PreservesStockMarketListing()
        {            var engine = NewEngine();
            engine.StockMarket.RestoreState(Finance.OwnershipStatus.EuronextGrowth, 42.5m, 2_000_000m, new[] { 40m, 42.5m });

            engine.PrepareForSave();
            Assert.Equal("EuronextGrowth", engine.State.StockMarket?.Status);
            Assert.Equal(new[] { 40m, 42.5m }, engine.State.StockMarket?.PriceHistory);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            Assert.Equal(Finance.OwnershipStatus.EuronextGrowth, loaded.StockMarket.Status);
            Assert.Equal(42.5m, loaded.StockMarket.SharePrice);
            Assert.Equal(2, loaded.StockMarket.PriceHistory.Count);
        }

        [Fact]
        public void RoundTrip_PreservesCompetitorFinancialsById()
        {
            var engine = NewEngine();
            var target = engine.CompetitorAI.Competitors.First();
            target.Revenue = 1_234_567m;
            target.IsDistressed = true;

            engine.PrepareForSave();
            var saved = Assert.Single(engine.State.CompetitorCompanies, c => c.Id == target.Id);
            Assert.Equal(1_234_567m, saved.Revenue);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            var restored = loaded.CompetitorAI.Competitors.First(c => c.Id == target.Id);
            Assert.Equal(1_234_567m, restored.Revenue);
            Assert.True(restored.IsDistressed);
        }

        [Fact]
        public void RoundTrip_PreservesEmpirePhaseAndUnlocks()
        {
            var engine = NewEngine();
            engine.EmpireProgression.RestoreState(Finance.EmpirePhase.NationalLeader,
                new[] { "offshore" }, new[] { "first_license" }, new[] { "victory_largest" });

            engine.PrepareForSave();
            Assert.Equal("NationalLeader", engine.State.EmpireProgression?.CurrentPhase);

            var loaded = NewEngine();
            loaded.RestoreFromLoad(engine.State);
            Assert.Equal(Finance.EmpirePhase.NationalLeader, loaded.EmpireProgression.CurrentPhase);
            Assert.Contains("offshore", loaded.EmpireProgression.UnlockedFeatures);
            Assert.Equal(1, loaded.EmpireProgression.UnlockedMilestoneCount);
        }
    }
}
