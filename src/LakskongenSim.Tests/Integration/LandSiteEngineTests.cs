using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-12 wiring (docs/FARM_TYPES_SPEC.md §7.1 F2-12, docs/run/evidence/f2_12_land_integration_notes.md):
/// the standalone land physics run in the engine. A land site with lots holds
/// its fish in physical tanks; the land tick solves each lot's water on the
/// day's feed and books its energy, oxygen and staff; the biology reads the
/// tank's water, appetite and density. Engine-side acceptance 5-7: stocking
/// over 85 kg/m³ refused; lots change tanks, energy and staff and the nameplate
/// is enforced; land fish have no lice, sit outside the traffic light and pay no rent.
/// </summary>
public class LandSiteEngineTests
{
    private readonly ITestOutputHelper _output;
    public LandSiteEngineTests(ITestOutputHelper output) => _output = output;

    private static SimulationEngine NewEngine(int seed, decimal cash = 3_000_000_000m)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        if (cash > 0m) engine.ReceiveLoan(cash);
        return engine;
    }

    private static RASFacility Commission(SimulationEngine engine, LandTemplate template = LandTemplate.HfsLot, int lots = 1,
        string plot = "pad_a")
    {
        var placed = engine.CommissionLandSite(plot, template, lots, "Test");
        Assert.True(placed.Ok, placed.Message);
        return engine.FindLandSite(placed.Id)!;
    }

    /// <summary>The plan's room in a tank (the most smolts its projected peak allows).</summary>
    private static int Room(SimulationEngine engine, RASFacility site, string tank)
        => engine.PreviewLandStocking(site.Id, tank, 0).MaxSmolts;

    [Fact]
    public void ACommissionedHfsLot_HasItsTanks_OnItsPad_AndRunsTheLandPhysics()
    {
        var engine = NewEngine(41);
        var site = Commission(engine);
        var cal = engine.LandCalibration;

        // One full HFS lot: 12 grow-out + 2 pre-grow-out tanks of the calibration, numbered across the site.
        var lot = Assert.Single(site.Lots);
        Assert.Equal((LandTemplate.HfsLot, $"{site.Id}/lot1"), (lot.Template, lot.Id));
        Assert.Equal(cal.Hfs.TankCount, lot.Tanks.Count);
        Assert.Equal(Enumerable.Range(1, 14).Select(n => LandTank.ComposeId(site.Id, n)), lot.Tanks.Select(t => t.Id));
        Assert.Equal(LandTank.VolumeOf(28.0, 8.0), lot.Tanks[0].VolumeM3, 9);
        Assert.Equal(("pad_a", "pad_a"), (site.PlotId, site.ZoneId));
        Assert.Equal(site.Id, engine.FindPlot("pad_a")!.OwnerUnitId);
        Assert.True(site.IsOperational);
        Assert.Equal((14, 33, cal.Hfs.NameplateTonnesHogPerYear), (site.TankCount, site.EmployeeCount, site.CapacityTonnesPerYear));

        int room = Room(engine, site, "t01");
        Assert.True(room > 10_000, $"room {room}");
        var stocked = engine.StockLandTank(site.Id, "t01", room);
        Assert.True(stocked.Ok, stocked.Message);
        var population = site.PopulationIn(lot.Tanks[0])!;
        Assert.Equal(0.4, population.Cohorts.Single().MeanWeightKg, 9);           // [L:R1]: 400 g post-smolt
        Assert.Equal(SmoltQualityTier.Standard, population.Cohorts.Single().Quality); // no sea-phase tier bonus on land
        Assert.Equal(SmoltQualityTier.SuperSmolt, engine.PreviewLandStocking(site.Id, "t02", 1000).Tier); // the price's tier
        Assert.Same(lot.Tanks[0].Pen, engine.HoldingOf(population).Pen);           // the holding carries the tank

        var bookings = new List<(string Category, decimal Amount)>();
        engine.PnlBooked += (_, category, amount, income) => { if (!income) bookings.Add((category, amount)); };
        double startKg = population.GetTotalBiomassKg();
        engine.Advance(60);

        // The fish grew in the tank's own water, the setpoint the heat pumps hold.
        var tank = lot.Tanks[0];
        var water = engine.HoldingWaterOf(population);
        Assert.Equal((tank.TemperatureC, tank.DoMgL, HoldingKind.LandTank), (water.TemperatureC, water.OxygenMgPerL, water.Kind));
        Assert.InRange(tank.TemperatureC, cal.Hfs.DefaultSetpointC - 0.01, cal.Hfs.DefaultSetpointC + cal.Energy.CoolingDeadbandK + 0.01);
        Assert.Equal(cal.Oxygen.TankOutletDoMgL, tank.DoMgL, 9);
        Assert.InRange(tank.Co2MgL, 1.0, 15.0);
        Assert.True(population.GetTotalBiomassKg() > startKg * 1.5, $"{startKg:N0} → {population.GetTotalBiomassKg():N0} kg");
        Assert.True(lot.Tanks.Skip(1).All(t => t.Pen.IsEmpty));

        // The land tick books the lot's electricity, oxygen and staff as their own opex lines.
        Assert.True(site.DailyEnergyKWh > cal.Hfs.BaseKWhPerDay);
        Assert.True(site.DailyLoxKg > 0.0);
        Assert.Equal(60, bookings.Count(b => b.Category == "land_energy"));
        Assert.Equal(60, bookings.Count(b => b.Category == "land_staff"));
        Assert.True(bookings.Count(b => b.Category == "land_oxygen") >= 59);
        decimal wages = 33 * cal.Operations.StaffCostNokPerFteYear / SimCalendar.DaysPerYear;
        Assert.All(bookings.Where(b => b.Category == "land_staff"), b => Assert.Equal(-wages, b.Amount));
        Assert.Contains(site.CarbonTracker.Emissions, e => e.Category == "Scope2_Electricity");
        Assert.Contains(site.CarbonTracker.Emissions, e => e.Category == "Scope3_Oxygen");
    }

    [Fact]
    public void TheLandTick_DrawsExactlyTenUniformsADay_FromTheSitesOwnStream()
    {
        var engine = NewEngine(42);
        var site = Commission(engine);
        engine.Advance(1);
        string key = SimRngStreams.RASFacility + "\u0001" + site.Id;
        ulong day1 = engine.Rng.CaptureStates()[key];
        engine.Advance(5);
        var probe = new SimRng(engine.State.RandomSeed);
        var stream = probe.Stream(SimRngStreams.RASFacility, site.Id);
        for (int i = 0; i < 6 * SimulationEngine.LandDrawsPerDay; i++) stream.NextDouble();
        Assert.Equal(probe.CaptureStates()[key], engine.Rng.CaptureStates()[key]);
        Assert.NotEqual(day1, engine.Rng.CaptureStates()[key]);
    }

    // ── Acceptance 5: stocking over the density limit is refused ──────────

    [Fact]
    public void Acceptance5_AStockingWhoseProjectedPeakPasses85KgPerM3_IsRefused_AndTheRoomIsAllowed()
    {
        var engine = NewEngine(43);
        var site = Commission(engine);
        var tank = site.Lots[0].Tanks[0];
        int room = Room(engine, site, tank.Id);
        var atRoom = engine.PreviewLandStocking(site.Id, tank.Id, room);
        Assert.True(atRoom.Allowed, atRoom.Message);
        Assert.True(atRoom.PeakDensityKgM3 <= 85.0, $"peak {atRoom.PeakDensityKgM3}");
        var over = engine.PreviewLandStocking(site.Id, tank.Id, room + 1);
        Assert.Equal(LandSiteReasons.DensityExceeded, over.Reason);
        Assert.True(over.PeakDensityKgM3 > 85.0);

        decimal cash = engine.FinanceManager.Balance;
        int populations = engine.State.Populations.Count;
        var refused = engine.StockLandTank(site.Id, tank.Id, room + 50_000);
        Assert.Equal(LandSiteReasons.DensityExceeded, refused.ErrorKey);
        Assert.Equal((cash, populations), (engine.FinanceManager.Balance, engine.State.Populations.Count));
        Assert.True(tank.Pen.IsEmpty);

        // The engine's check is the standalone planner's (LandStockingPlannerTests).
        var plan = LandStockingPlan.For(engine.LandCalibration.Hfs, engine.LandCalibration.Production) with
        {
            TemperatureC = site.TargetWaterTemp,
            GrowthMultiplier = engine.LandCalibration.Production.PlanGrowthMultiplier * SmoltStrainRegistry.Get("standard").BuildGenome().GetGrowthMultiplier(),
        };
        Assert.Equal(LandStockingPlanner.Check(engine.Growth.Law, plan, tank.VolumeM3, room + 1, 85.0).Reason, over.Reason);
    }

    [Fact]
    public void Stocking_RefusesEveryBadCase_WithItsKey_AndChangesNothing()
    {
        var engine = NewEngine(44);
        var site = Commission(engine);
        Assert.Equal(LandSiteReasons.NotFound, engine.StockLandTank("ras_99", "t01", 1000).ErrorKey);
        Assert.Equal(LandSiteReasons.TankNotFound, engine.StockLandTank(site.Id, "t99", 1000).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.StockLandTank(site.Id, "t01", 0).ErrorKey);
        Assert.True(engine.StockLandTank(site.Id, "t01", 1000).Ok);
        Assert.Equal(LandSiteReasons.TankOccupied, engine.StockLandTank(site.Id, "t01", 1000).ErrorKey);
        // A post-smolt module's biofilter matures for 60 days before it takes fish.
        var module = Commission(engine, LandTemplate.PostSmoltModule, 1, "pad_delta");
        var young = engine.StockLandTank(module.Id, module.Lots[0].Tanks[0].Id, 1000);
        Assert.Equal(LandSiteReasons.BiofilterImmature, young.ErrorKey);
        engine.Advance(engine.LandCalibration.PostSmolt.BiofilterMaturationDays);
        Assert.True(engine.StockLandTank(module.Id, module.Lots[0].Tanks[0].Id, 1000).Ok);
        // Broke: insufficient funds, nothing stocked.
        var poor = NewEngine(45, cash: 0m);
        var poorSite = Commission(poor);
        poor.FinanceManager.ForceSpend(poor.FinanceManager.Balance + 1m);
        Assert.Equal(LandSiteReasons.InsufficientFunds, poor.StockLandTank(poorSite.Id, "t01", 1000).ErrorKey);
        Assert.True(poorSite.Lots[0].Tanks[0].Pen.IsEmpty);
    }

    // ── Acceptance 6: lots change tanks, energy and staff; nameplate enforced ─

    [Fact]
    public void Acceptance6_LotsChangeTanksEnergyAndStaff()
    {
        var one = NewEngine(46);
        var two = NewEngine(46);
        var a = Commission(one, LandTemplate.HfsLot, 1);
        var b = Commission(two, LandTemplate.HfsLot, 2);
        Assert.Equal((14, 28), (a.TankCount, b.TankCount));
        Assert.Equal((33, 66), (a.EmployeeCount, b.EmployeeCount));
        Assert.Equal(2.0 * a.CapacityTonnesPerYear, b.CapacityTonnesPerYear, 9);
        // The same stocking in each lot's first tank: two lots draw two lots' energy.
        foreach (var (engine, site) in new[] { (one, a), (two, b) })
            foreach (var lot in site.Lots)
                Assert.True(engine.StockLandTank(site.Id, lot.Tanks[0].Id, 50_000).Ok);
        one.Advance(30);
        two.Advance(30);
        Assert.True(b.DailyEnergyKWh > 1.9 * a.DailyEnergyKWh && b.DailyEnergyKWh < 2.1 * a.DailyEnergyKWh,
            $"{a.DailyEnergyKWh:N0} vs {b.DailyEnergyKWh:N0} kWh/day");
    }

    [Fact]
    public void Acceptance6_TheNameplate_CapsTheRollingYearsPlannedProduction()
    {
        var engine = NewEngine(47);
        var site = Commission(engine, LandTemplate.HfsPilotLot);
        // F2-15: at competence 1.0 the utilisation cap is the nameplate (at 0.4 it refuses at 55 %, LandCycleTests).
        site.Competence.CompletedCycles = 3;
        var cal = engine.LandCalibration;
        double hogPerSmolt = cal.HfsPilot.PlanHarvestWeightKg * cal.Production.CycleSurvival * cal.Production.HogPerLw;
        int yearRoom = (int)Math.Floor(cal.HfsPilot.NameplateTonnesHogPerYear * 1000.0 / hogPerSmolt);
        Assert.Equal(yearRoom, engine.PreviewLandStocking(site.Id, "t01", 0).AnnualRoomSmolts);
        // Stock every tank to its room, harvest and restock until the year's nameplate refuses.
        int stocked = 0;
        string? refusal = null;
        for (int round = 0; round < 10 && refusal == null; round++)
        {
            foreach (var tank in site.Lots[0].Tanks)
            {
                int room = engine.PreviewLandStocking(site.Id, tank.Id, 0).DensityRoomSmolts;
                if (room <= 0) continue;
                var result = engine.StockLandTank(site.Id, tank.Id, room);
                if (!result.Ok) { refusal = result.ErrorKey; break; }
                stocked += room;
            }
            foreach (var tank in site.Lots[0].Tanks.Where(t => !t.Pen.IsEmpty).ToList())
                Assert.True(engine.HarvestLandTank(site.Id, tank.Id).Ok);
        }
        Assert.Equal(LandSiteReasons.AnnualCapacity, refusal);
        Assert.True(stocked <= yearRoom, $"{stocked} > {yearRoom}");
        Assert.True(site.StockingLog.Sum(r => r.PlannedHogKg) <= cal.HfsPilot.NameplateTonnesHogPerYear * 1000.0 + 1e-6);
        // A year on, the rolling year has room again.
        engine.Advance(SimCalendar.DaysPerYear);
        Assert.Equal(yearRoom, engine.PreviewLandStocking(site.Id, "t01", 0).AnnualRoomSmolts);
    }

    // ── Acceptance 7: no lice, outside the traffic light, no rent ─────────

    [Fact]
    public void Acceptance7_LandFish_HaveNoLice_SitOutsideTheTrafficLight_AndPayNoRent()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 48);
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        engine.ReceiveLoan(3_000_000_000m);
        var site = Commission(engine);
        Assert.True(engine.StockLandTank(site.Id, "t01", 60_000).Ok);
        var population = site.PopulationIn(site.Lots[0].Tanks[0])!;
        engine.Advance(200);

        Assert.Equal(0.0, population.SeaLiceLoad);
        Assert.DoesNotContain(engine.State.Zones, z => z.Id == population.ZoneId);
        foreach (var zone in engine.State.Zones)
        {
            long seaFish = engine.State.SeaSites.Where(s => s.ZoneId == zone.Id).SelectMany(s => s.Pens).Sum(p => (long)p.TotalCount);
            Assert.Equal(seaFish, engine.ZoneLiceForTrafficLight(zone.Id).Fish);
        }

        decimal rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        decimal ringFenced = engine.State.RentRingFencedCostsThisTaxYear;
        var harvest = engine.HarvestLandTank(site.Id, "t01");
        Assert.True(harvest.Ok, harvest.Message);
        Assert.Equal(rentBase, engine.State.ResourceRentHarvestValueThisTaxYear);
        Assert.False(engine.IsResourceRentLiable(site.Id, HoldingKind.LandTank));
        Assert.True(ringFenced > 0m, "the land site's opex stays out of the rent base");
        Assert.True(site.Lots[0].Tanks[0].Pen.IsEmpty);
    }

    // ── The biology reads the tank ─────────────────────────────────────────

    [Fact]
    public void TheTanksCo2_AndItsDensity_BiteOnTheFish_ThroughTheHooks()
    {
        var engine = NewEngine(49);
        var site = Commission(engine);
        Assert.True(engine.StockLandTank(site.Id, "t01", 60_000).Ok);
        var population = site.PopulationIn(site.Lots[0].Tanks[0])!;
        var tank = site.Lots[0].Tanks[0];
        engine.Advance(2);
        var holding = engine.HoldingOf(population);
        // Clean water: full appetite.
        Assert.Equal(tank.AppetiteFactor, engine.AppetiteFactorFor(population), 12);
        tank.AppetiteFactor = 0.7;
        Assert.Equal(0.7, engine.AppetiteFactorFor(population), 12);
        tank.DoMgL = 3.0; // a deep oxygen shortfall bites through the tank's water
        Assert.True(engine.AppetiteFactorFor(population) < 0.7);
        // Density: 1.0 below 50 kg/m³, the land curve above.
        Assert.Equal(1.0, LandHooks.DensityGrowthMultiplier(engine, holding, population), 12);
        population.Cohorts[0].MeanWeightKg = 70.0 * tank.VolumeM3 / population.TotalCount;
        double at70 = LandHooks.DensityGrowthMultiplier(engine, holding, population);
        Assert.Equal(LandWaterModel.DensityGrowthMultiplier(70.0, engine.LandCalibration.Density), at70, 9);
        Assert.InRange(at70, 0.85, 0.99);
    }

    [Fact]
    public void Vannstrategi_ChangesTheWater_AndRefusesOutOfRange()
    {
        var engine = NewEngine(50);
        var site = Commission(engine);
        Assert.True(engine.StockLandTank(site.Id, "t01", Room(engine, site, "t01")).Ok);
        engine.Advance(200);
        double co2At065 = site.Lots[0].Tanks[0].Co2MgL;
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SetVannstrategi(site.Id, 1.0, 0.5, 12.0, true).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SetVannstrategi(site.Id, 0.5, 1.5, 12.0, true).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SetVannstrategi(site.Id, 0.5, 0.5, 20.0, true).ErrorKey);
        Assert.Equal(LandSiteReasons.NotFound, engine.SetVannstrategi("ras_99", 0.5, 0.5, 12.0, true).ErrorKey);
        Assert.True(engine.SetVannstrategi(site.Id, 0.9, 0.5, 12.0, true).Ok);
        engine.Advance(1);
        // Reuse past the strippers' design (0.65) returns unstripped water: CO2 rises (F2-12 acceptance 3).
        Assert.True(site.Lots[0].Tanks[0].Co2MgL > co2At065 * 1.2, $"{co2At065:F1} → {site.Lots[0].Tanks[0].Co2MgL:F1} mg/L");
        Assert.Equal((0.9, 12.0, true), (site.RecirculationRate, site.TargetWaterTemp, site.BlendTowardSetpoint));
    }

    [Fact]
    public void HeatRecovery_CostsItsCapex_AndCutsTheHeatingEnergy()
    {
        var plain = NewEngine(51);
        var fitted = NewEngine(51);
        var a = Commission(plain, LandTemplate.PostSmoltModule, 1, "pad_delta");
        var b = Commission(fitted, LandTemplate.PostSmoltModule, 1, "pad_delta");
        Assert.False(b.Lots[0].HeatRecovery);
        decimal capex = fitted.FinancialStatements.LifetimeCapex;
        var install = fitted.InstallHeatRecovery(b.Lots[0].Id);
        Assert.True(install.Ok, install.Message);
        decimal cost = fitted.LandCalibration.Build.HeatRecoveryCapexNokPerMWe * (decimal)b.Lots[0].HeatPumpMWe;
        Assert.Equal(capex + cost, fitted.FinancialStatements.LifetimeCapex);
        Assert.Equal(LandSiteReasons.AlreadyActive, fitted.InstallHeatRecovery(b.Lots[0].Id).ErrorKey);
        // January: the intake is colder than the setpoint, so the heat pumps work.
        foreach (var (engine, site) in new[] { (plain, a), (fitted, b) })
        {
            engine.Advance(engine.LandCalibration.PostSmolt.BiofilterMaturationDays);
            Assert.True(engine.StockLandTank(site.Id, site.Lots[0].Tanks[0].Id, 200_000).Ok);
        }
        double plainKwh = 0.0, fittedKwh = 0.0;
        for (int d = 0; d < 30; d++)
        {
            plain.Advance(1);
            fitted.Advance(1);
            plainKwh += a.DailyEnergyKWh;
            fittedKwh += b.DailyEnergyKWh;
        }
        Assert.True(fittedKwh < plainKwh, $"{plainKwh:N0} vs {fittedKwh:N0} kWh");
    }

    // ── Determinism: save/load and twins ──────────────────────────────────

    [Fact]
    public void ASiteMidCycle_SavesAndResumesBitIdentically_AndTwinsAgree()
    {
        SimulationEngine Played()
        {
            var e = NewEngine(52);
            var s = Commission(e, LandTemplate.HfsLot, 2);
            Assert.True(e.StockLandTank(s.Id, "t01", 70_000).Ok);
            Assert.True(e.StockLandTank(s.Id, "t15", 40_000).Ok);
            Assert.True(e.SetVannstrategi(s.Id, 0.7, 0.3, 12.5, false).Ok);
            e.Advance(40);
            return e;
        }
        var engine = Played();
        var twin = Played();
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(twin)));
        var site = engine.LandSites.Single();
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: false);
        engine.Advance(30);
        resumed.Advance(30);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed)));
        var resumedSite = resumed.FindLandSite(site.Id)!;
        Assert.Equal(site.Lots.SelectMany(l => l.Tanks).Select(t => (t.Id, t.Pen.PopulationId != "", t.Co2MgL, t.TemperatureC)),
            resumedSite.Lots.SelectMany(l => l.Tanks).Select(t => (t.Id, t.Pen.PopulationId != "", t.Co2MgL, t.TemperatureC)));
    }

    /// <summary>
    /// F2-12 acceptance 1-2 on the engine path: a Salmon Evolution-like full
    /// HFS lot in PO5 water (r 0.65, the intakes blended toward 12 °C, heat
    /// recovery), every tank stocked to its room in rotation and harvested at
    /// the plan's 5.2 kg, measured over its second year: electricity per kg of
    /// live-weight biomass growth inside [L:R2]'s 6.0-7.5 kWh/kg and liquid
    /// oxygen inside 0.45-0.65 kg O2 per kg, as the standalone projection
    /// (docs/BALANCE.md §1p) measured them.
    /// </summary>
    [Fact]
    public void TheEngineLot_HoldsTheStandaloneEnergyAndOxygenAnchors()
    {
        var engine = NewEngine(54);
        engine.State.HomeProductionArea = LakskongenSim.Tests.Farm.LandTestSites.SalmonEvolutionArea;
        var site = Commission(engine);
        // F2-15: the projection is the plan, which a plant at competence 1.0 grows (and its cap is the nameplate).
        site.Competence.CompletedCycles = 3;
        var lot = site.Lots[0];
        var cal = engine.LandCalibration;
        double harvestedKg = 0.0, stockedKg = 0.0, kwh = 0.0, lox = 0.0, startKg = 0.0;
        var planForecast = LandStockingPlanner.Project(engine.Growth.Law, LandStockingPlan.For(cal.Hfs, cal.Production) with
        {
            TemperatureC = site.TargetWaterTemp,
            GrowthMultiplier = engine.LandCalibration.Production.PlanGrowthMultiplier * SmoltStrainRegistry.Get("standard").BuildGenome().GetGrowthMultiplier(),
        });
        int stagger = 365 / lot.Tanks.Count;
        // F2-15: a harvested tank stands its fallow days before it is restocked, as the projection's do.
        var harvestedOn = Enumerable.Repeat(int.MinValue / 2, lot.Tanks.Count).ToArray();
        var shallow = new double[2 * SimCalendar.DaysPerYear];
        for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day++)
        {
            bool measuring = day >= SimCalendar.DaysPerYear;
            if (day == SimCalendar.DaysPerYear) startKg = site.GetTotalBiomassKg();
            for (int i = 0; i < lot.Tanks.Count; i++)
            {
                var tank = lot.Tanks[i];
                var fish = site.PopulationIn(tank);
                if (fish != null && fish.TotalCount > 0 && fish.GetTotalBiomassKg() / fish.TotalCount >= cal.Hfs.PlanHarvestWeightKg)
                {
                    var harvest = engine.HarvestLandTank(site.Id, tank.Id);
                    Assert.True(harvest.Ok, harvest.Message);
                    if (measuring) harvestedKg += harvest.Value;
                    harvestedOn[i] = day;
                }
                else if (tank.Pen.IsEmpty && day >= i * stagger && day >= harvestedOn[i] + cal.Production.FallowDays)
                {
                    // The plan stocks each tank for its 80 kg/m³ peak (LandProjectionSettings' plan).
                    int room = planForecast.MaxSmolts(tank.VolumeM3, cal.Hfs.PlanPeakDensityKgM3);
                    var stocked = engine.StockLandTank(site.Id, tank.Id, room);
                    Assert.True(stocked.Ok, stocked.Message);
                    if (measuring) stockedKg += room * cal.Hfs.PlanReleaseWeightKg;
                }
            }
            engine.Advance(1);
            shallow[day] = engine.SeaTemperatureOfZone(site.ZoneId);
            if (measuring)
            {
                kwh += site.DailyEnergyKWh;
                lox += site.DailyLoxKg;
            }
        }
        double growth = harvestedKg + site.GetTotalBiomassKg() - startKg - stockedKg;
        double kwhPerKg = kwh / growth, loxPerKg = lox / growth;
        _output.WriteLine($"engine HFS lot, PO5, year 2: {growth / 1000.0:N0} t growth ({harvestedKg / 1000.0:N0} t harvested), " +
                          $"{kwh / 1e6:N2} GWh, {kwhPerKg:F2} kWh/kg, {loxPerKg:F3} kg O2/kg, standing {site.GetTotalBiomassKg() / 1000.0:N0} t");
        Assert.True(growth > 3_000_000.0, $"growth {growth:N0} kg");
        Assert.InRange(kwhPerKg, 6.0, 7.5);
        Assert.InRange(loxPerKg, 0.45, 0.65);
        // And the engine path is the standalone physics: within 6 % of the projection's year (§1p).
        // F2-15: like with like, the projection grows the engine's fish (the plan's multiplier × the
        // standard strain's genome, planForecast's plan; the engine's fish keep the plan's survival)
        // on the engine's pad (its intake lines) and the shallow water the engine's year drew.
        var standalone = LandLotProjector.Run(LakskongenSim.Tests.Farm.LandTestSites.SalmonEvolutionYear(cal) with
        {
            Site = engine.LandIntakeOf(site),
            ShallowWaterC = d => d >= 0 && d < shallow.Length
                ? shallow[d]
                : ProductionAreas.ClimatologyC(LakskongenSim.Tests.Farm.LandTestSites.SalmonEvolutionArea, d),
            Plan = LandStockingPlan.For(cal.Hfs, cal.Production) with
            {
                TemperatureC = site.TargetWaterTemp,
                GrowthMultiplier = cal.Production.PlanGrowthMultiplier * SmoltStrainRegistry.Get("standard").BuildGenome().GetGrowthMultiplier(),
            },
        }, cal);
        _output.WriteLine($"standalone: {standalone.NetGrowthKg / 1000.0:N0} t growth ({standalone.HarvestedKg / 1000.0:N0} t harvested), " +
                          $"{standalone.Energy.TotalKWh / 1e6:N2} GWh, {standalone.KWhPerKgGrowth:F2} kWh/kg, {standalone.LoxPerKgGrowth:F3} kg O2/kg, " +
                          $"mean density {standalone.MeanDensityKgM3:F1} kg/m³, appetite {standalone.MeanAppetite:F3}");
        Assert.InRange(kwhPerKg / standalone.KWhPerKgGrowth, 0.94, 1.06);
        Assert.InRange(loxPerKg / standalone.LoxPerKgGrowth, 0.94, 1.06);
    }

    [Fact]
    public void GradingATank_MovesItsShareIntoAnEmptyTank_AndKeepsEveryFish()
    {
        var engine = NewEngine(55);
        var site = Commission(engine);
        var lot = site.Lots[0];
        var pre = lot.Tanks.First(t => t.Role == TankRole.PreGrowOut);
        Assert.True(engine.StockLandTank(site.Id, pre.Id, 20_000).Ok);
        engine.Advance(30);
        var source = site.PopulationIn(pre)!;
        int before = source.TotalCount;
        double weight = source.GetTotalBiomassKg() / before;
        long all = engine.State.Populations.Sum(p => (long)p.TotalCount);

        Assert.Equal(LandSiteReasons.OutOfRange, engine.GradeTank(site.Id, pre.Id, "t01", 0.0).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.GradeTank(site.Id, pre.Id, pre.Id, 0.5).ErrorKey);
        Assert.Equal("holding_empty", engine.GradeTank(site.Id, "t02", "t03", 0.5).ErrorKey);
        var half = engine.GradeTank(site.Id, pre.Id, "t01", 0.5);
        Assert.True(half.Ok, half.Message);
        var moved = site.PopulationIn(lot.Tanks[0])!;
        Assert.NotSame(source, moved);
        Assert.Equal(before, source.TotalCount + moved.TotalCount);
        Assert.Equal(before / 2.0, moved.TotalCount, 0);
        Assert.Equal(weight, moved.GetTotalBiomassKg() / moved.TotalCount, 9);
        Assert.Equal(all, engine.State.Populations.Sum(p => (long)p.TotalCount));
        Assert.Equal(LandSiteReasons.TankOccupied, engine.GradeTank(site.Id, pre.Id, "t01", 1.0).ErrorKey);
        // The whole tank moves as it is: the same population, the source empty.
        Assert.True(engine.GradeTank(site.Id, pre.Id, "t02", 1.0).Ok);
        Assert.Same(source, site.PopulationIn(lot.Tanks[1]));
        Assert.Null(site.PopulationIn(pre));
        // Too many fish for the target's limit on the plan.
        Assert.True(engine.StockLandTank(site.Id, "t03", engine.PreviewLandStocking(site.Id, "t03", 0).DensityRoomSmolts).Ok);
        var pre2 = lot.Tanks.Last(t => t.Role == TankRole.PreGrowOut);
        Assert.True(engine.GradeTank(site.Id, "t03", pre2.Id, 1.0).ErrorKey == LandSiteReasons.DensityExceeded);
        engine.Advance(5);
        Assert.Empty(UnitResolver.Violations(engine.State));
    }

    [Fact]
    public void APreF2LandSite_KeepsThePreF2Tick()
    {
        var engine = NewEngine(53);
        var built = engine.BuildRASFacility("Legacy", RASScale.Medium);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id)!;
        Assert.False(site.HasLots);
        Assert.Empty(site.Lots);
        var population = new FishPopulation { ZoneId = site.ZoneId, SiteId = site.Id };
        Assert.Null(engine.HoldingOf(population).Pen);
        Assert.Equal(LandSiteReasons.NotFound, engine.StockLandTank(site.Id, "t01", 10).ErrorKey);
    }
}
