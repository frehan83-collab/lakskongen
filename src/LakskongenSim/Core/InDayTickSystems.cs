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

    /// <summary>WeatherAndEnvironmentSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class WeatherAndEnvironmentSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.Weather.AdvanceDays(1);
            double temperature = engine.Weather.CurrentTemperatureC;
            engine.Audio?.Update(engine.State.Day);

            // Update environment model and all zones. S3: EnvironmentModel
            // keeps its own zone list, so mirror the live zones first —
            // otherwise water chemistry never advances.
            engine.Environment.SyncZones(engine.State.Zones);
            engine.Environment.AdvanceDays(1);
            // NOTE: Do NOT override zone temperatures here — zones maintain their own temperatures
            // zone.AdvanceDays(1) is already called by engine.Environment.AdvanceDays(1)

            // Plankton bloom dynamics (depends on updated zone conditions)
            engine.PlanktonBloom?.AdvanceDay(engine.State, engine.Weather);

            // Storm containment hazards (engine-owned): net-integrity wear +
            // deterministic escape accounting. Only when events are enabled, so
            // enableEvents:false stays deterministic and unaffected.
            engine.ApplyStormHazards();

        }
    }

    /// <summary>HealthPrepSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class HealthPrepSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
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
            engine.AdvanceSiteLiceModels(temperature);

            // Only calculate disease if events are enabled (skip in deterministic tests)
            if (engine.EventsEnabled)
            {
                // Calculate disease mortality rates and inject into MortalityModel
                // This MUST happen before FishPopulation.AdvanceDays() to avoid double application
                engine.IntegratedDisease.Update(
                    engine.State.Zones.ToList(),
                    engine.State.Populations.ToList(),
                    1);
            }

        }
    }

    /// <summary>BiologySystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BiologySystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            double temperature = engine.Weather.CurrentTemperatureC;
            var biologyStopwatch = Stopwatch.StartNew();
            for (int popIdx = 0; popIdx < engine.State.Populations.Count; popIdx++)
            {
                var population = engine.State.Populations[popIdx];
                var zone = engine.State.Zones.FirstOrDefault(z => z.Id == population.ZoneId);

                // Manual biomass sum — avoids LINQ delegate allocation (T56)
                double biomassKg = 0.0;
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var c = population.Cohorts[ci];
                    biomassKg += c.Count * c.MeanWeightKg;
                }

                if (zone != null)
                {
                    zone.CurrentFishBiomassKg = biomassKg;
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
                        zone.WaterChemistry.Temperature,
                        densityForO2,
                        35.0);
                }

                // Advance population: age and mortality only — growth handled by SDE below
                population.AdvanceDays(1, skipGrowth: true);

                // Manual growth adjustment — indexed loop, in-place (T56)
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
                        temperature,
                        SimulationEngine.SdeTargetWeightKg,
                        SimulationEngine.SdeGeneticGrowthRateAlphaMax, // calibrating: SDE-only growth
                        SimulationEngine.SdeIndividualVarianceSigma, // kept small vs deterministic growth
                        cohort.Stage,
                        environmentalMultiplier);

                    // Apply genetic modifier only (environmental already applied in SDE)
                    // Phase 4.1: containment density-stress growth effect.
                    // P5: post-smolt quality carryover (SuperSmolt grows faster).
                    double qualityGrowth = Farm.SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier;
                    double delta = newCalculatedWeight - cohort.MeanWeightKg;
                    cohort.MeanWeightKg += (delta * geneticMultiplier * inbreedingGrowthFactor * containmentMods.GrowthMult * qualityGrowth);
                    if (cohort.MeanWeightKg < 0) cohort.MeanWeightKg = 0;
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
                        zone.WaterChemistry.OxygenO2, temperature);
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
                var feedResult = engine.Feed.Feed(population, 1, temperature, densityForO2, waterPenalty, spreadQuality);

                if (zone != null && feedResult.FeedUsedKg > 0)
                {
                    zone.FeedConversionRatio = feedResult.ActualFCR;
                }

                // Phase 4.2: autopilot spill — unfed-eyes overfeed +8%.
                double spill = Optimization.RemoteOpsCentre.FeedSpillFraction(homeSite?.WatchMode);
                if (spill > 0.0 && feedResult.CostNOK > 0)
                {
                    decimal spillCost = feedResult.CostNOK * (decimal)spill;
                    if (!engine.FinanceManager.Spend(spillCost))
                        engine.FinanceManager.ForceSpend(spillCost);
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

    /// <summary>FarmOpexSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class FarmOpexSystem : ITickSystem
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
                var rasResult = rasFacility.TickDay(engine.State.Day, engine.Weather.CurrentTemperatureC);
                engine._dailyFarmSystemsOpex += rasResult.TotalDailyOPEX;
            }

            foreach (var offshoreFarm in engine.OffshoreFarms)
            {
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

    /// <summary>CarbonLedgerSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CarbonLedgerSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.ProcessCarbonLedger();

        }
    }

    /// <summary>EventsAndTimeSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class EventsAndTimeSystem : ITickSystem
    {
        public void Tick(SimulationEngine engine)
        {
            engine.Time.AdvanceDays(1);

        }
    }

    /// <summary>MarketSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class MarketSystem : ITickSystem
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

            engine.MarketSimulation.AdvanceDays(1);
            engine.CurrencySystem.AdvanceDays(1);
            // P9: Price Council binds the monthly pen price from the rolling spot series.
            engine.PenPriceCouncil.Update(engine.MarketSimulation.PricePerKg);

        }
    }

    /// <summary>FinanceSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class FinanceSystem : ITickSystem
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

    /// <summary>StockMarketSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class StockMarketSystem : ITickSystem
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
    }

    /// <summary>SustainabilitySystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class SustainabilitySystem : ITickSystem
    {
        private int _lastSustainabilityUpdate = 0;
        private const int SustainabilityUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastSustainabilityUpdate >= SustainabilityUpdateInterval)
            {
                engine.SustainabilityManager?.UpdateScore(engine.State);
                engine.State.SustainabilityScore = engine.SustainabilityManager?.CurrentScore ?? engine.State.SustainabilityScore;
                engine.State.SustainabilityCertification = engine.SustainabilityManager?.CurrentCertification ?? engine.State.SustainabilityCertification;
                _lastSustainabilityUpdate = engine.State.Day;
            }

        }
    }

    /// <summary>SludgeValorizationSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class SludgeValorizationSystem : ITickSystem
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
    }

    /// <summary>TrafficLightSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class TrafficLightSystem : ITickSystem
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
    }

    /// <summary>BrandReputationSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BrandReputationSystem : ITickSystem
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
    }

    /// <summary>CompetitorAISystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CompetitorAISystem : ITickSystem
    {
        private int _lastCompetitorAIUpdate = 0;
        private const int CompetitorAIUpdateInterval = 90;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastCompetitorAIUpdate >= CompetitorAIUpdateInterval)
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
    }

    /// <summary>EmpireProgressionSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class EmpireProgressionSystem : ITickSystem
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
    }

    /// <summary>CrisisCapitalismTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class CrisisCapitalismTickSystem : ITickSystem
    {
        private int _lastCrisisCapitalismUpdate = 0;
        private const int CrisisCapitalismUpdateInterval = 7;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastCrisisCapitalismUpdate >= CrisisCapitalismUpdateInterval)
            {
                engine.CrisisCapitalism?.Update();
                _lastCrisisCapitalismUpdate = engine.State.Day;
            }

        }
    }

    /// <summary>VerticalIntegrationTickSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class VerticalIntegrationTickSystem : ITickSystem
    {
        private int _lastVerticalIntegrationUpdate = 0;
        private const int VerticalIntegrationUpdateInterval = 30;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastVerticalIntegrationUpdate >= VerticalIntegrationUpdateInterval)
            {
                // VerticalIntegrationSystem doesn't have an explicit Update() method
                // but we track the interval for future use (e.g., construction completion checks)
                _lastVerticalIntegrationUpdate = engine.State.Day;
            }

        }
    }

    /// <summary>TaxAndResourceRentSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class TaxAndResourceRentSystem : ITickSystem
    {
        private int _lastTaxUpdate = 0;
        private const int TaxUpdateInterval = 90;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastTaxUpdate >= TaxUpdateInterval)
            {
                decimal netIncome = engine.FinancialStatements?.ComputeNetIncome() ?? 0m;
                if (netIncome > 0 && engine.TaxSystem != null)
                {
                    decimal quarterlyTax = engine.TaxSystem.ComputeTax(netIncome);
                    if (quarterlyTax > 0)
                    {
                        if (!engine.FinanceManager.Spend(quarterlyTax))
                        {
                            engine.FireEvent($"💸 Cannot pay {quarterlyTax:N0} NOK in quarterly taxes. Balance: {engine.FinanceManager.Balance:N0} NOK");
                            engine.FinanceManager.ForceSpend(quarterlyTax);
                        }
                        else
                        {
                            engine.FinancialStatements?.RecordTax(quarterlyTax);
                        }
                    }
                }
                _lastTaxUpdate = engine.State.Day;
            }

            // PHASE 7F2: RESOURCE-RENT TAX (quarterly, P9, opt-in)
            // 25% cash-flow tax on the sea phase with a 70M NOK standard deduction.
            // Opt-in so default/legacy economies and tests are unaffected.
            if (engine.EnableResourceRentTax && engine.State.Day - _lastTaxUpdate == 0)
            {
                decimal seaProfit = engine.FinancialStatements?.ComputeNetIncome() ?? 0m;
                decimal rr = LakskongenSim.Finance.ResourceRentTax.Compute(seaProfit);
                if (rr > 0)
                {
                    if (!engine.FinanceManager.Spend(rr))
                    {
                        engine.FireEvent($"💸 Cannot pay {rr:N0} NOK resource-rent tax. Balance: {engine.FinanceManager.Balance:N0} NOK");
                        engine.FinanceManager.ForceSpend(rr);
                    }
                    engine.ResourceRentPaid += rr;
                    engine.FinancialStatements?.RecordTax(rr);
                    engine.State.EventLog.Add(new Events.EventRecord
                    {
                        Day = engine.State.Day,
                        Name = "Ressursrente",
                        Category = Events.EventCategory.Regulation,
                        Description = $"Ressursrenteskatt (25%, 70M-fradrag): {rr:N0} NOK",
                        BalanceChange = -rr
                    });
                    engine.State.TrimEventLog();
                }
            }

        }
    }

    /// <summary>InvestorSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class InvestorSystem : ITickSystem
    {
        private int _lastInvestorUpdate = 0;
        private const int InvestorUpdateInterval = 30;
        public void Tick(SimulationEngine engine)
        {
            if (engine.State.Day - _lastInvestorUpdate >= InvestorUpdateInterval)
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
    }

    /// <summary>BloomDeckSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class BloomDeckSystem : ITickSystem
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
    }

    /// <summary>MarketIntelligenceSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class MarketIntelligenceSystem : ITickSystem
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
    }

    /// <summary>ContractExpirySystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class ContractExpirySystem : ITickSystem
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
                        if (!engine.FinanceManager.Spend(penalty))
                            engine.FinanceManager.ForceSpend(penalty);
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
    }

    /// <summary>ProductMixOptimizerSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class ProductMixOptimizerSystem : ITickSystem
    {
        private int _lastProductMixOptimizerUpdate = 0;
        private const int ProductMixOptimizerUpdateInterval = 30;
        public void Tick(SimulationEngine engine)
        {
            // ProductMixOptimizer is called on-demand when player opens processing UI
            // No periodic update needed — it's a decision support tool, not a simulation system
            // This interval marker is for future features (e.g., auto-optimization recommendations)
            if (engine.State.Day - _lastProductMixOptimizerUpdate >= ProductMixOptimizerUpdateInterval)
            {
                _lastProductMixOptimizerUpdate = engine.State.Day;
            }

        }
    }

    /// <summary>TutorialAnalyticsSystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class TutorialAnalyticsSystem : ITickSystem
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

    /// <summary>NightShiftPolicySystem: migrated AdvanceOneDay phase (Phase 1 wave 4).</summary>
    internal sealed class NightShiftPolicySystem : ITickSystem
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
    }
