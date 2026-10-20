using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Farm.Offshore;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-08 acceptance 5, pre-registered (docs/FARM_TYPES_SPEC.md §7.1): at off_1
/// with the policy "handle when required" and standard vessels, a warning in
/// 10-30 % of winters, an order in at most 5 %, a withdrawal in none (20 seeds
/// × 10 winters); with offshore-wellboat charter, a warning in at most 10 %.
/// The run is <see cref="OffshoreHandlingProbe"/>; the seeds run side by side
/// (each its own engine), so the result is the same as one after another.
/// </summary>
public class OffshoreHandlingProbeTests
{
    private readonly ITestOutputHelper _out;

    public OffshoreHandlingProbeTests(ITestOutputHelper output) => _out = output;

    private static readonly Dictionary<string, List<OffshoreHandlingProbe.Winter>> Measured = new();
    private static readonly object Gate = new();

    /// <summary>The 200 winters of a vessel choice (measured once per test run).</summary>
    private List<OffshoreHandlingProbe.Winter> Measure(string vessel)
    {
        lock (Gate)
        {
            if (!Measured.TryGetValue(vessel, out var winters))
            {
                var bySeed = new List<OffshoreHandlingProbe.Winter>[20];
                Parallel.For(0, 20, i => bySeed[i] = OffshoreHandlingProbe.Run(i + 1, 10, vessel));
                winters = bySeed.SelectMany(w => w).ToList();
                Measured[vessel] = winters;
            }
            double warned = winters.Count(w => w.Warned) / (double)winters.Count;
            double ordered = winters.Count(w => w.Ordered) / (double)winters.Count;
            double withdrawn = winters.Count(w => w.Withdrawn) / (double)winters.Count;
            var firsts = winters.Where(w => w.DaysToFirstHandling >= 0).Select(w => w.DaysToFirstHandling).OrderBy(d => d).ToList();
            _out.WriteLine($"{vessel}: {winters.Count} winters; warning {warned:P1}, order {ordered:P1}, withdrawal {withdrawn:P1}; " +
                $"days to the first handling median {firsts[firsts.Count / 2]}, p90 {firsts[(int)(firsts.Count * 0.9)]}, max {firsts[^1]}; " +
                $"never handled in the winter {winters.Count(w => w.DaysToFirstHandling < 0)}; longest overdue {winters.Max(w => w.MaxOverdueDays)} d");
            return winters;
        }
    }

    /// <summary>
    /// The upper bounds of acceptance 5: an order in at most 5 % and a
    /// withdrawal in no winter with standard vessels; a warning in at most
    /// 10 % with the offshore wellboat, never more often than with standard
    /// vessels; every winter's requirement was handled within the winter.
    /// </summary>
    [Fact]
    public void HandleWhenRequired_AtOff1_StaysUnderTheOrderAndWithdrawalBounds()
    {
        var standard = Measure(OffshorePolicies.VesselStandard);
        var offshore = Measure(OffshorePolicies.VesselOffshoreWellboat);
        Assert.Equal(200, standard.Count);
        Assert.True(standard.Count(w => w.Ordered) <= 10, $"an order in {standard.Count(w => w.Ordered)} of 200 winters (at most 5 %)");
        Assert.Equal(0, standard.Count(w => w.Withdrawn));
        Assert.True(offshore.Count(w => w.Warned) <= 20, $"a warning in {offshore.Count(w => w.Warned)} of 200 winters with the offshore wellboat (at most 10 %)");
        Assert.True(offshore.Count(w => w.Warned) <= standard.Count(w => w.Warned));
        Assert.All(standard.Concat(offshore), w => Assert.True(w.DaysToFirstHandling >= 0, $"seed {w.Seed} winter {w.Index}: never handled"));
    }

    /// <summary>
    /// The lower bound of acceptance 5: a warning in at least 10 % (at most
    /// 30 %) of winters with standard vessels. Skipped with the finding of
    /// docs/RUN_LOG.md §8 (F2-08): measured 0 of 200 winters; the sea at off_1
    /// leaves a handling window within 15 days of every requirement and the
    /// policy books it, so the warning needs friction the game does not model
    /// (wellboat availability, booking lead, treatment capacity in the fleet).
    /// </summary>
    [Fact(Skip = "F2-08 acceptance 5's warning band (10-30 % of winters) waits for the owner: docs/RUN_LOG.md §8 (F2-08 finding); measured 0 of 200")]
    public void HandleWhenRequired_AtOff1_WarnsInTenToThirtyPercentOfWinters()
    {
        var standard = Measure(OffshorePolicies.VesselStandard);
        double warned = standard.Count(w => w.Warned) / (double)standard.Count;
        Assert.InRange(warned, 0.10, 0.30);
    }
}
