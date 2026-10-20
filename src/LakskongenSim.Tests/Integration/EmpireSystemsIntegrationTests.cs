using System;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Processing;
using LakskongenSim.Events;
using LakskongenSim.Expansion;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Integration tests for the 6 newly-wired empire systems in SimulationEngine (T67).
    /// Tests verify that CompetitorAI, EmpireProgression, StockMarket, BrandReputation,
    /// CrisisCapitalism, and VerticalIntegration systems are correctly initialized and
    /// function over time within the full simulation context.
    /// </summary>
    public class EmpireSystemsIntegrationTests
    {
        private readonly SimulationEngine _engine;
        private readonly GameState _state;

        public EmpireSystemsIntegrationTests()
        {
            _engine = new SimulationEngine();
            _engine.Initialize(enableEvents: false); // Deterministic tests
            _state = _engine.State;
        }

        [Fact]
        public void CompetitorAI_InitializesWithCompanies()
        {
            // Assert: CompetitorAI should be initialized
            Assert.NotNull(_engine.CompetitorAI);

            // CompetitorAI should have at least some predefined companies
            var companies = _engine.CompetitorAI.Competitors;
            Assert.NotNull(companies);
            Assert.NotEmpty(companies);

            // Verify companies have valid properties
            foreach (var company in companies)
            {
                Assert.False(string.IsNullOrEmpty(company.Id), "Company should have an ID");
                Assert.False(string.IsNullOrEmpty(company.Name), "Company should have a name");
                Assert.True(company.LicenseCount >= 0, "License count should be non-negative");
                Assert.True(company.MarketShareNorway >= 0, "Market share should be non-negative");
                Assert.True(company.MarketShareNorway <= 1, "Market share should not exceed 100%");
            }
        }

        [Fact]
        public void EmpireProgression_StartsAtSmallFarmer()
        {
            // Assert: EmpireProgression should be initialized
            Assert.NotNull(_engine.EmpireProgression);

            // Initial phase should be SmallFarmer
            var currentPhase = _engine.EmpireProgression.CurrentPhase;
            Assert.Equal(EmpirePhase.SmallFarmer, currentPhase);

            // Milestones list should exist
            var milestones = _engine.EmpireProgression.Milestones;
            Assert.NotNull(milestones);
        }

        [Fact]
        public void StockMarket_StartsPrivate()
        {
            // Assert: StockMarket should be initialized
            Assert.NotNull(_engine.StockMarket);

            // Initial ownership status should be Private
            Assert.Equal(OwnershipStatus.Private, _engine.StockMarket.Status);

            // Price history should be initialized
            Assert.NotNull(_engine.StockMarket.PriceHistory);
        }

        [Fact]
        public void BrandReputation_InitializesWithDefaultScore()
        {
            // Assert: BrandReputation should be initialized
            Assert.NotNull(_engine.BrandReputation);

            // Should have a default brand name
            Assert.False(string.IsNullOrEmpty(_engine.BrandReputation.BrandName));

            // Overall score should be in valid range (0-100)
            var score = _engine.BrandReputation.OverallScore;
            Assert.True(score >= 0m, "Brand score should be non-negative");
            Assert.True(score <= 100m, "Brand score should not exceed 100");

            // Initial reputation metrics should be in valid ranges
            Assert.True(_engine.BrandReputation.QualityReputation >= 0m);
            Assert.True(_engine.BrandReputation.QualityReputation <= 100m);
            Assert.True(_engine.BrandReputation.SustainabilityReputation >= 0m);
            Assert.True(_engine.BrandReputation.SustainabilityReputation <= 100m);
        }

        [Fact]
        public void CrisisCapitalism_CanGenerateCrises()
        {
            // Assert: CrisisCapitalism should be initialized
            Assert.NotNull(_engine.CrisisCapitalism);

            // An insolvent rival goes into distress at the first quarterly
            // review (day 90) and its bankruptcy opens a crisis (review F499: the
            // distress event had no subscriber and this test only checked >= 0).
            _engine.CompetitorAI.Competitors[0].CashReserves = -50_000_000_000m;
            _engine.Advance(95);

            // Check if system can track active crises (may be zero, but should not error)
            var activeCrises = _engine.CrisisCapitalism.GetActiveCrises(_engine.State.Day);
            Assert.NotNull(activeCrises);
            
            // Verify the system maintains a list of available distressed assets
            var distressedAssets = _engine.CrisisCapitalism.GetAllAvailableDistressedAssets();
            Assert.NotNull(distressedAssets);
            
            // The crisis is open and its assets are on the market.
            Assert.NotEmpty(activeCrises);
            Assert.NotEmpty(distressedAssets);
        }

        [Fact]
        public void VerticalIntegration_StartsWithNoAssets()
        {
            // Assert: VerticalIntegration should be initialized
            Assert.NotNull(_engine.VerticalIntegration);

            // Should start with no owned assets
            var ownedAssets = _engine.VerticalIntegration.GetOwnedAssets();
            Assert.NotNull(ownedAssets);
            Assert.Empty(ownedAssets);

            // Should have buildable assets available
            var buildableAssets = _engine.VerticalIntegration.GetBuildableAssets();
            Assert.NotNull(buildableAssets);
            // May or may not have buildable assets initially depending on unlock conditions
        }

        [Fact]
        public void FullCycle_EmpireSystemsUpdateOverTime()
        {
            // Arrange: Track initial state
            var initialPhase = _engine.EmpireProgression.CurrentPhase;
            var initialCompetitorCount = _engine.CompetitorAI.Competitors.Count;
            
            // Act: Advance simulation for one year
            _engine.Advance(365);

            // Assert: Systems should have updated without crashing
            Assert.Equal(EmpirePhase.SmallFarmer, _engine.EmpireProgression.CurrentPhase);
            Assert.NotNull(_engine.CompetitorAI.Competitors);
            
            // CompetitorAI should still have companies
            var finalCompetitorCount = _engine.CompetitorAI.Competitors.Count;
            Assert.True(finalCompetitorCount > 0, "Should maintain competitor companies over time");

            // BrandReputation score should remain in valid range
            var finalScore = _engine.BrandReputation.OverallScore;
            Assert.True(finalScore >= 0m && finalScore <= 100m, 
                       $"Brand score {finalScore} should remain in valid range after 1 year");

            // StockMarket should maintain valid state
            Assert.Equal(OwnershipStatus.Private, _engine.StockMarket.Status);

            // VerticalIntegration should maintain asset lists
            var assets = _engine.VerticalIntegration.GetOwnedAssets();
            Assert.NotNull(assets);
        }

        [Fact]
        public void CompetitorAI_AffectsMarketDynamics()
        {
            // Arrange: Get initial market state
            var initialMarketPrice = _engine.MarketSimulation?.PricePerKg ?? 0m;
            var initialCompetitors = _engine.CompetitorAI.Competitors;
            var initialTotalMarketShare = initialCompetitors.Sum(c => c.MarketShareNorway);

            // Act: Advance simulation to trigger quarterly competitor updates
            _engine.Advance(91); // Just over one quarter

            // Assert: Market should be affected by competitor dynamics
            var updatedCompetitors = _engine.CompetitorAI.Competitors;
            Assert.NotEmpty(updatedCompetitors);

            // Competitors should maintain realistic market shares
            var totalMarketShare = updatedCompetitors.Sum(c => c.MarketShareNorway);
            Assert.True(totalMarketShare >= 0f, "Total market share should be non-negative");
            Assert.True(totalMarketShare <= 2.0f, "Total market share should be realistic");

            // At least some competitors should have positive production
            var activeCompetitors = updatedCompetitors.Where(c => c.WeeklyHarvestTonnes > 0).ToList();
            Assert.NotEmpty(activeCompetitors);

            // Market price should remain in realistic bounds
            var updatedPrice = _engine.MarketSimulation?.PricePerKg ?? 0m;
            Assert.True(updatedPrice > 0m, "Market price should remain positive");
            Assert.True(updatedPrice >= 30m && updatedPrice <= 150m, 
                       $"Market price {updatedPrice} NOK/kg should be realistic");
        }

        [Fact]
        public void EmpireProgression_TracksPlayerGrowth()
        {
            // Arrange: Initial state
            var initialPhase = _engine.EmpireProgression.CurrentPhase;
            
            // Simulate growth by adding sites and revenue
            _state.SeaSites.Add(new SeaSite("site1"));
            _state.SeaSites.Add(new SeaSite("site2"));

            // Act: Advance to trigger empire progression weekly update
            _engine.Advance(8);

            // Assert: System should track player progress
            var currentPhase = _engine.EmpireProgression.CurrentPhase;
            Assert.Equal(EmpirePhase.SmallFarmer, currentPhase);

            // Milestones should be trackable
            var availableMilestones = _engine.EmpireProgression.Milestones;
            Assert.NotNull(availableMilestones);
        }

        [Fact]
        public void StockMarket_HandlesQuarterlyEarnings()
        {
            // Arrange: Initial state (private company)
            var initialStatus = _engine.StockMarket.Status;
            Assert.Equal(OwnershipStatus.Private, initialStatus);

            // Act: Advance through a full quarter
            _engine.Advance(91);

            // Assert: System should handle quarterly progression without errors
            // Even if private, the system should track financial metrics
            Assert.NotNull(_engine.StockMarket);
            Assert.Equal(OwnershipStatus.Private, _engine.StockMarket.Status);
            
            // Financial statements should be tracked
            Assert.NotNull(_engine.FinancialStatements);
        }

        [Fact]
        public void BrandReputation_RespondsToEvents()
        {
            // Arrange: Initial brand score
            var initialScore = _engine.BrandReputation.OverallScore;

            // Act: Record a positive brand event
            _engine.BrandReputation.RecordEvent(
                ReputationEvent.CertificationEarned,
                "Test certification",
                severity: 1);

            // Assert: Brand score should respond to events
            var updatedScore = _engine.BrandReputation.OverallScore;
            
            // Score should change or remain stable (depending on implementation)
            Assert.True(updatedScore >= 0m && updatedScore <= 100m);
            
            // History should track the event
            Assert.NotEmpty(_engine.BrandReputation.History);
            Assert.Contains(_engine.BrandReputation.History, 
                           h => h.EventType == ReputationEvent.CertificationEarned);
        }

        [Fact]
        public void VerticalIntegration_TracksCostSavings()
        {
            // Arrange: Get buildable assets
            var buildableAssets = _engine.VerticalIntegration.GetBuildableAssets();
            Assert.NotNull(buildableAssets);

            // Act: Check that system can calculate integration scores
            var integrationScore = _engine.VerticalIntegration.GetIntegrationScore();
            
            // Assert: Integration score should be valid
            Assert.True(integrationScore >= 0f, "Integration score should be non-negative");
            Assert.True(integrationScore <= 1f, "Integration score should not exceed 100%");

            // Owned assets should start empty
            var ownedAssets = _engine.VerticalIntegration.GetOwnedAssets();
            Assert.Empty(ownedAssets);
        }

        [Fact]
        public void AllEmpireSystems_MaintainConsistentState()
        {
            // Act: Advance simulation significantly
            _engine.Advance(180); // ~6 months

            // Assert: All empire systems should maintain valid state
            Assert.NotNull(_engine.CompetitorAI);
            Assert.NotNull(_engine.EmpireProgression);
            Assert.NotNull(_engine.StockMarket);
            Assert.NotNull(_engine.BrandReputation);
            Assert.NotNull(_engine.CrisisCapitalism);
            Assert.NotNull(_engine.VerticalIntegration);

            // No system should have null critical properties
            Assert.NotNull(_engine.CompetitorAI.Competitors);
            Assert.Equal(EmpirePhase.SmallFarmer, _engine.EmpireProgression.CurrentPhase);
            Assert.Equal(OwnershipStatus.Private, _engine.StockMarket.Status);
            Assert.True(_engine.BrandReputation.OverallScore >= 0m);
            Assert.NotNull(_engine.CrisisCapitalism.GetActiveCrises(_engine.State.Day));
            Assert.NotNull(_engine.VerticalIntegration.GetOwnedAssets());
        }
    }
}
