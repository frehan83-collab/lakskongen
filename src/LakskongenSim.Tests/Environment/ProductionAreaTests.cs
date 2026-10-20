using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// Roadmap S2-02: the 13 production areas and their offline data (sea
    /// temperature climatology, lice spring weeks, PD zone, licence size).
    /// </summary>
    public class ProductionAreaTests
    {
        /// <summary>
        /// Havforskningsinstituttet's normal (2012-2024, NorKyst v3) winter and
        /// summer temperature per area, Rapport fra havforskningen 2025-69 section 4,
        /// range midpoints. Kept here independently of the engine's table.
        /// </summary>
        private static readonly (double Winter, double Summer)[] HiNormals =
        {
            (3.5, 17.0), (4.5, 15.5), (5.5, 15.5), (5.5, 15.5), (5.5, 15.0), (6.0, 14.5), (5.5, 13.5),
            (5.0, 13.5), (5.0, 12.5), (4.5, 13.0), (4.0, 11.5), (4.0, 11.5), (4.0, 11.0),
        };

        [Fact]
        public void ThereAreThirteenAreas_WithTheirOfficialNames()
        {
            Assert.Equal(13, ProductionAreas.All.Count);
            Assert.Equal(Enumerable.Range(1, 13), ProductionAreas.All.Select(a => a.Number));
            Assert.Equal("Svenskegrensen til Jæren", ProductionAreas.Get(1).Name);
            Assert.Equal("Karmøy til Sotra", ProductionAreas.Get(3).Name);
            Assert.Equal("Nordmøre og Sør-Trøndelag", ProductionAreas.Get(6).Name);
            Assert.Equal("Vestfjorden og Vesterålen", ProductionAreas.Get(9).Name);
            Assert.Equal("Øst-Finnmark", ProductionAreas.Get(13).Name);
            Assert.Equal("PO4", ProductionAreas.Get(4).Code);
            Assert.False(ProductionAreas.IsValid(0));
            Assert.False(ProductionAreas.IsValid(14));
            Assert.True(ProductionAreas.HasClimatology(ProductionAreas.NationalReference));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProductionAreas.Get(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProductionAreas.Get(14));
        }

        [Fact]
        public void EachClimatology_SpansHavforskningsinstituttetsNormalWinterToSummer()
        {
            for (int po = 1; po <= 13; po++)
            {
                var monthly = ProductionAreas.Get(po).SeaTemperatureMonthlyC;
                Assert.Equal(12, monthly.Count);
                Assert.Equal(HiNormals[po - 1].Winter, monthly.Min(), 2);
                Assert.Equal(HiNormals[po - 1].Summer, monthly.Max(), 2);
                // Coldest in February-April, warmest in July-August, as at every station.
                int coldest = Enumerable.Range(0, 12).OrderBy(m => monthly[m]).First();
                int warmest = Enumerable.Range(0, 12).OrderByDescending(m => monthly[m]).First();
                Assert.InRange(coldest, 1, 3);
                Assert.InRange(warmest, 6, 7);
            }
        }

        [Fact]
        public void TheSouth_IsWarmerInSummer_AndTheFarNorthColderAllYear()
        {
            double August(int po) => ProductionAreas.Get(po).SeaTemperatureMonthlyC[7];
            double Mean(int po) => ProductionAreas.Get(po).SeaTemperatureMonthlyC.Average();
            Assert.True(August(1) - August(13) >= 5.0, $"PO1 {August(1)} vs PO13 {August(13)}");
            Assert.True(Mean(4) - Mean(13) >= 2.5, $"PO4 {Mean(4):F2} vs PO13 {Mean(13):F2}");
            for (int po = 2; po <= 6; po++)
                Assert.True(Mean(po) > Mean(12), $"PO{po} {Mean(po):F2} vs PO12 {Mean(12):F2}");
        }

        [Fact]
        public void DailyClimatology_HitsTheMonthlyMeansAtMidMonth_AndRunsSmoothlyThroughNewYear()
        {
            for (int po = 0; po <= 13; po++)
            {
                for (int m = 0; m < 12; m++)
                {
                    double mid = SimCalendar.MonthMidDayOfYear(m);
                    if (mid != Math.Floor(mid)) continue; // February's middle falls between two days
                    double expected = po == 0
                        ? new[] { 4.5, 3.8, 4.2, 5.5, 8.0, 10.5, 12.5, 13.5, 12.0, 9.5, 7.0, 5.5 }[m]
                        : ProductionAreas.Get(po).SeaTemperatureMonthlyC[m];
                    Assert.Equal(expected, ProductionAreas.ClimatologyC(po, (int)mid), 9);
                }
                double maxStep = 0.0;
                for (int day = -10; day < 2 * SimCalendar.DaysPerYear; day++)
                    maxStep = Math.Max(maxStep, Math.Abs(ProductionAreas.ClimatologyC(po, day + 1) - ProductionAreas.ClimatologyC(po, day)));
                Assert.True(maxStep < 0.15, $"area {po}: day-to-day step {maxStep:F3} °C");
                Assert.Equal(ProductionAreas.ClimatologyC(po, 10), ProductionAreas.ClimatologyC(po, 10 + SimCalendar.DaysPerYear), 12);
            }
        }

        [Fact]
        public void ThePdZone_IsPO2ToPO6()
        {
            var inZone = ProductionAreas.All.Where(a => a.InPdZone).Select(a => a.Number);
            Assert.Equal(new[] { 2, 3, 4, 5, 6 }, inZone);
        }

        [Fact]
        public void Licences_Hold945Tonnes_InTromsAndFinnmark_And780Elsewhere()
        {
            for (int po = 1; po <= 13; po++)
                Assert.Equal(po >= 10 ? 945 : 780, ProductionAreas.Get(po).LicenceMtbTonnes);
        }

        /// <summary>
        /// Roadmap S2-02 acceptance 4, the spring window per region: 0.2 adult
        /// female lice from ISO week 16 to 21 up to Nord-Trøndelag (PO1-PO7) and
        /// from week 21 to 26 in Nordland, Troms and Finnmark (PO8-PO13), 0.5
        /// otherwise (luseforskriften § 8). Every day of two sim years.
        /// </summary>
        [Fact]
        public void TheLiceSpringWindow_FollowsEachRegionsIsoWeeks()
        {
            for (int po = 1; po <= 13; po++)
            {
                bool north = po >= 8;
                for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day++)
                {
                    int week = SimCalendar.IsoWeek(day);
                    bool spring = north ? week >= 21 && week <= 26 : week >= 16 && week <= 21;
                    Assert.True(spring == ProductionAreas.IsLiceSpringPeriod(po, day), $"PO{po} day {day} week {week}");
                    Assert.Equal(spring ? 0.2 : 0.5, ProductionAreas.LiceLimitAdultFemales(po, day));
                }
            }
            // Week 21 is the one week both rules share; week 18 is south-only, week 24 north-only.
            int week18 = Enumerable.Range(0, 365).First(d => SimCalendar.IsoWeek(d) == 18);
            int week24 = Enumerable.Range(0, 365).First(d => SimCalendar.IsoWeek(d) == 24);
            Assert.True(ProductionAreas.IsLiceSpringPeriod(4, week18));
            Assert.False(ProductionAreas.IsLiceSpringPeriod(9, week18));
            Assert.False(ProductionAreas.IsLiceSpringPeriod(4, week24));
            Assert.True(ProductionAreas.IsLiceSpringPeriod(9, week24));
        }

        [Fact]
        public void TheNationalReference_KeepsTheOldSpringWeeks()
        {
            for (int day = 0; day < SimCalendar.DaysPerYear; day++)
            {
                int week = SimCalendar.IsoWeek(day);
                Assert.Equal(week >= 16 && week <= 21, ProductionAreas.IsLiceSpringPeriod(ProductionAreas.NationalReference, day));
            }
        }
    }
}
