using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using StateHashProbe;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-06 acceptance (docs/FARM_TYPES_SPEC.md §7 F2-06 row), measured on the
/// standalone component over seeds 1-20 × 50 years at the shipped calibration
/// (<see cref="MetoceanRuns.Acceptance"/>):
/// 1. off_1 annual mean Hs 0.8-1.1 m and median annual maximum 3.5-4.8 m;
/// 2. off_1 days with Hs above 2.0 m: 8-14 % of the year, 20-35 % in January-March;
/// 3. each plot's empirical 100-year level within ±10 % of its Hs100;
/// 4. every swell event peaking at ≥ 3 m at off_1 is in the forecast ≥ 4 days before onset;
/// 5. reliability at leads 1, 3, 5, 7, 10 (<see cref="MetoceanForecastReliabilityTests"/>);
/// 6. forecast polls never move a stream; 7. exactly 8 uniforms a day;
/// 8. the fjord weather series is unchanged (<see cref="WeatherSeriesFingerprintTests"/>);
/// 9. a save in the middle of an event resumes bit-identically.
/// </summary>
public class OffshoreMetoceanTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private readonly ITestOutputHelper _output;

    public OffshoreMetoceanTests(ITestOutputHelper output) => _output = output;

    private static double Off1 => Cal.Plots.Off1.HsScale;

    private static string F(double v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    // ─── 1 and 2: the off_1 climate ───

    [Fact]
    public void Acceptance1_Off1_AnnualMeanAndMedianAnnualMaximum()
    {
        var runs = MetoceanRuns.Acceptance;
        double mean = runs.SelectMany(r => r.ReferenceHs).Average() * Off1;
        double medianMax = MetoceanRuns.Quantile(MetoceanRuns.AnnualMaxima(runs, Off1), 0.5);
        _output.WriteLine($"off_1 annual mean {F(mean)} m, median annual maximum {F(medianMax)} m");
        Assert.InRange(mean, 0.8, 1.1);
        Assert.InRange(medianMax, 3.5, 4.8);
    }

    [Fact]
    public void Acceptance2_Off1_ShareOfDaysAbove2m_PerYearAndInJanuaryToMarch()
    {
        var runs = MetoceanRuns.Acceptance;
        long days = 0, above = 0, q1Days = 0, q1Above = 0;
        foreach (var run in runs)
            for (int d = 0; d < run.Days; d++)
            {
                bool rough = run.ReferenceHs[d] * Off1 > 2.0;
                days++;
                if (rough) above++;
                if (SimCalendar.MonthIndex(d) <= 2)
                {
                    q1Days++;
                    if (rough) q1Above++;
                }
            }
        double year = (double)above / days, q1 = (double)q1Above / q1Days;
        _output.WriteLine($"off_1 days above 2.0 m: {F(year)} of the year, {F(q1)} of January-March");
        Assert.InRange(year, 0.08, 0.14);
        Assert.InRange(q1, 0.20, 0.35);
    }

    // ─── 3: the 100-year level per plot ───

    [Theory]
    [InlineData("off_1")]
    [InlineData("off_2")]
    [InlineData("hav_1")]
    public void Acceptance3_EmpiricalHundredYearLevel_IsWithinTenPercentOfHs100(string plotId)
    {
        var plot = Cal.Plots.For(plotId);
        double level = MetoceanRuns.HundredYearLevel(MetoceanRuns.Acceptance, plot.HsScale);
        _output.WriteLine($"{plotId}: empirical 100-year level {F(level)} m against Hs100 {F(plot.Hs100M, "0.0")} m " +
                          $"({F(level / plot.Hs100M - 1.0, "+0.0%;-0.0%")})");
        Assert.InRange(level, 0.9 * plot.Hs100M, 1.1 * plot.Hs100M);
    }

    // ─── 4: big swell is forecast at least four days ahead ───

    /// <summary>
    /// Every event that peaks at 3 m or more at off_1 is in the forecast's
    /// known swell, with its onset and plateau, on every day from four days
    /// before its onset (and from its creation, which is earlier still), and
    /// it raises the forecast: the p90 Hs on its first plateau day, forecast four
    /// days before onset, is higher than the same forecast without the event.
    /// </summary>
    [Fact]
    public void Acceptance4_EverySwellEventPeakingAtThreeMetresOrMore_IsForecastFourDaysBeforeOnset()
    {
        int checkedEvents = 0;
        int lateEvents = 0;
        object gate = new();
        Parallel.For(1, 21, seed =>
        {
            int local = 0;
            var big = new Dictionary<int, SwellEvent>();
            var seenFrom = new Dictionary<int, int>();
            MetoceanRuns.Run(seed, MetoceanRuns.AcceptanceYears, afterTick: (day, sea, weather) =>
            {
                foreach (var e in sea.Events)
                {
                    if (e.PeakHsM * Off1 < 3.0) continue;
                    if (!big.ContainsKey(e.Serial))
                    {
                        big[e.Serial] = e;
                        Assert.True(e.OnsetDay - e.CreatedDay >= 4, $"seed {seed} event {e.Serial} created {e.OnsetDay - e.CreatedDay} days before onset");
                    }
                }
                foreach (var e in big.Values)
                {
                    if (day < e.OnsetDay - 4 || day > e.OnsetDay) continue;
                    var forecast = MetoceanForecast.Issue(sea, weather, Off1, Array.Empty<double>(), withQuantiles: false);
                    var listed = forecast.KnownSwell.SingleOrDefault(o => o.Serial == e.Serial);
                    Assert.True(listed != null, $"seed {seed}: event {e.Serial} missing from the forecast on day {day} (onset {e.OnsetDay})");
                    Assert.Equal(e.OnsetDay, listed!.OnsetDay);
                    Assert.Equal(e.DurationDays, listed.DurationDays);
                    if (!seenFrom.ContainsKey(e.Serial)) seenFrom[e.Serial] = day;
                    if (day == e.OnsetDay - 4)
                    {
                        int plateauLead = e.OnsetDay + 1 - day;
                        double with = forecast.Quantile(plateauLead, 0.9);
                        var without = JsonSerializer.Deserialize<OffshoreMetocean>(JsonSerializer.Serialize(sea, SaveJson.Options), SaveJson.Options)!;
                        without.Events.RemoveAll(x => x.Serial == e.Serial);
                        double p90Without = MetoceanForecast.Issue(without, weather, Off1, Array.Empty<double>(), withQuantiles: false)
                            .Quantile(plateauLead, 0.9);
                        Assert.True(with > p90Without + 0.05,
                            $"seed {seed} event {e.Serial}: p90 {with:0.000} m with it, {p90Without:0.000} m without");
                        local++;
                    }
                }
            });
            foreach (var e in big.Values)
                if (!seenFrom.TryGetValue(e.Serial, out int first) || first > e.OnsetDay - 4)
                    if (e.OnsetDay - 4 < MetoceanRuns.AcceptanceYears * SimCalendar.DaysPerYear) lock (gate) lateEvents++;
            lock (gate) checkedEvents += local;
        });
        _output.WriteLine($"{checkedEvents} events peaking at ≥ 3 m at off_1 checked four days before onset");
        Assert.Equal(0, lateEvents);
        Assert.True(checkedEvents >= 1000, $"only {checkedEvents} events of ≥ 3 m in 1,000 years");
    }

    // ─── 6: forecast polls never move a stream ───

    [Fact]
    public void Acceptance6_AThousandForecastPolls_LeaveEveryStreamPositionAndTheSeaUnchanged()
    {
        var quietRng = new SimRng(5);
        var polledRng = new SimRng(5);
        var quietWeather = new WeatherSystem(quietRng.Stream(SimRngStreams.Weather));
        var polledWeather = new WeatherSystem(polledRng.Stream(SimRngStreams.Weather));
        var quiet = new OffshoreMetocean();
        var polled = new OffshoreMetocean();
        var quietStream = quietRng.Stream(OffshoreMetocean.StreamScope);
        var polledStream = polledRng.Stream(OffshoreMetocean.StreamScope);
        double[] limits = { 2.0, 2.5, 3.0, 5.0 };
        int polls = 0;
        for (int day = 0; day < 1000; day++)
        {
            if (day > 0)
            {
                quietWeather.AdvanceDays(1);
                polledWeather.AdvanceDays(1);
            }
            quiet.TickDay(day, quietWeather.CurrentState, quietStream);
            polled.TickDay(day, polledWeather.CurrentState, polledStream);
            var before = polledRng.CaptureStates();
            string seaBefore = JsonSerializer.Serialize(polled, SaveJson.Options);
            var forecast = MetoceanForecast.Issue(polled, polledWeather, Off1, limits, withQuantiles: day % 10 == 0);
            OperabilityWindows.Outlook(forecast, OffshoreOp.CrewChange, VesselClass.Helicopter, Cal.Operability, Cal.Metocean);
            polls++;
            Assert.Equal(before, polledRng.CaptureStates());
            Assert.Equal(seaBefore, JsonSerializer.Serialize(polled, SaveJson.Options));
            Assert.Equal(BitConverter.DoubleToInt64Bits(quiet.ReferenceHsToday()), BitConverter.DoubleToInt64Bits(polled.ReferenceHsToday()));
        }
        Assert.Equal(1000, polls);
        Assert.Equal(quietRng.CaptureStates(), polledRng.CaptureStates());
        Assert.Equal(JsonSerializer.Serialize(quiet, SaveJson.Options), JsonSerializer.Serialize(polled, SaveJson.Options));
    }

    // ─── 7: eight uniforms a day ───

    [Fact]
    public void Acceptance7_ExactlyEightUniformsAreDrawnEveryDay_AndNoneFromTheWeather()
    {
        long days = 0;
        object gate = new();
        Parallel.For(1, 21, seed =>
        {
            var rng = new SimRng(seed);
            var weather = new WeatherSystem(rng.Stream(SimRngStreams.Weather));
            var weatherStream = rng.Stream(SimRngStreams.Weather);
            var stream = rng.Stream(OffshoreMetocean.StreamScope);
            var sea = new OffshoreMetocean();
            long local = 0;
            for (int day = 0; day < MetoceanRuns.AcceptanceYears * SimCalendar.DaysPerYear; day++)
            {
                if (day > 0) weather.AdvanceDays(1);
                ulong before = stream.ExportState(), weatherBefore = weatherStream.ExportState();
                sea.TickDay(day, weather.CurrentState, stream);
                Assert.Equal(8UL, GoldenTraceState.DrawsBetween(before, stream.ExportState()));
                Assert.Equal(weatherBefore, weatherStream.ExportState());
                local++;
            }
            lock (gate) days += local;
        });
        Assert.Equal(20L * MetoceanRuns.AcceptanceYears * SimCalendar.DaysPerYear, days);
        Assert.Equal(8, OffshoreMetocean.UniformsPerDay);
    }

    /// <summary>Acceptance 8 alongside the sea: ticking the sea never changes the fjord weather series.</summary>
    [Fact]
    public void Acceptance8_TheSeaTickingBesideTheWeather_LeavesTheWeatherSeriesUnchanged()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var alone = new WeatherSystem(new SimRng(seed).Stream(SimRngStreams.Weather));
            var rng = new SimRng(seed);
            var beside = new WeatherSystem(rng.Stream(SimRngStreams.Weather));
            var sea = new OffshoreMetocean();
            var stream = rng.Stream(OffshoreMetocean.StreamScope);
            for (int day = 0; day < 3650; day++)
            {
                if (day > 0)
                {
                    alone.AdvanceDays(1);
                    beside.AdvanceDays(1);
                }
                sea.TickDay(day, beside.CurrentState, stream);
                Assert.Equal(alone.CurrentState, beside.CurrentState);
                Assert.Equal(BitConverter.DoubleToInt64Bits(alone.CurrentTemperatureC), BitConverter.DoubleToInt64Bits(beside.CurrentTemperatureC));
                Assert.Equal(BitConverter.DoubleToInt64Bits(alone.CurrentWindSpeedMs), BitConverter.DoubleToInt64Bits(beside.CurrentWindSpeedMs));
            }
        }
    }

    // ─── 9: save in the middle of an event ───

    /// <summary>
    /// Save on a plateau day of one event while another is known and not yet
    /// arrived; the loaded sea (state JSON through the game's save options, the
    /// stream position through SimRng's capture) continues 500 days bit for bit,
    /// and its forecast equals the original's bit for bit.
    /// </summary>
    [Fact]
    public void Acceptance9_SaveMidEvent_ResumesBitIdentically()
    {
        var rng = new SimRng(9);
        var weather = new WeatherSystem(rng.Stream(SimRngStreams.Weather));
        var stream = rng.Stream(OffshoreMetocean.StreamScope);
        var sea = new OffshoreMetocean();
        int day = 0;
        var states = new List<WeatherState>();
        for (; ; day++)
        {
            if (day > 0) weather.AdvanceDays(1);
            states.Add(weather.CurrentState);
            sea.TickDay(day, weather.CurrentState, stream);
            bool onPlateau = sea.Events.Any(e => day > e.OnsetDay && day < e.LastActiveDay());
            bool pending = sea.Events.Any(e => e.OnsetDay > day);
            if (onPlateau && pending && day > 30) break;
            Assert.True(day < 5000, "no mid-event day found");
        }

        string saved = JsonSerializer.Serialize(sea, SaveJson.Options);
        var savedStreams = rng.CaptureStates();
        var restoredRng = new SimRng(9);
        restoredRng.ImportStates(savedStreams);
        var restored = JsonSerializer.Deserialize<OffshoreMetocean>(saved, SaveJson.Options)!;
        Assert.Equal(saved, JsonSerializer.Serialize(restored, SaveJson.Options));

        var original = MetoceanForecast.Issue(sea, weather, Off1, new[] { 2.0, 3.0 });
        var reloaded = MetoceanForecast.Issue(restored, weather, Off1, new[] { 2.0, 3.0 });
        for (int k = 0; k < original.Days.Count; k++)
        {
            Assert.Equal(original.Days[k].ProbabilityAtOrBelow.Select(BitConverter.DoubleToInt64Bits),
                reloaded.Days[k].ProbabilityAtOrBelow.Select(BitConverter.DoubleToInt64Bits));
            Assert.Equal(BitConverter.DoubleToInt64Bits(original.Days[k].HsP90M), BitConverter.DoubleToInt64Bits(reloaded.Days[k].HsP90M));
        }

        var restoredStream = restoredRng.Stream(OffshoreMetocean.StreamScope);
        for (int d = day + 1; d <= day + 500; d++)
        {
            weather.AdvanceDays(1);
            sea.TickDay(d, weather.CurrentState, stream);
            restored.TickDay(d, weather.CurrentState, restoredStream);
            Assert.Equal(BitConverter.DoubleToInt64Bits(sea.ReferenceHsToday()), BitConverter.DoubleToInt64Bits(restored.ReferenceHsToday()));
            Assert.Equal(BitConverter.DoubleToInt64Bits(sea.VisibilityKmToday), BitConverter.DoubleToInt64Bits(restored.VisibilityKmToday));
        }
        Assert.Equal(JsonSerializer.Serialize(sea, SaveJson.Options), JsonSerializer.Serialize(restored, SaveJson.Options));
        Assert.Equal(stream.ExportState(), restoredStream.ExportState());
    }

    // ─── The model's parts ───

    [Fact]
    public void TickDay_RefusesADayThatIsNotLater()
    {
        var sea = new OffshoreMetocean();
        var stream = new SimRng(1).Stream(OffshoreMetocean.StreamScope);
        sea.TickDay(3, WeatherState.Calm, stream);
        Assert.Throws<InvalidOperationException>(() => sea.TickDay(3, WeatherState.Calm, stream));
        Assert.Throws<InvalidOperationException>(() => sea.TickDay(2, WeatherState.Calm, stream));
    }

    /// <summary>Events are created 4-7 days ahead, plateau 1-4 days, and leave the queue after their ramp-down day.</summary>
    [Fact]
    public void SwellEvents_HaveTheirLeadAndPlateauRanges_AndLeaveAfterRampingDown()
    {
        var run = MetoceanRuns.Acceptance[0];
        Assert.NotEmpty(run.Events);
        foreach (var e in run.Events)
        {
            Assert.InRange(e.OnsetDay - e.CreatedDay, Cal.Metocean.SwellLeadDaysMin, Cal.Metocean.SwellLeadDaysMax);
            Assert.InRange(e.DurationDays, Cal.Metocean.SwellDurationDaysMin, Cal.Metocean.SwellDurationDaysMax);
            Assert.InRange(e.PeakTpS, Cal.Metocean.SwellTpMinS, Cal.Metocean.SwellTpMaxS);
            Assert.Equal(Cal.Metocean.SwellRampFraction, e.ShapeOn(e.OnsetDay, Cal.Metocean.SwellRampFraction));
            Assert.Equal(1.0, e.ShapeOn(e.OnsetDay + 1, Cal.Metocean.SwellRampFraction));
            Assert.Equal(Cal.Metocean.SwellRampFraction, e.ShapeOn(e.LastActiveDay(), Cal.Metocean.SwellRampFraction));
            Assert.Equal(0.0, e.ShapeOn(e.LastActiveDay() + 1, Cal.Metocean.SwellRampFraction));
        }
        // Every lead and plateau length occurs, about equally often.
        var leads = run.Events.GroupBy(e => e.OnsetDay - e.CreatedDay).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(4, leads.Count);
        Assert.All(leads.Values, c => Assert.InRange(c, run.Events.Count / 4 * 0.8, run.Events.Count / 4 * 1.2));

        var sea = new OffshoreMetocean();
        var stream = new SimRng(3).Stream(OffshoreMetocean.StreamScope);
        for (int day = 0; day < 2000; day++)
        {
            sea.TickDay(day, WeatherState.Storm, stream);
            Assert.All(sea.Events, e => Assert.True(e.LastActiveDay() >= day));
        }
    }

    /// <summary>
    /// The swell arrives at the calibrated monthly rates (about 22 events a
    /// year) and is seasonal: January peaks are larger than July's by the NORA10
    /// monthly 1-year ratio.
    /// </summary>
    [Fact]
    public void SwellEvents_ArriveAtTheMonthlyRates_WithSeasonalPeaks()
    {
        var events = MetoceanRuns.Acceptance.SelectMany(r => r.Events).ToList();
        int years = 20 * MetoceanRuns.AcceptanceYears;
        double expectedPerYear = Enumerable.Range(0, 12).Sum(m => Cal.Metocean.SwellRatePerDay[m] * SimCalendar.DaysInMonth(m));
        double perYear = (double)events.Count / years;
        _output.WriteLine($"{F(perYear, "0.00")} events a year (expected {F(expectedPerYear, "0.00")})");
        Assert.InRange(perYear, expectedPerYear * 0.97, expectedPerYear * 1.03);
        double MedianPeak(int month) => MetoceanRuns.Quantile(events
            .Where(e => SimCalendar.MonthIndex(e.CreatedDay) == month).Select(e => e.PeakHsM).ToList(), 0.5);
        Assert.InRange(MedianPeak(0), Cal.Metocean.SwellMedianM(0) * 0.97, Cal.Metocean.SwellMedianM(0) * 1.03);
        Assert.InRange(MedianPeak(6), Cal.Metocean.SwellMedianM(6) * 0.95, Cal.Metocean.SwellMedianM(6) * 1.05);
        Assert.True(MedianPeak(0) > 2.0 * MedianPeak(6));
    }

    /// <summary>The base sea follows the weather chain: a stormier state never brings a calmer base on the same residual.</summary>
    [Fact]
    public void BaseSea_RisesWithTheWeatherState_AndKeepsItsFloor()
    {
        var m = Cal.Metocean;
        for (int month = 0; month < 12; month++)
        {
            double previous = 0.0;
            foreach (WeatherState w in Enum.GetValues<WeatherState>())
            {
                double b = OffshoreMetoceanModel.BaseHs(m, OffshoreMetoceanModel.BaseLevel(m, month, w));
                Assert.True(b > previous);
                previous = b;
            }
            Assert.Equal(m.BaseFloorM, OffshoreMetoceanModel.BaseHs(m, OffshoreMetoceanModel.BaseLevel(m, month, WeatherState.Calm) - 10.0));
        }
        var runs = MetoceanRuns.Acceptance;
        double MeanBase(WeatherState w) => runs.SelectMany(r => Enumerable.Range(0, r.Days).Where(d => r.Weather[d] == (byte)w).Select(d => r.Base[d])).Average();
        Assert.True(MeanBase(WeatherState.Calm) < MeanBase(WeatherState.Moderate));
        Assert.True(MeanBase(WeatherState.Moderate) < MeanBase(WeatherState.Storm));
        Assert.True(MeanBase(WeatherState.Storm) < MeanBase(WeatherState.Severe));
        double floorShare = runs.SelectMany(r => r.Base).Count(b => b <= m.BaseFloorM) / (double)runs.Sum(r => r.Days);
        _output.WriteLine($"base at its floor on {floorShare:0.000000} of days");
        Assert.True(floorShare < 0.001);
    }

    /// <summary>Visibility follows the monthly low-visibility shares (it gates the helicopter).</summary>
    [Fact]
    public void Visibility_FollowsTheMonthlyLowVisibilityShares()
    {
        var runs = MetoceanRuns.Acceptance;
        for (int month = 0; month < 12; month++)
        {
            var vis = runs.SelectMany(r => Enumerable.Range(0, r.Days).Where(d => SimCalendar.MonthIndex(d) == month).Select(d => r.Visibility[d])).ToList();
            double low = vis.Count(v => v < Cal.Metocean.LowVisibilityKm) / (double)vis.Count;
            Assert.InRange(low, Cal.Metocean.LowVisibilityDayShare[month] - 0.006, Cal.Metocean.LowVisibilityDayShare[month] + 0.006);
            Assert.All(vis, v => Assert.InRange(v, Cal.Metocean.LowVisibilityMinKm, Cal.Metocean.ClearVisibilityMaxKm));
        }
    }

    /// <summary>
    /// The monthly mean daily Hs at off_1 follows the NORA10 monthly shape the
    /// base was fitted to, within sampling noise (the fit ran on other seeds).
    /// </summary>
    [Fact]
    public void Off1_MonthlyMeans_FollowTheNora10Shape()
    {
        var runs = MetoceanRuns.Acceptance;
        for (int month = 0; month < 12; month++)
        {
            double mean = runs.SelectMany(r => Enumerable.Range(0, r.Days).Where(d => SimCalendar.MonthIndex(d) == month).Select(d => r.ReferenceHs[d])).Average();
            double target = Cal.Metocean.ReferenceMonthlyMeanHsM(month);
            _output.WriteLine($"month {month + 1,2}: off_1 mean {F(mean * Off1)} m, reference {F(mean)} against {F(target)}");
            Assert.InRange(mean, target * 0.97, target * 1.03);
        }
    }

    /// <summary>Hs at a plot is its scale times the reference sea; Tp is the longer of the wind sea's and the swell's.</summary>
    [Fact]
    public void PlotHsAndTp_FollowTheReferenceSea()
    {
        var sea = new OffshoreMetocean();
        var stream = new SimRng(11).Stream(OffshoreMetocean.StreamScope);
        for (int day = 0; day < 400; day++)
        {
            sea.TickDay(day, WeatherState.Moderate, stream);
            foreach (var id in OffshorePlotsCalibration.PlotIds)
            {
                double scale = Cal.Plots.For(id).HsScale;
                Assert.Equal(scale * sea.ReferenceHsToday(), sea.HsAt(scale), 12);
                double tp = sea.TpAt(scale);
                Assert.True(tp >= Cal.Metocean.WindSeaTpPerSqrtHs * Math.Sqrt(sea.HsAt(scale)) - 1e-12);
                Assert.True(tp >= sea.SwellTpToday);
                if (sea.SwellHsToday > 0.0) Assert.InRange(sea.SwellTpToday, Cal.Metocean.SwellTpMinS, Cal.Metocean.SwellTpMaxS);
                else Assert.Equal(0.0, sea.SwellTpToday);
            }
        }
    }
}
