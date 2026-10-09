// =============================================================================
// MarketIntelligence.cs - Forecasting, News Events & Competitor Tracking
// =============================================================================
// Provides the player with market intelligence:
// - Price forecasting (4-week trend predictions with confidence intervals)
// - Competitor harvest volume tracking (affects supply)
// - Market news events (political, biological, economic shocks)
// - Brand reputation system (consistent quality → premium pricing power)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

using LakskongenSim.Core;
namespace LakskongenSim.Market
{
    /// <summary>
    /// A market news event that affects supply, demand, or prices.
    /// </summary>
    public record MarketNewsEvent
    {
        public string Headline { get; init; } = "";
        public string Description { get; init; } = "";
        public MarketNewsCategory Category { get; init; }
        /// <summary>Multiplier on global supply (e.g., 0.8 = 20% supply drop).</summary>
        public double SupplyImpact { get; init; } = 1.0;
        /// <summary>Multiplier on global demand.</summary>
        public double DemandImpact { get; init; } = 1.0;
        /// <summary>Direct price shock in NOK/kg (additive).</summary>
        public decimal PriceShockNOK { get; init; } = 0m;
        /// <summary>Duration in game weeks.</summary>
        public int DurationWeeks { get; init; } = 4;
        /// <summary>Remaining weeks of this event.</summary>
        public int RemainingWeeks { get; set; }
        /// <summary>When the event was triggered (game week).</summary>
        public int TriggeredAtWeek { get; init; }
    }

    public enum MarketNewsCategory
    {
        BiologicalCrisis,    // Algae blooms, disease outbreaks, mass mortality
        TradePolicy,         // Tariffs, bans, trade agreements
        CompetitorAction,    // Chile floods market, Scottish harvest surge
        ConsumerTrend,       // Sushi trend, health trend, sustainability push
        CurrencyShift,       // NOK vs EUR/USD/JPY
        Regulatory,          // New regulations, license changes
        Weather              // Storm damage, sea temperature anomalies
    }

    /// <summary>
    /// Competitor salmon producer tracked for supply intelligence.
    /// </summary>
    public class CompetitorProducer
    {
        public string Name { get; init; } = "";
        public string Country { get; init; } = "";
        /// <summary>Estimated weekly harvest volume in tonnes.</summary>
        public double WeeklyHarvestTonnes { get; set; }
        /// <summary>Base weekly harvest (normal operations).</summary>
        public double BaseWeeklyHarvestTonnes { get; init; }
        /// <summary>Current disruption factor (1.0 = normal, 0.5 = 50% disrupted).</summary>
        public double DisruptionFactor { get; set; } = 1.0;
    }

    /// <summary>
    /// Price forecast with confidence interval.
    /// </summary>
    public record PriceForecast
    {
        public int WeeksAhead { get; init; }
        public decimal PredictedPrice { get; init; }
        public decimal LowerBound { get; init; }
        public decimal UpperBound { get; init; }
        /// <summary>Confidence level (0-1). Decreases further out.</summary>
        public double Confidence { get; init; }
        /// <summary>Predicted trend direction.</summary>
        public PriceTrend Trend { get; init; }
    }

    public enum PriceTrend
    {
        StrongDown,
        Down,
        Stable,
        Up,
        StrongUp
    }

    /// <summary>
    /// Player brand reputation, built through consistent quality deliveries.
    /// Higher reputation unlocks premium channels and price bonuses.
    /// </summary>
    public class MarketBrandReputation
    {
        /// <summary>Reputation score 0-100.</summary>
        public double Score { get; private set; } = 25.0;

        /// <summary>Price premium multiplier derived from reputation (1.0 at score=0, up to 1.10 at score=100).</summary>
        public decimal PremiumMultiplier => 1.0m + (decimal)(Score / 1000.0);  // Max +10%

        /// <summary>Number of consecutive deliveries meeting or exceeding quality expectations.</summary>
        public int ConsecutiveGoodDeliveries { get; private set; } = 0;

        /// <summary>Total deliveries made.</summary>
        public int TotalDeliveries { get; private set; } = 0;

        internal (double Score, int Good, int Total) ExportState() => (Score, ConsecutiveGoodDeliveries, TotalDeliveries);

        internal void ImportState((double Score, int Good, int Total) s)
        {
            Score = s.Score; ConsecutiveGoodDeliveries = s.Good; TotalDeliveries = s.Total;
        }

