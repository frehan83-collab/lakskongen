using System;
using System.Collections.Generic;
using System.Linq;

using LakskongenSim.Core;
namespace LakskongenSim.Finance
{
    // =========================================================================
    // CompetitorAI.cs - AI Competitor Simulation for Commercial Gameplay (T79)
    // =========================================================================
    // Simulates rival salmon farming companies that compete with the player
    // for licenses, market share, acquisitions, and industry dominance.
    // Uses T75 parody names for real Norwegian salmon companies.
    // =========================================================================

    /// <summary>
    /// Strategic behavior archetype for AI competitors.
    /// </summary>
    public enum CompetitorStrategy
    {
        /// <summary>Cost leader: high volume, low margins, aggressive expansion (Mowier, SalMax)</summary>
        CostLeader,
        /// <summary>Premium specialist: quality focus, high margins, brand moat (Herøy)</summary>
        PremiumSpecialist,
        /// <summary>Aggressive consolidator: M&amp;A focused, debt-fueled growth (SalMax, Greed)</summary>
        Consolidator,
        /// <summary>Tech disruptor: offshore/land-based, hype-driven (Offshore Optimists)</summary>
        TechDisruptor,
        /// <summary>Conservative: steady growth, low debt, family-owned style</summary>
        Conservative
    }

    /// <summary>
    /// Market positioning for competitor pricing strategy.
    /// </summary>
    public enum MarketPositioning
    {
        /// <summary>Low cost, high volume</summary>
        VolumeLeader,
        /// <summary>High margin, quality focus</summary>
        PremiumSpecialist,
        /// <summary>Diversified across segments</summary>
        Diversified
    }

    /// <summary>
    /// Represents an AI-controlled competitor company in the salmon industry.
    /// </summary>
    public class CompetitorCompany
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParodyOf { get; set; } = string.Empty;  // Industry archetype this parodies (no real names, D17)
        public string Tagline { get; set; } = string.Empty;
        public string Headquarters { get; set; } = string.Empty;
        public CompetitorStrategy Strategy { get; set; }
        public MarketPositioning Positioning { get; set; }

        // Financial state
        public decimal Revenue { get; set; }           // Annual revenue NOK
        public decimal EBITDA { get; set; }             // Annual EBITDA NOK
        public decimal MarketCap { get; set; }          // Valuation NOK
        public decimal DebtLevel { get; set; }          // Total debt NOK
        public decimal CashReserves { get; set; }       // Available cash NOK
        public bool IsPubliclyListed { get; set; }

        // Operational state
        public int LicenseCount { get; set; }
        public float TotalMABTonnes { get; set; }       // Maximum Allowed Biomass
        public int SiteCount { get; set; }
        public float MarketShareNorway { get; set; }    // 0-1
        public float MarketShareGlobal { get; set; }    // 0-1
        public float WeeklyHarvestTonnes { get; set; }
        public float ProductionCostPerKg { get; set; }  // NOK/kg

        // Strategic state
        public float ExpansionAppetite { get; set; }    // 0-1 (how aggressively they expand)
        public float AcquisitionAggression { get; set; }// 0-1 (how likely to bid on targets)
        public float PriceWarWillingness { get; set; }  // 0-1 (willingness to undercut)
        public float LobbyingPower { get; set; }        // 0-1 (political influence)
        public float BrandStrength { get; set; }        // 0-1 (premium pricing power)
        public float FinancialHealth { get; set; }      // 0-1 (overall financial robustness)

        // International presence
        public List<string> OperatingRegions { get; set; } = new();
        public bool HasInternationalOperations => OperatingRegions.Any(r => r != "Norway");

        // History tracking
        public List<string> RecentActions { get; set; } = new();
        public int QuartersSinceLastAcquisition { get; set; }
        public bool IsDistressed { get; set; }
        public bool IsAcquisitionTarget { get; set; }   // Can player acquire this company?
        public decimal AcquisitionPrice { get; set; }   // Price to buy entire company

        /// <summary>Independent copy (lists included) for save snapshots.</summary>
        public CompetitorCompany Clone()
        {
            var copy = (CompetitorCompany)MemberwiseClone();
            copy.OperatingRegions = new List<string>(OperatingRegions);
            copy.RecentActions = new List<string>(RecentActions);
            return copy;
        }

        /// <summary>
        /// Debt-to-EBITDA ratio. Above 4x is considered risky in salmon industry.
        /// </summary>
        public decimal LeverageRatio => EBITDA > 0 ? DebtLevel / EBITDA : decimal.MaxValue;

        /// <summary>
        /// EBITDA margin percentage.
        /// </summary>
        public decimal EBITDAMargin => Revenue > 0 ? EBITDA / Revenue : 0;

