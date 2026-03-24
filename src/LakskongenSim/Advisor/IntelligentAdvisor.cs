using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Biology;
using LakskongenSim.Market;
using LakskongenSim.Finance;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Events;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Intelligent Advisor System for Lakskongen salmon farming simulator.
    /// Generates actionable recommendations by combining environmental, biological, and economic data.
    /// 
    /// Key features:
    /// - Multi-factor analysis for optimal harvest timing
    /// - Feeding adjustments based on environmental conditions
    /// - Pen management recommendations
    /// - Market timing and price optimization
    /// - Risk mitigation strategies
    /// - Integrated alert system
    /// - Recommendation tracking and learning
    /// - AI storyteller integration for narrative context
    /// </summary>
    public class IntelligentAdvisor
    {
        private readonly MareanoEnvironmentalDataProvider _environmentalData;
        private readonly MarketSimulation _marketSimulation;
        private readonly FinanceManager _financeManager;
        private readonly GameState _gameState;
        private readonly Storyteller? _storyteller;
        private readonly AlertSystem _alertSystem;
        private readonly RecommendationHistory _recommendationHistory;
        private readonly Random _rng;

        // Configuration thresholds
        private const double CRITICAL_OXYGEN_THRESHOLD = 5.0; // mg/L
        private const double WARNING_OXYGEN_THRESHOLD = 6.5; // mg/L
        private const double OPTIMAL_TEMP_MIN = 8.0; // °C
        private const double OPTIMAL_TEMP_MAX = 14.0; // °C
        private const double CRITICAL_TEMP_MIN = 4.0; // °C
        private const double CRITICAL_TEMP_MAX = 20.0; // °C
        private const double OPTIMAL_HARVEST_WEIGHT = 4.5; // kg
        private const double MAX_STOCKING_DENSITY = 25.0; // kg/m³
        private const double WARNING_STOCKING_DENSITY = 20.0; // kg/m³
        private const double CRITICAL_MORTALITY_RATE = 0.002; // 0.2% per day
        private const double CRITICAL_SEA_LICE_COUNT = 2.0; // per fish
        private const decimal LOW_CASH_THRESHOLD = 500_000m; // NOK
        private const decimal CRITICAL_CASH_THRESHOLD = 100_000m; // NOK

        /// <summary>Event fired when a new recommendation is generated.</summary>
        public event Action<Recommendation>? OnRecommendationGenerated;

        /// <summary>Event fired when an alert is triggered.</summary>
        public event Action<Alert>? OnAlertGenerated;

        /// <summary>Access to the alert system.</summary>
        public AlertSystem Alerts => _alertSystem;

        /// <summary>Access to recommendation history.</summary>
        public RecommendationHistory History => _recommendationHistory;

        /// <summary>Current simulation day.</summary>
        public int CurrentDay => _gameState.Day;

        public IntelligentAdvisor(
            MareanoEnvironmentalDataProvider environmentalData,
            MarketSimulation marketSimulation,
            FinanceManager financeManager,
            GameState gameState,
            Storyteller? storyteller = null,
            string? historyStoragePath = null,
            int? seed = null)
        {
            _environmentalData = environmentalData ?? throw new ArgumentNullException(nameof(environmentalData));
            _marketSimulation = marketSimulation ?? throw new ArgumentNullException(nameof(marketSimulation));
            _financeManager = financeManager ?? throw new ArgumentNullException(nameof(financeManager));
            _gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
            _storyteller = storyteller;
            _alertSystem = new AlertSystem();
            _recommendationHistory = new RecommendationHistory(historyStoragePath);
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();

            // Wire up events
            _alertSystem.OnAlertGenerated += alert => OnAlertGenerated?.Invoke(alert);
        }

        // ─── Main Analysis Methods ───────────────────────────────────────────

        /// <summary>
        /// Run comprehensive analysis and generate all applicable recommendations.
        /// Call this daily or when significant changes occur.
        /// </summary>
        public List<Recommendation> AnalyzeAndGenerateRecommendations()
        {
            var recommendations = new List<Recommendation>();

            // Analyze each sea site
            foreach (var site in _gameState.SeaSites)
            {
                var zoneData = _environmentalData.GetCurrentData(site.ZoneId);
                
                // Check environmental conditions
                recommendations.AddRange(AnalyzeEnvironmentalConditions(site, zoneData));
                
                // Analyze each pen/population
                foreach (var pen in site.Pens)
                {
                    recommendations.AddRange(AnalyzePopulation(site, pen, zoneData));
                }
                
                // Check pen management
                recommendations.AddRange(AnalyzePenManagement(site, zoneData));
            }

            // Market and financial analysis
            recommendations.AddRange(AnalyzeMarketTiming());
            recommendations.AddRange(AnalyzeFinancialHealth());
            recommendations.AddRange(AnalyzeRiskMitigation());

            // Filter duplicates and recently recommended items
            var filtered = FilterRecommendations(recommendations);

            // Add to history and notify
            foreach (var rec in filtered)
            {
                _recommendationHistory.AddRecommendation(rec);
                OnRecommendationGenerated?.Invoke(rec);
                
                // Generate narrative if storyteller is available
                GenerateNarrative(rec);
            }

            // Cleanup old recommendations
            _recommendationHistory.CleanOutdatedRecommendations(CurrentDay, 30);

            return filtered;
        }

        /// <summary>Analyze environmental conditions for a site.</summary>
        private List<Recommendation> AnalyzeEnvironmentalConditions(SeaSite site, EnvironmentalDataPoint envData)
        {
            var recs = new List<Recommendation>();

            // Get oxygen level from zone's water chemistry (authoritative) or fall back to environmental data
            var zone = _gameState.Zones.FirstOrDefault(z => z.Id == site.ZoneId);
            double oxygenLevel = zone?.WaterChemistry?.OxygenO2 ?? envData.OxygenAtCageDepth;

            // Oxygen alert/recommendation
            if (oxygenLevel < CRITICAL_OXYGEN_THRESHOLD)
            {
                _alertSystem.EnvironmentalAlert(
                    AlertSeverity.Critical,
                    "Kritisk oksygennivå!",
                    $"Oksygennivået i {site.ZoneId} er kritisk lavt ({oxygenLevel:F1} mg/L). Øyeblikkelig handling nødvendig.",
                    site.ZoneId,
                    CRITICAL_OXYGEN_THRESHOLD,
                    oxygenLevel);

                recs.Add(CreateRecommendation(
                    RecommendationType.RiskMitigation,
                    RecommendationCategory.Emergency,
                    "🚨 Øk oksygen tilførsel umiddelbart",
                    $"Oksygennivået er kritisk lavt ({oxygenLevel:F1} mg/L). Aktiver nødoksygenering eller reduser beiting.",
                    "Aktiver reserveoksygensystemer og reduser fôring med 50% inntil nivået stabiliserer seg over 6 mg/L.",
                    site.ZoneId,
                    site.Id,
                    urgency: 5,
                    confidence: 0.95,
                    expectedImpact: -50_000m, // Cost of emergency measures
                    envContext: new EnvironmentalFactors
                    {
                        Temperature = envData.TemperatureAtCageDepth,
                        OxygenLevel = oxygenLevel,
                        OxygenStressIndex = envData.OxygenStressIndex
                    }));
            }
            else if (oxygenLevel < WARNING_OXYGEN_THRESHOLD)
            {
                _alertSystem.EnvironmentalAlert(
                    AlertSeverity.Warning,
                    "Lavt oksygennivå",
                    $"Oksygennivået i {site.ZoneId} er lavt ({oxygenLevel:F1} mg/L). Overvåk nøye.",
                    site.ZoneId,
                    WARNING_OXYGEN_THRESHOLD,
                    oxygenLevel);

                recs.Add(CreateRecommendation(
                    RecommendationType.PenManagement,
                    RecommendationCategory.Environmental,
                    "⚠️ Reduser fôring - lavt oksygen",
                    $"Oksygennivået er {oxygenLevel:F1} mg/L, under optimalt nivå.",
                    "Reduser fôring med 20-30% inntil oksygennivået bedres. Vurder økt vannutskiftning.",
                    site.ZoneId,
                    site.Id,
                    urgency: 3,
                    confidence: 0.8,
                    expectedImpact: 10_000m, // Avoided losses
                    envContext: new EnvironmentalFactors
                    {
                        OxygenLevel = oxygenLevel,
                        OxygenStressIndex = envData.OxygenStressIndex
                    }));
            }

            // Temperature analysis
            if (envData.TemperatureAtCageDepth > CRITICAL_TEMP_MAX)
            {
                _alertSystem.EnvironmentalAlert(
                    AlertSeverity.Critical,
                    "Ekstrem varme!",
                    $"Vanntemperaturen i {site.ZoneId} er farlig høy ({envData.TemperatureAtCageDepth:F1}°C).",
                    site.ZoneId,
                    CRITICAL_TEMP_MAX,
                    envData.TemperatureAtCageDepth);

                recs.Add(CreateRecommendation(
                    RecommendationType.RiskMitigation,
                    RecommendationCategory.Emergency,
                    "🌡️ Høy vanntemperatur - økt dødelighetsrisiko",
                    $"Temperaturen er {envData.TemperatureAtCageDepth:F1}°C, godt over optimalområdet. Stress og sykdomsrisiko er høy.",
                    "Reduser fôring betydelig, øk overvåking av fiskens velferd, vurder tidlig slakting av største fisk.",
                    site.ZoneId,
                    site.Id,
                    urgency: 4,
                    confidence: 0.9,
                    expectedImpact: 100_000m,
                    envContext: new EnvironmentalFactors
                    {
                        Temperature = envData.TemperatureAtCageDepth,
                        TemperatureStressIndex = envData.TemperatureStressIndex
                    }));
            }
            else if (envData.TemperatureAtCageDepth < OPTIMAL_TEMP_MIN || 
                     envData.TemperatureAtCageDepth > OPTIMAL_TEMP_MAX)
            {
                var isCold = envData.TemperatureAtCageDepth < OPTIMAL_TEMP_MIN;
                recs.Add(CreateRecommendation(
                    RecommendationType.FeedingAdjustment,
                    RecommendationCategory.Environmental,
                    isCold ? "❄️ Juster fôring for kaldt vann" : "🌡️ Juster fôring for varmt vann",
                    $"Vanntemperatur {envData.TemperatureAtCageDepth:F1}°C er utenfor optimalt område (8-14°C).",
                    isCold 
                        ? "Reduser fôrrasjon med 10-15%. Fiskens metabolisme er redusert i kaldt vann."
                        : "Reduser fôrrasjon med 15-25%. Unngå fôring midt på dagen når temperaturen er høyest.",
                    site.ZoneId,
                    site.Id,
                    urgency: 2,
                    confidence: 0.75,
                    expectedImpact: 25_000m,
                    envContext: new EnvironmentalFactors
                    {
                        Temperature = envData.TemperatureAtCageDepth,
                        TemperatureStressIndex = envData.TemperatureStressIndex
                    }));
            }

            // Algal bloom risk
            if (envData.ChlorophyllA > 10)
            {
                recs.Add(CreateRecommendation(
                    RecommendationType.RiskMitigation,
                    RecommendationCategory.Environmental,
                    "🦠 Algeoppblomstring påvist",
                    "Høye klorofyll-nivåer indikerer algeoppblomstring. Risiko for fiskhelse.",
                    "Øk overvåking av oksygen, vurder dyppere merdinger, reduser fôring hvis fisk viser tegn på stress.",
                    site.ZoneId,
                    site.Id,
                    urgency: 4,
                    confidence: 0.85,
                    expectedImpact: 75_000m,
                    envContext: new EnvironmentalFactors
                    {
                        EnvironmentalQualityScore = envData.EnvironmentalQualityScore
                    }));
            }

            return recs;
        }

        /// <summary>Analyze a specific fish population/pen.</summary>
        private List<Recommendation> AnalyzePopulation(SeaSite site, FishPopulation pen, EnvironmentalDataPoint envData)
        {
            var recs = new List<Recommendation>();
            
            var totalBiomass = pen.GetTotalBiomassKg();
            var fishCount = pen.TotalCount;
            if (fishCount == 0) return recs;

            var avgWeight = totalBiomass / fishCount;
            var mortalityRate = CalculateMortalityRate(pen);
            var daysInSea = CalculateDaysInSea(pen);

            var bioContext = new BiologicalFactors
            {
                AverageWeightKg = avgWeight,
                FishCount = fishCount,
                TotalBiomassKg = totalBiomass,
                DaysInSea = daysInSea,
                MortalityRate = mortalityRate,
                SeaLiceCount = pen.SeaLiceLoad,
                StockingDensity = site.CurrentStockingDensity
            };

            // Harvest timing recommendation
            if (avgWeight >= OPTIMAL_HARVEST_WEIGHT - 0.5 && avgWeight <= OPTIMAL_HARVEST_WEIGHT + 1.0)
            {
                var marketStatus = _marketSimulation.GetStatus();
                var isGoodMarket = marketStatus.SpotPrice > marketStatus.LongTermMean;
                var daysToOptimal = EstimateDaysToOptimalHarvest(pen, envData);

                if (isGoodMarket || daysToOptimal > 30)
                {
                    var expectedRevenue = (decimal)totalBiomass * marketStatus.SpotPrice * 0.95m; // 95% yield
                    var currentCostPerDay = CalculateDailyCost(pen);
                    var potentialGain = currentCostPerDay * daysToOptimal;

                    recs.Add(CreateRecommendation(
                        RecommendationType.HarvestTiming,
                        RecommendationCategory.Financial,
                        isGoodMarket ? "💰 Godt marked - vurder slakting" : "⚖️ Optimal slaktevekt nådd",
                        $"Fisken veier i gjennomsnitt {avgWeight:F2} kg. {GetHarvestTimingReason(marketStatus, daysToOptimal)}",
                        "Planlegg slakting innen 7-14 dager for å realisere verdien. Koordiner med slakteriet for logistikk.",
                        site.ZoneId,
                        site.Id,
                        pen.Id,
                        urgency: isGoodMarket ? 4 : 3,
                        confidence: isGoodMarket ? 0.85 : 0.75,
                        expectedImpact: expectedRevenue - potentialGain,
                        bioContext: bioContext,
                        econContext: new EconomicFactors
                        {
                            CurrentPricePerKg = marketStatus.SpotPrice,
                            OperationalCostPerDay = currentCostPerDay
                        }));
                }
            }

            // Mortality alert
            if (mortalityRate > CRITICAL_MORTALITY_RATE)
            {
                _alertSystem.HealthAlert(
                    AlertSeverity.Critical,
                    "Høy dødelighet påvist",
                    $"Dødeligheten i {site.Id} er {mortalityRate:P2}, kritisk høyt. Undersøk umiddelbart.",
                    site.Id,
                    site.ZoneId);

                recs.Add(CreateRecommendation(
                    RecommendationType.DiseasePrevention,
                    RecommendationCategory.Biological,
                    "⚠️ Høy dødelighet - undersøk årsak",
                    $"Dødelighet på {mortalityRate:P2} per dag. Normal er under {CRITICAL_MORTALITY_RATE:P2}.",
                    "Utfør helseundersøkelse, sjekk for sykdom, vurder behandling. Dokumenter funn for Mattilsynet.",
                    site.ZoneId,
                    site.Id,
                    pen.Id,
                    urgency: 5,
                    confidence: 0.9,
                    expectedImpact: -30_000m,
                    bioContext: bioContext));
            }

            // Sea lice alert
            if (pen.SeaLiceLoad > CRITICAL_SEA_LICE_COUNT)
            {
                _alertSystem.HealthAlert(
                    AlertSeverity.Warning,
                    "Høy lakselus-belastning",
                    $"Lusetelling viser {pen.SeaLiceLoad:F1} lus per fisk, over grenseverdi.",
                    site.Id,
                    site.ZoneId);

                recs.Add(CreateRecommendation(
                    RecommendationType.DiseasePrevention,
                    RecommendationCategory.Compliance,
                    "🦠 Behandling mot lakselus nødvendig",
                    $"{pen.SeaLiceLoad:F1} lus per fisk overstiger reguleringsgrensen. Mattilsynet må varsles hvis ikke behandlet.",
                    "Planlegg lusebehandling innen 7 dager. Vurder termisk, mekanisk eller medikamentell behandling.",
                    site.ZoneId,
                    site.Id,
                    pen.Id,
                    urgency: 4,
                    confidence: 0.85,
                    expectedImpact: -75_000m, // Treatment cost
                    bioContext: bioContext));
            }

            return recs;
        }

        /// <summary>Analyze pen management needs.</summary>
        private List<Recommendation> AnalyzePenManagement(SeaSite site, EnvironmentalDataPoint envData)
        {
            var recs = new List<Recommendation>();

            // Stocking density check
            if (site.CurrentStockingDensity > MAX_STOCKING_DENSITY)
            {
                _alertSystem.OperationalAlert(
                    AlertSeverity.Critical,
                    "Overbefolking i merd",
                    $"Tettheten i {site.Id} er {site.CurrentStockingDensity:F1} kg/m³, over maksimum {MAX_STOCKING_DENSITY} kg/m³.",
                    site.Id);

                recs.Add(CreateRecommendation(
                    RecommendationType.PenManagement,
                    RecommendationCategory.Compliance,
                    "🚨 Reduser tetthet umiddelbart",
                    $"Tetthet på {site.CurrentStockingDensity:F1} kg/m³ overstiger tillatt grense.",
                    "Slakte eller flytt fisk umiddelbart for å redusere tetthet. Risiko for velferdsbrudd og bot.",
                    site.ZoneId,
                    site.Id,
                    urgency: 5,
                    confidence: 0.95,
                    expectedImpact: -200_000m,
                    bioContext: new BiologicalFactors
                    {
                        StockingDensity = site.CurrentStockingDensity
                    }));
            }
            else if (site.CurrentStockingDensity > WARNING_STOCKING_DENSITY)
            {
                recs.Add(CreateRecommendation(
                    RecommendationType.PenManagement,
                    RecommendationCategory.Operations,
                    "⚠️ Høy tetthet - planlegg uttak",
                    $"Tetthet på {site.CurrentStockingDensity:F1} kg/m³ nærmer seg maksimum.",
                    "Planlegg delslakting innen 2-4 uker for å opprettholde god fiskevelferd og vekst.",
                    site.ZoneId,
                    site.Id,
                    urgency: 3,
                    confidence: 0.75,
                    expectedImpact: 50_000m,
                    bioContext: new BiologicalFactors
                    {
                        StockingDensity = site.CurrentStockingDensity
                    }));
            }

            return recs;
        }

        /// <summary>Analyze market timing opportunities.</summary>
        private List<Recommendation> AnalyzeMarketTiming()
        {
            var recs = new List<Recommendation>();
            var marketStatus = _marketSimulation.GetStatus();
            var forecasts = _marketSimulation.GetPriceForecasts();

            // Price opportunity
            if (marketStatus.SpotPrice > marketStatus.LongTermMean * 1.15m)
            {
                _alertSystem.MarketAlert(
                    AlertSeverity.Opportunity,
                    "Høye laksepriser!",
                    $"Spotpris på {marketStatus.SpotPrice:N0} NOK/kg er {((marketStatus.SpotPrice / marketStatus.LongTermMean - 1) * 100):F0}% over gjennomsnitt. Vurder salg.",
                    (double)marketStatus.LongTermMean * 1.15,
                    (double)marketStatus.SpotPrice);

                recs.Add(CreateRecommendation(
                    RecommendationType.MarketTiming,
                    RecommendationCategory.Financial,
                    "📈 Utmerket marked - selg nå!",
                    $"Prisen på {marketStatus.SpotPrice:N0} NOK/kg er betydelig over langsiktig gjennomsnitt ({marketStatus.LongTermMean:N0} NOK/kg).",
                    "Prioriter slakting og salg for å utnytte høye priser. Forventet tilbakegang i prisene de neste ukene.",
                    urgency: 4,
                    confidence: 0.8,
                    expectedImpact: CalculateMarketOpportunityValue(marketStatus),
                    econContext: new EconomicFactors
                    {
                        CurrentPricePerKg = marketStatus.SpotPrice,
                        LongTermMean = marketStatus.LongTermMean,
                        PriceTrend = marketStatus.SpotPrice / marketStatus.LongTermMean
                    }));
            }

            // Price drop warning
            if (marketStatus.SpotPrice < marketStatus.LongTermMean * 0.85m)
            {
                recs.Add(CreateRecommendation(
                    RecommendationType.MarketTiming,
                    RecommendationCategory.Strategic,
                    "📉 Lave priser - vurder å vente",
                    $"Prisen på {marketStatus.SpotPrice:N0} NOK/kg er under gjennomsnitt. Vurder å holde fisk lenger hvis helse tillater.",
                    "Utslaking kan være lønnsomt hvis fiskens vekt og helse er god. Vurder kostnad mot forventet prisoppgang.",
                    urgency: 2,
                    confidence: 0.7,
                    expectedImpact: 0, // Depends on holding strategy
                    econContext: new EconomicFactors
                    {
                        CurrentPricePerKg = marketStatus.SpotPrice,
                        LongTermMean = marketStatus.LongTermMean
                    }));
            }

            return recs;
        }

        /// <summary>Analyze financial health.</summary>
        private List<Recommendation> AnalyzeFinancialHealth()
        {
            var recs = new List<Recommendation>();
            var balance = _financeManager.Balance;

            if (balance < CRITICAL_CASH_THRESHOLD)
            {
                _alertSystem.FinancialAlert(
                    AlertSeverity.Critical,
                    "Kritisk likviditetssituasjon!",
                    $"Saldo på {balance:N0} NOK er kritisk lav. Umiddelbar handling nødvendig.",
                    (double)CRITICAL_CASH_THRESHOLD,
                    (double)balance);

                recs.Add(CreateRecommendation(
                    RecommendationType.CostOptimization,
                    RecommendationCategory.Emergency,
                    "💰 KRITISK: Umiddelbar likviditetstiltak",
                    $"Kun {balance:N0} NOK tilgjengelig. Drift kan ikke opprettholdes.",
                    "Søk lån, selg eiendeler, eller slakt fisk umiddelbart. Kontakt bank og investorer nå.",
                    urgency: 5,
                    confidence: 0.95,
                    expectedImpact: -500_000m,
                    econContext: new EconomicFactors
                    {
                        CashPosition = balance
                    }));
            }
            else if (balance < LOW_CASH_THRESHOLD)
            {
                _alertSystem.FinancialAlert(
                    AlertSeverity.Warning,
                    "Lav likviditet",
                    $"Saldo på {balance:N0} NOK er under anbefalt minimum. Overvåk kostnader nøye.",
                    (double)LOW_CASH_THRESHOLD,
                    (double)balance);

                recs.Add(CreateRecommendation(
                    RecommendationType.CostOptimization,
                    RecommendationCategory.Financial,
                    "💳 Vurder kostnadsreduksjon",
                    $"Likviditet på {balance:N0} NOK gir liten buffer.",
                    "Utsett ikke-kritiske investeringer, vurder salg av produksjonsklar fisk, forhandle betalingsutsettelser.",
                    urgency: 3,
                    confidence: 0.7,
                    expectedImpact: 0,
                    econContext: new EconomicFactors
                    {
                        CashPosition = balance
                    }));
            }

            return recs;
        }

        /// <summary>Analyze risk mitigation needs.</summary>
        private List<Recommendation> AnalyzeRiskMitigation()
        {
            var recs = new List<Recommendation>();

            // Check for insurance gaps
            // Check for overconcentration in one zone
            // Check for single points of failure
            // These would integrate with InsuranceSystem, etc.

            return recs;
        }

        // ─── Helper Methods ──────────────────────────────────────────────────

        private Recommendation CreateRecommendation(
            RecommendationType type,
            RecommendationCategory category,
            string title,
            string description,
            string action,
            string? zoneId = null,
            string? siteId = null,
            string? populationId = null,
            int urgency = 3,
            double confidence = 0.7,
            decimal expectedImpact = 0,
            EnvironmentalFactors? envContext = null,
            BiologicalFactors? bioContext = null,
            EconomicFactors? econContext = null)
        {
            return new Recommendation
            {
                Id = $"REC-{Guid.NewGuid().ToString()[..8]}",
                CreatedAt = DateTime.UtcNow,
                SimulationDay = CurrentDay,
                Type = type,
                Category = category,
                Title = title,
                Description = description,
                RecommendedAction = action,
                TargetZoneId = zoneId,
                TargetSiteId = siteId,
                TargetPopulationId = populationId,
                UrgencyRating = urgency,
                ConfidenceLevel = confidence,
                ExpectedImpactNOK = expectedImpact,
                TimeHorizon = GetTimeHorizon(urgency),
                SuccessProbability = confidence * (1.0 - (urgency - 1) * 0.05),
                EnvironmentalContext = envContext ?? new EnvironmentalFactors(),
                BiologicalContext = bioContext ?? new BiologicalFactors(),
                EconomicContext = econContext ?? new EconomicFactors()
            };
        }

        private TimeHorizon GetTimeHorizon(int urgency)
        {
            return urgency switch
            {
                5 => TimeHorizon.Immediate,
                4 => TimeHorizon.ShortTerm,
                3 => TimeHorizon.MediumTerm,
                _ => TimeHorizon.LongTerm
            };
        }

        private List<Recommendation> FilterRecommendations(List<Recommendation> recommendations)
        {
            var filtered = new List<Recommendation>();
            
            foreach (var rec in recommendations)
            {
                if (!_recommendationHistory.WasRecentlyRecommended(
                    rec.Type, 
                    rec.TargetSiteId ?? rec.TargetZoneId, 
                    7))
                {
                    filtered.Add(rec);
                }
            }

            return filtered;
        }

        private void GenerateNarrative(Recommendation rec)
        {
            if (_storyteller == null) return;

            rec.NarrativeText = rec.Type switch
            {
                RecommendationType.HarvestTiming => 
                    $"Din rådgiver har identifisert en god salgsmulighet. {rec.Description}",
                RecommendationType.RiskMitigation =>
                    $"En potensiell krise har blitt oppdaget. {rec.Description}",
                RecommendationType.DiseasePrevention =>
                    $"Helseovervåkingen indikerer behov for tiltak. {rec.Description}",
                _ => $"Råd fra din fiskerådgiver: {rec.Description}"
            };
        }

        // ─── Calculation Helpers ─────────────────────────────────────────────

        private double CalculateMortalityRate(FishPopulation pen)
        {
            var recentCohorts = pen.Cohorts.Where(c => c.AgeDays < 30).ToList();
            if (!recentCohorts.Any()) return 0;
            return 0.0005; // Baseline
        }

        private int CalculateDaysInSea(FishPopulation pen)
        {
            if (!pen.Cohorts.Any()) return 0;
            return pen.Cohorts.Min(c => c.AgeDays);
        }

        private int EstimateDaysToOptimalHarvest(FishPopulation pen, EnvironmentalDataPoint envData)
        {
            var avgWeight = pen.Cohorts.Any() 
                ? pen.Cohorts.Average(c => c.MeanWeightKg) 
                : 0;
            
            if (avgWeight >= OPTIMAL_HARVEST_WEIGHT) return 0;
            
            var growthMultiplier = _environmentalData.GetGrowthMultiplier(envData.ZoneId);
            var dailyGrowth = 0.015 * growthMultiplier;
            var weightNeeded = OPTIMAL_HARVEST_WEIGHT - avgWeight;
            var daysNeeded = (int)(weightNeeded / dailyGrowth);
            
            return Math.Max(0, daysNeeded);
        }

        private string GetHarvestTimingReason(MarketStatus marketStatus, int daysToOptimal)
        {
            if (marketStatus.SpotPrice > marketStatus.LongTermMean * 1.1m)
                return "Prisene er sterke - godt tidspunkt for salg.";
            if (daysToOptimal > 45)
                return "Ytterligere vekstpotensial før optimal størrelse.";
            return "Fisken har nådd god slaktestørrelse.";
        }

        private decimal CalculateDailyCost(FishPopulation pen)
        {
            var biomass = (decimal)pen.GetTotalBiomassKg();
            var feedCost = biomass * 0.015m * 15m;
            var operationalCost = biomass * 0.05m;
            return feedCost + operationalCost;
        }

        private decimal CalculateMarketOpportunityValue(MarketStatus marketStatus)
        {
            var typicalHarvest = 100_000m;
            var premium = marketStatus.SpotPrice - marketStatus.LongTermMean;
            return typicalHarvest * premium;
        }

        // ─── Public API Methods ──────────────────────────────────────────────

        public IEnumerable<Recommendation> GetPrioritizedRecommendations()
        {
            return _recommendationHistory.PrioritizedRecommendations;
        }

        public IEnumerable<Recommendation> GetUrgentRecommendations()
        {
            return _recommendationHistory.GetUrgentRecommendations();
        }

        public void FollowRecommendation(string recommendationId)
        {
            _recommendationHistory.MarkAsFollowed(recommendationId);
        }

        public void DismissRecommendation(string recommendationId, string? reason = null)
        {
            _recommendationHistory.DismissRecommendation(recommendationId, reason);
        }

        public void RecordOutcome(string recommendationId, RecommendationOutcome outcome)
        {
            _recommendationHistory.RecordOutcome(recommendationId, outcome);
        }

        public AdvisorPerformanceMetrics GetPerformanceMetrics()
        {
            return _recommendationHistory.GetPerformanceMetrics();
        }

        public DecisionPatternAnalysis GetDecisionPatterns()
        {
            return _recommendationHistory.GetDecisionPatterns();
        }

        public AdvisorSummary GetSummary()
        {
            var activeAlerts = _alertSystem.ActiveAlerts;
            var recommendations = _recommendationHistory.ActiveRecommendations.ToList();
            
            return new AdvisorSummary
            {
                TotalActiveRecommendations = recommendations.Count,
                CriticalAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Critical),
                WarningAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Warning),
                Opportunities = recommendations.Count(r => r.ExpectedImpactNOK > 0 && r.ConfidenceLevel > 0.7),
                UrgentActions = recommendations.Count(r => r.UrgencyRating >= 4),
                TopPriority = recommendations.OrderByDescending(r => r.UrgencyRating).FirstOrDefault()
            };
        }
    }

    /// <summary>Summary of advisor's current assessment.</summary>
    public class AdvisorSummary
    {
        public int TotalActiveRecommendations { get; set; }
        public int CriticalAlerts { get; set; }
        public int WarningAlerts { get; set; }
        public int Opportunities { get; set; }
        public int UrgentActions { get; set; }
        public Recommendation? TopPriority { get; set; }
        
        public bool HasCriticalIssues => CriticalAlerts > 0 || UrgentActions > 0;
        
        public string StatusText => HasCriticalIssues 
            ? "🔴 Kritiske forhold påvist - umiddelbar handling nødvendig" 
            : (WarningAlerts > 0 
                ? "🟠 Advarsler aktive - overvåk situasjonen" 
                : (Opportunities > 0 
                    ? "🟢 Muligheter identifisert" 
                    : "⚪ Ingen aktive anbefalinger"));
    }
}