        public event Action<double>? OnReputationChanged;

        /// <summary>
        /// Record a delivery and update reputation.
        /// </summary>
        public void RecordDelivery(FishQualityGrade grade, bool onTime, int volumeKg)
        {
            TotalDeliveries++;

            double change = grade switch
            {
                FishQualityGrade.SashimiGrade => 3.0,
                FishQualityGrade.Superior => 1.5,
                FishQualityGrade.Standard => 0.5,
                FishQualityGrade.Production => -1.0,
                FishQualityGrade.Inferior => -3.0,
                _ => 0.0
            };

            // Late delivery penalty
            if (!onTime) change -= 2.0;

            // Volume bonus (larger deliveries build reputation faster)
            if (volumeKg > 50_000) change *= 1.2;

            // Consistency bonus
            if (grade <= FishQualityGrade.Standard)
            {
                ConsecutiveGoodDeliveries++;
                if (ConsecutiveGoodDeliveries >= 10) change += 0.5;  // Streak bonus
            }
            else
            {
                ConsecutiveGoodDeliveries = 0;
            }

            Score = Math.Clamp(Score + change, 0, 100);
            OnReputationChanged?.Invoke(Score);
        }

        /// <summary>Natural reputation decay over time (call weekly).</summary>
        public void WeeklyDecay()
        {
            Score = Math.Max(0, Score - 0.1);
        }
    }

    /// <summary>
    /// Market intelligence system providing forecasting, news, and competitor tracking.
    /// </summary>
    public class MarketIntelligenceSystem
    {
        private readonly List<MarketNewsEvent> _activeEvents = new();
        private readonly List<MarketNewsEvent> _eventHistory = new();
        private readonly List<CompetitorProducer> _competitors = new();
        private readonly Queue<decimal> _priceHistory = new();
        private SimRandom _rng = SimRandom.CreateUnseeded();
        internal SimRandom RngForSnapshot => _rng;

        /// <summary>Persistence (Phase 1 wave 6): weekly news/competitor state.</summary>
        internal (int Week, System.Collections.Generic.List<MarketNewsEvent> Active, System.Collections.Generic.List<MarketNewsEvent> History,
            System.Collections.Generic.List<CompetitorProducer> Competitors, System.Collections.Generic.List<decimal> PriceHistory,
            double BrandScore, int BrandGood, int BrandTotal) ExportState()
            => (_currentWeek, new System.Collections.Generic.List<MarketNewsEvent>(_activeEvents),
                new System.Collections.Generic.List<MarketNewsEvent>(_eventHistory),
                _competitors.Select(c => new CompetitorProducer
                {
                    Name = c.Name, Country = c.Country, WeeklyHarvestTonnes = c.WeeklyHarvestTonnes,
                    BaseWeeklyHarvestTonnes = c.BaseWeeklyHarvestTonnes, DisruptionFactor = c.DisruptionFactor
                }).ToList(),
                new System.Collections.Generic.List<decimal>(_priceHistory),
                PlayerReputation.ExportState().Score, PlayerReputation.ExportState().Good, PlayerReputation.ExportState().Total);

        internal void ImportState((int Week, System.Collections.Generic.List<MarketNewsEvent> Active, System.Collections.Generic.List<MarketNewsEvent> History,
            System.Collections.Generic.List<CompetitorProducer> Competitors, System.Collections.Generic.List<decimal> PriceHistory,
            double BrandScore, int BrandGood, int BrandTotal) s)
        {
            _currentWeek = s.Week;
            _activeEvents.Clear(); _activeEvents.AddRange(s.Active);
            // Shared-reference restore (like GameStateRelinker for Pens):
            // at runtime Active and History hold the SAME event objects, but
            // JSON persists two arrays. Re-unify by identity (template events
            // never stack, so headline+trigger week is unique) or the weekly
            // decrement would move only the Active copy.
            _eventHistory.Clear();
            foreach (var h in s.History)
            {
                var live = _activeEvents.FirstOrDefault(a =>
                    a.Headline == h.Headline &&
                    a.TriggeredAtWeek == h.TriggeredAtWeek &&
                    a.Category == h.Category);
                _eventHistory.Add(live ?? h);
            }
            _competitors.Clear(); _competitors.AddRange(s.Competitors);
            _priceHistory.Clear();
            foreach (var p in s.PriceHistory) _priceHistory.Enqueue(p);
            PlayerReputation.ImportState((s.BrandScore, s.BrandGood, s.BrandTotal));
        }
        private int _currentWeek = 0;

