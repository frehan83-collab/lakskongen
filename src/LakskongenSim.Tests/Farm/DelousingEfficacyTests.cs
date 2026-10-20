using System;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Health;
using Xunit;
using DelousingMethod = LakskongenSim.Data.DelousingMethod;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F480: a delousing method kills its own efficacy of the mobile lice, less
/// the resistance of its resistance slot. The kill used to be normalised by
/// the lice model's per-type base and capped there: Laser (0.90) fell to the
/// unknown-type 0.50, Thermolicer (0.95) and Hydrolicer (0.85) to the
/// mechanical 0.82, and hydrogen peroxide (0.70) to 0.65.
/// </summary>
public class DelousingEfficacyTests
{
    public static TheoryData<DelousingMethod> Methods()
    {
        var data = new TheoryData<DelousingMethod>();
        foreach (var method in Enum.GetValues<DelousingMethod>())
            data.Add(method);
        return data;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void EachMethod_KillsItsProfileEfficacy_OfTheMobileLice(DelousingMethod method)
    {
        var lice = new SeaLiceModel { PreAdultPerFish = 1.0, AdultFemalePerFish = 1.0, AdultMalePerFish = 1.0 };

        Treat(method, lice);

        // 10 °C: no temperature penalty on the chemical methods; no resistance yet.
        double expectedSurvival = 1.0 - DelousingSystem.GetProfile(method).Efficacy;
        Assert.Equal(expectedSurvival, lice.AdultFemalePerFish, 6);
        Assert.Equal(expectedSurvival, lice.PreAdultPerFish, 6);
        Assert.Equal(expectedSurvival, lice.AdultMalePerFish, 6);
    }

    [Fact]
    public void Resistance_CutsTheKill_OfItsOwnSlotOnly()
    {
        var peroxide = new SeaLiceModel { AdultFemalePerFish = 1.0, HydrogenPeroxideResistance = 0.40 };
        Treat(DelousingMethod.HydrogenPeroxide, peroxide);
        Assert.Equal(1.0 - 0.70 * (1.0 - 0.40), peroxide.AdultFemalePerFish, 6);

        var thermal = new SeaLiceModel { AdultFemalePerFish = 1.0, MechanicalResistance = 0.10, HydrogenPeroxideResistance = 0.90 };
        Treat(DelousingMethod.Thermolicer, thermal);
        Assert.Equal(1.0 - 0.95 * (1.0 - 0.10), thermal.AdultFemalePerFish, 6);

        // Laser has no resistance slot: the chemical resistances do not touch it.
        var laser = new SeaLiceModel { AdultFemalePerFish = 1.0, HydrogenPeroxideResistance = 0.90, MechanicalResistance = 0.90 };
        Treat(DelousingMethod.LaserDelousing, laser);
        Assert.Equal(1.0 - 0.90, laser.AdultFemalePerFish, 6);
    }

    [Fact]
    public void TheDoseBasedTreatment_StillScalesTheTypesOwnBase()
    {
        // ApplyTreatment(type, dose) keeps its contract for its other callers.
        var lice = new SeaLiceModel { AdultFemalePerFish = 1.0 };
        double kill = lice.ApplyTreatment("thermal", 0.5);
        Assert.Equal(SeaLiceModel.GetBaseEfficacy("thermal") * 0.5, kill, 6);
        Assert.Equal(1.0 - kill, lice.AdultFemalePerFish, 6);
    }

    private static void Treat(DelousingMethod method, SeaLiceModel lice)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(100_000_000m);
        engine.Initialize(enableEvents: false, seed: 480);
        var pop = new FishPopulation { Id = $"pop-{method}" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(300, 10_000, 1.0, LifeStage.Seawater));

        var result = new DelousingSystem().Treat(pop, method, lice, engine.IntegratedDisease, temperatureC: 10.0);

        Assert.True(result.Success, result.Message);
    }
}
