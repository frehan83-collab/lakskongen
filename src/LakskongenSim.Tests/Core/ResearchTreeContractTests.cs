using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// The research tree is one contract across data, engine, scenarios and UI
/// (review F1001/F1003/F1012/F1027). The shipped godot/data/research_nodes.json
/// used to replace the integrated tree with 25 nodes whose effect keys no
/// system read; scenarios granted ids that no longer existed; the price and
/// mortality effects were dead even on the code-defined tree.
/// </summary>
public class ResearchTreeContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static readonly JsonSerializerOptions LoaderOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static List<ResearchNode> ShippedTree()
        => JsonSerializer.Deserialize<List<ResearchNode>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "research_nodes.json")), LoaderOptions)!;

    private static List<ResearchNode> CodeTree() => new ResearchSystem().Nodes.Values.ToList();

    // ── Data contract ────────────────────────────────────────────────────────

    [Fact]
    public void ShippedJson_IsTheCodeDefinedTree()
    {
        var shipped = ShippedTree().ToDictionary(n => n.Id);
        var code = CodeTree();

        Assert.Equal(code.Count, shipped.Count);
        foreach (var node in code)
        {
            Assert.True(shipped.TryGetValue(node.Id, out var s), $"'{node.Id}' missing from research_nodes.json");
            Assert.Equal(node.Name, s!.Name);
            Assert.Equal(node.Description, s.Description);
            Assert.Equal(node.Branch, s.Branch);
            Assert.Equal(node.Tier, s.Tier);
            Assert.Equal(node.CostNOK, s.CostNOK);
            Assert.Equal(node.ResearchDays, s.ResearchDays);
            Assert.Equal(node.Prerequisites.OrderBy(x => x), s.Prerequisites.OrderBy(x => x));
            Assert.Equal(node.Effects.OrderBy(kv => kv.Key), s.Effects.OrderBy(kv => kv.Key));
        }
    }

    [Fact]
    public void ShippedJson_CarriesTheCodeTreesUnlocksAndScopes()
    {
        // F2-04: the research gates and the narrowed effects are part of the contract.
        var shipped = ShippedTree().ToDictionary(n => n.Id);
        foreach (var node in CodeTree())
        {
            Assert.Equal(node.Unlocks, shipped[node.Id].Unlocks);
            Assert.Equal(node.AppliesTo, shipped[node.Id].AppliesTo);
        }
        Assert.Contains(ShippedTree(), n => n.Unlocks.Count > 0);
        Assert.Contains(ShippedTree(), n => n.AppliesTo.Count > 0);
    }

    [Fact]
    public void EveryUnlock_IsAFarmTypeVariant_AndEveryScopeIsAUnitKind_CarryingOnlyUnitEffects()
    {
        foreach (var node in CodeTree())
        {
            Assert.All(node.Unlocks, id => Assert.Contains(id, FarmTypeIds.Variants));
            Assert.Empty(ResearchSystem.ScopeProblems(node));
        }
        // The check itself: an unknown kind and a company-wide effect on a scoped node are named.
        var bad = new ResearchNode
        {
            Id = "bad", AppliesTo = new() { "moon" },
            Effects = new() { [ResearchEffectKeys.MarketPriceBonus] = 0.1, [ResearchEffectKeys.LiceReduction] = 0.1 }
        };
        Assert.Equal(new[] { "unknown unit kind 'moon' in appliesTo", "effect 'market_price_bonus' cannot be scoped to a unit kind" },
            ResearchSystem.ScopeProblems(bad));
    }

    [Fact]
    public void TheFarmTypeNodes_GateTheirFamilies_AndActOnTheirOwnUnitsOnly()
    {
        var tree = CodeTree().ToDictionary(n => n.Id);
        Assert.Equal(new[] { "post_smolt", "hfs" }, tree["land-based-ras"].Unlocks);
        Assert.Equal(new[] { ResearchScopes.Land }, tree["land-based-ras"].AppliesTo);
        Assert.Equal(new[] { "of1" }, tree["deep-sea-farming"].Unlocks);
        Assert.Equal(new[] { ResearchScopes.Offshore }, tree["deep-sea-farming"].AppliesTo);
        Assert.All(tree.Values.Where(n => n.Id is not ("land-based-ras" or "deep-sea-farming")), n =>
        {
            Assert.Empty(n.Unlocks);
            Assert.Empty(n.AppliesTo);
        });
    }

    [Fact]
    public void EveryNode_HasAnEffect_AndEveryEffectKeyIsLive()
    {
        foreach (var node in CodeTree())
        {
            Assert.NotEmpty(node.Effects);
            Assert.Empty(ResearchSystem.UnknownEffectKeys(node.Effects.Keys));
        }
    }

    [Fact]
    public void EveryLiveKey_IsUsedByTheTree()
    {
        var used = CodeTree().SelectMany(n => n.Effects.Keys).ToHashSet();
        foreach (var key in ResearchEffectKeys.Live)
            Assert.Contains(key, used);
    }

    [Fact]
    public void Prerequisites_ExistAndSitInALowerTier()
    {
        var tree = CodeTree().ToDictionary(n => n.Id);
        foreach (var node in tree.Values)
        foreach (var pre in node.Prerequisites)
        {
            Assert.True(tree.TryGetValue(pre, out var p), $"'{node.Id}' requires unknown '{pre}'");
            Assert.True(p!.Tier < node.Tier, $"'{node.Id}' (T{node.Tier}) requires '{pre}' (T{p.Tier})");
        }
    }

    [Fact]
    public void EngineLoadsTheShippedTree()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 3);
        Assert.Equal(CodeTree().Select(n => n.Id).OrderBy(x => x), engine.Research.Nodes.Keys.OrderBy(x => x));
    }

    [Fact]
    public void ExampleModResearch_UsesOnlyLiveKeys()
    {
        var dir = Path.Combine(RepoRoot(), "godot", "mods");
        var files = Directory.GetFiles(dir, "research_*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var nodes = JsonSerializer.Deserialize<List<ModResearchNode>>(File.ReadAllText(file))!;
            foreach (var node in nodes)
                Assert.Empty(ResearchSystem.UnknownEffectKeys(node.Effects.Keys));
        }
    }

    // ── Scenario starting research (F1012) ──────────────────────────────────

    [Fact]
    public void ScenarioStartingResearch_ExistsInTheTree_AndIsUnlockedLive()
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: 5);
        var withResearch = probe.Scenario.All.Values.Where(s => s.StartingResearch.Length > 0).ToList();
        Assert.NotEmpty(withResearch);

        foreach (var scenario in withResearch)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: 5);
            var def = engine.Scenario.GetScenario(scenario.Id)!;
            def.ApplyStartingState(engine.State, engine);
            foreach (var id in def.StartingResearch)
            {
                Assert.True(engine.Research.Nodes.ContainsKey(id), $"{scenario.Id}: unknown research '{id}'");
                Assert.True(engine.Research.Nodes[id].Unlocked, $"{scenario.Id}: '{id}' not unlocked on the live tree");
                Assert.Contains(id, engine.State.UnlockedResearchIds);
            }
        }
    }

    [Fact]
    public void ScenarioStartingResearch_SurvivesTheResearchSync()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 6);
        var def = engine.Scenario.GetScenario("po3_red_sone")!;
        def.ApplyStartingState(engine.State, engine);

        engine.SyncResearchToState();

        Assert.Contains("sensor-networks", engine.State.UnlockedResearchIds);
        Assert.True(engine.MortalityReduction > 0.0, "starting research must act on the engine");
    }

    [Fact]
    public void ShippedScenarioJson_GrantsOnlyExistingResearch()
    {
        var tree = CodeTree().Select(n => n.Id).ToHashSet();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));
        foreach (var scenario in doc.RootElement.EnumerateArray())
        {
            if (!scenario.TryGetProperty("starting_research", out var ids)) continue;
            foreach (var id in ids.EnumerateArray())
                Assert.True(tree.Contains(id.GetString()!), $"{scenario.GetProperty("id")}: unknown research '{id}'");
        }
    }

    // ── Every live key has a consumer ───────────────────────────────────────

    private static SimulationEngine EngineWith(int seed, string? key = null, double value = 0.0)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(100_000_000m);
        engine.Initialize(enableEvents: false, seed: seed);
        if (key != null)
        {
            engine.Research.AddModNodes(new List<ModResearchNode>
            {
                new() { Id = "test_" + key, Name = key, Effects = new() { [key] = value } }
            });
            engine.Research.ForceUnlock("test_" + key);
            Assert.Equal(value, engine.GetResearchEffect(key));
        }
        return engine;
    }

    private static SeaSite StockedSite(SimulationEngine engine, int fish = 50_000, double weightKg = 1.0)
    {
        var site = new SeaSite("research-site", "zone-research") { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);
        var pop = new FishPopulation { Id = "research-pen" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(200, fish, weightKg, LifeStage.Seawater));
        site.Stock(pop);
        engine.State.Populations.Add(pop);
        return site;
    }

    private static double MeanWeight(SeaSite site)
        => site.Pens.Sum(p => p.GetTotalBiomassKg()) / Math.Max(1, site.Pens.Sum(p => p.TotalCount));

    [Fact]
    public void GrowthRateBonus_GrowsFishFaster()
    {
        var plain = EngineWith(11); var plainSite = StockedSite(plain);
        var boosted = EngineWith(11, ResearchEffectKeys.GrowthRateBonus, 0.2); var boostedSite = StockedSite(boosted);
        plain.Advance(60); boosted.Advance(60);
        Assert.True(MeanWeight(boostedSite) > MeanWeight(plainSite));
    }

    [Fact]
    public void FcrReduction_FeedsLessForTheSameGrowth()
    {
        var plain = EngineWith(12); StockedSite(plain);
        var lean = EngineWith(12, ResearchEffectKeys.FcrReduction, 0.2); StockedSite(lean);
        plain.Advance(60); lean.Advance(60);
        Assert.True(lean.Feed.TotalFeedUsedKg < plain.Feed.TotalFeedUsedKg * 0.9,
            $"{lean.Feed.TotalFeedUsedKg:N0} vs {plain.Feed.TotalFeedUsedKg:N0} kg");
    }

    [Fact]
    public void FeedWasteReduction_CutsThePrecisionFeedingPenalty()
    {
        var plain = EngineWith(13); StockedSite(plain);
        var tidy = EngineWith(13, ResearchEffectKeys.FeedWasteReduction, 0.9); StockedSite(tidy);
        plain.Advance(60); tidy.Advance(60);
        Assert.True(tidy.Feed.TotalFeedUsedKg < plain.Feed.TotalFeedUsedKg,
            $"{tidy.Feed.TotalFeedUsedKg:N0} vs {plain.Feed.TotalFeedUsedKg:N0} kg");
    }

    [Fact]
    public void MortalityReduction_RemovesThatFractionOfTheBaselineHazard()
    {
        // F1027: same model, 10% research reduction -> 10% fewer baseline deaths.
        var model = new MortalityModel();
        int plain = 1_000_000 - model.ApplyMortality(1_000_000, 100, LifeStage.Seawater, "", 0.0, 0.0);
        int reduced = 1_000_000 - model.ApplyMortality(1_000_000, 100, LifeStage.Seawater, "", 0.10, 0.0);
        Assert.InRange(reduced / (double)plain, 0.88, 0.92);

        // Capped: a huge summed reduction never removes the hazard entirely.
        Assert.True(MortalityModel.ResearchAdjustedRate(3e-4, 0.0, 0.0, 5.0, 0.0) > 0.0);

        var engine = EngineWith(14); var site = StockedSite(engine);
        var safer = EngineWith(14, ResearchEffectKeys.MortalityReduction, 0.5); var saferSite = StockedSite(safer);
        engine.Advance(120); safer.Advance(120);
        Assert.True(saferSite.Pens.Sum(p => p.TotalCount) > site.Pens.Sum(p => p.TotalCount));
    }

    [Fact]
    public void DiseaseResistance_RemovesThatFractionOfTheDiseaseHazard()
    {
        var model = new MortalityModel();
        model.SetDiseaseMortalityRate("sick", 0.01);
        int plain = 100_000 - model.ApplyMortality(100_000, 10, LifeStage.Seawater, "sick", 0.0, 0.0);
        int resistant = 100_000 - model.ApplyMortality(100_000, 10, LifeStage.Seawater, "sick", 0.0, 0.5);
        Assert.True(resistant < plain * 0.6, $"{resistant} vs {plain} deaths");
    }

    [Fact]
    public void MarketPriceBonus_RaisesThePriceWhereTheBandLeavesHeadroom()
    {
        // F1003: the premium used to be a bare fraction gated on > 1.0, so it never
        // applied. It now enters the realized price inside the market band, like
        // certification: where 5% fits under the band ceiling it shows in full.
        var plain = EngineWith(15);
        var premium = EngineWith(15, ResearchEffectKeys.MarketPriceBonus, 0.05);
        int fullPremiumDays = 0;
        for (int day = 0; day < 365; day++)
        {
            plain.Advance(1); premium.Advance(1);
            decimal p = plain.MarketSimulation.PricePerKg, q = premium.MarketSimulation.PricePerKg;
            Assert.True(q >= p, $"day {day}: premium price {q} below plain {p}");
            if (Math.Abs((double)(q - p * 1.05m)) < 0.01) fullPremiumDays++;
        }
        Assert.True(fullPremiumDays > 0, "the 5% premium never showed in full in a year");
    }

    [Fact]
    public void SustainabilityScoreBonus_AddsItsPoints()
    {
        var plain = EngineWith(16); StockedSite(plain);
        var green = EngineWith(16, ResearchEffectKeys.SustainabilityScoreBonus, 10); StockedSite(green);
        plain.Advance(21); green.Advance(21);
        Assert.True(plain.State.SustainabilityScore < 90, $"baseline score {plain.State.SustainabilityScore} leaves no room");
        Assert.Equal(plain.State.SustainabilityScore + 10, green.State.SustainabilityScore);
    }

    [Fact]
    public void LiceReduction_LowersTheSitesOwnLice()
    {
        static double Lice(SimulationEngine engine)
        {
            var site = StockedSite(engine);
            engine.Advance(150);
            site.Lice.SeedExternalInfection(2.0);
            engine.Advance(60);
            return site.Lice.AverageLicePerFish;
        }
        double plain = Lice(EngineWith(17));
        double treated = Lice(EngineWith(17, ResearchEffectKeys.LiceReduction, 0.5));
        Assert.True(treated < plain * 0.9, $"{treated:F4} vs {plain:F4}");
    }

    [Fact]
    public void CarbonReduction_CutsFuelAndPowerEmissions()
    {
        static double EnergyEmissions(SimulationEngine engine)
        {
            var site = StockedSite(engine);
            engine.Advance(30);
            return site.CarbonTracker.Emissions
                .Where(e => e.Category is "Scope1_Fuel" or "Scope2_Electricity")
                .Sum(e => e.KgCO2e);
        }
        double plain = EnergyEmissions(EngineWith(18));
        double cut = EnergyEmissions(EngineWith(18, ResearchEffectKeys.CarbonReduction, 0.5));
        Assert.True(plain > 0.0);
        Assert.InRange(cut / plain, 0.49, 0.51);
    }
}