        public MarketBrandReputation PlayerReputation { get; } = new();
        public IReadOnlyList<MarketNewsEvent> ActiveEvents => _activeEvents;
        public IReadOnlyList<MarketNewsEvent> EventHistory => _eventHistory;
        public IReadOnlyList<CompetitorProducer> Competitors => _competitors;

        public event Action<MarketNewsEvent>? OnNewsEvent;

        // ─── Predefined historical/scripted events ───
        private static readonly (string headline, string desc, MarketNewsCategory cat, double supply, double demand, decimal priceShock, int duration, double probability)[] EventPool =
        {
            // Biological
            ("🦠 Chile reports massive algae bloom", "Algal bloom kills millions of salmon in Chile. Global supply drops sharply.", MarketNewsCategory.BiologicalCrisis, 0.80, 1.0, 0m, 8, 0.02),
            ("🦠 ISA outbreak in Norway", "Infectious Salmon Anemia detected in Nordland. Slaughter orders issued.", MarketNewsCategory.BiologicalCrisis, 0.92, 1.0, 5m, 6, 0.03),
            ("🪱 Sea lice crisis — industry-wide treatments", "Nationwide lice counts exceed limits. Mandatory treatments delay harvests.", MarketNewsCategory.BiologicalCrisis, 0.95, 1.0, 3m, 4, 0.04),

            // Trade policy
            ("🚫 Russia bans Norwegian salmon imports", "Political tensions lead to import ban. Major market lost.", MarketNewsCategory.TradePolicy, 1.0, 0.88, -8m, 12, 0.01),
            ("🤝 New EU-UK trade deal for seafood", "Reduced tariffs boost UK demand for Norwegian salmon.", MarketNewsCategory.TradePolicy, 1.0, 1.08, 0m, 16, 0.02),
            ("📋 China tightens cold-chain import rules", "New documentation requirements slow Chinese imports by 30%.", MarketNewsCategory.TradePolicy, 1.0, 0.93, 0m, 8, 0.03),

            // Competitor actions
            ("🇨🇱 Chile floods market with cheap salmon", "Record Chilean harvests push global prices down 20-30%.", MarketNewsCategory.CompetitorAction, 1.25, 1.0, -15m, 10, 0.02),
            ("🏴 Scottish salmon harvest surge", "Scotland reports record production quarter.", MarketNewsCategory.CompetitorAction, 1.08, 1.0, -3m, 6, 0.03),
            ("🇫🇴 Faroese producers cut output", "Environmental regulations force Faroese harvest reductions.", MarketNewsCategory.CompetitorAction, 0.95, 1.0, 2m, 8, 0.02),

            // Consumer trends
            ("🍣 Global sushi trend accelerates", "Sushi consumption grows 15% in Europe. Premium salmon demand spikes.", MarketNewsCategory.ConsumerTrend, 1.0, 1.12, 0m, 12, 0.03),
            ("🥗 Health-conscious consumers boost salmon demand", "Omega-3 studies drive record retail salmon sales.", MarketNewsCategory.ConsumerTrend, 1.0, 1.06, 0m, 8, 0.04),
            ("🌿 Plant-based salmon gains market share", "Alternative protein products take 5% of retail salmon market.", MarketNewsCategory.ConsumerTrend, 1.0, 0.95, 0m, 16, 0.02),

            // Currency
            ("💱 NOK weakens against EUR", "Norwegian krone drops 5%. Export revenues increase.", MarketNewsCategory.CurrencyShift, 1.0, 1.0, 4m, 6, 0.04),
            ("💱 NOK strengthens against USD", "Strong krone hurts competitiveness in dollar markets.", MarketNewsCategory.CurrencyShift, 1.0, 1.0, -3m, 6, 0.04),

            // Regulatory
            ("📜 New production zone regulations (trafikklyssystemet)", "Traffic light system changes restrict several production zones.", MarketNewsCategory.Regulatory, 0.93, 1.0, 5m, 12, 0.02),
            ("🏷️ EU mandates sustainability labeling", "New EU rules require detailed sustainability reporting on all imported fish.", MarketNewsCategory.Regulatory, 1.0, 0.97, 0m, 20, 0.02),

            // Weather
            ("🌊 Storm damage across Norwegian coast", "Winter storms damage sea cages. Multiple sites report fish escapes.", MarketNewsCategory.Weather, 0.96, 1.0, 2m, 4, 0.05),
            ("🌡️ Record sea temperatures", "Warm water causes stress and slower growth across industry.", MarketNewsCategory.Weather, 0.97, 1.0, 0m, 8, 0.04),
        };

