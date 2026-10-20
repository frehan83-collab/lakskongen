using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.FacilityFinance;

/// <summary>
/// F2-09: development licences, their conversion and placement
/// (docs/FARM_TYPES_SPEC.md §2C "Licence / permit path", §3.4, §6.1 acts 3-4,
/// §7.1 F2-09 acceptance 1-6). The round opens after the gate and draws its
/// rival field from a private stream; the score breakdown shown before filing
/// is the one the case is decided on; an award brings eight free, unpooled,
/// rent-free licences that expire after 15 years; conversion follows the
/// checklist (mortality against the area's live figure), asks the CPI-indexed
/// fee within 90 days and may be appealed; converted licences are placed in a
/// production area before use and then pool, count in its traffic light and
/// pay the rent.
/// </summary>
public class DevelopmentLicenceTests
{
    private static readonly OffshoreLicencesCalibration L = OffshoreCalibration.Default.Licences;
    private static readonly FarmTypeCatalogCalibration K = CalibrationRegistry.Load().Facilities.Common.Catalog;
    private readonly ITestOutputHelper _out;

    public DevelopmentLicenceTests(ITestOutputHelper output) => _out = output;

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>A rich bare engine that meets the round's gate but the escape-free year (research done, a first harvest sold).</summary>
    private static SimulationEngine GatedEngine(int seed, bool? rivalOffshore = null, bool events = false, decimal cash = 5_000_000_000m)
    {
        var engine = OffshoreTestUnits.RichEngine(seed, events, cash);
        engine.Research.ForceUnlock(SimulationEngine.DeepSeaResearchId);
        engine.State.HarvestBatchesSold = 1;
        engine.State.RivalOffshore = rivalOffshore;
        return engine;
    }

