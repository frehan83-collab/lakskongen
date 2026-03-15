using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    // =========================================================================
    // InvestmentManager.cs - ROI Calculator & Capital Allocation (T79)
    // =========================================================================
    // Central hub for investment decisions: ROI analysis, NPV/IRR calculations,
    // financing options, and strategic capital allocation.
    // Connects FinanceManager, InvestorSystem, LoanSystem, and StockMarket.
    // =========================================================================

    /// <summary>
    /// Type of financing instrument available to the player.
    /// </summary>
    public enum FinancingType
    {
        /// <summary>Use existing cash reserves</summary>
        CashReserves,
        /// <summary>Bank loan (secured debt, fixed interest)</summary>
        BankLoan,
        /// <summary>Corporate bond issuance (unsecured debt, lower rate)</summary>
        CorporateBond,
        /// <summary>Equity sale (dilute ownership, no repayment)</summary>
        EquitySale,
        /// <summary>Secondary share offering (public companies only)</summary>
        SecondaryOffering,
        /// <summary>Government innovation grant (non-dilutive, restricted use)</summary>
        GovernmentGrant,
        /// <summary>Joint venture (shared cost and revenue)</summary>
        JointVenture
    }

    /// <summary>
    /// A financing option with terms and conditions.
    /// </summary>
    public class FinancingOption
    {
        public FinancingType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal MaxAmount { get; set; }
        public decimal InterestRate { get; set; }        // Annual rate (0 for equity)
        public int TermYears { get; set; }               // Maturity
        public float DilutionPercent { get; set; }       // Ownership dilution (0 for debt)
        public bool RequiresCollateral { get; set; }
        public bool RequiresPublicListing { get; set; }
        public decimal MinimumRevenue { get; set; }      // Revenue requirement
        public List<string> Covenants { get; set; } = new(); // Debt covenants
        public string RiskLevel { get; set; } = "Medium";
    }

    /// <summary>
    /// An investment opportunity with projected returns.
    /// </summary>
    public class InvestmentOpportunity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;  // "Organic", "Acquisition", "VerticalIntegration", "Diversification"
        public string Description { get; set; } = string.Empty;

        // Cost
        public decimal CapexRequired { get; set; }
        public decimal AnnualOpex { get; set; }

        // Projected returns
        public decimal ProjectedAnnualRevenue { get; set; }
        public decimal ProjectedAnnualEBITDA { get; set; }
        public float ProjectedROI { get; set; }              // Annual ROI %
        public float PaybackPeriodYears { get; set; }

        // Risk assessment
        public RiskLevel Risk { get; set; }
        public List<string> RiskFactors { get; set; } = new();
        public float SuccessProbability { get; set; }        // 0-1

        // Strategic value
        public float MarketShareImpact { get; set; }        // Additional market share gained
        public int LicensesGained { get; set; }
        public float MABTonnesGained { get; set; }
        public bool UnlocksNewCapability { get; set; }
        public string CapabilityUnlocked { get; set; } = string.Empty;

        // Timeline
        public int ImplementationMonths { get; set; }
        public bool Available { get; set; } = true;
    }

    public enum RiskLevel
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh
    }

    /// <summary>
    /// Result of NPV/IRR calculation for a specific investment.
    /// </summary>
    public class InvestmentAnalysis
    {
        public string InvestmentId { get; set; } = string.Empty;
        public string InvestmentName { get; set; } = string.Empty;
        public decimal NPV { get; set; }                     // Net Present Value
        public float IRR { get; set; }                        // Internal Rate of Return
        public float PaybackYears { get; set; }
        public float ROIC { get; set; }                       // Return on Invested Capital
        public decimal TotalCashOutflow { get; set; }
        public decimal TotalCashInflow { get; set; }
        public string Recommendation { get; set; } = string.Empty;  // "Strong Buy", "Buy", "Hold", "Avoid"
        public List<decimal> CashFlowProjection { get; set; } = new(); // Year-by-year
    }

    /// <summary>
    /// Central investment decision engine. Provides ROI analysis, financing options,
    /// and strategic capital allocation recommendations.
    /// </summary>
    public class InvestmentManager
    {
        private readonly FinanceManager _finance;
        private readonly InvestorSystem _investors;
        private readonly LoanSystem _loans;
        private readonly StockMarketSystem? _stockMarket;
        private readonly List<InvestmentOpportunity> _opportunities = new();
        private readonly List<FinancingOption> _financingOptions = new();
        private readonly List<InvestmentAnalysis> _completedAnalyses = new();

        // Industry benchmarks (Norwegian salmon farming)
        private const float INDUSTRY_AVG_ROIC = 0.18f;      // 18% typical ROIC
        private const float INDUSTRY_AVG_EBITDA_MARGIN = 0.25f;
        private const decimal WACC = 0.08m;                  // 8% weighted avg cost of capital
        private const decimal RISK_FREE_RATE = 0.04m;        // ~4% (Norwegian gov bond)
        private const int PROJECTION_YEARS = 10;

        public IReadOnlyList<InvestmentOpportunity> Opportunities => _opportunities;
        public IReadOnlyList<FinancingOption> FinancingOptions => _financingOptions;
        public IReadOnlyList<InvestmentAnalysis> CompletedAnalyses => _completedAnalyses;

        public event Action<InvestmentAnalysis>? OnAnalysisCompleted;
        public event Action<string, FinancingType, decimal>? OnInvestmentExecuted;

        public InvestmentManager(
            FinanceManager finance,
            InvestorSystem investors,
            LoanSystem loans,
            StockMarketSystem? stockMarket = null)
        {
            _finance = finance ?? throw new ArgumentNullException(nameof(finance));
            _investors = investors ?? throw new ArgumentNullException(nameof(investors));
            _loans = loans ?? throw new ArgumentNullException(nameof(loans));
            _stockMarket = stockMarket;

            InitializeFinancingOptions();
            InitializeInvestmentOpportunities();
        }

        /// <summary>
        /// Calculate NPV for a given set of cash flows.
        /// </summary>
        public decimal CalculateNPV(decimal initialInvestment, List<decimal> annualCashFlows, decimal discountRate)
        {
            decimal npv = -initialInvestment;
            for (int i = 0; i < annualCashFlows.Count; i++)
            {
                npv += annualCashFlows[i] / (decimal)Math.Pow(1 + (double)discountRate, i + 1);
            }
            return npv;
        }

        /// <summary>
        /// Calculate IRR using iterative Newton-Raphson method.
        /// </summary>
        public float CalculateIRR(decimal initialInvestment, List<decimal> annualCashFlows, int maxIterations = 100)
        {
            double guess = 0.10; // Start with 10% guess

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double npv = -(double)initialInvestment;
                double derivative = 0;

                for (int i = 0; i < annualCashFlows.Count; i++)
                {
                    double cf = (double)annualCashFlows[i];
                    double discountFactor = Math.Pow(1 + guess, i + 1);
                    npv += cf / discountFactor;
                    derivative -= (i + 1) * cf / Math.Pow(1 + guess, i + 2);
                }

                if (Math.Abs(npv) < 0.01) break;
                if (Math.Abs(derivative) < 1e-10) break;

                guess -= npv / derivative;
                guess = Math.Clamp(guess, -0.99, 10.0); // Reasonable bounds
            }

            return (float)guess;
        }

        /// <summary>
        /// Perform full investment analysis for an opportunity.
        /// </summary>
        public InvestmentAnalysis AnalyzeInvestment(InvestmentOpportunity opportunity)
        {
            // Project cash flows over 10 years
            var cashFlows = new List<decimal>();
            decimal annualBenefit = opportunity.ProjectedAnnualEBITDA - opportunity.AnnualOpex;

            for (int year = 0; year < PROJECTION_YEARS; year++)
            {
                // Ramp-up: year 1 = 30%, year 2 = 70%, year 3+ = 100%
                decimal rampUp = year switch
                {
                    0 => 0.3m,
                    1 => 0.7m,
                    _ => 1.0m
                };

                // Growth rate (2% real + inflation)
                decimal growth = (decimal)Math.Pow(1.02, year);

                cashFlows.Add(annualBenefit * rampUp * growth);
            }

            decimal npv = CalculateNPV(opportunity.CapexRequired, cashFlows, WACC);
            float irr = CalculateIRR(opportunity.CapexRequired, cashFlows);

            // Payback period
            decimal cumulative = 0;
            float payback = PROJECTION_YEARS;
            for (int i = 0; i < cashFlows.Count; i++)
            {
                cumulative += cashFlows[i];
                if (cumulative >= opportunity.CapexRequired)
                {
                    payback = i + 1 - (float)((cumulative - opportunity.CapexRequired) / cashFlows[i]);
                    break;
                }
            }

            // ROIC
            float roic = opportunity.CapexRequired > 0
                ? (float)(annualBenefit / opportunity.CapexRequired)
                : 0;

            // Recommendation
            string recommendation;
            if (npv > 0 && irr > (float)WACC * 1.5f && roic > INDUSTRY_AVG_ROIC)
                recommendation = "Strong Buy";
            else if (npv > 0 && irr > (float)WACC)
                recommendation = "Buy";
            else if (npv > -opportunity.CapexRequired * 0.1m)
                recommendation = "Hold";
            else
                recommendation = "Avoid";

            // Adjust recommendation for risk
            if (opportunity.Risk >= RiskLevel.High && recommendation == "Strong Buy")
                recommendation = "Buy (High Risk)";

            var analysis = new InvestmentAnalysis
            {
                InvestmentId = opportunity.Id,
                InvestmentName = opportunity.Name,
                NPV = npv,
                IRR = irr,
                PaybackYears = payback,
                ROIC = roic,
                TotalCashOutflow = opportunity.CapexRequired + opportunity.AnnualOpex * PROJECTION_YEARS,
                TotalCashInflow = cashFlows.Sum(),
                Recommendation = recommendation,
                CashFlowProjection = cashFlows
            };

            _completedAnalyses.Add(analysis);
            OnAnalysisCompleted?.Invoke(analysis);

            return analysis;
        }

        /// <summary>
        /// Get available financing options based on current company status.
        /// </summary>
        public List<FinancingOption> GetAvailableFinancing(
            decimal annualRevenue,
            decimal totalAssets,
            decimal totalDebt,
            bool isPubliclyListed)
        {
            return _financingOptions.Where(f =>
            {
                if (f.RequiresPublicListing && !isPubliclyListed) return false;
                if (f.MinimumRevenue > annualRevenue) return false;
                // Check debt covenants (max leverage)
                if (f.Type == FinancingType.BankLoan || f.Type == FinancingType.CorporateBond)
                {
                    decimal currentLeverage = annualRevenue > 0 ? totalDebt / (annualRevenue * 0.25m) : 99;
                    if (currentLeverage > 4) return false; // Too leveraged for more debt
                }
                return true;
            }).ToList();
        }

        /// <summary>
        /// Execute an investment using specified financing.
        /// </summary>
        public (bool Success, string Message) ExecuteInvestment(
            InvestmentOpportunity opportunity,
            FinancingType financing,
            decimal amount)
        {
            if (!opportunity.Available)
                return (false, "Investment no longer available");

            switch (financing)
            {
                case FinancingType.CashReserves:
                    if (!_finance.CanAfford(amount))
                        return (false, $"Insufficient cash. Need {amount:N0} NOK, have {_finance.Balance:N0} NOK");
                    _finance.Spend(amount);
                    break;

                case FinancingType.BankLoan:
                    _loans.InterestRateAnnual = 0.06m; // 6% for secured loans
                    _finance.AddRevenue(amount); // Loan proceeds
                    _investors.IssueBond(amount); // Track as debt
                    break;

                case FinancingType.CorporateBond:
                    _investors.IssueBond(amount);
                    _finance.AddRevenue(amount);
                    break;

                case FinancingType.EquitySale:
                    decimal pricePerShare = _stockMarket?.SharePrice ?? _investors.SharePrice;
                    _investors.IssueEquity(amount, pricePerShare);
                    _finance.AddRevenue(amount);
                    break;

                case FinancingType.SecondaryOffering:
                    if (_stockMarket == null || _stockMarket.Status == OwnershipStatus.Private)
                        return (false, "Must be publicly listed for secondary offering");
                    var (success, raised, _) = _stockMarket.IssueEquity((float)(amount / _stockMarket.MarketCap));
                    if (!success) return (false, "Secondary offering failed");
                    _finance.AddRevenue(raised);
                    break;

                case FinancingType.GovernmentGrant:
                    decimal grantAmount = _finance.ApplyGrantMultiplier(amount * 0.5m); // Grants cover 50%
                    _finance.AddRevenue(grantAmount);
                    if (!_finance.CanAfford(amount - grantAmount))
                        return (false, "Cannot cover remaining cost after grant");
                    _finance.Spend(amount - grantAmount);
                    break;

                case FinancingType.JointVenture:
                    // Player pays 50%, gets 50% of returns
                    decimal playerShare = amount * 0.5m;
                    if (!_finance.CanAfford(playerShare))
                        return (false, $"Cannot afford JV contribution of {playerShare:N0} NOK");
                    _finance.Spend(playerShare);
                    break;
            }

            opportunity.Available = false;
            OnInvestmentExecuted?.Invoke(opportunity.Id, financing, amount);

            return (true, $"Investment in {opportunity.Name} executed via {financing}");
        }

        /// <summary>
        /// Get strategic investment recommendations based on current empire phase.
        /// </summary>
        public List<InvestmentOpportunity> GetRecommendedInvestments(
            int playerLicenses,
            float playerMarketShare,
            decimal playerRevenue,
            bool hasProcessing,
            bool hasHatchery,
            bool isPubliclyListed)
        {
            var recommendations = new List<InvestmentOpportunity>();

            // Phase-based recommendations
            if (playerLicenses < 5)
            {
                // Small Farmer → Regional Player
                recommendations.AddRange(_opportunities.Where(o =>
                    o.Category == "Organic" && o.CapexRequired < 500_000_000m));
            }
            else if (playerLicenses < 20)
            {
                // Regional Player → National Leader
                if (!hasProcessing)
                    recommendations.AddRange(_opportunities.Where(o =>
                        o.Category == "VerticalIntegration" && o.CapabilityUnlocked.Contains("Processing")));
                recommendations.AddRange(_opportunities.Where(o =>
                    o.Category == "Acquisition" && o.CapexRequired < 2_000_000_000m));
            }
            else if (playerLicenses < 50)
            {
                // National Leader → Global Titan
                recommendations.AddRange(_opportunities.Where(o =>
                    o.Category == "Diversification" || o.Category == "Acquisition"));
            }
            else
            {
                // Global Titan → Monopolist
                recommendations.AddRange(_opportunities.Where(o =>
                    o.Category == "Acquisition" && o.MarketShareImpact > 0.05f));
            }

            return recommendations
                .Where(o => o.Available)
                .OrderByDescending(o => o.ProjectedROI)
                .Take(5)
                .ToList();
        }

        // ─── Initialization ─────────────────────────────────

        private void InitializeFinancingOptions()
        {
            _financingOptions.AddRange(new[]
            {
                new FinancingOption
                {
                    Type = FinancingType.BankLoan,
                    Name = "DNB Secured Term Loan",
                    Description = "Standard Norwegian bank loan secured against assets (licenses, equipment, property)",
                    MaxAmount = 500_000_000m,
                    InterestRate = 0.06m,
                    TermYears = 7,
                    RequiresCollateral = true,
                    MinimumRevenue = 50_000_000m,
                    Covenants = new() { "Debt/EBITDA < 4.0x", "Interest coverage > 2.5x", "Min equity ratio 30%" },
                    RiskLevel = "Low"
                },
                new FinancingOption
                {
                    Type = FinancingType.BankLoan,
                    Name = "Nordea Expansion Facility",
                    Description = "Larger credit facility for major expansion projects",
                    MaxAmount = 2_000_000_000m,
                    InterestRate = 0.055m,
                    TermYears = 10,
                    RequiresCollateral = true,
                    MinimumRevenue = 500_000_000m,
                    Covenants = new() { "Debt/EBITDA < 3.5x", "Min DSCR 1.5x", "No additional senior debt" },
                    RiskLevel = "Medium"
                },
                new FinancingOption
                {
                    Type = FinancingType.CorporateBond,
                    Name = "Nordic Bond (Unsecured)",
                    Description = "High-yield corporate bond placement via Nordic ABM",
                    MaxAmount = 1_500_000_000m,
                    InterestRate = 0.075m,
                    TermYears = 5,
                    RequiresCollateral = false,
                    MinimumRevenue = 1_000_000_000m,
                    Covenants = new() { "Debt/EBITDA < 5.0x", "Change of control clause" },
                    RiskLevel = "Medium"
                },
                new FinancingOption
                {
                    Type = FinancingType.CorporateBond,
                    Name = "Green Bond (Sustainability-linked)",
                    Description = "Lower rate bond for ESG-aligned investments (green feed, RAS systems, carbon reduction)",
                    MaxAmount = 1_000_000_000m,
                    InterestRate = 0.05m,
                    TermYears = 7,
                    RequiresCollateral = false,
                    MinimumRevenue = 500_000_000m,
                    Covenants = new() { "Sustainability KPI targets", "Annual ESG reporting", "Use of proceeds restricted" },
                    RiskLevel = "Low"
                },
                new FinancingOption
                {
                    Type = FinancingType.EquitySale,
                    Name = "Private Equity Round",
                    Description = "Sell minority stake to PE investor (DNB Private Equity, Herkules, FSN Capital)",
                    MaxAmount = 3_000_000_000m,
                    InterestRate = 0m,
                    TermYears = 0,
                    DilutionPercent = 0.20f,
                    MinimumRevenue = 200_000_000m,
                    RiskLevel = "Medium"
                },
                new FinancingOption
                {
                    Type = FinancingType.SecondaryOffering,
                    Name = "Secondary Share Offering",
                    Description = "Issue new shares on the stock exchange to raise capital",
                    MaxAmount = 5_000_000_000m,
                    InterestRate = 0m,
                    TermYears = 0,
                    DilutionPercent = 0.15f,
                    RequiresPublicListing = true,
                    MinimumRevenue = 1_000_000_000m,
                    RiskLevel = "Low"
                },
                new FinancingOption
                {
                    Type = FinancingType.GovernmentGrant,
                    Name = "Innovasjon Norge Green Tech Grant",
                    Description = "Government grant covering 50% of green technology investments (RAS, offshore, sustainable feed)",
                    MaxAmount = 100_000_000m,
                    InterestRate = 0m,
                    TermYears = 0,
                    MinimumRevenue = 10_000_000m,
                    RiskLevel = "Very Low"
                },
                new FinancingOption
                {
                    Type = FinancingType.JointVenture,
                    Name = "International Joint Venture",
                    Description = "Partner with local operator for international expansion (Scotland, Chile, Canada)",
                    MaxAmount = 2_000_000_000m,
                    InterestRate = 0m,
                    TermYears = 0,
                    DilutionPercent = 0f,  // No equity dilution, but 50/50 revenue split
                    MinimumRevenue = 500_000_000m,
                    RiskLevel = "High"
                }
            });
        }

        private void InitializeInvestmentOpportunities()
        {
            _opportunities.AddRange(new[]
            {
                // === ORGANIC GROWTH ===
                new InvestmentOpportunity
                {
                    Id = "org_license_3",
                    Name = "Acquire 3 New Licenses",
                    Category = "Organic",
                    Description = "Purchase 3 new aquaculture licenses in Nordland region",
                    CapexRequired = 400_000_000m,
                    AnnualOpex = 15_000_000m,
                    ProjectedAnnualRevenue = 200_000_000m,
                    ProjectedAnnualEBITDA = 50_000_000m,
                    ProjectedROI = 0.125f,
                    PaybackPeriodYears = 5.5f,
                    Risk = RiskLevel.Low,
                    RiskFactors = new() { "License allocation uncertainty", "Site-specific disease risk" },
                    SuccessProbability = 0.85f,
                    MarketShareImpact = 0.002f,
                    LicensesGained = 3,
                    MABTonnesGained = 2340f,
                    ImplementationMonths = 6
                },
                new InvestmentOpportunity
                {
                    Id = "org_pen_expand",
                    Name = "Expand Existing Site Capacity",
                    Category = "Organic",
                    Description = "Add 4 new pens and upgraded feeding systems to existing sites",
                    CapexRequired = 120_000_000m,
                    AnnualOpex = 8_000_000m,
                    ProjectedAnnualRevenue = 80_000_000m,
                    ProjectedAnnualEBITDA = 25_000_000m,
                    ProjectedROI = 0.21f,
                    PaybackPeriodYears = 3.5f,
                    Risk = RiskLevel.VeryLow,
                    RiskFactors = new() { "Construction delays", "Environmental permits" },
                    SuccessProbability = 0.92f,
                    ImplementationMonths = 8
                },
                new InvestmentOpportunity
                {
                    Id = "org_tech_upgrade",
                    Name = "Smart Farm Technology Package",
                    Category = "Organic",
                    Description = "AI-powered feeding, underwater cameras, real-time biomass sensors",
                    CapexRequired = 45_000_000m,
                    AnnualOpex = 5_000_000m,
                    ProjectedAnnualRevenue = 0m,
                    ProjectedAnnualEBITDA = 15_000_000m,  // Cost savings
                    ProjectedROI = 0.33f,
                    PaybackPeriodYears = 2.5f,
                    Risk = RiskLevel.Low,
                    RiskFactors = new() { "Technology integration complexity" },
                    SuccessProbability = 0.88f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Precision Farming",
                    ImplementationMonths = 4
                },

                // === ACQUISITIONS ===
                new InvestmentOpportunity
                {
                    Id = "acq_greed_gregarious",
                    Name = "Acquire Greed Gregarious ASA",
                    Category = "Acquisition",
                    Description = "Hostile takeover of mid-tier competitor. 60 licenses, 22 sites, global operations. Overleveraged = opportunity.",
                    CapexRequired = 28_000_000_000m,
                    AnnualOpex = 200_000_000m,
                    ProjectedAnnualRevenue = 14_000_000_000m,
                    ProjectedAnnualEBITDA = 2_800_000_000m,
                    ProjectedROI = 0.10f,
                    PaybackPeriodYears = 8f,
                    Risk = RiskLevel.High,
                    RiskFactors = new() { "Integration risk", "Antitrust review", "Hidden liabilities", "Cultural clash" },
                    SuccessProbability = 0.55f,
                    MarketShareImpact = 0.06f,
                    LicensesGained = 60,
                    MABTonnesGained = 80_000f,
                    ImplementationMonths = 18
                },
                new InvestmentOpportunity
                {
                    Id = "acq_offshore_optimists",
                    Name = "Acquire Offshore Optimists AS",
                    Category = "Acquisition",
                    Description = "Buy the struggling tech startup. Offshore platform IP, 8 licenses, high cash burn. Potential turnaround or write-off.",
                    CapexRequired = 18_000_000_000m,
                    AnnualOpex = 150_000_000m,
                    ProjectedAnnualRevenue = 2_000_000_000m,
                    ProjectedAnnualEBITDA = -500_000_000m,  // Currently unprofitable
                    ProjectedROI = -0.03f,
                    PaybackPeriodYears = 12f,
                    Risk = RiskLevel.VeryHigh,
                    RiskFactors = new() { "Unproven technology", "High OPEX", "Regulatory uncertainty", "Key person risk" },
                    SuccessProbability = 0.40f,
                    MarketShareImpact = 0.01f,
                    LicensesGained = 8,
                    MABTonnesGained = 15_000f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Offshore Farming Technology",
                    ImplementationMonths = 24
                },
                new InvestmentOpportunity
                {
                    Id = "acq_nordlaks",
                    Name = "Acquire NordLaks Nord AS",
                    Category = "Acquisition",
                    Description = "Buy conservative family company. 40 licenses, excellent Arctic locations. Willing seller seeking succession.",
                    CapexRequired = 22_000_000_000m,
                    AnnualOpex = 100_000_000m,
                    ProjectedAnnualRevenue = 8_000_000_000m,
                    ProjectedAnnualEBITDA = 2_000_000_000m,
                    ProjectedROI = 0.09f,
                    PaybackPeriodYears = 9f,
                    Risk = RiskLevel.Medium,
                    RiskFactors = new() { "Integration of family culture", "Key employee retention", "Arctic weather risk" },
                    SuccessProbability = 0.70f,
                    MarketShareImpact = 0.04f,
                    LicensesGained = 40,
                    MABTonnesGained = 55_000f,
                    ImplementationMonths = 12
                },

                // === VERTICAL INTEGRATION ===
                new InvestmentOpportunity
                {
                    Id = "vi_processing_line",
                    Name = "Build Own Processing Line",
                    Category = "VerticalIntegration",
                    Description = "Small processing facility: whole fish, HOG, and basic fillets. End toll processing dependence.",
                    CapexRequired = 320_000_000m,
                    AnnualOpex = 32_000_000m,
                    ProjectedAnnualRevenue = 150_000_000m,
                    ProjectedAnnualEBITDA = 48_000_000m,
                    ProjectedROI = 0.15f,
                    PaybackPeriodYears = 5f,
                    Risk = RiskLevel.Medium,
                    RiskFactors = new() { "Construction delays", "Operating complexity", "Labor costs" },
                    SuccessProbability = 0.80f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Processing (Whole/HOG/Fillet)",
                    ImplementationMonths = 18
                },
                new InvestmentOpportunity
                {
                    Id = "vi_hatchery",
                    Name = "Build RAS Hatchery",
                    Category = "VerticalIntegration",
                    Description = "Recirculating Aquaculture System for smolt production. Secure supply chain + genetic control.",
                    CapexRequired = 350_000_000m,
                    AnnualOpex = 25_000_000m,
                    ProjectedAnnualRevenue = 80_000_000m,
                    ProjectedAnnualEBITDA = 35_000_000m,
                    ProjectedROI = 0.10f,
                    PaybackPeriodYears = 7f,
                    Risk = RiskLevel.Medium,
                    RiskFactors = new() { "RAS technology complexity", "Biosecurity", "Power costs" },
                    SuccessProbability = 0.75f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Own Smolt Production",
                    ImplementationMonths = 24
                },
                new InvestmentOpportunity
                {
                    Id = "vi_logistics",
                    Name = "Build Cold Chain & Logistics Fleet",
                    Category = "VerticalIntegration",
                    Description = "Refrigerated trucks, cold storage, and direct-to-customer distribution",
                    CapexRequired = 180_000_000m,
                    AnnualOpex = 28_000_000m,
                    ProjectedAnnualRevenue = 60_000_000m,
                    ProjectedAnnualEBITDA = 20_000_000m,
                    ProjectedROI = 0.11f,
                    PaybackPeriodYears = 6f,
                    Risk = RiskLevel.Low,
                    RiskFactors = new() { "Fleet management complexity", "Fuel costs", "Driver shortage" },
                    SuccessProbability = 0.85f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Direct Distribution",
                    ImplementationMonths = 15
                },

                // === DIVERSIFICATION ===
                new InvestmentOpportunity
                {
                    Id = "div_scotland",
                    Name = "Enter Scotland Market",
                    Category = "Diversification",
                    Description = "Acquire Scottish Highland sites. Different regulations, GBP currency exposure, Brexit risk.",
                    CapexRequired = 3_000_000_000m,
                    AnnualOpex = 200_000_000m,
                    ProjectedAnnualRevenue = 2_500_000_000m,
                    ProjectedAnnualEBITDA = 500_000_000m,
                    ProjectedROI = 0.17f,
                    PaybackPeriodYears = 4.5f,
                    Risk = RiskLevel.High,
                    RiskFactors = new() { "Currency risk (GBP/NOK)", "Different regulations", "Sea lice pressure", "NIMBY opposition" },
                    SuccessProbability = 0.60f,
                    MarketShareImpact = 0.005f,
                    LicensesGained = 15,
                    MABTonnesGained = 20_000f,
                    ImplementationMonths = 18
                },
                new InvestmentOpportunity
                {
                    Id = "div_chile",
                    Name = "Enter Chile Market",
                    Category = "Diversification",
                    Description = "Joint venture with local Chilean operator. Counter-seasonal production, different disease profile.",
                    CapexRequired = 5_000_000_000m,
                    AnnualOpex = 350_000_000m,
                    ProjectedAnnualRevenue = 4_000_000_000m,
                    ProjectedAnnualEBITDA = 800_000_000m,
                    ProjectedROI = 0.16f,
                    PaybackPeriodYears = 5f,
                    Risk = RiskLevel.VeryHigh,
                    RiskFactors = new() { "Political instability", "SRS/ISA disease risk", "Environmental activism", "Currency risk (CLP)" },
                    SuccessProbability = 0.45f,
                    MarketShareImpact = 0.008f,
                    LicensesGained = 25,
                    MABTonnesGained = 40_000f,
                    ImplementationMonths = 24
                },
                new InvestmentOpportunity
                {
                    Id = "div_trout",
                    Name = "Diversify: Fjord Trout Production",
                    Category = "Diversification",
                    Description = "Add large rainbow trout (fjord trout) production. Lower competition, growing market, uses existing sites.",
                    CapexRequired = 200_000_000m,
                    AnnualOpex = 20_000_000m,
                    ProjectedAnnualRevenue = 300_000_000m,
                    ProjectedAnnualEBITDA = 75_000_000m,
                    ProjectedROI = 0.38f,
                    PaybackPeriodYears = 2f,
                    Risk = RiskLevel.Low,
                    RiskFactors = new() { "Market acceptance", "Different biology (faster growth, different feed)" },
                    SuccessProbability = 0.82f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Trout Production",
                    ImplementationMonths = 8
                },
                new InvestmentOpportunity
                {
                    Id = "div_branded_retail",
                    Name = "Launch Premium Retail Brand",
                    Category = "Diversification",
                    Description = "Create consumer brand 'Fjordkongen' for retail. Smoked salmon, sashimi packs, ready meals.",
                    CapexRequired = 150_000_000m,
                    AnnualOpex = 40_000_000m,
                    ProjectedAnnualRevenue = 500_000_000m,
                    ProjectedAnnualEBITDA = 100_000_000m,
                    ProjectedROI = 0.67f,
                    PaybackPeriodYears = 1.2f,
                    Risk = RiskLevel.Medium,
                    RiskFactors = new() { "Brand building costs", "Retail competition", "Marketing complexity" },
                    SuccessProbability = 0.55f,
                    UnlocksNewCapability = true,
                    CapabilityUnlocked = "Premium Retail Brand",
                    ImplementationMonths = 12
                }
            });
        }
    }
}
