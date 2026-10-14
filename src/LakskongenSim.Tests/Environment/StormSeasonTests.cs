using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// S1-02 (review F456, roadmap D3): the storm-season multiplier was applied to
/// the Storm and Severe columns without renormalizing the row, so in Oct-Mar the
/// cumulative roll was used up by Storm before Severe: a severe storm could only
/// persist, never start, winter had no severe-storm days, and the 3-day forecast
/// (which did renormalize) disagreed with the weather that happened. One helper
/// (WeatherSystem.FillTransitionRow) now feeds both the sampler and the forecast.
/// </summary>
public class StormSeasonTests
{
    private const int StateCount = 4;
    private const int WinterDay = 15;   // mid-January, storm season
    private const int SummerDay = 195;  // mid-July
    private const int Seeds = 10;
    private const int Years = 3;

    private static double[] Stationary(double[,] m)
    {
        var p = new[] { 0.25, 0.25, 0.25, 0.25 };
        for (int it = 0; it < 5_000; it++)
        {
            var next = new double[StateCount];
            for (int i = 0; i < StateCount; i++)
                for (int j = 0; j < StateCount; j++)
                    next[j] += p[i] * m[i, j];
            p = next;
        }
        return p;
    }

    private static WeatherSystem NewWeather(int seed) => new(new SimRng(seed).Stream(SimRngStreams.Weather));

    [Theory]
    [InlineData(1.0, WeatherState.Severe)]
    [InlineData(1.5, WeatherState.Severe)]  // scenario "harsh_storms"
    [InlineData(1.0, WeatherState.Moderate)] // scenario "calm_weather"
    public void EveryTransitionRow_IsAProbabilityDistribution(double stormMultiplier, WeatherState cap)
    {
        foreach (int day in new[] { WinterDay, SummerDay })
        {
            var m = WeatherSystem.TransitionMatrixFor(day, stormMultiplier, cap);
            for (int from = 0; from < StateCount; from++)
            {
                double sum = 0.0;
                for (int to = 0; to < StateCount; to++)
                {
                    Assert.InRange(m[from, to], 0.0, 1.0);
                    if (to > (int)cap) Assert.Equal(0.0, m[from, to]);
                    sum += m[from, to];
                }
                Assert.Equal(1.0, sum, 12);
            }
        }
    }

    /// <summary>Acceptance 1: winter has severe storms, and at least the off-season's storm share.</summary>
    [Fact]
    public void StationaryDistribution_WinterHasSevereStorms_AndAtLeastTheSummerStormShare()
    {
        var winter = Stationary(WeatherSystem.TransitionMatrixFor(WinterDay, 1.0, WeatherState.Severe));
        var summer = Stationary(WeatherSystem.TransitionMatrixFor(SummerDay, 1.0, WeatherState.Severe));

        // Before: winter Severe 0.000 (summer 0.093), winter Storm+Severe 0.313 vs summer 0.337.
        Assert.True(winter[(int)WeatherState.Severe] > 0.10, $"winter severe share {winter[3]:F3}");
        Assert.True(winter[2] + winter[3] >= summer[2] + summer[3],
            $"winter storm+severe {winter[2] + winter[3]:F3} < summer {summer[2] + summer[3]:F3}");
        // The off-season chain is unchanged (its base rows already sum to 1).
        Assert.Equal(0.093, summer[(int)WeatherState.Severe], 3);
    }

