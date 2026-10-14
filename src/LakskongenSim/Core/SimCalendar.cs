using System;

namespace LakskongenSim.Core
{
    /// <summary>
    /// The one simulation calendar (roadmap S2-02, "calendar governance"). Every
    /// system that asks "which month, which week, which day of the year" asks
    /// here: weather, sea temperature, lice regulation, market seasonality,
    /// finance cadences, events and the UI's date (godot SimFormatters mirrors
    /// <see cref="StartYear"/> and the month lengths; a pytest keeps them equal).
    ///
    /// <para>
    /// Sim day 0 is Friday 1 January <see cref="StartYear"/>. Years have 365 days
    /// (no leap days), months their real Gregorian lengths, and the weekday
    /// advances one day a year, as it does between real non-leap years. Weeks
    /// are ISO-8601 weeks (Monday first; week 1 holds the year's first
    /// Thursday), which is what Norwegian regulation counts in (luseforskriften
    /// § 8 "uke 16 til og med uke 21"). Sim year 1 is day for day the real 2027.
    /// </para>
    ///
    /// <para>
    /// Before S2-02 the code base carried three calendars: 30-day months
    /// (<c>day / 30</c>) in the weather and the market, 365-day years elsewhere
    /// and a 7-day "week of year" from <c>day % 365 / 7</c> in the lice rule.
    /// </para>
    /// </summary>
    public static class SimCalendar
    {
        /// <summary>Calendar year of sim day 0 (the UI shows "1. jan. 2027").</summary>
        public const int StartYear = 2027;

        /// <summary>Days in every sim year (the sim has no leap days).</summary>
        public const int DaysPerYear = 365;

        /// <summary>Days in a week.</summary>
        public const int DaysPerWeek = 7;

        /// <summary>Months in a year.</summary>
        public const int MonthsPerYear = 12;

        /// <summary>Mean month length (365 / 12 days), for rates quoted per month.</summary>
        public const double AverageDaysPerMonth = (double)DaysPerYear / MonthsPerYear;

        /// <summary>Weekday of sim day 0, 1 January 2027: a Friday.</summary>
        public const DayOfWeek Day0Weekday = DayOfWeek.Friday;

        private static readonly int[] MonthLengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>Day of year (0-based) on which each month starts.</summary>
        private static readonly int[] MonthStarts = BuildMonthStarts();

        /// <summary>Month index (0 = January) of each day of the year.</summary>
        private static readonly byte[] MonthOfDay = BuildMonthOfDay();

        private static int[] BuildMonthStarts()
        {
            var starts = new int[MonthsPerYear];
            int day = 0;
            for (int m = 0; m < MonthsPerYear; m++)
            {
                starts[m] = day;
                day += MonthLengths[m];
            }
            return starts;
        }

        private static byte[] BuildMonthOfDay()
        {
            var months = new byte[DaysPerYear];
            for (int m = 0; m < MonthsPerYear; m++)
                for (int d = 0; d < MonthLengths[m]; d++)
                    months[MonthStarts[m] + d] = (byte)m;
            return months;
        }

        /// <summary>Floor division that rounds toward minus infinity (negative days stay in year -1).</summary>
        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>Day of the year, 0 (1 January) to 364 (31 December). Negative days wrap backwards.</summary>
        public static int DayOfYear(int day)
        {
            int r = day % DaysPerYear;
            return r < 0 ? r + DaysPerYear : r;
        }

        /// <summary>Zero-based year index of a sim day (day 0-364 is year 0).</summary>
        public static int YearIndex(int day) => FloorDiv(day, DaysPerYear);

        /// <summary>Calendar year of a sim day (<see cref="StartYear"/> for days 0-364).</summary>
        public static int Year(int day) => StartYear + YearIndex(day);

        /// <summary>First sim day of the year that holds <paramref name="day"/>.</summary>
        public static int YearStartDay(int day) => YearIndex(day) * DaysPerYear;

        /// <summary>Month index, 0 (January) to 11 (December).</summary>
        public static int MonthIndex(int day) => MonthOfDay[DayOfYear(day)];

        /// <summary>Month number, 1 (January) to 12 (December).</summary>
        public static int Month(int day) => MonthIndex(day) + 1;

        /// <summary>Day of the month, 1 to 31.</summary>
        public static int DayOfMonth(int day)
        {
            int doy = DayOfYear(day);
            return doy - MonthStarts[MonthOfDay[doy]] + 1;
        }

        /// <summary>True on the first day of a month.</summary>
        public static bool IsFirstDayOfMonth(int day) => DayOfMonth(day) == 1;

        /// <summary>Length of a month in days (<paramref name="monthIndex"/> 0 = January).</summary>
        public static int DaysInMonth(int monthIndex) => MonthLengths[WrapMonth(monthIndex)];

        /// <summary>Day of year (0-based) of the first day of a month.</summary>
        public static int MonthStartDayOfYear(int monthIndex) => MonthStarts[WrapMonth(monthIndex)];

