using System;
using System.Linq;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G1-05: the sea the water shader draws comes from the wind and the fetch
    /// (JONSWAP growth, Carter 1982; Pierson–Moskowitz cap; Monahan whitecaps).
    /// The old water multiplied a fixed wave by the wind and by 0.12·Hs.
    /// </summary>
    public class SeaStateModelTests
    {
        /// <summary>The basin of the default fjord: a few kilometres of water between the sill and the head.</summary>
        private const double BasinFetchM = 3000.0;

        [Fact]
        public void ExposedCoast_At20Ms_Rolls2To4MetreSeas()
        {
            var sea = SeaStateModel.Derive(20.0, SeaStateModel.ExposedFetchM);
            Assert.InRange(sea.SignificantHeightM, 2.0, 4.0);
            // A storm sea: long waves (Tp 7-10 s, wavelength 75-150 m).
            Assert.InRange(sea.PeakPeriodS, 7.0, 10.0);
            Assert.InRange(sea.PeakWavelengthM, 75.0, 160.0);
        }

        [Fact]
        public void ShelteredBasin_At3Ms_StaysNearFlat()
        {
            // NS 9415 class A ("small exposure"): Hs below 0.5 m, Tp below 2 s
            // even as a design value. A light breeze over 3 km of fetch raises
            // a ripple: Carter gives Hs 0.075 m, Tp 1.3 s.
            var sea = SeaStateModel.Derive(3.0, BasinFetchM);
            Assert.InRange(sea.SignificantHeightM, 0.05, 0.15);
            Assert.InRange(sea.PeakPeriodS, 1.0, 2.0);
            // No fetch makes a 3 m/s wind exceed the fully developed sea (0.22 m).
            Assert.InRange(SeaStateModel.Derive(3.0, 1e7).SignificantHeightM, 0.2, 0.23);
        }

        [Fact]
        public void ShelteredBasin_InAGale_RaisesAShortSteepChop()
        {
            var basin = SeaStateModel.Derive(20.0, BasinFetchM);
            var coast = SeaStateModel.Derive(20.0, SeaStateModel.ExposedFetchM);
            Assert.InRange(basin.SignificantHeightM, 0.4, 1.0);
            Assert.True(basin.PeakWavelengthM < 15.0, $"basin peak wavelength {basin.PeakWavelengthM:F1} m");
            Assert.True(coast.SignificantHeightM > 3.0 * basin.SignificantHeightM);
            // Steeper than the coast's swell-like sea.
            Assert.True(basin.SignificantHeightM / basin.PeakWavelengthM > coast.SignificantHeightM / coast.PeakWavelengthM);
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(6.0)]
        [InlineData(12.0)]
        [InlineData(25.0)]
        public void Height_GrowsWithFetchUpToTheFullyDevelopedSea(double wind)
        {
            double previous = 0.0;
            foreach (double fetch in new[] { 500.0, 2_000.0, 10_000.0, 50_000.0, 200_000.0, 1e6, 1e7 })
            {
                double hs = SeaStateModel.Derive(wind, fetch).SignificantHeightM;
                Assert.True(hs >= previous - 1e-12, $"Hs fell from {previous} to {hs} at {fetch} m");
                previous = hs;
            }
            Assert.Equal(0.243 * wind * wind / SeaStateModel.Gravity, previous, 6);
        }

        [Fact]
        public void Height_IsNotProportionalToWind()
        {
            // Fetch-limited Hs grows as U, a fully developed sea as U²: never a
            // fixed wave scaled by the wind.
            double ratioFetchLimited = SeaStateModel.Derive(20.0, 3000.0).SignificantHeightM / SeaStateModel.Derive(10.0, 3000.0).SignificantHeightM;
            double ratioDeveloped = SeaStateModel.Derive(4.0, 1e7).SignificantHeightM / SeaStateModel.Derive(2.0, 1e7).SignificantHeightM;
            Assert.Equal(2.0, ratioFetchLimited, 3);
            Assert.Equal(4.0, ratioDeveloped, 3);
        }

        [Fact]
        public void NoWind_MeansAGlassySea()
        {
            Assert.Equal(0.0, SeaStateModel.Derive(0.0, 10_000.0).SignificantHeightM);
            Assert.All(SeaStateModel.BandAmplitudes(SeaStateModel.Derive(0.0, 10_000.0)), a => Assert.Equal(0.0, a));
        }

        [Theory]
        [InlineData(3.0, 3_000.0)]
        [InlineData(9.0, 3_000.0)]
        [InlineData(16.0, 100_000.0)]
        [InlineData(25.0, 100_000.0)]
        public void BandAmplitudes_CarryTheSignificantHeight(double wind, double fetch)
        {
            var sea = SeaStateModel.Derive(wind, fetch);
            double[] a = SeaStateModel.BandAmplitudes(sea);
            Assert.Equal(SeaStateModel.BandWavelengthsM.Length, a.Length);
            // Σa² = Hs²/8.
            Assert.Equal(sea.SignificantHeightM * sea.SignificantHeightM / 8.0, a.Sum(x => x * x), 9);
            Assert.Equal(sea.SignificantHeightM, SeaStateModel.SignificantHeight(a), 9);
        }

        [Fact]
        public void BandAmplitudes_PeakAtTheBandNearestThePeakWavelength()
        {
            foreach (var (wind, fetch) in new[] { (3.0, 3_000.0), (15.0, 3_000.0), (15.0, 100_000.0), (25.0, 100_000.0) })
            {
                var sea = SeaStateModel.Derive(wind, fetch);
                double[] a = SeaStateModel.BandAmplitudes(sea);
                int strongest = Array.IndexOf(a, a.Max());
                double[] bands = SeaStateModel.BandWavelengthsM;
                int nearest = Enumerable.Range(0, bands.Length)
                    .OrderBy(i => Math.Abs(Math.Log(bands[i] / sea.PeakWavelengthM))).First();
                Assert.True(Math.Abs(strongest - nearest) <= 1,
                    $"U={wind} F={fetch}: peak {sea.PeakWavelengthM:F1} m, strongest band {bands[strongest]} m");
            }
        }

        [Fact]
        public void Bands_AreFixedAndLogSpaced()
        {
            double[] bands = SeaStateModel.BandWavelengthsM;
            Assert.InRange(bands.Length, 6, 8);
            for (int i = 1; i < bands.Length; i++)
                Assert.InRange(bands[i - 1] / bands[i], 1.7, 2.1);
        }

        [Fact]
        public void Whitecaps_FollowMonahan()
        {
            // W = 3.84e-6 U^3.41: about 1 % at 10 m/s, 11 % at 20 m/s, none in a calm.
            Assert.InRange(SeaStateModel.WhitecapFraction(10.0), 0.009, 0.011);
            Assert.InRange(SeaStateModel.WhitecapFraction(20.0), 0.10, 0.12);
            Assert.True(SeaStateModel.WhitecapFraction(3.0) < 0.0003);
            Assert.Equal(0.0, SeaStateModel.WhitecapFraction(0.0));
        }
    }

    /// <summary>
    /// G1-05: the baked bathymetry, shore distance and exposure the opaque
    /// water reads instead of seeing the terrain through itself.
    /// </summary>
    public class WaterBakeTests
    {
        [Fact]
        public void SquaredDistance_MatchesBruteForce()
        {
            const int m = 41;
            var mask = new bool[m * m];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = AtmosphereModel.Hash01(i, 5) < 0.04;
            float[] d = WaterBake.SquaredDistance(mask, m, true);
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < m; c++)
                {
                    float best = float.MaxValue;
                    for (int rr = 0; rr < m; rr++)
                        for (int cc = 0; cc < m; cc++)
                            if (mask[rr * m + cc])
                                best = Math.Min(best, (rr - r) * (rr - r) + (cc - c) * (cc - c));
                    Assert.Equal(best, d[r * m + c], 2);
                }
            }
        }

        [Fact]
        public void ShoreDistance_IsSignedAndAccurateAtAStraightBeach()
        {
            // A plane rising along +X through sea level at x = 103 m.
            const int n = 65;
            const float spacing = 8f;
            var h = new float[n * n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    h[r * n + c] = (c * spacing - 103f) * 0.5f;
            float[] d = WaterBake.ShoreDistance(h, n, spacing, 0f);
            for (int c = 0; c < n; c++)
            {
                float expected = 103f - c * spacing;
                float got = d[32 * n + c];
                Assert.True(Math.Abs(got - expected) <= 2.0f + 0.01f, $"x={c * spacing}: {got} vs {expected}");
                Assert.Equal(Math.Sign(expected), Math.Sign(got));
            }
        }

        [Fact]
        public void DefaultFjord_IsDeepAndShelteredInsideAndOpenOutside()
        {
            var settings = new FjordTerrainSettings { Resolution = 257 };
            var gen = new TerrainGenerator(settings);
            float[] heights = gen.GenerateHeightmap();
            int n = settings.Resolution;
            var bake = WaterBake.Build(heights, n, settings.WorldSizeM, 0f, 0.3f, 1f);
            Assert.Equal(n * n * 4, bake.Rgba.Length);

            float Sample(int channel, float x, float z)
            {
                int c = (int)MathF.Round((x + settings.WorldSizeM / 2) / settings.SpacingM);
                int r = (int)MathF.Round((z + settings.WorldSizeM / 2) / settings.SpacingM);
                return bake.Rgba[(r * n + c) * 4 + channel];
            }

            // Mid-basin on the centreline: deep, far from shore, sheltered.
            float midZ = 0.5f * (settings.MouthZ + settings.HeadZ);
            float cx = gen.CenterX(midZ);
            Assert.InRange(Sample(0, cx, midZ), 120f, 260f);
            Assert.True(Sample(1, cx, midZ) > 150f, $"shore distance {Sample(1, cx, midZ)}");
            Assert.True(Sample(2, cx, midZ) < WaterBake.ShelteredExposure, $"basin exposure {Sample(2, cx, midZ)}");
            // Depth is sea level minus height everywhere.
            for (int i = 0; i < n * n; i += 97)
                Assert.Equal(-heights[i], bake.Rgba[i * 4], 3);
            // Land carries a negative shore distance.
            int land = Enumerable.Range(0, n * n).First(i => heights[i] > 50f);
            Assert.True(bake.Rgba[land * 4 + 1] < 0f);

            // Open water near the world edge beyond the skerries: exposed.
            float edgeZ = -settings.WorldSizeM / 2 + 60f;
            float bestExposure = 0f;
            for (float x = -1800f; x <= 1800f; x += 100f)
                if (Sample(0, x, edgeZ) > 0f)
                    bestExposure = Math.Max(bestExposure, Sample(2, x, edgeZ));
            Assert.True(bestExposure > 0.8f, $"edge exposure {bestExposure}");

            // Every pen slot lies in sheltered water: the open sea reaches it
            // only up a long channel.
            foreach (var slot in gen.GetRecommendedLocations(6))
                Assert.True(Sample(2, slot.X, slot.Z) < WaterBake.ShelteredExposure,
                    $"pen slot ({slot.X:F0}, {slot.Z:F0}) exposure {Sample(2, slot.X, slot.Z)}");

            // The sheltered basin's fetch is a few kilometres, not the open sea's.
            Assert.InRange(bake.ShelteredFetchM, 800.0, 4500.0);
        }

        [Fact]
        public void Bake_IsDeterministic()
        {
            var settings = new FjordTerrainSettings { Resolution = 129 };
            float[] heights = new TerrainGenerator(settings).GenerateHeightmap();
            var a = WaterBake.Build(heights, 129, settings.WorldSizeM, 0f, 0.3f, 1f);
            var b = WaterBake.Build(heights, 129, settings.WorldSizeM, 0f, 0.3f, 1f);
            Assert.Equal(a.Rgba, b.Rgba);
            Assert.Equal(a.ShelteredFetchM, b.ShelteredFetchM);
        }

        [Fact]
        public void HalfBytes_RoundTrip()
        {
            float[] values = { 0f, 1.5f, -3.25f, 190f, -8000f, 0.0625f };
            byte[] bytes = WaterBake.ToHalfBytes(values);
            Assert.Equal(values.Length * 2, bytes.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var half = BitConverter.UInt16BitsToHalf((ushort)(bytes[i * 2] | bytes[i * 2 + 1] << 8));
                Assert.Equal(values[i], (float)half, 2);
            }
        }
    }
}
