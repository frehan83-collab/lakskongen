using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The pre-registered handling probe of F2-08 acceptance 5
/// (docs/FARM_TYPES_SPEC.md §7.1; RUN_LOG §7 records the reading): an OF1-class
/// unit on off_1 in PO 6 (OF1's waters off Frøya), run on its Driftsplan
/// defaults plus "Behandle ved krav" with the chosen fish-handling vessel,
/// through ten winters (October to March) per seed, events off so the sea is
/// the model's. Each 1 September the cycle restarts (the fish harvested at
/// once, about 1 M smolts stocked); on a seeded day of each winter (15 October
/// to 11 February) the lice go over the limit at two counts, which raises a
/// delousing requirement. A winter counts a warning, an order or a withdrawal
/// case when the escalation reached it during that winter.
/// </summary>
public static class OffshoreHandlingProbe
{
    /// <summary>The production area the probe's unit stands in (PO 6).</summary>
    public const int ProductionArea = 6;

    /// <summary>One winter's outcome.</summary>
    public sealed record Winter(int Seed, int Index, int TriggerDay, bool Warned, bool Ordered, bool Withdrawn, int MaxOverdueDays, int DaysToFirstHandling);

    /// <summary>Run <paramref name="winters"/> winters for one seed with the handling vessel <paramref name="vesselChoice"/>.</summary>
    public static List<Winter> Run(int seed, int winters, string vesselChoice)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        engine.State.HomeProductionArea = ProductionArea;
        var built = engine.BuildOffshoreFarm("Probe", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        var site = engine.OffshoreUnits.Single(u => u.Id == built.Id);
        var unit = site.Offshore!;
        unit.ConstructionDays = 1;
        for (int guard = 0; !unit.IsCommissioned; guard++)
        {
            Assert.True(guard < 10, "the unit never commissioned");
            engine.Advance(1);
        }
        Assert.True(engine.SetDriftsplan(site.Id, OffshorePolicies.Handling, null,
            new Dictionary<string, string> { [HandlingPolicy.HandlingVessel.Key] = vesselChoice }).Ok);

        // A private stream for the trigger days: the seed's, moving no game stream.
        var triggers = new SimRngStream(SimRng.Mix((ulong)(uint)seed ^ 0x5EA2_7121UL));
        var results = new List<Winter>();
        for (int w = 0; w < winters; w++)
        {
            int year = w; // winter w runs from 1 October of year w to 31 March of year w+1
            int restock = year * SimCalendar.DaysPerYear + SimCalendar.MonthStartDayOfYear(8);   // 1 September
            int winterStart = year * SimCalendar.DaysPerYear + SimCalendar.MonthStartDayOfYear(9); // 1 October
            int winterEnd = (year + 1) * SimCalendar.DaysPerYear + SimCalendar.MonthStartDayOfYear(3) - 1; // 31 March
            int trigger = winterStart + 14 + (int)(triggers.NextDouble() * 120);
            AdvanceTo(engine, restock);
            if (site.Pens.Any(p => p.TotalCount > 0)) engine.HarvestOffshoreFarm(site.Id);
            foreach (var order in engine.HarvestOrders.Where(o => o.HoldingId == site.Id).ToList()) engine.CancelHarvestOrder(order.Id);
            for (int z = 0; z < site.PhysicalPens.Count; z++)
                Assert.True(engine.StockOffshoreFarm(site.Id, 330_000).Ok);
            site.ConsecutiveLiceBreaches = 0;
            site.Lice.AdultFemalePerFish = 0.05;
            AdvanceTo(engine, trigger - 1);
            site.Lice.AdultFemalePerFish = 2.0;
            site.ConsecutiveLiceBreaches = 2;
            bool warned = false, ordered = false, withdrawn = false;
            int maxOverdue = 0, firstHandling = -1;
            int handledBefore = unit.LastHandlingDoneDay;
            while (engine.State.Day < winterEnd)
            {
                engine.Advance(1);
                maxOverdue = Math.Max(maxOverdue, unit.HandlingOverdueDays);
                warned |= unit.HandlingStage >= HandlingStage.Warned;
                ordered |= unit.HandlingStage >= HandlingStage.Ordered;
                withdrawn |= unit.WithdrawalCaseId.Length > 0;
                if (firstHandling < 0 && unit.LastHandlingDoneDay > handledBefore && unit.LastHandlingDoneDay >= trigger)
                    firstHandling = unit.LastHandlingDoneDay - trigger;
            }
            results.Add(new Winter(seed, w, trigger, warned, ordered, withdrawn, maxOverdue, firstHandling));
        }
        return results;
    }

    private static void AdvanceTo(SimulationEngine engine, int day)
    {
        if (day > engine.State.Day) engine.Advance(day - engine.State.Day);
    }
}
