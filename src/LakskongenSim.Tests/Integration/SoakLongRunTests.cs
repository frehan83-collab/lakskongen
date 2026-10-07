using System;
using System.Diagnostics;
using System.Linq;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Long-running "soak" guard: three simulated years with storyteller events
    /// and the opt-in resource-rent regime switched on. The simulation must not
    /// blow up (NaN/inf, negative fish, absurd prices) and must stay fast so the
    /// campaign remains playable at 3x speed.
    /// </summary>
    public class SoakLongRunTests
    {
        [Fact]
        public void ThreeYearResourceRentSoak_StaysSane_AndFast()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: 12345);

            // Use the ressursrente scenario starting state when available.
            var scenario = engine.Scenario.GetScenario("ressursrente");
            Assert.NotNull(scenario);
            scenario!.ApplyStartingState(engine.State);
            engine.EnableResourceRentTax = true;

            var market = engine.MarketSimulation;
            decimal priceFloor = market.PriceFloorPerKg * 0.6m;
            decimal priceCeiling = market.PriceCeilingPerKg * 1.5m;

            const int days = 1095; // 3 simulated years
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < days; i++)
            {
                engine.Advance(1);

                // Balance is decimal (cannot be NaN/inf) but guard the cast too.
                double balance = (double)engine.FinanceManager.Balance;
                Assert.False(double.IsNaN(balance) || double.IsInfinity(balance),
                    $"Non-finite balance on day {engine.State.Day}: {balance}");

                decimal price = market.PricePerKg;
                Assert.True(price >= priceFloor && price <= priceCeiling,
                    $"Price {price:N2} NOK/kg escaped [{priceFloor:N2}, {priceCeiling:N2}] on day {engine.State.Day}");

                foreach (var population in engine.State.Populations)
                {
                    foreach (var cohort in population.Cohorts)
                    {
                        Assert.True(cohort.Count >= 0,
                            $"Negative fish count ({cohort.Count}) on day {engine.State.Day}");
                        Assert.False(double.IsNaN(cohort.MeanWeightKg) || double.IsInfinity(cohort.MeanWeightKg),
                            $"Non-finite cohort weight on day {engine.State.Day}");
                    }
                }
            }

            stopwatch.Stop();

            Assert.Equal(days, engine.State.Day);
            Assert.True(stopwatch.Elapsed.TotalSeconds < 5.0,
                $"3-year soak took {stopwatch.Elapsed.TotalSeconds:F2}s (expected <5s)");
        }
    }
}
