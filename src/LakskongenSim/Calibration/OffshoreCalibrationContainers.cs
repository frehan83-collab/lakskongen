using System.ComponentModel.DataAnnotations;

namespace LakskongenSim.Calibration
{
    // The F2-06 containers, one record per use (wiring stage, F2-07 branch): the
    // twelve months or four weather states carry the holding property's [Range]
    // and provenance as attributes, so F2-04's reflection guard
    // (CalibrationGuardTests) checks every value against its own range. The doc
    // comment of the holding property in OffshoreCalibration.cs stays the text.

    /// <summary>The base sea's monthly level μ in the sea transform (OffshoreMetoceanCalibration.BaseMuMonth).</summary>
    public sealed record BaseMuMonthValues : MonthlyValues
    {
        private const string Why = "fitted so the reference sea's monthly mean Hs follows NORCE 2020 Table 3.2's shape at the 1.0 m annual mean (OffshoreMetoceanFitTests)";

        /// <summary>Month 01.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M01 { get; init; }

        /// <summary>Month 02.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M02 { get; init; }

        /// <summary>Month 03.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M03 { get; init; }

        /// <summary>Month 04.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M04 { get; init; }

        /// <summary>Month 05.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M05 { get; init; }

        /// <summary>Month 06.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M06 { get; init; }

        /// <summary>Month 07.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M07 { get; init; }

        /// <summary>Month 08.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M08 { get; init; }

        /// <summary>Month 09.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M09 { get; init; }

        /// <summary>Month 10.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M10 { get; init; }

        /// <summary>Month 11.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M11 { get; init; }

        /// <summary>Month 12.</summary>
        [Range(-3.0, 1.0)]
        [GameBalance(Why)]
        public override double M12 { get; init; }
    }

    /// <summary>The monthly mean Hs whose shape the base level is fitted to (OffshoreMetoceanCalibration.FitMonthlyMeanHsM).</summary>
    public sealed record FitMonthlyMeanHsValues : MonthlyValues
    {
        private const string Why = "NORCE 2020 Table 3.2";

        /// <summary>Month 01.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M01 { get; init; }

        /// <summary>Month 02.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M02 { get; init; }

        /// <summary>Month 03.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M03 { get; init; }

        /// <summary>Month 04.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M04 { get; init; }

        /// <summary>Month 05.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M05 { get; init; }

        /// <summary>Month 06.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M06 { get; init; }

        /// <summary>Month 07.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M07 { get; init; }

        /// <summary>Month 08.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M08 { get; init; }

        /// <summary>Month 09.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M09 { get; init; }

        /// <summary>Month 10.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M10 { get; init; }

        /// <summary>Month 11.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M11 { get; init; }

        /// <summary>Month 12.</summary>
        [Range(0.5, 6.0)]
        [Source(Why)]
        public override double M12 { get; init; }
    }

    /// <summary>The daily storm-swell creation rate by month (OffshoreMetoceanCalibration.SwellRatePerDay).</summary>
    public sealed record SwellRateValues : MonthlyValues
    {
        private const string Why = "spec §3.3 monthly swell rates; F2-06 acceptance 2 (8-14 % of days above 2 m, 20-35 % in January-March)";

        /// <summary>Month 01.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M01 { get; init; }

        /// <summary>Month 02.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M02 { get; init; }

        /// <summary>Month 03.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M03 { get; init; }

        /// <summary>Month 04.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M04 { get; init; }

        /// <summary>Month 05.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M05 { get; init; }

        /// <summary>Month 06.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M06 { get; init; }

        /// <summary>Month 07.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M07 { get; init; }

        /// <summary>Month 08.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M08 { get; init; }

        /// <summary>Month 09.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M09 { get; init; }

        /// <summary>Month 10.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M10 { get; init; }

        /// <summary>Month 11.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M11 { get; init; }

        /// <summary>Month 12.</summary>
        [Range(0.0, 0.3)]
        [GameBalance(Why)]
        public override double M12 { get; init; }
    }

    /// <summary>The monthly 1-year extreme Hs that scales the swell median (OffshoreMetoceanCalibration.SwellMedianShape1YearHsM).</summary>
    public sealed record SwellMedianShapeValues : MonthlyValues
    {
        private const string Why = "NORCE 2020 Table 3.4";

        /// <summary>Month 01.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M01 { get; init; }

        /// <summary>Month 02.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M02 { get; init; }

        /// <summary>Month 03.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M03 { get; init; }

        /// <summary>Month 04.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M04 { get; init; }

        /// <summary>Month 05.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M05 { get; init; }

        /// <summary>Month 06.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M06 { get; init; }

        /// <summary>Month 07.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M07 { get; init; }

        /// <summary>Month 08.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M08 { get; init; }

        /// <summary>Month 09.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M09 { get; init; }

        /// <summary>Month 10.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M10 { get; init; }

        /// <summary>Month 11.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M11 { get; init; }

        /// <summary>Month 12.</summary>
        [Range(3.0, 15.0)]
        [Source(Why)]
        public override double M12 { get; init; }
    }

    /// <summary>The monthly share of low-visibility days (OffshoreMetoceanCalibration.LowVisibilityDayShare).</summary>
    public sealed record LowVisibilityShareValues : MonthlyValues
    {
        private const string Why = "anchored to ship reports north of 62 °N: fog in under 3 % of observations, most in June-August (ICOADS 1950-2007, AMS 2017)";

        /// <summary>Month 01.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M01 { get; init; }

        /// <summary>Month 02.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M02 { get; init; }

        /// <summary>Month 03.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M03 { get; init; }

        /// <summary>Month 04.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M04 { get; init; }

        /// <summary>Month 05.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M05 { get; init; }

        /// <summary>Month 06.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M06 { get; init; }

        /// <summary>Month 07.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M07 { get; init; }

        /// <summary>Month 08.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M08 { get; init; }

        /// <summary>Month 09.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M09 { get; init; }

        /// <summary>Month 10.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M10 { get; init; }

        /// <summary>Month 11.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M11 { get; init; }

        /// <summary>Month 12.</summary>
        [Range(0.0, 0.2)]
        [GameBalance(Why)]
        public override double M12 { get; init; }
    }

    /// <summary>The base sea's shift by the fjord weather state (OffshoreMetoceanCalibration.WeatherDelta).</summary>
    public sealed record WeatherDeltaValues : WeatherStateValues
    {
        private const string Why = "spec §3.3: couples the offshore sea to the fjord weather chain so the two never visibly disagree";

        /// <summary>Calm.</summary>
        [Range(-1.0, 1.5)]
        [GameBalance(Why)]
        public override double Calm { get; init; }

        /// <summary>Moderate.</summary>
        [Range(-1.0, 1.5)]
        [GameBalance(Why)]
        public override double Moderate { get; init; }

        /// <summary>Storm.</summary>
        [Range(-1.0, 1.5)]
        [GameBalance(Why)]
        public override double Storm { get; init; }

        /// <summary>Severe.</summary>
        [Range(-1.0, 1.5)]
        [GameBalance(Why)]
        public override double Severe { get; init; }
    }
}
