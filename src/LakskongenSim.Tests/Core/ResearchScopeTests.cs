using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// F2-04 acceptance 4 (docs/FARM_TYPES_SPEC.md §3.1 decision 0, §7.1): the two
/// farm-type research nodes act only on their own unit kinds. land-based-ras
/// (mortality −5 %, disease −14 %) reaches land tanks only; deep-sea-farming
/// (lice −10 %, disease −6 %) reaches offshore units only. Before F2-04 both
/// acted on every fjord pen, so researching either was a fjord upgrade.
/// </summary>
public class ResearchScopeTests
{
    private static SimulationEngine NewEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.ReceiveLoan(3_000_000_000m);
        return engine;
    }

    [Fact]
    public void TheEffectSums_IncludeANarrowedNodeOnlyForItsUnitKind()
    {
        var engine = NewEngine(1);
        engine.Research.ForceUnlock("sensor-networks");     // mortality 0.02, everywhere
        engine.Research.ForceUnlock("land-based-ras");      // mortality 0.05, disease 0.14: land
        engine.Research.ForceUnlock("deep-sea-farming");    // lice 0.10, disease 0.06: offshore

        Assert.Equal(0.02, engine.ResearchEffectFor(ResearchEffectKeys.MortalityReduction, HoldingKind.SeaPen), 12);
        Assert.Equal(0.07, engine.ResearchEffectFor(ResearchEffectKeys.MortalityReduction, HoldingKind.LandTank), 12);
        Assert.Equal(0.02, engine.ResearchEffectFor(ResearchEffectKeys.MortalityReduction, HoldingKind.Offshore), 12);
        Assert.Equal(0.0, engine.ResearchEffectFor(ResearchEffectKeys.DiseaseResistanceBonus, HoldingKind.SeaPen));
        Assert.Equal(0.14, engine.ResearchEffectFor(ResearchEffectKeys.DiseaseResistanceBonus, HoldingKind.LandTank), 12);
        Assert.Equal(0.06, engine.ResearchEffectFor(ResearchEffectKeys.DiseaseResistanceBonus, HoldingKind.Offshore), 12);
        Assert.Equal(0.0, engine.ResearchEffectFor(ResearchEffectKeys.LiceReduction, HoldingKind.SeaPen));
        Assert.Equal(0.10, engine.ResearchEffectFor(ResearchEffectKeys.LiceReduction, HoldingKind.Offshore), 12);
        // The fjord-pen figures (the engine's properties, the forecasts, the UI's totals) leave both out.
        Assert.Equal(0.02, engine.MortalityReduction, 12);
        Assert.Equal(0.0, engine.DiseaseResistanceBonus);
        Assert.Equal(0.0, engine.LiceReduction);
        Assert.Equal(0.02, engine.Research.GetTotalEffect(ResearchEffectKeys.MortalityReduction), 12);
    }

    /// <summary>A fjord site's pen and a land site's tank, both stocked with the same big cohort.</summary>
    private static (FishPopulation Fjord, FishPopulation Tank) StockFjordAndLand(SimulationEngine engine)
    {
        var site = new SeaSite("fjord-1", "zone-scope") { MABTonnes = 10_000 };
        engine.State.SeaSites.Add(site);
        var fjord = new FishPopulation { Id = "fjord-pen" };
        fjord.InjectDependencies(engine.Growth, engine.Mortality);
        fjord.Cohorts.Add(new FishPopulation.Cohort(200, 1_000_000, 1.0, LifeStage.Seawater));
        site.Stock(fjord);
        engine.State.Populations.Add(fjord);

        var ras = new RASFacility { Id = "ras-scope", Name = "Land", ConstructionProgress = 1.0 };
        engine.State.RASFacilities.Add(ras);
        var tank = new FishPopulation { Id = "land-tank", SiteId = ras.Id };
        tank.InjectDependencies(engine.Growth, engine.Mortality);
        tank.Cohorts.Add(new FishPopulation.Cohort(200, 1_000_000, 1.0, LifeStage.Seawater));
        ras.Tanks.Add(tank);
        engine.State.Populations.Add(tank);
        return (fjord, tank);
    }

    [Fact]
    public void LandBasedRas_LowersDeathsInLandTanks_AndLeavesTheFjordExactlyAsItWas()
    {
        var plain = NewEngine(31);
        var researched = NewEngine(31);
        researched.Research.ForceUnlock("land-based-ras");
        var (plainFjord, plainTank) = StockFjordAndLand(plain);
        var (resFjord, resTank) = StockFjordAndLand(researched);
        Assert.Equal(HoldingKind.LandTank, researched.HoldingOf(resTank).Kind);

        plain.Advance(150);
        researched.Advance(150);

        // The fjord pen: not one fish or gram apart.
        Assert.Equal(plainFjord.TotalCount, resFjord.TotalCount);
        Assert.Equal(plainFjord.GetTotalBiomassKg(), resFjord.GetTotalBiomassKg());
        // The tank: 5 % of the baseline hazard (and 14 % of the disease hazard) removed.
        int plainDeaths = 1_000_000 - plainTank.TotalCount;
        int researchedDeaths = 1_000_000 - resTank.TotalCount;
        Assert.True(plainDeaths > 1_000, $"only {plainDeaths} deaths in 150 days");
        Assert.True(researchedDeaths < plainDeaths, $"{researchedDeaths} vs {plainDeaths}");
    }

    [Fact]
    public void DeepSeaFarming_BlocksLiceAtOffshoreUnits_AndLeavesTheFjordExactlyAsItWas()
    {
        static (SeaSite Fjord, SeaSite Unit) Game(SimulationEngine engine)
        {
            var site = new SeaSite("fjord-1", "zone-scope") { MABTonnes = 10_000 };
            engine.State.SeaSites.Add(site);
            var pen = new FishPopulation { Id = "fjord-pen" };
            pen.InjectDependencies(engine.Growth, engine.Mortality);
            pen.Cohorts.Add(new FishPopulation.Cohort(200, 200_000, 1.0, LifeStage.Seawater));
            site.Stock(pen);
            engine.State.Populations.Add(pen);
            var built = engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible);
            Assert.True(built.Ok, built.Message);
            var unit = engine.OffshoreUnits.Single();
            unit.Offshore!.ConstructionProgress = 1.0;
            Assert.True(engine.StockOffshoreFarm(unit.Id, 200_000).Ok);
            return (site, unit);
        }
        var plain = NewEngine(32);
        var researched = NewEngine(32);
        researched.Research.ForceUnlock("deep-sea-farming");
        var (plainFjord, plainUnit) = Game(plain);
        var (resFjord, resUnit) = Game(researched);
        plain.Advance(60);
        researched.Advance(60);

        // The fjord site blocks no more settlement than without the research
        // (its lice may still differ a little: the unit's own lice, now fewer,
        // reach it farm to farm).
        Assert.Equal(plainFjord.Lice.PreventiveCoverage01, resFjord.Lice.PreventiveCoverage01);
        Assert.True(resUnit.Lice.PreventiveCoverage01 > plainUnit.Lice.PreventiveCoverage01,
            $"{resUnit.Lice.PreventiveCoverage01} vs {plainUnit.Lice.PreventiveCoverage01}");
        // The unit blocks 10 % more of what got through: 1 − (1 − before) × (1 − 0.10).
        Assert.Equal(1.0 - (1.0 - plainUnit.Lice.PreventiveCoverage01) * 0.9, resUnit.Lice.PreventiveCoverage01, 9);
    }

    [Fact]
    public void AGameThatResearchesNeither_SumsEveryEffectExactlyAsBefore()
    {
        // The per-kind sum walks the tree in the same order as the old one, so
        // a game with no narrowed node unlocked reads bit-identical effects.
        var engine = NewEngine(33);
        foreach (var id in new[] { "selective-breeding", "disease-resistance", "sensor-networks", "underwater-drones", "automated-feeding" })
            engine.Research.ForceUnlock(id);
        foreach (var key in ResearchEffectKeys.Live)
        {
            double old = engine.Research.Nodes.Values.Where(n => n.Unlocked && n.Effects.ContainsKey(key)).Sum(n => n.Effects[key]);
            foreach (var kind in new[] { HoldingKind.SeaPen, HoldingKind.Offshore, HoldingKind.LandTank })
                Assert.Equal(BitConverter.DoubleToInt64Bits(old), BitConverter.DoubleToInt64Bits(engine.ResearchEffectFor(key, kind)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(old), BitConverter.DoubleToInt64Bits(engine.GetResearchEffect(key)));
        }
    }
}