    /// <summary>Acceptance 1, on the sampled weather (10 seeds x 3 years).</summary>
    [Fact]
    public void SampledWeather_OctToMarHasSevereDays_AndAtLeastAprToSepStormShare()
    {
        int[] days = new int[2];
        int[] stormy = new int[2];
        int[] severe = new int[2];
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var wx = NewWeather(seed);
            for (int d = 0; d < Years * 365; d++)
            {
                wx.AdvanceDays(1);
                int s = WeatherSystem.IsStormSeason(wx.SimulationDay) ? 1 : 0;
                days[s]++;
                if (wx.CurrentState >= WeatherState.Storm) stormy[s]++;
                if (wx.CurrentState == WeatherState.Severe) severe[s]++;
            }
        }
        double winterSevere = (double)severe[1] / days[1];
        double winterStormy = (double)stormy[1] / days[1];
        double summerStormy = (double)stormy[0] / days[0];
        Assert.True(winterSevere > 0.10, $"Oct-Mar severe share {winterSevere:P1}");
        Assert.True(winterStormy >= summerStormy, $"Oct-Mar storm+severe {winterStormy:P1} < Apr-Sep {summerStormy:P1}");
    }

    /// <summary>
    /// Acceptance 2: over 10 seeds x 3 years the forecast's mean probability that
    /// the weather three days ahead is Storm or Severe matches how often it was,
    /// within 2 percentage points. Each season (half the sample, autocorrelated
    /// days: about 1.3 points of sampling error) must match within 3 points.
    /// Before S1-02 the forecast renormalized and the sampler did not.
    /// </summary>
    [Theory]
    [InlineData(1.0, Seeds, 0.02, 0.03)]
    [InlineData(1.5, Seeds, 0.02, 0.03)] // scenario "harsh_storms": the forecast follows the scenario's chain too
    // Unbiased, not just close: on 200 seeds the gap shrinks to sampling noise
    // (measured 10 seeds -1.5 points, 100 seeds -0.15, 400 seeds -0.04).
    [InlineData(1.0, 200, 0.005, 0.01)]
    public void Forecast_MatchesRealizedStormFrequency_WithinTwoPoints(double stormMultiplier, int seeds,
        double overallTolerance, double seasonTolerance)
    {
        int horizon = WeatherSystem.StormForecastDays;
        double[] forecastSum = new double[2];
        int[] realized = new int[2];
        int[] samples = new int[2];
        for (int seed = 1; seed <= seeds; seed++)
        {
            var wx = NewWeather(seed);
            wx.ApplyScenarioWeather(WeatherState.Severe, stormMultiplier);
            int days = Years * 365;
            var forecast = new double[days];
            var season = new int[days];
            var stormyOn = new bool[days];
            for (int d = 0; d < days; d++)
            {
                wx.AdvanceDays(1);
                forecast[d] = wx.StormRiskNext3Days();
                season[d] = WeatherSystem.IsStormSeason(wx.SimulationDay) ? 1 : 0;
                stormyOn[d] = wx.CurrentState >= WeatherState.Storm;
            }
            for (int d = 0; d + horizon < days; d++)
            {
                forecastSum[season[d]] += forecast[d];
                if (stormyOn[d + horizon]) realized[season[d]]++;
                samples[season[d]]++;
            }
        }

        double overallForecast = (forecastSum[0] + forecastSum[1]) / (samples[0] + samples[1]);
        double overallRealized = (double)(realized[0] + realized[1]) / (samples[0] + samples[1]);
        Console.WriteLine($"x{stormMultiplier} {seeds} seeds: overall forecast {overallForecast:P2} realized {overallRealized:P2}; " +
            $"Apr-Sep {forecastSum[0] / samples[0]:P2}/{(double)realized[0] / samples[0]:P2}; " +
            $"Oct-Mar {forecastSum[1] / samples[1]:P2}/{(double)realized[1] / samples[1]:P2}");
        Assert.True(Math.Abs(overallRealized - overallForecast) <= overallTolerance,
            $"forecast {overallForecast:P2} vs realized {overallRealized:P2}");
        for (int s = 0; s < 2; s++)
        {
            double f = forecastSum[s] / samples[s];
            double r = (double)realized[s] / samples[s];
            Assert.True(Math.Abs(r - f) <= seasonTolerance,
                $"{(s == 1 ? "Oct-Mar" : "Apr-Sep")}: forecast {f:P2} vs realized {r:P2}");
        }
    }

    [Fact]
    public void Forecast_UnderCalmWeatherScenario_IsZero_AndNoStormComes()
    {
        var wx = NewWeather(3);
        wx.ApplyScenarioWeather(WeatherState.Moderate, 1.0);
        for (int d = 0; d < 365; d++)
        {
            wx.AdvanceDays(1);
            Assert.Equal(0.0, wx.StormRiskNext3Days());
            Assert.True(wx.CurrentState <= WeatherState.Moderate);
        }
    }

    /// <summary>
    /// The storm-cooling rule applies to the stratified half of the year. In the
    /// storm season the coastal surface is the coldest layer, so a winter storm
    /// no longer knocks 1.5 °C off the cage water (it never had to before S1-02,
    /// when the chain could not reach Severe in winter).
    /// </summary>
    [Fact]
    public void SevereStormsCoolTheWaterInSummer_ButNotInWinter()
    {
        static double MeanTemperatureOnSevereDays(int dayOfYear)
        {
            double sum = 0.0;
            const int samples = 2_000;
            for (int seed = 1; seed <= samples; seed++)
            {
                var wx = NewWeather(seed);
                wx.ImportState(dayOfYear - 1, 8.0, 5.0, 0.5, WeatherState.Severe);
                wx.AdvanceDays(1);
                sum += wx.CurrentTemperatureC;
            }
            return sum / samples;
        }

        // S2-02 (one calendar): the monthly means now sit at mid-month on real
        // month lengths, not on the first of 30-day months, so the climatology on
        // these days moved (national reference: 6.78 °C on 20 Nov, 12.61 °C on
        // 20 Jul; it was 6.0 and 13.2). The rule is unchanged and asserted more
        // tightly against the climatology itself: severe-day fluctuation sd
        // 0.9 °C plus one day of the slow anomaly, 2,000 samples (standard error
        // 0.02 °C); the old bands were ±0.4 and ±0.35 °C.
        double november = MeanTemperatureOnSevereDays(320);
        double july = MeanTemperatureOnSevereDays(200);
        double novemberClimatology = ProductionAreas.ClimatologyC(ProductionAreas.NationalReference, 320);
        double julyClimatology = ProductionAreas.ClimatologyC(ProductionAreas.NationalReference, 200);
        Assert.InRange(november, novemberClimatology - 0.15, novemberClimatology + 0.15);  // no cooling
        Assert.InRange(july, julyClimatology - 1.5 - 0.15, julyClimatology - 1.5 + 0.15);  // 1.5 °C cooler
    }

    // ── Storm episodes (engine) ─────────────────────────────────────────────

    private static SimulationEngine EngineWithStockedSite()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 5);
        engine.State.Zones.Add(new Zone("storm-zone"));
        engine.State.SeaSites.Add(new SeaSite("storm-site", "storm-zone"));
        Assert.True(engine.StockSmolts("storm-site", 10_000, "standard").Ok);
        return engine;
    }

    private static int StormDamageEntries(SimulationEngine engine)
        => engine.State.EventLog.Count(e => e.Name == "Storm Damage");

    private static void Weather(SimulationEngine engine, WeatherState state)
    {
        engine.Weather.ForceWeatherState(state);
        engine.ApplyStormAndTemperatureImpacts();
    }

    /// <summary>
    /// A storm episode is charged once at the severity it starts with, plus the
    /// escalation to Severe once; flipping between Storm and Severe inside the
    /// episode charges nothing more. The old edge rule charged every flip.
    /// </summary>
    [Fact]
    public void AStormEpisode_IsChargedOnce_PlusOneEscalation()
    {
        var engine = EngineWithStockedSite();
        decimal before = engine.FinancialStatements.CostBreakdown.GetValueOrDefault("storm_repairs");

        Weather(engine, WeatherState.Calm);
        Weather(engine, WeatherState.Storm);    // new episode: 0.5
        Weather(engine, WeatherState.Severe);   // escalation: +0.5
        Weather(engine, WeatherState.Storm);    // same episode: nothing
        Weather(engine, WeatherState.Severe);   // back to its peak: nothing
        Weather(engine, WeatherState.Storm);
        Assert.Equal(2, StormDamageEntries(engine));
        Weather(engine, WeatherState.Calm);     // episode over
        Weather(engine, WeatherState.Severe);   // new episode: 1.0
        Assert.Equal(3, StormDamageEntries(engine));

        decimal charged = engine.FinancialStatements.CostBreakdown.GetValueOrDefault("storm_repairs") - before;
        decimal oneSevereEpisode = engine.State.EventLog.Last(e => e.Name == "Storm Damage").BalanceChange;
        Assert.Equal(-2m * oneSevereEpisode, charged); // (0.5 + 0.5) + 1.0 severe-episode units
    }

    [Fact]
    public void AStormEpisode_SurvivesASave_WithoutBeingChargedAgain()
    {
        var engine = EngineWithStockedSite();
        Weather(engine, WeatherState.Calm);
        Weather(engine, WeatherState.Severe);
        Weather(engine, WeatherState.Storm);
        int charged = StormDamageEntries(engine);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Weather(resumed, WeatherState.Severe); // still the same episode, already at its peak
        Assert.Equal(charged, StormDamageEntries(resumed));
        Weather(resumed, WeatherState.Calm);
        Weather(resumed, WeatherState.Storm);
        Assert.Equal(charged + 1, StormDamageEntries(resumed));
    }
}
