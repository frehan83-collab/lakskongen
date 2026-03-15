using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    // =========================================================================
    // EmpireProgression.cs - Empire Building Milestones & Phases (T79)
    // =========================================================================
    // Tracks the player's progression from small farmer to industry titan.
    // Manages phase transitions, milestone unlocks, victory conditions,
    // and the emergent narrative of empire building.
    // =========================================================================

    /// <summary>
    /// Player's current empire building phase.
    /// </summary>
    public enum EmpirePhase
    {
        /// <summary>Phase 1: 1-2 licenses, 2-4 pens, toll processing, spot market</summary>
        SmallFarmer,
        /// <summary>Phase 2: 5-10 licenses, 2-3 zones, own processing, spot+contracts</summary>
        RegionalPlayer,
        /// <summary>Phase 3: 20-50 licenses, 5+ zones, full processing, exports, logistics</summary>
        NationalLeader,
        /// <summary>Phase 4: 100+ licenses, international, vertically integrated, stock listing</summary>
        GlobalTitan,
        /// <summary>Phase 5: Acquire competitors, >30% market share, political influence</summary>
        IndustryMonopolist
    }

    /// <summary>
    /// Growth strategy the player has chosen.
    /// </summary>
    public enum GrowthStrategy
    {
        /// <summary>Slow, safe, capital-efficient</summary>
        Organic,
        /// <summary>Fast, risky, capital-intensive M&amp;A</summary>
        MergersAndAcquisitions,
        /// <summary>Joint ventures, shared risk/reward</summary>
        Partnership,
        /// <summary>Balanced mix of strategies</summary>
        Balanced
    }

    /// <summary>
    /// Product strategy for market positioning.
    /// </summary>
    public enum ProductStrategy
    {
        /// <summary>Sell whole fish/HOG — low margin, high volume</summary>
        Commodity,
        /// <summary>Smoked, sashimi, fillets — high margin, lower volume</summary>
        ValueAdd,
        /// <summary>Retail consumer products — highest margin, marketing costs</summary>
        BrandedRetail,
        /// <summary>Mix of all strategies</summary>
        Mixed
    }

    /// <summary>
    /// A milestone achievement in the empire building journey.
    /// </summary>
    public class EmpireMilestone
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;  // "Growth", "Financial", "Strategic", "Victory"
        public EmpirePhase RequiredPhase { get; set; }
        public bool IsUnlocked { get; set; }
        public DateTime? UnlockedAt { get; set; }

        // Unlock conditions (checked dynamically)
        public int? RequiredLicenses { get; set; }
        public float? RequiredMarketShare { get; set; }
        public decimal? RequiredRevenue { get; set; }
        public decimal? RequiredValuation { get; set; }
        public int? RequiredSites { get; set; }
        public int? RequiredZones { get; set; }
        public bool? RequiresIPO { get; set; }
        public bool? RequiresProcessing { get; set; }
        public bool? RequiresHatchery { get; set; }
        public bool? RequiresInternational { get; set; }
        public int? RequiredAcquisitions { get; set; }
        public int? RequiredSustainabilityScore { get; set; }

        // Reward
        public string RewardDescription { get; set; } = string.Empty;
        public float? RewardBrandBonus { get; set; }
        public float? RewardCostReduction { get; set; }
        public decimal? RewardCashBonus { get; set; }
        public string? RewardUnlockId { get; set; }  // Unlocks another feature
    }

    /// <summary>
    /// Victory condition for endgame.
    /// </summary>
    public class VictoryCondition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public bool IsAchieved { get; set; }
        public float Progress { get; set; }  // 0-1

        // Condition parameters
        public Func<EmpireState, float>? ProgressCalculator { get; set; }
    }

    /// <summary>
    /// Snapshot of player's empire state for milestone/phase checking.
    /// </summary>
    public class EmpireState
    {
        public int LicenseCount { get; set; }
        public int SiteCount { get; set; }
        public int ZoneCount { get; set; }
        public float MarketShareNorway { get; set; }
        public float MarketShareGlobal { get; set; }
        public decimal AnnualRevenue { get; set; }
        public decimal AnnualEBITDA { get; set; }
        public decimal Valuation { get; set; }
        public decimal TotalAssets { get; set; }
        public int AcquisitionCount { get; set; }
        public bool HasProcessing { get; set; }
        public bool HasHatchery { get; set; }
        public bool HasFeedMill { get; set; }
        public bool HasLogistics { get; set; }
        public bool HasRetailBrand { get; set; }
        public bool IsPubliclyListed { get; set; }
        public bool HasInternationalOps { get; set; }
        public List<string> InternationalRegions { get; set; } = new();
        public int SustainabilityScore { get; set; }
        public int CompetitorsAcquired { get; set; }
        public GrowthStrategy CurrentStrategy { get; set; }
        public ProductStrategy CurrentProductStrategy { get; set; }
        public float IntegrationScore { get; set; }  // 0-1 from VerticalIntegrationSystem
        public int EmployeeCount { get; set; }
        public float WeeklyHarvestTonnes { get; set; }
    }

    /// <summary>
    /// Event when player transitions to a new empire phase.
    /// </summary>
    public class PhaseTransition
    {
        public EmpirePhase OldPhase { get; set; }
        public EmpirePhase NewPhase { get; set; }
        public string Headline { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> NewUnlocks { get; set; } = new();
    }

    /// <summary>
    /// Tracks and manages the player's empire building progression.
    /// </summary>
    public class EmpireProgressionSystem
    {
        private readonly List<EmpireMilestone> _milestones = new();
        private readonly List<VictoryCondition> _victoryConditions = new();
        private EmpirePhase _currentPhase = EmpirePhase.SmallFarmer;
        private GrowthStrategy _growthStrategy = GrowthStrategy.Organic;
        private ProductStrategy _productStrategy = ProductStrategy.Commodity;
        private readonly List<string> _unlockedFeatures = new();
        private readonly List<PhaseTransition> _phaseHistory = new();

        public EmpirePhase CurrentPhase => _currentPhase;
        public GrowthStrategy CurrentGrowthStrategy => _growthStrategy;
        public ProductStrategy CurrentProductStrategy => _productStrategy;
        public IReadOnlyList<EmpireMilestone> Milestones => _milestones;
        public IReadOnlyList<VictoryCondition> VictoryConditions => _victoryConditions;
        public IReadOnlyList<string> UnlockedFeatures => _unlockedFeatures;
        public IReadOnlyList<PhaseTransition> PhaseHistory => _phaseHistory;
        public int UnlockedMilestoneCount => _milestones.Count(m => m.IsUnlocked);
        public int TotalMilestoneCount => _milestones.Count;

        public event Action<PhaseTransition>? OnPhaseTransition;
        public event Action<EmpireMilestone>? OnMilestoneUnlocked;
        public event Action<VictoryCondition>? OnVictoryAchieved;

        public EmpireProgressionSystem()
        {
            InitializeMilestones();
            InitializeVictoryConditions();
        }

        /// <summary>
        /// Update empire progression based on current state.
        /// Call this periodically (e.g., quarterly or when major events occur).
        /// </summary>
        public void Update(EmpireState state)
        {
            // Check phase transitions
            var newPhase = DeterminePhase(state);
            if (newPhase != _currentPhase)
            {
                TransitionToPhase(newPhase, state);
            }

            // Check milestones
            foreach (var milestone in _milestones.Where(m => !m.IsUnlocked))
            {
                if (CheckMilestone(milestone, state))
                {
                    UnlockMilestone(milestone);
                }
            }

            // Update victory conditions
            foreach (var victory in _victoryConditions.Where(v => !v.IsAchieved))
            {
                if (victory.ProgressCalculator != null)
                {
                    victory.Progress = victory.ProgressCalculator(state);
                    if (victory.Progress >= 1.0f)
                    {
                        victory.IsAchieved = true;
                        OnVictoryAchieved?.Invoke(victory);
                    }
                }
            }
        }

        /// <summary>
        /// Set growth strategy (affects recommendations and AI behavior).
        /// </summary>
        public void SetGrowthStrategy(GrowthStrategy strategy)
        {
            _growthStrategy = strategy;
        }

        /// <summary>
        /// Set product strategy.
        /// </summary>
        public void SetProductStrategy(ProductStrategy strategy)
        {
            _productStrategy = strategy;
        }

        /// <summary>
        /// Get phase requirements for the next phase.
        /// </summary>
        public (EmpirePhase NextPhase, string Requirements) GetNextPhaseRequirements()
        {
            return _currentPhase switch
            {
                EmpirePhase.SmallFarmer => (EmpirePhase.RegionalPlayer,
                    "5+ licenses, 2+ zones, own processing OR 500M+ NOK revenue"),
                EmpirePhase.RegionalPlayer => (EmpirePhase.NationalLeader,
                    "20+ licenses, 5+ zones, 1B+ NOK revenue, processing + exports"),
                EmpirePhase.NationalLeader => (EmpirePhase.GlobalTitan,
                    "100+ licenses, international operations, 10B+ NOK revenue, stock listing"),
                EmpirePhase.GlobalTitan => (EmpirePhase.IndustryMonopolist,
                    "30%+ Norwegian market share, acquire 2+ competitors, 50B+ NOK valuation"),
                _ => (EmpirePhase.IndustryMonopolist, "You've reached the top. Sandbox mode.")
            };
        }

        /// <summary>
        /// Get empire summary for UI display.
        /// </summary>
        public EmpireSummary GetSummary(EmpireState state)
        {
            var (nextPhase, requirements) = GetNextPhaseRequirements();

            return new EmpireSummary
            {
                Phase = _currentPhase,
                PhaseName = GetPhaseName(_currentPhase),
                PhaseDescription = GetPhaseDescription(_currentPhase),
                NextPhase = nextPhase,
                NextPhaseRequirements = requirements,
                GrowthStrategy = _growthStrategy,
                ProductStrategy = _productStrategy,
                MilestonesUnlocked = UnlockedMilestoneCount,
                MilestonesTotal = TotalMilestoneCount,
                VictoriesAchieved = _victoryConditions.Count(v => v.IsAchieved),
                VictoriesTotal = _victoryConditions.Count,
                PhaseProgress = CalculatePhaseProgress(state)
            };
        }

        // ─── Private methods ─────────────────────────────────

        private EmpirePhase DeterminePhase(EmpireState state)
        {
            // Phase 5: Industry Monopolist
            if (state.MarketShareNorway >= 0.30f && state.CompetitorsAcquired >= 2 && state.Valuation >= 50_000_000_000m)
                return EmpirePhase.IndustryMonopolist;

            // Phase 4: Global Titan
            if (state.LicenseCount >= 100 && state.HasInternationalOps && state.AnnualRevenue >= 10_000_000_000m)
                return EmpirePhase.GlobalTitan;

            // Phase 3: National Leader
            if (state.LicenseCount >= 20 && state.ZoneCount >= 5 && state.AnnualRevenue >= 1_000_000_000m)
                return EmpirePhase.NationalLeader;

            // Phase 2: Regional Player
            if (state.LicenseCount >= 5 && (state.ZoneCount >= 2 || state.AnnualRevenue >= 500_000_000m))
                return EmpirePhase.RegionalPlayer;

            return EmpirePhase.SmallFarmer;
        }

        private void TransitionToPhase(EmpirePhase newPhase, EmpireState state)
        {
            var transition = new PhaseTransition
            {
                OldPhase = _currentPhase,
                NewPhase = newPhase,
                Headline = $"🏆 PHASE TRANSITION: {GetPhaseName(_currentPhase)} → {GetPhaseName(newPhase)}",
                Description = GetPhaseDescription(newPhase),
                NewUnlocks = GetPhaseUnlocks(newPhase)
            };

            _currentPhase = newPhase;
            _unlockedFeatures.AddRange(transition.NewUnlocks);
            _phaseHistory.Add(transition);

            OnPhaseTransition?.Invoke(transition);
        }

        private float CalculatePhaseProgress(EmpireState state)
        {
            return _currentPhase switch
            {
                EmpirePhase.SmallFarmer => Math.Min(1f,
                    (state.LicenseCount / 5f) * 0.4f +
                    (state.ZoneCount / 2f) * 0.3f +
                    ((float)(state.AnnualRevenue / 500_000_000m)) * 0.3f),

                EmpirePhase.RegionalPlayer => Math.Min(1f,
                    (state.LicenseCount / 20f) * 0.3f +
                    (state.ZoneCount / 5f) * 0.2f +
                    ((float)(state.AnnualRevenue / 1_000_000_000m)) * 0.3f +
                    (state.HasProcessing ? 0.2f : 0f)),

                EmpirePhase.NationalLeader => Math.Min(1f,
                    (state.LicenseCount / 100f) * 0.25f +
                    (state.HasInternationalOps ? 0.25f : 0f) +
                    ((float)(state.AnnualRevenue / 10_000_000_000m)) * 0.25f +
                    (state.IsPubliclyListed ? 0.25f : 0f)),

                EmpirePhase.GlobalTitan => Math.Min(1f,
                    (state.MarketShareNorway / 0.30f) * 0.4f +
                    (state.CompetitorsAcquired / 2f) * 0.3f +
                    ((float)(state.Valuation / 50_000_000_000m)) * 0.3f),

                _ => 1f  // Monopolist is endgame
            };
        }

        private bool CheckMilestone(EmpireMilestone m, EmpireState state)
        {
            if (m.RequiredLicenses.HasValue && state.LicenseCount < m.RequiredLicenses) return false;
            if (m.RequiredMarketShare.HasValue && state.MarketShareNorway < m.RequiredMarketShare) return false;
            if (m.RequiredRevenue.HasValue && state.AnnualRevenue < m.RequiredRevenue) return false;
            if (m.RequiredValuation.HasValue && state.Valuation < m.RequiredValuation) return false;
            if (m.RequiredSites.HasValue && state.SiteCount < m.RequiredSites) return false;
            if (m.RequiredZones.HasValue && state.ZoneCount < m.RequiredZones) return false;
            if (m.RequiresIPO == true && !state.IsPubliclyListed) return false;
            if (m.RequiresProcessing == true && !state.HasProcessing) return false;
            if (m.RequiresHatchery == true && !state.HasHatchery) return false;
            if (m.RequiresInternational == true && !state.HasInternationalOps) return false;
            if (m.RequiredAcquisitions.HasValue && state.AcquisitionCount < m.RequiredAcquisitions) return false;
            if (m.RequiredSustainabilityScore.HasValue && state.SustainabilityScore < m.RequiredSustainabilityScore) return false;
            return true;
        }

        private void UnlockMilestone(EmpireMilestone milestone)
        {
            milestone.IsUnlocked = true;
            milestone.UnlockedAt = DateTime.Now;

            if (!string.IsNullOrEmpty(milestone.RewardUnlockId))
                _unlockedFeatures.Add(milestone.RewardUnlockId);

            OnMilestoneUnlocked?.Invoke(milestone);
        }

        private string GetPhaseName(EmpirePhase phase) => phase switch
        {
            EmpirePhase.SmallFarmer => "Small Farmer",
            EmpirePhase.RegionalPlayer => "Regional Player",
            EmpirePhase.NationalLeader => "National Leader",
            EmpirePhase.GlobalTitan => "Global Titan",
            EmpirePhase.IndustryMonopolist => "Industry Monopolist",
            _ => "Unknown"
        };

        private string GetPhaseDescription(EmpirePhase phase) => phase switch
        {
            EmpirePhase.SmallFarmer =>
                "Starting out with a handful of pens and a dream. Sell on the spot market, use toll processing, and build your first cash reserves.",
            EmpirePhase.RegionalPlayer =>
                "Multiple sites across zones. You're a known name in the region. Time for your own processing line and contract sales.",
            EmpirePhase.NationalLeader =>
                "A major force in Norwegian salmon. Full processing, export to EU and Asia, logistics fleet. Competitors take notice.",
            EmpirePhase.GlobalTitan =>
                "International operations, stock exchange listing, vertically integrated from hatchery to retail. You ARE the industry.",
            EmpirePhase.IndustryMonopolist =>
                "You dominate. Competitors fall one by one. Regulators watch nervously. The Lakskongen has arrived.",
            _ => ""
        };

        private List<string> GetPhaseUnlocks(EmpirePhase phase) => phase switch
        {
            EmpirePhase.RegionalPlayer => new()
            {
                "Contract Sales",
                "Processing Investment",
                "Multi-Zone Management",
                "Regional Branding"
            },
            EmpirePhase.NationalLeader => new()
            {
                "Export Markets (EU, Asia)",
                "Logistics Fleet",
                "IPO Preparation",
                "Full Value Chain",
                "Analyst Coverage"
            },
            EmpirePhase.GlobalTitan => new()
            {
                "International Expansion",
                "Stock Exchange Listing",
                "Competitor Acquisitions",
                "Retail Brand Launch",
                "Political Lobbying"
            },
            EmpirePhase.IndustryMonopolist => new()
            {
                "Industry Consolidation",
                "Regulatory Influence",
                "Monopoly Pricing Power",
                "Diversification (Trout, Cod)",
                "Victory Conditions"
            },
            _ => new()
        };

        // ─── Milestone initialization ─────────────────────────

        private void InitializeMilestones()
        {
            _milestones.AddRange(new[]
            {
                // Growth milestones
                new EmpireMilestone
                {
                    Id = "first_license", Name = "First License", Category = "Growth",
                    Description = "Acquire your first aquaculture license",
                    RequiredLicenses = 1,
                    RewardDescription = "Welcome to the industry!",
                    RewardCashBonus = 100_000m
                },
                new EmpireMilestone
                {
                    Id = "five_licenses", Name = "Regional Footprint", Category = "Growth",
                    Description = "Operate 5 licenses",
                    RequiredLicenses = 5, RequiredPhase = EmpirePhase.RegionalPlayer,
                    RewardDescription = "Unlock contract sales",
                    RewardUnlockId = "contract_sales"
                },
                new EmpireMilestone
                {
                    Id = "twenty_licenses", Name = "National Presence", Category = "Growth",
                    Description = "Operate 20 licenses across 5+ zones",
                    RequiredLicenses = 20, RequiredZones = 5,
                    RewardDescription = "Unlock export markets",
                    RewardUnlockId = "export_markets"
                },
                new EmpireMilestone
                {
                    Id = "hundred_licenses", Name = "Hundred Club", Category = "Growth",
                    Description = "Control 100+ aquaculture licenses",
                    RequiredLicenses = 100,
                    RewardDescription = "+5% brand premium",
                    RewardBrandBonus = 0.05f
                },
                new EmpireMilestone
                {
                    Id = "five_percent_share", Name = "Market Mover", Category = "Growth",
                    Description = "Achieve 5% Norwegian market share",
                    RequiredMarketShare = 0.05f,
                    RewardDescription = "Competitors start watching you"
                },
                new EmpireMilestone
                {
                    Id = "ten_percent_share", Name = "Top 5 Producer", Category = "Growth",
                    Description = "Achieve 10% Norwegian market share",
                    RequiredMarketShare = 0.10f,
                    RewardDescription = "-2% production costs from scale",
                    RewardCostReduction = 0.02f
                },

                // Financial milestones
                new EmpireMilestone
                {
                    Id = "first_billion", Name = "Billion Kroner Club", Category = "Financial",
                    Description = "Reach 1B NOK annual revenue",
                    RequiredRevenue = 1_000_000_000m,
                    RewardDescription = "Unlock IPO preparation",
                    RewardUnlockId = "ipo_preparation"
                },
                new EmpireMilestone
                {
                    Id = "ten_billion_rev", Name = "Major Corporation", Category = "Financial",
                    Description = "Reach 10B NOK annual revenue",
                    RequiredRevenue = 10_000_000_000m,
                    RewardDescription = "-3% financing costs",
                    RewardCostReduction = 0.03f
                },
                new EmpireMilestone
                {
                    Id = "ten_billion_val", Name = "Unicorn", Category = "Financial",
                    Description = "Achieve 10B NOK company valuation",
                    RequiredValuation = 10_000_000_000m,
                    RewardDescription = "Attract institutional investors"
                },

                // Strategic milestones
                new EmpireMilestone
                {
                    Id = "own_processing", Name = "Vertical Starter", Category = "Strategic",
                    Description = "Build or acquire a processing plant",
                    RequiresProcessing = true,
                    RewardDescription = "+10% margin on processed products",
                    RewardUnlockId = "value_add_products"
                },
                new EmpireMilestone
                {
                    Id = "own_hatchery", Name = "Smolt Master", Category = "Strategic",
                    Description = "Own a hatchery for smolt production",
                    RequiresHatchery = true,
                    RewardDescription = "Secure smolt supply, genetic control"
                },
                new EmpireMilestone
                {
                    Id = "go_public", Name = "Ring the Bell", Category = "Strategic",
                    Description = "Take the company public (IPO)",
                    RequiresIPO = true,
                    RewardDescription = "Unlock massive capital access",
                    RewardCashBonus = 500_000_000m
                },
                new EmpireMilestone
                {
                    Id = "go_international", Name = "Beyond the Fjords", Category = "Strategic",
                    Description = "Establish operations outside Norway",
                    RequiresInternational = true,
                    RewardDescription = "Counter-seasonal production, risk diversification"
                },
                new EmpireMilestone
                {
                    Id = "first_acquisition", Name = "Predator", Category = "Strategic",
                    Description = "Complete your first company acquisition",
                    RequiredAcquisitions = 1,
                    RewardDescription = "Unlock M&A strategy path"
                },

                // Victory milestones
                new EmpireMilestone
                {
                    Id = "beat_mowier", Name = "Dethrone the King", Category = "Victory",
                    Description = "Surpass Mowier ASA as Norway's largest salmon farmer",
                    RequiredMarketShare = 0.21f,
                    RewardDescription = "The crown is yours. #1 in Norway."
                },
                new EmpireMilestone
                {
                    Id = "green_giant", Name = "Green Giant", Category = "Victory",
                    Description = "Achieve 100% sustainability score while remaining profitable",
                    RequiredSustainabilityScore = 100,
                    RequiredRevenue = 1_000_000_000m,
                    RewardDescription = "The gold standard: profitable AND sustainable"
                },
                new EmpireMilestone
                {
                    Id = "monopolist", Name = "The Monopolist", Category = "Victory",
                    Description = "Control >50% of Norwegian salmon production",
                    RequiredMarketShare = 0.50f,
                    RewardDescription = "Total industry dominance. Regulators incoming."
                }
            });
        }

        private void InitializeVictoryConditions()
        {
            _victoryConditions.AddRange(new[]
            {
                new VictoryCondition
                {
                    Id = "victory_largest",
                    Name = "The Lakskongen",
                    Description = "Become the #1 largest salmon farmer in Norway (surpass Mowier ASA's 20% market share)",
                    Difficulty = "Hard",
                    ProgressCalculator = state => Math.Min(1f, state.MarketShareNorway / 0.21f)
                },
                new VictoryCondition
                {
                    Id = "victory_valuation",
                    Name = "Ten Billion Club",
                    Description = "Achieve a company valuation of 10B NOK",
                    Difficulty = "Medium",
                    ProgressCalculator = state => Math.Min(1f, (float)(state.Valuation / 10_000_000_000m))
                },
                new VictoryCondition
                {
                    Id = "victory_sustainable",
                    Name = "Green Gold",
                    Description = "Achieve 100% sustainability rating while maintaining profitability (EBITDA > 500M NOK)",
                    Difficulty = "Very Hard",
                    ProgressCalculator = state =>
                        Math.Min(1f, (state.SustainabilityScore / 100f) * 0.6f +
                        Math.Min(1f, (float)(state.AnnualEBITDA / 500_000_000m)) * 0.4f)
                },
                new VictoryCondition
                {
                    Id = "victory_monopoly",
                    Name = "Total Dominance",
                    Description = "Control >50% of Norwegian salmon production market share",
                    Difficulty = "Extreme",
                    ProgressCalculator = state => Math.Min(1f, state.MarketShareNorway / 0.50f)
                },
                new VictoryCondition
                {
                    Id = "victory_global",
                    Name = "Global Empire",
                    Description = "Operate in 4+ countries with 100B NOK+ revenue",
                    Difficulty = "Very Hard",
                    ProgressCalculator = state =>
                        Math.Min(1f, (state.InternationalRegions.Count / 4f) * 0.5f +
                        Math.Min(1f, (float)(state.AnnualRevenue / 100_000_000_000m)) * 0.5f)
                },
                new VictoryCondition
                {
                    Id = "victory_integrated",
                    Name = "Full Value Chain",
                    Description = "Own every step from genetics to retail (integration score = 100%)",
                    Difficulty = "Hard",
                    ProgressCalculator = state => state.IntegrationScore
                }
            });
        }
    }

    /// <summary>
    /// Empire summary for UI display.
    /// </summary>
    public struct EmpireSummary
    {
        public EmpirePhase Phase;
        public string PhaseName;
        public string PhaseDescription;
        public EmpirePhase NextPhase;
        public string NextPhaseRequirements;
        public GrowthStrategy GrowthStrategy;
        public ProductStrategy ProductStrategy;
        public int MilestonesUnlocked;
        public int MilestonesTotal;
        public int VictoriesAchieved;
        public int VictoriesTotal;
        public float PhaseProgress;
    }
}
