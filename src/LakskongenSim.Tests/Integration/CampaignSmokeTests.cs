using System;
using System.Diagnostics;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Biology;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// CI smoke coverage for every scenario in the campaign roster: each one must
    /// initialise from its declared starting state and survive a full year of
    /// event-enabled simulation without crashing, producing a sane market price,
    /// non-negative fish counts and finite cash.
    /// </summary>
    public class CampaignSmokeTests
    {
        // Wall-clock gate with load-noise margin (Phase 1 wave 4): a full year
        // per scenario measures well under 1 s; 6 s still fails an order-of-
        // magnitude regression while surviving shared-runner spikes.
        private const double PerScenarioBudgetSeconds = 6.0;
        private const double PriceFloorFactor = 0.6;
        private const double PriceCeilingFactor = 1.5;

        public const string ForsteSesong = "forste_sesong";
        public const string LofotenEventyr = "lofoten_eventyr";
        public const string Laksebaronene = "laksebaronene";
        public const string KriseManager = "krise_manager";
        public const string BarekraftMester = "barekraft_mester";
        public const string Po3RedSone = "po3_red_sone";
        public const string IsaUtbrudd = "isa_utbrudd";
        public const string Algekatastrofe = "algekatastrofe";
        public const string Ressursrente = "ressursrente";

        [Fact]
        public void Roster_ContainsTheNineCampaignScenarios()
        {
            var roster = new ScenarioSystem(new GameState());

            Assert.Equal(9, roster.All.Count);
            Assert.All(
                new[]
                {
                    ForsteSesong, LofotenEventyr, Laksebaronene, KriseManager, BarekraftMester,
                    Po3RedSone, IsaUtbrudd, Algekatastrofe, Ressursrente
                },
                id => Assert.NotNull(roster.GetScenario(id)));
        }

        [Theory]
        [InlineData(ForsteSesong)]
        [InlineData(LofotenEventyr)]
        [InlineData(Laksebaronene)]
        [InlineData(KriseManager)]
        [InlineData(BarekraftMester)]
        [InlineData(Po3RedSone)]
        [InlineData(IsaUtbrudd)]
        [InlineData(Algekatastrofe)]
        [InlineData(Ressursrente)]
        public void Scenario_SurvivesOneYearWithEvents(string scenarioId)
        {
            var engine = new SimulationEngine();
            var stopwatch = Stopwatch.StartNew();

            engine.Initialize(enableEvents: true, seed: 7);

            var scenario = engine.Scenario.GetScenario(scenarioId);
            Assert.NotNull(scenario);
            scenario!.ApplyStartingState(engine.State);
            EnsureRunnable(engine);

            engine.Advance(365);
            stopwatch.Stop();

            Assert.True(double.IsFinite((double)engine.FinanceManager.Balance),
                $"{scenarioId}: balance is not finite.");

            double floor = (double)engine.MarketSimulation.PriceFloorPerKg * PriceFloorFactor;
            double ceiling = (double)engine.MarketSimulation.PriceCeilingPerKg * PriceCeilingFactor;
            double price = (double)engine.MarketSimulation.PricePerKg;
            Assert.InRange(price, floor, ceiling);

            Assert.All(engine.State.Populations, pop =>
            {
                Assert.True(pop.TotalCount >= 0, $"{scenarioId}: negative population count.");
                Assert.All(pop.Cohorts, cohort =>
                    Assert.True(cohort.Count >= 0, $"{scenarioId}: negative cohort count."));
            });

            Assert.True(stopwatch.Elapsed.TotalSeconds < PerScenarioBudgetSeconds,
                $"{scenarioId}: took {stopwatch.Elapsed.TotalSeconds:F2}s, budget {PerScenarioBudgetSeconds}s.");
        }

        [Fact]
        public void Ressursrente_ResourceRentTaxThreeYears_StaysFiniteAndSolvent()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: 7);

            engine.Scenario.StartScenario(Ressursrente);
            engine.Scenario.GetScenario(Ressursrente)!.ApplyStartingState(engine.State);
            engine.EnableResourceRentTax = true;
            EnsureRunnable(engine);

            engine.Advance(365 * 3);

            Assert.Equal(1095, engine.State.Day);
            Assert.True(double.IsFinite((double)engine.FinanceManager.Balance));
            Assert.True(engine.ResourceRentPaid >= 0m);

            var statements = engine.FinancialStatements;
            Assert.True(double.IsFinite((double)statements.Revenue));
            Assert.True(double.IsFinite((double)statements.COGS));
            Assert.True(double.IsFinite((double)statements.OperatingExpenses));
            Assert.True(double.IsFinite((double)statements.ComputeNetIncome()));

            Assert.All(engine.State.Populations, pop =>
                Assert.True(pop.TotalCount >= 0));
        }

        private static void EnsureRunnable(SimulationEngine engine)
        {
            if (engine.State.Zones.Count == 0)
                engine.State.Zones.Add(new LakskongenSim.Environment.Zone("smoke_zone", 10.0));

            if (engine.State.Populations.Count == 0)
            {
                var population = new FishPopulation(engine.Growth, engine.Mortality)
                {
                    ZoneId = engine.State.Zones[0].Id,
                    SiteId = "smoke_site"
                };
                population.AddCohort(new FishPopulation.Cohort(
                    ageDays: 300,
                    count: 10_000,
                    meanWeightKg: 0.1,
                    stage: LifeStage.Smolt));
                engine.State.Populations.Add(population);
            }
        }
    }
}
