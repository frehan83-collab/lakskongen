using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G3-04: the underwater view's fog follows the sim's visibility, and a
    /// bloom's visibility is below a clear day's. <see cref="WaterClarity"/> is
    /// the horizontal sighting range of a black target (y = 4.8 / α, Zaneveld
    /// &amp; Pegau 2003) from a fjord's measured Secchi cycle and, at a site with
    /// an algal bloom, the attenuation its chlorophyll adds (Voss 1992). The
    /// engine's read is pure.
    /// </summary>
    public class WaterClarityTests
    {
        /// <summary>Sim day of mid-month <paramref name="monthIndex"/> (0 = January) in the first year.</summary>
        private static int MidMonth(int monthIndex) => (int)Math.Round(SimCalendar.MonthMidDayOfYear(monthIndex));

        [Fact]
        public void Visibility_TimesAttenuation_IsTheBlackTargetConstant()
        {
            foreach (int day in new[] { 0, 45, 120, 200, 300, 364, 365 + 180 })
            {
                foreach (double severity in new[] { 0.0, 0.3, 1.0 })
                {
                    var water = WaterClarity.Derive(day, severity);
                    Assert.Equal(4.8, water.VisibilityM * water.AttenuationPerM, 9);
                }
            }
        }

        [Fact]
        public void TheSeasonSitsOnTheMeasuredSecchiCycle_AtMidMonth()
        {
            // OH-10 Hardangerfjorden 2024, three-month running mean, times the
            // horizontal-per-Secchi ratio 0.75.
            double[] secchi = { 15.7, 14.3, 11.3, 8.3, 7.0, 7.3, 8.2, 7.8, 7.2, 6.0, 9.0, 12.3 };
            for (int m = 0; m < 12; m++)
            {
                double doy = SimCalendar.MonthMidDayOfYear(m);
                if (Math.Abs(doy - Math.Round(doy)) > 1e-9)
                    continue; // only months whose middle falls on a whole day
                Assert.Equal(secchi[m], WaterClarity.SeasonalSecchiM((int)doy), 9);
                Assert.Equal(secchi[m] * 0.75, WaterClarity.SeasonalVisibilityM((int)doy), 9);
            }
        }

        [Fact]
        public void WinterWater_IsClearerThanSummerWater()
        {
            double winter = WaterClarity.SeasonalVisibilityM(MidMonth(0));
            double summer = (WaterClarity.SeasonalVisibilityM(MidMonth(5)) + WaterClarity.SeasonalVisibilityM(MidMonth(6))
                + WaterClarity.SeasonalVisibilityM(MidMonth(7))) / 3.0;
            // January 15.7 m Secchi (11.8 m), summer 7.8 m (5.8 m): the clear
            // winter water a diver knows, the summer's plankton and runoff.
            Assert.InRange(winter, 10.0, 13.0);
            Assert.InRange(summer, 5.0, 7.0);
            Assert.True(winter > 1.8 * summer, $"winter {winter:F1} m vs summer {summer:F1} m");
        }

        [Fact]
        public void TheSeasonRunsSmoothlyAcrossTheYear_AndRepeatsEveryYear()
        {
            double worstStep = 0.0;
            for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day++)
            {
                worstStep = Math.Max(worstStep, Math.Abs(WaterClarity.SeasonalVisibilityM(day + 1) - WaterClarity.SeasonalVisibilityM(day)));
                Assert.Equal(WaterClarity.SeasonalVisibilityM(day), WaterClarity.SeasonalVisibilityM(day + SimCalendar.DaysPerYear), 12);
            }
            // The steepest stretches (December to January, March to April:
            // about 3 m of Secchi in a month) move the visibility under 0.1 m
            // a day; New Year's Eve is no step: it moves as the days round it do.
            Assert.True(worstStep < 0.1, $"largest day-to-day step {worstStep:F3} m");
            int eve = SimCalendar.DaysPerYear - 1;
            double newYear = Math.Abs(WaterClarity.SeasonalVisibilityM(eve + 1) - WaterClarity.SeasonalVisibilityM(eve));
            double dayBefore = Math.Abs(WaterClarity.SeasonalVisibilityM(eve) - WaterClarity.SeasonalVisibilityM(eve - 1));
            double dayAfter = Math.Abs(WaterClarity.SeasonalVisibilityM(eve + 2) - WaterClarity.SeasonalVisibilityM(eve + 1));
            Assert.True(newYear <= 1.05 * Math.Max(dayBefore, dayAfter), $"new year {newYear:F4} m vs {dayBefore:F4} / {dayAfter:F4} m");
        }

        [Fact]
        public void ChlorophyllAttenuation_IsVossCarriedToPhotopicLight()
        {
            // c(490) - c_w(490) = 0.39 Chl^0.57 (Voss 1992), x 0.952 to 532 nm,
            // x 0.9 to photopic attenuation (Zaneveld & Pegau 2003).
            Assert.Equal(0.9 * 0.952 * 0.39, WaterClarity.ChlorophyllAttenuation, 3);
            Assert.Equal(0.334, WaterClarity.ChlorophyllAttenuationPerM(1.0), 9);
            Assert.Equal(0.334 * Math.Pow(10.0, 0.57), WaterClarity.ChlorophyllAttenuationPerM(10.0), 9);
            Assert.Equal(0.0, WaterClarity.ChlorophyllAttenuationPerM(0.0));
            Assert.Equal(0.0, WaterClarity.ChlorophyllAttenuationPerM(-1.0));
        }

        [Theory]
        [InlineData(15)]
        [InlineData(166)]
        [InlineData(258)]
        public void ABloom_ClosesTheView_MoreTheWorseItIs(int day)
        {
            var clear = WaterClarity.Derive(day, 0.0);
            Assert.Equal(clear.SeasonalVisibilityM, clear.VisibilityM, 9);
            Assert.Equal(0.0, clear.BloomChlorophyllUgL);
            double previous = clear.VisibilityM;
            foreach (double severity in new[] { 0.1, 0.3, 0.6, 0.8, 1.0 })
            {
                var bloom = WaterClarity.Derive(day, severity);
                Assert.True(bloom.VisibilityM < previous, $"day {day} severity {severity}: {bloom.VisibilityM:F2} m not below {previous:F2} m");
                Assert.Equal(severity * 20.0, bloom.BloomChlorophyllUgL, 9);
                previous = bloom.VisibilityM;
            }
            // A full harmful bloom (20 ug/L) leaves a couple of metres even in
            // the clear winter water; the harness's QA bloom (0.8) under 3 m in summer.
            Assert.InRange(WaterClarity.Derive(day, 1.0).VisibilityM, 1.2, 2.6);
            Assert.True(WaterClarity.Derive(MidMonth(6), 0.8).VisibilityM < 3.0);
            // Out-of-range severities are clamped.
            Assert.Equal(WaterClarity.Derive(day, 1.0), WaterClarity.Derive(day, 3.0));
            Assert.Equal(WaterClarity.Derive(day, 0.0), WaterClarity.Derive(day, -1.0));
        }

        private static SimulationEngine Started(int seed)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000m) };
            engine.Initialize(enableEvents: false, seed: seed);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            engine.State.RandomSeed = seed;
            return engine;
        }

        private static string Digest(SimulationEngine engine)
        {
            engine.PrepareForSave();
            var text = new StringBuilder(JsonSerializer.Serialize(engine.State, SaveJson.Options));
            foreach (var kv in engine.Rng.CaptureStates().OrderBy(k => k.Key, StringComparer.Ordinal))
                text.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
            return text.ToString();
        }

        [Fact]
        public void TheEngine_ReadsTheSiteBloom_AndOnlyAlgaeBloomsAtThatSite()
        {
            var engine = Started(20261012);
            var site = engine.State.SeaSites.First();
            var other = engine.State.SeaSites.First(s => s.Id != site.Id);
            int day = engine.State.Day;
            var clear = engine.WaterClarityOf(site);
            Assert.Equal(WaterClarity.Derive(day, 0.0), clear);

            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "jelly", Kind = "jellyfish", SiteId = site.Id, Severity01 = 0.9, StartDay = day, DurationDays = 10 });
            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "hypoxia", Kind = "hypoxia", SiteId = site.Id, Severity01 = 0.9, StartDay = day, DurationDays = 10 });
            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "elsewhere", Kind = "algae", SiteId = other.Id, Severity01 = 0.9, StartDay = day, DurationDays = 10 });
            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "over", Kind = "algae", SiteId = site.Id, Severity01 = 0.9, StartDay = day - 30, DurationDays = 10 });
            Assert.Equal(clear, engine.WaterClarityOf(site));

            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "light", Kind = "algae", SiteId = site.Id, Severity01 = 0.3, StartDay = day, DurationDays = 21 });
            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "heavy", Kind = "Algae", SiteId = site.Id, Severity01 = 0.8, StartDay = day, DurationDays = 21 });
            var bloom = engine.WaterClarityOf(site);
            Assert.Equal(WaterClarity.Derive(day, 0.8), bloom);
            Assert.True(bloom.VisibilityM < clear.VisibilityM);
            Assert.True(engine.WaterClarityOf(other).VisibilityM < clear.VisibilityM);
        }

        [Fact]
        public void TheEngineRead_IsPure()
        {
            var engine = Started(7);
            var site = engine.State.SeaSites.First();
            engine.State.ActiveBlooms.Add(new BloomEvent { Id = "qa", Kind = "algae", SiteId = site.Id, Severity01 = 0.8, StartDay = engine.State.Day, DurationDays = 21 });
            string before = Digest(engine);
            for (int i = 0; i < 5; i++)
                foreach (var s in engine.State.SeaSites)
                    engine.WaterClarityOf(s);
            Assert.Equal(before, Digest(engine));
        }
    }
}
