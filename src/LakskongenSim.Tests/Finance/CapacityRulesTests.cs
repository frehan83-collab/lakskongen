using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests;

/// <summary>
/// S2-01a, the pure parts of capacity v1: the licence register's pooling,
/// the production plan the stocking check projects, and the regulator's
/// arithmetic for a biomass breach (FOR-2013-12-20-1675 §§ 6 and 8).
/// </summary>
public class CapacityRulesTests
{
    private static readonly CalibrationRegistry C = CalibrationRegistry.Load();

    // ── The register ──────────────────────────────────────────────────────

    [Fact]
    public void Licences_PoolPerProductionArea_AndIdsFollowTheGrantSequence()
    {
        var holdings = new List<LicenceHolding>();
        var a = LicenceRegister.Grant(holdings, 1, 3, 780, "po3_nord", LicenceOrigin.Scenario, 0);
        LicenceRegister.Grant(holdings, 2, 3, 780, "po3_sor", LicenceOrigin.Scenario, 0);
        LicenceRegister.Grant(holdings, 3, 12, 945, "", LicenceOrigin.Auction, 40, 150_000_000m);

        Assert.Equal("tl_3_1", a.Id);
        Assert.Equal(1_560.0, LicenceRegister.PooledMtbTonnes(holdings, 3));
        Assert.Equal(945.0, LicenceRegister.PooledMtbTonnes(holdings, 12));
        Assert.Equal(0.0, LicenceRegister.PooledMtbTonnes(holdings, 4));
        Assert.Equal(2, LicenceRegister.CountIn(holdings, 3));
        Assert.Equal(new[] { "tl_3_1", "tl_3_2", "tl_12_3" }, holdings.Select(h => h.Id));
        Assert.Equal(150_000_000m, holdings[2].PricePaidNok);
        Assert.Equal(40, holdings[2].GrantedDay);
    }

    // ── The production plan ───────────────────────────────────────────────

    private static double TenDegrees(int area, int day) => 10.0;

    [Fact]
    public void Plan_GrowsOnTheThermalGrowthCoefficient_AndHarvestsAtThePlanWeight()
    {
        // 100 g to 5.2 kg at 10 °C and TGC 2.8: (5200^(1/3) - 100^(1/3)) / (2.8 x 10 / 1000) days.
        var settings = new CapacityPlanSettings(2.8, 5.2, 1.0, 2_000);
        var plan = CapacityPlanner.Project(new[] { new PlannedCohort("s", 3, 100_000, 0.1) }, 0, settings, TenDegrees);

        double daysToHarvest = (Math.Cbrt(5_200.0) - Math.Cbrt(100.0)) / (2.8 * 10.0 / 1000.0);
        Assert.InRange(plan.PeakDay, (int)Math.Floor(daysToHarvest), (int)Math.Ceiling(daysToHarvest));
        // The peak is the day the cohort reaches (just over) 5.2 kg: 100,000 x 5.2 kg = 520 t.
        Assert.InRange(plan.PeakTonnes, 520.0, 520.0 * 1.01);
        Assert.Equal(plan.PeakTonnes, plan.PeakTonnesBySite["s"], 9);
    }

    [Fact]
    public void Plan_ThinsTheCohortAtTheDailySurvivalOfTheCycleLoss()
    {
        double survival = CapacityPlanner.DailySurvivalFor(0.15, 540);
        Assert.Equal(0.85, Math.Pow(survival, 540), 12);
        var withLoss = CapacityPlanner.Project(new[] { new PlannedCohort("s", 3, 100_000, 0.1) }, 0,
            new CapacityPlanSettings(2.8, 5.2, survival, 2_000), TenDegrees);
        var noLoss = CapacityPlanner.Project(new[] { new PlannedCohort("s", 3, 100_000, 0.1) }, 0,
            new CapacityPlanSettings(2.8, 5.2, 1.0, 2_000), TenDegrees);
        Assert.Equal(noLoss.PeakDay, withLoss.PeakDay);
        Assert.Equal(noLoss.PeakTonnes * Math.Pow(survival, noLoss.PeakDay), withLoss.PeakTonnes, 6);
    }