    /// <summary>
    /// The PO3 career game (<see cref="PlayerScriptProbe.CareerScenarioId"/>:
    /// fjord sites, zones, production area 3) with 4 bn more cash; with
    /// <paramref name="gated"/> it meets the round's gate but the escape-free
    /// year. Its rival flag is the scenario's (off) unless given.
    /// </summary>
    private static SimulationEngine CareerEngine(int seed, bool gated = true, bool? rivalOffshore = null)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        engine.FinanceManager.AddRevenue(4_000_000_000m);
        if (gated)
        {
            engine.Research.ForceUnlock(SimulationEngine.DeepSeaResearchId);
            engine.State.HarvestBatchesSold = 1;
        }
        if (rivalOffshore.HasValue) engine.State.RivalOffshore = rivalOffshore;
        return engine;
    }

    /// <summary>Advance a gated engine until its round is open.</summary>
    private static DevelopmentLicenceRound OpenRound(SimulationEngine engine)
    {
        int guard = 0;
        while (engine.OpenDevelopmentRound() == null)
        {
            engine.Advance(1);
            Assert.True(++guard < 1_000, "the round never opened");
        }
        return engine.OpenDevelopmentRound()!;
    }

    /// <summary>
    /// A commissioned OF1-class unit on a development-licence award (its eight
    /// licences bound), empty, its standing orders cleared but the net
    /// inspection, its incident hazards held at 0.
    /// </summary>
    private static (SimulationEngine Engine, SeaSite Site) AwardedUnit(int seed, bool career = false)
    {
        var engine = career ? CareerEngine(seed, gated: false) : OffshoreTestUnits.RichEngine(seed);
        var site = OffshoreTestUnits.Commissioned(engine);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        Assert.True(engine.SetDriftsplan(site.Id, OffshorePolicies.NetInspection).Ok);
        var award = engine.AwardDevelopmentLicencesForTests(site.PlotId);
        engine.BindAwardToUnit(award, site);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;
        return (engine, site);
    }

    /// <summary>Stock a zone, grow the fish a little, harvest the unit and let a day pass (the unit seen empty: one completed cycle).</summary>
    private static CommandResult GrowAndHarvest(SimulationEngine engine, SeaSite site, int fish = 50_000, int days = 40)
    {
        Assert.True(engine.StockOffshoreFarm(site.Id, fish).Ok);
        engine.Advance(days);
        var harvested = engine.HarvestOffshoreFarm(site.Id);
        engine.Advance(1);
        return harvested;
    }

    /// <summary>The checklist's unmet criteria, for a message.</summary>
    private static string Unmet(SimulationEngine engine, SeaSite site)
        => string.Join(", ", engine.ConversionChecklist(site).Where(c => !c.Met).Select(c => $"{c.Key} {c.Value:G4}/{c.Target:G4}"));

    /// <summary>Advance until the case is decided (no longer pending).</summary>
    private static PermitCase AdvanceToDecision(SimulationEngine engine, string caseId)
    {
        var permit = engine.State.PermitCases.Single(c => c.Id == caseId);
        int guard = 0;
        while (permit.IsPending)
        {
            engine.Advance(1);
            Assert.True(++guard < 400, "the case was never decided");
        }
        return permit;
    }

    /// <summary>A fjord zone of the game in the unit's production area.</summary>
    private static Zone ZoneInAreaOf(SimulationEngine engine, SeaSite site)
    {
        int area = engine.ProductionAreaOfSite(site);
        var zone = engine.State.Zones.FirstOrDefault(z => engine.ProductionAreaOf(z) == area);
        Assert.True(zone != null, $"no fjord zone of the game lies in production area {area}");
        return zone!;
    }

    // ── Acceptance 1: the round and its rival field ────────────────────────

    /// <summary>
    /// Acceptance 1: no round without the research or without a first harvest
    /// sold; with both it opens on the day the company has gone 365 days
    /// without an escape (an escape at a fjord site on day 200 moves it to day
    /// 565), open for two years; past its closing day it is closed and takes
    /// no application.
    /// </summary>
    [Fact]
    public void TheRound_OpensOnlyAfterTheGate_AndClosesAfterTwoYears()
    {
        var noResearch = OffshoreTestUnits.RichEngine(201);
        noResearch.State.HarvestBatchesSold = 1;
        var noHarvest = OffshoreTestUnits.RichEngine(201);
        noHarvest.Research.ForceUnlock(SimulationEngine.DeepSeaResearchId);
        noResearch.Advance(400);
        noHarvest.Advance(400);
        Assert.Empty(noResearch.DevelopmentRounds);
        Assert.Empty(noHarvest.DevelopmentRounds);

        var engine = GatedEngine(201);
        engine.Advance(K.Of1.RoundEscapeFreeDays);
        Assert.Empty(engine.DevelopmentRounds);
        engine.Advance(1);
        var round = Assert.Single(engine.DevelopmentRounds);
        Assert.Equal((K.Of1.RoundEscapeFreeDays, K.Of1.RoundEscapeFreeDays + L.RoundOpenDays), (round.OpenedDay, round.ClosesDay));
        Assert.Same(round, engine.OpenDevelopmentRound());
        Assert.DoesNotContain(engine.State.EventLog, e => e.Day == round.OpenedDay && e.Name.Contains("Søknadsrunde", StringComparison.Ordinal));

        var late = CareerEngine(201);
        Assert.False(late.RivalOffshoreActive);
        late.Advance(200);
        late.FjordSites().First().RecordEscape(500, late.State.Day);
        late.Advance(K.Of1.RoundEscapeFreeDays);
        Assert.Empty(late.DevelopmentRounds);
        late.Advance(1);
        Assert.Equal(200 + K.Of1.RoundEscapeFreeDays, Assert.Single(late.DevelopmentRounds).OpenedDay);

        round.ClosesDay = engine.State.Day; // the two years run out today: today still takes applications
        Assert.True(round.IsOpenOn(engine.State.Day));
        engine.Advance(1);
        Assert.Null(engine.OpenDevelopmentRound());
        engine.Advance(1);
        Assert.Equal(DevelopmentRoundStatus.Closed, round.Status);
        Assert.Null(engine.PreviewDevelopmentApplication(DevelopmentConcepts.Of1, "off_1"));
        Assert.Equal(SimulationEngine.OffshoreReasons.RoundClosed, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1").ErrorKey);
        engine.Advance(30);
        Assert.Single(engine.DevelopmentRounds); // one round in stage 2
    }

    /// <summary>
    /// Acceptance 1 and 2: at least one rival applies in at least 80 % of
    /// seeds (200 seeds of the engine's own draw; the calibrated no-applicant
    /// share is 10 %); the engine's field is that draw for its seed, the same
    /// in two games of the seed, and the same field (concepts, strengths,
    /// order) in a game of the seed whose round opens later, its filing days
    /// moved with the opening.
    /// </summary>
    [Fact]
    public void TheRivalField_IsTheSeeds_AndAtLeastOneRivalAppliesInFourFifthsOfSeeds()
    {
        var roster = new List<(string Id, string Name)> { ("a", "A"), ("b", "B"), ("c", "C") };
        int withRival = 0;
        var scores = new List<double>();
        for (int seed = 1; seed <= 200; seed++)
        {
            var field = SimulationEngine.DrawRivalField(seed, "dlr_1", 365, L, roster, atLeastOne: false);
            Assert.InRange(field.Count, 0, L.RivalApplicantsMax);
            Assert.All(field, r => Assert.InRange(r.Score, L.RivalScoreMin, L.RivalScoreMax));
            Assert.All(field, r => Assert.InRange(r.SubmittedDay, 365, 365 + L.RivalSubmissionWindowDays - 1));
            Assert.All(field, r => Assert.Equal(r.SubmittedDay + L.AwardDelayDays, r.DecisionDay));
            if (field.Count > 0) withRival++;
            scores.AddRange(field.Select(r => r.Score));
            Assert.NotEmpty(SimulationEngine.DrawRivalField(seed, "dlr_1", 365, L, roster, atLeastOne: true));
        }
        _out.WriteLine($"seeds with a rival application: {withRival}/200; rival scores {scores.Min():F2}-{scores.Max():F2}, mean {scores.Average():F2}");
        Assert.True(withRival >= 160, $"only {withRival}/200 seeds drew a rival application");

        var a = CareerEngine(211);
        var b = CareerEngine(211);
        var later = CareerEngine(211);
        later.Advance(100);
        later.FjordSites().First().RecordEscape(10, later.State.Day);
        var ra = OpenRound(a);
        var rb = OpenRound(b);
        var rl = OpenRound(later);
        var engineRoster = a.CompetitorAI?.Competitors?.Select(c => (c.Id, c.Name)).ToList() ?? new List<(string, string)>();
        var expected = SimulationEngine.DrawRivalField(211, ra.Id, ra.OpenedDay, L, engineRoster, atLeastOne: false);
        string Of(IEnumerable<RivalApplication> f, int shift = 0)
            => string.Join(";", f.Select(r => $"{r.RivalId}|{r.Concept}|{r.SubmittedDay - shift}|{r.Score:R}|{r.DecisionDay - shift}"));
        Assert.Equal(Of(expected), Of(ra.Rivals));
        Assert.Equal(Of(ra.Rivals), Of(rb.Rivals));
        Assert.Equal(100, rl.OpenedDay - ra.OpenedDay);
        Assert.Equal(Of(ra.Rivals.Select(r => new RivalApplication { Concept = r.Concept, Score = r.Score, SubmittedDay = r.SubmittedDay, DecisionDay = r.DecisionDay })),
            Of(rl.Rivals.Select(r => new RivalApplication { Concept = r.Concept, Score = r.Score, SubmittedDay = r.SubmittedDay, DecisionDay = r.DecisionDay }), 100));
    }

    /// <summary>
    /// Acceptance 1: with <c>rivalOffshore</c> on, the round's strongest rival
    /// is awarded on its decision day and takes off_2 (the plot claimed by the
    /// rival, the company's application for it refused plot_taken), and no
    /// other rival is; with it off, the same seed's field decides with no
    /// award and off_2 stays free. The flag is off in the demo scenarios, on
    /// in the others, and a scenario rule or the saved flag overrides it.
    /// </summary>
    [Fact]
    public void RivalOffshore_On_OneRivalAwardTakesOff2_Off_None()
    {
        var on = GatedEngine(221, rivalOffshore: true);
        var off = GatedEngine(221, rivalOffshore: false);
        var roundOn = OpenRound(on);
        var roundOff = OpenRound(off);
        Assert.True(roundOn.RivalAwards);
        Assert.False(roundOff.RivalAwards);
        Assert.NotEmpty(roundOn.Rivals);
        var strongest = SimulationEngine.StrongestRival(roundOn)!;
        Assert.Equal(roundOn.Rivals.Max(r => r.Score), strongest.Score);

        int last = Math.Max(roundOn.Rivals.Max(r => r.DecisionDay), roundOff.Rivals.Count == 0 ? 0 : roundOff.Rivals.Max(r => r.DecisionDay));
        on.Advance(strongest.DecisionDay - on.State.Day);
        Assert.Empty(on.DevelopmentAwards);
        on.Advance(1);
        var award = Assert.Single(on.DevelopmentAwards);
        Assert.Equal((strongest.RivalId, "off_2", false, strongest.DecisionDay), (award.RivalId, award.PlotId, award.IsCompany, award.AwardedDay));
        Assert.True(strongest.Awarded);
        var off2 = on.SitePlots.Single(p => p.Id == "off_2");
        Assert.False(off2.IsFree);
        Assert.Equal("rival:" + strongest.RivalId, off2.OwnerUnitId);
        Assert.Equal(SimulationEngine.OffshoreReasons.PlotTaken, on.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_2").ErrorKey);
        Assert.DoesNotContain(on.State.LicenceHoldings, l => l.AwardId == award.Id); // a rival's licences are not the company's
        on.Advance(Math.Max(1, last - on.State.Day + 1));
        Assert.Single(on.DevelopmentAwards);
        Assert.All(roundOn.Rivals, r => Assert.True(r.Decided));
        Assert.Single(roundOn.Rivals, r => r.Awarded);

        off.Advance(Math.Max(1, last - off.State.Day + 1));
        Assert.All(roundOff.Rivals, r => Assert.True(r.Decided && !r.Awarded));
        Assert.Empty(off.DevelopmentAwards);
        Assert.True(off.SitePlots.Single(p => p.Id == "off_2").IsFree);

        foreach (var (scenario, active) in new[] { ("po3_red_sone", false), ("forste_sesong", false), ("lofoten_eventyr", true) })
        {
            var game = OffshoreTestUnits.RichEngine(222);
            Assert.True(game.StartScenario(scenario).Ok);
            Assert.Equal(active, game.RivalOffshoreActive);
            game.State.RivalOffshore = !active;
            Assert.Equal(!active, game.RivalOffshoreActive);
        }
        Assert.False(OffshoreTestUnits.RichEngine(223).RivalOffshoreActive); // no scenario: a test bench, no rival
    }

    // ── Acceptance 2: the score shown is the score decided ─────────────────

    /// <summary>
    /// Acceptance 2: the breakdown the company sees before filing (novelty,
    /// escape-free months, lice compliance, financing, against the field's
    /// band and bar) is the score the case is filed and decided with, part by
    /// part; a dossier at the bar is awarded after 180 days with eight free
    /// development licences of 780 t (unbound, unpooled, expiring 15 years
    /// on, nothing paid), and construction must start within 365 days.
    /// </summary>
    [Fact]
    public void ThePreview_IsTheEngineScore_AndAnAwardBringsEightFreeLicences()
    {
        var engine = GatedEngine(231);
        var round = OpenRound(engine);
        var preview = engine.PreviewDevelopmentApplication(DevelopmentConcepts.Of1, "off_1")!;
        Assert.NotNull(preview);
        bool earlierOf1 = round.Rivals.Any(r => r.Concept == DevelopmentConcepts.Of1 && r.SubmittedDay < engine.State.Day);
        Assert.Equal(earlierOf1 ? 0.0 : L.NoveltyBonus, preview.Novelty);
        Assert.Equal(engine.State.Day / (SimCalendar.DaysPerYear / 12.0), preview.EscapeFreeMonths, 12);
        Assert.Equal(L.EscapeFreeWeight * Math.Min(1.0, preview.EscapeFreeMonths / L.EscapeFreeFullMonths), preview.EscapeFree, 12);
        Assert.InRange(preview.LiceComplianceShare, 0.0, 1.0);
        Assert.Equal(L.LiceComplianceWeight * preview.LiceComplianceShare, preview.LiceCompliance, 12);
        decimal need = K.Of1.UnitCapexNok * (decimal)K.Of1.ConsortiumEquityShare * (decimal)L.FinancingShareOfEquityCapex;
        Assert.Equal((double)(engine.FinanceManager.Balance / need), preview.FinancingCoverage, 9);
        Assert.Equal(L.FinancingWeight, preview.Financing, 12); // 5 bn covers the need
        Assert.Equal(preview.Novelty + preview.EscapeFree + preview.LiceCompliance + preview.Financing, preview.Total, 12);
        Assert.Equal(round.AwardBar(L), preview.AwardBar);
        Assert.Equal(round.FieldMedian(), preview.FieldMedian);
        Assert.Equal(preview.Total >= preview.AwardBar, preview.WouldBeAwarded);
        Assert.True(preview.WouldBeAwarded, $"a rich, escape-free company scored {preview.Total:F2} against {preview.AwardBar:F2}");
        _out.WriteLine($"score {preview.Total:F3} (novelty {preview.Novelty}, escape-free {preview.EscapeFree:F3}, lice {preview.LiceCompliance:F3}, financing {preview.Financing:F3}); field {preview.FieldMin:F2}/{preview.FieldMedian:F2}/{preview.FieldMax:F2}, bar {preview.AwardBar:F2}, band {preview.Band}");

        var filed = engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1");
        Assert.True(filed.Ok, filed.Message);
        Assert.Equal(preview.Total, filed.Value);
        Assert.Equal(engine.State.Day + L.AwardDelayDays, filed.Value2);
        var permit = engine.State.PermitCases.Single(c => c.Id == filed.Id);
        Assert.Equal(PermitKind.DevelopmentLicence, permit.Kind);
        Assert.Equal(preview.Total, permit.Conditions["score"]);
        Assert.Equal(preview.AwardBar, permit.Conditions["bar"]);
        Assert.Equal(preview.Novelty, permit.Conditions["novelty"]);
        Assert.Equal(preview.EscapeFree, permit.Conditions["escape_free"]);
        Assert.Equal(preview.LiceCompliance, permit.Conditions["lice_compliance"]);
        Assert.Equal(preview.Financing, permit.Conditions["financing"]);
        Assert.Equal(permit.SubmittedDay + L.AwardDelayDays, permit.DecisionDay);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyApplied, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1").ErrorKey);

        var licenceLines = new List<string>();
        engine.PnlBooked += (day, category, amount, income) => { if (category.Contains("licen", StringComparison.Ordinal)) licenceLines.Add(category); };
        int holdings = engine.State.LicenceHoldings.Count;
        double pooledBefore = engine.AreaLicenceMtbTonnes(engine.State.HomeProductionArea);
        AdvanceToDecision(engine, filed.Id);
        Assert.Equal(PermitStatus.Approved, permit.Status);
        engine.Advance(1);
        var award = Assert.Single(engine.DevelopmentAwards);
        Assert.True(award.IsCompany);
        Assert.Equal((filed.Id, "off_1", DevelopmentConcepts.Of1, permit.DecidedDay, permit.DecidedDay + L.ConstructionStartDays),
            (award.CaseId, award.PlotId, award.Concept, award.AwardedDay, award.ConstructionDeadlineDay));
        Assert.Equal(preview.Total, award.Score);
        var licences = engine.State.LicenceHoldings.Skip(holdings).ToList();
        Assert.Equal(K.Of1.Licences, licences.Count);
        Assert.All(licences, l =>
        {
            Assert.Equal((LicenceKind.Development, award.Id, "", 0m, K.LicenceSizeTonnesMtb), (l.Kind, l.AwardId, l.BoundUnitId, l.PricePaidNok, l.MtbTonnes));
            Assert.Equal(award.AwardedDay + L.DevelopmentLicenceYears * SimCalendar.DaysPerYear, l.ExpiresDay);
            Assert.Equal(LicenceOrigin.DevelopmentRound, l.Origin);
        });
        Assert.Empty(licenceLines);
        Assert.Equal(pooledBefore, engine.AreaLicenceMtbTonnes(engine.State.HomeProductionArea));
        Assert.Equal(1, engine.DevelopmentAwardsWaiting(DevelopmentConcepts.Of1));
        Assert.Same(award, engine.BuildableAward("off_1", DevelopmentConcepts.Of1));
    }

    /// <summary>
    /// Acceptance 2: the band reads the field: "strong" at or above the
    /// strongest rival, "middle" at or above the bar (the median, not below
    /// the floor), "weak" below it; a weak dossier (no novelty after an
    /// earlier OF1-class rival, the bar at a strong median) is refused after
    /// 180 days and brings no award.
    /// </summary>
    [Fact]
    public void TheBand_ReadsTheField_AndAWeakDossierIsRefused()
    {
        var engine = GatedEngine(241);
        var round = OpenRound(engine);
        int day = engine.State.Day;
        RivalApplication Rival(string concept, double score) => new()
        {
            RivalId = "r" + score, RivalName = "R", Concept = concept, SubmittedDay = day - 1, Score = score, DecisionDay = day + 10_000,
        };
        round.Rivals = new List<RivalApplication> { Rival(DevelopmentConcepts.Havfarm, 0.50), Rival(DevelopmentConcepts.Havfarm, 0.60), Rival(DevelopmentConcepts.Havfarm, 0.70) };
        var strong = engine.PreviewDevelopmentApplication(DevelopmentConcepts.Of1, "off_1")!;
        Assert.Equal((0.60, 0.70, "strong", true), (strong.AwardBar, strong.FieldMax, strong.Band, strong.WouldBeAwarded));
        Assert.Equal(L.NoveltyBonus, strong.Novelty);

        round.Rivals = new List<RivalApplication> { Rival(DevelopmentConcepts.Havfarm, 0.10), Rival(DevelopmentConcepts.Havfarm, 0.20), Rival(DevelopmentConcepts.Havfarm, 0.99) };
        var middle = engine.PreviewDevelopmentApplication(DevelopmentConcepts.Of1, "off_1")!;
        Assert.Equal((L.AwardFloorScore, "middle", true), (middle.AwardBar, middle.Band, middle.WouldBeAwarded));

        round.Rivals = new List<RivalApplication> { Rival(DevelopmentConcepts.Of1, 0.85), Rival(DevelopmentConcepts.Havfarm, 0.84), Rival(DevelopmentConcepts.Havfarm, 0.86) };
        var weak = engine.PreviewDevelopmentApplication(DevelopmentConcepts.Of1, "off_1")!;
        Assert.Equal((0.0, 0.85, "weak", false), (weak.Novelty, weak.AwardBar, weak.Band, weak.WouldBeAwarded));
        var filed = engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1");
        Assert.True(filed.Ok, filed.Message);
        Assert.Equal(weak.Total, filed.Value);
        var permit = AdvanceToDecision(engine, filed.Id);
        Assert.Equal(PermitStatus.Refused, permit.Status);
        engine.Advance(2);
        Assert.Empty(engine.DevelopmentAwards);
        Assert.DoesNotContain(engine.State.LicenceHoldings, l => l.Kind == LicenceKind.Development);
    }

    /// <summary>
    /// The refusals before any change: an unknown concept, a plot that is no
    /// offshore slot or does not exist, a company that no longer meets the
    /// gate (an escape since the round opened).
    /// </summary>
    [Fact]
    public void Apply_RefusesBeforeAnyChange()
    {
        var engine = CareerEngine(245);
        OpenRound(engine);
        int cases = engine.State.PermitCases.Count;
        Assert.Equal(SimulationEngine.OffshoreReasons.InvalidConcept, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Havfarm, "off_1").ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreReasons.PlotNotFound, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "no_plot").ErrorKey);
        var fjordPlot = engine.SitePlots.First(p => p.Kind != PlotKind.OffshoreSlot && !p.IsOverflow);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotAnOffshoreSlot, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, fjordPlot.Id).ErrorKey);
        engine.FjordSites().First().RecordEscape(10, engine.State.Day);
        Assert.Equal(SimulationEngine.OffshoreReasons.GateUnmet, engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1").ErrorKey);
        Assert.Equal(cases, engine.State.PermitCases.Count);
    }

    // ── Acceptance 3: free, expiring, unpooled, rent-free ──────────────────

    /// <summary>
    /// Acceptance 3: building needs the award (licence_missing without it,
    /// or with one for another concept); built, the award's licences are bound
    /// to the unit, its MTB is theirs (never above the locality's), they never
    /// pool in any area; an unbuilt award lapses the day after its 365-day
    /// construction window with its licences; a licence past its 15 years
    /// holds no MTB and the unit takes no stocking (licence_expired).
    /// </summary>
    [Fact]
    public void Awards_BindToTheUnit_LapseUnbuilt_AndLicencesExpire()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(251, award: false);
        Assert.Equal(SimulationEngine.OffshoreReasons.LicenceMissing,
            engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).ErrorKey);
        engine.AwardDevelopmentLicencesForTests("off_1", DevelopmentConcepts.Havfarm);
        Assert.Equal(SimulationEngine.OffshoreReasons.LicenceMissing,
            engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).ErrorKey);
        var award = engine.AwardDevelopmentLicencesForTests("off_1");
        var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed);
        Assert.True(built.Ok, built.Message);
        var site = engine.OffshoreUnits.Single(u => u.Id == built.Id);
        Assert.Equal((DevelopmentAwardStatus.Built, site.Id), (award.Status, award.UnitId));
        Assert.True(site.Offshore!.HoldsLicences);
        var bound = engine.LicencesOfUnit(site);
        Assert.Equal(K.Of1.Licences, bound.Count);
        Assert.All(bound, l => Assert.Equal((site.Id, award.Id), (l.BoundUnitId, l.AwardId)));
        Assert.Equal(Math.Min(site.MABTonnes, K.Of1.Licences * K.LicenceSizeTonnesMtb), engine.UnitMtbTonnes(site));
        for (int area = 0; area <= 13; area++)
            Assert.DoesNotContain(engine.State.LicenceHoldings, l => l.Kind == LicenceKind.Development && l.IsPooledIn(area));
        Assert.False(engine.UnitCountsInItsAreaPool(site));
        Assert.DoesNotContain(engine.SeaSitesInArea(engine.ProductionAreaOfSite(site)), s => s.Id == site.Id);

        var lapsing = OffshoreTestUnits.UnlockedForOf1(252, award: false);
        var unbuilt = lapsing.AwardDevelopmentLicencesForTests("off_1");
        var its = lapsing.State.LicenceHoldings.Where(l => l.AwardId == unbuilt.Id).ToList();
        lapsing.Advance(unbuilt.ConstructionDeadlineDay - lapsing.State.Day);
        Assert.Equal(DevelopmentAwardStatus.AwaitingConstruction, unbuilt.Status);
        Assert.NotNull(lapsing.BuildableAward("off_1", DevelopmentConcepts.Of1)); // the deadline day still builds
        lapsing.Advance(2);
        Assert.Equal(DevelopmentAwardStatus.Lapsed, unbuilt.Status);
        Assert.All(its, l => Assert.False(l.IsInForce(lapsing.State.Day)));
        Assert.Equal(0, lapsing.DevelopmentAwardsWaiting(DevelopmentConcepts.Of1));
        Assert.Equal(SimulationEngine.OffshoreReasons.LicenceMissing,
            lapsing.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).ErrorKey);
        Assert.Equal(K.Of1.Licences, its.Count); // kept in the register, never deleted

        var (unitEngine, unit) = AwardedUnit(253);
        var licences = unitEngine.LicencesOfUnit(unit).ToList();
        int expires = licences[0].ExpiresDay;
        Assert.Equal(unitEngine.State.Day + L.DevelopmentLicenceYears * SimCalendar.DaysPerYear, expires);
        Assert.True(licences[0].IsInForce(expires - 1));
        Assert.False(licences[0].IsInForce(expires));
        foreach (var l in licences) l.ExpiresDay = unitEngine.State.Day + 1; // fifteen years on
        unitEngine.Advance(1);
        Assert.Equal(0.0, unitEngine.UnitMtbTonnes(unit));
        Assert.Equal(SimulationEngine.OffshoreReasons.LicenceExpired, unitEngine.StockOffshoreFarm(unit.Id, 10_000).ErrorKey);
        Assert.Contains(unitEngine.ConversionChecklist(unit), c => c.Key == "licences_in_force" && !c.Met);
    }

    /// <summary>
    /// Acceptance 3 and 5 (the ledger): a harvest from a unit on development
    /// licences adds nothing to the resource-rent base; the cycle it ends
    /// counts toward conversion; converted, paid and placed in a fjord zone
    /// of the unit's area, the licences pool there (the area's licence MTB
    /// grows by the eight licences), the unit's fish count in the area pool
    /// and in the zone's traffic-light fish, and its next harvest adds to the
    /// rent base.
    /// </summary>
    [Fact]
    public void DevelopmentHarvestsPayNoRent_ConvertedAndPlacedOnesDo()
    {
        var (engine, site) = AwardedUnit(261, career: true);
        var unit = site.Offshore!;
        Assert.True(engine.StockOffshoreFarm(site.Id, 50_000).Ok);
        engine.Advance(40);
        decimal rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        var first = engine.HarvestOffshoreFarm(site.Id);
        Assert.True(first.Ok, first.Message);
        Assert.Equal(rentBase, engine.State.ResourceRentHarvestValueThisTaxYear);
        Assert.Equal(site.Id, engine.HarvestReports[^1].HoldingId);
        engine.Advance(1);
        Assert.Equal(1, unit.CompletedCycles);

        int guard = 0;
        while (!engine.ConversionChecklist(site).All(c => c.Met))
        {
            engine.Advance(10);
            Assert.True(++guard < 60, "the checklist was never met: " + Unmet(engine, site));
        }
        var requested = engine.RequestConversion(site.Id);
        Assert.True(requested.Ok, requested.Message);
        Assert.Equal(1.0, requested.Value);
        var permit = AdvanceToDecision(engine, requested.Id);
        Assert.Equal(PermitStatus.Approved, permit.Status);
        engine.Advance(1);
        Assert.Equal(engine.ConversionFeeNok(site, permit.DecidedDay), unit.ConversionFeeDueNok);
        decimal cash = engine.FinanceManager.Balance;
        decimal fee = unit.ConversionFeeDueNok;
        Assert.Equal(K.Of1.Licences * engine.ConversionFeePerLicenceNok(permit.DecidedDay), fee);
        var paid = engine.PayConversion(site.Id);
        Assert.True(paid.Ok, paid.Message);
        Assert.Equal(fee, cash - engine.FinanceManager.Balance);
        Assert.Equal((-1, 0m), (unit.ConversionPaymentDueDay, unit.ConversionFeeDueNok));
        var licences = engine.LicencesOfUnit(site).ToList();
        Assert.All(licences, l => Assert.Equal((LicenceKind.Commercial, true, int.MaxValue), (l.Kind, l.AwaitingPlacement, l.ExpiresDay)));
        Assert.Equal(SimulationEngine.OffshoreReasons.LicencesUnplaced, engine.StockOffshoreFarm(site.Id, 10_000).ErrorKey);
        Assert.False(engine.OffshoreUnitRentLiable(site.Id));

        var zone = ZoneInAreaOf(engine, site);
        int area = engine.ProductionAreaOf(zone);
        double pooled = engine.AreaLicenceMtbTonnes(area);
        var placed = engine.PlaceLicences(licences.Select(l => l.Id).ToList(), zone.Id);
        Assert.True(placed.Ok, placed.Message);
        Assert.Equal(pooled + K.Of1.Licences * K.LicenceSizeTonnesMtb, engine.AreaLicenceMtbTonnes(area), 6);
        Assert.All(licences, l => Assert.Equal((false, zone.Id, area), (l.AwaitingPlacement, l.PlacedZoneId, l.PooledArea)));
        Assert.True(engine.UnitCountsInItsAreaPool(site));
        Assert.True(engine.OffshoreUnitRentLiable(site.Id));

        var (fishBefore, _) = engine.ZoneLiceForTrafficLight(zone.Id);
        Assert.True(engine.StockOffshoreFarm(site.Id, 50_000).Ok);
        engine.Advance(40);
        int unitFish = site.Pens.Sum(p => p.TotalCount);
        Assert.True(unitFish > 0);
        Assert.Contains(engine.SeaSitesInArea(area), s => s.Id == site.Id);
        int zoneFjordFish = engine.State.Populations.Where(p => p.ZoneId == zone.Id).Sum(p => p.TotalCount);
        Assert.Equal(zoneFjordFish + unitFish, engine.ZoneLiceForTrafficLight(zone.Id).Fish);
        _out.WriteLine($"zone {zone.Id}: {fishBefore} fish before, {zoneFjordFish} fjord + {unitFish} unit after");

        rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        var second = engine.HarvestOffshoreFarm(site.Id);
        Assert.True(second.Ok, second.Message);
        Assert.True(engine.State.ResourceRentHarvestValueThisTaxYear > rentBase, "a converted, placed unit's harvest added nothing to the rent base");
    }

    // ── Acceptance 4: conversion ───────────────────────────────────────────

    /// <summary>
    /// Acceptance 4: the fee is NOK 13,804,348 a licence at January 2026,
    /// indexed by the game's CPI (2 % a year) by the month (the game starts in
    /// January 2027); growth on a development licence above its 780 t is
    /// charged at the fixed price.
    /// </summary>
    [Fact]
    public void TheConversionFee_IsIndexedByCpi_AndGrowthPaysTheFixedPrice()
    {
        var (engine, site) = AwardedUnit(271);
        Assert.Equal(13_804_348m, K.ConversionFeeNokPerLicence);
        Assert.Equal(Math.Round(13_804_348m * (decimal)Math.Pow(1.02, 1.0), 0, MidpointRounding.AwayFromZero), engine.ConversionFeePerLicenceNok(0));
        Assert.Equal(14_080_435m, engine.ConversionFeePerLicenceNok(0));
        Assert.Equal(Math.Round(13_804_348m * (decimal)Math.Pow(1.02, 2.0), 0, MidpointRounding.AwayFromZero), engine.ConversionFeePerLicenceNok(SimCalendar.DaysPerYear));
        int july = SimCalendar.DaysPerYear * 3 + Enumerable.Range(0, SimCalendar.DaysPerYear).First(d => SimCalendar.MonthIndex(d) == 6);
        Assert.Equal(Math.Round(13_804_348m * (decimal)Math.Pow(1.02, (12.0 + 36.0 + 6.0) / 12.0), 0, MidpointRounding.AwayFromZero), engine.ConversionFeePerLicenceNok(july));
        for (int d = 0; d < 5 * SimCalendar.DaysPerYear; d += 30)
            Assert.True(engine.ConversionFeePerLicenceNok(d + 30) >= engine.ConversionFeePerLicenceNok(d));

        int day = engine.State.Day;
        Assert.Equal(K.Of1.Licences * engine.ConversionFeePerLicenceNok(day), engine.ConversionFeeNok(site, day));
        engine.LicencesOfUnit(site)[0].MtbTonnes = K.LicenceSizeTonnesMtb + 120.0; // 120 t of growth on one licence
        Assert.Equal(K.Of1.Licences * engine.ConversionFeePerLicenceNok(day) + 120m * L.FixedGrowthPriceNokPerTonne, engine.ConversionFeeNok(site, day));
        Assert.Equal(170_000m, L.FixedGrowthPriceNokPerTonne);
    }

    /// <summary>
    /// Acceptance 4: every criterion on the checklist with its value and
    /// target; the mortality criterion compares the unit's lifetime mortality
    /// with the area's live figure (the company's fjord sites there, the
    /// national figure without one), so a unit that lost more fish than the
    /// area fails it alone and its conversion is refused on its day.
    /// </summary>
    [Fact]
    public void TheChecklist_NeedsEveryCriterion_MortalityAgainstTheAreasLiveFigure()
    {
        var (engine, site) = AwardedUnit(275, career: true);
        var unit = site.Offshore!;
        if (!engine.FjordSites().Any(s => s.Pens.Any(p => p.TotalCount > 0)))
            Assert.True(engine.StockSmolts("po3_nord", 12_000, "standard").Ok);
        engine.Advance(1);
        var fresh = engine.ConversionChecklist(site).ToDictionary(c => c.Key);
        Assert.Equal(new[] { "completed_cycles", "mortality", "escape_free_days", "no_order_or_withdrawal", "licences_in_force" }, engine.ConversionChecklist(site).Select(c => c.Key));
        Assert.Equal((0.0, 1.0, false), (fresh["completed_cycles"].Value, fresh["completed_cycles"].Target, fresh["completed_cycles"].Met));
        Assert.Equal((L.ConversionEscapeFreeDays, false), ((int)fresh["escape_free_days"].Target, fresh["escape_free_days"].Met));
        Assert.True(fresh["no_order_or_withdrawal"].Met);
        Assert.Equal((8.0, 8.0, true), (fresh["licences_in_force"].Value, fresh["licences_in_force"].Target, fresh["licences_in_force"].Met));

        int area = engine.ProductionAreaOfSite(site);
        (double Value, double Passed) AreaLive()
        {
            double deaths = 0.0, passed = 0.0;
            foreach (var fjord in engine.FjordSites().Where(s => engine.ProductionAreaOfSite(s) == area))
            {
                var ledger = engine.MortalityLedger.ForSite(fjord.Id);
                deaths += ledger.Total.Fish;
                passed += ledger.Total.Fish + MortalityCauses.AllRemovals.Sum(r => (double)ledger.Removed(r).Fish) + fjord.Pens.Sum(p => (double)p.TotalCount);
            }
            return (passed > 0.0 ? deaths / passed : L.FallbackAreaMortality, passed);
        }
        var live = AreaLive();
        Assert.True(live.Passed > 0.0, $"no fjord site of the company in area {area} has had fish: the live figure would be the fallback");
        Assert.Equal(live.Value, fresh["mortality"].Target, 12);

        var harvested = GrowAndHarvest(engine, site);
        Assert.True(harvested.Ok, harvested.Message);
        Assert.Equal(1, unit.CompletedCycles);
        int guard = 0;
        while (!engine.ConversionChecklist(site).Single(c => c.Key == "escape_free_days").Met)
        {
            engine.Advance(10);
            Assert.True(++guard < 60, "the escape-free year never came: " + Unmet(engine, site));
        }
        Assert.True(engine.ConversionChecklist(site).All(c => c.Met), Unmet(engine, site));

        Assert.True(engine.StockOffshoreFarm(site.Id, 100_000).Ok);
        var pop = site.Pens.Single();
        live = AreaLive();
        double target = engine.ConversionChecklist(site).Single(c => c.Key == "mortality").Target;
        Assert.Equal(live.Value, target, 12); // live: the area's figure as its fish are today
        int kill = (int)Math.Ceiling(100_000 * Math.Min(0.9, target * 3.0 + 0.05));
        pop.RemoveDead(pop.Cohorts[0], kill, MortalityCause.Disease);
        var list = engine.ConversionChecklist(site);
        var mortality = list.Single(c => c.Key == "mortality");
        Assert.False(mortality.Met, $"unit {mortality.Value:P2} vs area {mortality.Target:P2}");
        Assert.Equal(engine.SiteLifetimeMortalityShare(site), mortality.Value, 12);
        Assert.All(list.Where(c => c.Key != "mortality"), c => Assert.True(c.Met, c.Key));
        _out.WriteLine($"area {area}: live mortality {target:P2}; the unit's {mortality.Value:P2} after {kill} deaths");
        var requested = engine.RequestConversion(site.Id);
        Assert.True(requested.Ok, requested.Message);
        Assert.Equal(0.0, requested.Value);
        Assert.InRange(requested.Value2 - engine.State.Day, L.ConversionDecisionDaysMin, L.ConversionDecisionDaysMax);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyApplied, engine.RequestConversion(site.Id).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotAppealable, engine.AppealConversion(site.Id).ErrorKey);
        var permit = AdvanceToDecision(engine, requested.Id);
        Assert.Equal(PermitStatus.Refused, permit.Status);
        Assert.All(engine.LicencesOfUnit(site), l => Assert.Equal(LicenceKind.Development, l.Kind));
        Assert.Equal(-1, unit.ConversionPaymentDueDay);
    }

    /// <summary>
    /// Acceptance 4: a refusal may be appealed and the appeal is decided 180
    /// days on by the criteria met by then (here the cycle completed during
    /// the appeal); the approval asks the fee indexed on its decision day,
    /// payable within 90 days; unpaid, the conversion lapses (the licences
    /// stay development licences, the payment refused payment_lapsed) and a
    /// new conversion may be filed.
    /// </summary>
    [Fact]
    public void ARefusal_IsAppealedOnTheCriteriaMetByThen_AndAnUnpaidApprovalLapses()
    {
        var (engine, site) = AwardedUnit(281);
        var unit = site.Offshore!;
        engine.Advance(L.ConversionEscapeFreeDays + 1);
        Assert.True(engine.ConversionChecklist(site).Single(c => c.Key == "escape_free_days").Met);
        var requested = engine.RequestConversion(site.Id);
        Assert.True(requested.Ok, requested.Message);
        Assert.Equal(0.0, requested.Value); // no completed cycle yet
        var permit = AdvanceToDecision(engine, requested.Id);
        Assert.Equal(PermitStatus.Refused, permit.Status);

        var appeal = engine.AppealConversion(site.Id);
        Assert.True(appeal.Ok, appeal.Message);
        Assert.Equal(PermitStatus.Appealed, permit.Status);
        Assert.Equal(engine.State.Day + L.ConversionAppealDays, permit.DecisionDay);
        var harvested = GrowAndHarvest(engine, site); // the cycle completes while the appeal runs
        Assert.True(harvested.Ok, harvested.Message);
        AdvanceToDecision(engine, requested.Id);
        Assert.Equal(PermitStatus.Approved, permit.Status);
        engine.Advance(1);
        Assert.Equal(permit.DecidedDay + L.ConversionPaymentDays, unit.ConversionPaymentDueDay);
        Assert.Equal(K.Of1.Licences * engine.ConversionFeePerLicenceNok(permit.DecidedDay), unit.ConversionFeeDueNok);

        engine.Advance(unit.ConversionPaymentDueDay - engine.State.Day + 1);
        Assert.Equal(PermitStatus.Approved, permit.Status); // payable through its 90th day
        engine.Advance(1);
        Assert.Equal(PermitStatus.Lapsed, permit.Status);
        Assert.Equal(-1, unit.ConversionPaymentDueDay);
        Assert.Equal(SimulationEngine.OffshoreReasons.PaymentLapsed, engine.PayConversion(site.Id).ErrorKey);
        Assert.All(engine.LicencesOfUnit(site), l => Assert.Equal((LicenceKind.Development, site.Id), (l.Kind, l.BoundUnitId)));
        var again = engine.RequestConversion(site.Id);
        Assert.True(again.Ok, again.Message);
        Assert.NotEqual(requested.Id, again.Id);
    }

    // ── Acceptance 5: placement ────────────────────────────────────────────

    /// <summary>
    /// Acceptance 5: placement refuses before any change: an id that is no
    /// converted licence awaiting placement, an unknown zone, a red zone.
    /// </summary>
    [Fact]
    public void PlaceLicences_RefusesBeforeAnyChange()
    {
        var (engine, site) = AwardedUnit(291, career: true);
        var licences = engine.LicencesOfUnit(site).ToList();
        var zone = ZoneInAreaOf(engine, site);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.PlaceLicences(licences.Select(l => l.Id).ToList(), zone.Id).ErrorKey); // not converted
        foreach (var l in licences) { l.Kind = LicenceKind.Commercial; l.AwaitingPlacement = true; } // as a paid conversion leaves them
        var ids = licences.Select(l => l.Id).ToList();
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.PlaceLicences(Array.Empty<string>(), zone.Id).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.PlaceLicences(ids.Append("tl_x").ToList(), zone.Id).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreReasons.ZoneNotFound, engine.PlaceLicences(ids, "no_zone").ErrorKey);
        engine.TrafficLightSystem.SetZoneColor(zone.Id, ZoneColor.Red);
        Assert.Equal(SimulationEngine.OffshoreReasons.ZoneFull, engine.PlaceLicences(ids, zone.Id).ErrorKey);
        Assert.All(licences, l => Assert.True(l.AwaitingPlacement && l.PlacedZoneId.Length == 0));
        engine.TrafficLightSystem.SetZoneColor(zone.Id, ZoneColor.Yellow);
        Assert.True(engine.PlaceLicences(ids, zone.Id).Ok);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.PlaceLicences(ids, zone.Id).ErrorKey); // placed once
    }

    // ── Acceptance 6: save and twin mid-round ──────────────────────────────

    /// <summary>
    /// Acceptance 6: a game saved mid-round (the field drawn, the company's
    /// application pending, rival awards on) and resumed runs on identical to
    /// the game that was not saved and to its twin through the company's
    /// award and the rivals' decisions.
    /// </summary>
    [Fact]
    public void SaveAndTwin_MidRound_AreIdentical()
    {
        SimulationEngine Mid()
        {
            var engine = GatedEngine(299, rivalOffshore: true, events: true);
            OpenRound(engine);
            engine.Advance(20);
            Assert.True(engine.ApplyDevelopmentLicence(DevelopmentConcepts.Of1, "off_1").Ok);
            engine.Advance(5);
            return engine;
        }
        var engine = Mid();
        var twin = Mid();
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: true);
        Assert.Equal(SaveStateComparer.Normalize(saved), SaveStateComparer.Normalize(SaveStateComparer.Serialize(twin)));
        Assert.True(resumed.RivalOffshoreActive);
        Assert.Single(resumed.DevelopmentRounds);
        for (int day = 0; day < 200; day++)
        {
            engine.Advance(1);
            resumed.Advance(1);
            twin.Advance(1);
        }
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        string c = SaveStateComparer.Normalize(SaveStateComparer.Serialize(twin));
        Assert.True(a == b, "the resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
        Assert.True(a == c, "the twin diverged at " + SaveStateComparer.FirstDifference(a, c));
        Assert.Contains(engine.DevelopmentAwards, x => x.IsCompany);
        Assert.Equal(engine.DevelopmentAwards.Count, resumed.DevelopmentAwards.Count);
    }
}
