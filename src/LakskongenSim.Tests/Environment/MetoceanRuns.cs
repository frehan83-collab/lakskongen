#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// Runs the standalone metocean component the way the engine will (F2-06
/// acceptance is "measured on the standalone component"): one SimRng per seed,
/// the fjord weather on its own stream advancing first, then the sea ticking
/// the same day on <see cref="OffshoreMetocean.StreamScope"/>. Day 0 is
/// 1 January; a run of Y years covers days 0 to 365·Y − 1, so every calendar
/// year is whole.
/// </summary>
internal static class MetoceanRuns
{
    /// <summary>One seed's daily record at the reference scale.</summary>
    internal sealed class SeedRun
    {
        public int Seed;
        public double[] ReferenceHs = Array.Empty<double>();
        public double[] Base = Array.Empty<double>();
        public double[] SwellEnergy = Array.Empty<double>();
        public double[] ResidualX = Array.Empty<double>();
        public byte[] Weather = Array.Empty<byte>();
        public double[] Visibility = Array.Empty<double>();
        public List<SwellEvent> Events = new();
        public int Days => ReferenceHs.Length;
    }

    /// <summary>
    /// Run one seed for <paramref name="years"/> years. <paramref name="afterTick"/>
    /// sees the sea and the weather after each day's tick (forecasts are issued there).
    /// </summary>
    public static SeedRun Run(int seed, int years, OffshoreMetoceanCalibration? calibration = null,
        Action<int, OffshoreMetocean, WeatherSystem>? afterTick = null)
    {
        var rng = new SimRng(seed);
        var weather = new WeatherSystem(rng.Stream(SimRngStreams.Weather));
        var stream = rng.Stream(OffshoreMetocean.StreamScope);
        var sea = new OffshoreMetocean();
        if (calibration != null) sea.ApplyCalibration(calibration);
        int days = years * SimCalendar.DaysPerYear;
        var run = new SeedRun
        {
            Seed = seed,
            ReferenceHs = new double[days],
            Base = new double[days],
            SwellEnergy = new double[days],
            ResidualX = new double[days],
            Weather = new byte[days],
            Visibility = new double[days],
        };
        int lastSerial = 0;
        for (int day = 0; day < days; day++)
        {
            if (day > 0) weather.AdvanceDays(1);
            sea.TickDay(day, weather.CurrentState, stream);
            run.ReferenceHs[day] = sea.ReferenceHsToday();
            run.Base[day] = sea.BaseHsToday;
            run.SwellEnergy[day] = sea.SwellHsToday * sea.SwellHsToday;
            run.ResidualX[day] = sea.ResidualX;
            run.Weather[day] = (byte)weather.CurrentState;
            run.Visibility[day] = sea.VisibilityKmToday;
            foreach (var e in sea.Events)
                if (e.Serial > lastSerial)
                {
                    run.Events.Add(e);
                    lastSerial = e.Serial;
                }
            afterTick?.Invoke(day, sea, weather);
        }
        return run;
    }

    /// <summary>Run seeds in parallel (each seed is independent and deterministic), results in seed order.</summary>
    public static SeedRun[] RunSeeds(IReadOnlyList<int> seeds, int years, OffshoreMetoceanCalibration? calibration = null)
    {
        var runs = new SeedRun[seeds.Count];
        Parallel.For(0, seeds.Count, i => runs[i] = Run(seeds[i], years, calibration));
        return runs;
    }

    /// <summary>The highest daily Hs of each calendar year (m, at <paramref name="scale"/>).</summary>
    public static List<double> AnnualMaxima(IEnumerable<SeedRun> runs, double scale)
    {
        var maxima = new List<double>();
        foreach (var run in runs)
            for (int y = 0; y < run.Days / SimCalendar.DaysPerYear; y++)
            {
                double max = 0.0;
                for (int d = y * SimCalendar.DaysPerYear; d < (y + 1) * SimCalendar.DaysPerYear; d++)
                    max = Math.Max(max, run.ReferenceHs[d]);
                maxima.Add(scale * max);
            }
        return maxima;
    }

    /// <summary>Linear-interpolation quantile (Hyndman-Fan type 7, numpy's default).</summary>
    public static double Quantile(IReadOnlyList<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        double h = (sorted.Length - 1) * p;
        int lo = (int)Math.Floor(h);
        int hi = Math.Min(sorted.Length - 1, lo + 1);
        return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
    }

    /// <summary>
    /// The empirical 100-year level: the Hs exceeded by the annual maximum in 1 %
    /// of years (the 0.99 quantile of annual maxima).
    /// </summary>
    public static double HundredYearLevel(IEnumerable<SeedRun> runs, double scale)
        => Quantile(AnnualMaxima(runs, scale), 0.99);

