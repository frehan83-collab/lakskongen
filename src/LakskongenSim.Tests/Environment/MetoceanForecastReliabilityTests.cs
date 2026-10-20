using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-06 acceptance 5: the forecast is reliable. Over seeds 1-20 × 50 years
/// at off_1, every forecast of P(Hs ≤ L) for the handling (2.0 m), resupply
/// (2.5 m) and net-inspection (3.0 m) limits at leads 1, 3, 5, 7 and 10 days
/// is binned by decile of its probability; in every decile with forecasts the
/// realized share of days at or below the limit is within ±0.05 of the mean
/// forecast probability.
/// </summary>
public class MetoceanForecastReliabilityTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private static readonly int[] Leads = { 1, 3, 5, 7, 10 };
    private static readonly double[] LimitsM = { 2.0, 2.5, 3.0 };
    private readonly ITestOutputHelper _output;

    public MetoceanForecastReliabilityTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Per lead and decile: forecasts, their summed probability and how many
    /// came true; and per lead the deciles of the PIT, the forecast's
    /// P(Hs ≤ the Hs that came).
    /// </summary>
    private sealed class Tally
    {
        public readonly long[,] Count = new long[Leads.Length, 10];
        public readonly double[,] ProbabilitySum = new double[Leads.Length, 10];
        public readonly long[,] Hits = new long[Leads.Length, 10];
        public readonly long[,] Pit = new long[Leads.Length, 10];

        public void Add(Tally other)
        {
            for (int l = 0; l < Leads.Length; l++)
                for (int d = 0; d < 10; d++)
                {
                    Count[l, d] += other.Count[l, d];
                    ProbabilitySum[l, d] += other.ProbabilitySum[l, d];
                    Hits[l, d] += other.Hits[l, d];
                    Pit[l, d] += other.Pit[l, d];
                }
        }
    }

    private static Tally RunSeed(int seed)
    {
        double scale = Cal.Plots.Off1.HsScale;
        int horizon = Leads.Max();
        // Pending forecasts by target day: (lead index, limit index, probability), and the day models for the PIT.
        var pending = new Dictionary<int, List<(int Lead, int Limit, double P)>>();
        var pendingModels = new Dictionary<int, List<(int Lead, MetoceanForecast.DayModel Model)>>();
        var tally = new Tally();
        MetoceanRuns.Run(seed, MetoceanRuns.AcceptanceYears, afterTick: (day, sea, weather) =>
        {
            double hs = sea.HsAt(scale);
            if (pending.Remove(day, out var due))
                foreach (var (lead, limit, p) in due)
                {
                    int decile = Math.Min(9, (int)(p * 10.0));
                    tally.Count[lead, decile]++;
                    tally.ProbabilitySum[lead, decile] += p;
                    if (hs <= LimitsM[limit]) tally.Hits[lead, decile]++;
                }
            if (pendingModels.Remove(day, out var models))
                foreach (var (lead, model) in models)
                    tally.Pit[lead, Math.Min(9, (int)(model.Cdf(sea.ReferenceHsToday()) * 10.0))]++;
            if (day + horizon >= MetoceanRuns.AcceptanceYears * SimCalendar.DaysPerYear) return;
            for (int l = 0; l < Leads.Length; l++)
            {
                var dist = weather.StateDistribution(Leads[l]);
                var model = MetoceanForecast.BuildDay(sea, sea.Calibration(), dist, Leads[l]);
                if (!pending.TryGetValue(day + Leads[l], out var list))
                    pending[day + Leads[l]] = list = new List<(int, int, double)>();
                for (int i = 0; i < LimitsM.Length; i++)
                    list.Add((l, i, model.Cdf(LimitsM[i] / scale)));
                if (!pendingModels.TryGetValue(day + Leads[l], out var modelList))
                    pendingModels[day + Leads[l]] = modelList = new List<(int, MetoceanForecast.DayModel)>();
                modelList.Add((l, model));
            }
        });
        return tally;
    }

    /// <summary>The acceptance sample's tallies, computed once per test process for both checks.</summary>
    private static readonly Lazy<Tally> Total = new(() =>
    {
        var total = new Tally();
        var perSeed = new Tally[20];
        Parallel.For(0, 20, i => perSeed[i] = RunSeed(i + 1));
        foreach (var t in perSeed) total.Add(t);
        return total;
    });

    [Fact]
    public void Acceptance5_RealizedFrequency_IsWithinFivePointsOfTheForecast_InEveryDecile_AtLeads1To10()
    {
        var total = Total.Value;

        var report = new StringBuilder();
        var failures = new List<string>();
        for (int l = 0; l < Leads.Length; l++)
        {
            report.Append(CultureInfo.InvariantCulture, $"lead {Leads[l],2}:");
            for (int d = 0; d < 10; d++)
            {
                long n = total.Count[l, d];
                if (n == 0)
                {
                    report.Append("      -      ");
                    continue;
                }
                double forecast = total.ProbabilitySum[l, d] / n, realized = (double)total.Hits[l, d] / n;
                report.Append(CultureInfo.InvariantCulture, $" {forecast:0.000}/{realized:0.000}({n})");
                if (Math.Abs(realized - forecast) > 0.05)
                    failures.Add($"lead {Leads[l]} decile {d + 1}: forecast {forecast:0.000}, realized {realized:0.000} over {n}");
            }
            report.AppendLine();
        }
        _output.WriteLine("mean forecast / realized share (forecasts) per decile, limits 2.0, 2.5 and 3.0 m at off_1:");
        _output.WriteLine(report.ToString());
        Assert.True(failures.Count == 0, string.Join(System.Environment.NewLine, failures));
        // Every lead's forecasts spread over the deciles: the forecast is sharp, not climatology.
        for (int l = 0; l < Leads.Length; l++)
            Assert.True(Enumerable.Range(0, 10).Count(d => total.Count[l, d] >= 100) >= 8,
                $"lead {Leads[l]} uses too few deciles");
    }

    /// <summary>
    /// Stronger than acceptance 5: the whole forecast distribution is
    /// calibrated, not only three limits. At every lead the forecast's
    /// P(Hs ≤ the Hs that came) (the PIT) falls in each tenth of [0, 1] on
    /// 10 % ± 0.6 % of the 365,000 days; an over- or underconfident forecast
    /// piles the PIT at the ends or the middle.
    /// </summary>
    [Fact]
    public void TheWholeForecastDistribution_IsCalibrated_AtEveryLead()
    {
        var total = Total.Value;
        for (int l = 0; l < Leads.Length; l++)
        {
            long n = Enumerable.Range(0, 10).Sum(d => total.Pit[l, d]);
            var shares = Enumerable.Range(0, 10).Select(d => total.Pit[l, d] / (double)n).ToArray();
            _output.WriteLine($"lead {Leads[l],2}: PIT deciles " + string.Join(" ", shares.Select(v => v.ToString("0.0000", CultureInfo.InvariantCulture))) + $" ({n} days)");
            Assert.All(shares, v => Assert.InRange(v, 0.094, 0.106));
        }
    }
}
