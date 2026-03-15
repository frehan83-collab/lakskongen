using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Realistic salmon market price simulation based on Norwegian industry dynamics.
    /// Models: seasonal patterns, calibrated Ornstein-Uhlenbeck mean reversion,
    /// supply/demand shocks, historical Norwegian Fish Pool anchors, quality premiums,
    /// multiple sales channels, and market intelligence.
    ///
    /// O-U parameters calibrated to Fish Pool FPI data 2020-2025:
    ///   - Long-term mean ~85 NOK/kg (2020-2025 average; 2023-2025 elevated to ~90-100)
    ///   - Annual kappa ~0.6-0.8 (moderate mean reversion)
    ///   - Weekly sigma ~8-12 NOK/kg (high volatility typical of salmon spot)
    ///   - Price range: NOK 40-160/kg historically
    /// </summary>
    public class MarketSimulation
    {
        // ─── Calibrated to Fish Pool FPI 2020-2025 data ───
        private const decimal DefaultInitialPrice = 90m;   // 2024-2025 FPI average ~85-95 NOK/kg
        private const decimal PriceFloor = 40m;            // Historical low (2020 COVID trough ~45)
        private const decimal PriceCeiling = 160m;         // Historical high (2022 peak ~140, allow gameplay headroom)
        private const decimal WeeklyThetaMin = 0.008m;     // Annual kappa ~0.42 (slow reversion, persistent trends)
        private const decimal WeeklyThetaMax = 0.018m;     // Annual kappa ~0.94 (faster reversion)
        private const decimal WeeklySigmaMin = 6m;         // Lower vol in stable periods
        private const decimal WeeklySigmaMax = 14m;        // High vol during supply shocks
        private const decimal DefaultLongTermMean = 85m;   // FPI 2020-2025 avg ≈ 80-90 NOK/kg
        private const decimal DefaultWeeklyTheta = 0.012m; // Annual kappa ~0.62 (moderate reversion)
        private const decimal DefaultWeeklySigma = 9m;     // Weekly moves ~5-12 NOK/kg typical
        private const int DaysPerWeek = 7;

        private static readonly string HistoricalPriceMarkdownPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "research", "real-salmon-prices.md"));

        // ─── Price state ───
        public decimal BasePricePerKg { get; private set; } = DefaultInitialPrice;
        public decimal PricePerKg { get; private set; } = DefaultInitialPrice;
        public decimal WeeklyAvgPrice { get; private set; } = DefaultInitialPrice;

        // ─── Calibrated O-U parameters ───
        public decimal LongTermMean { get; private set; } = DefaultLongTermMean;
        public decimal MeanReversionSpeedWeekly { get; private set; } = DefaultWeeklyTheta;
        public decimal VolatilityWeekly { get; private set; } = DefaultWeeklySigma;
        public decimal PriceFloorPerKg => PriceFloor;
        public decimal PriceCeilingPerKg => PriceCeiling;
        public IReadOnlyList<HistoricalPrice> HistoricalSeries => _historicalSeries;
        public string CalibrationSummary { get; private set; } = "Using default market calibration";

        // ─── Seasonal parameters (Norwegian/global salmon market) ───
        // Based on Fish Pool FPI seasonal decomposition 2018-2025:
        // - Jan/Feb: Chinese New Year demand spike (Jan peak)
        // - Mar-May: post-winter recovery, Easter demand
        // - Jun-Aug: BBQ/grilling season peak (strongest seasonal signal)
        // - Sep-Oct: post-summer dip as harvest volumes increase
        // - Nov-Dec: Christmas/holiday demand recovery
        // Amplitude ~8-12 NOK/kg based on Fish Pool seasonal decomposition.
        private static readonly decimal[] MonthlySeasonalOffsets =
        {
              5m, -2m, -5m,  // Jan (CNY), Feb (post-CNY dip), Mar (low)
             -3m,  2m,  8m,  // Apr (Easter variable), May (rising), Jun (BBQ starts)
             12m, 10m,  3m,  // Jul (peak BBQ), Aug (still high), Sep (harvest surge → drop)
             -4m, -2m,  6m   // Oct (oversupply), Nov (stable), Dec (Christmas demand)
        };

        // ─── Supply/demand ───
        public double GlobalSupplyIndex { get; private set; } = 1.0;
        public double GlobalDemandIndex { get; private set; } = 1.0;

        // ─── Market access ───
        private HashSet<string> _accessibleMarkets = new() { "Domestic", "EU", "USA" };
        private decimal _certificationMultiplier = 1.0m;

        // ─── Price history (for charts/analytics) ───
        private readonly Queue<decimal> _priceHistory = new();
        private const int MaxHistoryLength = 365;

        // ─── Calibration data ───
        private readonly List<HistoricalPrice> _historicalSeries = new();

        // ─── Random ───
        private readonly Random _rng = new();
        private int _simulationDay;

        // ─── Integrated subsystems (T77 expansion) ───
        /// <summary>Sales channel system with spot, export, direct retail channels.</summary>
        public MarketChannelSystem Channels { get; } = new();
        /// <summary>Market intelligence: forecasting, news events, competitor tracking, brand reputation.</summary>
        public MarketIntelligenceSystem Intelligence { get; } = new();

        // ─── Events ───
        public event Action<decimal>? OnPriceChanged;
        public event Action<HashSet<string>>? OnMarketAccessChanged;
        public event Action<string>? OnMarketEvent;

        public MarketSimulation()
        {
            Initialize();
        }

        /// <summary>
        /// Load historical price anchors and initialize the O-U model with calibrated parameters.
        /// Safe to call multiple times.
        /// </summary>
        public void Initialize()
        {
            CalibrateOUParameters();

            BasePricePerKg = ClampPrice(LongTermMean);
            PricePerKg = BasePricePerKg * _certificationMultiplier;
            WeeklyAvgPrice = PricePerKg;

            _priceHistory.Clear();
            _priceHistory.Enqueue(PricePerKg);
            _simulationDay = 0;
        }

        /// <summary>
        /// Calibrate O-U parameters from the bundled markdown research file.
        /// The calibration is intentionally conservative: observed data sets the center and scale,
        /// while gameplay clamps keep output inside realistic historical salmon ranges.
        /// </summary>
        public void CalibrateOUParameters()
        {
            _historicalSeries.Clear();
            _historicalSeries.AddRange(HistoricalPrice.LoadFromMarkdown(HistoricalPriceMarkdownPath));

            if (_historicalSeries.Count == 0)
            {
                LongTermMean = DefaultLongTermMean;
                MeanReversionSpeedWeekly = DefaultWeeklyTheta;
                VolatilityWeekly = DefaultWeeklySigma;
                CalibrationSummary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Fallback calibration: μ={0:F2}, θ={1:F2}, σ={2:F2}",
                    LongTermMean,
                    MeanReversionSpeedWeekly,
                    VolatilityWeekly);
                return;
            }

            var observed = _historicalSeries.Select(p => p.PricePerKg).ToList();
            var diffs = new List<decimal>();
            for (var i = 1; i < observed.Count; i++)
                diffs.Add(Math.Abs(observed[i] - observed[i - 1]));

            var mean = observed.Average();
            var sampleStdDev = CalculateStdDev(observed);
            var avgWeeklyMove = diffs.Count == 0 ? 0m : diffs.Average();
            var minObserved = observed.Min();
            var maxObserved = observed.Max();
            var range = Math.Max(1m, maxObserved - minObserved);
            var normalizedReversion = 0.15m + (avgWeeklyMove / range) * 0.10m;

            LongTermMean = ClampToBand(mean, 75m, 100m); // Fish Pool FPI 2020-2025 avg range (lower during COVID, higher 2022-2024)
            MeanReversionSpeedWeekly = ClampToBand(normalizedReversion, WeeklyThetaMin, WeeklyThetaMax);
            VolatilityWeekly = ClampToBand(sampleStdDev, WeeklySigmaMin, WeeklySigmaMax);

            CalibrationSummary = string.Format(
                CultureInfo.InvariantCulture,
                "Calibrated from {0} observations: μ={1:F2} NOK/kg, θ={2:F2}/week, σ={3:F2} NOK/kg/√week (observed range {4:F2}-{5:F2})",
                _historicalSeries.Count,
                LongTermMean,
                MeanReversionSpeedWeekly,
                VolatilityWeekly,
                minObserved,
                maxObserved);
        }

        /// <summary>
        /// Advance market simulation by a number of days.
        /// Uses a daily-discretized Ornstein-Uhlenbeck process with seasonal Norwegian demand overlays.
        /// Integrates market intelligence events and competitor supply data.
        /// </summary>
        public void AdvanceDays(int days)
        {
            for (int d = 0; d < days; d++)
            {
                _simulationDay++;

                // Weekly intelligence tick (every 7 days)
                if (_simulationDay % DaysPerWeek == 0)
                {
                    Intelligence.AdvanceWeek(BasePricePerKg);
                    Channels.ResetWeeklyVolumes();

                    // Apply active news event impacts to supply/demand
                    var (supFactor, demFactor, priceShock) = Intelligence.GetActiveEventImpacts();
                    if (Math.Abs(supFactor - 1.0) > 0.001 || Math.Abs(demFactor - 1.0) > 0.001)
                    {
                        // Blend event impacts gradually (don't shock instantly)
                        GlobalSupplyIndex = GlobalSupplyIndex * 0.9 + (GlobalSupplyIndex * supFactor) * 0.1;
                        GlobalDemandIndex = GlobalDemandIndex * 0.9 + (GlobalDemandIndex * demFactor) * 0.1;
                    }
                    if (priceShock != 0m)
                    {
                        BasePricePerKg = ClampPrice(BasePricePerKg + priceShock * 0.15m);  // Gradual price shock absorption
                    }

                    // Competitor supply affects global supply index
                    var competitorSupply = Intelligence.GetTotalCompetitorSupplyTonnes();
                    var normalSupply = 31400.0;  // Sum of all base weekly harvests
                    if (normalSupply > 0)
                    {
                        var supplyDeviation = (competitorSupply - normalSupply) / normalSupply;
                        GlobalSupplyIndex *= 1.0 + supplyDeviation * 0.05;  // 5% sensitivity
                    }
                }

                var seasonalTarget = LongTermMean + GetSeasonalOffsetForSimulationDay(_simulationDay);
                var currentPrice = (double)BasePricePerKg;
                var thetaDaily = (double)MeanReversionSpeedWeekly / DaysPerWeek;
                var sigmaDaily = (double)VolatilityWeekly / Math.Sqrt(DaysPerWeek);

                double u1 = Math.Max(1e-10, _rng.NextDouble());
                double u2 = _rng.NextDouble();
                double normalShock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

                var drift = thetaDaily * ((double)seasonalTarget - currentPrice);
                var diffusion = sigmaDaily * normalShock;
                var supplyDemandEffect = (GlobalDemandIndex - GlobalSupplyIndex) * 2.25;

                var newPrice = currentPrice + drift + diffusion + supplyDemandEffect;
                BasePricePerKg = ClampPrice((decimal)newPrice);

                // Apply brand reputation premium
                PricePerKg = BasePricePerKg * _certificationMultiplier * Intelligence.PlayerReputation.PremiumMultiplier;

                _priceHistory.Enqueue(PricePerKg);
                if (_priceHistory.Count > MaxHistoryLength)
                    _priceHistory.Dequeue();

                WeeklyAvgPrice = _priceHistory.TakeLast(Math.Min(DaysPerWeek, _priceHistory.Count)).Average();

                if (_rng.NextDouble() < 0.02)
                    TriggerMarketShock();
            }

            OnPriceChanged?.Invoke(PricePerKg);
        }

        private decimal GetSeasonalOffsetForSimulationDay(int simulationDay)
        {
            int month = ((simulationDay / 30) % 12 + 12) % 12;
            return MonthlySeasonalOffsets[month];
        }

        private void TriggerMarketShock()
        {
            double roll = _rng.NextDouble();

            if (roll < 0.3)
            {
                GlobalSupplyIndex *= 0.95;
                OnMarketEvent?.Invoke("📰 Chile reports major algae bloom — global supply tightens");
            }
            else if (roll < 0.5)
            {
                GlobalSupplyIndex *= 1.03;
                OnMarketEvent?.Invoke("📰 Norwegian harvest volumes exceed expectations");
            }
            else if (roll < 0.7)
            {
                GlobalDemandIndex *= 1.04;
                OnMarketEvent?.Invoke("📰 Asian sushi market demand surges — salmon prices rise");
            }
            else if (roll < 0.85)
            {
                GlobalDemandIndex *= 0.96;
                OnMarketEvent?.Invoke("📰 EU imposes new tariffs on Norwegian salmon imports");
            }
            else
            {
                double currencyShift = (_rng.NextDouble() - 0.5) * 0.035;
                BasePricePerKg = ClampPrice(BasePricePerKg * (1.0m + (decimal)currencyShift));
                OnMarketEvent?.Invoke($"📰 NOK {(currencyShift > 0 ? "weakens" : "strengthens")} — export {(currencyShift > 0 ? "revenues up" : "revenues down")}");
            }

            GlobalSupplyIndex = 1.0 + (GlobalSupplyIndex - 1.0) * 0.95;
            GlobalDemandIndex = 1.0 + (GlobalDemandIndex - 1.0) * 0.95;
        }

        public void ApplyExternalShock(double supplyFactor, double demandFactor)
        {
            if (supplyFactor != 1.0) GlobalSupplyIndex *= supplyFactor;
            if (demandFactor != 1.0) GlobalDemandIndex *= demandFactor;

            GlobalSupplyIndex = Math.Clamp(GlobalSupplyIndex, 0.5, 2.0);
            GlobalDemandIndex = Math.Clamp(GlobalDemandIndex, 0.5, 2.0);

            OnPriceChanged?.Invoke(PricePerKg);
        }

        public void ApplyIndustrySignals(double biomassUtilization, double diseasePressure, double mortalityStress, double marketSupport)
        {
            var supplyFactor = 1.0;
            var demandFactor = 1.0;

            if (biomassUtilization > 0.90)
                supplyFactor *= 1.03;
            else if (biomassUtilization < 0.70)
                supplyFactor *= 0.98;

            if (diseasePressure > 1.10 || mortalityStress > 1.10)
                supplyFactor *= 0.97;

            if (marketSupport > 1.0)
                demandFactor *= 1.0 + ((marketSupport - 1.0) * 0.30);
            else if (marketSupport < 1.0)
                demandFactor *= 1.0 - ((1.0 - marketSupport) * 0.20);

            ApplyExternalShock(supplyFactor, demandFactor);
        }

        public void UpdateCertificationLevel(CertificationLevel level, decimal priceMultiplier)
        {
            _certificationMultiplier = priceMultiplier;
            _accessibleMarkets = GetAccessibleMarkets(level);
            PricePerKg = BasePricePerKg * _certificationMultiplier;

            OnPriceChanged?.Invoke(PricePerKg);
            OnMarketAccessChanged?.Invoke(_accessibleMarkets);
        }

        private HashSet<string> GetAccessibleMarkets(CertificationLevel level)
        {
            var markets = new HashSet<string> { "Domestic" };
            if (level >= CertificationLevel.BasicCompliance)
            {
                markets.Add("EU");
                markets.Add("USA");
                markets.Add("China");
            }
            if (level >= CertificationLevel.ASCCertified)
            {
                markets.Add("WholeFoods");
                markets.Add("Waitrose");
            }
            if (level >= CertificationLevel.OrganicCertified)
            {
                markets.Add("JapanPremium");
                markets.Add("MichelinRestaurants");
            }
            return markets;
        }

        public bool CanAccessMarket(string marketName) => _accessibleMarkets.Contains(marketName);
        public HashSet<string> GetAccessibleMarkets() => new(_accessibleMarkets);
        public decimal[] GetPriceHistory() => _priceHistory.ToArray();

        // ─── T77: Quality-aware selling ───

        /// <summary>
        /// Sell fish through a specific channel with quality grading applied.
        /// Convenience method that wires MarketChannelSystem to current spot price.
        /// </summary>
        public SaleResult SellFish(SalesChannelType channel, int volumeKg, FishBatchAttributes batchAttributes, ProductForm form = ProductForm.WholeRound)
        {
            var grading = QualityGrader.GradeFull(batchAttributes, form);

            // Record delivery for brand reputation
            Intelligence.PlayerReputation.RecordDelivery(grading.Grade, onTime: true, volumeKg);

            return Channels.Sell(channel, volumeKg, BasePricePerKg, grading);
        }

        /// <summary>
        /// Get price forecasts for player decision-making.
        /// </summary>
        public List<PriceForecast> GetPriceForecasts()
        {
            return Intelligence.GetForecasts(BasePricePerKg, LongTermMean, VolatilityWeekly);
        }

        public MarketStatus GetStatus() => new MarketStatus
        {
            SpotPrice = BasePricePerKg,
            EffectivePrice = PricePerKg,
            WeeklyAverage = WeeklyAvgPrice,
            SeasonalIndex = (double)(LongTermMean + GetSeasonalOffsetForSimulationDay(_simulationDay == 0 ? 1 : _simulationDay)) / (double)LongTermMean,
            SupplyIndex = GlobalSupplyIndex,
            DemandIndex = GlobalDemandIndex,
            CertificationMultiplier = _certificationMultiplier,
            AccessibleMarkets = _accessibleMarkets.Count,
            LongTermMean = LongTermMean,
            MeanReversionSpeedWeekly = MeanReversionSpeedWeekly,
            VolatilityWeekly = VolatilityWeekly,
            PriceFloor = PriceFloor,
            PriceCeiling = PriceCeiling,
            BrandReputation = Intelligence.PlayerReputation.Score,
            BrandPremiumMultiplier = Intelligence.PlayerReputation.PremiumMultiplier,
            ActiveNewsEvents = Intelligence.ActiveEvents.Count,
            CompetitorSupplyTonnesPerWeek = Intelligence.GetTotalCompetitorSupplyTonnes()
        };

        private static decimal CalculateStdDev(IReadOnlyList<decimal> values)
        {
            if (values.Count <= 1)
                return 0m;

            var mean = values.Average();
            var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
            return (decimal)Math.Sqrt((double)variance);
        }

        private static decimal ClampToBand(decimal value, decimal min, decimal max)
            => Math.Clamp(value, min, max);

        private static decimal ClampPrice(decimal value)
            => Math.Clamp(value, PriceFloor, PriceCeiling);
    }

    /// <summary>Market status snapshot for UI display.</summary>
    public struct MarketStatus
    {
        public decimal SpotPrice;
        public decimal EffectivePrice;
        public decimal WeeklyAverage;
        public double SeasonalIndex;
        public double SupplyIndex;
        public double DemandIndex;
        public decimal CertificationMultiplier;
        public int AccessibleMarkets;
        public decimal LongTermMean;
        public decimal MeanReversionSpeedWeekly;
        public decimal VolatilityWeekly;
        public decimal PriceFloor;
        public decimal PriceCeiling;
        // T77 additions
        public double BrandReputation;
        public decimal BrandPremiumMultiplier;
        public int ActiveNewsEvents;
        public double CompetitorSupplyTonnesPerWeek;
    }
}