    [Fact]
    public void Plan_PeaksWhenCohortsOverlap_NotAtTheSumOfTheirHarvestWeights()
    {
        // A 4 kg cohort leaves within weeks; a smolt cohort stocked beside it is
        // still small then. The plan's peak is far below both at 5.2 kg.
        var settings = new CapacityPlanSettings(2.8, 5.2, 1.0, 2_000);
        var cohorts = new[]
        {
            new PlannedCohort("old", 3, 100_000, 4.0),
            new PlannedCohort("new", 3, 100_000, 0.1),
        };
        var plan = CapacityPlanner.Project(cohorts, 0, settings, TenDegrees);
        Assert.True(plan.PeakTonnes < 1_040.0 * 0.6, $"peak {plan.PeakTonnes:F1} t");
        Assert.InRange(plan.PeakTonnesBySite["old"], 520.0, 520.0 * 1.01);
        Assert.InRange(plan.PeakTonnesBySite["new"], 520.0, 520.0 * 1.01);
        // The old cohort's peak comes first, the new one's much later.
        Assert.True(plan.PeakDayBySite["new"] > plan.PeakDayBySite["old"] + 300);
    }

    [Fact]
    public void Plan_ReadsTheAreasWater_AndAFishAtHarvestWeightCountsOnlyToday()
    {
        var settings = new CapacityPlanSettings(2.8, 5.2, 1.0, 2_000);
        // Colder water, slower growth: the same cohort peaks later.
        var warm = CapacityPlanner.Project(new[] { new PlannedCohort("s", 1, 1_000, 0.1) }, 0, settings, (_, _) => 12.0);
        var cold = CapacityPlanner.Project(new[] { new PlannedCohort("s", 13, 1_000, 0.1) }, 0, settings, (_, _) => 6.0);
        Assert.True(cold.PeakDay > warm.PeakDay * 1.9, $"{cold.PeakDay} vs {warm.PeakDay}");
        // Fish already at harvest weight stand today and leave.
        var ready = CapacityPlanner.Project(new[] { new PlannedCohort("s", 3, 1_000, 6.0) }, 0, settings, TenDegrees);
        Assert.Equal(0, ready.PeakDay);
        Assert.Equal(6.0, ready.PeakTonnes, 9);
        // No fish, no peak.
        Assert.Equal(0.0, CapacityPlanner.Project(Array.Empty<PlannedCohort>(), 0, settings, TenDegrees).PeakTonnes);
    }

    // ── The regulator's arithmetic ────────────────────────────────────────

    [Fact]
    public void Confiscation_IsTheNetGainPerIllegalKilo_AsTheRegulationComputesIt()
    {
        // FOR-2013-12-20-1675 § 6: price x 0.83 - feed price x economic FCR - other variable costs.
        decimal gain = BiomassBreachRules.NetGainPerKgNok(80m, 0.83, 22.3m, 1.30, 4.93m);
        Assert.Equal(80m * 0.83m - 22.3m * 1.30m - 4.93m, gain);
        // A kilo that loses money has no gain to confiscate.
        Assert.Equal(0m, BiomassBreachRules.NetGainPerKgNok(30m, 0.83, 22.3m, 1.30, 4.93m));
        Assert.Equal(Math.Round(2_500m * gain, 2), BiomassBreachRules.ConfiscationIncrementNok(2_500.0, gain));
        Assert.Equal(0m, BiomassBreachRules.ConfiscationIncrementNok(0.0, gain));
        Assert.Equal(0m, BiomassBreachRules.ConfiscationIncrementNok(-5.0, gain));
    }

    [Fact]
    public void Fee_IsTheExcessAtItsSalesValue_AndNeverPassesTheCeiling()
    {
        decimal ceiling = C.Capacity.InfringementFeeCeilingNok;
        decimal value = BiomassBreachRules.SalesValuePerKgNok(80m, 0.83);
        Assert.Equal(66.4m, value);
        Assert.Equal(664_000m, BiomassBreachRules.FeeIncrementNok(10_000.0, value, 0m, ceiling));
        // Near the ceiling only the room left is charged; at it, nothing more.
        Assert.Equal(ceiling - 2_000_000m, BiomassBreachRules.FeeIncrementNok(10_000.0, value, 2_000_000m, ceiling));
        Assert.Equal(0m, BiomassBreachRules.FeeIncrementNok(10_000.0, value, ceiling, ceiling));
        Assert.Equal(0m, BiomassBreachRules.FeeIncrementNok(0.0, value, 0m, ceiling));
    }
}
