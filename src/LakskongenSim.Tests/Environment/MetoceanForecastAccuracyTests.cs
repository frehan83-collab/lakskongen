using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// The forecast's numbers are the model's (F2-06, spec §3.3 point 6 "the exact
/// conditional distribution"): its quadrature against a high-resolution
/// integration of the same terms, its terms against futures simulated from the
/// generative model, and the known events' peak posteriors against the peaks
/// that happened.
/// </summary>
public class MetoceanForecastAccuracyTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private readonly ITestOutputHelper _output;

    public MetoceanForecastAccuracyTests(ITestOutputHelper output) => _output = output;

    private static string F(double v, string format = "0.0000") => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A sea after its tick on some day, with the weather chain's state on that day.</summary>
    internal sealed record Snapshot(int Seed, int Day, string SeaJson, WeatherState Weather, double[][] WeatherByLead)
    {
        public OffshoreMetocean Sea() => JsonSerializer.Deserialize<OffshoreMetocean>(SeaJson, SaveJson.Options)!;
    }

    /// <summary>
    /// Snapshots from seeds 31-33 over four years: every 41st day, and every
    /// day a known event's peak is still uncertain (onset ahead) on a 9th day,
    /// so winter swell, overlaps and summer calm are all covered.
    /// </summary>
    internal static List<Snapshot> Snapshots(int horizon)
    {
        var result = new List<Snapshot>();
        foreach (int seed in new[] { 31, 32, 33 })
            MetoceanRuns.Run(seed, 4, afterTick: (day, sea, weather) =>
            {
                bool pending = sea.Events.Any(e => e.OnsetDay > day);
                if (day % 41 != 0 && !(pending && day % 9 == 0)) return;
                var byLead = Enumerable.Range(1, horizon).Select(k => weather.StateDistribution(k)).ToArray();
                result.Add(new Snapshot(seed, day, JsonSerializer.Serialize(sea, SaveJson.Options), weather.CurrentState, byLead));
            });
        return result;
    }

    // ─── Quadrature against a high-resolution integration ───

    private static double RefOne(MetoceanForecast.DayModel m, double c, in MetoceanForecast.SwellComponent comp, int panels)
    {
        double hi = Math.Min(9.0, comp.StandardFor(Math.Sqrt(c)));
        const double lo = -9.0;
        if (hi <= lo) return 0.0;
        double width = (hi - lo) / panels, sum = 0.0;
        for (int i = 0; i < panels; i++)
        {
            double mid = lo + (i + 0.5) * width, half = 0.5 * width;
            for (int j = 0; j < 8; j++)
            {
                double zeta = mid + half * MetoceanMath.GaussLegendreNodes[j];
                double s = comp.ValueAtStandard(zeta);
                double rest = c - s * s;
                if (rest <= 0.0) continue;
                sum += half * MetoceanMath.GaussLegendreWeights[j] * MetoceanMath.NormalPdf(zeta) * m.BaseCdf(Math.Sqrt(rest));
            }
        }
        return sum;
    }

    private static double RefTwo(MetoceanForecast.DayModel m, double c, MetoceanForecast.SwellComponent inner,
        MetoceanForecast.SwellComponent outer)
    {
        double hi = Math.Min(9.0, outer.StandardFor(Math.Sqrt(c)));
        const double lo = -9.0;
        const int panels = 40;
        if (hi <= lo) return 0.0;
        double width = (hi - lo) / panels, sum = 0.0;
        for (int i = 0; i < panels; i++)
        {
            double mid = lo + (i + 0.5) * width, half = 0.5 * width;
            for (int j = 0; j < 8; j++)
            {
                double zeta = mid + half * MetoceanMath.GaussLegendreNodes[j];
                double s = outer.ValueAtStandard(zeta);
                double rest = c - s * s;
                if (rest <= 0.0) continue;
                sum += half * MetoceanMath.GaussLegendreWeights[j] * MetoceanMath.NormalPdf(zeta) * RefOne(m, rest, inner, 60);
            }
        }
        return sum;
    }

    /// <summary>Three or more swells: a 2^15-point Halton rule (shifted off the forecast's own points).</summary>
    private static double RefMany(MetoceanForecast.DayModel m, double c, MetoceanForecast.SwellComponent[] comps)
    {
        int[] bases = { 2, 3, 5, 7, 11, 13 };
        const int n = 1 << 15;
        double sum = 0.0;
        for (int j = 0; j < n; j++)
        {
            double rest = c;
            for (int i = 0; i < comps.Length && rest > 0.0; i++)
            {
                double u = (RadicalInverse(j + 7919, bases[i]) + 0.5 / n) % 1.0;
                double s = comps[i].ValueAtStandard(MetoceanMath.NormalQuantile(Math.Clamp(u, 1e-15, 1.0 - 1e-15)));
                rest -= s * s;
            }
            if (rest > 0.0) sum += m.BaseCdf(Math.Sqrt(rest));
        }
        return sum / n;
    }

    private static double RadicalInverse(int index, int b)
    {
        double inv = 1.0 / b, f = inv, r = 0.0;
        while (index > 0)
        {
            r += f * (index % b);
            index /= b;
            f *= inv;
        }
        return r;
    }

    internal static double Reference1(MetoceanForecast.DayModel m, double c, MetoceanForecast.SwellComponent[] comps)
        => comps.Length switch
        {
            0 => m.BaseCdf(Math.Sqrt(c)),
            1 => RefOne(m, c, comps[0], 200),
            2 => RefTwo(m, c, comps[0], comps[1]),
            _ => RefMany(m, c, comps),
        };

    internal static double Reference(MetoceanForecast.DayModel m, double h)
    {
        double c = h * h - m.KnownEnergy;
        if (c <= 0.0) return 0.0;
        double total = 0.0;
        foreach (var (p, comps) in m.AllTerms)
        {
            total += p * comps.Length switch
            {
                0 => m.BaseCdf(Math.Sqrt(c)),
                1 => RefOne(m, c, comps[0], 200),
                2 => RefTwo(m, c, comps[0], comps[1]),
                _ => RefMany(m, c, comps),
            };
        }
        return total;
    }

    /// <summary>
    /// At every third snapshot, leads 1, 3, 5, 7 and 10, the 2.0 and 3.0 m
    /// limits at off_1 and the forecast's own 10/50/90 % quantiles, P(Hs ≤ h)
    /// is within 5e-4 of a 1,600-node (one swell), 320 × 480-node (two) or
    /// 32,768-point (three or more) integration of the same terms.
    /// </summary>
    [Fact]
    public void Quadrature_MatchesAHighResolutionIntegration()
    {
        var snapshots = Snapshots(Cal.Metocean.ForecastHorizonDays).Where((_, i) => i % 3 == 0).ToList();
        double worst = 0.0;
        string worstAt = "";
        int checks = 0, withTwo = 0, withTail = 0;
        object gate = new();
        Parallel.ForEach(snapshots, snap =>
        {
            var sea = snap.Sea();
            foreach (int k in new[] { 1, 3, 5, 7, 10 })
            {
                var m = MetoceanForecast.BuildDay(sea, sea.Calibration(), snap.WeatherByLead[k - 1], k);
                var hs = new List<double> { 2.0, 3.0 }.Select(l => l / Cal.Plots.Off1.HsScale).ToList();
                hs.AddRange(new[] { 0.1, 0.5, 0.9 }.Select(m.Quantile));
                bool two = m.AllTerms.Any(t => t.Uncertain.Length == 2), tail = m.TailMass > 0.0;
                foreach (double h in hs)
                {
                    double fast = m.Cdf(h), slow = Reference(m, h);
                    double error = Math.Abs(fast - slow);
                    lock (gate)
                    {
                        checks++;
                        if (error > worst)
                        {
                            worst = error;
                            worstAt = $"seed {snap.Seed} day {snap.Day} lead {k} h {F(h, "0.000")}: {F(fast, "0.000000")} vs {F(slow, "0.000000")}";
                        }
                    }
                }
                lock (gate)
                {
                    if (two) withTwo++;
                    if (tail) withTail++;
                }
            }
        });
        _output.WriteLine($"{checks} probabilities over {snapshots.Count} snapshots, {withTwo} days with two uncertain swells, {withTail} with a tail; worst error {F(worst, "0.0e0")} at {worstAt}");
        Assert.True(withTwo >= 50 && withTail >= 50, $"too few hard cases: {withTwo} two-swell days, {withTail} tail days");
        Assert.True(worst <= 5e-4, worstAt);
    }

    // ─── Terms against simulated futures ───

    /// <summary>
    /// The generative model run forward from a saved sea: the weather chain
    /// day by day through its transition rows, the AR(1) residual day by day,
    /// new events created on each day at the month's rate with uniform onset
    /// and plateau, known events with their true timing and a peak drawn from
    /// the posterior given today's estimate (the true peak once it has begun).
    /// Returns the reference sea on the target day.
    /// </summary>
    private static double SimulateFuture(OffshoreMetocean sea, WeatherState weatherToday, int lead, SimRngStream rng)
    {
        var cal = sea.Calibration();
        int issue = sea.LastTickDay, target = issue + lead;
        var state = weatherToday;
        double x = sea.ResidualX;
        var energy = 0.0;
        for (int d = issue + 1; d <= target; d++)
        {
            var m = WeatherSystem.TransitionMatrixFor(d, 1.0, WeatherState.Severe);
            double roll = rng.NextDouble(), cumulative = 0.0;
            var next = WeatherState.Severe;
            for (int to = 0; to < 4; to++)
            {
                cumulative += m[(int)state, to];
                if (roll < cumulative) { next = (WeatherState)to; break; }
            }
            state = next;
            x = cal.ArPhi * x + cal.ArSigma * MetoceanMath.UniformToNormal(rng.NextDouble());
            int month = SimCalendar.MonthIndex(d);
            if (rng.NextDouble() < cal.SwellRatePerDay[month])
            {
                int onset = d + OffshoreMetoceanModel.OnsetOffsetDays(cal, rng.NextDouble());
                int plateau = OffshoreMetoceanModel.PlateauDays(cal, rng.NextDouble());
                double peak = OffshoreMetoceanModel.SwellPeak(cal, cal.SwellMedianM(month), MetoceanMath.UniformToNormal(rng.NextDouble()));
                var e = new SwellEvent(0, d, onset, plateau, peak, 10.0, 0.0);
                double s = e.ShapeOn(target, cal.SwellRampFraction) * peak;
                energy += s * s;
            }
        }
        foreach (var e in sea.Events)
        {
            double shape = e.ShapeOn(target, cal.SwellRampFraction);
            if (shape <= 0.0) continue;
            double peak = e.PeakHsM;
            if (e.OnsetDay > issue)
            {
                double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
                double tau = Math.Min(1.0, (double)(e.OnsetDay - issue) / cal.ForecastFullErrorLeadDays);
                double estimate = OffshoreMetoceanModel.SwellLatent(cal, median, e.PeakHsM) + tau * e.EstimateLogError;
                double z = estimate / (1.0 + tau * tau) + tau / Math.Sqrt(1.0 + tau * tau) * MetoceanMath.UniformToNormal(rng.NextDouble());
                peak = OffshoreMetoceanModel.SwellPeak(cal, median, z);
            }
            energy += shape * shape * peak * peak;
        }
        double b = OffshoreMetoceanModel.BaseHs(cal, OffshoreMetoceanModel.BaseLevel(cal, SimCalendar.MonthIndex(target), state) + x);
        return OffshoreMetoceanModel.CombineHs(cal, b, energy);
    }

    /// <summary>
    /// For 24 snapshots (half with a known event still to arrive) at leads 1,
    /// 3, 6 and 10, P(Hs ≤ h) from the forecast equals the share of 60,000
    /// simulated futures within 4.5 standard errors plus the quadrature's 5e-4,
    /// at the operation limits and the forecast's own quartiles.
    /// </summary>
    [Fact]
    public void Forecast_MatchesSimulatedFutures()
    {
        var all = Snapshots(Cal.Metocean.ForecastHorizonDays);
        var pending = all.Where(s => s.Sea().Events.Any(e => e.OnsetDay > s.Day)).Take(12);
        var plain = all.Where(s => s.Day % 41 == 0).Take(12);
        var chosen = pending.Concat(plain).ToList();
        Assert.Equal(24, chosen.Count);
        const int samples = 60_000;
        double worstZ = 0.0;
        string worstAt = "";
        object gate = new();
        Parallel.ForEach(chosen, snap =>
        {
            var sea = snap.Sea();
            var rng = new SimRng(1000 + snap.Seed * 10_000 + snap.Day).Stream("metocean_mc_reference");
            foreach (int k in new[] { 1, 3, 6, 10 })
            {
                var m = MetoceanForecast.BuildDay(sea, sea.Calibration(), snap.WeatherByLead[k - 1], k);
                var futures = new double[samples];
                for (int i = 0; i < samples; i++)
                    futures[i] = SimulateFuture(sea, snap.Weather, k, rng);
                var hs = new List<double> { 2.0, 3.0 }.Select(l => l / Cal.Plots.Off1.HsScale).ToList();
                hs.AddRange(new[] { 0.25, 0.5, 0.75 }.Select(m.Quantile));
                foreach (double h in hs)
                {
                    double p = m.Cdf(h);
                    double share = futures.Count(f => f <= h) / (double)samples;
                    double se = Math.Sqrt(Math.Max(p * (1 - p), 1e-6) / samples);
                    double z = (Math.Abs(share - p) - 5e-4) / se;
                    lock (gate)
                        if (z > worstZ)
                        {
                            worstZ = z;
                            worstAt = $"seed {snap.Seed} day {snap.Day} lead {k} h {F(h, "0.000")}: forecast {F(p)}, simulated {F(share)}";
                        }
                }
            }
        });
        _output.WriteLine($"worst deviation {F(worstZ, "0.00")} standard errors at {worstAt}");
        Assert.True(worstZ <= 4.5, worstAt);
    }

    // ─── Known events' peaks ───

    /// <summary>
    /// The posterior of a known event's peak is calibrated: over every event of
    /// seeds 1-20 × 50 years, the probability the forecast gave to a peak at or
    /// below the true one (the PIT) is uniform at each lead 1-4 days before
    /// onset (each decile holds 10 % ± 1.2 %), and its 10-90 % band holds the
    /// true peak 80 % ± 2 % of the time.
    /// </summary>
    [Fact]
    public void KnownEventPeakPosterior_IsCalibratedAtEveryLead()
    {
        var events = MetoceanRuns.Acceptance.SelectMany(r => r.Events).ToList();
        var cal = Cal.Metocean;
        Assert.True(events.Count > 20_000);
        for (int lead = 1; lead <= 4; lead++)
        {
            var deciles = new int[10];
            int inBand = 0;
            foreach (var e in events)
            {
                var (mean, sd) = MetoceanForecast.PeakPosterior(cal, e, e.OnsetDay - lead);
                double zTrue = OffshoreMetoceanModel.SwellLatent(cal, cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay)), e.PeakHsM);
                double pit = MetoceanMath.NormalCdf((zTrue - mean) / sd);
                deciles[Math.Min(9, (int)(pit * 10))]++;
                if (pit >= 0.1 && pit <= 0.9) inBand++;
            }
            string shares = string.Join(" ", deciles.Select(d => F(d / (double)events.Count, "0.000")));
            _output.WriteLine($"lead {lead}: PIT deciles {shares}; 10-90 % band holds {F(inBand / (double)events.Count, "0.000")}");
            Assert.All(deciles, d => Assert.InRange(d / (double)events.Count, 0.088, 0.112));
            Assert.InRange(inBand / (double)events.Count, 0.78, 0.82);
        }
    }

    /// <summary>The posterior narrows as the onset nears and is exact from the onset day.</summary>
    [Fact]
    public void KnownEventPeakPosterior_NarrowsTowardTheOnset()
    {
        var cal = Cal.Metocean;
        var e = new SwellEvent(1, 100, 106, 2, 2.6, 11.0, 1.3);
        double previous = double.PositiveInfinity;
        for (int day = 100; day <= 106; day++)
        {
            var (_, sd) = MetoceanForecast.PeakPosterior(cal, e, day);
            Assert.True(sd <= previous);
            previous = sd;
        }
        var (meanAtOnset, sdAtOnset) = MetoceanForecast.PeakPosterior(cal, e, 106);
        Assert.Equal(0.0, sdAtOnset);
        Assert.Equal(e.PeakHsM, OffshoreMetoceanModel.SwellPeak(cal, cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay)), meanAtOnset), 12);
        Assert.Equal(1.0 / Math.Sqrt(2.0), MetoceanForecast.PeakPosterior(cal, e, 100).Sd, 12);
    }
}
