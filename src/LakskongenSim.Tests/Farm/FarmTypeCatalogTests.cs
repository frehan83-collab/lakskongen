using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-04 acceptance 5 (docs/FARM_TYPES_SPEC.md §1, §2, §6.2): the farm-type
/// catalogue has three families and seven variants, one set of axes for
/// every variant, each value computed from the registry's sourced reference
/// inputs and carrying their source keys, and every unlock gate (the research
/// gates from the research tree's unlocks).
/// </summary>
public class FarmTypeCatalogTests
{
    private static readonly CalibrationRegistry Registry = CalibrationRegistry.Default;

    private static IReadOnlyList<FarmTypeFamily> Catalog() => FarmTypeCatalog.Build(Registry, new ResearchSystem());

    private static FarmTypeVariant Variant(string id) => Catalog().SelectMany(f => f.Variants).Single(v => v.Id == id);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void ThreeFamilies_SevenVariants_InTheSpecsOrder()
    {
        var catalog = Catalog();
        Assert.Equal(new[] { "sjo", "land", "hav" }, catalog.Select(f => f.Id));
        Assert.Equal(new[] { "FARM_FAMILY_SJO", "FARM_FAMILY_LAND", "FARM_FAMILY_HAV" }, catalog.Select(f => f.NameKey));
        Assert.Equal(new[] { "open_pen", "closed_pen" }, catalog[0].Variants.Select(v => v.Id));
        Assert.Equal(new[] { "post_smolt", "hfs", "ras" }, catalog[1].Variants.Select(v => v.Id));
        Assert.Equal(new[] { "of1", "havfarm" }, catalog[2].Variants.Select(v => v.Id));
        Assert.Equal(FarmTypeIds.Variants, catalog.SelectMany(f => f.Variants).Select(v => v.Id));
        // Spec §7.5: RAS and Havfarm-class are stage 3; the core loop ships first.
        Assert.Equal(new[] { "ras", "havfarm" }, catalog.SelectMany(f => f.Variants).Where(v => v.Stage == 3).Select(v => v.Id));
        Assert.All(catalog.SelectMany(f => f.Variants), v =>
        {
            Assert.Equal(f(v), v.FamilyId);
            Assert.Equal("FARM_VARIANT_" + v.Id.ToUpperInvariant(), v.NameKey);
        });
        string f(FarmTypeVariant v) => catalog.Single(x => x.Variants.Contains(v)).Id;
    }

    [Fact]
    public void EveryVariant_HasTheSameAxes_EachWithSourceKeys()
    {
        foreach (var variant in Catalog().SelectMany(f => f.Variants))
        {
            Assert.Equal(FarmTypeIds.Axes, variant.Axes.Select(a => a.Axis));
            foreach (var axis in variant.Axes)
            {
                Assert.NotEmpty(axis.Sources);
                Assert.All(axis.Sources, k => Assert.False(string.IsNullOrWhiteSpace(k)));
                if (axis.Median == null)
                    Assert.Equal(FarmTypeCatalog.NoFigureNote, axis.NoteKey);
                else
                    Assert.True(double.IsFinite(axis.Median.Value) && axis.Median >= 0.0, $"{variant.Id}.{axis.Axis} = {axis.Median}");
            }
            // Bad cases are worse than the median: more capex, more days, less survival.
            var capex = variant.Axis(FarmTypeIds.CapexPerTonneYear);
            if (capex.Bad != null) Assert.True(capex.Bad > capex.Median, variant.Id);
            var days = variant.Axis(FarmTypeIds.BuildDays);
            Assert.True(days.Bad >= days.Median, variant.Id);
            var survival = variant.Axis(FarmTypeIds.Survival);
            Assert.True(survival.Bad < survival.Median && survival.Median <= 1.0, variant.Id);
        }
        // Only the axes with no published figure are empty: post-smolt cost, offshore energy.
        var empty = Catalog().SelectMany(f => f.Variants).SelectMany(v => v.Axes.Where(a => a.Median == null).Select(a => $"{v.Id}.{a.Axis}"));
        Assert.Equal(new[] { "post_smolt.opex_nok_per_kg_hog", "of1.energy_kwh_per_kg", "havfarm.energy_kwh_per_kg" }, empty);
    }

