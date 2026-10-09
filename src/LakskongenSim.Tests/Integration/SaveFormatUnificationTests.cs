using System;
using System.IO;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// One-serializer unification (Phase 1 wave 5): the library and the game now
/// share SaveJson.Options. New saves write PascalCase + enum NAMES, while
/// legacy v1/v2 saves (camelCase, numeric enums) keep loading through the
/// same options — compatibility is a property of the serializer, not of a
/// payload migration.
/// </summary>
public class SaveFormatUnificationTests
{
    private static SimulationEngine NewStockedEngine(string siteId)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite(siteId, zone.Id));
        Assert.True(engine.StockSmolts(siteId, 500, "standard").Ok);
        return engine;
    }

    [Fact]
    public void UnifiedSerializer_WritesEnumNames_NotIntegers()
    {
        var engine = NewStockedEngine("site-1");
        engine.PrepareForSave();
        string tmp = Path.Combine(Path.GetTempPath(), "lks_sfmt_" + Guid.NewGuid().ToString("N"));
        try
        {
            var sl = new SaveLoadManager(tmp);
            sl.SaveGame(engine.State, "save");
            string json = File.ReadAllText(Path.Combine(tmp, "save.json"));

            // Enum names on disk:
            Assert.Contains("\"Stage\": \"Smolt\"", json);
            // ...and the runtime cadence snapshot rides the same payload:
            Assert.Contains("\"Runtime\"", json);
            Assert.Contains("\"InDayClocks\"", json);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void LegacyCamelCaseNumericEnumSave_StillDeserializes()
    {
        // v2-era fragment: camelCase keys + numeric enum value, exactly what
        // the old game serializer produced. Case-insensitive props bind the
        // camel keys; the string enum converter accepts the numeric value.
        const string legacy = """
            {
              "day": 42,
              "randomSeed": 7,
              "sustainabilityCertification": 1,
              "sustainabilityScore": 61,
              "seaSites": [
                { "id": "site-1", "zoneId": "z1", "pens": [], "lice": { "adultFemalePerFish": 0.1 } }
              ],
              "zones": [ { "id": "z1", "name": "Test" } ],
              "populations": [],
              "eventLog": [],
              "wellboatJobs": [],
              "crew": [],
              "activeBlooms": [],
              "activeCrises": [],
              "activeLoans": [],
              "taxHistory": [],
              "acquisitions": [],
              "licenses": [],
              "recentMarketReports": [],
              "achievedVisualMilestones": [],
              "competitorCompanies": [],
              "factionRelations": {},
              "insurancePolicies": [],
              "bloomMitigations": [],
              "growthState": null,
              "mortalityState": null,
              "runtime": null
            }
            """;
        var state = JsonSerializer.Deserialize<GameState>(legacy, SaveJson.Options);
        Assert.NotNull(state);
        Assert.Equal(42, state.Day);
        Assert.Equal(7, state.RandomSeed);
        Assert.Equal((int)LakskongenSim.Sustainability.CertificationLevel.BasicCompliance,
            (int)state.SustainabilityCertification);
        Assert.Single(state.SeaSites);
        Assert.Equal("site-1", state.SeaSites[0].Id);
    }

    [Fact]
    public void UnifiedSerializeRoundTrip_PreservesRuntimeSnapshot()
    {
        var engine = NewStockedEngine("site-1");
        engine.Advance(8);
        engine.PrepareForSave();

        var json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
        var back = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options);
        Assert.NotNull(back?.Runtime);
        Assert.Equal(engine.State.Runtime.InDayClocks, back.Runtime.InDayClocks);
    }
}