        public MarketIntelligenceSystem()
        {
            InitializeCompetitors();
        }

        /// <summary>
        /// Create the intelligence system with a deterministic seed so news
        /// events, competitor variation and forecasts are reproducible under a
        /// seeded engine.
        /// </summary>
        public MarketIntelligenceSystem(int seed)
        {
            _rng = new SimRandom(seed);
            InitializeCompetitors();
        }

        /// <summary>
        /// Replace the RNG with a deterministically-seeded one. Used by
        /// <see cref="MarketSimulation"/> when the engine supplies a seed.
        /// </summary>
        public void Reseed(int seed) => _rng = new SimRandom(seed);

        private void InitializeCompetitors()
        {
            _competitors.AddRange(new[]
            {
                new CompetitorProducer { Name = "Mowier ASA", Country = "Norway", BaseWeeklyHarvestTonnes = 8500, WeeklyHarvestTonnes = 8500 },
                new CompetitorProducer { Name = "SalMax", Country = "Norway", BaseWeeklyHarvestTonnes = 6200, WeeklyHarvestTonnes = 6200 },
                new CompetitorProducer { Name = "NordLaks", Country = "Norway", BaseWeeklyHarvestTonnes = 3800, WeeklyHarvestTonnes = 3800 },
                new CompetitorProducer { Name = "Cermaq", Country = "Norway/Chile", BaseWeeklyHarvestTonnes = 4500, WeeklyHarvestTonnes = 4500 },
                new CompetitorProducer { Name = "Multiexport Foods", Country = "Chile", BaseWeeklyHarvestTonnes = 3200, WeeklyHarvestTonnes = 3200 },
                new CompetitorProducer { Name = "Bakkafrost", Country = "Faroe Islands", BaseWeeklyHarvestTonnes = 2800, WeeklyHarvestTonnes = 2800 },
                new CompetitorProducer { Name = "Mowi Scotland", Country = "Scotland", BaseWeeklyHarvestTonnes = 2400, WeeklyHarvestTonnes = 2400 },
            });
        }

        /// <summary>
        /// Advance intelligence system by one week. Checks for new events, updates competitors.
        /// </summary>
        public void AdvanceWeek(decimal currentSpotPrice)
        {
            _currentWeek++;
            _priceHistory.Enqueue(currentSpotPrice);
            if (_priceHistory.Count > 52) _priceHistory.Dequeue();

            // Decay active events
            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                _activeEvents[i].RemainingWeeks--;
                if (_activeEvents[i].RemainingWeeks <= 0)
                    _activeEvents.RemoveAt(i);
            }

            // Random event check
            foreach (var template in EventPool)
            {
                if (_rng.NextDouble() < template.probability / 52.0)  // Convert annual prob to weekly
                {
                    // Don't stack similar events
                    if (_activeEvents.Any(e => e.Category == template.cat))
                        continue;

                    var evt = new MarketNewsEvent
                    {
                        Headline = template.headline,
                        Description = template.desc,
                        Category = template.cat,
                        SupplyImpact = template.supply,
                        DemandImpact = template.demand,
                        PriceShockNOK = template.priceShock,
                        DurationWeeks = template.duration,
                        RemainingWeeks = template.duration,
                        TriggeredAtWeek = _currentWeek
                    };

                    _activeEvents.Add(evt);
                    _eventHistory.Add(evt);
                    OnNewsEvent?.Invoke(evt);
                }
            }

            // Update competitor volumes (seasonal + random variation)
            foreach (var comp in _competitors)
            {
                var seasonalFactor = 1.0 + 0.15 * Math.Sin(2.0 * Math.PI * (_currentWeek - 13) / 52.0);
                var randomFactor = 0.95 + _rng.NextDouble() * 0.10;
                comp.WeeklyHarvestTonnes = comp.BaseWeeklyHarvestTonnes * seasonalFactor * randomFactor * comp.DisruptionFactor;
            }