        /// <summary>
        /// Day of year (fractional, 0-based) of the middle of a month: where a
        /// monthly mean sits when monthly values are interpolated by day.
        /// </summary>
        public static double MonthMidDayOfYear(int monthIndex)
        {
            int m = WrapMonth(monthIndex);
            return MonthStarts[m] + (MonthLengths[m] - 1) / 2.0;
        }

        private static int WrapMonth(int monthIndex)
        {
            int m = monthIndex % MonthsPerYear;
            return m < 0 ? m + MonthsPerYear : m;
        }

        /// <summary>
        /// The sim day <paramref name="months"/> calendar months after
        /// <paramref name="day"/>, on the same day of the month or the month's
        /// last day when it is shorter (31 January + 1 month = 28 February).
        /// </summary>
        public static int AddMonths(int day, int months)
        {
            int monthsFromEpoch = YearIndex(day) * MonthsPerYear + MonthIndex(day) + months;
            int year = FloorDiv(monthsFromEpoch, MonthsPerYear);
            int month = monthsFromEpoch - year * MonthsPerYear;
            int dom = Math.Min(DayOfMonth(day), MonthLengths[month]);
            return year * DaysPerYear + MonthStarts[month] + dom - 1;
        }

        /// <summary>Days in the next <paramref name="months"/> calendar months from <paramref name="day"/>.</summary>
        public static int DaysInMonthsFrom(int day, int months) => AddMonths(day, months) - day;

        /// <summary>Whole calendar months from <paramref name="fromDay"/> to <paramref name="toDay"/> (0 when not after).</summary>
        public static int WholeMonthsBetween(int fromDay, int toDay)
        {
            if (toDay <= fromDay) return 0;
            int months = (YearIndex(toDay) - YearIndex(fromDay)) * MonthsPerYear + MonthIndex(toDay) - MonthIndex(fromDay);
            if (AddMonths(fromDay, months) > toDay) months--;
            return Math.Max(0, months);
        }

        /// <summary>Fraction of a year a span of days covers (ACT/365, the sim's day count).</summary>
        public static double YearFraction(int days) => (double)days / DaysPerYear;

        /// <summary>
        /// A monthly amount (salary, premium) spread per day: twelve months over
        /// 365 days. Dividing by 30 paid 12.17 months a year.
        /// </summary>
        public static decimal MonthlyToDaily(decimal monthlyAmount) => monthlyAmount * MonthsPerYear / DaysPerYear;

        /// <summary>Calendar months since sim day 0 (January of year 0 is 0).</summary>
        public static int MonthsSinceEpoch(int day) => YearIndex(day) * MonthsPerYear + MonthIndex(day);

        /// <summary>Calendar quarters since sim day 0 (January-March of year 0 is 0).</summary>
        public static int QuartersSinceEpoch(int day) => YearIndex(day) * 4 + MonthIndex(day) / 3;

        /// <summary>True on the last day of a month (monthly books close on it).</summary>
        public static bool IsLastDayOfMonth(int day) => IsFirstDayOfMonth(day + 1);

        /// <summary>True on 31 March, 30 June, 30 September and 31 December (quarterly books close on it).</summary>
        public static bool IsLastDayOfQuarter(int day) => QuartersSinceEpoch(day + 1) != QuartersSinceEpoch(day);

        /// <summary>True on 31 December.</summary>
        public static bool IsLastDayOfYear(int day) => DayOfYear(day) == DaysPerYear - 1;

        /// <summary>The weekday of a sim day.</summary>
        public static DayOfWeek Weekday(int day)
        {
            int w = ((int)Day0Weekday + day) % DaysPerWeek;
            return (DayOfWeek)(w < 0 ? w + DaysPerWeek : w);
        }

        /// <summary>ISO weekday, 1 (Monday) to 7 (Sunday).</summary>
        public static int IsoWeekday(int day)
        {
            var wd = Weekday(day);
            return wd == DayOfWeek.Sunday ? 7 : (int)wd;
        }

        /// <summary>ISO-8601 weeks in the sim year holding <paramref name="day"/>: 53 when 1 January is a Thursday.</summary>
        public static int IsoWeeksInYear(int day)
        {
            // A 365-day year starts and ends on the same weekday, so it has 53
            // ISO weeks exactly when that weekday is a Thursday.
            return Weekday(YearStartDay(day)) == DayOfWeek.Thursday ? 53 : 52;
        }

        /// <summary>
        /// ISO-8601 week number (1-53). Days before the year's first Thursday
        /// week belong to the last week of the previous year; the last days of
        /// December can belong to week 1 of the next.
        /// </summary>
        public static int IsoWeek(int day)
        {
            int week = (DayOfYear(day) + 1 - IsoWeekday(day) + 10) / DaysPerWeek;
            if (week < 1)
                return IsoWeeksInYear(YearStartDay(day) - 1);
            if (week > IsoWeeksInYear(day))
                return 1;
            return week;
        }

        /// <summary>True when <paramref name="day"/> falls in ISO weeks <paramref name="firstWeek"/> through <paramref name="lastWeek"/> (inclusive).</summary>
        public static bool InIsoWeeks(int day, int firstWeek, int lastWeek)
        {
            int week = IsoWeek(day);
            return week >= firstWeek && week <= lastWeek;
        }
    }
}
