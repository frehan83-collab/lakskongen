using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Farm.Land;
using LakskongenSim.Tests.Farm;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Roadmap F2-12 acceptance 4 (docs/FARM_TYPES_SPEC.md §7.1): a profit
    /// sweep over the Vannstrategi's reuse r and deep-intake share w on an HFS
    /// lot on pad_a (Salmon Evolution's PO5 water) finds an interior r
    /// (0.3-0.8) in at least 2 of 4 seasons, and the best probe pins neither
    /// lever at a bound. Profit is the value of the live-weight growth at the
    /// registry's salmon price, less feed, electricity and liquid oxygen;
    /// fixed costs do not move the optimum.
    /// </summary>
    public class LandVannstrategiSweepTests
    {
        private static readonly LandCalibration C = LandCalibration.Default;
        private static readonly double[] Reuses = Enumerable.Range(0, 20).Select(i => i * 0.05).ToArray();
        private static readonly double[] Blends = Enumerable.Range(0, 11).Select(i => i * 0.1).ToArray();
        private static readonly string[] SeasonNames = { "winter", "spring", "summer", "autumn" };
        private static readonly int[] SeasonStarts = { 0, 90, 181, 273 };
        private const int SeasonDays = 91;

        private readonly ITestOutputHelper _output;

        public LandVannstrategiSweepTests(ITestOutputHelper output) => _output = output;

        private static double PriceLwNok
            => (double)CalibrationRegistry.Default.Economy.BaseSalmonPricePerKgNOK * C.Production.HogPerLw;

        private static double Profit(LandProjectionResult r)
            => r.NetGrowthKg * PriceLwNok
               - r.FeedKg * (double)CalibrationRegistry.Default.Feed.FeedPricePerKgFeedNOK
               - r.Energy.TotalKWh * C.Energy.PowerPriceNokPerKWh
               - r.LoxKg * C.Oxygen.LoxPriceNokPerKg;

        private sealed record Probe(double Reuse, double Blend, bool Auto, double Profit);

        private static LandProjectionSettings Base => LandTestSites.SalmonEvolutionYear(C);

        private static readonly Lazy<Probe[]> SeasonBest = new(() =>
        {
            var best = new Probe[SeasonStarts.Length];
            for (int s = 0; s < SeasonStarts.Length; s++)
            {
                Probe? top = null;
                foreach (double r in Reuses)
                    foreach (double w in Blends)
                    {
                        var settings = Base with
                        {
                            Strategy = new LandWaterStrategy(r, w, C.Hfs.DefaultSetpointC),
                            WarmupStrategy = LandSiteTemplates.DefaultStrategy(C.Hfs),
                            StartDay = SimCalendar.DaysPerYear + SeasonStarts[s],
                            Days = SeasonDays,
                        };
                        double profit = Profit(LandLotProjector.Run(settings, C));
                        if (top == null || profit > top.Profit) top = new Probe(r, w, false, profit);
                    }
                best[s] = top!;
            }
            return best;
        });

        [Fact]
        public void F2_12_Acceptance4_AnInteriorReuseWins_InAtLeastTwoOfFourSeasons()
        {
            var best = SeasonBest.Value;
            int interior = 0;
            for (int s = 0; s < best.Length; s++)
            {
                _output.WriteLine($"{SeasonNames[s]}: best r {best[s].Reuse:F2}, w {best[s].Blend:F1}, {best[s].Profit / 1e6:F1} MNOK");
                if (best[s].Reuse >= 0.3 && best[s].Reuse <= 0.8 && best[s].Reuse > Reuses[0] && best[s].Reuse < Reuses[^1])
                    interior++;
            }
            Assert.True(interior >= 2, $"an interior reuse wins in {interior} of 4 seasons");
        }

        [Fact]
        public void TheSummerBlend_IsInterior_WhenTheShallowWaterRunsWarmerThanTheSetpoint()
        {
            // July-September PO5 water at 3 m is 13.5-15 °C, the deep water 10 °C:
            // the best fixed blend lies between them.
            var summer = SeasonBest.Value[2];
            Assert.InRange(summer.Blend, 0.05, 0.95);
        }

        [Fact]
        public void F2_12_Acceptance4_TheBestProbe_PinsNeitherReuseNorBlendAtABound()
        {
            // A probe holds one Vannstrategi for a year: a fixed (r, w), or r
            // with the intakes blended toward the setpoint every day.
            var probes = new List<Probe>();
            foreach (double r in Reuses)
            {
                foreach (double w in Blends)
                    probes.Add(new Probe(r, w, false, Profit(LandLotProjector.Run(Base with
                    {
                        Strategy = new LandWaterStrategy(r, w, C.Hfs.DefaultSetpointC),
                    }, C))));
                probes.Add(new Probe(r, double.NaN, true, Profit(LandLotProjector.Run(Base with
                {
                    Strategy = new LandWaterStrategy(r, 0.5, C.Hfs.DefaultSetpointC, BlendTowardSetpoint: true),
                }, C))));
            }
            var best = probes.OrderByDescending(p => p.Profit).First();
            var bestFixed = probes.Where(p => !p.Auto).OrderByDescending(p => p.Profit).First();
            _output.WriteLine($"best probe: r {best.Reuse:F2}, {(best.Auto ? "blend toward the setpoint" : $"w {best.Blend:F1}")}, {best.Profit / 1e6:F1} MNOK");
            _output.WriteLine($"best fixed blend: r {bestFixed.Reuse:F2}, w {bestFixed.Blend:F1}, {bestFixed.Profit / 1e6:F1} MNOK");

            Assert.InRange(best.Reuse, 0.3, 0.8);
            Assert.True(best.Auto, "a fixed blend beat blending toward the setpoint");
            // The best probe moves w through the year: interior on the warm-water
            // days, never parked at one end all year.
            var days = LandLotProjector.Run(Base with
            {
                Strategy = new LandWaterStrategy(best.Reuse, 0.5, C.Hfs.DefaultSetpointC, BlendTowardSetpoint: true),
                RecordDays = true,
            }, C).Daily;
            int interiorDays = days.Count(d => d.DeepShare > 0.0 && d.DeepShare < 1.0);
            _output.WriteLine($"blend interior on {interiorDays} of {days.Count} days; mean w {days.Average(d => d.DeepShare):F2}");
            Assert.True(interiorDays >= days.Count / 4, $"the blend is interior on only {interiorDays} days");
            Assert.InRange(days.Average(d => d.DeepShare), 0.05, 0.95);
        }
    }
}
