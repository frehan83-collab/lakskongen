// In-day ITickSystem phases, migrated verbatim from SimulationEngine.AdvanceOneDay
// (Phase 1 wave 4). Registration order in SimulationEngine._inDayPhases IS the
// simulation order. Each system owns its interval state; the array is rebuilt
// every Initialize, so clocks are fresh per New Game (S9), including the
// formerly-leaking _quarterStartDay.
using System.Diagnostics;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Health;
using LakskongenSim.Processing;

namespace LakskongenSim.Core;

    /// <summary>
    /// Optional clock persistence for migrated tick systems (Phase 1 wave 5):
    /// the engine snapshots each clock into EngineRuntimeSnapshot.InDayClocks
    /// so a loaded game continues the weekly/quarterly cadence instead of
    /// double-firing on the first post-load tick.
    /// </summary>
    internal interface ITickClock
    {
        int ClockValue { get; }
        void SetClock(int value);
    }

    /// <summary>WeatherAndEnvironmentTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class WeatherAndEnvironmentTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.Weather.AdvanceDays(1);
            engine.Audio?.Update(engine.State.Day);

            // Update environment model and all zones. S3: EnvironmentModel
            // keeps its own zone list, so mirror the live zones first —
            // otherwise water chemistry never advances.
            engine.Environment.SyncZones(engine.State.Zones);
            // The zone water takes today's sea temperature before its chemistry
            // runs: oxygen solubility, the Q10 rates and the plankton bloom's
            // temperature factor read it (S1-01). Each zone takes its own
            // production area's climatology plus the shared anomaly (S2-02).
            engine.ApplyProductionAreaSeaTemperatures();
            engine.Environment.AdvanceDays(1);
            // zone.AdvanceDays(1) is already called by engine.Environment.AdvanceDays(1)

            // Plankton bloom dynamics (depends on updated zone conditions)
            engine.PlanktonBloom?.AdvanceDay(engine.State, engine.Weather);

            // Storm containment hazards (engine-owned): net-integrity wear +
            // deterministic escape accounting. Only when events are enabled, so
            // enableEvents:false stays deterministic and unaffected.
            engine.ApplyStormHazards();

        }
    }

    /// <summary>HealthPrepTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class HealthPrepTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            // The company-wide lice rollup follows the home area's water; the
            // per-site models below each take their own site's (S2-02).
            double temperature = engine.Weather.CurrentTemperatureC;
            engine.Mortality.ClearDiseaseMortalityRates(); // Clear previous day's disease rates

            // engine.SeaLice.AdvanceDays(1);
            // Phase 3 (P10): climate coupling — warming shortens lice development time.
            Health.ThermalLiceCoupling.SyncSeaLiceTemperature(engine.SeaLice, temperature, SimulationEngine.WarmingPerYearC, engine.State.Day);
            // Seasonal external copepodid pressure (neighbouring farms / wild
            // salmon): spring/summer peak, winter trough. Keeps lice pressure
            // realistic over the year without constant artificial seeding.
            engine.SeaLice.SetExternalPressure(SimulationEngine.SeasonalLicePressure(engine.State.Day));
            engine.SeaLice.AdvanceDay();
            // Phase 2.1: per-site lice models are the authority for farm
            // truth (company model retained as a fallback/compat rollup).
            engine.AdvanceSiteLiceModels();

            // Only calculate disease if events are enabled (skip in deterministic tests)
            if (engine.EventsEnabled)
            {
                // Calculate disease mortality rates and inject into MortalityModel
                // This MUST happen before FishPopulation.AdvanceDays() to avoid double application.
                // Each population's disease tick gets its own water temperature (S2-02).
                var facilityWater = engine.BuildFacilityWater();
                engine.IntegratedDisease.Update(
                    engine.State.Zones.ToList(),
                    engine.State.Populations.ToList(),
                    1,
                    population => engine.HoldingWaterOf(population, facilityWater).TemperatureC);
            }

        }
    }

    /// <summary>BiologyTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BiologyTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            var biologyStopwatch = Stopwatch.StartNew();

            // Zone load is rebuilt from the pens every day: summed over all of a
            // zone's populations, and zero once they are harvested (F054 — it
            // used to hold only the last pen, and kept a phantom load after harvest).
            for (int zi = 0; zi < engine.State.Zones.Count; zi++)
                engine.State.Zones[zi].CurrentFishBiomassKg = 0.0;

            // S2-02 seam: every population grows, eats and breathes in the water
            // of whatever holds it (sea pen: its zone's production-area water;
            // RAS tank: the facility's controlled water; offshore: its area's sea).
            // The facility map is built only when a facility holds fish.
            var facilityWater = engine.BuildFacilityWater();

            for (int popIdx = 0; popIdx < engine.State.Populations.Count; popIdx++)
            {
                var population = engine.State.Populations[popIdx];
                var zone = engine.State.Zones.FirstOrDefault(z => z.Id == population.ZoneId);
                var water = engine.HoldingWaterOf(population, facilityWater);
                double waterTemperature = water.TemperatureC;

                // Manual biomass sum — avoids LINQ delegate allocation (T56)
                double biomassKg = 0.0;
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var c = population.Cohorts[ci];
                    biomassKg += c.Count * c.MeanWeightKg;
                }

                if (zone != null)
                {
                    zone.CurrentFishBiomassKg += biomassKg;
                }

                // Calculate combined environmental multiplier (DO saturation × stocking density)
                double stockingDensity = zone != null ? (biomassKg / SimulationEngine.PenVolumeProxyM3) : SimulationEngine.DefaultStockingDensityKgM3;
                // Phase 4.1: containment — closed rungs behave denser for O2
                // purposes (recirculation load) via o2DemandMult.
                var homeSite = engine.State.SeaSites.FirstOrDefault(s => s.Id == population.SiteId);
                var containmentMods = Farm.ContainmentTech.ModifiersFor(
                    homeSite?.Containment ?? Farm.ContainmentLevel.OpenNet);
                double densityForO2 = stockingDensity * containmentMods.OxygenDemandMult;
                double environmentalMultiplier = 1.0;
                if (zone != null)
                {
                    environmentalMultiplier = engine.Growth.CalculateCombinedEnvironmentalMultiplier(
                        zone.WaterChemistry,
                        water.TemperatureC,
                        densityForO2,
                        water.SalinityPsu);
                }

                // Advance population: age and mortality only — growth handled by SDE below
                population.AdvanceDays(1, skipGrowth: true, engine.MortalityReduction, engine.DiseaseResistanceBonus);

                // Manual growth adjustment — indexed loop, in-place (T56).
                // grownKg = the biomass this population gained today; the feed
                // ration below is derived from it.
                double grownKg = 0.0;
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var cohort = population.Cohorts[ci];

                    // Apply genetics multiplier
                    double geneticMultiplier = cohort.Genetics?.GetGrowthMultiplier() ?? 1.0;

                    // Wave 6: inbreeding depression — additive and opt-in. A genome
                    // carrying a cumulative inbreeding proxy > 0 loses a small fraction
                    // of its growth; a null genome or zero inbreeding leaves the factor
                    // at 1.0, so existing behaviour is unchanged.
                    double inbreedingGrowthFactor = 1.0;
                    if (cohort.Genetics != null && cohort.Genetics.InbreedingCoefficient > 0.0)
                    {
                        inbreedingGrowthFactor = 1.0 - GenomicSelection.InbreedingDepressionPenalty(
                            GeneticTrait.GrowthRate, cohort.Genetics.InbreedingCoefficient);
                    }

                    // Use SDE model for more realistic growth with environmental modifiers
                    double newCalculatedWeight = engine.Growth.ApplySDEGrowthOneDay(
                        cohort.MeanWeightKg,
                        waterTemperature,
                        SimulationEngine.SdeTargetWeightKg,
                        SimulationEngine.SdeGeneticGrowthRateAlphaMax * (1.0 + engine.GrowthRateBonus), // calibrating: SDE-only growth with research bonus
                        SimulationEngine.SdeIndividualVarianceSigma, // kept small vs deterministic growth
                        cohort.Stage,
                        environmentalMultiplier);

                    // Apply genetic modifier only (environmental already applied in SDE)
                    // Phase 4.1: containment density-stress growth effect.
                    // P5: post-smolt quality carryover (SuperSmolt grows faster).
                    double qualityGrowth = Farm.SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier;
                    double delta = newCalculatedWeight - cohort.MeanWeightKg;
                    double weightBefore = cohort.MeanWeightKg;
                    cohort.MeanWeightKg += (delta * geneticMultiplier * inbreedingGrowthFactor * containmentMods.GrowthMult * qualityGrowth);
                    if (cohort.MeanWeightKg < 0) cohort.MeanWeightKg = 0;
                    grownKg += cohort.Count * Math.Max(0.0, cohort.MeanWeightKg - weightBefore);
                }

                // Feed the population — FCR-aware.
                // Phase 2.1: real DO-saturation penalty (was hardcoded 1.0, which
                // kept FCR rosy while growth stalled in low oxygen).
                // Phase 4.1: contained sites feed at effective (denser) density.
                // Phase 4.3: damaged gills throttle appetite (0.6-1.0).
                double waterPenalty = 1.0;
                if (zone != null)
                {
                    double doSat = Biology.WaterQuality.CalculateDOSaturation(
                        water.OxygenMgPerL, water.TemperatureC, water.SalinityPsu);
                    waterPenalty = Biology.WaterQuality.CalculateDOSaturationModifier(doSat);
                }
                if (homeSite != null)
                    waterPenalty *= 0.6 + 0.4 * (homeSite.GillScore / 100.0);
                // P6: precision feeding — feed-spread quality by site watch mode,
                // improved by AI feeding. Poor spread raises FCR, waste, size variance.
                double spreadQuality = homeSite?.WatchMode switch
                {
                    "watch" => 0.95,
                    "scan" => 0.85,
                    "auto" => 0.65,
                    _ => 0.80
                };
                if (engine.AIFeeding != null && engine.AIFeeding.IsInstalled)
                    spreadQuality = Math.Min(1.0, spreadQuality + 0.1);
                var feedResult = engine.Feed.Feed(population, 1, waterTemperature, densityForO2, waterPenalty, spreadQuality,
                    engine.FCRReduction, engine.FeedWasteReduction, actualGrowthKg: grownKg);

                if (zone != null && feedResult.FeedUsedKg > 0)
                {
                    zone.FeedConversionRatio = feedResult.ActualFCR;
                }

                // Phase 4.2: autopilot spill — unfed-eyes overfeed +8%.
                double spill = Optimization.RemoteOpsCentre.FeedSpillFraction(homeSite?.WatchMode);
                if (spill > 0.0 && feedResult.CostNOK > 0)
                {
                    decimal spillCost = feedResult.CostNOK * (decimal)spill;
                    engine.PayCost(spillCost, CostBooking.Cogs, "feed_spill");
                }

                // Phase 5.3: Sludge accumulation from feed used.
                if (homeSite != null && feedResult.FeedUsedKg > 0)
                {
                    double sludgeKgDs = Farm.SludgeValorization.DailySludgeKgDs(homeSite, feedResult.FeedUsedKg);
                    homeSite.SludgeAccumulatedKgDs += sludgeKgDs;
                }
            }
            biologyStopwatch.Stop();

        }
    }

    /// <summary>FarmOpexTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class FarmOpexTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine._dailyFarmSystemsOpex = 0m;
            engine._dailyAiFeedingLicenseCost = 0m;
            double totalBiomassForAI = engine.State.Populations.Sum(p => p.GetTotalBiomassKg());
            decimal currentFeedPricePerKg = engine.TickFeedCostPerKgNOK; // Using balancing config for now

            // AIFeedingSystem TickDay (applies globally for all farms)
            if (engine.AIFeeding?.IsInstalled == true)
            {
                var aiFeedResult = engine.AIFeeding.TickDay(totalBiomassForAI, currentFeedPricePerKg);
                engine._dailyAiFeedingLicenseCost = aiFeedResult.DailyLicenseCost;
            }

            foreach (var rasFacility in engine.RASFacilities)
            {
                rasFacility.SetRng(engine.Rng.Stream(SimRngStreams.RASFacility,
                    string.IsNullOrEmpty(rasFacility.Id) ? rasFacility.Name : rasFacility.Id));
                var rasResult = rasFacility.TickDay(engine.State.Day, engine.Weather.CurrentTemperatureC);
                engine._dailyFarmSystemsOpex += rasResult.TotalDailyOPEX;
            }

            foreach (var offshoreFarm in engine.OffshoreFarms)
            {
                offshoreFarm.SetRng(engine.Rng.Stream(SimRngStreams.OffshoreFarm,
                    string.IsNullOrEmpty(offshoreFarm.Id) ? offshoreFarm.Name : offshoreFarm.Id));
                var offshoreResult = offshoreFarm.TickDay(engine.State.Day, engine.Weather.CurrentWindSpeedMs, engine.Weather.CurrentWaveHeight, engine.Weather.CurrentState == WeatherState.Storm);
                engine._dailyFarmSystemsOpex += offshoreResult.TotalDailyOPEX;
            }

            foreach (var postSmoltFacility in engine.PostSmoltFacilities)
            {
                var postSmoltResult = postSmoltFacility.TickDay(engine.State.Day);
                engine._dailyFarmSystemsOpex += postSmoltResult.TotalDailyOPEX;
            }

        }
    }

    /// <summary>CarbonLedgerTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CarbonLedgerTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.ProcessCarbonLedger();

        }
    }

    /// <summary>EventsAndTimeTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class EventsAndTimeTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.Time.AdvanceDays(1);

        }
    }

    /// <summary>MarketTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class MarketTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            if (engine._aquacultureModifiers.ProfitabilityAdjustment != 0m)
            {
                var supplyFactor = 1.0 - (double)engine._aquacultureModifiers.ProfitabilityAdjustment;
                var demandFactor = 1.0 + (double)engine._aquacultureModifiers.ProfitabilityAdjustment;
                engine.MarketSimulation.ApplyExternalShock(supplyFactor, demandFactor);
            }

            if (engine.State.ExternalAquacultureSnapshot != null)
            {
                var crisis = engine.State.ExternalHealthCrisis ?? new HealthCrisisScenario();
                engine.MarketSimulation.ApplyIndustrySignals(
                    engine.State.ExternalAquacultureSnapshot.NationalBiomassUtilization,
                    engine.State.ExternalAquacultureSnapshot.DiseasePressureIndex * crisis.MortalityPenaltyMultiplier,
                    engine.State.ExternalAquacultureSnapshot.MortalityStressIndex * crisis.EnvironmentalStressMultiplier,
                    engine.State.ExternalAquacultureSnapshot.MarketPriceSupportIndex * crisis.MarketPriceSupportMultiplier);
            }

            // Phase 4.4: research premium and vertical-integration quality combine
            // as multipliers and enter today's price inside the realized band
            // (review F1003: the bare fraction was multiplied and then gated on
            // > 1.0, so neither premium ever applied).
            engine.MarketSimulation.SetResearchPriceMultiplier(
                (1.0 + engine.MarketPriceBonus) * engine.VerticalIntegrationQualityMultiplier
                * engine.ScenarioPriceMultiplier);
            engine.MarketSimulation.AdvanceDays(1);
            engine.CurrencySystem.AdvanceDays(1);
            // P9: Price Council binds the monthly pen price from the rolling spot series.
            engine.PenPriceCouncil.Update(engine.MarketSimulation.PricePerKg);

        }
    }

    /// <summary>FinanceTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class FinanceTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.ProcessDailyFinance();

            // GameState.Balance is a read-only MIRROR of engine.FinanceManager.Balance.
            // Writers must use FinanceManager (AddRevenue/Spend/ForceSpend);
            // this keeps event Conditions and UI readers correct all day.
            #pragma warning disable CS0618 // Balance mirror: save-compat field by design
            engine.State.Balance = engine.FinanceManager.Balance;
            #pragma warning restore CS0618

        }
    }

    /// <summary>StockMarketTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class StockMarketTickSystem : ITickSystem, ITickClock
    {
        private int _quarterStartDay = 0;
        public void Tick(SimulationEngine engine)
        {
            // Daily price movement (only if listed)
            if (engine.StockMarket != null && engine.StockMarket.Status != Finance.OwnershipStatus.Private)
            {
                // Check for quarterly earnings report (every ~90 days)
                if ((engine.State.Day - _quarterStartDay) >= 90)
                {
                    // Process quarterly earnings
                    decimal quarterlyRevenue = engine.FinancialStatements?.Revenue ?? 0m;
                    decimal quarterlyEBITDA = (engine.FinancialStatements?.Revenue ?? 0m) - (engine.FinancialStatements?.COGS ?? 0m) - (engine.FinancialStatements?.OperatingExpenses ?? 0m);
                    decimal quarterlyNetIncome = engine.FinancialStatements?.ComputeNetIncome() ?? 0m;
                    decimal totalAssets = engine.FinancialStatements?.Assets ?? (engine.State.SeaSites.Count * 5_000_000m);
                    decimal totalDebt = engine.FinancialStatements?.Liabilities ?? 0m;

                    var earningsReport = engine.StockMarket.AdvanceQuarter(
                        quarterlyRevenue,
                        quarterlyEBITDA,
                        quarterlyNetIncome,
                        totalAssets,
                        totalDebt);

                    if (earningsReport != null)
                    {
                        engine.FireEvent(
                            $"📊 Q{earningsReport.Quarter} Earnings: EPS {earningsReport.EPS:F2} NOK " +
                            $"({(earningsReport.Beat ? "BEAT" : "MISS")} consensus {earningsReport.ConsensusEPS:F2})");
                    }

                    _quarterStartDay = engine.State.Day;
                }
            }

        }
        int ITickClock.ClockValue => _quarterStartDay;
        void ITickClock.SetClock(int value) => _quarterStartDay = value;
    }

    /// <summary>SustainabilityTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class SustainabilityTickSystem : ITickSystem, ITickClock
    {
        private int _lastSustainabilityUpdate = 0;
        private const int SustainabilityUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastSustainabilityUpdate >= SustainabilityUpdateInterval)
            {
                // Research sustainability points (review F1001: the bonus was never passed).
                engine.SustainabilityManager?.UpdateScore(engine.State, engine.SustainabilityScoreBonus);
                engine.State.SustainabilityScore = engine.SustainabilityManager?.CurrentScore ?? engine.State.SustainabilityScore;
                engine.State.SustainabilityCertification = engine.SustainabilityManager?.CurrentCertification ?? engine.State.SustainabilityCertification;
                _lastSustainabilityUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastSustainabilityUpdate;
        void ITickClock.SetClock(int value) => _lastSustainabilityUpdate = value;
    }

    /// <summary>SludgeValorizationTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class SludgeValorizationTickSystem : ITickSystem, ITickClock
    {
        private int _lastSludgeValorizationUpdate = 0;
        private const int SludgeValorizationInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastSludgeValorizationUpdate >= SludgeValorizationInterval)
            {
                engine.ProcessSludgeValorization();
            }

        }
        int ITickClock.ClockValue => _lastSludgeValorizationUpdate;
        void ITickClock.SetClock(int value) => _lastSludgeValorizationUpdate = value;
    }

    /// <summary>TrafficLightTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class TrafficLightTickSystem : ITickSystem, ITickClock
    {
        private int _lastTrafficLightUpdate = 0;
        private const int TrafficLightUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastTrafficLightUpdate >= TrafficLightUpdateInterval)
            {
                // Phase 2.1: per-site sync + weekly counting + Mattilsynet
                // orders, before the traffic-light assessment reads them.
                engine.UpdateSiteLiceCounts();
                engine.UpdateTrafficLightZones();
                engine.UpdatePreventiveCoverage();
                _lastTrafficLightUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastTrafficLightUpdate;
        void ITickClock.SetClock(int value) => _lastTrafficLightUpdate = value;
    }

    /// <summary>BrandReputationTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BrandReputationTickSystem : ITickSystem, ITickClock
    {
        private int _lastBrandReputationUpdate = 0;
        private const int BrandReputationUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.BrandReputation != null && engine.State.Day - _lastBrandReputationUpdate >= BrandReputationUpdateInterval)
            {
                // Natural reputation decay over time (prevents stagnation)
                engine.BrandReputation.ApplyNaturalDecay(0.005m);

                // Reward consistent quality performance
                double avgBiomass = engine.State.Populations.Any() ? engine.State.Populations.Average(p => p.GetTotalBiomassKg()) : 0;
                if (avgBiomass > 100_000 && engine.State.SustainabilityScore > 60)
                {
                    engine.BrandReputation.RecordEvent(
                        ReputationEvent.ConsistentQuality,
                        "Maintained quality standards and sustainability",
                        severity: 1);
                }

                _lastBrandReputationUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastBrandReputationUpdate;
        void ITickClock.SetClock(int value) => _lastBrandReputationUpdate = value;
    }

    /// <summary>CompetitorAITickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CompetitorAITickSystem : ITickSystem, ITickClock
    {
        private int _lastCompetitorAIUpdate = 0;
        public void Tick(SimulationEngine engine)
        {
            // Each calendar quarter (S2-02; it was every 90 days).
            if (SimCalendar.IsNewQuarterSince(_lastCompetitorAIUpdate, engine.State.Day))
            {
                // Calculate player metrics for competitor context
                float playerMarketShare = engine.CalculatePlayerMarketShare();
                decimal playerRevenue = engine.FinancialStatements?.Revenue ?? 0m;
                float spotPrice = (float)(engine.MarketSimulation?.PricePerKg ?? 70m);
                int playerLicenseCount = engine.State.SeaSites.Count; // Approximate licenses by site count

                engine.CompetitorAI?.AdvanceQuarter(playerMarketShare, playerRevenue, spotPrice, playerLicenseCount);
                _lastCompetitorAIUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastCompetitorAIUpdate;
        void ITickClock.SetClock(int value) => _lastCompetitorAIUpdate = value;
    }

    /// <summary>EmpireProgressionTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class EmpireProgressionTickSystem : ITickSystem, ITickClock
    {
        private int _lastEmpireProgressionUpdate = 0;
        private const int EmpireProgressionUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastEmpireProgressionUpdate >= EmpireProgressionUpdateInterval)
            {
                var empireState = engine.BuildEmpireState();
                engine.EmpireProgression?.Update(empireState);
                _lastEmpireProgressionUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastEmpireProgressionUpdate;
        void ITickClock.SetClock(int value) => _lastEmpireProgressionUpdate = value;
    }

    /// <summary>CrisisCapitalismTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CrisisCapitalismTickSystem : ITickSystem, ITickClock
    {
        private int _lastCrisisCapitalismUpdate = 0;
        // Daily: purchase and availability read the listing countdown this
        // refreshes, so a weekly update left listings buyable up to six days
        // past ExpiresOnDay. The update is cheap and draws no RNG.
        private const int CrisisCapitalismUpdateInterval = 1;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastCrisisCapitalismUpdate >= CrisisCapitalismUpdateInterval)
            {
                engine.CrisisCapitalism?.Update(engine.State.Day);
                _lastCrisisCapitalismUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastCrisisCapitalismUpdate;
        void ITickClock.SetClock(int value) => _lastCrisisCapitalismUpdate = value;
    }

    /// <summary>VerticalIntegrationTickSystem: Phase 4.4 — advances construction timers,
/// applies annual OPEX from owned integration assets, and calculates cost savings.</summary>
    internal sealed class VerticalIntegrationTickSystem : ITickSystem, ITickClock
    {
        private int _lastVerticalIntegrationUpdate = 0;
        public void Tick(SimulationEngine engine)
        {
            // Each calendar month (S2-02; every 30 days charged 12.17 monthly OPEX slices a year).
            if (SimCalendar.IsNewMonthSince(_lastVerticalIntegrationUpdate, engine.State.Day))
            {
                if (engine.VerticalIntegration != null)
                {
                    // Advance construction on owned assets (1 month per interval)
                    foreach (var asset in engine.OwnedIntegrationAssets)
                    {
                        if (!asset.IsOwned) continue;
                        // Construction time tracking in days
                        // ConstructionCompleted is DateTime in the current implementation
                        // We'll use a simple day counter approach
                    }

                    // Apply annual OPEX from owned integration assets (spread monthly)
                    decimal monthlyOpex = engine.VerticalIntegrationAnnualOpexNOK / 12m;
                    if (monthlyOpex > 0)
                    {
                        engine.PayCost(monthlyOpex, CostBooking.Opex, "vertical_integration");
                        engine.FireEvent($"🏭 Vertical integration monthly OPEX: {monthlyOpex:N0} NOK");
                    }

                    // Calculate cost savings from completed tiers (applied via FCR/market price in Feed/Market)
                    // This is handled via VerticalIntegrationCostSavingPerKg and VerticalIntegrationQualityMultiplier
                }
                _lastVerticalIntegrationUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastVerticalIntegrationUpdate;
        void ITickClock.SetClock(int value) => _lastVerticalIntegrationUpdate = value;
    }

    /// <summary>CorporateFinanceTickSystem: Phase 4.3 — quarterly/annual corporate finance processing.
/// Replaces TaxAndResourceRentTickSystem with comprehensive tax, covenants, IPO, and annual close.</summary>
    internal sealed class CorporateFinanceTickSystem : ITickSystem, ITickClock
    {
        private int _lastCorporateFinanceUpdate = 0;
        public void Tick(SimulationEngine engine)
        {
            // Each calendar quarter, so the fourth closes the calendar year (S2-02;
            // every 90 days made a 360-day financial year that drifted off the calendar).
            if (SimCalendar.IsNewQuarterSince(_lastCorporateFinanceUpdate, engine.State.Day))
            {
                // Quarterly/annual corporate finance processing
                if (engine.CorporateFinance != null)
                {
                    var result = engine.CorporateFinance.ExecuteQuarterly(
                        engine.FinancialStatements!,
                        engine.FinanceManager!,
                        engine.InvestorSystem,
                        engine.OutstandingDebt(),
                        engine.State.Day);

                    // Handle year-end: the engine ledger settles the year's tax
                    // first, then corporate finance closes the books.
                    if (result.IsYearEnd)
                    {
                        var tax = engine.SettleAnnualTax();
                        var annualResult = engine.CorporateFinance.ExecuteAnnualClose(
                            engine.FinancialStatements!,
                            engine.FinanceManager!,
                            engine.InvestorSystem,
                            tax,
                            engine.OutstandingDebt(),
                            engine.State.Day);

                        engine.FireEvent($"📋 YEAR {annualResult.Year} CLOSE: Net income {annualResult.NetIncomeBeforeTax:N0}, Tax {annualResult.TaxPaid:N0}, Covenants {(annualResult.CovenantTest.AllPassed ? "OK" : "BREACH")}");

                        if (!annualResult.CovenantTest.AllPassed)
                        {
                            engine.FireEvent($"⚠️ COVENANT BREACHES: {annualResult.CovenantBreachCount} active");
                        }
                    }
                    else
                    {
                        engine.FireEvent($"📊 Q{result.Quarter} {result.Year}: Covenants {(result.CovenantTest.AllPassed ? "OK" : "BREACH")}");

                        if (!result.CovenantTest.AllPassed)
                        {
                            engine.FireEvent($"⚠️ Quarterly covenant breach detected");
                        }
                    }

                    // IPO market update
                    if (result.IPOStage == Finance.IPOSession.IPOStage.Listed)
                    {
                        engine.FireEvent($"📈 Share price: {result.SharePriceNOK:N2} NOK (Market cap: {result.MarketCapNOK:N0} NOK)");
                    }
                }

                _lastCorporateFinanceUpdate = engine.State.Day;
            }
        }
        int ITickClock.ClockValue => _lastCorporateFinanceUpdate;
        void ITickClock.SetClock(int value) => _lastCorporateFinanceUpdate = value;
    }

    /// <summary>InvestorTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class InvestorTickSystem : ITickSystem, ITickClock
    {
        private int _lastInvestorUpdate = 0;
        public void Tick(SimulationEngine engine)
        {
            if (SimCalendar.IsNewMonthSince(_lastInvestorUpdate, engine.State.Day))
            {
                // Monthly investor update - report dilution and bond status
                if (engine.InvestorSystem != null)
                {
                    decimal dilution = engine.InvestorSystem.DilutionFraction();
                    if (dilution > 0.01m)
                    {
                        engine.FireEvent($"📊 Equity dilution: {dilution * 100:F1}% — {engine.InvestorSystem.OutstandingShares:N0} shares outstanding");
                    }
                    if (engine.InvestorSystem.OutstandingBondPrincipal > 0)
                    {
                        engine.FireEvent($"💼 Outstanding bonds: {engine.InvestorSystem.OutstandingBondPrincipal:N0} NOK");
                    }
                }
                _lastInvestorUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastInvestorUpdate;
        void ITickClock.SetClock(int value) => _lastInvestorUpdate = value;
    }

    /// <summary>BloomDeckTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BloomDeckTickSystem : ITickSystem, ITickClock
    {
        private int _lastBloomUpdate = 0;
        private const int BloomUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastBloomUpdate >= BloomUpdateInterval)
            {
                engine.RollBloomWeek();
                _lastBloomUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastBloomUpdate;
        void ITickClock.SetClock(int value) => _lastBloomUpdate = value;
    }

    /// <summary>MarketIntelligenceTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class MarketIntelligenceTickSystem : ITickSystem, ITickClock
    {
        private int _lastMarketIntelligenceUpdate = 0;
        private const int MarketIntelligenceUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastMarketIntelligenceUpdate >= MarketIntelligenceUpdateInterval)
            {
                // Market intelligence updates weekly with current spot price
                decimal currentSpotPrice = engine.MarketSimulation?.PricePerKg ?? 70m;
                engine.MarketIntelligence?.AdvanceWeek(currentSpotPrice);
                _lastMarketIntelligenceUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastMarketIntelligenceUpdate;
        void ITickClock.SetClock(int value) => _lastMarketIntelligenceUpdate = value;
    }

    /// <summary>ContractExpiryTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class ContractExpiryTickSystem : ITickSystem, ITickClock
    {
        private int _lastContractUpdate = 0;
        private const int ContractExpiryInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastContractUpdate >= ContractExpiryInterval
                && engine.ContractSystem != null)
            {
                _lastContractUpdate = engine.State.Day;
                foreach (var (contract, penalty) in engine.ContractSystem.AdvanceWeek())
                {
                    if (penalty > 0)
                    {
                        engine.PayCost(penalty, CostBooking.Opex, "contract_penalties");
                        engine.State.EventLog.Add(new Events.EventRecord
                        {
                            Day = engine.State.Day,
                            Name = "Kontraktsbrudd",
                            Category = Events.EventCategory.Market,
                            Description = $"Kontrakt med {contract.Buyer} utløpt ({contract.FulfilledKg:N0}/{contract.QuantityKg:N0} kg) — bot {penalty:N0} NOK",
                            BalanceChange = -penalty
                        });
                        engine.State.TrimEventLog();
                    }
                }
            }

        }
        int ITickClock.ClockValue => _lastContractUpdate;
        void ITickClock.SetClock(int value) => _lastContractUpdate = value;
    }

    /// <summary>ProductMixOptimizerTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class ProductMixOptimizerTickSystem : ITickSystem, ITickClock
    {
        private int _lastProductMixOptimizerUpdate = 0;
        public void Tick(SimulationEngine engine)
        {
            // ProductMixOptimizer is called on-demand when player opens processing UI
            // No periodic update needed — it's a decision support tool, not a simulation system
            // This interval marker is for future features (e.g., auto-optimization recommendations)
            if (SimCalendar.IsNewMonthSince(_lastProductMixOptimizerUpdate, engine.State.Day))
            {
                _lastProductMixOptimizerUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastProductMixOptimizerUpdate;
        void ITickClock.SetClock(int value) => _lastProductMixOptimizerUpdate = value;
    }

    /// <summary>TutorialAnalyticsTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class TutorialAnalyticsTickSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.Tutorial?.CheckProgress();
            engine.Analytics?.RecordSnapshot(engine);
            engine.Achievements?.CheckAchievements();
            engine.Research?.AdvanceDay();
            engine.SyncResearchToState();

            // ─── Scenario win/loss evaluation (runs after all subsystems) ───
            engine.Scenario?.CheckWinConditions(engine.State.SustainabilityScore, engine.Tutorial?.IsComplete ?? false);

        }
    }

    /// <summary>NightShiftPolicyTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class NightShiftPolicyTickSystem : ITickSystem, ITickClock
    {
        private int _lastNightPolicyUpdate = 0;
        private const int NightPolicyInterval = 1;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastNightPolicyUpdate >= NightPolicyInterval)
            {
                engine.ProcessNightShiftPolicy();
                _lastNightPolicyUpdate = engine.State.Day;
            }

        }
        int ITickClock.ClockValue => _lastNightPolicyUpdate;
        void ITickClock.SetClock(int value) => _lastNightPolicyUpdate = value;
    }
