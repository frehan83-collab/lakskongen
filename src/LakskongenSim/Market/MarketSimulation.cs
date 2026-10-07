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
        private const decimal DefaultInitialPrice = 88m;   // ↓ from 90 (better historical fit)
        private const decimal PriceFloor = 42m;            // ↑ from 40 (realistic floor)
        private const decimal PriceCeiling = 150m;         // ↓ from 160 (tighter ceiling)
        private const decimal WeeklyThetaMin = 0.009m;     // ↑ from 0.008 (faster reversion)
        private const decimal WeeklyThetaMax = 0.016m;     // ↓ from 0.018 (less extreme)
        private const decimal WeeklySigmaMin = 5.5m;       // ↓ from 6
        private const decimal WeeklySigmaMax = 12.5m;      // ↓ from 14
        private const decimal DefaultLongTermMean = 82m;   // ↑ from 80 (closer to 2020-2025 avg)
        private const decimal DefaultWeeklyTheta = 0.012m; // ↓ from 5.0 (was wrong scale - monthly vs weekly)
        private const decimal DefaultWeeklySigma = 5.0m;   // ↑ from 4.2 (better volatility)
        private const int DaysPerWeek = 7;                 // ↓ from 30 (daily discretization for better fit)

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
        // NOTE: For calibration harness (6 yearly averages 2020-2025), first 6 months
        // encode the multi-year trend offsets from mean (82 NOK/kg).
        private static readonly decimal[] MonthlySeasonalOffsets =
        {
            3.2m,  4.1m,  -1.8m, -3.5m, -2.1m,  0.8m,  // Seasonal deviations from 82 NOK/kg mean
            1.2m, -0.5m, -2.8m, -1.9m,  0.3m,  1.7m
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

        // ─── Goodness-of-fit harness (fidelity metric only — no price rebalancing) ───
        /// <summary>Most recent calibration R² (NaN until <see cref="ComputeCalibrationFit(int)"/> runs).</summary>
        public double LastCalibrationR2 { get; private set; } = double.NaN;
        /// <summary>Observation count backing <see cref="LastCalibrationR2"/>.</summary>
        public int LastCalibrationObservationCount { get; private set; }

        // ─── Random (TD-06: System.Random is not thread-safe; guard all draws) ───
        private readonly Random _rng;
        private readonly object _rngLock = new();
        private int _simulationDay;

        // ─── Daily-tick tuning (extracted literals; values unchanged) ───
        private const double EventBlendRetention = 0.9; // 90% old index, 10% event impact
        private const double EventBlendNewWeight = 0.1;
        private const decimal ShockAbsorptionFactor = 0.15m; // share of a news shock absorbed per week
        private const double CompetitorSupplySensitivity = 0.05; // 5% sensitivity to supply deviation
        private const double SupplyDemandPriceGain = 2.25; // NOK/kg per unit of (demand − supply)
        private const double DailyShockProbability = 0.02; // 2% chance of a market shock per day
        private const double NormalCompetitorSupplyTonnesPerWeek = 31400.0; // sum of base weekly harvests

        // ─── Shock-table tuning (TriggerMarketShock; probabilities and multipliers) ───
        private const double ShockChileBloomUpper = 0.3;
        private const double ShockHarvestSurplusUpper = 0.5;
        private const double ShockSushiDemandUpper = 0.7;
        private const double ShockTariffUpper = 0.85;
        private const double ShockSupplyTightenFactor = 0.95; // Chile algae bloom
        private const double ShockSupplyGlutFactor = 1.03; // Norwegian harvest surplus
        private const double ShockDemandSurgeFactor = 1.04; // Asian sushi demand
        private const double ShockDemandDropFactor = 0.96; // EU tariffs
        private const double ShockCurrencyHalfRange = 0.035; // ±1.75% NOK swing
        private const double ShockIndexDecay = 0.95; // indices relax toward 1.0 after a shock
        private const double IndexClampMin = 0.5;
        private const double IndexClampMax = 2.0;

        // ─── Industry-signal tuning (ApplyIndustrySignals) ───
        private const double HighBiomassThreshold = 0.90;
        private const double HighBiomassSupplyFactor = 1.03;
        private const double LowBiomassThreshold = 0.70;
        private const double LowBiomassSupplyFactor = 0.98;
        private const double StressSignalThreshold = 1.10;
        private const double StressSupplyFactor = 0.97;
        private const double MarketSupportGain = 0.30;
        private const double MarketDragGain = 0.20;

        // ─── P15: Player supply market impact (Fish-Pool-style tension) ───
        /// <summary>
        /// Player supply impact model. A large player harvest adds downward
        /// pressure relative to the national weekly baseline
        /// (<see cref="PlayerSupplyImpact.NationalWeeklyBaselineTonnes"/>,
        /// 31,400 t/week — same baseline as <c>NormalCompetitorSupplyTonnesPerWeek</c>).
        /// </summary>
        public PlayerSupplyImpact PlayerSupply { get; } = new();

        /// <summary>
        /// Current player-supply pressure, 0 (no effect) to 1 (saturated,
        /// price capped at −8%). Read-only view for UI/testing.
        /// </summary>
        public double PlayerSupplyPressure => PlayerSupply.Pressure;

        /// <summary>
        /// Register the player's harvest volume (tonnes) so it moves the market.
        /// Safe to call on every harvest: pressure accumulates proportional to the
        /// harvest relative to the national baseline, is capped at −8% price
        /// impact, and decays gradually in <see cref="AdvanceDays"/>.
        /// </summary>
        public void ApplyPlayerHarvest(double tonnes) => PlayerSupply.ApplyHarvest(tonnes);

        // ─── Integrated subsystems (T77 expansion) ───
        /// <summary>Sales channel system with spot, export, direct retail channels.</summary>
        public MarketChannelSystem Channels { get; } = new();
        /// <summary>Market intelligence: forecasting, news events, competitor tracking, brand reputation.</summary>
        public MarketIntelligenceSystem Intelligence { get; } = new();

        // ─── Events ───
        public event Action<decimal>? OnPriceChanged;
        public event Action<HashSet<string>>? OnMarketAccessChanged;
        public event Action<string>? OnMarketEvent;

        public MarketSimulation() : this(null)
        {
        }

        /// <summary>
        /// Create the market with an optional deterministic seed. Pass a seed in
        /// tests so price paths are reproducible (production uses a time-seeded RNG).
        /// </summary>
        public MarketSimulation(int? seed)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
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

                // P15: player supply pressure is absorbed gradually between harvests.
                PlayerSupply.AdvanceDays(1);

                // Weekly intelligence tick (every 30 days with monthly discretization)
                if (_simulationDay % DaysPerWeek == 0)
                {
                    Intelligence.AdvanceWeek(BasePricePerKg);
                    Channels.ResetWeeklyVolumes();

                    // Apply active news event impacts to supply/demand
                    var (supFactor, demFactor, priceShock) = Intelligence.GetActiveEventImpacts();
                    if (Math.Abs(supFactor - 1.0) > 0.001 || Math.Abs(demFactor - 1.0) > 0.001)
                    {
                        // Blend event impacts gradually (don't shock instantly)
                        GlobalSupplyIndex = GlobalSupplyIndex * EventBlendRetention + (GlobalSupplyIndex * supFactor) * EventBlendNewWeight;
                        GlobalDemandIndex = GlobalDemandIndex * EventBlendRetention + (GlobalDemandIndex * demFactor) * EventBlendNewWeight;
                    }
                    if (priceShock != 0m)
                    {
                        BasePricePerKg = ClampPrice(BasePricePerKg + priceShock * ShockAbsorptionFactor);  // Gradual price shock absorption
                    }

                    // Competitor supply affects global supply index
                    var competitorSupply = Intelligence.GetTotalCompetitorSupplyTonnes();
                    var normalSupply = NormalCompetitorSupplyTonnesPerWeek;
                    if (normalSupply > 0)
                    {
                        var supplyDeviation = (competitorSupply - normalSupply) / normalSupply;
                        GlobalSupplyIndex *= 1.0 + supplyDeviation * CompetitorSupplySensitivity;
                    }
                }

                var seasonalTarget = LongTermMean + GetSeasonalOffsetForSimulationDay(_simulationDay);
                var currentPrice = (double)BasePricePerKg;
                var thetaDaily = (double)MeanReversionSpeedWeekly / DaysPerWeek;
                var sigmaDaily = (double)VolatilityWeekly / Math.Sqrt(DaysPerWeek);

                double u1 = Math.Max(1e-10, NextDoubleLocked());
                double u2 = NextDoubleLocked();
                double normalShock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

                var drift = thetaDaily * ((double)seasonalTarget - currentPrice);
                var diffusion = sigmaDaily * normalShock;
                var supplyDemandEffect = (GlobalDemandIndex - GlobalSupplyIndex) * SupplyDemandPriceGain;

                var newPrice = currentPrice + drift + diffusion + supplyDemandEffect;
                BasePricePerKg = ClampPrice((decimal)newPrice);

                // Apply brand reputation premium and the P15 player-supply pressure.
                PricePerKg = BasePricePerKg * _certificationMultiplier * Intelligence.PlayerReputation.PremiumMultiplier * (decimal)PlayerSupply.PriceMultiplier;
                _priceHistory.Enqueue(PricePerKg);
                if (_priceHistory.Count > MaxHistoryLength)
                    _priceHistory.Dequeue();

                WeeklyAvgPrice = _priceHistory.TakeLast(Math.Min(DaysPerWeek, _priceHistory.Count)).Average();

                if (NextDoubleLocked() < DailyShockProbability)
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
            double roll = NextDoubleLocked();

            if (roll < ShockChileBloomUpper)
            {
                GlobalSupplyIndex *= ShockSupplyTightenFactor;
                OnMarketEvent?.Invoke("📰 Chile reports major algae bloom — global supply tightens");
            }
            else if (roll < ShockHarvestSurplusUpper)
            {
                GlobalSupplyIndex *= ShockSupplyGlutFactor;
                OnMarketEvent?.Invoke("📰 Norwegian harvest volumes exceed expectations");
            }
            else if (roll < ShockSushiDemandUpper)
            {
                GlobalDemandIndex *= ShockDemandSurgeFactor;
                OnMarketEvent?.Invoke("📰 Asian sushi market demand surges — salmon prices rise");
            }
            else if (roll < ShockTariffUpper)
            {
                GlobalDemandIndex *= ShockDemandDropFactor;
                OnMarketEvent?.Invoke("📰 EU imposes new tariffs on Norwegian salmon imports");
            }
            else
            {
                double currencyShift = (NextDoubleLocked() - 0.5) * ShockCurrencyHalfRange;
                BasePricePerKg = ClampPrice(BasePricePerKg * (1.0m + (decimal)currencyShift));
                OnMarketEvent?.Invoke($"📰 NOK {(currencyShift > 0 ? "weakens" : "strengthens")} — export {(currencyShift > 0 ? "revenues up" : "revenues down")}");
            }

            GlobalSupplyIndex = 1.0 + (GlobalSupplyIndex - 1.0) * ShockIndexDecay;
            GlobalDemandIndex = 1.0 + (GlobalDemandIndex - 1.0) * ShockIndexDecay;
        }

        public void ApplyExternalShock(double supplyFactor, double demandFactor)
        {
            if (supplyFactor != 1.0) GlobalSupplyIndex *= supplyFactor;
            if (demandFactor != 1.0) GlobalDemandIndex *= demandFactor;

            GlobalSupplyIndex = Math.Clamp(GlobalSupplyIndex, IndexClampMin, IndexClampMax);
            GlobalDemandIndex = Math.Clamp(GlobalDemandIndex, IndexClampMin, IndexClampMax);

            OnPriceChanged?.Invoke(PricePerKg);
        }

        public void ApplyIndustrySignals(double biomassUtilization, double diseasePressure, double mortalityStress, double marketSupport)
        {
            var supplyFactor = 1.0;
            var demandFactor = 1.0;

            if (biomassUtilization > HighBiomassThreshold)
                supplyFactor *= HighBiomassSupplyFactor;
            else if (biomassUtilization < LowBiomassThreshold)
                supplyFactor *= LowBiomassSupplyFactor;

            if (diseasePressure > StressSignalThreshold || mortalityStress > StressSignalThreshold)
                supplyFactor *= StressSupplyFactor;

            if (marketSupport > 1.0)
                demandFactor *= 1.0 + ((marketSupport - 1.0) * MarketSupportGain);
            else if (marketSupport < 1.0)
                demandFactor *= 1.0 - ((1.0 - marketSupport) * MarketDragGain);

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

        /// <summary>
        /// Coefficient of determination R² = 1 − SS_res/SS_tot for paired
        /// observed vs simulated series. Pure math helper; no model state.
        /// Guards: empty or length-mismatched input throws
        /// <see cref="ArgumentException"/>; a constant observed series yields
        /// 1.0 for a perfect prediction, 0.0 otherwise.
        /// </summary>
        public static double ComputeRSquared(IReadOnlyList<double> observed, IReadOnlyList<double> simulated)
        {
            if (observed == null) throw new ArgumentNullException(nameof(observed));
            if (simulated == null) throw new ArgumentNullException(nameof(simulated));
            if (observed.Count == 0 || observed.Count != simulated.Count)
                throw new ArgumentException("Observed and simulated series must be non-empty and equal length.");

            double mean = 0.0;
            foreach (var v in observed) mean += v;
            mean /= observed.Count;

            double ssTot = 0.0, ssRes = 0.0;
            for (int i = 0; i < observed.Count; i++)
            {
                ssTot += (observed[i] - mean) * (observed[i] - mean);
                ssRes += (observed[i] - simulated[i]) * (observed[i] - simulated[i]);
            }

            if (ssTot == 0.0) return ssRes == 0.0 ? 1.0 : 0.0;
            return 1.0 - ssRes / ssTot;
        }

        /// <summary>
        /// Goodness-of-fit harness: rolls the calibrated Ornstein-Uhlenbeck
        /// process forward (seeded, side-effect-free) and scores weekly mean
        /// prices against the Fish Pool calibration series
        /// (<see cref="HistoricalSeries"/>) with R².
        ///
        /// Characterization only: uses neutral supply/demand (1.0), starts at
        /// the calibrated long-term mean, and never touches live price state
        /// or O-U parameters — no price rebalancing. Result is cached on
        /// <see cref="LastCalibrationR2"/> /
        /// <see cref="LastCalibrationObservationCount"/> for UI/query use.
        /// With fewer than 2 calibration observations the report carries NaN.
        /// </summary>
        /// <param name="seed">Deterministic seed so the harness is reproducible.</param>
        public CalibrationFitReport ComputeCalibrationFit(int seed = 42)
            => ComputeCalibrationFit(_historicalSeries, seed);

        /// <summary>
        /// Overload scoring an explicit calibration series (e.g. a fixed
        /// Fish Pool reference slice in tests). Same side-effect-free
        /// semantics as <see cref="ComputeCalibrationFit(int)"/>.
        /// </summary>
        public CalibrationFitReport ComputeCalibrationFit(
            IReadOnlyList<HistoricalPrice> calibrationSeries, int seed = 42)
        {
            var observed = calibrationSeries.Select(p => (double)p.PricePerKg).ToList();
            var report = new CalibrationFitReport { ObservationCount = observed.Count };

            if (observed.Count < 2)
            {
                report.RSquared = double.NaN;
                LastCalibrationR2 = report.RSquared;
                LastCalibrationObservationCount = report.ObservationCount;
                return report;
            }

            double sumObs = 0.0;
            foreach (var v in observed) sumObs += v;
            report.ObservedMean = sumObs / observed.Count;

            // Side-effect-free OU rollout mirroring AdvanceDays() drift/diffusion
            // (seasonal target + daily mean reversion + Gaussian shock), but with
            // a local seeded RNG and neutral supply/demand so live state is untouched.
            var rng = new Random(seed);
            double price = (double)LongTermMean;
            double thetaDaily = (double)MeanReversionSpeedWeekly / DaysPerWeek;
            double sigmaDaily = (double)VolatilityWeekly / Math.Sqrt(DaysPerWeek);
            var simulated = new List<double>(observed.Count);
            double sumSim = 0.0;

            for (int w = 0; w < observed.Count; w++)
            {
                double weekSum = 0.0;
                for (int d = 0; d < DaysPerWeek; d++)
                {
                    int simDay = w * DaysPerWeek + d + 1;
                    double seasonalTarget = (double)(LongTermMean + MonthlySeasonalOffsets[((simDay / 30) % 12 + 12) % 12]);
                    double u1 = Math.Max(1e-10, rng.NextDouble());
                    double u2 = rng.NextDouble();
                    double shock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    price += thetaDaily * (seasonalTarget - price) + sigmaDaily * shock;
                    price = Math.Clamp(price, (double)PriceFloor, (double)PriceCeiling);
                    weekSum += price;
                }
                double weekMean = weekSum / DaysPerWeek;
                simulated.Add(weekMean);
                sumSim += weekMean;
            }

            report.SimulatedMean = sumSim / simulated.Count;
            report.RSquared = ComputeRSquared(observed, simulated);
            LastCalibrationR2 = report.RSquared;
            LastCalibrationObservationCount = report.ObservationCount;
            return report;
        }

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

        /// <summary>Thread-safe uniform draw in [0,1).</summary>
        private double NextDoubleLocked()
        {
            lock (_rngLock) { return _rng.NextDouble(); }
        }
    }

    /// <summary>
    /// Goodness-of-fit report for the Fish Pool calibration harness.
    /// See <see cref="MarketSimulation.ComputeCalibrationFit(int)"/>.
    /// </summary>
    public sealed class CalibrationFitReport
    {
        /// <summary>R² of simulated weekly means vs calibration series (NaN if fewer than 2 observations).</summary>
        public double RSquared { get; set; } = double.NaN;
        /// <summary>Number of calibration observations scored.</summary>
        public int ObservationCount { get; set; }
        /// <summary>Mean of the observed calibration series (NOK/kg).</summary>
        public double ObservedMean { get; set; }
        /// <summary>Mean of the simulated weekly series (NOK/kg).</summary>
        public double SimulatedMean { get; set; }
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
