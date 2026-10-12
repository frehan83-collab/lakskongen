using System.Linq;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Phase 5.5 "regulatory chess" — felt consequence for running a red-zone
    /// site over MAB: on RED entry Mattilsynet issues a dated order; the player
    /// can get under MAB before the deadline (order fulfilled, no penalty) or
    /// miss it and eat a forced harvest at 70% of market price.
    /// </summary>
    public class MattilsynetOrderTests
    {
        private const double RedLiceLoad = 2.0; // >= TrafficLightSystem.RedLiceThreshold

        private static (SimulationEngine Engine, SeaSite Site, FishPopulation Pop) NewRedReadyEngine()
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(100_000_000m);
            engine.Initialize(enableEvents: false, seed: 4242);

            var zone = new Zone("zone-1");
            engine.State.Zones.Add(zone);

            // Tiny MAB so 5 t of fish is comfortably over the limit.
            var site = new SeaSite("site-1", zone.Id) { MABTonnes = 0.5 };
            engine.State.SeaSites.Add(site);

            var pop = new FishPopulation();
            pop.AddCohort(new FishPopulation.Cohort(400, 10_000, 0.5, LifeStage.Harvest));
            site.Stock(pop);
            engine.State.Populations.Add(pop);

            // Green baseline; the lice assessment below flips the zone to RED.
            engine.TrafficLightSystem.SetZoneColor(zone.Id, ZoneColor.Green);
            pop.SeaLiceLoad = RedLiceLoad;
            return (engine, site, pop);
        }

        [Fact]
        public void RedTransition_IssuesDatedOrder_AndMissedDeadlineForcesPenaltyHarvest()
        {
            var (engine, site, pop) = NewRedReadyEngine();

            // Act 1 — zone goes RED: order issued with a 14-day deadline.
            engine.UpdateTrafficLightZones();
            Assert.Equal(ZoneColor.Red, engine.TrafficLightSystem.GetZoneColor("zone-1"));
            Assert.True(site.HasActiveMattilsynetOrder);
            Assert.Equal(engine.State.Day + 14, site.MattilsynetDeadlineDay);

            // Act 2 — deadline passes with the site still over MAB.
            engine.State.Day = site.MattilsynetDeadlineDay;
            decimal cashBefore = engine.FinanceManager.Balance;
            engine.ProcessMattilsynetOverMabOrders();

            // Assert — forced harvest happened, at the penalty price.
            Assert.False(site.HasActiveMattilsynetOrder);
            Assert.True(site.MattilsynetOrderFulfilled);
            Assert.True(engine.FinancialStatements.RevenueBreakdown.TryGetValue(
                "forced_harvest", out var penaltyRevenue));
            Assert.True(penaltyRevenue > 0m);
            Assert.True(engine.FinanceManager.Balance > cashBefore);
            Assert.Contains(engine.State.EventLog, e => e.Name == "Tvangsavslakt (Mattilsynet)");

            // The over-MAB biomass was removed from the pens.
            Assert.True(pop.Cohorts.Sum(c => c.Count) < 10_000);
        }

        [Fact]
        public void MeetingTheDeadline_FulfillsOrder_WithoutPenalty()
        {
            var (engine, site, pop) = NewRedReadyEngine();

            engine.UpdateTrafficLightZones();
            Assert.True(site.HasActiveMattilsynetOrder);

            // Player gets under MAB before the deadline (harvest/reduce stock).
            foreach (var cohort in pop.Cohorts) cohort.Count = 10;
            site.AdvanceDays(0); // refresh stocking density
            Assert.False(site.IsOverMAB);

            decimal cashBefore = engine.FinanceManager.Balance;
            engine.ProcessMattilsynetOverMabOrders();

            Assert.True(site.MattilsynetOrderFulfilled);
            Assert.False(site.HasActiveMattilsynetOrder);
            Assert.Equal(cashBefore, engine.FinanceManager.Balance);
            Assert.False(engine.FinancialStatements.RevenueBreakdown.ContainsKey("forced_harvest"));
            Assert.Contains(engine.State.EventLog, e => e.Name == "Mattilsynet MAB-oppgjør");
        }

        [Fact]
        public void LeavingRedZone_ClearsTheOrder()
        {
            var (engine, site, pop) = NewRedReadyEngine();

            engine.UpdateTrafficLightZones();
            Assert.True(site.HasActiveMattilsynetOrder);

            // Lice load recovers → zone assessed GREEN again.
            pop.SeaLiceLoad = 0.0;
            engine.UpdateTrafficLightZones();

            Assert.Equal(ZoneColor.Green, engine.TrafficLightSystem.GetZoneColor("zone-1"));
            Assert.False(site.HasActiveMattilsynetOrder);
            Assert.Contains(engine.State.EventLog, e => e.Name == "Mattilsynet besked trukket");
        }
    }
}
