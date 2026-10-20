using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Calibration;
using LakskongenSim.Tests.Integration;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-06 wired into the engine (roadmap F2-06, docs/FARM_TYPES_SPEC.md §3.3
/// "Metocean", acceptance 6-9 on the engine; the standalone model's own
/// acceptance 1-7 is <see cref="OffshoreMetoceanTests"/>): one open sea per
/// game, created with the first offshore unit and ticked in its own in-day
/// phase right after the weather, 8 uniforms a day from its own stream; each
/// unit's sea is its plot's fitted scale; the operation hook reads it; the
/// fjord weather never moves; a save mid-event resumes bit for bit.
/// </summary>
public class OffshoreSeaWiringTests
{
    private const string SeaStreamKey = SimRngStreams.OffshoreMetocean + "\u0001";

    private static SimulationEngine RichEngine(int seed, bool events = false)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: events, seed: seed);
        return engine;
    }

    private static SeaSite BuildUnit(SimulationEngine engine)
    {
        var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        return engine.OffshoreUnits.Single(u => u.Id == built.Id);
    }

    [Fact]
    public void AFjordOnlyGame_NeverCreatesTheSea_NorItsStream()
    {
        var engine = RichEngine(3, events: true);
        engine.Advance(120);
        Assert.Null(engine.State.Metocean);
        Assert.DoesNotContain(engine.Rng.CaptureStates().Keys, k => k.StartsWith(SimRngStreams.OffshoreMetocean, StringComparison.Ordinal));
        // The phase is registered right after the weather, and does nothing here.
        var phases = engine.InDayPhasesForTests.Select(p => p.GetType().Name).ToList();
        Assert.Equal(phases.IndexOf("WeatherAndEnvironmentTickSystem") + 1, phases.IndexOf("OffshoreSeaTickSystem"));
        Assert.Null(engine.GetSeaStateForecastView("offshore_1"));
    }

    /// <summary>
    /// Acceptance 7 on the engine: from the first offshore unit the sea draws
    /// exactly 8 uniforms a day, every day; it is on the weather's day between
    /// days, and each unit stands in its plot's sea at the fitted scale.
    /// </summary>
    [Fact]
    public void TheSea_DrawsEightUniformsEveryDay_AndEachUnitTakesItsPlotsFittedSea()
    {
        var engine = RichEngine(11);
        var unit = BuildUnit(engine);
        Assert.Null(engine.State.Metocean); // the legacy build starts no sea; the first step does
        engine.Advance(1);
        var sea = Assert.IsType<OffshoreMetocean>(engine.State.Metocean);
        Assert.Equal(engine.Weather.SimulationDay, sea.LastTickDay);
        Assert.Equal(engine.State.Day, sea.LastTickDay);
        double scale = OffshoreCalibration.Default.Plots.Off1.HsScale;
        Assert.Equal(1.058, scale);
        Assert.Equal(scale, engine.PlotHsScaleOf(unit), 15);
        for (int day = 0; day < 400; day++)
        {
            ulong before = engine.Rng.CaptureStates()[SeaStreamKey];
            engine.Advance(1);
            ulong after = engine.Rng.CaptureStates()[SeaStreamKey];
            Assert.Equal(8UL, GoldenTraceState.DrawsBetween(before, after));
            Assert.Equal(engine.State.Day, sea.LastTickDay);
            Assert.Equal(sea.HsAt(scale), unit.Offshore!.CurrentWaveHeight, 15);
            Assert.Equal(unit.Offshore.CurrentWaveHeight, engine.SeaHsAt(unit), 15);
        }
    }

    /// <summary>
    /// The engine's sea is the F2-06 model, not a copy: the same stream and the
    /// day's fjord weather fed to a standalone sea give the same days, bit for bit.
    /// </summary>
    [Fact]
    public void TheEnginesSea_IsTheStandaloneModel_OnTheDaysWeather()
    {
        var engine = RichEngine(29);
        BuildUnit(engine);
        engine.Advance(1);
        int start = engine.State.Metocean!.LastTickDay;
        var weatherByDay = new Dictionary<int, WeatherState> { [start] = engine.State.Metocean.WeatherToday };
        var hsByDay = new Dictionary<int, double> { [start] = engine.State.Metocean.ReferenceHsToday() };
        for (int i = 0; i < 300; i++)
        {
            engine.Advance(1);
            weatherByDay[engine.State.Day] = engine.Weather.CurrentState;
            hsByDay[engine.State.Day] = engine.State.Metocean.ReferenceHsToday();
        }
        var standalone = new OffshoreMetocean();
        var stream = new SimRng(29).Stream(SimRngStreams.OffshoreMetocean);
        for (int day = start; day <= engine.State.Day; day++)
        {
            standalone.TickDay(day, weatherByDay[day], stream);
            Assert.Equal(hsByDay[day], standalone.ReferenceHsToday());
        }
        Assert.Equal(engine.State.Metocean.ResidualX, standalone.ResidualX);
        Assert.Equal(engine.State.Metocean.NextSerial, standalone.NextSerial);
    }

    /// <summary>
    /// Acceptance 8: the fjord weather series for seeds 1-10 is unchanged by an
    /// offshore unit and its sea (state, temperature, wind, wave, the 3-day
    /// storm risk and the weather stream, every day of a year).
    /// </summary>
    [Fact]
    public void TheFjordWeatherSeries_IsUnchanged_ForSeedsOneToTen()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var plain = RichEngine(seed);
            var offshore = RichEngine(seed);
            BuildUnit(offshore);
            for (int day = 0; day < 365; day++)
            {
                plain.Advance(1);
                offshore.Advance(1);
                var a = plain.Weather;
                var b = offshore.Weather;
                Assert.True(
                    (a.CurrentState, a.CurrentTemperatureC, a.CurrentWindSpeedMs, a.CurrentWaveHeight, a.StormRiskNext3Days())
                    == (b.CurrentState, b.CurrentTemperatureC, b.CurrentWindSpeedMs, b.CurrentWaveHeight, b.StormRiskNext3Days()),
                    $"seed {seed}, day {day}: the fjord weather moved");
            }
            string weatherKey = SimRngStreams.Weather + "\u0001";
            Assert.Equal(plain.Rng.CaptureStates()[weatherKey], offshore.Rng.CaptureStates()[weatherKey]);
            Assert.NotNull(offshore.State.Metocean);
        }
    }

    /// <summary>
    /// Acceptance 9: a save taken while a known swell event waits for its onset
    /// (and the sea holds its AR state) resumes bit for bit: the resumed game
    /// and the uninterrupted one are equal for 120 days, save and sea.
    /// </summary>
    [Fact]
    public void ASaveMidEvent_ResumesBitIdentical()
    {
        var engine = RichEngine(5, events: true);
        BuildUnit(engine);
        engine.Advance(1);
        // Run to a day with a known swell event whose onset is still ahead.
        int guard = 0;
        while (!engine.State.Metocean!.Events.Any(e => e.OnsetDay > engine.State.Day && e.CreatedDay < engine.State.Day))
        {
            engine.Advance(1);
            Assert.True(++guard < 400, "no swell event in 400 days");
        }
        var pending = engine.State.Metocean.Events.First(e => e.OnsetDay > engine.State.Day);
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: true);
        Assert.NotNull(resumed.State.Metocean);
        Assert.Contains(resumed.State.Metocean!.Events, e => e == pending);
        Assert.Equal(engine.State.Metocean.ResidualX, resumed.State.Metocean.ResidualX);
        // The forecast reads the same saved state: the same numbers.
        var unit = engine.OffshoreUnits.Single();
        var f1 = engine.SeaForecastFor(unit)!;
        var f2 = resumed.SeaForecastFor(resumed.OffshoreUnits.Single())!;
        for (int lead = 1; lead <= f1.HorizonDays; lead++)
            Assert.Equal(f1.ProbabilityAtOrBelow(lead, 2.0), f2.ProbabilityAtOrBelow(lead, 2.0));

        for (int day = 0; day < 120; day++)
        {
            engine.Advance(1);
            resumed.Advance(1);
            Assert.Equal(engine.State.Metocean.ReferenceHsToday(), resumed.State.Metocean.ReferenceHsToday());
        }
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    /// <summary>
    /// Acceptance 6 on the engine: 1,000 forecast polls (the board's view and
    /// raw probabilities) move no stream and change no save.
    /// </summary>
    [Fact]
    public void AThousandForecastPolls_MoveNoStream_AndChangeNoSave()
    {
        var engine = RichEngine(17, events: true);
        var unit = BuildUnit(engine);
        engine.Advance(40);
        var streams = engine.Rng.CaptureStates();
        string save = SaveStateComparer.Serialize(engine);
        for (int i = 0; i < 1000; i++)
        {
            var view = engine.GetSeaStateForecastView(unit.Id);
            Assert.NotNull(view);
            var forecast = engine.SeaForecastFor(unit)!;
            _ = forecast.ProbabilityAtOrBelow(1 + i % forecast.HorizonDays, 1.0 + (i % 40) / 10.0);
        }
        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(save, SaveStateComparer.Serialize(engine));
    }

    /// <summary>The window board's view: ten days, every row's probability, today's sea as the unit stands in it.</summary>
    [Fact]
    public void TheForecastView_HoldsTenDaysOfEveryRow_AndTodaysSea()
    {
        var engine = RichEngine(23);
        var unit = BuildUnit(engine);
        engine.Advance(30);
        var view = engine.GetSeaStateForecastView(unit.Id)!;
        Assert.Equal(engine.State.Day, view.IssuedDay);
        Assert.Equal(unit.Offshore!.CurrentWaveHeight, view.HsTodayM, 12);
        Assert.Equal(10, view.Days.Count);
        Assert.Equal(SimulationEngine.SeaBoardRows.Count - 1, view.RowLimitsM.Count); // feeding has no limit
        foreach (var day in view.Days)
        {
            Assert.Equal(view.IssuedDay + day.LeadDays, day.Day);
            Assert.True(day.HsP10M <= day.HsP50M && day.HsP50M <= day.HsP90M);
            Assert.Equal(SimulationEngine.SeaBoardRows.Select(r => r.Id), day.RowProbabilities.Select(r => r.Row));
            Assert.All(day.RowProbabilities, r => Assert.InRange(r.Probability, 0.0, 1.0));
            var p = day.RowProbabilities.ToDictionary(r => r.Row, r => r.Probability);
            Assert.Equal(1.0, p["feeding"]);
            // A DP vessel (3.0 m) is never less likely than a standard one (2.5 m).
            Assert.True(p["resupply_dp"] >= p["resupply"]);
            Assert.True(p["wellboat_offshore"] >= p["wellboat"]);
        }
        Assert.Null(engine.GetSeaStateForecastView("site_does_not_exist"));
    }

    /// <summary>
    /// The F2-06 hook in the wellboat queue: at an offshore unit a due ROV job
    /// runs on a day whose sea is within 3.0 m and waits on a day above it;
    /// at a fjord site the hook never stops a job.
    /// </summary>
    [Fact]
    public void ADueWellboatJob_AtAnOffshoreUnit_RunsOnlyOnADayWithinItsLimit()
    {
        var engine = RichEngine(41);
        var unit = BuildUnit(engine);
        engine.Advance(1);
        double limit = OffshoreCalibration.Default.Operability.NetInspectionHsM;
        int ran = 0, waited = 0;
        for (int day = 0; day < 240; day++)
        {
            if (!engine.State.WellboatJobs.Any(j => j.SiteId == unit.Id))
                Assert.True(engine.RequestWellboat(unit.Id, "rov").Ok);
            var job = engine.State.WellboatJobs.Single(j => j.SiteId == unit.Id);
            bool due = job.DueDay <= engine.State.Day + 1;
            engine.Advance(1);
            bool stillQueued = engine.State.WellboatJobs.Any(j => j.Id == job.Id);
            double hs = unit.Offshore!.CurrentWaveHeight;
            if (!due) continue;
            if (stillQueued)
            {
                Assert.True(hs > limit, $"day {engine.State.Day}: the job waited at Hs {hs:F2} m");
                waited++;
            }
            else
            {
                Assert.True(hs <= limit, $"day {engine.State.Day}: the job ran at Hs {hs:F2} m");
                ran++;
            }
        }
        Assert.True(ran > 20, $"only {ran} jobs ran");
        Assert.True(waited > 0, "no rough day stopped a job: the test saw no sea above the limit");

        // The hook on a fjord site is always true, whatever the sea.
        var fjord = new SeaSite { Id = "fjord_x", Kind = SiteKind.Fjord };
        Assert.True(engine.OperationPossible(fjord, "harvest", engine.State.Day));
        Assert.True(engine.OperationPossible(null, "harvest", engine.State.Day));
    }

    /// <summary>The offshore section of the facilities calibration keeps F2-04's guard: every leaf, every month.</summary>
    [Fact]
    public void TheOffshoreSection_KeepsTheProvenanceGuard_OnEveryLeafAndMonth()
    {
        var section = CalibrationRegistry.Default.Facilities.Offshore;
        var violations = CalibrationProvenanceGuard.Violations(section, "facilities.offshore");
        Assert.True(violations.Count == 0, string.Join("\n", violations));
        var leaves = CalibrationProvenanceGuard.Leaves(section, "facilities.offshore");
        // 5 monthly containers × 12 + 4 weather states + the metocean, operability and plot leaves + F2-07's.
        Assert.True(leaves.Count >= 5 * 12 + 4 + 38, $"only {leaves.Count} leaves");
        Assert.Contains(leaves, l => l.Path == "facilities.offshore.Metocean.BaseMuMonth.M07");
        Assert.Contains(leaves, l => l.Path == "facilities.offshore.Metocean.WeatherDelta.Severe");
        // The registry's section is the file's (loaded through the include list).
        Assert.Equal(OffshoreCalibration.Default, section);
    }
}
