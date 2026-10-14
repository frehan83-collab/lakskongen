using System.Linq;
using Xunit;
using LakskongenSim.Calibration;
using LakskongenSim.Biology;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>
    /// Phase A — Calibration Spine guards.
    ///
    /// One test per domain. Each assertion names the real Norwegian reference and
    /// the tolerance that separates "calibrated" from "drifted". If art/reality/code
    /// drift out of these bands the suite fails loudly, which is the whole point of
    /// routing every balance value through <see cref="CalibrationRegistry"/>.
    ///
    /// References are the ones recorded in docs/REALISM_CHECKLIST.md (Oct-2026 review):
    /// Fish Pool / Euronext SISALMONI, Fiskeridir Key Figures, Mowi/SalMar quarterlies,
    /// Vet Institute Fish Health Report, Mattilsynet/BarentsWatch, Skatteetaten Prop.1 LS.
    /// </summary>
    public class CalibrationGuardTests
    {
        private static readonly CalibrationRegistry C = CalibrationRegistry.Load();

        [Fact]
        public void Version_IsPinned()
        {
            // Bump this alongside any reconciled value change.
            Assert.Equal(CalibrationRegistry.DefaultVersion, C.Version);
        }

        [Fact]
        public void Economy_MatchesRealReferences()
        {
            // Salmon spot: Oct-2026 ~65-73 NOK/kg (Fish Pool/Euronext SISALMONI).
            // Asserted against the ~70 anchor with a ±8 NOK/kg tolerance.
            Assert.InRange(C.Economy.BaseSalmonPricePerKgNOK, 62m, 78m);
            // Smolt purchase price ~12 NOK/fish (Fiskeridir smolt stats); 8-20 band.
            Assert.InRange(C.Economy.SmoltCostPerFishNOK, 8m, 20m);
            Assert.True(C.Economy.StartingCapitalNOK > 0, "starting capital must be positive");
        }

        [Fact]
        public void Labor_MatchesRealReferences()
        {
            // Aquaculture worker monthly salary 2024 ~48k NOK (industry wage stats);
            // ±12k tolerance; manager ~70k ±15k.
            Assert.InRange(C.Labor.WorkerMonthlySalaryNOK, 36_000m, 60_000m);
            Assert.InRange(C.Labor.ManagerMonthlySalaryNOK, 55_000m, 85_000m);
            Assert.True(C.Labor.WorkersPerSite > 0);
        }

        [Fact]
        public void Infrastructure_MatchesRealReferences()
        {
            // Sea-site capex ~6M, hatchery ~18M, processing plant ~55M, feed mill ~35M.
            // No single public reference: tolerances are game-default ±50%.
            Assert.InRange(C.Infrastructure.SeaSiteBuildCostNOK, 3_000_000m, 12_000_000m);
            Assert.InRange(C.Infrastructure.HatcheryBuildCostNOK, 9_000_000m, 36_000_000m);
            Assert.InRange(C.Infrastructure.ProcessingPlantCostNOK, 27_500_000m, 110_000_000m);
            Assert.InRange(C.Infrastructure.FeedMillCostNOK, 17_500_000m, 70_000_000m);
            // Production limit must equal the MAB constant (see Licensing).
            Assert.Equal(C.Licensing.MABLimitKgPerLicense, C.Infrastructure.MaxBiomassPerSiteKg);
        }

        [Fact]
        public void Operations_MatchesRealReferences()
        {
            // Harvest/wellboat ~3-5 NOK/kg; processing ~8-10 NOK/kg;
            // transport ~0.02-0.03 NOK/kg/km.
            Assert.InRange(C.Operations.HarvestCostPerKgNOK, 3.0m, 5.0m);
            Assert.InRange(C.Operations.ProcessingCostPerKgNOK, 7m, 11m);
            Assert.InRange(C.Operations.TransportCostPerKgPerKmNOK, 0.015m, 0.035m);
        }

        [Fact]
        public void Health_MatchesRealReferences()
        {
            // Lice action limit: 0.5 adult females/fish (Mattilsynet/BarentsWatch).
            // Tight ±0.01 tolerance — this is a hard regulatory trigger.
            Assert.InRange(C.Health.SeaLiceThresholdPerFish, 0.49, 0.51);
            // Whole-cycle sea mortality 14-16%, 2024 = 15.4% (Vet Institute).
            Assert.InRange(C.Health.SeaMortalityPerCycle, 0.13, 0.17);
            // Baseline daily mortality stays in a sane husbandry band.
            Assert.InRange(C.Health.MortalityRateBasePercent, 0.01, 0.05);
        }

        [Fact]
        public void Finance_MatchesRealReferences()
        {
            // Resource rent tax 25% effective since 1 Jan 2023 (Skatteetaten/Prop.1 LS).
            // ±1pp tolerance around the statutory rate.
            Assert.InRange(C.Finance.ResourceRentTaxRate, 0.24, 0.26);
            // Corporate tax 22% (Norwegian statutory rate).
            Assert.Equal(0.22, C.Finance.CorporateTaxRate, 3);
            // Basic deduction for resource rent = 70M NOK.
            Assert.InRange(C.Finance.ResourceRentBasicDeductionNOK, 60_000_000m, 80_000_000m);
            Assert.InRange(C.Finance.LoanInterestRate, 0.03, 0.08);
        }

        /// <summary>
        /// S1-03 (D16): the bankruptcy floors and the warning ladder. Floors are
        /// negative; a scenario floor lies below the default (it is the
        /// scenario's working-capital facility); the ladder has three rungs,
        /// ascending in [0, 1), the first at zero cash (overdraft).
        /// </summary>
        [Fact]
        public void Finance_BankruptcyFloorsAndWarningLadder_AreWellFormed()
        {
            Assert.InRange(C.Finance.BankruptcyFloorNOK, -5_000_000m, -100_000m);
            Assert.NotEmpty(C.Finance.BankruptcyFloorByScenarioNOK);
            var roster = new LakskongenSim.Core.ScenarioSystem(new LakskongenSim.Core.GameState());
            foreach (var entry in C.Finance.BankruptcyFloorByScenarioNOK)
            {
                Assert.NotNull(roster.GetScenario(entry.Key));
                Assert.True(entry.Value < C.Finance.BankruptcyFloorNOK, $"{entry.Key}: {entry.Value:N0}");
                Assert.Equal(0m, entry.Value % 500_000m);
            }
            var ladder = C.Finance.SolvencyWarningFractions;
            Assert.Equal(3, ladder.Count);
            Assert.Equal(0.0, ladder[0]);
            for (int i = 1; i < ladder.Count; i++)
                Assert.True(ladder[i] > ladder[i - 1] && ladder[i] < 1.0, $"rung {i + 1}: {ladder[i]}");
        }

        [Fact]
        public void Licensing_MatchesRealReferences()
        {
            // MAB per standard licence: 780 t = 780,000 kg (Fiskeridir). Tight ±1 t.
            Assert.InRange(C.Licensing.MABLimitKgPerLicense, 779_000, 781_000);
            // Licence market value ~150-200M NOK (Fiskeridir).
            Assert.InRange(C.Licensing.LicenseCostNOK, 100_000_000m, 250_000_000m);
        }

        [Fact]
        public void Biology_MatchesRealReferences()
        {
            // Harvest live weight 4.5-5.5 kg (Mowi Handbook / SalMar).
            Assert.InRange(C.Biology.TargetHarvestWeightKg, 4.5, 5.5);
            Assert.True(C.Biology.MinHarvestWeightKg < C.Biology.TargetHarvestWeightKg);
            Assert.True(C.Biology.TargetHarvestWeightKg < C.Biology.MaxHarvestWeightKg);
            // FCR band: biological 1.1-1.3, base 1.14-1.15 (SalMar sustainability).
            Assert.InRange(C.Biology.OptimalFCR, 1.1, 1.3);
            Assert.InRange(C.Biology.OptimalFCR, 1.14, 1.15);
            Assert.InRange(C.Biology.PoorFCR, 1.1, 1.6);
            Assert.InRange(C.Biology.SmoltMaturationDays, 305, 425);
        }

        [Fact]
        public void Environment_MatchesRealReferences()
        {
            // Salmon optimal growth 8-14°C; lethal ceiling 22°C (Norwegian fjord data).
            Assert.InRange(C.Environment.OptimalTemperatureMinC, 6, 10);
            Assert.InRange(C.Environment.OptimalTemperatureMaxC, 12, 16);
            Assert.InRange(C.Environment.LethalTemperatureMaxC, 20, 24);
            Assert.InRange(C.Environment.FallowPeriodDays, 30, 90);

            // PROVISIONAL storm-episode loss fraction: reconciled 2026-10-08 with
            // the sea-mortality reference band (14-16%/cycle; the old 0.02 in-tick
            // factor drove ~67%). Keep it in a corridor until the episodic storm
            // model replaces it — the EFFECTIVE regression guard is
            // BalanceReportTests.DiseaseStress_TotalMortality_WithinReferenceBand.
            Assert.InRange(C.Environment.StormFishLossBaseFraction, 1e-4, 1e-3);
        }

        [Fact]
        public void WaterQuality_MatchesRealReferences()
        {
            // DO stress threshold 0.6 saturation; stocking density limit ~25 kg/m³.
            Assert.InRange(C.WaterQuality.CriticalDOSaturationThreshold, 0.4, 0.8);
            Assert.InRange(C.WaterQuality.StockingDensityThresholdKgM3, 20.0, 30.0);
            Assert.True(C.WaterQuality.DOSaturationMinMultiplier <= C.WaterQuality.DOSaturationMaxMultiplier);
        }

        [Fact]
        public void Compliance_MatchesRealReferences()
        {
            // Escape fine 500k NOK per 100 fish; ASC premium ~8-15%, organic ~15-25%.
            Assert.InRange(C.Compliance.EscapeFinePer100FishNOK, 250_000m, 1_000_000m);
            Assert.InRange(C.Compliance.ASCPricePremiumPercent, 5, 20);
            Assert.InRange(C.Compliance.OrganicPricePremiumPercent, 10, 30);
        }

        [Fact]
        public void Research_MatchesRealReferences()
        {
            // R&D node envelope: base ~450k, max ~4.5M NOK (game default ±50%).
            Assert.InRange(C.Research.ResearchBaseCostNOK, 225_000m, 900_000m);
            Assert.InRange(C.Research.ResearchMaxCostNOK, 2_250_000m, 9_000_000m);
            Assert.True(C.Research.ResearchMaxCostNOK > C.Research.ResearchBaseCostNOK);
        }

        [Fact]
        public void Market_MatchesRealReferences()
        {
            // O-U long-term mean ~82 NOK/kg: 2020-2025 Fish Pool average 82-85;
            // ±8 tolerance. Price floor/ceiling bracket the mean.
            Assert.InRange(C.Market.LongTermMeanNOKPerKg, 74m, 90m);
            Assert.InRange(C.Market.MeanReversionSpeedWeekly, 0.005m, 0.020m);
            Assert.InRange(C.Market.VolatilityWeekly, 3.0m, 14.0m);
            Assert.True(C.Market.PriceFloorNOKPerKg < C.Market.LongTermMeanNOKPerKg);
            Assert.True(C.Market.LongTermMeanNOKPerKg < C.Market.PriceCeilingNOKPerKg);
            Assert.Equal(12, C.Market.MonthlySeasonalOffsets.Length);
        }

        [Fact]
        public void Feed_MatchesRealReferences()
        {
            // Base FCR 1.14-1.15 biological (SalMar), within the 1.1-1.3 industry band.
            Assert.InRange(C.Feed.BaseFCR, 1.14, 1.15);
            Assert.InRange(C.Feed.BaseFCR, 1.1, 1.3);
            // Feed price per kg feed stays in a plausible 15-30 NOK/kg band.
            Assert.InRange(C.Feed.FeedPricePerKgFeedNOK, 15m, 30m);
            // Feed FCR anchor and the biology best-practice FCR must not drift apart.
            Assert.Equal(C.Biology.OptimalFCR, C.Feed.BaseFCR, 6);
        }

        [Fact]
        public void Feed_CostModel_PriceTimesFcrMatchesRealFeedCostPerKgProduced()
        {
            // THE flagged discrepancy. Real feed cost ~25.7 NOK/kg produced (2024)
            // (Fiskeridir Tab.38 / Mowi annual). Model = price × FCR.
            // Tolerance ±4 NOK/kg (~15%) around the real reference.
            var modelled = C.FeedCostPerKgProducedNOK;
            Assert.InRange(modelled, 25.7m - 4m, 25.7m + 4m);
            // Guard specifically against regression to the old flat 12.5 NOK/kg model.
            Assert.True(modelled > 20m, $"feed cost/kg produced {modelled} regressed to the old flat model");
        }

        [Fact]
        public void FeedSystem_Defaults_MatchRegistry_NoDrift()
        {
            // Regression guard for the old FeedSystem defaults (13.5 NOK/kg feed,
            // 1.12 FCR) which produced a standalone effective cost of only ~15.1
            // NOK/kg produced and disagreed with the registry. A bare FeedSystem
            // must source its cost/FCR anchors from the calibration spine, so any
            // code path that bypasses SimulationEngine.ApplyBalancingConfig still
            // lands on the reconciled ~25.7 NOK/kg produced.
            var feed = new FeedSystem();
            Assert.Equal(C.Feed.FeedPricePerKgFeedNOK, feed.FeedCostPerKg);
            Assert.Equal(C.Feed.BaseFCR, feed.BaseFCR, 6);
            Assert.Equal(C.Feed.BaseFCR, feed.CurrentFCR, 6);
            // Effective cost-per-kg-produced must equal the registry anchor.
            Assert.Equal(C.FeedCostPerKgProducedNOK, feed.FeedCostPerKgProducedNOK, 3);
            Assert.InRange(feed.FeedCostPerKgProducedNOK, 25.7m - 4m, 25.7m + 4m);
            Assert.True(feed.FeedCostPerKgProducedNOK > 20m,
                $"standalone FeedSystem cost/kg produced {feed.FeedCostPerKgProducedNOK} regressed to the flat model");
        }

        [Fact]
        public void SeaTemperature_MatchesItsSources()
        {
            // S2-02. Slow anomaly SD ~1 °C: PO3's winter minimum and summer maximum
            // at 3 m vary with SD 1.2 °C (Sandvik et al. 2021, 2012-2020 farm reports).
            Assert.InRange(C.SeaTemperature.AnomalyStdDevC, 0.6, 1.2);
            // Monthly anomalies persist for months (HI note 2017: lag-1 month ~0.7).
            Assert.InRange(C.SeaTemperature.AnomalyEFoldingDays, 45.0, 180.0);
            // Day-to-day fluctuation well below the slow anomaly.
            Assert.InRange(C.SeaTemperature.DailyFluctuationStdDevC, 0.0, 0.6);
            // A marine heatwave lasts weeks (HI 2025-69: most of July 2025).
            Assert.InRange(C.SeaTemperature.EventAnomalyEFoldingDays, 7.0, 45.0);
            // The file and the code defaults agree (the embedded copy is the file).
            var defaults = new SeaTemperatureCalibration();
            Assert.Equal(defaults, C.SeaTemperature);
        }

        [Fact]
        public void Growth_MatchesItsSources()
        {
            // S2-03. Sea-phase TGC 2.8-3.2 (roadmap; Reid et al. 2013a average 3.0,
            // Karlsen et al. 2018 open net pen 2.80, semi-closed 3.04).
            Assert.InRange(C.Growth.ThermalGrowthCoefficient, 2.8, 3.2);
            // Growth optimum 13-14 °C (Kleiven et al. 2026), growth stops near the
            // incremental thermal maximum ~22 °C (Calado et al. 2021).
            Assert.InRange(C.Growth.OptimalTemperatureC, 12.5, 14.5);
            Assert.InRange(C.Growth.UpperGrowthLimitC, 20.0, 23.0);
            // Degree-days below the optimum: at 4 °C at least 0.3 of the optimum.
            Assert.True(4.0 / C.Growth.OptimalTemperatureC >= 0.3);
            // The TGC holds to ~3 kg (Alanärä et al. 2001); grown-on fish pass 6 kg.
            Assert.InRange(C.Growth.TgcValidToKg, 2.5, 3.5);
            Assert.True(C.Growth.AsymptoticWeightKg > 10.0);
            // Smolt weight CV 17-21% one month after transfer (Karlsen et al. 2018).
            Assert.InRange(C.Growth.SmoltWeightCv, 0.15, 0.22);
            Assert.InRange(C.Growth.BaseWeightCv, 0.15, 0.30);
            Assert.InRange(C.Growth.MaintenanceRationFraction, 0.0, 0.3);
            Assert.InRange(C.Growth.PhotoperiodGrowthSlope, 0.0, 0.3);
            // The file and the code defaults agree (the embedded copy is the file).
            Assert.Equal(new GrowthCalibration(), C.Growth);
        }

        [Fact]
        public void Harvest_MatchesItsSources()
        {
            var h = C.Harvest;
            // S2-04. Live to gutted (HOG) 84% (Mowi Salmon Farming Industry Handbook 2025, p. 114; 2018 edition 84%).
            Assert.Equal(0.84, h.HogYieldFraction);
            // Harvest and transport 5.0 NOK/kg HOG: Fiskeridirektoratet 2023 "slakting og
            // transport" 4.45 NOK/kg round weight x 1.125 gutting loss (Fiskehelserapporten
            // 2024, table 3.2.1); Mowi Handbook 2025 EUR 0.40/kg GWT (~4.7 NOK).
            Assert.InRange((double)(h.WellboatCostPerKgHogNok + h.SlaughterCostPerKgHogNok), 4.45 * 1.125 * 0.99, 4.45 * 1.125 * 1.01);
            // Wellboat ~31% of it (Marine Harvest Norway 2017: well boat 0.15 of 0.48 EUR/kg GWE).
            Assert.InRange((double)(h.WellboatCostPerKgHogNok / (h.WellboatCostPerKgHogNok + h.SlaughterCostPerKgHogNok)), 0.29, 0.33);
            // Size ladder on gutted weight, 4-5 kg the reference; Kontali 2015-2024 means:
            // 2-3 kg ~86%, 6-7 kg ~108% of 4-5 kg (Mowi Handbook 2025, §6.7).
            Assert.Equal(1.00, h.PriceFactor4To5Kg);
            Assert.InRange(h.PriceFactor2To3Kg, 0.84, 0.88);
            Assert.InRange(h.PriceFactor6To7Kg, 1.06, 1.10);
            Assert.True(h.PriceFactorUnder1Kg < h.PriceFactor1To2Kg && h.PriceFactor1To2Kg < h.PriceFactor2To3Kg
                && h.PriceFactor2To3Kg < h.PriceFactor3To4Kg && h.PriceFactor3To4Kg < h.PriceFactor4To5Kg
                && h.PriceFactor4To5Kg < h.PriceFactor5To6Kg && h.PriceFactor5To6Kg < h.PriceFactor6To7Kg
                && h.PriceFactor6To7Kg <= h.PriceFactorOver7Kg);
            // Downgraded fish ~30% below superior (Mowi Handbook 2025, p. 116).
            Assert.Equal(0.70, h.ProductionPriceFactor);
            Assert.True(h.OrdinaryPriceFactor > h.ProductionPriceFactor && h.OrdinaryPriceFactor < 1.0);
            // Norway 2024: ordinary 1.5% of slaughtered salmon (Fiskehelserapporten 2024, table 5.8.1).
            Assert.Equal(20_976.0 / 1_408_650.0, h.OrdinaryShare, 3);
            // Kvalitetsforskriften § 16: fasting until the gut is empty; at least a few days.
            Assert.True(h.FastingDegreeDays > 0 && h.MinFastingDays >= 1);
            Assert.InRange(h.SlaughterCapacityTonnesHogPerDay, 50.0, 500.0);
            // The file and the code defaults agree (the embedded copy is the file).
            Assert.Equal(new HarvestCalibration(), h);
        }

        [Fact]
        public void LowOxygen_MatchesItsSources()
        {
            // Wave 2 sim-a-1. Acute limit: US EPA 1986 (salmonid other life stages,
            // 3 mg/L); Atlantic salmon Pcrit 3.39-3.46 mg/L at 14-18 °C (Barnes et al. 2011).
            var m = C.Mortality;
            Assert.InRange(m.AcuteLowOxygenMgL, 2.5, 3.5);
            // The ladder steps fall with the oxygen and the kill rises: no step kills
            // more in better water.
            Assert.True(m.AcuteLowOxygenMgL > m.LowOxygenSevereMgL && m.LowOxygenSevereMgL > m.LowOxygenAnoxicMgL && m.LowOxygenAnoxicMgL > 0.0);
            Assert.True(0.0 < m.LowOxygenDeathFraction && m.LowOxygenDeathFraction < m.LowOxygenDeathFractionSevere
                && m.LowOxygenDeathFractionSevere < m.LowOxygenDeathFractionAnoxic && m.LowOxygenDeathFractionAnoxic <= 1.0);

            var w = C.WaterOxygen;
            // The worst daily-mean sag lies inside the in-cage range Solstorm et al.
            // (2018) measured (26-90 % saturation): a depth above 0 and at most 1 - 26/100.
            Assert.InRange(w.HypoxiaSagMaxDepth, 0.1, 0.74);
            // A seasonal coastal bloom: 1-10 µg/L chlorophyll a (NIVA FerryBox 2017/2018).
            Assert.InRange(w.BloomChlorophyllAtCapacityUgL, 1.0, 20.0);
            // C:Chl inside the range Cloern et al. (1995) modelled.
            Assert.InRange(w.PhytoplanktonCarbonPerChlorophyll, 10.0, 330.0);
            // Redfield: 138 mol O2 per 106 mol C = 3.47 g O2 per g C.
            Assert.Equal(138.0 / 106.0 * 32.0 / 12.0, w.OxygenPerOrganicCarbon, 2);
            // A whole bloom's carbon holds less oxygen demand than a saturated
            // summer sea holds oxygen: a crash cannot empty the 3 m water.
            Assert.True(w.BloomCrashOxygenDemandAtCapacityMgL < WaterQuality.CalculateOxygenSaturation(18.0) - m.AcuteLowOxygenMgL);
            // The file and the code defaults agree.
            Assert.Equal(new MortalityCalibration(), C.Mortality);
            Assert.Equal(new WaterOxygenCalibration(), C.WaterOxygen);
        }

        [Fact]
        public void Capacity_MatchesItsSources()
        {
            // S2-01a. NFD letter 20.09.2017 (ref 15/4396-64): a locality cleared
            // for 3,120 t is four standard licences' worth.
            var c = C.Capacity;
            Assert.Equal(4 * 780.0, c.BuiltSiteLocalityMtbTonnes);
            // Commercial sea cages TGC 2.8 ± 0.3 (Nofima/SalMar 2012); CtrlAQUA's
            // 2.9-3.3 was "good growth compared to commercial farming".
            Assert.InRange(c.PlanningThermalGrowthCoefficient, 2.5, 3.1);
            // FOR-2013-12-20-1675: § 8 the fee's ceiling is 15 G; § 6 converts the
            // sales price from live to gutted weight with 0.83 for salmon.
            Assert.Equal(15.0, c.InfringementFeeCeilingG);
            Assert.Equal(0.83, c.LiveToGuttedFactor);
            // NAV: G = 136,549 NOK from 1 May 2026, so 15 G = 2,048,235 NOK.
            Assert.Equal(136_549m, c.GrunnbelopNok);
            Assert.Equal(2_048_235m, c.InfringementFeeCeilingNok);
            // Fiskeridirektoratet, lønnsomhetsundersøkelse 2024: economic feed
            // factor 1.30 (table C.5.1); insurance 0.26 + slaughter incl. freight
            // 4.67 NOK/kg (table A.3.1).
            Assert.Equal(1.30, c.EconomicFeedFactor);
            Assert.Equal(0.26m + 4.67m, c.OtherVariableCostPerKgNok);
            // At the base salmon price an illegal kilo carries a gain to confiscate.
            Assert.True(C.Economy.BaseSalmonPricePerKgNOK * (decimal)c.LiveToGuttedFactor
                - C.Feed.FeedPricePerKgFeedNOK * (decimal)c.EconomicFeedFactor - c.OtherVariableCostPerKgNok > 0m);
            // The game's own rules stay inside sane bounds.
            Assert.InRange(c.OrderDeadlineDays, 7, 60);
            Assert.InRange(c.ForcedHarvestPriceFactor, 0.5, 1.0);
            Assert.InRange(c.ForcedHarvestHeadroomFraction, 0.0, 0.15);
            Assert.InRange(c.PlanningHorizonDays, 730, 1460);
            // The file and the code defaults agree.
            Assert.Equal(new CapacityCalibration(), c);
        }

        [Fact]
        public void StockingDensity_MatchesItsSources()
        {
            var c = C.Capacity;
            // Akvakulturdriftsforskriften § 25: at most 25 kg/m³ per production unit.
            Assert.Equal(25.0, c.MaxStockingDensityKgPerM3);
            // The reference weight sits in the 4-5 kg reference class of the spot (S2-04).
            Assert.InRange(c.DensityReferenceWeightKg, 4.0, 5.5);
            // The 10,000 m³ density proxy pen then holds S2-03's commercial pen of
            // 50,000 fish, under the 200,000 fish of a real unit (§ 47a).
            var engine = new LakskongenSim.Core.SimulationEngine();
            engine.Initialize(enableEvents: false, seed: 1);
            Assert.Equal(50_000.0, engine.FishPerDensityProxyPen, 6);
            Assert.True(engine.FishPerDensityProxyPen < 200_000.0);
        }
    }
}