using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F2-21 (farm-types spec §4 "Wave driver", §7.2 acceptance 4): the open sea
/// the renderer draws at an offshore plot is the engine's sea split into its
/// wind sea and its swell (Σa² = Hs²/8 across both), and the swell lives in
/// long bands at 150-300 m wavelength beyond the wind sea's 128 m band. The
/// split is a pure read: it moves no stream and changes no save.
/// </summary>
public class OpenSeaSwellTests
{
    private static SimulationEngine RichEngine(int seed)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: true, seed: seed);
        return engine;
    }

    private static SeaSite BuildUnit(SimulationEngine engine)
    {
        var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        return engine.OffshoreUnits.Single(u => u.Id == built.Id);
    }

    /// <summary>Acceptance 4's spectrum: at Hs 4 m the swell bands carry Hs²/8 and sit at 150-300 m.</summary>
    [Theory]
    [InlineData(9.0)]
    [InlineData(10.1)]
    [InlineData(12.0)]
    [InlineData(13.4)]
    [InlineData(14.0)]
    public void TheSwellBands_CarryHsSquaredOverEight_At150To300Metres(double tpS)
    {
        double[] a = SeaStateModel.SwellAmplitudes(4.0, tpS);
        Assert.Equal(SeaStateModel.SwellBandWavelengthsM.Length, a.Length);
        Assert.Equal(4.0 * 4.0 / 8.0, a.Sum(x => x * x), 9);
        Assert.Equal(4.0, SeaStateModel.SignificantHeight(a), 9);
        Assert.All(SeaStateModel.SwellBandWavelengthsM, l => Assert.InRange(l, 150.0, 300.0));
        // Beyond the wind sea's longest band: the swell is its own, longer sea.
        Assert.True(SeaStateModel.SwellBandWavelengthsM.Min() > SeaStateModel.BandWavelengthsM.Max());
        Assert.Equal(SeaStateModel.SwellBandWavelengthsM.OrderByDescending(l => l), SeaStateModel.SwellBandWavelengthsM);
    }

    /// <summary>
    /// The swell's period picks its band: most of a 9 s swell's energy is in
    /// the 160 m band, most of a 14 s swell's in the 280 m band (JONSWAP's
    /// high-frequency tail keeps a share in the shorter band).
    /// </summary>
    [Fact]
    public void ASwellsPeriod_PicksItsBand()
    {
        static double LongShare(double[] a) => a[0] * a[0] / (a[0] * a[0] + a[1] * a[1]);
        double[] shortSwell = SeaStateModel.SwellAmplitudes(3.0, 9.0);
        double[] longSwell = SeaStateModel.SwellAmplitudes(3.0, 14.0);
        Assert.True(LongShare(shortSwell) < 0.2, $"9 s: 280 m band share {LongShare(shortSwell):F3}");
        Assert.True(LongShare(longSwell) > 0.65, $"14 s: 280 m band share {LongShare(longSwell):F3}");
        Assert.True(LongShare(SeaStateModel.SwellAmplitudes(3.0, 12.0)) > LongShare(SeaStateModel.SwellAmplitudes(3.0, 10.0)));
        Assert.All(SeaStateModel.SwellAmplitudes(0.0, 12.0), x => Assert.Equal(0.0, x));
    }

    /// <summary>The equivalent wind inverts the growth law: its sea over the fetch has the asked height.</summary>
    [Theory]
    [InlineData(0.2)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(4.0)]
    [InlineData(8.0)]
    public void WindForHeight_InvertsTheGrowthLaw(double hsM)
    {
        double wind = SeaStateModel.WindForHeight(hsM, SeaStateModel.ExposedFetchM);
        Assert.InRange(wind, 0.3, SeaStateModel.MaxEquivalentWindMs);
        Assert.Equal(hsM, SeaStateModel.Derive(wind, SeaStateModel.ExposedFetchM).SignificantHeightM, 6);
        Assert.Equal(0.0, SeaStateModel.WindForHeight(0.0, SeaStateModel.ExposedFetchM));
    }

    /// <summary>
    /// The engine's split of today's sea at a unit: wind sea² + swell² = the
    /// unit's Hs² every day, the swell never under the background swell at the
    /// plot's scale, its period inside the model's swell periods; null for a
    /// site that is no unit and for a game with no open sea; and reading it a
    /// thousand times moves no stream and changes no save.
    /// </summary>
    [Fact]
    public void TodaysSea_SplitsIntoWindSeaAndSwell_AsAPureRead()
    {
        var bare = RichEngine(5);
        bare.Advance(5);
        Assert.Null(bare.OpenSeaPartsAt("offshore_1"));

        var engine = RichEngine(29);
        var unit = BuildUnit(engine);
        var cal = OffshoreCalibration.Default.Metocean;
        int withStormSwell = 0;
        for (int day = 0; day < 240; day++)
        {
            engine.Advance(1);
            var view = engine.GetSeaStateForecastView(unit.Id)!;
            var parts = engine.OpenSeaPartsAt(unit.Id)!;
            double hs = view.HsTodayM;
            Assert.Equal(hs * hs, parts.WindSeaHsM * parts.WindSeaHsM + parts.SwellHsM * parts.SwellHsM, 9);
            double scale = hs / engine.State.Metocean!.ReferenceHsToday();
            Assert.True(parts.SwellHsM >= scale * cal.BackgroundSwellHsM - 1e-12, $"day {day}: swell {parts.SwellHsM:F3}");
            Assert.InRange(parts.SwellTpS, cal.SwellTpMinS, cal.SwellTpMaxS);
            if (view.SwellHsTodayM > 0.0)
                withStormSwell++;
        }
        Assert.True(withStormSwell > 0, "240 days with no storm swell: the split's storm half was never exercised");
        Assert.Null(engine.OpenSeaPartsAt("site_does_not_exist"));

        var streams = engine.Rng.CaptureStates();
        string save = SaveStateComparer.Serialize(engine);
        for (int i = 0; i < 1000; i++)
            Assert.NotNull(engine.OpenSeaPartsAt(unit.Id));
        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(save, SaveStateComparer.Serialize(engine));
    }
}
