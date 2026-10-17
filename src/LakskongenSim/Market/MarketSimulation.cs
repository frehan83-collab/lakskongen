using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LakskongenSim.Calibration;
using LakskongenSim.Sustainability;

using LakskongenSim.Core;
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
    public partial class MarketSimulation
    {
// ─── Calibrated to Fish Pool FPI 2020-2025 data ───
        // Live values are sourced from CalibrationRegistry.Market (single source of
        // truth). The constants below are only fallbacks for a registry that fails
        // to load; Initialize() overwrites every one of them from the registry.
        private const decimal DefaultInitialPrice = 88m;   // ↓ from 90 (better historical fit)
        private const decimal DefaultPriceFloor = 42m;     // ↑ from 40 (realistic floor)
        private const decimal DefaultPriceCeiling = 150m;  // ↓ from 160 (tighter ceiling)
        private const decimal WeeklyThetaMin = 0.009m;     // ↑ from 0.008 (faster reversion)
        private const decimal WeeklyThetaMax = 0.016m;     // ↓ from 0.018 (less extreme)
        private const decimal WeeklySigmaMin = 5.5m;       // ↓ from 6
        private const decimal WeeklySigmaMax = 12.5m;      // ↓ from 14
        private const decimal DefaultLongTermMean = 82m;   // ↑ from 80 (closer to 2020-2025 avg)
        private const decimal DefaultWeeklyTheta = 0.012m; // ↓ from 5.0 (was wrong scale - monthly vs weekly)
        private const decimal DefaultWeeklySigma = 5.0m;   // ↑ from 4.2 (better volatility)
        private const int DefaultDaysPerWeek = 7;          // ↓ from 30 (daily discretization for better fit)

        // ─── Live calibration (sourced from CalibrationRegistry.Market) ───
        private decimal _initialPrice = DefaultInitialPrice;
        private decimal _priceFloor = DefaultPriceFloor;
        private decimal _priceCeiling = DefaultPriceCeiling;
        private int _daysPerWeek = DefaultDaysPerWeek;
        private decimal[] _monthlySeasonalOffsets =
        {
            3.2m, 4.1m, -1.8m, -3.5m, -2.1m, 0.8m,
            1.2m, -0.5m, -2.8m, -1.9m, 0.3m, 1.7m
        };

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

        /// <summary>
        /// Scenario market rule: scales daily price noise and the market-shock
        /// probability (an "easy market" halves both). Set from the active
        /// scenario on start and on load, so it is not saved.
        /// </summary>
        public double VolatilityScale { get; set; } = 1.0;
        public decimal PriceFloorPerKg => _priceFloor;
        public decimal PriceCeilingPerKg => _priceCeiling;
        public IReadOnlyList<HistoricalPrice> HistoricalSeries => _historicalSeries;

        /// <summary>Persistence (Phase 1 wave 6): the full price-trajectory
        /// state must survive a save or post-load prices diverge.</summary>
        internal (decimal Base, decimal Price, decimal WeeklyAvg, decimal LongMean, decimal MeanRev, decimal Vol,
            double SupplyIdx, double DemandIdx, decimal CertMult, double LastR2, int LastObs, int SimDay,
            System.Collections.Generic.List<HistoricalPrice> History) ExportState()
            => (BasePricePerKg, PricePerKg, WeeklyAvgPrice, LongTermMean, MeanReversionSpeedWeekly, VolatilityWeekly,
                GlobalSupplyIndex, GlobalDemandIndex, _certificationMultiplier, LastCalibrationR2, LastCalibrationObservationCount,
                _simulationDay, new System.Collections.Generic.List<HistoricalPrice>(_historicalSeries));

        internal void ImportState((decimal Base, decimal Price, decimal WeeklyAvg, decimal LongMean, decimal MeanRev, decimal Vol,
            double SupplyIdx, double DemandIdx, decimal CertMult, double LastR2, int LastObs, int SimDay,
            System.Collections.Generic.List<HistoricalPrice> History) s)
        {
            BasePricePerKg = s.Base; PricePerKg = s.Price; WeeklyAvgPrice = s.WeeklyAvg;
            LongTermMean = s.LongMean; MeanReversionSpeedWeekly = s.MeanRev; VolatilityWeekly = s.Vol;
            GlobalSupplyIndex = s.SupplyIdx; GlobalDemandIndex = s.DemandIdx;
            _certificationMultiplier = s.CertMult; LastCalibrationR2 = s.LastR2;
            LastCalibrationObservationCount = s.LastObs; _simulationDay = s.SimDay;
            _historicalSeries.Clear(); _historicalSeries.AddRange(s.History);
        }
        public string CalibrationSummary { get; private set; } = "Using default market calibration";

        // ─── Seasonal parameters (Norwegian/global salmon market) ───
        // Sourced from CalibrationRegistry.Market.MonthlySeasonalOffsets in
        // Initialize(); the field default above mirrors the registry.
        // - Jan/Feb: Chinese New Year demand spike (Jan peak)
        // - Mar-May: post-winter recovery, Easter demand
        // - Jun-Aug: BBQ/grilling season peak (strongest seasonal signal)
        // - Sep-Oct: post-summer dip as harvest volumes increase
        // - Nov-Dec: Christmas/holiday demand recovery

        // ─── Supply/demand ───
        public double GlobalSupplyIndex { get; private set; } = 1.0;
        public double GlobalDemandIndex { get; private set; } = 1.0;

        // ─── Market access ───
        private HashSet<string> _accessibleMarkets = new() { "Domestic", "EU", "USA" };
        private decimal _certificationMultiplier = 1.0m;
        private decimal _researchPriceMultiplier = 1.0m;

        // ─── Price history (for charts/analytics) ───
        private readonly Queue<decimal> _priceHistory = new();
        private const int MaxHistoryLength = 365;

        /// <summary>
        /// The daily realized prices the weekly mean and the price chart read
        /// (at most a year), oldest first. Saved since F2-05: a loaded game
        /// started the queue afresh, so its weekly mean was the mean of one or
        /// two days for a week and the chart lost its history.
        /// </summary>
        internal List<decimal> ExportDailyPrices() => _priceHistory.ToList();

        /// <summary>Restore the daily prices of a save (the newest <see cref="MaxHistoryLength"/>).</summary>
        internal void ImportDailyPrices(IEnumerable<decimal> prices)
        {
            _priceHistory.Clear();
            foreach (var price in prices.TakeLast(MaxHistoryLength))
                _priceHistory.Enqueue(price);
        }

        // ─── Calibration data ───
        private readonly List<HistoricalPrice> _historicalSeries = new();

        // ─── Goodness-of-fit harness (fidelity metric only — no price rebalancing) ───
        /// <summary>Most recent calibration R² (NaN until <see cref="ComputeCalibrationFit(int)"/> runs).</summary>
        public double LastCalibrationR2 { get; private set; } = double.NaN;
        /// <summary>Observation count backing <see cref="LastCalibrationR2"/>.</summary>
        public int LastCalibrationObservationCount { get; private set; }

        // ─── Random ───
        /// <summary>Named RNG stream for market simulation (O-U process, shocks, seasonality).</summary>
        private readonly SimRngStream _rng;

        /// <summary>Persistence (Phase 1 wave 6): the shock-window countdown
        /// and the decaying supply/demand offset decide whether the
        /// reference-band clamp engages — must survive a save.</summary>


        internal (int ShockDays, double SupplyOffset) ExportShockState()
            => (_shockDaysRemaining, _supplyDemandOffsetNok);

        internal void ImportShockState((int ShockDays, double SupplyOffset) s)
        {
            _shockDaysRemaining = s.ShockDays;
            _supplyDemandOffsetNok = s.SupplyOffset;
        }
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
        // Daily relaxation of supply/demand indices toward neutral 1.0 (half-life ~35d)
        // and a bounded per-day supply/demand price contribution, so a persistent
        // news/competitor bias cannot compound into a ceiling-climbing drift.
        private const double IndexMeanReversionDaily = 0.02;
        private const double MaxSupplyDemandEffectNok = 4.0;
        // Supply/demand imbalance is a transient price offset that decays each
        // day, preventing a persistent index bias from compounding to the ceiling.
        private const double SupplyDemandOffsetDecay = 0.90;
        private double _supplyDemandOffsetNok = 0.0;

        // Soft normal-play band around the long-term mean (the 74-90 NOK/kg
        // reference band = 82 ±8). Outside a shock window the spot is clamped to
        // this band; shocks open a temporary window during which the wider
        // floor/ceiling applies so they can land.
        private const double NormalBandHalfWidthNok = 8.0;
        private const double RealizedBandUpperHalfWidthNok = 6.0;
        private const int ShockWindowDays = 30;
        private int _shockDaysRemaining = 0;

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

        public MarketSimulation(SimRngStream rng, SimRngStream? intelligenceRng = null)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Intelligence.Reseed(intelligenceRng ?? SimRng.CreateUnseeded().Stream(SimRngStreams.Intelligence));
            Initialize();
        }

        public MarketSimulation() : this(SimRng.CreateUnseeded().Stream(SimRngStreams.Market))
        {
        }

        /// <summary>
        /// Apply the calibrated market parameters and initialize live price state.
        /// Safe to call multiple times.
        /// </summary>
        public void Initialize()
        {
            CalibrateOUParameters();
            ApplyRegistryCalibration();

            // Start at the calibrated long-term mean so the price is centred on
            // the reference band from day one (the O-U target is the mean, not
            // the registry's illustrative initial print).
            BasePricePerKg = ClampPrice(LongTermMean);
            PricePerKg = BasePricePerKg * _certificationMultiplier;
            WeeklyAvgPrice = PricePerKg;

            GlobalSupplyIndex = 1.0;
            GlobalDemandIndex = 1.0;
            _supplyDemandOffsetNok = 0.0;
            _shockDaysRemaining = 0;

            _priceHistory.Clear();
            _priceHistory.Enqueue(PricePerKg);
            _simulationDay = 0;
        }

        /// <summary>
        /// Push <see cref="CalibrationRegistry.Market"/> into the live O-U model.
        /// The registry is the single source of truth (long-term mean ~82 NOK/kg,
        /// weekly theta/sigma and the 42-150 price band); without this the model
        /// drifted to its hardcoded ceiling on an unclamped supply/demand bias.
        /// </summary>
        private void ApplyRegistryCalibration()
        {
            var m = CalibrationRegistry.Default.Market;
            LongTermMean = m.LongTermMeanNOKPerKg;
            MeanReversionSpeedWeekly = m.MeanReversionSpeedWeekly;
            VolatilityWeekly = m.VolatilityWeekly;
            _priceFloor = m.PriceFloorNOKPerKg;
            _priceCeiling = m.PriceCeilingNOKPerKg;
            _initialPrice = m.InitialPriceNOKPerKg;
            _daysPerWeek = m.DaysPerWeek > 0 ? m.DaysPerWeek : DefaultDaysPerWeek;
            if (m.MonthlySeasonalOffsets is { Length: 12 })
                _monthlySeasonalOffsets = m.MonthlySeasonalOffsets;
        }

        /// <summary>
        /// Load the bundled historical price anchors for the goodness-of-fit
        /// harness. Live O-U parameters come from <see cref="CalibrationRegistry.Market"/>
        /// (via <see cref="ApplyRegistryCalibration"/>), not from this file.
        /// </summary>
        public void CalibrateOUParameters()
        {
            _historicalSeries.Clear();
            _historicalSeries.AddRange(HistoricalPrice.LoadFromMarkdown(HistoricalPriceMarkdownPath));

            var m = CalibrationRegistry.Default.Market;
            CalibrationSummary = _historicalSeries.Count == 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Registry calibration: μ={0:F2}, θ={1:F2}, σ={2:F2}",
                    m.LongTermMeanNOKPerKg,
                    m.MeanReversionSpeedWeekly,
                    m.VolatilityWeekly)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Registry calibration: μ={0:F2}, θ={1:F2}, σ={2:F2} ({3} historical obs loaded)",
                    m.LongTermMeanNOKPerKg,
                    m.MeanReversionSpeedWeekly,
                    m.VolatilityWeekly,
                    _historicalSeries.Count);
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
                if (_simulationDay % _daysPerWeek == 0)
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

                // Supply/demand indices relax toward neutral (1.0) so a persistent
                // news/competitor bias cannot compound into a ceiling-climbing
                // drift. Without this the O-U mean reversion is too weak to hold
                // the price near the calibrated ~82 NOK/kg mean.
                GlobalSupplyIndex = Math.Clamp(
                    GlobalSupplyIndex + (1.0 - GlobalSupplyIndex) * IndexMeanReversionDaily,
                    IndexClampMin, IndexClampMax);
                GlobalDemandIndex = Math.Clamp(
                    GlobalDemandIndex + (1.0 - GlobalDemandIndex) * IndexMeanReversionDaily,
                    IndexClampMin, IndexClampMax);

                var seasonalTarget = LongTermMean + GetSeasonalOffsetForSimulationDay(_simulationDay);
                var currentPrice = (double)BasePricePerKg;
                var thetaDaily = (double)MeanReversionSpeedWeekly / _daysPerWeek;
                var sigmaDaily = (double)VolatilityWeekly / Math.Sqrt(_daysPerWeek) * VolatilityScale;

                double u1 = Math.Max(1e-10, _rng.NextDouble());
                double u2 = _rng.NextDouble();
                double normalShock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

                var drift = thetaDaily * ((double)seasonalTarget - currentPrice);
                var diffusion = sigmaDaily * normalShock;
                // Bounded supply/demand pressure applied as a *decaying* price
                // offset (not a per-day additive drift): neutral play therefore
                // contributes no permanent drift, while shocks still move price.
                _supplyDemandOffsetNok = _supplyDemandOffsetNok * SupplyDemandOffsetDecay
                    + Math.Clamp(
                        (GlobalDemandIndex - GlobalSupplyIndex) * SupplyDemandPriceGain,
                        -MaxSupplyDemandEffectNok, MaxSupplyDemandEffectNok);

                var newPrice = currentPrice + drift + diffusion + _supplyDemandOffsetNok;

                // Normal-play band (reference 74-90 NOK/kg = mean ±8). Outside a
                // shock window both the spot and the realized price are clamped
                // into the band so weak mean reversion cannot park at the 150
                // ceiling; during a shock window the wider floor/ceiling
                // (42-150) applies so shocks can land.
                bool shockActive = _shockDaysRemaining > 0;
                if (shockActive) _shockDaysRemaining--;
                double bandLow = (double)LongTermMean - NormalBandHalfWidthNok;
                double bandHigh = (double)LongTermMean + NormalBandHalfWidthNok;
                if (!shockActive)
                    newPrice = Math.Clamp(newPrice, bandLow, bandHigh);
                BasePricePerKg = ClampPrice((decimal)newPrice);

                // Apply brand reputation premium, P15 player-supply pressure, AND research/vertical integration bonus.
                decimal rawPrice = BasePricePerKg * _certificationMultiplier * _researchPriceMultiplier * Intelligence.PlayerReputation.PremiumMultiplier * (decimal)PlayerSupply.PriceMultiplier;
                if (!shockActive)
                {
                    // Certification/brand premiums are applied on top of the spot
                    // band; cap the realized price a little inside the 90 ceiling
                    // so a certified farm sits in-band rather than pinned at 90.
                    rawPrice = Math.Clamp(rawPrice,
                        (decimal)bandLow,
                        LongTermMean + (decimal)RealizedBandUpperHalfWidthNok);
                }
                PricePerKg = rawPrice;
                _priceHistory.Enqueue(PricePerKg);
                if (_priceHistory.Count > MaxHistoryLength)
                    _priceHistory.Dequeue();

                WeeklyAvgPrice = _priceHistory.TakeLast(Math.Min(_daysPerWeek, _priceHistory.Count)).Average();

                if (_rng.NextDouble() < DailyShockProbability * VolatilityScale)
                    TriggerMarketShock();
            }

            OnPriceChanged?.Invoke(PricePerKg);
        }

        private decimal GetSeasonalOffsetForSimulationDay(int simulationDay)
        {
            int month = LakskongenSim.Core.SimCalendar.MonthIndex(simulationDay);
            return _monthlySeasonalOffsets[month];
        }

        private void TriggerMarketShock()
        {
            double roll = _rng.NextDouble();

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
                double currencyShift = (_rng.NextDouble() - 0.5) * ShockCurrencyHalfRange;
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

            // Only material external shocks open a shock window; the engine's
            // routine daily profitability nudge (±4%) must not disable the band.
            if (Math.Abs(supplyFactor - 1.0) > 0.05 || Math.Abs(demandFactor - 1.0) > 0.05)
                _shockDaysRemaining = Math.Max(_shockDaysRemaining, ShockWindowDays);

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
            PricePerKg = BasePricePerKg * _certificationMultiplier * _researchPriceMultiplier;

            OnPriceChanged?.Invoke(PricePerKg);
            OnMarketAccessChanged?.Invoke(_accessibleMarkets);
        }

        /// <summary>
        /// Set the research + vertical-integration price multiplier (floored at
        /// 1). It is applied by the next <see cref="AdvanceDays"/> inside the
        /// realized price band, like the certification and brand premiums; the
        /// engine sets it every day before advancing the market (review F1003).
        /// </summary>
        public void SetResearchPriceMultiplier(double multiplier)
            => _researchPriceMultiplier = (decimal)Math.Max(1.0, multiplier);

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
            // a local seeded stream and neutral supply/demand so live state is
            // untouched. SplitMix core: cross-runtime stable (System.Random is
            // implementation-defined).
            var rng = new SimRng(seed).Stream(SimRngStreams.Market, "calibration");
            double price = (double)LongTermMean;
            double thetaDaily = (double)MeanReversionSpeedWeekly / _daysPerWeek;
            double sigmaDaily = (double)VolatilityWeekly / Math.Sqrt(_daysPerWeek);
            var simulated = new List<double>(observed.Count);
            double sumSim = 0.0;

            for (int w = 0; w < observed.Count; w++)
            {
                double weekSum = 0.0;
                for (int d = 0; d < _daysPerWeek; d++)
                {
                    int simDay = w * _daysPerWeek + d + 1;
                    double seasonalTarget = (double)(LongTermMean + _monthlySeasonalOffsets[LakskongenSim.Core.SimCalendar.MonthIndex(simDay)]);
                    double u1 = Math.Max(1e-10, rng.NextDouble());
                    double u2 = rng.NextDouble();
                    double shock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    price += thetaDaily * (seasonalTarget - price) + sigmaDaily * shock;
                    price = Math.Clamp(price, (double)_priceFloor, (double)_priceCeiling);
                    weekSum += price;
                }
                double weekMean = weekSum / _daysPerWeek;
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
            PriceFloor = _priceFloor,
            PriceCeiling = _priceCeiling,
            BrandReputation = Intelligence.PlayerReputation.Score,
            BrandPremiumMultiplier = Intelligence.PlayerReputation.PremiumMultiplier,
            ActiveNewsEvents = Intelligence.ActiveEvents.Count,
            CompetitorSupplyTonnesPerWeek = Intelligence.GetTotalCompetitorSupplyTonnes()
        };

        private decimal ClampPrice(decimal value)
            => Math.Clamp(value, _priceFloor, _priceCeiling);
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
