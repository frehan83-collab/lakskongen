using System;
using System.Globalization;
using System.Linq;
using LakskongenSim.Calibration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// The shipped base levels (μ_month) and plot scales are the output of
/// <see cref="OffshoreMetoceanFit"/> on seeds 1001-1100 × 50 years, rounded to
/// three decimals: a change to any other metocean parameter that is not
/// followed by a refit fails here and prints the values to ship.
/// </summary>
public class OffshoreMetoceanFitTests
{
    private const double Rounding = 0.0005 + 1e-9;
    private readonly ITestOutputHelper _output;

    public OffshoreMetoceanFitTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ShippedCalibration_IsTheFitResult()
    {
        var cal = OffshoreCalibration.Default;
        var fit = OffshoreMetoceanFit.Fit(cal.Metocean, cal.Plots);
        string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        string report =
            $"BaseMuMonth = MonthlyValues.Of({string.Join(", ", fit.BaseMuMonth.Select(F))})\n" +
            $"reference 100-year level {F(fit.ReferenceHundredYearM)} m; scales off_1 {F(fit.Off1Scale)}, " +
            $"off_2 {F(fit.Off2Scale)}, hav_1 {F(fit.Hav1Scale)}\n" +
            $"monthly means {string.Join(" ", fit.MonthlyMeanM.Select(v => v.ToString("0.0000", CultureInfo.InvariantCulture)))}";
        _output.WriteLine(report);

        var shipped = cal.Metocean.BaseMuMonth.ToArray();
        for (int m = 0; m < 12; m++)
        {
            Assert.True(Math.Abs(fit.BaseMuMonth[m] - shipped[m]) <= Rounding,
                $"month {m + 1}: fit {fit.BaseMuMonth[m]:0.00000} vs shipped {shipped[m]}\n{report}");
            Assert.Equal(cal.Metocean.ReferenceMonthlyMeanHsM(m), fit.MonthlyMeanM[m], 9);
        }
        Assert.True(Math.Abs(fit.Off1Scale - cal.Plots.Off1.HsScale) <= Rounding, report);
        Assert.True(Math.Abs(fit.Off2Scale - cal.Plots.Off2.HsScale) <= Rounding, report);
        Assert.True(Math.Abs(fit.Hav1Scale - cal.Plots.Hav1.HsScale) <= Rounding, report);
    }
}
