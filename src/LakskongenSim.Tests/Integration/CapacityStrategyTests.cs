using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S2-01a acceptance 1 (and roadmap D10's "overstock" family): on
/// <c>po3_red_sone</c>, events on, the pre-S2-01 overstock exploit (three
/// times the plan's fish) is refused and ends below the compliant player; the
/// same script under the old rules dominated, which is the defect S2-01 fixes
/// (and proves the comparison can fail); a player who grows fish on over the
/// caps is fined, and the fines cost more than the illegal kilos earned.
/// Measured over 20 seeds in docs/BALANCE.md §1h.
/// </summary>
public class CapacityStrategyTests
{
    private readonly ITestOutputHelper _out;
    public CapacityStrategyTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Overstock_IsRefused_AndEndsBelowTheCompliantPlayer(int seed)
    {
        var compliant = CapacityStrategyProbe.Run(seed, CapacityStrategy.Compliant);
        var overstock = CapacityStrategyProbe.Run(seed, CapacityStrategy.Overstock);
        _out.WriteLine(CapacityStrategyProbe.Format(compliant));
        _out.WriteLine(CapacityStrategyProbe.Format(overstock));

        // Refused at the door, each site, and the refusal costs nothing.
        Assert.Equal(2, overstock.RefusedRequests);
        Assert.StartsWith("mtb_", overstock.RefusedStockingKey);
        Assert.False(overstock.RefusedStockingMovedCash);
        Assert.Equal(0, overstock.StockedSmolts);
        Assert.True(overstock.PeakStandingTonnes <= overstock.AreaMtbTonnes);
        // The compliant player fills the plan and never breaks a cap.
        Assert.True(compliant.StockedSmolts > 100_000, $"compliant stocked {compliant.StockedSmolts:N0}");
        Assert.Equal(0, compliant.Breaches);
        Assert.True(compliant.PeakStandingTonnes <= compliant.AreaMtbTonnes);
        Assert.True(overstock.EndingEquityNok < compliant.EndingEquityNok,
            $"seed {seed}: overstock {overstock.EndingEquityNok:N0} NOK >= compliant {compliant.EndingEquityNok:N0} NOK");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void UnderTheOldRules_TheSameOverstockDominated(int seed)
    {
        var compliant = CapacityStrategyProbe.Run(seed, CapacityStrategy.Compliant, regulated: false);
        var overstock = CapacityStrategyProbe.Run(seed, CapacityStrategy.Overstock, regulated: false);
        _out.WriteLine(CapacityStrategyProbe.Format(compliant));
        _out.WriteLine(CapacityStrategyProbe.Format(overstock));

        // Only the smolts' own weight was checked: three times the fish went in,
        // grew to about twice the licences and outearned the compliant player.
        Assert.Equal(CapacityStrategyProbe.OverstockFactor * compliant.StockedSmolts, overstock.StockedSmolts);
        Assert.True(overstock.PeakStandingTonnes > 1.5 * overstock.AreaMtbTonnes);
        Assert.True(overstock.EndingEquityNok > compliant.EndingEquityNok);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void GrowingOverTheCaps_IsFined_AndTheFinesCostMoreThanTheIllegalKilosEarned(int seed)
    {
        var fined = CapacityStrategyProbe.Run(seed, CapacityStrategy.GrowOn);
        var unpoliced = CapacityStrategyProbe.Run(seed, CapacityStrategy.GrowOn, regulated: false);
        _out.WriteLine(CapacityStrategyProbe.Format(fined));
        _out.WriteLine(CapacityStrategyProbe.Format(unpoliced));

        if (fined.Breaches == 0)
        {
            // Never over a cap: the rules cost nothing.
            Assert.Equal(unpoliced.EndingEquityNok, fined.EndingEquityNok);
            return;
        }
        Assert.True(fined.FeesNok > 0m);
        Assert.True(fined.ConfiscatedNok > 0m);
        Assert.True(fined.EndingEquityNok < unpoliced.EndingEquityNok,
            $"seed {seed}: the breach paid ({fined.EndingEquityNok:N0} vs {unpoliced.EndingEquityNok:N0} NOK unpoliced)");
    }

    [Fact]
    public void AGrowOnCareerWithABreach_ReplaysExactly()
    {
        var a = CapacityStrategyProbe.Run(7, CapacityStrategy.GrowOn);
        var b = CapacityStrategyProbe.Run(7, CapacityStrategy.GrowOn);
        Assert.Equal(a.EndingEquityNok, b.EndingEquityNok);
        Assert.Equal(a.FeesNok, b.FeesNok);
        Assert.Equal(a.ForcedHarvestKg, b.ForcedHarvestKg);
        Assert.Equal(a.DaysRun, b.DaysRun);
    }
}