        /// <summary>
        /// Enterprise value (market cap + debt - cash).
        /// </summary>
        public decimal EnterpriseValue => MarketCap + DebtLevel - CashReserves;
    }

    /// <summary>
    /// Result of a competitor action that affects the market or player.
    /// </summary>
    public class CompetitorAction
    {
        public string CompetitorId { get; set; } = string.Empty;
        public string CompetitorName { get; set; } = string.Empty;
        public CompetitorActionType ActionType { get; set; }
        public string Description { get; set; } = string.Empty;
        public string NewsHeadline { get; set; } = string.Empty;
        public float MarketImpact { get; set; }         // -1 to 1 (negative = bad for player)
        public float SupplyImpact { get; set; }         // Multiplier on global supply
        public float PriceImpact { get; set; }          // Direct NOK/kg impact
        public string TargetId { get; set; } = string.Empty;  // ID of acquisition target if applicable
    }

    public enum CompetitorActionType
    {
        /// <summary>Competitor increases harvest volumes</summary>
        HarvestIncrease,
        /// <summary>Competitor acquires another company</summary>
        Acquisition,
        /// <summary>Competitor bids on a license the player wanted</summary>
        LicenseBid,
        /// <summary>Competitor starts a price war</summary>
        PriceWar,
        /// <summary>Competitor lobbies for favorable regulations</summary>
        Lobbying,
        /// <summary>Competitor goes public (IPO)</summary>
        IPO,
        /// <summary>Competitor issues bonds/debt for expansion</summary>
        DebtExpansion,
        /// <summary>Competitor enters crisis (disease, scandal)</summary>
        Crisis,
        /// <summary>Competitor enters new international market</summary>
        InternationalExpansion,
        /// <summary>Competitor invests in vertical integration</summary>
        VerticalIntegration,
        /// <summary>Competitor undercuts contracts</summary>
        ContractUndercut
    }

    /// <summary>
    /// Quarterly industry report summarizing all competitor activity.
    /// </summary>
    public class IndustryReport
    {
        public int Quarter { get; set; }
        public int Year { get; set; }
        public decimal IndustryTotalRevenue { get; set; }
        public float TotalNorwegianHarvestTonnes { get; set; }
        public float AverageSpotPrice { get; set; }
        public List<CompetitorAction> QuarterActions { get; set; } = new();
        public string MarketOutlook { get; set; } = string.Empty;  // "Bullish", "Bearish", "Neutral"
        public float PlayerMarketShare { get; set; }
        public string PlayerRanking { get; set; } = string.Empty;  // "#5 of 8"
        public List<(string CompetitorName, float MarketShare)> MarketShareRanking { get; set; } = new();
    }

    /// <summary>
    /// Simulates AI competitor behavior in the salmon farming industry.
    /// Generates realistic competitive dynamics: M&amp;A, price wars, harvest volumes,
    /// license bidding, and industry consolidation.
    /// </summary>
    public class CompetitorAISystem
    {
        private readonly List<CompetitorCompany> _competitors = new();
        private readonly SimRngStream _rng;
        internal SimRngStream RngForSnapshot => _rng;
        private readonly List<CompetitorAction> _pendingActions = new();
        private readonly List<CompetitorAction> _actionHistory = new();
        private int _currentQuarter;
        private int _currentYear;

        // Industry constants (Norwegian salmon)
        private const float TOTAL_NORWEGIAN_MAB = 1_250_000f;  // tonnes
        private const float ANNUAL_PRODUCTION_GROWTH = 0.02f;  // 2% annual
        private const float CONSOLIDATION_THRESHOLD = 0.15f;   // 15% share triggers M&A interest
        private const float ANTITRUST_BLOCK_THRESHOLD = 0.35f; // 35% share blocks mergers
        private const float PRICE_WAR_TRIGGER_MARGIN = 0.10f;  // <10% margin triggers price war defense
        private const float DISTRESS_HEALTH_THRESHOLD = 0.30f; // Below 30% = distressed
        private const decimal LEVERAGE_DISTRESS_RATIO = 5.0m;  // >5x debt/EBITDA = trouble

        public IReadOnlyList<CompetitorCompany> Competitors => _competitors;
        public IReadOnlyList<CompetitorAction> PendingActions => _pendingActions;
        public IReadOnlyList<CompetitorAction> ActionHistory => _actionHistory;

        /// <summary>Action history entries kept in a save (UI history only; no logic reads it).</summary>
        public const int ActionHistoryInSave = 100;

        /// <summary>
        /// Full roster + quarter clock for a save (F1071). The old DTO kept 14
        /// financial fields, so acquired rivals came back on load and
        /// FinancialHealth, appetite, counters and quarter/year reset.
        /// </summary>
        public CompetitorAISnapshot ExportState() => new()
        {
            Competitors = _competitors.Select(c => c.Clone()).ToList(),
            CurrentQuarter = _currentQuarter,
            CurrentYear = _currentYear,
            PendingActions = _pendingActions.ToList(),
            ActionHistory = _actionHistory.Skip(Math.Max(0, _actionHistory.Count - ActionHistoryInSave)).ToList()
        };

        /// <summary>Replace the roster and clock with a saved snapshot (no events fired).</summary>
        public void ImportState(CompetitorAISnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _competitors.Clear();
            _competitors.AddRange((snapshot.Competitors ?? new List<CompetitorCompany>()).Select(c => c.Clone()));
            _currentQuarter = snapshot.CurrentQuarter;
            _currentYear = snapshot.CurrentYear;
            _pendingActions.Clear();
            _pendingActions.AddRange(snapshot.PendingActions ?? new List<CompetitorAction>());
            _actionHistory.Clear();
            _actionHistory.AddRange(snapshot.ActionHistory ?? new List<CompetitorAction>());
        }

        public event Action<CompetitorAction>? OnCompetitorAction;
        public event Action<IndustryReport>? OnQuarterlyReport;
        public event Action<string, string>? OnCompetitorAcquired;  // acquirerId, targetId
        /// <summary>Fired when a competitor first enters distress. Provides the competitor and current simulation day.</summary>
        public event Action<CompetitorCompany, int>? OnCompetitorDistressed;

        public CompetitorAISystem(SimRngStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            InitializeCompetitors();
        }

        public CompetitorAISystem(int? seed = null)
            : this((seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded()).Stream(SimRngStreams.CompetitorAI))
        {
        }

        /// <summary>
        /// Initialize the T75 parody competitors with realistic starting stats.
        /// </summary>
        private void InitializeCompetitors()
        {
            _competitors.AddRange(new[]
            {
                new CompetitorCompany
                {
                    Id = "mowier",
                    Name = "Mowier ASA",
                    ParodyOf = "the global market leader",
                    Tagline = "Leading the Blue Revolution™ (to the bank)",
                    Headquarters = "Bergen, Vestland",
                    Strategy = CompetitorStrategy.CostLeader,
                    Positioning = MarketPositioning.VolumeLeader,
                    Revenue = 52_000_000_000m,      // ~52B NOK
                    EBITDA = 12_000_000_000m,        // ~23% margin
                    MarketCap = 120_000_000_000m,    // ~120B NOK
                    DebtLevel = 25_000_000_000m,
                    CashReserves = 3_000_000_000m,
                    IsPubliclyListed = true,
                    LicenseCount = 180,
                    TotalMABTonnes = 250_000f,
                    SiteCount = 55,
                    MarketShareNorway = 0.20f,
                    MarketShareGlobal = 0.08f,
                    WeeklyHarvestTonnes = 4800f,
                    ProductionCostPerKg = 48f,
                    ExpansionAppetite = 0.85f,
                    AcquisitionAggression = 0.90f,
                    PriceWarWillingness = 0.70f,
                    LobbyingPower = 0.95f,
                    BrandStrength = 0.75f,
                    FinancialHealth = 0.88f,
                    OperatingRegions = new() { "Norway", "Scotland", "Chile", "Canada", "Ireland" },
                    IsAcquisitionTarget = false
                },
                new CompetitorCompany
                {
                    Id = "salmax",
                    Name = "SalMax Group ASA",
                    ParodyOf = "the innovative number two",
                    Tagline = "Passion for Profit",
                    Headquarters = "Frøya, Trøndelag",
                    Strategy = CompetitorStrategy.Consolidator,
                    Positioning = MarketPositioning.VolumeLeader,
                    Revenue = 28_000_000_000m,
                    EBITDA = 8_400_000_000m,         // ~30% margin
                    MarketCap = 95_000_000_000m,
                    DebtLevel = 15_000_000_000m,
                    CashReserves = 5_000_000_000m,
                    IsPubliclyListed = true,
                    LicenseCount = 110,
                    TotalMABTonnes = 170_000f,
                    SiteCount = 38,
                    MarketShareNorway = 0.14f,
                    MarketShareGlobal = 0.05f,
                    WeeklyHarvestTonnes = 3300f,
                    ProductionCostPerKg = 42f,
                    ExpansionAppetite = 0.90f,
                    AcquisitionAggression = 0.95f,
                    PriceWarWillingness = 0.60f,
                    LobbyingPower = 0.80f,
                    BrandStrength = 0.65f,
                    FinancialHealth = 0.92f,
                    OperatingRegions = new() { "Norway", "Iceland" },
                    IsAcquisitionTarget = false
                },
                new CompetitorCompany
                {
                    Id = "heroy",
                    Name = "Herøy Seafood AS",
                    ParodyOf = "the integrated seafood group",
                    Tagline = "Lords of the Fjords",
                    Headquarters = "Bergen, Vestland",
                    Strategy = CompetitorStrategy.PremiumSpecialist,
                    Positioning = MarketPositioning.PremiumSpecialist,
                    Revenue = 30_000_000_000m,
                    EBITDA = 6_000_000_000m,
                    MarketCap = 65_000_000_000m,
                    DebtLevel = 10_000_000_000m,
                    CashReserves = 2_500_000_000m,
                    IsPubliclyListed = true,
                    LicenseCount = 95,
                    TotalMABTonnes = 130_000f,
                    SiteCount = 32,
                    MarketShareNorway = 0.10f,
                    MarketShareGlobal = 0.04f,
                    WeeklyHarvestTonnes = 2500f,
                    ProductionCostPerKg = 55f,
                    ExpansionAppetite = 0.50f,
                    AcquisitionAggression = 0.45f,
                    PriceWarWillingness = 0.20f,
                    LobbyingPower = 0.70f,
                    BrandStrength = 0.92f,
                    FinancialHealth = 0.82f,
                    OperatingRegions = new() { "Norway" },
                    IsAcquisitionTarget = false
                },
                new CompetitorCompany
                {
                    Id = "greed_gregarious",
                    Name = "Greed Gregarious ASA",
                    ParodyOf = "the listed west-coast farmer",
                    Tagline = "Growth at Any Cost",
                    Headquarters = "Bergen, Vestland",
                    Strategy = CompetitorStrategy.Consolidator,
                    Positioning = MarketPositioning.Diversified,
                    Revenue = 14_000_000_000m,
                    EBITDA = 2_800_000_000m,
                    MarketCap = 22_000_000_000m,
                    DebtLevel = 8_000_000_000m,
                    CashReserves = 1_200_000_000m,
                    IsPubliclyListed = true,
                    LicenseCount = 60,
                    TotalMABTonnes = 80_000f,
                    SiteCount = 22,
                    MarketShareNorway = 0.06f,
                    MarketShareGlobal = 0.03f,
                    WeeklyHarvestTonnes = 1500f,
                    ProductionCostPerKg = 52f,
                    ExpansionAppetite = 0.75f,
                    AcquisitionAggression = 0.80f,
                    PriceWarWillingness = 0.55f,
                    LobbyingPower = 0.60f,
                    BrandStrength = 0.50f,
                    FinancialHealth = 0.65f,
                    OperatingRegions = new() { "Norway", "Canada", "Scotland" },
                    IsAcquisitionTarget = true,
                    AcquisitionPrice = 28_000_000_000m
                },
                new CompetitorCompany
                {
                    Id = "offshore_optimists",
                    Name = "Offshore Optimists AS",
                    ParodyOf = "offshore farming start-ups",
                    Tagline = "The Future is Offshore (we promise, this time it's different)",
                    Headquarters = "Trondheim, Trøndelag",
                    Strategy = CompetitorStrategy.TechDisruptor,
                    Positioning = MarketPositioning.PremiumSpecialist,
                    Revenue = 2_000_000_000m,
                    EBITDA = -500_000_000m,          // Still unprofitable
                    MarketCap = 15_000_000_000m,     // Hype valuation
                    DebtLevel = 6_000_000_000m,
                    CashReserves = 3_000_000_000m,
                    IsPubliclyListed = true,
                    LicenseCount = 8,
                    TotalMABTonnes = 15_000f,
                    SiteCount = 3,
                    MarketShareNorway = 0.01f,
                    MarketShareGlobal = 0.005f,
                    WeeklyHarvestTonnes = 280f,
                    ProductionCostPerKg = 85f,
                    ExpansionAppetite = 0.95f,
                    AcquisitionAggression = 0.30f,
                    PriceWarWillingness = 0.10f,
                    LobbyingPower = 0.40f,
                    BrandStrength = 0.60f,
                    FinancialHealth = 0.45f,
                    OperatingRegions = new() { "Norway" },
                    IsAcquisitionTarget = true,
                    AcquisitionPrice = 18_000_000_000m
                },
                new CompetitorCompany
                {
                    Id = "nordlaks_nord",
                    Name = "Kongsemne Havbruk AS",
                    ParodyOf = "the northern regional farmer",
                    Tagline = "Arctic Excellence, Royal Ambitions",
                    Headquarters = "Stokmarknes, Nordland",
                    Strategy = CompetitorStrategy.Conservative,
                    Positioning = MarketPositioning.Diversified,
                    Revenue = 8_000_000_000m,
                    EBITDA = 2_000_000_000m,
                    MarketCap = 18_000_000_000m,
                    DebtLevel = 3_500_000_000m,
                    CashReserves = 1_800_000_000m,
                    IsPubliclyListed = false,
                    LicenseCount = 40,
                    TotalMABTonnes = 55_000f,
                    SiteCount = 15,
                    MarketShareNorway = 0.04f,
                    MarketShareGlobal = 0.015f,
                    WeeklyHarvestTonnes = 1050f,
                    ProductionCostPerKg = 50f,
                    ExpansionAppetite = 0.35f,
                    AcquisitionAggression = 0.25f,
                    PriceWarWillingness = 0.30f,
                    LobbyingPower = 0.45f,
                    BrandStrength = 0.70f,
                    FinancialHealth = 0.85f,
                    OperatingRegions = new() { "Norway" },
                    IsAcquisitionTarget = true,
                    AcquisitionPrice = 22_000_000_000m
                }
            });
        }

        /// <summary>
        /// Advance competitor simulation by one quarter. Returns actions taken.
        /// </summary>
        public List<CompetitorAction> AdvanceQuarter(
            float playerMarketShare,
            decimal playerRevenue,
            float spotPriceNOK,
            int playerLicenseCount)
        {
            _currentQuarter = (_currentQuarter % 4) + 1;
            if (_currentQuarter == 1) _currentYear++;

            _pendingActions.Clear();
            var quarterActions = new List<CompetitorAction>();

            foreach (var competitor in _competitors.Where(c => !c.IsDistressed || _rng.NextDouble() < 0.3))
            {
                // Update competitor financials based on market conditions
                UpdateCompetitorFinancials(competitor, spotPriceNOK);

                // Check for distress
                CheckForDistress(competitor, _currentYear * 365 + _currentQuarter * 90);

                // Generate strategic actions based on competitor personality
                var actions = GenerateQuarterActions(competitor, playerMarketShare, spotPriceNOK, playerLicenseCount);
                quarterActions.AddRange(actions);
            }

            // AI-vs-AI interactions (competitors acquiring each other)
            var aiAcquisitions = SimulateAIAcquisitions();
            quarterActions.AddRange(aiAcquisitions);

            _actionHistory.AddRange(quarterActions);

            // Generate quarterly report
            var report = GenerateQuarterlyReport(playerMarketShare, playerRevenue, spotPriceNOK, quarterActions);
            OnQuarterlyReport?.Invoke(report);

            foreach (var action in quarterActions)
            {
                OnCompetitorAction?.Invoke(action);
            }

            return quarterActions;
        }

        private void UpdateCompetitorFinancials(CompetitorCompany c, float spotPrice)
        {
            // Revenue = harvest * price * positioning premium
            float pricePremium = c.Positioning switch
            {
                MarketPositioning.PremiumSpecialist => 1.15f,
                MarketPositioning.VolumeLeader => 0.95f,
                _ => 1.0f
            };
            decimal weeklyRevenue = (decimal)(c.WeeklyHarvestTonnes * 1000 * spotPrice * pricePremium);
            c.Revenue = weeklyRevenue * 52;

            // EBITDA based on margin and cost structure
            decimal costPerKg = (decimal)c.ProductionCostPerKg;
            decimal annualCost = (decimal)(c.WeeklyHarvestTonnes * 1000 * 52) * costPerKg;
            c.EBITDA = c.Revenue - annualCost;

            // Update financial health
            if (c.EBITDA > 0)
            {
                c.FinancialHealth = Math.Min(1.0f, (float)(c.EBITDA / c.Revenue) * 2.5f);
                if (c.LeverageRatio > 4) c.FinancialHealth *= 0.7f;
            }
            else
            {
                c.FinancialHealth = Math.Max(0.05f, c.FinancialHealth - 0.1f);
            }

            // Slow natural growth
            c.WeeklyHarvestTonnes *= (1f + ANNUAL_PRODUCTION_GROWTH / 4f);

            // Cash flow
            decimal quarterCashFlow = c.EBITDA / 4 - (c.DebtLevel * 0.05m / 4); // EBITDA minus interest
            c.CashReserves = Math.Max(0, c.CashReserves + quarterCashFlow);

            // Update market cap based on EBITDA multiple
            decimal multiple = c.Strategy switch
            {
                CompetitorStrategy.TechDisruptor => 30m,  // High multiple for hype
                CompetitorStrategy.CostLeader => 10m,
                CompetitorStrategy.PremiumSpecialist => 12m,
                _ => 8m
            };
            if (c.EBITDA > 0) c.MarketCap = c.EBITDA * multiple;

            c.QuartersSinceLastAcquisition++;
        }

        private void CheckForDistress(CompetitorCompany c, int currentDay)
        {
            bool wasDistressed = c.IsDistressed;
            c.IsDistressed = c.FinancialHealth < DISTRESS_HEALTH_THRESHOLD
                          || c.LeverageRatio > LEVERAGE_DISTRESS_RATIO
                          || c.CashReserves <= 0;

            if (c.IsDistressed && !wasDistressed)
            {
                c.AcquisitionPrice = c.EnterpriseValue * 0.6m;  // 40% distress discount
                c.IsAcquisitionTarget = true;

                var action = new CompetitorAction
                {
                    CompetitorId = c.Id,
                    CompetitorName = c.Name,
                    ActionType = CompetitorActionType.Crisis,
                    Description = $"{c.Name} enters financial distress. Leverage ratio: {c.LeverageRatio:F1}x",
                    NewsHeadline = $"📉 {c.Name} in trouble — creditors circling, assets for sale",
                    MarketImpact = 0.05f,  // Slightly positive for remaining players
                    SupplyImpact = 0.98f   // Slight supply reduction
                };
                _pendingActions.Add(action);

                OnCompetitorDistressed?.Invoke(c, currentDay);
            }
        }

        private List<CompetitorAction> GenerateQuarterActions(
            CompetitorCompany c,
            float playerMarketShare,
            float spotPrice,
            int playerLicenses)
        {
            var actions = new List<CompetitorAction>();

            // Harvest volume decisions
            if (_rng.NextDouble() < 0.4)
            {
                actions.Add(GenerateHarvestAction(c, spotPrice));
            }

            // Acquisition attempts (more likely for consolidators)
            if (_rng.NextDouble() < c.AcquisitionAggression * 0.3 && c.QuartersSinceLastAcquisition > 2)
            {
                var acqAction = GenerateAcquisitionAction(c);
                if (acqAction != null) actions.Add(acqAction);
            }

            // License bidding (compete with player)
            if (_rng.NextDouble() < c.ExpansionAppetite * 0.2)
            {
                actions.Add(GenerateLicenseBidAction(c, playerLicenses));
            }

            // Price war initiation
            if (c.PriceWarWillingness > 0.5f && playerMarketShare > c.MarketShareNorway * 0.8f)
            {
                if (_rng.NextDouble() < c.PriceWarWillingness * 0.15)
                {
                    actions.Add(GeneratePriceWarAction(c, spotPrice));
                }
            }

            // Lobbying actions
            if (c.LobbyingPower > 0.6f && _rng.NextDouble() < c.LobbyingPower * 0.1)
            {
                actions.Add(GenerateLobbyingAction(c));
            }

            // International expansion
            if (c.ExpansionAppetite > 0.7f && !c.HasInternationalOperations && _rng.NextDouble() < 0.05)
            {
                actions.Add(GenerateInternationalAction(c));
            }

            c.RecentActions = actions.Select(a => a.Description).ToList();
            return actions;
        }

        private CompetitorAction GenerateHarvestAction(CompetitorCompany c, float spotPrice)
        {
            // High prices → increase harvest, Low prices → strategic holdback
            float priceRatio = spotPrice / 85f;  // Relative to long-term mean
            float adjustment = c.Strategy switch
            {
                CompetitorStrategy.CostLeader => priceRatio > 1.1f ? 1.08f : 1.02f,
                CompetitorStrategy.PremiumSpecialist => priceRatio > 1.2f ? 1.05f : 0.98f,
                CompetitorStrategy.Consolidator => 1.05f,
                _ => 1.0f + (_rng.NextSingle() - 0.5f) * 0.06f
            };

            c.WeeklyHarvestTonnes *= adjustment;

            return new CompetitorAction
            {
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                ActionType = CompetitorActionType.HarvestIncrease,
                Description = $"{c.Name} adjusts harvest to {c.WeeklyHarvestTonnes:F0}t/week ({adjustment:P0} change)",
                NewsHeadline = adjustment > 1.03f
                    ? $"📰 {c.Name} ramps up production — flooding the market"
                    : $"📰 {c.Name} holds steady on harvest volumes",
                SupplyImpact = adjustment,
                PriceImpact = (adjustment - 1f) * -5f  // More supply → lower price
            };
        }

        private CompetitorAction? GenerateAcquisitionAction(CompetitorCompany acquirer)
        {
            // Find a suitable target among other competitors
            var targets = _competitors
                .Where(t => t.Id != acquirer.Id
                    && t.IsAcquisitionTarget
                    && t.AcquisitionPrice < acquirer.CashReserves + acquirer.MarketCap * 0.1m)
                .ToList();

            if (targets.Count == 0) return null;

            var target = targets[_rng.Next(targets.Count)];

            // Check antitrust
            float combinedShare = acquirer.MarketShareNorway + target.MarketShareNorway;
            if (combinedShare > ANTITRUST_BLOCK_THRESHOLD)
            {
                return new CompetitorAction
                {
                    CompetitorId = acquirer.Id,
                    CompetitorName = acquirer.Name,
                    ActionType = CompetitorActionType.Acquisition,
                    Description = $"{acquirer.Name} bid for {target.Name} blocked by antitrust regulators",
                    NewsHeadline = $"🚫 Konkurransetilsynet blocks {acquirer.Name}'s takeover of {target.Name}",
                    MarketImpact = 0f,
                    TargetId = target.Id
                };
            }

            acquirer.QuartersSinceLastAcquisition = 0;

            return new CompetitorAction
            {
                CompetitorId = acquirer.Id,
                CompetitorName = acquirer.Name,
                ActionType = CompetitorActionType.Acquisition,
                Description = $"{acquirer.Name} acquires {target.Name} for {target.AcquisitionPrice / 1_000_000_000:F1}B NOK",
                NewsHeadline = $"💰 {acquirer.Name} swallows {target.Name} in {target.AcquisitionPrice / 1_000_000_000:F1}B NOK deal",
                MarketImpact = -0.05f,
                SupplyImpact = 1.0f,
                TargetId = target.Id
            };
        }

        private CompetitorAction GenerateLicenseBidAction(CompetitorCompany c, int playerLicenses)
        {
            int licensesAcquired = _rng.Next(1, 4);
            c.LicenseCount += licensesAcquired;
            c.TotalMABTonnes += licensesAcquired * 780f;

            return new CompetitorAction
            {
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                ActionType = CompetitorActionType.LicenseBid,
                Description = $"{c.Name} wins {licensesAcquired} new license(s). Total: {c.LicenseCount}",
                NewsHeadline = $"📋 {c.Name} secures {licensesAcquired} new aquaculture license(s)",
                MarketImpact = -0.02f * licensesAcquired
            };
        }

        private CompetitorAction GeneratePriceWarAction(CompetitorCompany c, float spotPrice)
        {
            float priceCut = spotPrice * (0.05f + _rng.NextSingle() * 0.10f);

            return new CompetitorAction
            {
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                ActionType = CompetitorActionType.PriceWar,
                Description = $"{c.Name} slashes contract prices by {priceCut:F0} NOK/kg to grab market share",
                NewsHeadline = $"⚔️ PRICE WAR: {c.Name} undercuts market by {priceCut:F0} NOK/kg",
                PriceImpact = -priceCut,
                MarketImpact = -0.10f
            };
        }

        private CompetitorAction GenerateLobbyingAction(CompetitorCompany c)
        {
            string[] lobbyTopics =
            {
                "looser sea lice limits",
                "reduced resource rent tax",
                "faster license approvals",
                "relaxed environmental monitoring requirements",
                "increased MAB allocation per license",
                "delayed implementation of traffic light system changes"
            };
            string topic = lobbyTopics[_rng.Next(lobbyTopics.Length)];

            return new CompetitorAction
            {
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                ActionType = CompetitorActionType.Lobbying,
                Description = $"{c.Name} lobbies Stortinget for {topic}",
                NewsHeadline = $"🏛️ {c.Name} intensifies lobbying for {topic}",
                MarketImpact = -0.03f
            };
        }

        private CompetitorAction GenerateInternationalAction(CompetitorCompany c)
        {
            string[] regions = { "Scotland", "Chile", "Canada", "Tasmania" };
            string region = regions[_rng.Next(regions.Length)];
            c.OperatingRegions.Add(region);

            return new CompetitorAction
            {
                CompetitorId = c.Id,
                CompetitorName = c.Name,
                ActionType = CompetitorActionType.InternationalExpansion,
                Description = $"{c.Name} enters {region} market with new operations",
                NewsHeadline = $"🌍 {c.Name} expands to {region} — Norwegian competition goes global",
                MarketImpact = -0.02f
            };
        }

        private List<CompetitorAction> SimulateAIAcquisitions()
        {
            var actions = new List<CompetitorAction>();

            // 5% chance per quarter of a major AI-vs-AI acquisition event
            if (_rng.NextDouble() < 0.05)
            {
                var acquirers = _competitors
                    .Where(c => c.AcquisitionAggression > 0.6f && c.FinancialHealth > 0.5f)
                    .OrderByDescending(c => c.CashReserves)
                    .Take(3)
                    .ToList();

                var targets = _competitors
                    .Where(c => c.IsDistressed || (c.IsAcquisitionTarget && c.FinancialHealth < 0.6f))
                    .ToList();

                if (acquirers.Count > 0 && targets.Count > 0)
                {
                    var acquirer = acquirers[_rng.Next(acquirers.Count)];
                    var target = targets.FirstOrDefault(t => t.Id != acquirer.Id);

                    if (target != null)
                    {
                        // Execute acquisition: merge target into acquirer
                        acquirer.LicenseCount += target.LicenseCount;
                        acquirer.TotalMABTonnes += target.TotalMABTonnes;
                        acquirer.SiteCount += target.SiteCount;
                        acquirer.MarketShareNorway += target.MarketShareNorway;
                        acquirer.WeeklyHarvestTonnes += target.WeeklyHarvestTonnes;

                        actions.Add(new CompetitorAction
                        {
                            CompetitorId = acquirer.Id,
                            CompetitorName = acquirer.Name,
                            ActionType = CompetitorActionType.Acquisition,
                            Description = $"MAJOR: {acquirer.Name} acquires {target.Name}, creating industry giant with {acquirer.MarketShareNorway:P0} Norwegian market share",
                            NewsHeadline = $"🏢 MEGA-DEAL: {acquirer.Name} takes over {target.Name} — industry consolidation accelerates",
                            MarketImpact = -0.08f,
                            TargetId = target.Id
                        });

                        OnCompetitorAcquired?.Invoke(acquirer.Id, target.Id);

                        // Remove target from active competitors
                        _competitors.Remove(target);
                    }
                }
            }

            return actions;
        }

        private IndustryReport GenerateQuarterlyReport(
            float playerMarketShare,
            decimal playerRevenue,
            float spotPrice,
            List<CompetitorAction> actions)
        {
            var totalHarvest = _competitors.Sum(c => c.WeeklyHarvestTonnes) * 13; // 13 weeks per quarter
            var totalRevenue = _competitors.Sum(c => c.Revenue / 4);

            var ranking = _competitors
                .Select(c => (c.Name, c.MarketShareNorway))
                .Append(("Player", playerMarketShare))
                .OrderByDescending(x => x.Item2)
                .ToList();

            int playerRank = ranking.FindIndex(x => x.Item1 == "Player") + 1;

            string outlook;
            float avgHealth = _competitors.Average(c => c.FinancialHealth);
            if (avgHealth > 0.75f && spotPrice > 90f) outlook = "Bullish";
            else if (avgHealth < 0.5f || spotPrice < 60f) outlook = "Bearish";
            else outlook = "Neutral";

            return new IndustryReport
            {
                Quarter = _currentQuarter,
                Year = _currentYear,
                IndustryTotalRevenue = totalRevenue,
                TotalNorwegianHarvestTonnes = totalHarvest,
                AverageSpotPrice = spotPrice,
                QuarterActions = actions,
                MarketOutlook = outlook,
                PlayerMarketShare = playerMarketShare,
                PlayerRanking = $"#{playerRank} of {ranking.Count}",
                MarketShareRanking = ranking
            };
        }

        // ─── Player interaction methods ─────────────────────────

        /// <summary>
        /// Get competitors available for player acquisition.
        /// </summary>
        public List<CompetitorCompany> GetAcquirableCompetitors()
        {
            return _competitors.Where(c => c.IsAcquisitionTarget).ToList();
        }

        /// <summary>
        /// Player attempts to acquire a competitor company.
        /// </summary>
        public (bool Success, string Message, decimal ActualPrice) PlayerAcquireCompetitor(
            string competitorId, decimal offerPrice, decimal playerCash)
        {
            var target = _competitors.FirstOrDefault(c => c.Id == competitorId);
            if (target == null) return (false, "Company not found", 0);
            if (!target.IsAcquisitionTarget) return (false, $"{target.Name} is not available for acquisition", 0);
            if (offerPrice > playerCash) return (false, "Insufficient funds", 0);

            decimal minAccept = target.IsDistressed
                ? target.AcquisitionPrice * 0.75m
                : target.AcquisitionPrice * 0.90m;

            if (offerPrice < minAccept)
            {
                return (false, $"{target.Name} rejects offer. Minimum acceptable: {minAccept / 1_000_000_000:F2}B NOK", 0);
            }

            // Check if another competitor outbids (30% chance)
            if (_rng.NextDouble() < 0.30)
            {
                var outbidder = _competitors
                    .Where(c => c.Id != competitorId && c.AcquisitionAggression > 0.7f && c.CashReserves > offerPrice)
                    .FirstOrDefault();

                if (outbidder != null)
                {
                    return (false,
                        $"OUTBID: {outbidder.Name} swoops in with a higher offer for {target.Name}!",
                        0);
                }
            }

            // Successful acquisition
            _competitors.Remove(target);
            return (true,
                $"Congratulations! You have acquired {target.Name} ({target.LicenseCount} licenses, {target.TotalMABTonnes:N0}T MAB)",
                offerPrice);
        }

        /// <summary>
        /// Get the total Norwegian market share of all AI competitors combined.
        /// </summary>
        public float GetTotalAIMarketShare()
        {
            return _competitors.Sum(c => c.MarketShareNorway);
        }

        /// <summary>
        /// Get a specific competitor by ID.
        /// </summary>
        public CompetitorCompany? GetCompetitor(string id)
        {
            return _competitors.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Get market share ranking including player.
        /// </summary>
        public List<(string Name, float Share, bool IsPlayer)> GetMarketShareRanking(float playerShare)
        {
            var ranking = _competitors
                .Select(c => (c.Name, c.MarketShareNorway, false))
                .Append(("Player", playerShare, true))
                .OrderByDescending(x => x.Item2)
                .ToList();
            return ranking;
        }
    }

    /// <summary>Serializable competitor AI state (GameState.CompetitorAI).</summary>
    public sealed class CompetitorAISnapshot
    {
        public List<CompetitorCompany> Competitors { get; set; } = new();
        public int CurrentQuarter { get; set; }
        public int CurrentYear { get; set; }
        public List<CompetitorAction> PendingActions { get; set; } = new();
        public List<CompetitorAction> ActionHistory { get; set; } = new();
    }
}
