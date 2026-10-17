using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-07 on whole cycles (roadmap F2-07, docs/FARM_TYPES_SPEC.md §7.1
/// acceptance 7 and 8): what an offshore unit costs to run against an open
/// pen in the same production area, and a unit left to its Driftsplan alone.
/// Numbers in docs/BALANCE.md §1v.
/// </summary>
public class OffshoreCycleTests
{
    private readonly ITestOutputHelper _out;
    public OffshoreCycleTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Acceptance 7: the unit's operating cost per kilo of gutted fish, ex feed
    /// and ex depreciation (and ex smolts, the same fish in both), is 1.15-1.45×
    /// an open pen's in the same PO: the median ratio over 10 seeds of the same
    /// company played by the same rules ([O:R37]: offshore pilots at 56 NOK/kg
    /// against conventional production, +30 %).
    /// </summary>
    [Fact]
    public void TheUnitsOpexPerKg_IsOneFifteenToOneFortyFiveTimesAnOpenPensInTheSamePo()
    {
        var ratios = new List<double>();
        foreach (int seed in Enumerable.Range(1, 10))
        {
            var fjord = OffshoreFarmCycle.Run(seed, offshore: false);
            var unit = OffshoreFarmCycle.Run(seed, offshore: true);
            double ratio = unit.OpexPerKgHog / fjord.OpexPerKgHog;
            ratios.Add(ratio);
            _out.WriteLine($"seed {seed}: fjord {fjord.OpexPerKgHog:F2} NOK/kg HOG ({fjord.HogKg / 1000:N0} t, {fjord.Days} d), " +
                $"offshore {unit.OpexPerKgHog:F2} ({unit.HogKg / 1000:N0} t, {unit.Days} d), ratio {ratio:F3}");
            _out.WriteLine("  fjord: " + string.Join(", ", fjord.Lines.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {(double)kv.Value / fjord.HogKg:F2}")));
            _out.WriteLine("  unit:  " + string.Join(", ", unit.Lines.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {(double)kv.Value / unit.HogKg:F2}")));
        }
        ratios.Sort();
        double median = (ratios[4] + ratios[5]) / 2.0;
        _out.WriteLine($"ratio min {ratios[0]:F3} median {median:F3} max {ratios[^1]:F3}");
        Assert.InRange(median, 1.15, 1.45);
    }
}
