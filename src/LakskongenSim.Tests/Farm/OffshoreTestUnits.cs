using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>Test helpers for F2-07's offshore units: engines, units ready to run, a shortened yard, a forced sea.</summary>
internal static class OffshoreTestUnits
{
    /// <summary>A bare engine with 5 bn NOK.</summary>
    public static SimulationEngine RichEngine(int seed, bool events = false, decimal cash = 5_000_000_000m)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(cash) };
        engine.Initialize(enableEvents: events, seed: seed);
        return engine;
    }

    /// <summary>
    /// A legacy-built OF1-class unit (the scenario path), operational after a
    /// day and commissioned by the next step: silo full, Driftsplan defaults on.
    /// With <paramref name="deck"/> it draws a teething deck at commissioning
    /// as a player-built unit does.
    /// </summary>
    public static SeaSite Commissioned(SimulationEngine engine, bool deck = false, int shakedownDays = -1)
    {
        var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        var site = engine.OffshoreUnits.Single(u => u.Id == built.Id);
        site.Offshore!.ConstructionDays = 1;
        site.Offshore.HasTeethingDeck = deck;
        if (shakedownDays >= 0) Assert.True(engine.ChooseShakedown(site.Id, shakedownDays).Ok);
        int guard = 0;
        while (!site.Offshore.IsCommissioned)
        {
            engine.Advance(1);
            Assert.True(++guard < 10, "the unit never commissioned");
        }
        return site;
    }

    /// <summary>Clear every standing order on the unit (a test that books by hand).</summary>
    public static void ClearDriftsplan(SimulationEngine engine, string unitId)
    {
        foreach (var entry in engine.GetDriftsplan(unitId).ToList())
            Assert.True(engine.ClearDriftsplan(unitId, entry.Policy).Ok);
    }

    /// <summary>
    /// An engine where OF1-class is unlocked: deep-sea-farming researched, a
    /// first harvest sold, a year without an escape (366 days of a bare game)
    /// and, since F2-09, a development-licence award for off_1 (granted as an
    /// approved application gives it, through the engine's test seam, so the
    /// helper's days stay F2-07's). <paramref name="award"/> false leaves it
    /// out (F2-09's licence_missing).
    /// </summary>
    public static SimulationEngine UnlockedForOf1(int seed, bool events = false, decimal cash = 5_000_000_000m, bool award = true)
    {
        var engine = RichEngine(seed, events, cash);
        engine.Research.ForceUnlock("deep-sea-farming");
        engine.State.HarvestBatchesSold = 1;
        engine.Advance(366);
        if (award) engine.AwardDevelopmentLicencesForTests("off_1");
        return engine;
    }

    /// <summary>
    /// Shorten a unit's construction to one planned day a phase but the tow (the current
    /// phase ends tomorrow), keeping its keys, payments and the player's tow.
    /// </summary>
    public static void ShortenProject(SimulationEngine engine, SeaSite site)
    {
        var project = engine.OffshoreProjectOf(site)!;
        project.Phases = project.Phases.Select(p => new ConstructionPhase
        {
            Key = p.Key, PlannedDays = p.PlayerStarted ? p.PlannedDays : 1, CapexShare = p.CapexShare, MaxHsM = p.MaxHsM,
            ClosedMonths = p.ClosedMonths, PlayerStarted = p.PlayerStarted,
        }).ToList();
        if (project.Status == ConstructionStatus.Building) project.PhaseDays = 1;
    }

    /// <summary>Advance until the unit's project waits at its tow (at most <paramref name="maxDays"/>).</summary>
    public static void AdvanceToTow(SimulationEngine engine, SeaSite site, int maxDays = 1200)
    {
        var project = engine.OffshoreProjectOf(site)!;
        for (int d = 0; d < maxDays; d++)
        {
            if (project.Status == ConstructionStatus.Waiting && project.Current?.Key == OffshoreProfiles.TowPhase) return;
            engine.Advance(1);
        }
        Assert.Fail($"{site.Id} never reached its tow (phase {project.Current?.Key}, {project.Status})");
    }
}