            // Brand reputation decay
            PlayerReputation.WeeklyDecay();
        }

        /// <summary>
        /// Get aggregate supply/demand impact from all active events.
        /// </summary>
        public (double supplyFactor, double demandFactor, decimal priceShock) GetActiveEventImpacts()
        {
            double supply = 1.0;
            double demand = 1.0;
            decimal shock = 0m;

            foreach (var evt in _activeEvents)
            {
                // Impact fades as event nears end
                double fade = Math.Min(1.0, evt.RemainingWeeks / (double)Math.Max(1, evt.DurationWeeks));
                supply *= 1.0 + (evt.SupplyImpact - 1.0) * fade;
                demand *= 1.0 + (evt.DemandImpact - 1.0) * fade;
                shock += evt.PriceShockNOK * (decimal)fade;
            }

            return (supply, demand, shock);
        }

        /// <summary>
        /// Generate price forecasts for 1-4 weeks ahead using trend extrapolation + O-U mean reversion.
        /// Intentionally imperfect (adds noise) so the player can't just follow the forecast blindly.
        /// </summary>
        public List<PriceForecast> GetForecasts(decimal currentPrice, decimal longTermMean, decimal weeklyVolatility)
        {
            var forecasts = new List<PriceForecast>();
            var prices = _priceHistory.ToArray();

            // Calculate recent trend (4-week moving average slope)
            decimal recentTrend = 0m;
            if (prices.Length >= 4)
            {
                var recent4 = prices.TakeLast(4).ToArray();
                recentTrend = (recent4[3] - recent4[0]) / 3.0m;
            }

            for (int w = 1; w <= 4; w++)
            {
                // Mean reversion pull
                var meanPull = (longTermMean - currentPrice) * 0.015m * w;

                // Trend extrapolation (weakens over time)
                var trendComponent = recentTrend * (decimal)(1.0 / w);

                // Event impacts
                var (supFac, demFac, priceShock) = GetActiveEventImpacts();
                var eventComponent = priceShock * 0.5m;  // Events partially priced in

                var predicted = currentPrice + meanPull + trendComponent + eventComponent;

                // Add deliberate forecasting noise (±5-15% uncertainty)
                var noiseRange = weeklyVolatility * (decimal)Math.Sqrt(w);
                var noise = ((decimal)_rng.NextDouble() - 0.5m) * noiseRange * 0.3m;
                predicted += noise;

                // Confidence decreases with horizon
                var confidence = Math.Max(0.3, 0.85 - 0.15 * w);

                // Bounds widen with horizon
                var lower = predicted - noiseRange * 1.5m;
                var upper = predicted + noiseRange * 1.5m;

                // Determine trend direction
                var pctChange = currentPrice > 0 ? (predicted - currentPrice) / currentPrice : 0m;
                var trend = pctChange switch
                {
                    < -0.05m => PriceTrend.StrongDown,
                    < -0.02m => PriceTrend.Down,
                    > 0.05m => PriceTrend.StrongUp,
                    > 0.02m => PriceTrend.Up,
                    _ => PriceTrend.Stable
                };

                forecasts.Add(new PriceForecast
                {
                    WeeksAhead = w,
                    PredictedPrice = Math.Max(50m, predicted),
                    LowerBound = Math.Max(40m, lower),
                    UpperBound = Math.Min(170m, upper),
                    Confidence = confidence,
                    Trend = trend
                });
            }

            return forecasts;
        }

        /// <summary>
        /// Get total competitor supply (tonnes/week) for supply/demand calculations.
        /// </summary>
        public double GetTotalCompetitorSupplyTonnes()
        {
            return _competitors.Sum(c => c.WeeklyHarvestTonnes);
        }

        /// <summary>
        /// Get a snapshot of the top competitors and their estimated volumes.
        /// </summary>
        public List<(string Name, string Country, double WeeklyTonnes)> GetCompetitorSnapshot()
        {
            return _competitors
                .OrderByDescending(c => c.WeeklyHarvestTonnes)
                .Select(c => (c.Name, c.Country, Math.Round(c.WeeklyHarvestTonnes, 0)))
                .ToList();
        }
    }
}
