using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-06 acceptance 8: the fjord weather series for seeds 1-10 is unchanged.
/// The fingerprints below were recorded on the commit before F2-06 touched
/// <see cref="WeatherSystem"/> (branch point of wave3/sim-lanes-2, ecf1cb9):
/// ten years of every saved and reported weather quantity (state, sea
/// temperature, wind, the three sea-temperature offsets) and the 3-day storm
/// forecast, bit for bit, under the default rules, "harsh_storms" (storm
/// multiplier 1.5) and "calm_weather" (cap at Moderate). F2-06 adds the pure
/// <see cref="WeatherSystem.StateDistribution(int)"/> (the 3-day forecast now
/// goes through it) and the pure wind probability; neither may move a bit.
/// </summary>
public class WeatherSeriesFingerprintTests
{
    private const int Days = 3650;

    private static readonly (string Rules, double Multiplier, WeatherState Cap)[] RuleSets =
    {
        ("default", 1.0, WeatherState.Severe),
        ("harsh_storms", 1.5, WeatherState.Severe),
        ("calm_weather", 1.0, WeatherState.Moderate),
    };

    /// <summary>FNV-1a 64 over the day series; recorded at ecf1cb9 (before F2-06).</summary>
    private static readonly Dictionary<string, ulong> Expected = new()
    {
        ["calm_weather/1"] = 0xcddc6992bec09e06UL,
        ["calm_weather/10"] = 0xfd547e8259af0682UL,
        ["calm_weather/2"] = 0xc5f65c867f7a6ebcUL,
        ["calm_weather/3"] = 0x88dc0e64ab7e24c4UL,
        ["calm_weather/4"] = 0x745c854a542b9bc2UL,
        ["calm_weather/5"] = 0x0835eb30e73ac690UL,
        ["calm_weather/6"] = 0x042174ca9012a58cUL,
        ["calm_weather/7"] = 0xbc2671a4143f2bdaUL,
        ["calm_weather/8"] = 0xf67c1e946a3f83e0UL,
        ["calm_weather/9"] = 0xd1c4782a85c04269UL,
        ["default/1"] = 0xd587e6dee14103a7UL,
        ["default/10"] = 0xc161384ee43ed013UL,
        ["default/2"] = 0x7eaa3a16925b806eUL,
        ["default/3"] = 0xb5ed6268552090a1UL,
        ["default/4"] = 0x34f95538971609a7UL,
        ["default/5"] = 0x62a5ef694b4a8642UL,
        ["default/6"] = 0x5994f10598de57d7UL,
        ["default/7"] = 0xda6ad8b230707a07UL,
        ["default/8"] = 0x62c148915b7344eeUL,
        ["default/9"] = 0xbf9f3ff02beea8abUL,
        ["harsh_storms/1"] = 0xca35c56679569e7eUL,
        ["harsh_storms/10"] = 0x4e80895d5faf26caUL,
        ["harsh_storms/2"] = 0x57b35416f6ce2e2aUL,
        ["harsh_storms/3"] = 0xb41063c358ebdb42UL,
        ["harsh_storms/4"] = 0x848ca2310df814d1UL,
        ["harsh_storms/5"] = 0x1efe0fd2448b416cUL,
        ["harsh_storms/6"] = 0x360960a4b9af287eUL,
        ["harsh_storms/7"] = 0xd8ff0e38c635d1f4UL,
        ["harsh_storms/8"] = 0x73c9564e4f92fc13UL,
        ["harsh_storms/9"] = 0x8e2391413553d826UL,
    };

    internal static ulong Fingerprint(int seed, double multiplier, WeatherState cap)
    {
        var weather = new WeatherSystem(new SimRng(seed).Stream(SimRngStreams.Weather));
        weather.ApplyScenarioWeather(cap, multiplier);
        ulong h = 0xcbf29ce484222325UL;
        void Mix(long v)
        {
            for (int b = 0; b < 8; b++)
            {
                h ^= (byte)(v >> (8 * b));
                h *= 0x100000001b3UL;
            }
        }
        for (int d = 0; d < Days; d++)
        {
            weather.AdvanceDays(1);
            Mix((long)weather.CurrentState);
            Mix(BitConverter.DoubleToInt64Bits(weather.CurrentTemperatureC));
            Mix(BitConverter.DoubleToInt64Bits(weather.CurrentWindSpeedMs));
            Mix(BitConverter.DoubleToInt64Bits(weather.SeaAnomalyC));
            Mix(BitConverter.DoubleToInt64Bits(weather.DailyFluctuationC));
            Mix(BitConverter.DoubleToInt64Bits(weather.StormCoolingC));
            Mix(BitConverter.DoubleToInt64Bits(weather.StormRiskNext3Days()));
        }
        return h;
    }

    private readonly ITestOutputHelper _output;

    public WeatherSeriesFingerprintTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Bit-exact on the platform the fingerprints were recorded on (the golden
    /// traces' math-library fingerprint, osx-arm64): the weather draws Box-Muller
    /// normals through Math.Log/Cos/Sin/Exp, which differ across C math
    /// libraries in the last ulp. Elsewhere the platform-independent tests
    /// below and the golden-trace base-vs-head gate hold the rule.
    /// </summary>
    [Fact]
    public void FjordWeatherSeries_Seeds1To10_AreBitIdenticalToThePreF206Recording()
    {
        var actual = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var (rules, multiplier, cap) in RuleSets)
            for (int seed = 1; seed <= 10; seed++)
                actual[$"{rules}/{seed}"] = Fingerprint(seed, multiplier, cap);

        string table = string.Join(System.Environment.NewLine, actual.Select(kv =>
            $"        [\"{kv.Key}\"] = 0x{kv.Value.ToString("x16", CultureInfo.InvariantCulture)}UL,"));
        Assert.True(Expected.Count == actual.Count, "fingerprints not recorded yet:" + System.Environment.NewLine + table);
        if (!Integration.GoldenTraceFixtures.MathMatchesRecording)
        {
            _output.WriteLine("not enforced here: this platform's math library differs from the recording's (see GoldenTraceFixtures)");
            return;
        }
        foreach (var kv in actual)
            Assert.True(Expected[kv.Key] == kv.Value,
                $"weather series {kv.Key} changed: 0x{kv.Value:x16} != recorded 0x{Expected[kv.Key]:x16}");
    }

    /// <summary>
    /// The 3-day storm forecast as it was before F2-06 (copied from
    /// WeatherSystem.StormRiskNext3Days at ecf1cb9), so the delegation to
    /// <see cref="WeatherSystem.StateDistribution(int)"/> is checked bit for bit
    /// on every platform.
    /// </summary>
    private static double PreF206StormRisk(WeatherSystem w)
    {
        var p = new double[4];
        p[(int)w.CurrentState] = 1.0;
        for (int step = 1; step <= WeatherSystem.StormForecastDays; step++)
        {
            var m = WeatherSystem.TransitionMatrixFor(w.SimulationDay + step, w.StormFrequencyMultiplier, w.MaxState);
            var next = new double[4];
            for (int from = 0; from < 4; from++)
                for (int to = 0; to < 4; to++)
                    next[to] += p[from] * m[from, to];
            p = next;
        }
        return Math.Clamp(p[(int)WeatherState.Storm] + p[(int)WeatherState.Severe], 0.0, 1.0);
    }

    [Fact]
    public void StormRiskNext3Days_EqualsThePreF206AlgorithmBitForBit_OnEveryPlatform()
    {
        foreach (var (_, multiplier, cap) in RuleSets)
            for (int seed = 1; seed <= 10; seed++)
            {
                var weather = new WeatherSystem(new SimRng(seed).Stream(SimRngStreams.Weather));
                weather.ApplyScenarioWeather(cap, multiplier);
                for (int d = 0; d < 730; d++)
                {
                    weather.AdvanceDays(1);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(PreF206StormRisk(weather)),
                        BitConverter.DoubleToInt64Bits(weather.StormRiskNext3Days()));
                }
            }
    }

    /// <summary>
    /// Polling the new pure methods never moves the weather stream: a run that
    /// asks for every lead's state distribution and the wind probabilities each
    /// day ends with the same stream position and series as one that does not.
    /// </summary>
    [Fact]
    public void PollingStateDistributionAndWindProbability_LeavesTheWeatherSeriesUnchanged()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var quietRng = new SimRng(seed);
            var polledRng = new SimRng(seed);
            var quiet = new WeatherSystem(quietRng.Stream(SimRngStreams.Weather));
            var polled = new WeatherSystem(polledRng.Stream(SimRngStreams.Weather));
            for (int d = 0; d < 365; d++)
            {
                quiet.AdvanceDays(1);
                polled.AdvanceDays(1);
                for (int k = 0; k <= 10; k++) polled.StateDistribution(k);
                foreach (WeatherState s in Enum.GetValues<WeatherState>())
                    WeatherSystem.WindSpeedBelowProbability(s, 25.0);
                Assert.Equal(quiet.CurrentState, polled.CurrentState);
                Assert.Equal(BitConverter.DoubleToInt64Bits(quiet.CurrentWindSpeedMs), BitConverter.DoubleToInt64Bits(polled.CurrentWindSpeedMs));
                Assert.Equal(BitConverter.DoubleToInt64Bits(quiet.CurrentTemperatureC), BitConverter.DoubleToInt64Bits(polled.CurrentTemperatureC));
            }
            Assert.Equal(quietRng.CaptureStates(), polledRng.CaptureStates());
        }
    }
}
