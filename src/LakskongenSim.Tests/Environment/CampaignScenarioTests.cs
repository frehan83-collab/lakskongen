using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Campaign
{
    /// <summary>
    /// Campaign wave tests: verifies the extended ScenarioDefinition schema, the
    /// engine-independent ApplyStartingState(), and the four research-grounded
    /// campaign scenarios (PO3 red zone, ISA, algal bloom, resource rent).
    /// </summary>
    public class CampaignScenarioTests
    {
        private static ScenarioSystem NewSystem() => new(new GameState());

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

            s.ApplyStartingState(state);

            Assert.Equal(sites, state.SeaSites.Count);
            Assert.Equal(zones, state.Zones.Count);
            Assert.Equal(pops, state.Populations.Count);
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
              "tags": ["a", "b"],
              "restrictions": ["r1"],
              "starting_research": ["sensor-networks"],
              "starting_buildings": ["hatchery"],
              "starting_sites": [
                { "zone": "z1", "pens": 2, "smolt_count": 5000, "smolt_quality": "robust" }
              ]
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
            Assert.Equal(new[] { "a", "b" }, s.Tags);
            Assert.Single(s.Restrictions);
            Assert.Single(s.StartingResearch);
            Assert.Single(s.StartingBuildings);
            Assert.Single(s.StartingSites);
            Assert.Equal("z1", s.StartingSites[0].Zone);
            Assert.Equal(2, s.StartingSites[0].Pens);
            Assert.Equal(5000, s.StartingSites[0].SmoltCount);
            Assert.Equal("robust", s.StartingSites[0].SmoltQuality);
        }
    }
}