    [Fact]
    public void TheCardValues_AreTheSpecsDerivations_OfTheRegistrysInputs()
    {
        // Spec §2A: capacity cost per tonne of yearly HOG capacity including the licence (1.2 t HOG/yr per t MTB).
        // Open: (6 M + 3,120 t × 305 k) / (3,120 × 1.2) ≈ 256 k.
        Assert.Equal((6_000_000 + 3_120 * 305_000.0) / (3_120 * 1.2), Variant("open_pen").Axis(FarmTypeIds.CapexPerTonneYear).Median!.Value, 6);
        // OF1: (877 M + 8 × 13.8 M) / (6,240 × 1.2) ≈ 132 k (spec: ≈ 132 k).
        Assert.InRange(Variant("of1").Axis(FarmTypeIds.CapexPerTonneYear).Median!.Value, 131_000, 133_000);
        // HFS ≈ 262 k (L:R1); post-smolt ≈ 230 k per t of yearly capacity (spec §2A); RAS 320 k at FX 10.7.
        Assert.Equal(262_000, Variant("hfs").Axis(FarmTypeIds.CapexPerTonneYear).Median!.Value);
        Assert.InRange(Variant("post_smolt").Axis(FarmTypeIds.CapexPerTonneYear).Median!.Value, 225_000, 235_000);
        Assert.InRange(Variant("ras").Axis(FarmTypeIds.CapexPerTonneYear).Median!.Value, 315_000, 325_000);
        // Open pens: 64.68 NOK/kg round / 0.83 ≈ 78 NOK/kg HOG (spec §2A); offshore 1.3× (O:R37).
        Assert.InRange(Variant("open_pen").Axis(FarmTypeIds.OpexPerKg).Median!.Value, 77.5, 78.5);
        Assert.Equal(Variant("open_pen").Axis(FarmTypeIds.OpexPerKg).Median!.Value * 1.3, Variant("of1").Axis(FarmTypeIds.OpexPerKg).Median!.Value, 9);
        Assert.Equal(70.7, Variant("hfs").Axis(FarmTypeIds.OpexPerKg).Median);
        // 14.2 % a year over a 16-month cycle ≈ 18 % (spec §2B); OF1 good 93 %, bad 81 %.
        Assert.InRange(1.0 - Variant("open_pen").Axis(FarmTypeIds.Survival).Median!.Value, 0.175, 0.19);
        Assert.Equal((0.93, 0.81), (Variant("of1").Axis(FarmTypeIds.Survival).Median, Variant("of1").Axis(FarmTypeIds.Survival).Bad));
        // Capacity: 6,240 t MTB × 1.2 = 7,488 t HOG/yr; a full HFS lot 7,900 t; a RAS hall ≈ 4,300 t (spec §2A).
        Assert.Equal(7_488, Variant("of1").Axis(FarmTypeIds.Capacity).Median!.Value, 6);
        Assert.Equal(7_900, Variant("hfs").Axis(FarmTypeIds.Capacity).Median);
        Assert.InRange(Variant("ras").Axis(FarmTypeIds.Capacity).Median!.Value, 4_250, 4_350);
        Assert.Equal(FarmTypeCatalog.PostSmoltBasis, Variant("post_smolt").CapacityBasis);
        // Build: OF1 548 + 77 + 45 = 670 days (spec §2A, derived); lice: open 1.0, land 0.
        Assert.Equal(670, Variant("of1").Axis(FarmTypeIds.BuildDays).Median);
        Assert.Equal(1.0, Variant("open_pen").Axis(FarmTypeIds.ExternalLice).Median);
        Assert.Equal(0.0, Variant("hfs").Axis(FarmTypeIds.ExternalLice).Median);
        // Bad case capex: the unit's capex at the overrun's 90th percentile.
        var p90 = Registry.Facilities.Common.Construction.OverrunP90;
        Assert.Equal(262_000 * (1 + p90), Variant("hfs").Axis(FarmTypeIds.CapexPerTonneYear).Bad!.Value, 6);
    }

