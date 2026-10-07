using System.Linq;
using Xunit;
using LakskongenSim.Calibration;
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
    }
}