using System;
using LakskongenSim.Calibration;
using LakskongenSim.Farm.Land;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// Roadmap F2-12 (docs/FARM_TYPES_SPEC.md §3.3 "Energy, built from its
    /// parts", §7.1 acceptance 1 and 2): each energy part against its physics,
    /// and the energy and oxygen anchors measured over a year on the
    /// standalone harness (an HFS lot on pad_a, Salmon Evolution's PO5 water).
    /// </summary>
    public class LandEnergyModelTests
    {
        private static readonly LandCalibration C = LandCalibration.Default;
        private readonly ITestOutputHelper _output;

        public LandEnergyModelTests(ITestOutputHelper output) => _output = output;

        // ── Parts ───────────────────────────────────────────────────────────

        [Fact]
        public void Pumping_IsRhoGVHOverEta()
        {
            // 1,000 m³ up 10 m: 1,027 × 9.80665 × 1,000 × 10 / 0.75 J = 37.30 kWh.
            Assert.Equal(1_027.0 * 9.80665 * 1_000.0 * 10.0 / 0.75 / 3.6e6,
                LandEnergyModel.PumpingKWh(1_000.0, 10.0, C.Energy), 9);
            Assert.Equal(37.30, LandEnergyModel.PumpingKWh(1_000.0, 10.0, C.Energy), 2);
            Assert.Equal(0.0, LandEnergyModel.PumpingKWh(0.0, 10.0, C.Energy));
        }

        [Fact]
        public void LineFriction_ScalesWithTheSquareOfTheFlow()
        {
            double capacity = 5.0 * LandWaterModel.SecondsPerDay;
            Assert.Equal(C.Energy.FrictionMPerKmAtCapacity * 2.0, LandEnergyModel.LineFrictionM(capacity, 2.0, 5.0, C.Energy), 9);
            Assert.Equal(C.Energy.FrictionMPerKmAtCapacity * 2.0 / 4.0,
                LandEnergyModel.LineFrictionM(capacity / 2.0, 2.0, 5.0, C.Energy), 9);
        }

        [Fact]
        public void IntakePumping_LiftsEachLinesShareOverTheLiftAndItsFriction()
        {
            var site = new LandIntakeSite(13.0, 0.5, 2.0, 5.0);
            double q = 400_000.0;
            double expected = LandEnergyModel.PumpingKWh(0.25 * q, 13.0 + LandEnergyModel.LineFrictionM(0.25 * q, 0.5, 5.0, C.Energy), C.Energy)
                + LandEnergyModel.PumpingKWh(0.75 * q, 13.0 + LandEnergyModel.LineFrictionM(0.75 * q, 2.0, 5.0, C.Energy), C.Energy);
            Assert.Equal(expected, LandEnergyModel.IntakePumpingKWh(q, 0.75, site, C.Energy), 9);
            // The long deep line costs more than the short shallow one.
            Assert.True(LandEnergyModel.IntakePumpingKWh(q, 1.0, site, C.Energy) > LandEnergyModel.IntakePumpingKWh(q, 0.0, site, C.Energy));
        }

        [Fact]
        public void HeatBalance_WithinCapacity_HoldsTheSetpoint_AtTheCop()
        {
            const double q = 200_000.0;
            var heat = LandEnergyModel.HeatBalance(q, 10.0, 12.0, 0.0, 8.0, heatRecovery: false, C.Energy);
            double needed = q * C.Energy.HeatKWhPerM3PerK * 2.0;
            Assert.False(heat.CapacityBound);
            Assert.Equal(12.0, heat.TankC, 9);
            Assert.Equal(needed / C.Energy.Cop, heat.HeatingKWh, 6);
            var recovered = LandEnergyModel.HeatBalance(q, 10.0, 12.0, 0.0, 8.0, heatRecovery: true, C.Energy);
            Assert.Equal(needed / C.Energy.CopWithHeatRecovery, recovered.HeatingKWh, 6);
            Assert.Equal(12.0, recovered.TankC, 9);
        }

        [Fact]
        public void HeatBalance_AtCapacity_MissesTheSetpoint()
        {
            const double q = 400_000.0;
            var heat = LandEnergyModel.HeatBalance(q, 6.0, 12.0, 0.0, 1.0, heatRecovery: false, C.Energy);
            Assert.True(heat.CapacityBound);
            Assert.Equal(1.0 * 1000.0 * 24.0, heat.HeatingKWh, 6);
            double expectedTank = 6.0 + 24_000.0 * C.Energy.Cop / (q * C.Energy.HeatKWhPerM3PerK);
            Assert.Equal(expectedTank, heat.TankC, 9);
            Assert.True(heat.TankC < 12.0);
        }

        [Fact]
        public void HeatBalance_InternalGains_WarmTheWater_AndAreCooledAboveTheBand()
        {
            const double q = 30_000.0;
            double perK = q * C.Energy.HeatKWhPerM3PerK;
            // 20 MWh of pump and fish heat lift the water 0.58 K: the chillers
            // take it back to the top of the 0.5 K band.
            double gain = 20_000.0;
            var heat = LandEnergyModel.HeatBalance(q, 12.0, 12.0, gain, 2.0, heatRecovery: true, C.Energy);
            Assert.Equal(12.0 + C.Energy.CoolingDeadbandK, heat.TankC, 9);
            Assert.Equal((gain - C.Energy.CoolingDeadbandK * perK) / C.Energy.CoolingCop, heat.CoolingKWh, 6);
            Assert.Equal(0.0, heat.HeatingKWh);
            // Inside the band the water floats and no chiller runs.
            var floating = LandEnergyModel.HeatBalance(q, 12.0, 12.0, 10_000.0, 2.0, heatRecovery: true, C.Energy);
            Assert.Equal(0.0, floating.CoolingKWh);
            Assert.Equal(12.0 + 10_000.0 / perK, floating.TankC, 9);
            // No chiller at all: the gains lift the tank.
            var free = LandEnergyModel.HeatBalance(q, 12.0, 12.0, gain, 0.0, heatRecovery: true, C.Energy);
            Assert.Equal(12.0 + gain / perK, free.TankC, 9);
            Assert.True(free.CapacityBound);
        }

        [Fact]
        public void OxygenProduction_OnSiteIsOnTheMeter_PurchasedIsUpstream()
        {
            var (onSite, upstream) = LandEnergyModel.OxygenProductionKWh(1_000.0, true, C.Oxygen);
            Assert.Equal(1_000.0 * C.Oxygen.OnSiteGeneratorKWhPerKgO2, onSite, 9);
            Assert.Equal(0.0, upstream);
            (onSite, upstream) = LandEnergyModel.OxygenProductionKWh(1_000.0, false, C.Oxygen);
            Assert.Equal(0.0, onSite);
            Assert.Equal(1_000.0 * C.Oxygen.PurchasedLoxUpstreamKWhPerKg, upstream, 9);
            var day = new LandEnergyDay(1, 2, 3, 4, 5, 6, 7, 8, 100);
            Assert.Equal(36.0, day.TotalKWh);
            Assert.Equal(72.0, (day + day).TotalKWh);
        }

        // ── F2-12 acceptance 1 and 2 over an annual cycle ───────────────────

        private LandProjectionResult Year(LandProjectionSettings settings, string label)
        {
            var r = LandLotProjector.Run(settings, C);
            var e = r.Energy;
            double g = r.NetGrowthKg;
            _output.WriteLine($"{label}: {r.KWhPerKgGrowth:F2} kWh/kg growth, LOX {r.LoxPerKgGrowth:F3} kg/kg, growth {g / 1000:F0} t, " +
                $"eFCR {r.EconomicFcr:F3}, CO2 {r.MeanCo2MgL:F1} (max {r.MaxCo2MgL:F1}) mg/L, tank {r.MeanTankC:F2} °C");
            _output.WriteLine($"  per kg: intake {e.IntakePumpingKWh / g:F2}, loop {e.LoopPumpingKWh / g:F2}, treatment {e.TreatmentKWh / g:F2}, " +
                $"UV {e.UvKWh / g:F2}, heating {e.HeatingKWh / g:F2}, cooling {e.CoolingKWh / g:F2}, base {e.BaseKWh / g:F2}");
            return r;
        }

        [Fact]
        public void F2_12_Acceptance1_HfsR065WithHeatRecovery_Uses6To75KWhPerKgLw()
        {
            // [L:R2]: Salmon Evolution phase 1 at full run rate, ~6.3-6.8 kWh/kg biomass (LW).
            var r = Year(LandTestSites.SalmonEvolutionYear(C), "HFS r 0.65, heat recovery, PO5");
            Assert.InRange(r.KWhPerKgGrowth, 6.0, 7.5);
            Assert.Equal(0, r.HeatBoundDays);
            Assert.InRange(r.MeanTankC, 11.95, 12.05);
        }

        [Fact]
        public void F2_12_Acceptance1_FlowThroughAtTheSetpoint_LiftUnder2M_Uses06To15KWhPerKgLw()
        {
            // [L:A1, L:A3]: Andfjord's flow-through pools, ~1 kWh/kg.
            var settings = LandTestSites.SalmonEvolutionYear(C) with
            {
                Strategy = new LandWaterStrategy(0.0, 0.0, 12.0),
                Site = LandTestSites.FlowThroughPlot(C),
                HeatRecovery = false,
                ShallowWaterC = _ => 12.0,
            };
            Assert.True(settings.Site.StaticLiftM <= 2.0);
            var r = Year(settings, "flow-through r 0, intake at the setpoint, 2 m lift");
            Assert.Equal(0, r.PumpLimitedDays);
            Assert.InRange(r.KWhPerKgGrowth, 0.6, 1.5);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void F2_12_Acceptance1_RasHallAtR097_Uses4To10KWhPerKgLw(bool heatRecovery)
        {
            // [L:R7, L:S3, L:S4]: RAS grow-out 4-10 kWh/kg.
            var settings = LandTestSites.SalmonEvolutionYear(C) with
            {
                Template = LandTemplate.RasHall,
                Strategy = LandSiteTemplates.DefaultStrategy(C.Ras),
                HeatRecovery = heatRecovery,
            };
            Assert.Equal(0.97, settings.Strategy.Reuse);
            var r = Year(settings, $"RAS r 0.97, heat recovery {heatRecovery}");
            Assert.InRange(r.KWhPerKgGrowth, 4.0, 10.0);
        }

        [Fact]
        public void F2_12_Acceptance2_LoxAtTheSalmonEvolutionSetup_Is045To065KgPerKgLw()
        {
            // [L:R2]: oxygen consumption 0.55 kg per kg biomass growth (LW).
            var r = Year(LandTestSites.SalmonEvolutionYear(C), "HFS r 0.65 oxygen");
            Assert.InRange(r.LoxPerKgGrowth, 0.45, 0.65);
        }

        [Fact]
        public void HeatRecovery_CutsTheHfsEnergy()
        {
            var with = LandLotProjector.Run(LandTestSites.SalmonEvolutionYear(C), C);
            var without = LandLotProjector.Run(LandTestSites.SalmonEvolutionYear(C) with { HeatRecovery = false }, C);
            Assert.True(without.Energy.HeatingKWh > 2.0 * with.Energy.HeatingKWh);
            Assert.True(without.KWhPerKgGrowth > with.KWhPerKgGrowth + 1.0);
        }

        [Fact]
        public void TheSalmonEvolutionPlan_GrowsInItsCycle_AtItsFcr()
        {
            // [L:R1]: 400 g → 5.2 kg LW in 8-9 months; F2-15 #1 band 8-10
            // months at 12-13 °C; F2-15 #2 HFS FCR 1.02-1.15.
            var plan = LandStockingPlan.For(C.Hfs, C.Production);
            var forecast = LandStockingPlanner.Project(LakskongenSim.Biology.TgcGrowthLaw.Default, plan);
            Assert.InRange(forecast.CycleDays, 240, 305);
            var r = LandLotProjector.Run(LandTestSites.SalmonEvolutionYear(C), C);
            Assert.InRange(r.EconomicFcr, 1.0, 1.15);
        }

        [Fact]
        public void Projector_IsDeterministic()
        {
            var settings = LandTestSites.SalmonEvolutionYear(C) with { Days = 120, RecordDays = true };
            var a = LandLotProjector.Run(settings, C);
            var b = LandLotProjector.Run(settings, C);
            Assert.Equal(a.Energy, b.Energy);
            Assert.Equal(a.LoxKg, b.LoxKg);
            Assert.Equal(a.NetGrowthKg, b.NetGrowthKg);
            Assert.Equal(a.Daily, b.Daily);
        }
    }
}
