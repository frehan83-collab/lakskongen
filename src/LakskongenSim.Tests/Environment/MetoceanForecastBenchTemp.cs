using System;
using System.Diagnostics;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Environment;

public class MetoceanForecastBenchTemp
{
    private readonly ITestOutputHelper _output;
    public MetoceanForecastBenchTemp(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Bench()
    {
        var cal = OffshoreCalibration.Default;
        int[] leads = { 1, 3, 5, 7, 10 };
        var ticks = new long[11];
        var counts = new long[11];
        var combos = new long[11];
        var sw = new Stopwatch();
        MetoceanRuns.Run(1, 5, afterTick: (day, sea, weather) =>
        {
            foreach (int k in leads)
            {
                var dist = weather.StateDistribution(k);
                sw.Restart();
                var model = MetoceanForecast.BuildDay(sea, sea.Calibration(), dist, k);
                double p = model.Cdf(2.0 / cal.Plots.Off1.HsScale);
                sw.Stop();
                ticks[k] += sw.ElapsedTicks;
                counts[k]++;
                combos[k] += model.Combinations.Count;
            }
        });
        foreach (int k in leads)
            _output.WriteLine($"lead {k}: {1e6 * ticks[k] / Stopwatch.Frequency / counts[k]:0.0} us per BuildDay+Cdf, {combos[k] / (double)counts[k]:0.0} combos");
        sw.Restart();
        int n = 0;
        MetoceanRuns.Run(2, 1, afterTick: (day, sea, weather) =>
        {
            if (day % 7 != 0) return;
            MetoceanForecast.Issue(sea, weather, cal.Plots.Off1.HsScale, new[] { 2.0, 2.5, 3.0, 5.0 });
            n++;
        });
        sw.Stop();
        _output.WriteLine($"Issue with quantiles: {sw.Elapsed.TotalMilliseconds / n:0.0} ms each ({n} issues)");
    }
}
