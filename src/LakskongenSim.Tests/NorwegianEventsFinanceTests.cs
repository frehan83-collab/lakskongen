using System;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Events;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Cash-authority regression: NorwegianEvents must settle through
    /// FinanceManager (production) — never the dead GameState.Balance mirror.
    /// </summary>
    public class NorwegianEventsFinanceTests
    {
        [Fact]
        public void Events_SettleThroughFinanceManager()
        {
            var state = new GameState();
            state.Populations.Add(new LakskongenSim.Biology.FishPopulation());
            state.SeaSites.Add(new LakskongenSim.Farm.SeaSite { Id = "test-site" });
            var time = new TimeManager();
            var finance = new FinanceManager(startingBalance: 100_000_000m);
            var storyteller = new Storyteller(time, state, finance, seed: 12345);
            NorwegianEvents.RegisterAll(storyteller, finance);

            for (int d = 0; d < 1200; d++)
            {
                state.Day++;
#pragma warning disable CS0618 // mirror the engine: keep gs.Balance readable for Conditions
                state.Balance = finance.Balance;
#pragma warning restore CS0618
                time.AdvanceDays(1);
            }

            var settled = state.EventLog.Where(r => r.BalanceChange != 0m).ToList();
            Assert.NotEmpty(settled); // vacuous-pass guard: seed must fire cash events
            decimal expected = 100_000_000m + settled.Sum(r => r.BalanceChange);
            Assert.Equal(expected, finance.Balance);
        }

        [Fact]
        public void Events_WithoutFinance_FallBackToGameState()
        {
            var state = new GameState();
            state.Populations.Add(new LakskongenSim.Biology.FishPopulation());
            var time = new TimeManager();
            var storyteller = new Storyteller(time, state, financeManager: null, seed: 12345);
            NorwegianEvents.RegisterAll(storyteller); // no finance: legacy path

            for (int d = 0; d < 1200; d++)
            {
                state.Day++;
                time.AdvanceDays(1);
            }

            var settled = state.EventLog.Where(r => r.BalanceChange != 0m).ToList();
            Assert.NotEmpty(settled);
#pragma warning disable CS0618 // legacy fallback under test
            Assert.Equal(settled.Sum(r => r.BalanceChange), state.Balance);
#pragma warning restore CS0618
        }
    }
}
