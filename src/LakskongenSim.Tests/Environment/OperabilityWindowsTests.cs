#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-06 operability (docs/FARM_TYPES_SPEC.md §3.3 point 5): the operation
/// limits by vessel, the heavy-sea appetite, the helicopter's wind and
/// visibility gate, the odds of each operation per forecast day, and the
/// off_1 climatology of handling windows the plot cards and F2-08 quote.
/// </summary>
public class OperabilityWindowsTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private static OffshoreOperabilityCalibration Ops => Cal.Operability;
    private readonly ITestOutputHelper _output;

    public OperabilityWindowsTests(ITestOutputHelper output) => _output = output;

    private static string F(double v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    public static IEnumerable<object?[]> LimitTable()
    {
        double inf = double.PositiveInfinity;
        foreach (var op in new[] { OffshoreOp.Crowding, OffshoreOp.Delousing, OffshoreOp.Harvest, OffshoreOp.Stocking, OffshoreOp.Transfer })
        {
            yield return new object?[] { op, VesselClass.Standard, 2.0 };
            yield return new object?[] { op, VesselClass.DpWalkToWork, 2.0 };
            yield return new object?[] { op, VesselClass.OffshoreWellboat, 2.5 };
            yield return new object?[] { op, VesselClass.Helicopter, null };
        }
        yield return new object?[] { OffshoreOp.CrewChange, VesselClass.Standard, 2.0 };
        yield return new object?[] { OffshoreOp.CrewChange, VesselClass.DpWalkToWork, 3.0 };
        yield return new object?[] { OffshoreOp.CrewChange, VesselClass.Helicopter, inf };
        yield return new object?[] { OffshoreOp.CrewChange, VesselClass.OffshoreWellboat, null };
        yield return new object?[] { OffshoreOp.NetInspection, VesselClass.Standard, 3.0 };
        yield return new object?[] { OffshoreOp.NetInspection, VesselClass.DpWalkToWork, 3.0 };
        yield return new object?[] { OffshoreOp.NetInspection, VesselClass.Helicopter, null };
        yield return new object?[] { OffshoreOp.Resupply, VesselClass.Standard, 2.5 };
        yield return new object?[] { OffshoreOp.Resupply, VesselClass.DpWalkToWork, 3.0 };
        yield return new object?[] { OffshoreOp.Silage, VesselClass.Standard, 2.5 };
        yield return new object?[] { OffshoreOp.Silage, VesselClass.DpWalkToWork, 3.0 };
        yield return new object?[] { OffshoreOp.Silage, VesselClass.OffshoreWellboat, null };
        yield return new object?[] { OffshoreOp.Tow, VesselClass.Standard, 2.5 };
        yield return new object?[] { OffshoreOp.Tow, VesselClass.DpWalkToWork, null };
        foreach (VesselClass v in Enum.GetValues<VesselClass>())
            yield return new object?[] { OffshoreOp.Feeding, v, inf };
    }

    /// <summary>The spec's operability table, cell by cell (null: the vessel cannot do it).</summary>
    [Theory]
    [MemberData(nameof(LimitTable))]
    public void HsLimit_FollowsTheSpecTable(OffshoreOp op, VesselClass vessel, double? expected)
    {
        Assert.Equal(expected, OperabilityWindows.HsLimitM(op, vessel, Ops));
        Assert.Equal(op == OffshoreOp.Tow ? 3 : 1, OperabilityWindows.DaysNeeded(op, Ops));
    }

    [Fact]
    public void IsPossible_AtTheLimitButNotAbove_AndTheHelicopterNeedsWindAndVisibility()
    {
        Assert.True(OperabilityWindows.IsPossible(OffshoreOp.Delousing, VesselClass.Standard, 2.0, 10, 20, Ops));
        Assert.False(OperabilityWindows.IsPossible(OffshoreOp.Delousing, VesselClass.Standard, 2.0001, 10, 20, Ops));
        Assert.True(OperabilityWindows.IsPossible(OffshoreOp.Delousing, VesselClass.OffshoreWellboat, 2.4, 10, 20, Ops));
        Assert.False(OperabilityWindows.IsPossible(OffshoreOp.Delousing, VesselClass.Helicopter, 0.5, 5, 20, Ops));
        // The helicopter flies in any sea, below 25 m/s and at 1.5 km visibility or more.
        Assert.True(OperabilityWindows.IsPossible(OffshoreOp.CrewChange, VesselClass.Helicopter, 9.0, 24.9, 1.5, Ops));
        Assert.False(OperabilityWindows.IsPossible(OffshoreOp.CrewChange, VesselClass.Helicopter, 1.0, 25.0, 30, Ops));
        Assert.False(OperabilityWindows.IsPossible(OffshoreOp.CrewChange, VesselClass.Helicopter, 1.0, 5.0, 1.49, Ops));
        Assert.True(OperabilityWindows.IsPossible(OffshoreOp.Feeding, VesselClass.Standard, 12.0, 40, 0.1, Ops));
        Assert.True(OperabilityWindows.WellboatCanStayAlongside(5.0, Ops));
        Assert.False(OperabilityWindows.WellboatCanStayAlongside(5.01, Ops));
    }

    [Fact]
    public void Appetite_IsFullTo4m_FallsLinearlyTo07At6m_AndStaysThere()
    {
        Assert.Equal(1.0, OperabilityWindows.AppetiteFactor(0.5, Ops));
        Assert.Equal(1.0, OperabilityWindows.AppetiteFactor(4.0, Ops));
        Assert.Equal(0.85, OperabilityWindows.AppetiteFactor(5.0, Ops), 12);
        Assert.Equal(0.7, OperabilityWindows.AppetiteFactor(6.0, Ops), 12);
        Assert.Equal(0.7, OperabilityWindows.AppetiteFactor(11.0, Ops), 12);
    }

    [Fact]
    public void LongestSpellWithoutWindow_CountsDaysOnWhichNoWindowStarts()
    {
        double[] hs = { 1.0, 2.5, 2.5, 1.5, 3.0, 1.0, 1.0, 1.0, 2.6 };
        Assert.Equal(2, OperabilityWindows.LongestSpellWithoutWindow(hs, 2.0, 1));
        // A 3-day window needs three calm days in a row: only day 5 starts one.
        Assert.Equal(5, OperabilityWindows.LongestSpellWithoutWindow(hs, 2.0, 3));
        Assert.Equal(9, OperabilityWindows.LongestSpellWithoutWindow(hs, 0.5, 1));
        Assert.Equal(0, OperabilityWindows.LongestSpellWithoutWindow(Array.Empty<double>(), 2.0, 1));
        Assert.Equal(1, OperabilityWindows.FirstWindowLead(new[] { 0.9, 0.2 }, 0.8));
        Assert.Equal(3, OperabilityWindows.FirstWindowLead(new[] { 0.5, 0.79, 0.8 }, 0.8));
        Assert.Null(OperabilityWindows.FirstWindowLead(new[] { 0.5, 0.79 }, 0.8));
    }

    /// <summary>The wind probability is the sampler's own: per weather state, the share of drawn days below 25 m/s.</summary>
    [Fact]
    public void WindProbability_MatchesTheWeatherSystemsDraws()
    {
        var below = new long[4];
        var days = new long[4];
        for (int seed = 1; seed <= 20; seed++)
        {
            var weather = new WeatherSystem(new SimRng(seed).Stream(SimRngStreams.Weather));
            for (int d = 0; d < 3650; d++)
            {
                weather.AdvanceDays(1);
                int s = (int)weather.CurrentState;
                days[s]++;
                if (weather.CurrentWindSpeedMs < Ops.HelicopterMaxWindMs) below[s]++;
            }
        }
        foreach (WeatherState s in Enum.GetValues<WeatherState>())
        {
            double expected = WeatherSystem.WindSpeedBelowProbability(s, Ops.HelicopterMaxWindMs);
            double observed = (double)below[(int)s] / days[(int)s];
            _output.WriteLine($"{s}: {F(observed)} of {days[(int)s]} days below 25 m/s (expected {F(expected)})");
            Assert.InRange(observed, expected - 0.02, expected + 0.02);
        }
        Assert.Equal(0.5, WeatherSystem.WindSpeedBelowProbability(WeatherState.Severe, 25.0));
        Assert.Equal(1.0, WeatherSystem.WindSpeedBelowProbability(WeatherState.Storm, 25.0));
        Assert.Equal(0.0, WeatherSystem.WindSpeedBelowProbability(WeatherState.Calm, 0.5));
    }

    /// <summary>
    /// The odds per forecast day are honest for every vessel: for the
    /// delousing (standard and offshore wellboat), resupply (DP) and helicopter
    /// crew change, over seeds 1-10 × 20 years at off_1, the mean forecast
    /// probability at leads 2 and 6 equals the share of target days on which
    /// the operation was possible within ±0.01, and Outlook lists the same
    /// numbers lead by lead.
    /// </summary>
    [Fact]
    public void Probability_IsHonestForEachVessel()
    {
        var cases = new (OffshoreOp Op, VesselClass Vessel)[]
        {
            (OffshoreOp.Delousing, VesselClass.Standard), (OffshoreOp.Delousing, VesselClass.OffshoreWellboat),
            (OffshoreOp.Resupply, VesselClass.DpWalkToWork), (OffshoreOp.CrewChange, VesselClass.Helicopter),
        };
        int[] leads = { 2, 6 };
        var forecastSum = new double[cases.Length, leads.Length];
        var hits = new long[cases.Length, leads.Length];
        var counts = new long[cases.Length, leads.Length];
        object gate = new();
        double scale = Cal.Plots.Off1.HsScale;
        Parallel.For(1, 11, seed =>
        {
            var pending = new Dictionary<int, List<(int Case, int Lead, double P)>>();
            var localSum = new double[cases.Length, leads.Length];
            var localHits = new long[cases.Length, leads.Length];
            var localCounts = new long[cases.Length, leads.Length];
            MetoceanRuns.Run(seed, 20, afterTick: (day, sea, weather) =>
            {
                if (pending.Remove(day, out var due))
                    foreach (var (c, l, p) in due)
                    {
                        localCounts[c, l]++;
                        localSum[c, l] += p;
                        if (OperabilityWindows.IsPossible(cases[c].Op, cases[c].Vessel, sea.HsAt(scale),
                                weather.CurrentWindSpeedMs, sea.VisibilityKmToday, Ops))
                            localHits[c, l]++;
                    }
                if (day % 2 != 0) return;
                var forecast = MetoceanForecast.Issue(sea, weather, scale, Array.Empty<double>(), withQuantiles: false);
                for (int c = 0; c < cases.Length; c++)
                {
                    var outlook = OperabilityWindows.Outlook(forecast, cases[c].Op, cases[c].Vessel, Ops, Cal.Metocean);
                    for (int l = 0; l < leads.Length; l++)
                    {
                        double p = OperabilityWindows.Probability(forecast, leads[l], cases[c].Op, cases[c].Vessel, Ops, Cal.Metocean);
                        Assert.Equal(p, outlook[leads[l] - 1]);
                        if (!pending.TryGetValue(day + leads[l], out var list)) pending[day + leads[l]] = list = new();
                        list.Add((c, l, p));
                    }
                }
            });
            lock (gate)
                for (int c = 0; c < cases.Length; c++)
                    for (int l = 0; l < leads.Length; l++)
                    {
                        forecastSum[c, l] += localSum[c, l];
                        hits[c, l] += localHits[c, l];
                        counts[c, l] += localCounts[c, l];
                    }
        });
        for (int c = 0; c < cases.Length; c++)
            for (int l = 0; l < leads.Length; l++)
            {
                double forecast = forecastSum[c, l] / counts[c, l], realized = (double)hits[c, l] / counts[c, l];
                _output.WriteLine($"{cases[c].Op} by {cases[c].Vessel}, lead {leads[l]}: forecast {F(forecast)}, realized {F(realized)} ({counts[c, l]} days)");
                Assert.InRange(realized, forecast - 0.01, forecast + 0.01);
            }
    }

    /// <summary>
    /// The off_1 handling-window climatology the plot card quotes ("Ytre kyst:
    /// forventet 1–3 uker uten behandlingsvindu per vinter", spec §4) and
    /// F2-08 builds on (winters October-March, seeds 1-20 × 50 years). AOF's
    /// barge was inaccessible on 92 days in about 25 months, 62 % of them in
    /// January-March [O:R17]: about 37-40 days a winter. At off_1 the median
    /// winter has 30-45 days above the 2.0 m handling limit, its longest spell
    /// without a handling day lasts 1-2 weeks (median 5-14 days), and the
    /// offshore wellboat's 2.5 m limit cuts the rough days by at least a third.
    /// Weather alone almost never shuts the unit for 21 days (F2-08's warning
    /// therefore needs the booking and logistics delays on top).
    /// </summary>
    [Fact]
    public void Off1Winters_HandlingWindowClimatology()
    {
        double scale = Cal.Plots.Off1.HsScale;
        var spells20 = new List<int>();
        var roughDays = new List<double>();
        var roughDays25 = new List<double>();
        var spellDays = new List<double>();
        foreach (var run in MetoceanRuns.Acceptance)
            for (int y = 0; y + 1 < MetoceanRuns.AcceptanceYears; y++)
            {
                int start = y * SimCalendar.DaysPerYear + SimCalendar.MonthStartDayOfYear(9);
                int end = (y + 1) * SimCalendar.DaysPerYear + SimCalendar.MonthStartDayOfYear(3);
                var winter = Enumerable.Range(start, end - start).Select(d => run.ReferenceHs[d] * scale).ToList();
                spells20.Add(OperabilityWindows.LongestSpellWithoutWindow(winter, Ops.FishHandlingHsM, 1));
                roughDays25.Add(winter.Count(h => h > Ops.FishHandlingOffshoreWellboatHsM));
                int rough = winter.Count(h => h > Ops.FishHandlingHsM);
                int inSpells = 0, roughRun = 0;
                foreach (var h in winter.Append(0.0))
                {
                    if (h > Ops.FishHandlingHsM) { roughRun++; continue; }
                    if (roughRun >= 3) inSpells += roughRun;
                    roughRun = 0;
                }
                roughDays.Add(rough);
                spellDays.Add(inSpells);
            }
        double share20 = spells20.Count(s => s >= 21) / (double)spells20.Count;
        double medianSpell = MetoceanRuns.Quantile(spells20.Select(s => (double)s).ToList(), 0.5);
        double medianRough = MetoceanRuns.Quantile(roughDays, 0.5), medianRough25 = MetoceanRuns.Quantile(roughDays25, 0.5);
        _output.WriteLine($"{spells20.Count} winters: longest spell without a 2.0 m day median {medianSpell} d, " +
                          $"p90 {MetoceanRuns.Quantile(spells20.Select(s => (double)s).ToList(), 0.9)} d, ≥ 21 d in {F(share20)}");
        _output.WriteLine($"days above 2.0 m per winter: median {medianRough}, p10 {MetoceanRuns.Quantile(roughDays, 0.1)}, p90 {MetoceanRuns.Quantile(roughDays, 0.9)}; " +
                          $"in spells of 3+ days: median {MetoceanRuns.Quantile(spellDays, 0.5)}; above 2.5 m: median {medianRough25}");
        Assert.InRange(medianRough, 30, 45);
        Assert.InRange(medianSpell, 5, 14);
        Assert.True(medianRough25 <= medianRough * 2.0 / 3.0);
        Assert.True(share20 < 0.01);
    }
}
