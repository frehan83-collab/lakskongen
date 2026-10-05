using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    // =========================================================================
    // StockMarket.cs - IPO & Stock Price Simulation (T79)
    // =========================================================================
    // Simulates going public on the Oslo Stock Exchange (Euronext Growth / OSE).
    // Models: IPO process, stock price dynamics, quarterly earnings pressure,
    // dividend policy, analyst coverage, institutional investors, and buybacks.
    // =========================================================================

    /// <summary>
    /// Current company ownership structure.
    /// </summary>
    public enum OwnershipStatus
    {
        /// <summary>Fully private, founder-controlled</summary>
        Private,
        /// <summary>Private equity backed (PE investors)</summary>
        PEBacked,
        /// <summary>Listed on Euronext Growth Oslo (small cap)</summary>
        EuronextGrowth,
        /// <summary>Full OSE main list (Oslo Børs)</summary>
        OsloBoers
    }

    /// <summary>
    /// Result of an IPO attempt.
    /// </summary>
    public class IPOResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal SharePrice { get; set; }
        public decimal MarketCap { get; set; }
        public decimal CapitalRaised { get; set; }
        public float OwnershipDilution { get; set; }  // Fraction of company sold
        public int AnalystCoverage { get; set; }       // Number of covering analysts
        public string ExchangeListedOn { get; set; } = string.Empty;
    }

    /// <summary>
    /// Quarterly earnings report that affects stock price.
    /// </summary>
    public class EarningsReport
    {
        public int Quarter { get; set; }
        public int Year { get; set; }
        public decimal Revenue { get; set; }
        public decimal EBITDA { get; set; }
        public decimal NetIncome { get; set; }
        public decimal EPS { get; set; }  // Earnings Per Share
        public decimal ConsensusEPS { get; set; }  // Analyst consensus estimate
        public decimal Surprise => EPS - ConsensusEPS;
        public bool Beat => Surprise > 0;
        public bool Miss => Surprise < 0;
        public string GuidanceOutlook { get; set; } = string.Empty;  // "Raised", "Maintained", "Lowered"
        public decimal DividendPerShare { get; set; }
    }

    /// <summary>
    /// Stock price movement event for UI notification.
    /// </summary>
    public class StockPriceEvent
    {
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
        public decimal ChangePercent => OldPrice > 0 ? (NewPrice - OldPrice) / OldPrice * 100 : 0;
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Investor type with different behavior patterns.
    /// </summary>
    public class InstitutionalInvestor
    {
        public string Name { get; set; } = string.Empty;
        public InvestorType Type { get; set; }
        public float OwnershipPercent { get; set; }
        public float Patience { get; set; }  // 0-1 (1 = very patient, long-term)
        public float ActivismRisk { get; set; }  // 0-1 (1 = activist, demands changes)
        public string Demands { get; set; } = string.Empty;
    }

    public enum InvestorType
    {
        /// <summary>Government pension fund (Oljefondet style) — very patient</summary>
        SovereignWealth,
        /// <summary>Long-term institutional (pension fund, endowment)</summary>
        LongTermInstitutional,
        /// <summary>Index fund — neutral, follows market</summary>
        IndexFund,
        /// <summary>Hedge fund — short-term, activist potential</summary>
        HedgeFund,
        /// <summary>Retail investors — volatile sentiment</summary>
        Retail,
        /// <summary>Founder/insider shares</summary>
        Insider
    }

    /// <summary>
    /// Dividend policy options for public companies.
    /// </summary>
    public enum DividendPolicy
    {
        /// <summary>No dividends — reinvest everything for growth</summary>
        NoDividend,
        /// <summary>Low payout (20-30% of net income)</summary>
        LowPayout,
        /// <summary>Moderate payout (40-60% of net income)</summary>
        ModeratePayout,
        /// <summary>High payout (70-90% of net income) — income stock</summary>
        HighPayout,
        /// <summary>Variable — tied to salmon price cycles</summary>
        VariableCyclical
    }

    /// <summary>
    /// Simulates the stock market for a publicly traded salmon farming company.
    /// Models Oslo Stock Exchange dynamics, analyst coverage, and investor relations.
    /// </summary>
    public class StockMarketSystem
    {
        // Company stock state
        public OwnershipStatus Status { get; private set; } = OwnershipStatus.Private;
        public decimal SharePrice { get; private set; }
        public decimal MarketCap => SharePrice * TotalShares;
        public decimal TotalShares { get; private set; } = 1_000_000m;
        public float FounderOwnership { get; private set; } = 1.0f;
        public decimal BookValuePerShare { get; private set; } = 10m;
        public DividendPolicy CurrentDividendPolicy { get; private set; } = DividendPolicy.NoDividend;
        public string Ticker { get; private set; } = "LAKS";

        // Valuation metrics
        public decimal PERatio => _lastEPS > 0 ? SharePrice / _lastEPS : 0;
        public decimal EVToEBITDA { get; private set; }
        public decimal PriceToBook => BookValuePerShare > 0 ? SharePrice / BookValuePerShare : 0;
        public decimal DividendYield => SharePrice > 0 ? _annualDividendPerShare / SharePrice : 0;

        // Market sentiment
        public float MarketSentiment { get; private set; } = 0.5f;  // 0=bearish, 1=bullish
        public float AnalystRating { get; private set; } = 3.0f;    // 1-5 (1=sell, 5=strong buy)
        public int AnalystCoverage { get; private set; }
        public decimal AnalystTargetPrice { get; private set; }

        // Investor base
        public List<InstitutionalInvestor> Investors { get; } = new();
        public float InstitutionalOwnership => Investors.Sum(i => i.OwnershipPercent);

        // History
        public List<StockPriceEvent> PriceHistory { get; } = new();
        public List<EarningsReport> EarningsHistory { get; } = new();

        /// <summary>
        /// Exact state restore from save (no cash side effects, no events).
        /// Institutional registry is session-built (analyst coverage events)
        /// and intentionally not persisted.
        /// </summary>
        public void RestoreState(OwnershipStatus status, decimal sharePrice, decimal totalShares, IEnumerable<decimal>? priceHistory = null)
        {
            Status = status;
            if (sharePrice > 0) SharePrice = sharePrice;
            if (totalShares > 0) TotalShares = totalShares;
            if (priceHistory != null)
            {
                PriceHistory.Clear();
                foreach (var price in priceHistory)
                    PriceHistory.Add(new StockPriceEvent { OldPrice = price, NewPrice = price, Reason = "Save restore", Timestamp = DateTime.Now });
            }
        }

        // Private state
        private readonly Random _rng;
        private decimal _lastEPS;
        private decimal _annualDividendPerShare;
        private decimal _consensusGrowthRate = 0.10m;  // 10% expected growth
        private int _quartersSinceListing;

        // Constants
        private const decimal MIN_IPO_VALUATION = 1_000_000_000m;    // 1B NOK minimum for credible IPO
        private const decimal OSE_UPGRADE_VALUATION = 5_000_000_000m; // 5B NOK for main OSE list
        private const float IPO_DILUTION_MIN = 0.15f;
        private const float IPO_DILUTION_MAX = 0.35f;
        private const decimal EARNINGS_SURPRISE_IMPACT = 0.08m;  // 8% move per EPS surprise
        private const decimal DAILY_VOLATILITY = 0.018m;  // ~1.8% daily vol (salmon stocks are volatile)

        #pragma warning disable CS0067 // Event is never used (reserved for future UI integration)
        public event Action<StockPriceEvent>? OnPriceChanged;
        #pragma warning restore CS0067
        public event Action<EarningsReport>? OnEarningsReported;
        public event Action<string>? OnInvestorAction;
        public event Action<IPOResult>? OnIPOCompleted;

        public StockMarketSystem(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Check if the company meets IPO requirements.
        /// </summary>
        public (bool Eligible, string Reason, decimal EstimatedValuation) CheckIPOEligibility(
            decimal annualRevenue, decimal annualEBITDA, decimal totalAssets)
        {
            if (Status != OwnershipStatus.Private && Status != OwnershipStatus.PEBacked)
                return (false, "Already listed", 0);

            decimal estimatedValuation = annualEBITDA > 0
                ? annualEBITDA * 10m  // 10x EBITDA for salmon companies
                : totalAssets * 1.5m;

            if (estimatedValuation < MIN_IPO_VALUATION)
                return (false, $"Valuation too low. Need ≥{MIN_IPO_VALUATION / 1_000_000_000:F1}B NOK, estimated at {estimatedValuation / 1_000_000_000:F2}B NOK", estimatedValuation);

            if (annualRevenue < 500_000_000m)
                return (false, "Revenue too low. Need ≥500M NOK annual revenue for credible IPO", estimatedValuation);

            if (annualEBITDA <= 0)
                return (false, "Must be profitable (positive EBITDA) before IPO", estimatedValuation);

            return (true, "Eligible for IPO", estimatedValuation);
        }

        /// <summary>
        /// Execute an IPO. Returns result with capital raised and dilution.
        /// </summary>
        public IPOResult ExecuteIPO(
            decimal annualRevenue,
            decimal annualEBITDA,
            decimal totalAssets,
            float targetDilution = 0.25f)
        {
            var (eligible, reason, valuation) = CheckIPOEligibility(annualRevenue, annualEBITDA, totalAssets);

            if (!eligible)
            {
                return new IPOResult { Success = false, Message = reason };
            }

            // Determine exchange
            string exchange = valuation >= OSE_UPGRADE_VALUATION ? "Oslo Børs (OSE)" : "Euronext Growth Oslo";
            Status = valuation >= OSE_UPGRADE_VALUATION ? OwnershipStatus.OsloBoers : OwnershipStatus.EuronextGrowth;

            // Calculate IPO pricing
            targetDilution = Math.Clamp(targetDilution, IPO_DILUTION_MIN, IPO_DILUTION_MAX);
            decimal preMoneyValuation = valuation;
            decimal capitalRaised = preMoneyValuation * (decimal)targetDilution / (1 - (decimal)targetDilution);
            decimal postMoneyValuation = preMoneyValuation + capitalRaised;

            // Issue new shares
            decimal newShares = TotalShares * (decimal)targetDilution / (1 - (decimal)targetDilution);
            TotalShares += newShares;
            SharePrice = postMoneyValuation / TotalShares;
            FounderOwnership = 1.0f - targetDilution;

            // Set book value
            BookValuePerShare = (totalAssets + capitalRaised) / TotalShares;

            // IPO day pop (10-30% typical for hot IPOs)
            float pop = 0.10f + _rng.NextSingle() * 0.20f;
            SharePrice *= 1 + (decimal)pop;

            // Initialize analyst coverage (2-8 analysts based on size)
            AnalystCoverage = Math.Min(8, Math.Max(2, (int)(valuation / 2_000_000_000m) + 2));
            AnalystTargetPrice = SharePrice * 1.10m;
            AnalystRating = 3.5f + _rng.NextSingle() * 1.0f;

            // Initialize institutional investors
            InitializeInvestorBase(targetDilution);

            // Set initial EPS
            _lastEPS = annualEBITDA * 0.75m / TotalShares; // ~75% EBITDA to net income after tax

            var result = new IPOResult
            {
                Success = true,
                Message = $"Successfully listed on {exchange}! Ticker: {Ticker}",
                SharePrice = SharePrice,
                MarketCap = MarketCap,
                CapitalRaised = capitalRaised,
                OwnershipDilution = targetDilution,
                AnalystCoverage = AnalystCoverage,
                ExchangeListedOn = exchange
            };

            OnIPOCompleted?.Invoke(result);

            PriceHistory.Add(new StockPriceEvent
            {
                OldPrice = 0,
                NewPrice = SharePrice,
                Reason = $"IPO at {SharePrice:F2} NOK/share on {exchange}",
                Timestamp = DateTime.Now
            });

            return result;
        }

        /// <summary>
        /// Advance stock simulation by one quarter. Must be called with current financials.
        /// </summary>
        public EarningsReport? AdvanceQuarter(
            decimal quarterlyRevenue,
            decimal quarterlyEBITDA,
            decimal quarterlyNetIncome,
            decimal totalAssets,
            decimal totalDebt)
        {
            if (Status == OwnershipStatus.Private) return null;

            _quartersSinceListing++;

            // Calculate EPS
            decimal eps = quarterlyNetIncome / TotalShares;
            decimal consensusEPS = _lastEPS * (1 + _consensusGrowthRate / 4);

            // Create earnings report
            var report = new EarningsReport
            {
                Quarter = (_quartersSinceListing - 1) % 4 + 1,
                Year = _quartersSinceListing / 4 + 1,
                Revenue = quarterlyRevenue,
                EBITDA = quarterlyEBITDA,
                NetIncome = quarterlyNetIncome,
                EPS = eps,
                ConsensusEPS = consensusEPS,
                GuidanceOutlook = DetermineGuidance(quarterlyEBITDA, quarterlyRevenue)
            };

            // Apply dividend
            decimal dividendPayout = CalculateDividend(quarterlyNetIncome);
            report.DividendPerShare = dividendPayout / TotalShares;
            _annualDividendPerShare = report.DividendPerShare * 4;

            EarningsHistory.Add(report);
            _lastEPS = eps;

            // Stock price reaction to earnings
            decimal oldPrice = SharePrice;
            ApplyEarningsReaction(report);

            // Daily price drift for the quarter (simulate ~65 trading days)
            for (int day = 0; day < 65; day++)
            {
                ApplyDailyPriceMovement();
            }

            // Update valuation metrics
            EVToEBITDA = quarterlyEBITDA > 0 ? (MarketCap + totalDebt) / (quarterlyEBITDA * 4) : 0;
            BookValuePerShare = totalAssets / TotalShares;

            // Update analyst consensus
            UpdateAnalystCoverage(report);

            // Check for investor activism
            CheckInvestorActivism(report);

            OnEarningsReported?.Invoke(report);

            return report;
        }

        /// <summary>
        /// Execute a share buyback to support stock price.
        /// </summary>
        public (bool Success, decimal SharesBought, decimal TotalCost) ExecuteBuyback(decimal budgetNOK)
        {
            if (Status == OwnershipStatus.Private) return (false, 0, 0);
            if (budgetNOK <= 0) return (false, 0, 0);

            decimal sharesBought = budgetNOK / SharePrice;
            TotalShares -= sharesBought;
            FounderOwnership = Math.Min(1.0f, FounderOwnership * (float)(TotalShares + sharesBought) / (float)TotalShares);

            // Buyback creates ~2-5% price support
            decimal priceImpact = (budgetNOK / MarketCap) * 3m;
            decimal oldPrice = SharePrice;
            SharePrice *= 1 + priceImpact;

            PriceHistory.Add(new StockPriceEvent
            {
                OldPrice = oldPrice,
                NewPrice = SharePrice,
                Reason = $"Buyback: {sharesBought:N0} shares at {oldPrice:F2} NOK",
                Timestamp = DateTime.Now
            });

            return (true, sharesBought, budgetNOK);
        }

        /// <summary>
        /// Issue new equity (secondary offering) for capital raising.
        /// </summary>
        public (bool Success, decimal CapitalRaised, float NewFounderOwnership) IssueEquity(float dilutionPercent)
        {
            if (Status == OwnershipStatus.Private) return (false, 0, 0);
            if (dilutionPercent <= 0 || dilutionPercent > 0.20f) return (false, 0, 0);

            decimal newShares = TotalShares * (decimal)dilutionPercent;
            decimal capitalRaised = newShares * SharePrice * 0.95m; // 5% discount to market
            TotalShares += newShares;
            FounderOwnership *= (1 - dilutionPercent);

            // Secondary offering typically causes 3-8% price drop
            decimal oldPrice = SharePrice;
            SharePrice *= 1 - (decimal)(0.03 + _rng.NextDouble() * 0.05);

            PriceHistory.Add(new StockPriceEvent
            {
                OldPrice = oldPrice,
                NewPrice = SharePrice,
                Reason = $"Secondary offering: {dilutionPercent:P0} dilution, raised {capitalRaised / 1_000_000:F0}M NOK",
                Timestamp = DateTime.Now
            });

            return (true, capitalRaised, FounderOwnership);
        }

        /// <summary>
        /// Set dividend policy.
        /// </summary>
        public void SetDividendPolicy(DividendPolicy policy)
        {
            CurrentDividendPolicy = policy;

            // Market reacts to dividend policy changes
            float sentiment = policy switch
            {
                DividendPolicy.HighPayout => 0.05f,  // Income investors like it
                DividendPolicy.NoDividend => -0.03f,  // Disappointment
                DividendPolicy.ModeratePayout => 0.02f,
                _ => 0f
            };
            MarketSentiment = Math.Clamp(MarketSentiment + sentiment, 0f, 1f);
        }

        /// <summary>
        /// Check if the company can upgrade from Euronext Growth to OSE main list.
        /// </summary>
        public (bool Eligible, string Reason) CheckOSEUpgradeEligibility()
        {
            if (Status != OwnershipStatus.EuronextGrowth)
                return (false, "Must be listed on Euronext Growth first");
            if (MarketCap < OSE_UPGRADE_VALUATION)
                return (false, $"Market cap too low: {MarketCap / 1_000_000_000:F2}B NOK (need {OSE_UPGRADE_VALUATION / 1_000_000_000:F0}B NOK)");
            if (_quartersSinceListing < 8)
                return (false, $"Need 2+ years of listing history ({_quartersSinceListing}/8 quarters)");

            return (true, "Eligible for OSE main list upgrade");
        }

        /// <summary>
        /// Upgrade listing to Oslo Børs main list.
        /// </summary>
        public bool UpgradeToOSE()
        {
            var (eligible, _) = CheckOSEUpgradeEligibility();
            if (!eligible) return false;

            Status = OwnershipStatus.OsloBoers;
            AnalystCoverage = Math.Min(12, AnalystCoverage + 3);

            // OSE upgrade typically adds 5-15% to valuation
            decimal oldPrice = SharePrice;
            SharePrice *= 1 + (decimal)(0.05 + _rng.NextDouble() * 0.10);

            PriceHistory.Add(new StockPriceEvent
            {
                OldPrice = oldPrice,
                NewPrice = SharePrice,
                Reason = "Upgraded to Oslo Børs main list — increased institutional interest",
                Timestamp = DateTime.Now
            });

            return true;
        }

        // ─── Private helpers ─────────────────────────────────

        private void InitializeInvestorBase(float publicFloat)
        {
            Investors.Clear();

            float remaining = publicFloat;
            Investors.Add(new InstitutionalInvestor
            {
                Name = "Founder (You)",
                Type = InvestorType.Insider,
                OwnershipPercent = 1.0f - publicFloat,
                Patience = 1.0f,
                ActivismRisk = 0f
            });

            // Institutional allocation
            float institutional = remaining * 0.6f;
            Investors.Add(new InstitutionalInvestor
            {
                Name = "Norges Bank Investment Management",
                Type = InvestorType.SovereignWealth,
                OwnershipPercent = institutional * 0.3f,
                Patience = 0.95f,
                ActivismRisk = 0.15f,
                Demands = "ESG compliance, sustainability reporting"
            });
            Investors.Add(new InstitutionalInvestor
            {
                Name = "KLP Asset Management",
                Type = InvestorType.LongTermInstitutional,
                OwnershipPercent = institutional * 0.2f,
                Patience = 0.85f,
                ActivismRisk = 0.20f,
                Demands = "Steady dividends, corporate governance"
            });
            Investors.Add(new InstitutionalInvestor
            {
                Name = "DNB Asset Management",
                Type = InvestorType.IndexFund,
                OwnershipPercent = institutional * 0.25f,
                Patience = 0.70f,
                ActivismRisk = 0.10f
            });
            Investors.Add(new InstitutionalInvestor
            {
                Name = "Pareto Securities Fund",
                Type = InvestorType.HedgeFund,
                OwnershipPercent = institutional * 0.15f,
                Patience = 0.30f,
                ActivismRisk = 0.70f,
                Demands = "Aggressive growth, M&A, margin expansion"
            });
            Investors.Add(new InstitutionalInvestor
            {
                Name = "Retail Investors (Nordnet/DNB)",
                Type = InvestorType.Retail,
                OwnershipPercent = remaining * 0.4f,
                Patience = 0.40f,
                ActivismRisk = 0.05f
            });
        }

        private void ApplyEarningsReaction(EarningsReport report)
        {
            decimal oldPrice = SharePrice;
            decimal surprise = report.Surprise;

            // Price impact from earnings surprise
            decimal priceChange = surprise / Math.Max(0.01m, report.ConsensusEPS) * EARNINGS_SURPRISE_IMPACT;

            // Guidance impact
            priceChange += report.GuidanceOutlook switch
            {
                "Raised" => 0.03m,
                "Lowered" => -0.05m,
                _ => 0m
            };

            SharePrice *= 1 + priceChange;
            SharePrice = Math.Max(SharePrice * 0.5m, SharePrice); // Floor at 50% drop

            // Update sentiment
            MarketSentiment = Math.Clamp(
                MarketSentiment + (float)priceChange * 2,
                0f, 1f);

            PriceHistory.Add(new StockPriceEvent
            {
                OldPrice = oldPrice,
                NewPrice = SharePrice,
                Reason = report.Beat
                    ? $"Q{report.Quarter} earnings BEAT: EPS {report.EPS:F2} vs {report.ConsensusEPS:F2} consensus"
                    : $"Q{report.Quarter} earnings MISS: EPS {report.EPS:F2} vs {report.ConsensusEPS:F2} consensus",
                Timestamp = DateTime.Now
            });
        }

        private void ApplyDailyPriceMovement()
        {
            // Random walk with mean reversion and sentiment bias
            double u1 = Math.Max(1e-10, _rng.NextDouble());
            double u2 = _rng.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            decimal sentimentDrift = ((decimal)MarketSentiment - 0.5m) * 0.001m; // Tiny daily drift from sentiment
            decimal dailyReturn = sentimentDrift + DAILY_VOLATILITY * (decimal)normal;

            SharePrice *= 1 + dailyReturn;
            SharePrice = Math.Max(0.01m, SharePrice); // Never go to zero
        }

        private void UpdateAnalystCoverage(EarningsReport report)
        {
            // Analysts update target based on earnings trend
            if (report.Beat)
            {
                AnalystTargetPrice = SharePrice * (1.05m + (decimal)(_rng.NextDouble() * 0.10));
                AnalystRating = Math.Min(5.0f, AnalystRating + 0.2f);
                _consensusGrowthRate *= 1.05m;
            }
            else if (report.Miss)
            {
                AnalystTargetPrice = SharePrice * (0.90m + (decimal)(_rng.NextDouble() * 0.10));
                AnalystRating = Math.Max(1.0f, AnalystRating - 0.3f);
                _consensusGrowthRate *= 0.90m;
            }
            _consensusGrowthRate = Math.Clamp(_consensusGrowthRate, -0.20m, 0.30m);
        }

        private void CheckInvestorActivism(EarningsReport report)
        {
            foreach (var investor in Investors.Where(i => i.ActivismRisk > 0.5f))
            {
                // Hedge funds get aggressive after 2 consecutive misses
                if (investor.Type == InvestorType.HedgeFund && EarningsHistory.TakeLast(2).All(e => e.Miss))
                {
                    OnInvestorAction?.Invoke(
                        $"⚠️ {investor.Name} demands management changes after consecutive earnings misses");
                }

                // Sovereign wealth funds pressure on ESG
                if (investor.Type == InvestorType.SovereignWealth && !string.IsNullOrEmpty(investor.Demands))
                {
                    if (_rng.NextDouble() < 0.1)
                    {
                        OnInvestorAction?.Invoke(
                            $"📋 {investor.Name} requests meeting to discuss {investor.Demands}");
                    }
                }
            }
        }

        private string DetermineGuidance(decimal quarterlyEBITDA, decimal quarterlyRevenue)
        {
            if (EarningsHistory.Count < 2) return "Maintained";

            var prev = EarningsHistory.TakeLast(2).First();
            decimal ebitdaGrowth = prev.EBITDA > 0 ? (quarterlyEBITDA - prev.EBITDA) / prev.EBITDA : 0;

            if (ebitdaGrowth > 0.10m) return "Raised";
            if (ebitdaGrowth < -0.10m) return "Lowered";
            return "Maintained";
        }

        private decimal CalculateDividend(decimal quarterlyNetIncome)
        {
            if (quarterlyNetIncome <= 0) return 0;

            decimal payoutRatio = CurrentDividendPolicy switch
            {
                DividendPolicy.NoDividend => 0m,
                DividendPolicy.LowPayout => 0.25m,
                DividendPolicy.ModeratePayout => 0.50m,
                DividendPolicy.HighPayout => 0.80m,
                DividendPolicy.VariableCyclical => 0.30m + (decimal)Math.Min(0.40, (double)MarketSentiment * 0.5),
                _ => 0m
            };

            return quarterlyNetIncome * payoutRatio;
        }

        /// <summary>
        /// Get a summary snapshot for UI display.
        /// </summary>
        public StockSummary GetSummary()
        {
            return new StockSummary
            {
                Ticker = Ticker,
                Status = Status,
                SharePrice = SharePrice,
                MarketCap = MarketCap,
                TotalShares = TotalShares,
                FounderOwnership = FounderOwnership,
                PERatio = PERatio,
                EVToEBITDA = EVToEBITDA,
                PriceToBook = PriceToBook,
                DividendYield = DividendYield,
                AnalystRating = AnalystRating,
                AnalystTargetPrice = AnalystTargetPrice,
                AnalystCoverage = AnalystCoverage,
                MarketSentiment = MarketSentiment,
                DividendPolicy = CurrentDividendPolicy
            };
        }
    }

    /// <summary>
    /// Stock market summary for UI display.
    /// </summary>
    public struct StockSummary
    {
        public string Ticker;
        public OwnershipStatus Status;
        public decimal SharePrice;
        public decimal MarketCap;
        public decimal TotalShares;
        public float FounderOwnership;
        public decimal PERatio;
        public decimal EVToEBITDA;
        public decimal PriceToBook;
        public decimal DividendYield;
        public float AnalystRating;
        public decimal AnalystTargetPrice;
        public int AnalystCoverage;
        public float MarketSentiment;
        public DividendPolicy DividendPolicy;
    }
}
