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
/// caps is fined on every seed and, over 20 seeds, ends below both the same
/// script unpoliced and the compliant player (roadmap D10, medians).
/// Measured over 20 seeds in docs/BALANCE.md §1j.
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

    /// <summary>
    /// Replaces the per-seed GrowingOverTheCaps_IsFined_AndTheFinesCostMoreThanTheIllegalKilosEarned
    /// (seeds 1-6, fined equity below unpoliced on every seed) under roadmap
    /// D10, which judges a rule-violating strategy by its median equity after
    /// fines over 20 seeds. On the integrated wave-2 tree the per-seed
    /// comparison is decided by the storyteller's price shocks on 4 of 20
    /// seeds (2, 8, 11, 16: the unpoliced career's fixed sale day lands in a
    /// 43 NOK/kg window that the fined career, its heaviest fish taken at a
    /// deadline, misses), not by the rules (RUN_LOG §7, docs/BALANCE.md §1j).
    /// Asserted here, over seeds 1-20: every grow-on career breaks a cap and is
    /// fined and confiscated on every seed; the median fined career ends below
    /// the median unpoliced one (the rules bite) and below the median
    /// compliant one (D10 (a) for the grow-on family).
    /// </summary>
    [Fact]
    public void GrowingOverTheCaps_IsFinedOnEverySeed_AndOverTwentySeedsEndsBelowTheUnpolicedAndCompliantMedians()
    {
        var fined = new System.Collections.Generic.List<decimal>();
        var unpoliced = new System.Collections.Generic.List<decimal>();
        var compliant = new System.Collections.Generic.List<decimal>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var f = CapacityStrategyProbe.Run(seed, CapacityStrategy.GrowOn);
            var u = CapacityStrategyProbe.Run(seed, CapacityStrategy.GrowOn, regulated: false);
            var c = CapacityStrategyProbe.Run(seed, CapacityStrategy.Compliant);
            _out.WriteLine(CapacityStrategyProbe.Format(f));
            _out.WriteLine(CapacityStrategyProbe.Format(u));
            _out.WriteLine(CapacityStrategyProbe.Format(c));

            Assert.True(f.Breaches > 0, $"seed {seed}: the grow-on career never broke a cap");
            Assert.True(f.FeesNok > 0m, $"seed {seed}: no infringement fee");
            Assert.True(f.ConfiscatedNok > 0m, $"seed {seed}: no confiscation");
            Assert.Equal(0, u.Breaches);
            Assert.Equal(0, c.Breaches);
            fined.Add(f.EndingEquityNok);
            unpoliced.Add(u.EndingEquityNok);
            compliant.Add(c.EndingEquityNok);
        }

        decimal medianFined = Median(fined), medianUnpoliced = Median(unpoliced), medianCompliant = Median(compliant);
        _out.WriteLine($"medians over 20 seeds: fined {medianFined:N0}, unpoliced {medianUnpoliced:N0}, compliant {medianCompliant:N0} NOK");
        Assert.True(medianFined < medianUnpoliced,
            $"the rules do not bite: fined median {medianFined:N0} >= unpoliced median {medianUnpoliced:N0} NOK");
        Assert.True(medianFined < medianCompliant,
            $"D10 (a): growing over the caps pays: fined median {medianFined:N0} >= compliant median {medianCompliant:N0} NOK");
    }

    private static decimal Median(System.Collections.Generic.List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
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
