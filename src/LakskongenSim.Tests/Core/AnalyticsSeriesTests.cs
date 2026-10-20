using System.Linq;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// S2 gate: the analytics time series (DailySnapshots) must follow the
/// simulation's own values — the finance/market time-series UI is only honest
/// if these hold. Pure engine test, no UI.
/// </summary>
public class AnalyticsSeriesTests
{
    private static SimulationEngine BuildEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        var scenario = engine.Scenario.GetScenario("po3_red_sone");
        scenario!.ApplyStartingState(engine.State, engine);
        engine.State.RandomSeed = seed;
        return engine;
    }

    [Fact]
    public void DailySeries_FollowsSimValues_AfterThirtyDays()
    {
        var engine = BuildEngine(seed: 7);
        engine.Advance(30);

        var analytics = engine.Analytics;
        Assert.NotNull(analytics);

        // One snapshot per simulated day (window logic caps at the request).
        var all = analytics.GetTimeSeries(AnalyticsMetric.Balance, 10_000);
        Assert.True(all.Count >= 25, $"expected ~30 daily snapshots, got {all.Count}");

        // A 30-day window returns exactly the last 30 (or all we have).
        var window = analytics.GetTimeSeries(AnalyticsMetric.Balance, 30);
        Assert.Equal(System.Math.Min(30, all.Count), window.Count);
        Assert.Equal(all.TakeLast(window.Count), window);

        // The newest series value IS the live sim value (the series is not a copy).
        double latest = analytics.GetLatest(AnalyticsMetric.Balance);
        Assert.Equal(window[^1], latest, 6);
        Assert.Equal((double)engine.State.Balance, latest, 6);

        // Stocked scenario → the fish series carries real, positive values.
        double fish = analytics.GetLatest(AnalyticsMetric.FishCount);
        Assert.True(fish > 0, $"expected stocked fish count, got {fish}");

        // Temperature stays inside a physically plausible band (calibration sanity).
        double temp = analytics.GetLatest(AnalyticsMetric.Temperature);
        Assert.InRange(temp, -2.0, 20.0);

        // Trend is one of the three declared directions.
        var trend = analytics.GetTrend(AnalyticsMetric.Balance, 14);
        Assert.True(trend is TrendDirection.Up or TrendDirection.Down or TrendDirection.Stable);
    }

    /// <summary>
    /// U1-05: the income and cost charts read these two series. They must be
    /// the statements' own year-to-date figures, day by day, not the cash
    /// balance (the dashboard's revenue card used to draw the balance).
    /// </summary>
    [Fact]
    public void RevenueAndExpenseSeries_AreTheStatementsYearToDate()
    {
        var engine = BuildEngine(seed: 11);
        var fs = engine.FinancialStatements;
        Assert.NotNull(fs);
        var revenue = new System.Collections.Generic.List<double>();
        var expenses = new System.Collections.Generic.List<double>();
        for (int day = 0; day < 40; day++)
        {
            engine.Advance(1);
            revenue.Add((double)fs!.Revenue);
            expenses.Add((double)(fs.COGS + fs.OperatingExpenses));
        }

        var revenueSeries = engine.Analytics.GetTimeSeries(AnalyticsMetric.Revenue, 40);
        var expenseSeries = engine.Analytics.GetTimeSeries(AnalyticsMetric.Expenses, 40);
        Assert.Equal(40, revenueSeries.Count);
        Assert.Equal(40, expenseSeries.Count);
        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(revenue[i], revenueSeries[i], 2);
            Assert.Equal(expenses[i], expenseSeries[i], 2);
        }
        // A farm feeding fish books costs every day: the year-to-date series
        // climbs and never falls inside the year.
        Assert.True(expenseSeries[^1] > expenseSeries[0], "costs must accumulate over 40 days");
        for (int i = 1; i < expenseSeries.Count; i++)
            Assert.True(expenseSeries[i] >= expenseSeries[i - 1] - 1e-6, $"day {i}: year-to-date costs fell");
        // Not the balance: the cash series moves differently.
        var balance = engine.Analytics.GetTimeSeries(AnalyticsMetric.Balance, 40);
        Assert.NotEqual(balance[^1], revenueSeries[^1]);
    }

    [Fact]
    public void Series_AfterMoreDays_ExtendsAndStaysAligned()
    {
        var engine = BuildEngine(seed: 13);
        engine.Advance(10);
        int after10 = engine.Analytics.GetTimeSeries(AnalyticsMetric.Balance, 10_000).Count;
        engine.Advance(10);
        int after20 = engine.Analytics.GetTimeSeries(AnalyticsMetric.Balance, 10_000).Count;

        Assert.True(after20 > after10, $"series must extend with days ({after10} → {after20})");
        // Newest snapshot tracks the live balance after both windows.
        Assert.Equal((double)engine.State.Balance,
            engine.Analytics.GetLatest(AnalyticsMetric.Balance), 6);
    }
}