    /// <summary>The F2-06 acceptance sample: seeds 1-20 × 50 years at the shipped calibration, run once per test process.</summary>
    public static SeedRun[] Acceptance => LazyAcceptance.Value;

    private static readonly Lazy<SeedRun[]> LazyAcceptance =
        new(() => RunSeeds(Enumerable.Range(1, 20).ToArray(), 50));

    public const int AcceptanceYears = 50;
}

/// <summary>
/// The fit behind the shipped base levels and plot scales (F2-06): seeds
/// 1001-1100 × 50 years (disjoint from the acceptance seeds 1-20).
/// <list type="number">
/// <item>Run the sea once. The weather, the AR(1) residual and the swell do not
/// depend on the base level, so each day's (month, weather, X, swell energy) is
/// kept and the base recomputed for any level with the tick's own function.</item>
/// <item>For each month, Newton's method on μ_month until the month's mean
/// daily Hs equals the reference annual mean × the NORA10 monthly shape.</item>
/// <item>The empirical 100-year level of the fitted reference sea gives each
/// plot's scale: Hs100 / level.</item>
/// </list>
/// </summary>
internal static class OffshoreMetoceanFit
{
    public static readonly int[] FitSeeds = Enumerable.Range(1001, 100).ToArray();
    public const int FitYears = 50;

    public sealed record Result(double[] BaseMuMonth, double ReferenceHundredYearM, double Off1Scale, double Off2Scale,
        double Hav1Scale, double[] MonthlyMeanM);

    public static Result Fit(OffshoreMetoceanCalibration cal, OffshorePlotsCalibration plots)
    {
        var runs = MetoceanRuns.RunSeeds(FitSeeds, FitYears, cal);
        // Each month's days: weather, residual and swell energy (independent of μ).
        var byMonth = Enumerable.Range(0, 12).Select(_ => new List<(WeatherState W, double X, double E)>()).ToArray();
        foreach (var run in runs)
            for (int d = 0; d < run.Days; d++)
                byMonth[SimCalendar.MonthIndex(d)].Add(((WeatherState)run.Weather[d], run.ResidualX[d], run.SwellEnergy[d]));
        var mu = cal.BaseMuMonth.ToArray();
        var means = new double[12];
        for (int m = 0; m < 12; m++)
        {
            double target = cal.ReferenceMonthlyMeanHsM(m);
            for (int it = 0; it < 50; it++)
            {
                double sum = 0.0, slope = 0.0;
                var fitted = cal with { BaseMuMonth = WithMonth(cal.BaseMuMonth, m, mu[m]) };
                foreach (var (w, x, e) in byMonth[m])
                {
                    double y = OffshoreMetoceanModel.BaseLevel(fitted, m, w) + x;
                    double b = OffshoreMetoceanModel.BaseHs(fitted, y);
                    double h = OffshoreMetoceanModel.CombineHs(fitted, b, e);
                    sum += h;
                    // dH/dμ = (B/H)·dB/dy, dB/dy = B / (dT/d ln B).
                    slope += b / h * (b / MetoceanMath.UpperBoxCoxSlopePerLn(b, fitted.BaseBoxCoxLambda));
                }
                int n = byMonth[m].Count;
                double mean = sum / n;
                means[m] = mean;
                if (Math.Abs(mean / target - 1.0) < 1e-12) break;
                mu[m] += (target - mean) / (slope / n);
            }
        }
        var final = cal with { BaseMuMonth = MonthlyValues.Of<BaseMuMonthValues>(mu) };
        // Recompute every day's reference Hs at the fitted levels.
        foreach (var run in runs)
            for (int d = 0; d < run.Days; d++)
            {
                int m = SimCalendar.MonthIndex(d);
                double b = OffshoreMetoceanModel.BaseHs(final,
                    OffshoreMetoceanModel.BaseLevel(final, m, (WeatherState)run.Weather[d]) + run.ResidualX[d]);
                run.ReferenceHs[d] = OffshoreMetoceanModel.CombineHs(final, b, run.SwellEnergy[d]);
            }
        double level = MetoceanRuns.HundredYearLevel(runs, 1.0);
        return new Result(mu, level, plots.Off1.Hs100M / level, plots.Off2.Hs100M / level, plots.Hav1.Hs100M / level, means);
    }

    private static BaseMuMonthValues WithMonth(MonthlyValues v, int month, double value)
    {
        var a = v.ToArray();
        a[month] = value;
        return MonthlyValues.Of<BaseMuMonthValues>(a);
    }
}