    [Fact]
    public void TheSourceKeys_AreTheRegistrysAttributes_AndExistInTheDossiers()
    {
        var open = Variant("open_pen").Axis(FarmTypeIds.CapexPerTonneYear);
        Assert.Equal(new[] { "code", "V:1", "GB" }, open.Sources);
        Assert.True(open.Derived);
        Assert.Equal(new[] { "L:R1" }, Variant("hfs").Axis(FarmTypeIds.OpexPerKg).Sources);
        Assert.False(Variant("hfs").Axis(FarmTypeIds.OpexPerKg).Derived);
        Assert.Contains("O:R4", Variant("of1").Axis(FarmTypeIds.CapexPerTonneYear).Sources);
        Assert.Contains("V:2", Variant("of1").Axis(FarmTypeIds.CapexPerTonneYear).Sources);

        // Every L:/O: key is in the research dossier's source lists, every V: key in the spec's verified list.
        string dossier = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "research", "FARM_TYPES_RESEARCH.md"));
        int split = dossier.IndexOf("## research-offshore", StringComparison.Ordinal);
        string land = dossier[..split], offshore = dossier[split..];
        string spec = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "FARM_TYPES_SPEC.md"));
        var keys = Catalog().SelectMany(f => f.Variants).SelectMany(v => v.Axes).SelectMany(a => a.Sources).Distinct().ToList();
        Assert.Contains("GB", keys);
        foreach (var key in keys)
        {
            if (key is "GB" or "code") continue;
            var m = Regex.Match(key, @"^(L|O|V):([A-Z]*\d+[a-z]?)$");
            Assert.True(m.Success, $"source key '{key}' is not L:x, O:x, V:n, code or GB");
            string id = m.Groups[2].Value;
            switch (m.Groups[1].Value)
            {
                case "L": Assert.Matches(new Regex(@"(?m)^- " + Regex.Escape(id) + @"[.:] "), land); break;
                case "O": Assert.Matches(new Regex(@"(?m)^- " + Regex.Escape(id) + @"[.:] "), offshore); break;
                case "V": Assert.Contains("V:" + id + " ", spec); break;
            }
        }
    }

    [Fact]
    public void TheResearchGates_ComeFromTheTreesUnlocks()
    {
        var research = new ResearchSystem();
        var gates = Catalog().SelectMany(f => f.Variants)
            .ToDictionary(v => v.Id, v => v.Gates.Where(g => g.Kind == FarmTypeGateKind.Research).Select(g => g.Subject).ToList());
        Assert.Equal(new[] { "land-based-ras" }, gates["post_smolt"]);
        Assert.Equal(new[] { "land-based-ras" }, gates["hfs"]);
        Assert.Equal(new[] { "deep-sea-farming" }, gates["of1"]);
        Assert.Empty(gates["open_pen"]);
        Assert.Empty(gates["closed_pen"]);
        Assert.Empty(gates["ras"]);
        Assert.Empty(gates["havfarm"]);
        // The other way round: every node's unlocks are research gates of those variants.
        foreach (var node in research.Nodes.Values)
            foreach (var id in node.Unlocks)
                Assert.Contains(node.Id, gates[id]);
        // Every variant shows its money gate (FR3: locked cards show every gate, money included).
        Assert.All(Catalog().SelectMany(f => f.Variants), v => Assert.Single(v.Gates, g => g.Kind == FarmTypeGateKind.Funds));
    }

    // ── The engine's view: every gate on the game's state ─────────────────────

    private static SimulationEngine NewGame(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        Assert.True(engine.StartScenario("po3_red_sone").Ok); // starts with deep-sea-farming researched
        return engine;
    }

    private static FarmTypeVariantStatus Status(SimulationEngine engine, string id) => engine.FarmTypeStatus(id)!;

    [Fact]
    public void AFjordStart_HasTheOpenPen_AndEveryOtherVariantLockedByItsGates()
    {
        var engine = NewGame(3);
        Assert.True(Status(engine, "open_pen").Unlocked);
        Assert.True(Status(engine, "open_pen").Affordable); // 12 M in the bank, a site costs 6 M
        foreach (var id in new[] { "closed_pen", "post_smolt", "hfs", "ras", "of1", "havfarm" })
            Assert.False(Status(engine, id).Unlocked, id);
        // OF1: the research is done in this scenario; the harvest, the escape-free year and the licence are not.
        var of1 = Status(engine, "of1").Gates.ToDictionary(g => g.Gate.Kind);
        Assert.True(of1[FarmTypeGateKind.Research].Met);
        Assert.False(of1[FarmTypeGateKind.FirstHarvest].Met);
        Assert.Equal((false, 0.0), (of1[FarmTypeGateKind.EscapeFreeDays].Met, of1[FarmTypeGateKind.EscapeFreeDays].Current));
        Assert.False(of1[FarmTypeGateKind.DevelopmentLicence].Met);
        Assert.Equal(877_000_000 * 0.35, of1[FarmTypeGateKind.Funds].Gate.Required, 3);
        Assert.False(of1[FarmTypeGateKind.Funds].Met);
    }

    [Fact]
    public void TheLandGates_OpenWithResearch_AHarvest_AnOperatingModule_AndAZonedPad()
    {
        var engine = NewGame(4);
        engine.ReceiveLoan(3_000_000_000m);
        Assert.False(Status(engine, "post_smolt").Unlocked);
        engine.Research.ForceUnlock("land-based-ras");
        Assert.False(Status(engine, "post_smolt").Unlocked);       // no harvest yet
        engine.State.HarvestBatchesSold = 1;
        Assert.True(Status(engine, "post_smolt").Unlocked);
        Assert.True(Status(engine, "closed_pen").Unlocked);

        var hfs = Status(engine, "hfs").Gates.ToDictionary(g => g.Gate.Kind);
        Assert.True(hfs[FarmTypeGateKind.Research].Met);
        Assert.False(hfs[FarmTypeGateKind.OperatingPostSmoltModule].Met);
        Assert.True(engine.BuildPostSmoltFacility("Post").Ok);
        engine.PostSmoltSites.Single().ConstructionProgress = 1.0;
        hfs = Status(engine, "hfs").Gates.ToDictionary(g => g.Gate.Kind);
        Assert.Equal((true, 1.0), (hfs[FarmTypeGateKind.OperatingPostSmoltModule].Met, hfs[FarmTypeGateKind.OperatingPostSmoltModule].Current));
        bool anyPreZoned = engine.SitePlots.Any(p => p.Kind == PlotKind.LandPad && p.IsFree && p.PreZoned);
        Assert.Equal(anyPreZoned, hfs[FarmTypeGateKind.ZonedPad].Met);
        // A zoning case approved for a free pad zones it.
        var pad = engine.SitePlots.First(p => p.Kind == PlotKind.LandPad && p.IsFree && !p.PreZoned);
        Assert.True(engine.SubmitPermitCase(PermitKind.Zoning, pad.Id, "", new PermitTerms(0, 0, 0.0)).Ok);
        engine.Advance(1);
        Assert.True(Status(engine, "hfs").Gates.Single(g => g.Gate.Kind == FarmTypeGateKind.ZonedPad).Met);
        Assert.True(Status(engine, "hfs").Unlocked);
    }

    [Fact]
    public void ReadingTheCatalogue_ChangesNothing()
    {
        var engine = NewGame(5);
        engine.Advance(10);
        string before = SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        for (int i = 0; i < 3; i++) engine.GetFarmTypeCatalog();
        Assert.Equal(before, SaveStateComparer.Serialize(engine));
        Assert.Equal(streams, engine.Rng.CaptureStates());
    }
}
