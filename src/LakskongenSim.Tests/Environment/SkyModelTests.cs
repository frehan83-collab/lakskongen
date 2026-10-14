using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G1-04: the sun, the moon and the aurora follow the sim calendar and the
    /// scenario's latitude (D20). The 3D sky used a seasonal "daylight hours"
    /// table and drew its sun disc from its own formula, so polar night,
    /// midnight sun and the noon height were all invented.
    /// </summary>
    public class SolarGeometryTests
    {
        private const double Bodo = 67.28;
        private const double Bergen = 60.39;
        private const int Dec21 = 355;
        private const int Jun21 = 172;

        [Fact]
        public void Bodo_HasPolarNightAtTheWinterSolstice()
        {
            double noon = SolarGeometry.NoonElevationDeg(Bodo, Dec21);
            Assert.True(noon < 0.0, $"Bodø noon elevation on 21 Dec is {noon:F2}°");
            // Every hour of the day is below the horizon.
            for (double h = 0; h < 24; h += 0.25)
                Assert.True(SolarGeometry.Sun(Bodo, Dec21, h).ElevationDeg < 0.0, $"sun up at {h:F2} h");
        }

        [Fact]
        public void Bodo_HasMidnightSunAtTheSummerSolstice()
        {
            double midnight = SolarGeometry.MidnightElevationDeg(Bodo, Jun21);
            Assert.True(midnight > 0.0, $"Bodø midnight elevation on 21 Jun is {midnight:F2}°");
            for (double h = 0; h < 24; h += 0.25)
                Assert.True(SolarGeometry.Sun(Bodo, Jun21, h).ElevationDeg > 0.0, $"sun down at {h:F2} h");
        }

        [Fact]
        public void Bergen_NoonAtTheSummerSolsticeIs53Degrees()
        {
            double noon = SolarGeometry.NoonElevationDeg(Bergen, Jun21);
            Assert.InRange(noon, 52.0, 54.0);
        }

        [Fact]
        public void Declination_CrossesZeroAtTheEquinoxesAndPeaksAtTheSolstices()
        {
            Assert.InRange(SolarGeometry.DeclinationDeg(80), -0.6, 0.6);   // 21 March
            Assert.InRange(SolarGeometry.DeclinationDeg(266), -0.6, 0.6);  // 23 September
            Assert.InRange(SolarGeometry.DeclinationDeg(Jun21), 23.2, 23.6);
            Assert.InRange(SolarGeometry.DeclinationDeg(Dec21), -23.6, -23.2);
        }

        [Fact]
        public void Sun_RisesInTheEastStandsSouthAtNoonAndSetsInTheWest()
        {
            // Bergen at the March equinox: sunrise near 06:00 due east.
            var noon = SolarGeometry.Sun(Bergen, 80, 12.0);
            Assert.InRange(noon.AzimuthDeg, 179.5, 180.5);
            var morning = SolarGeometry.Sun(Bergen, 80, 6.0);
            Assert.InRange(morning.AzimuthDeg, 80.0, 100.0);
            Assert.InRange(morning.ElevationDeg, -1.5, 1.5);
            var evening = SolarGeometry.Sun(Bergen, 80, 18.0);
            Assert.InRange(evening.AzimuthDeg, 260.0, 280.0);
        }

        [Fact]
        public void Sun_MovesSmoothlyThroughTheDay()
        {
            var last = SolarGeometry.Sun(Bergen, 200, 0.0);
            for (int minute = 1; minute <= 24 * 60; minute++)
            {
                var now = SolarGeometry.Sun(Bergen, 200, minute / 60.0);
                Assert.True(Math.Abs(now.ElevationDeg - last.ElevationDeg) < 0.3, $"elevation jump at minute {minute}");
                last = now;
            }
        }

        [Fact]
        public void Direction_IsAUnitVectorAtTheElevation()
        {
            var p = SolarGeometry.Sun(Bergen, 120, 15.5);
            var (n, up, e) = p.Direction();
            Assert.Equal(1.0, Math.Sqrt(n * n + up * up + e * e), 9);
            Assert.Equal(Math.Sin(p.ElevationDeg * Math.PI / 180.0), up, 9);
            // Afternoon: the sun is west of south, so the east component is negative.
            Assert.True(e < 0.0);
        }

        [Fact]
        public void DayOfYear_StartsOnTheFirstOfJanuaryAndWraps()
        {
            Assert.Equal(1, SolarGeometry.DayOfYear(0));
            Assert.Equal(365, SolarGeometry.DayOfYear(364));
            Assert.Equal(1, SolarGeometry.DayOfYear(365));
            Assert.Equal(172, SolarGeometry.DayOfYear(365 * 3 + 171));
        }

        [Fact]
        public void Moon_IsFullOnTheThirdAndNewOnTheEighteenthOfJanuary2026()
        {
            // Almanac: full moon 2026-01-03 10:03 UTC, new moon 2026-01-18 19:52 UTC.
            var full = SolarGeometry.Moon(Bodo, 2.0, 10.0);
            Assert.True(full.IlluminatedFraction > 0.98, $"lit {full.IlluminatedFraction:F3}");
            var dark = SolarGeometry.Moon(Bodo, 17.0, 20.0);
            Assert.True(dark.IlluminatedFraction < 0.02, $"lit {dark.IlluminatedFraction:F3}");
            Assert.True(SolarGeometry.Moon(Bodo, 10.0, 12.0).Waxing == false);
            Assert.True(SolarGeometry.Moon(Bodo, 24.0, 12.0).Waxing);
        }

        [Fact]
        public void Moon_RepeatsItsPhasesEverySynodicMonth()
        {
            var fullDays = new List<double>();
            double prev = 0.0, prevPrev = 0.0;
            for (double day = 0; day < 365; day += 0.05)
            {
                double lit = SolarGeometry.Moon(Bergen, day, 0.0).IlluminatedFraction;
                if (prev > lit && prev > prevPrev && prev > 0.95)
                    fullDays.Add(day - 0.05);
                prevPrev = prev;
                prev = lit;
            }
            Assert.InRange(fullDays.Count, 12, 13);
            for (int i = 1; i < fullDays.Count; i++)
                Assert.InRange(fullDays[i] - fullDays[i - 1], 28.9, 30.1);
        }

        [Fact]
        public void Moon_FullWinterMoonRidesHighInTheSouthAtMidnight()
        {
            // 3 January 2026, solar midnight in Bodø: opposite the low winter sun.
            var moon = SolarGeometry.Moon(Bodo, 3.0, 0.0);
            Assert.InRange(moon.Position.AzimuthDeg, 155.0, 205.0);
            Assert.True(moon.Position.ElevationDeg > 25.0, $"elevation {moon.Position.ElevationDeg:F1}");
        }

        [Fact]
        public void RegionLatitudes_RunSouthToNorth()
        {
            Assert.Equal(Bodo, RegionGeography.LatitudeDeg(ProductionRegion.Nordland));
            Assert.Equal(Bergen, RegionGeography.LatitudeDeg(ProductionRegion.Vestland));
            var order = new[]
            {
                ProductionRegion.Sørlandet, ProductionRegion.Rogaland, ProductionRegion.Vestland,
                ProductionRegion.MøreOgRomsdal, ProductionRegion.Trøndelag, ProductionRegion.Nordland,
                ProductionRegion.Troms, ProductionRegion.Finnmark,
            };
            for (int i = 1; i < order.Length; i++)
                Assert.True(RegionGeography.LatitudeDeg(order[i]) > RegionGeography.LatitudeDeg(order[i - 1]), order[i].ToString());
            foreach (ProductionRegion r in Enum.GetValues(typeof(ProductionRegion)))
                Assert.InRange(RegionGeography.LatitudeDeg(r), 57.9, 71.2);
        }
    }

    /// <summary>
    /// G1-02: the visibility the fog is derived from (Koschmieder). The FJORD
    /// focus is 900 m from the camera: clear days must leave it at least 85 %
    /// visible, storms at most 35 %.
    /// </summary>
    public class AtmosphereModelTests
    {
        private const double FjordFocusKm = 0.9;

        private static double Transmittance(AtmosphereConditions a, double km)
            => Math.Exp(-AtmosphereModel.ExtinctionPerKm(a.VisibilityKm) * km);

        [Fact]
        public void ClearDays_SeeTheFjordFocus()
        {
            for (int day = 0; day < 365 * 2; day++)
            {
                var a = AtmosphereModel.Derive(WeatherState.Calm, 3.0, 8.0, day, 60.39);
                if (a.SeaFog01 > 0.0)
                    continue;
                Assert.True(a.VisibilityKm >= 30.0, $"day {day}: {a.VisibilityKm:F1} km");
                Assert.True(Transmittance(a, FjordFocusKm) >= 0.85, $"day {day}");
                Assert.InRange(a.CloudCover01, 0.0, 0.45);
                Assert.Equal(0.0, a.PrecipitationMmPerHour);
            }
        }

        [Theory]
        [InlineData(13.0, 60.39)]
        [InlineData(17.0, 60.39)]
        [InlineData(15.0, 67.28)]
        public void Storms_HideMostOfTheFjordFocus(double windMs, double latitude)
        {
            for (int day = 0; day < 365; day++)
            {
                var a = AtmosphereModel.Derive(WeatherState.Storm, windMs, 7.0, day, latitude);
                Assert.True(Transmittance(a, FjordFocusKm) <= 0.35, $"day {day}: V {a.VisibilityKm:F2} km");
                Assert.True(a.CloudCover01 >= 0.95);
                Assert.True(a.PrecipitationMmPerHour >= 4.0);
            }
        }

        [Fact]
        public void SevereStorms_AreThickerThanStorms()
        {
            double storm = Enumerable.Range(0, 365).Average(d => AtmosphereModel.Derive(WeatherState.Storm, 15.0, 7.0, d, 63.4).VisibilityKm);
            double severe = Enumerable.Range(0, 365).Average(d => AtmosphereModel.Derive(WeatherState.Severe, 25.0, 7.0, d, 63.4).VisibilityKm);
            double moderate = Enumerable.Range(0, 365).Average(d => AtmosphereModel.Derive(WeatherState.Moderate, 7.0, 7.0, d, 63.4).VisibilityKm);
            Assert.True(severe < storm && storm < moderate, $"severe {severe:F2} storm {storm:F2} moderate {moderate:F2}");
            Assert.InRange(severe, 0.3, 1.5);
        }

        [Fact]
        public void NordlandWinterShowersFallAsSnow_VestlandSummerAsRain()
        {
            int snowDays = 0, rainDays = 0;
            for (int day = 0; day < 40; day++)
            {
                var winter = AtmosphereModel.Derive(WeatherState.Storm, 15.0, 4.5, day, 67.28);
                if (winter.Snow) snowDays++;
                var summer = AtmosphereModel.Derive(WeatherState.Storm, 15.0, 12.5, 180 + day, 60.39);
                if (!summer.Snow) rainDays++;
            }
            Assert.Equal(40, snowDays);
            Assert.Equal(40, rainDays);
        }

        [Fact]
        public void SeaFog_IsASummerCalmWeatherLayer()
        {
            int fogDays = 0;
            for (int day = 0; day < 365 * 6; day++)
            {
                int doy = SolarGeometry.DayOfYear(day);
                var calm = AtmosphereModel.Derive(WeatherState.Calm, 3.0, 9.0, day, 62.0);
                if (calm.SeaFog01 > 0.0)
                {
                    fogDays++;
                    Assert.InRange(doy, 75, 255);
                    Assert.InRange(calm.VisibilityKm, 0.2, 0.8);
                }
                Assert.Equal(0.0, AtmosphereModel.Derive(WeatherState.Storm, 15.0, 9.0, day, 62.0).SeaFog01);
            }
            // About 4-10 % of calm days across the year.
            Assert.InRange(fogDays, 365 * 6 * 0.02, 365 * 6 * 0.10);
        }

        [Fact]
        public void Derive_IsDeterministicAndHashIsInRange()
        {
            for (int day = 0; day < 500; day++)
            {
                Assert.Equal(
                    AtmosphereModel.Derive(WeatherState.Moderate, 7.0, 6.0, day, 63.4),
                    AtmosphereModel.Derive(WeatherState.Moderate, 7.0, 6.0, day, 63.4));
                double h = AtmosphereModel.Hash01(day, 3);
                Assert.InRange(h, 0.0, 0.9999999999);
            }
            Assert.NotEqual(AtmosphereModel.Hash01(1, 1), AtmosphereModel.Hash01(2, 1));
            Assert.NotEqual(AtmosphereModel.Hash01(1, 1), AtmosphereModel.Hash01(1, 2));
        }

        [Fact]
        public void ExtinctionIsKoschmieder()
        {
            Assert.Equal(1.0e-4, AtmosphereModel.ExtinctionPerKm(40.0) / 1000.0, 5);
            Assert.Equal(1.3e-2, AtmosphereModel.ExtinctionPerKm(0.3) / 1000.0, 3);
        }
    }

    /// <summary>
    /// G1-04: aurora strength follows geomagnetic activity and latitude; the
    /// renderer adds darkness and a clear sky.
    /// </summary>
    public class AuroraModelTests
    {
        [Fact]
        public void Kp_IsBoundedSmoothAndClimatological()
        {
            var kps = new List<double>();
            double last = AuroraModel.Kp(0);
            for (double day = 0; day < 3000; day += 0.25)
            {
                double kp = AuroraModel.Kp(day);
                Assert.InRange(kp, 0.0, 9.0);
                Assert.True(Math.Abs(kp - last) < 2.5, $"jump at {day}");
                last = kp;
                kps.Add(kp);
            }
            Assert.InRange(kps.Average(), 1.4, 2.2);
            double storms = kps.Count(k => k >= 5.0) / (double)kps.Count;
            Assert.InRange(storms, 0.01, 0.10);
        }

        [Fact]
        public void TheNorthSeesMoreAuroraThanTheSouth()
        {
            double lofoten = 0, bodo = 0, bergen = 0;
            int lofotenNights = 0, bergenQuietDark = 0, quiet = 0;
            for (int day = 0; day < 1000; day++)
            {
                double l = AuroraModel.At(68.2, day).Brightness01;
                lofoten += l;
                if (l >= 0.2) lofotenNights++;
                bodo += AuroraModel.At(67.28, day).Brightness01;
                var b = AuroraModel.At(60.39, day);
                bergen += b.Brightness01;
                if (b.Kp < 2.0)
                {
                    quiet++;
                    if (b.Brightness01 < 0.1) bergenQuietDark++;
                }
            }
            // Lofoten and Bodø both sit under the oval on most nights; Bergen only in storms.
            Assert.True(lofoten >= bodo * 0.98 && bodo > bergen * 2.0, $"lofoten {lofoten:F3} bodø {bodo:F3} bergen {bergen:F3}");
            Assert.True(lofotenNights >= 800, $"{lofotenNights} Lofoten nights of 1000 with a visible display");
            Assert.Equal(quiet, bergenQuietDark);
        }

        [Fact]
        public void StrongStormsReachBergenOverhead()
        {
            // Kp 5: the arc sits within a degree of Bergen's geomagnetic latitude.
            for (int day = 0; day < 3000; day++)
            {
                var a = AuroraModel.At(60.39, day);
                if (a.Kp >= 5.0)
                {
                    Assert.True(a.OvalOffsetKm < 250.0, $"day {day}: arc {a.OvalOffsetKm:F0} km north");
                    Assert.True(a.Brightness01 > 0.5);
                }
            }
        }
    }
}
