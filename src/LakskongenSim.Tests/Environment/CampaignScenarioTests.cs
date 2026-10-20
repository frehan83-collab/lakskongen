using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Campaign
{
    /// <summary>
    /// Campaign wave tests: verifies the extended ScenarioDefinition schema, the
    /// engine-independent ApplyStartingState(), and the four research-grounded
    /// campaign scenarios (PO3 red zone, ISA, algal bloom, resource rent).
    ///
    /// The campaigns must each apply a *distinctive* depth-system starting state
    /// (containment, insurance, mitigation, scripted bloom, disease seed,
    /// resource-rent opt-in) and survive a full year of event-enabled simulation
    /// once that state is materialised on the live engine.
    /// </summary>
    public class CampaignScenarioTests
    {
        private static ScenarioSystem NewSystem() => new(new GameState());

        private static SimulationEngine NewEngine()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: 7);
            return engine;
        }

        [Theory]
        [InlineData("po3_red_sone")]
        [InlineData("isa_utbrudd")]
        [InlineData("algekatastrofe")]
        [InlineData("ressursrente")]
        public void Campaign_IsRegisteredAndUnlocked(string id)
        {
            var s = NewSystem().GetScenario(id);

            Assert.NotNull(s);
            Assert.True(s!.IsUnlocked);
            Assert.False(string.IsNullOrWhiteSpace(s.Story));
            Assert.NotEmpty(s.Tags);
            Assert.True(s.StartingSites.Count > 0, "campaign should define starting sites");
        }

        [Theory]
        [InlineData("po3_red_sone")]
        [InlineData("isa_utbrudd")]
        [InlineData("algekatastrofe")]
        [InlineData("ressursrente")]
        public void Campaign_DeclaresObjectiveRestrictionsAndTags(string id)
        {
            var s = NewSystem().GetScenario(id)!;

            Assert.False(string.IsNullOrWhiteSpace(s.Objective),
                $"{id}: campaign must declare an objective");
            Assert.NotEmpty(s.Restrictions);
            Assert.NotEmpty(s.Tags);
        }

        [Theory]
        [InlineData("forste_sesong")]
        [InlineData("lofoten_eventyr")]
        [InlineData("laksebaronene")]
        [InlineData("krise_manager")]
        [InlineData("barekraft_mester")]
        public void LegacyScenariosStillPresent(string id)
        {
            Assert.NotNull(NewSystem().GetScenario(id));
        }

        [Fact]
        public void ApplyStartingState_CreatesSitesPopulationsResearchAndBuildings()
        {
            var state = new GameState();
            var s = NewSystem().GetScenario("po3_red_sone")!;

            s.ApplyStartingState(state);

            Assert.Equal(2, state.SeaSites.Count);
            Assert.Equal(2, state.Zones.Count);
            Assert.Equal(s.StartingSites.Sum(x => x.Pens), state.SeaSites.Sum(x => x.Pens.Count));
            Assert.All(state.SeaSites, site =>
                Assert.All(site.Pens, pen => Assert.True(pen.TotalCount > 0)));
            Assert.Contains("sensor-networks", state.UnlockedResearchIds);
            Assert.True(state.OwnsAIFeedingSystem);
        }

        [Fact]
        public void ApplyStartingState_IsIdempotent()
        {
            var state = new GameState();
            var s = NewSystem().GetScenario("isa_utbrudd")!;

            s.ApplyStartingState(state);
            int sites = state.SeaSites.Count;
            int zones = state.Zones.Count;
            int pops = state.Populations.Count;
            int blooms = state.ActiveBlooms.Count;
            int policies = state.InsurancePolicies.Count;

            s.ApplyStartingState(state);

            Assert.Equal(sites, state.SeaSites.Count);
            Assert.Equal(zones, state.Zones.Count);
            Assert.Equal(pops, state.Populations.Count);
            Assert.Equal(blooms, state.ActiveBlooms.Count);
            Assert.Equal(policies, state.InsurancePolicies.Count);
        }

        [Fact]
        public void DefaultDefinition_ApplyStartingState_IsSafe()
        {
            var state = new GameState();
            var s = new ScenarioDefinition();

            s.ApplyStartingState(state); // must not throw on empty defaults

            Assert.Empty(state.SeaSites);
            Assert.Empty(state.Populations);
        }

        [Fact]
        public void ExtendedSchema_RoundTripsThroughJson()
        {
            const string json = """
            {
              "id": "test_ext",
              "name": "Test",
              "starting_cash": 1234567,
              "story": "A story",
              "objective": "Survive the bloom",
              "tags": ["a", "b"],
              "restrictions": ["r1"],
              "starting_research": ["sensor-networks"],
              "starting_buildings": ["hatchery"],
              "starting_sites": [
                { "zone": "z1", "pens": 2, "smolt_count": 5000, "smolt_quality": "robust", "containment": "semi_closed", "barrier": "smoltscreen" }
              ],
              "starting_insurance": [
                { "kind": "ISA", "premium": 300000, "coverage": 0.9, "deductible": 500000 }
              ],
              "starting_mitigations": [
                { "site": "z1", "kind": "diffuser" }
              ],
              "starting_blooms": [
                { "site": "z1", "kind": "algae", "severity": 0.6, "duration_days": 28 }
              ],
              "starting_disease": { "type": "ISA", "site": "z1", "external_pressure": 1.7, "seed_days": 21 },
              "enable_resource_rent_tax": true
            }
            """;

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var s = JsonSerializer.Deserialize<ScenarioDefinition>(json, opts);

            Assert.NotNull(s);
            Assert.Equal(1_234_567m, s!.StartingCash);
            Assert.Equal("A story", s.Story);
            Assert.Equal("Survive the bloom", s.Objective);
            Assert.Equal(new[] { "a", "b" }, s.Tags);
            Assert.Single(s.Restrictions);
            Assert.Single(s.StartingResearch);
            Assert.Single(s.StartingBuildings);
            Assert.Single(s.StartingSites);
            Assert.Equal("z1", s.StartingSites[0].Zone);
            Assert.Equal(2, s.StartingSites[0].Pens);
            Assert.Equal(5000, s.StartingSites[0].SmoltCount);
            Assert.Equal("robust", s.StartingSites[0].SmoltQuality);
            Assert.Equal("semi_closed", s.StartingSites[0].Containment);
            Assert.Equal("smoltscreen", s.StartingSites[0].Barrier);
            Assert.Single(s.StartingInsurance);
            Assert.Equal("ISA", s.StartingInsurance[0].Kind);
            Assert.Single(s.StartingMitigations);
            Assert.Single(s.StartingBlooms);
            Assert.NotNull(s.StartingDisease);
            Assert.Equal("ISA", s.StartingDisease!.Type);
            Assert.True(s.EnableResourceRentTax);
        }

        // ─── Per-campaign distinctive starting state ──────────────────────────

        [Fact]
        public void Po3RedSone_StartsWithClosedContainmentForMabRecovery()
        {
            var state = new GameState();
            var s = NewSystem().GetScenario("po3_red_sone")!;

            s.ApplyStartingState(state);

            var nord = state.SeaSites.Single(x => x.Id == "po3_nord");
            var sor = state.SeaSites.Single(x => x.Id == "po3_sor");

            Assert.Equal(ContainmentLevel.SemiClosed, nord.Containment);
            Assert.Equal(ContainmentLevel.OpenNet, sor.Containment);
            Assert.Equal("semi_closed", s.SpecialRules["containment_target"]);
            Assert.Equal("closed_containment", s.SpecialRules["environmental_flexibility"]);
        }

        [Fact]
        public void IsaUtbrudd_SeedsDiseaseSubscribesInsuranceAndBuildsBarriers()
        {
            var engine = NewEngine();
            var s = engine.Scenario.GetScenario("isa_utbrudd")!;

            s.ApplyStartingState(engine.State, engine);

            // Disease seeded on the core site's population.
            Assert.NotNull(s.StartingDisease);
            var core = engine.State.Populations.First(p => p.SiteId == "isa_kjerne");
            Assert.True(engine.Disease.GetPrevalence(core) > 0.0,
                "ISA seed should raise SIR prevalence on the infected core site");

            // Insurance pre-subscribed for both ISA and routine mortality.
            Assert.Contains(engine.Insurance.Policies, p => p.Kind == InsuranceKind.ISA);
            Assert.Contains(engine.Insurance.Policies, p => p.Kind == InsuranceKind.Mortality);
            Assert.Equal(2, engine.State.InsurancePolicies.Count);

            // Biosecurity firewalls between the zones.
            Assert.Equal(BarrierKind.Smoltscreen,
                engine.State.SeaSites.Single(x => x.Id == "isa_nord").Barrier);
            Assert.Equal(BarrierKind.Smoltscreen,
                engine.State.SeaSites.Single(x => x.Id == "isa_sor").Barrier);

            // Outbreak recorded in the event log for the UI.
            Assert.Contains(engine.State.EventLog,
                e => e.Category == LakskongenSim.Events.EventCategory.Disease && e.Id.Contains("isa"));
        }

        [Fact]
        public void Algekatastrofe_SeedsBloomPreInstallsMitigationAndInsures()
        {
            var engine = NewEngine();
            var s = engine.Scenario.GetScenario("algekatastrofe")!;

            s.ApplyStartingState(engine.State, engine);

            // A scripted bloom is already active on an exposed site.
            var bloom = Assert.Single(engine.State.ActiveBlooms);
            Assert.Equal("alge_sor", bloom.SiteId);
            Assert.Equal("algae", bloom.Kind);
            Assert.True(bloom.Severity01 > 0.0);

            // Flag-backed mitigation is installed; the rest is mirrored.
            Assert.True(engine.State.SeaSites.Single(x => x.Id == "alge_indre").HasO2Diffuser);
            Assert.Contains(engine.State.BloomMitigations,
                m => m.SiteId == "alge_midt" && m.Kind == "pumping");

            // Catastrophe cover for the bloom.
            Assert.Contains(engine.Insurance.Policies, p => p.Kind == InsuranceKind.Bloom);
            Assert.Contains(s.SpecialRules, kv => kv.Key == "mitigation_target");
            Assert.Contains(s.SpecialRules, kv => kv.Key == "bloom_window_start_doy");
        }

        [Fact]
        public void Ressursrente_EnablesResourceRentTaxAndStartsInsured()
        {
            var engine = NewEngine();
            var s = engine.Scenario.GetScenario("ressursrente")!;

            s.ApplyStartingState(engine.State, engine);

            Assert.True(engine.State.EnableResourceRentTax);
            Assert.True(engine.EnableResourceRentTax);
            Assert.Contains(engine.Insurance.Policies, p => p.Kind == InsuranceKind.Mortality);
            Assert.Equal(0m, engine.ResourceRentPaid);
        }

        // ─── 365-day survival with the materialised starting state ────────────

        [Theory]
        [InlineData("po3_red_sone")]
        [InlineData("isa_utbrudd")]
        [InlineData("algekatastrofe")]
        [InlineData("ressursrente")]
        public void Campaign_SurvivesOneYearWithEngineSeededStartingState(string scenarioId)
        {
            var engine = NewEngine();
            var scenario = engine.Scenario.GetScenario(scenarioId);
            Assert.NotNull(scenario);

            scenario!.ApplyStartingState(engine.State, engine);
            EnsureRunnable(engine);

            engine.Advance(365);

            Assert.Equal(365, engine.State.Day);
            Assert.True(double.IsFinite((double)engine.FinanceManager.Balance),
                $"{scenarioId}: balance is not finite.");
            Assert.All(engine.State.Populations, pop =>
            {
                Assert.True(pop.TotalCount >= 0, $"{scenarioId}: negative population count.");
                Assert.All(pop.Cohorts, cohort =>
                    Assert.True(cohort.Count >= 0, $"{scenarioId}: negative cohort count."));
            });
        }

        private static void EnsureRunnable(SimulationEngine engine)
        {
            if (engine.State.Zones.Count == 0)
                engine.State.Zones.Add(new LakskongenSim.Environment.Zone("campaign_zone", 10.0));

            if (engine.State.Populations.Count == 0)
            {
                var population = new LakskongenSim.Biology.FishPopulation(engine.Growth, engine.Mortality)
                {
                    ZoneId = engine.State.Zones[0].Id,
                    SiteId = "campaign_site"
                };
                population.AddCohort(new LakskongenSim.Biology.FishPopulation.Cohort(
                    ageDays: 300,
                    count: 10_000,
                    meanWeightKg: 0.1,
                    stage: LakskongenSim.Data.LifeStage.Smolt));
                engine.State.Populations.Add(population);
            }
        }
    }
}
