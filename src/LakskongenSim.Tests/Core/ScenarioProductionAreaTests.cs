using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// S2-02: the zone ids of scenarios.json are mapped to production areas
    /// ("production_area" on every starting site), in step with the built-in roster.
    /// </summary>
    public class ScenarioProductionAreaTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        [Fact]
        public void EveryScenarioSite_NamesAProductionArea_AsTheRosterDoes()
        {
            var roster = new ScenarioSystem(new GameState());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));
            int sites = 0;
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var builtIn = roster.GetScenario(entry.GetProperty("id").GetString()!)!;
                var jsonSites = entry.GetProperty("starting_sites").EnumerateArray().ToList();
                Assert.Equal(builtIn.StartingSites.Count, jsonSites.Count);
                for (int i = 0; i < jsonSites.Count; i++)
                {
                    int area = jsonSites[i].GetProperty("production_area").GetInt32();
                    Assert.True(ProductionAreas.IsValid(area), $"{builtIn.Id}: production_area {area}");
                    Assert.Equal(builtIn.StartingSites[i].Id, jsonSites[i].GetProperty("id").GetString());
                    Assert.Equal(builtIn.StartingSites[i].ProductionArea, area);
                    sites++;
                }
                // A scenario's sites lie in one area, its region's (PO3 is Vestland).
                var region = Enum.Parse<Data.ProductionRegion>(entry.GetProperty("starting_region").GetString()!);
                Assert.Equal(builtIn.StartingRegion, region);
            }
            Assert.True(sites >= 16, $"{sites} sites");
        }

        [Fact]
        public void ASiteWithoutAnArea_TakesItsScenariosRegion()
        {
            var scenario = new ScenarioDefinition { Id = "mod", StartingRegion = Data.ProductionRegion.Finnmark };
            Assert.Equal(12, scenario.ProductionAreaOf(new StartingSiteDefinition { Zone = "po3_lookalike" }));
            Assert.Equal(7, scenario.ProductionAreaOf(new StartingSiteDefinition { Zone = "z", ProductionArea = 7 }));
            Assert.Equal(1, ProductionAreas.DefaultForRegion(Data.ProductionRegion.Sørlandet));
            Assert.Equal(3, ProductionAreas.DefaultForRegion(Data.ProductionRegion.Vestland));
            Assert.Equal(6, ProductionAreas.DefaultForRegion(Data.ProductionRegion.Trøndelag));
            Assert.Equal(11, ProductionAreas.DefaultForRegion(Data.ProductionRegion.Troms));
        }
    }
}
