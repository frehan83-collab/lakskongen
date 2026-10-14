using System;
using System.Globalization;
using System.Linq;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// Roadmap S2-02, "calendar governance": one calendar helper. Sim day 0 is
    /// Friday 1 January 2027, years have 365 days, months their real lengths,
    /// weeks are ISO-8601 weeks.
    /// </summary>
    public class SimCalendarTests
    {
        private static readonly DateTime Epoch = new(2027, 1, 1);

        [Fact]
        public void YearOne_IsTheReal2027_DayForDay()
        {
            // 2027 is not a leap year, so every sim day of year 1 is a real date.
            for (int day = 0; day < SimCalendar.DaysPerYear; day++)
            {
                var date = Epoch.AddDays(day);
                Assert.Equal(date.Year, SimCalendar.Year(day));
                Assert.Equal(date.Month, SimCalendar.Month(day));
                Assert.Equal(date.Day, SimCalendar.DayOfMonth(day));
                Assert.Equal(date.DayOfYear - 1, SimCalendar.DayOfYear(day));
                Assert.Equal(date.DayOfWeek, SimCalendar.Weekday(day));
                Assert.Equal(ISOWeek.GetWeekOfYear(date), SimCalendar.IsoWeek(day));
            }
        }

        [Fact]
        public void EveryYear_HasRealIsoWeeks_ForItsWeekdayLayout()
        {
            // The weekday moves one day a year (365-day years), so over 14 years
            // every weekday layout of a non-leap year occurs. Each sim year must
            // number its weeks like a real non-leap year that starts on the same
            // weekday and follows a non-leap year (ISO week 1 can start in late
            // December, and the first days of January can belong to the previous
            // year's last week).
            for (int year = 0; year < 14; year++)
            {
                int start = year * SimCalendar.DaysPerYear;
                var weekday = SimCalendar.Weekday(start);
                int realYear = Enumerable.Range(2001, 400).First(y =>
                    !DateTime.IsLeapYear(y) && !DateTime.IsLeapYear(y - 1)
                    && new DateTime(y, 1, 1).DayOfWeek == weekday);
                for (int d = 0; d < SimCalendar.DaysPerYear; d++)
                {
                    var date = new DateTime(realYear, 1, 1).AddDays(d);
                    Assert.True(ISOWeek.GetWeekOfYear(date) == SimCalendar.IsoWeek(start + d),
                        $"sim year {year} day {d}: ISO week {ISOWeek.GetWeekOfYear(date)} (real {realYear}) vs {SimCalendar.IsoWeek(start + d)}");
                    Assert.Equal(date.DayOfWeek, SimCalendar.Weekday(start + d));
                }
            }
        }

        [Fact]
        public void MonthsHaveTheirRealLengths_AndTheYearHas365Days()
        {
            int[] expected = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            Assert.Equal(expected, Enumerable.Range(0, 12).Select(SimCalendar.DaysInMonth));
            Assert.Equal(SimCalendar.DaysPerYear, expected.Sum());
            Assert.Equal(new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 },
                Enumerable.Range(0, 12).Select(SimCalendar.MonthStartDayOfYear));
            // Day 31 is 1 February (a 30-day month would have put it on 2 February).
            Assert.Equal(2, SimCalendar.Month(31));
            Assert.Equal(1, SimCalendar.DayOfMonth(31));
            Assert.Equal(12, SimCalendar.Month(364));
            Assert.Equal(31, SimCalendar.DayOfMonth(364));
            Assert.Equal(1, SimCalendar.Month(365));
            Assert.Equal(2028, SimCalendar.Year(365));
        }

        [Fact]
        public void NegativeDays_WrapIntoTheYearBefore()
        {
            Assert.Equal(364, SimCalendar.DayOfYear(-1));
            Assert.Equal(-1, SimCalendar.YearIndex(-1));
            Assert.Equal(12, SimCalendar.Month(-1));
            Assert.Equal(31, SimCalendar.DayOfMonth(-1));
            Assert.Equal(-365, SimCalendar.YearStartDay(-1));
        }

        [Fact]
        public void AddMonths_KeepsTheDayOfMonth_OrTheMonthsLastDay()
        {
            Assert.Equal(31, SimCalendar.AddMonths(0, 1));                // 1 Jan -> 1 Feb
            Assert.Equal(58, SimCalendar.AddMonths(30, 1));               // 31 Jan -> 28 Feb
            Assert.Equal(365 + 14, SimCalendar.AddMonths(348, 1));        // 15 Dec -> 15 Jan next year
            Assert.Equal(348, SimCalendar.AddMonths(365 + 14, -1));       // and back
            Assert.Equal(3 * SimCalendar.DaysPerYear, SimCalendar.AddMonths(0, 36));
            Assert.Equal(SimCalendar.DaysPerYear, SimCalendar.DaysInMonthsFrom(100, 12));
            Assert.Equal(11, SimCalendar.WholeMonthsBetween(0, 364));
            Assert.Equal(12, SimCalendar.WholeMonthsBetween(0, 365));
            Assert.Equal(0, SimCalendar.WholeMonthsBetween(10, 5));
        }

        [Fact]
        public void PeriodEnds_FallOnTheCalendarsLastDays()
        {
            var monthEnds = Enumerable.Range(0, 365).Where(SimCalendar.IsLastDayOfMonth).ToArray();
            Assert.Equal(new[] { 30, 58, 89, 119, 150, 180, 211, 242, 272, 303, 333, 364 }, monthEnds);
            var quarterEnds = Enumerable.Range(0, 730).Where(SimCalendar.IsLastDayOfQuarter).ToArray();
            // 31 Mar, 30 Jun, 30 Sep, 31 Dec, twice (it was every 90 days: 360 days a year).
            Assert.Equal(new[] { 89, 180, 272, 364, 454, 545, 637, 729 }, quarterEnds);
            Assert.Equal(new[] { 364, 729 }, Enumerable.Range(0, 730).Where(SimCalendar.IsLastDayOfYear));
            Assert.Equal(12, Enumerable.Range(0, 365).Count(SimCalendar.IsFirstDayOfMonth));
        }

        [Fact]
        public void MonthlyAmounts_SpreadOverTheYear_PayTwelveMonths()
        {
            decimal monthly = 45_000m;
            decimal year = SimCalendar.MonthlyToDaily(monthly) * SimCalendar.DaysPerYear;
            Assert.Equal(12m * monthly, Math.Round(year, 6));
            Assert.Equal(SimCalendar.DaysPerYear / 12.0, SimCalendar.AverageDaysPerMonth, 12);
        }

        [Fact]
        public void MonthMiddles_SitHalfwayThroughTheirMonths()
        {
            Assert.Equal(15.0, SimCalendar.MonthMidDayOfYear(0));    // 16 January (0-based day 15)
            Assert.Equal(44.5, SimCalendar.MonthMidDayOfYear(1));    // 14-15 February
            Assert.Equal(349.0, SimCalendar.MonthMidDayOfYear(11));  // 16 December
        }
    }
}
