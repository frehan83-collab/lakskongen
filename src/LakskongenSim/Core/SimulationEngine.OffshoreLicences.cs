using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Development licences, their conversion and placement (roadmap F2-09,
/// docs/FARM_TYPES_SPEC.md §2C "Licence / permit path", §3.4 ".Offshore",
/// §6.1 acts 3-4; V:2 Fiskeridirektoratet's AOF conversion decision of
/// 13.03.2026, V:3 development licences are outside the resource-rent tax).
/// <list type="bullet">
/// <item><b>The round.</b> It opens once the company has researched offshore
/// farming, sold its first harvest and gone twelve months without an escape,
/// and stays open two in-game years. Its rival field is drawn when it opens
/// from a private stream seeded by the game's seed (no game stream moves).
/// The company files a dossier (concept, plot) whose score breakdown it sees
/// first; the award comes 180 days later when the score reaches the round's
/// bar (the field's median, not below the floor). With <c>rivalOffshore</c>
/// on, the strongest rival is awarded on its decision day and takes off_2.</item>
/// <item><b>The licences.</b> An award brings eight development licences of
/// 780 t: free, bound to the unit built on them, never pooled, no resource
/// rent, expiring after 15 years unconverted; construction must start within
/// 365 days or the award lapses.</item>
/// <item><b>Conversion.</b> Once every criterion is met (a completed cycle,
/// mortality at or below the production area's live average, a year without
/// an escape, no order or withdrawal standing, licences in force) the
/// company files a conversion; it is decided on the criteria met on its day,
/// a refusal may be appealed (180 days, decided the same way); an approval
/// asks the fee (NOK 13,804,348 a licence at January 2026, indexed by the
/// game's CPI) plus any growth at the fixed price, payable within 90 days or
/// the conversion lapses. Paid, the licences are ordinary licences that must
/// be placed in a production area before use; placed, they pool there, count
/// in its traffic light and pay the resource rent.</item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The scenarios where no rival wins development licences (spec §4: the demo scenarios, D9).</summary>
    public static readonly IReadOnlyList<string> RivalOffshoreOffScenarios = new[] { "forste_sesong", "po3_red_sone" };

    /// <summary>The research that opens the Hav family and the round (F2-04's gate).</summary>
    internal const string DeepSeaResearchId = "deep-sea-farming";

    private OffshoreLicencesCalibration LicenceCal => OffshoreCal.Licences;

    private FarmTypeCatalogCalibration CatalogCal => _calibration.Facilities.Common.Catalog;

    /// <summary>True when rivals may win development licences in this game (spec §4 <c>rivalOffshore</c>).</summary>
    public bool RivalOffshoreActive
    {
        get
        {
            if (State?.RivalOffshore is bool forced) return forced;
            var scenario = Scenario?.ActiveScenario;
            if (scenario == null) return false;
            if (scenario.SpecialRules != null && scenario.SpecialRules.TryGetValue("rival_offshore", out var rule)
                && bool.TryParse(rule, out bool parsed))
                return parsed;
            return !RivalOffshoreOffScenarios.Contains(scenario.Id);
        }
    }

    /// <summary>The development-licence rounds, in the order opened.</summary>
    public IReadOnlyList<DevelopmentLicenceRound> DevelopmentRounds
        => State?.DevelopmentRounds ?? (IReadOnlyList<DevelopmentLicenceRound>)Array.Empty<DevelopmentLicenceRound>();

    /// <summary>The awards (the company's and the rivals'), in the order awarded.</summary>
    public IReadOnlyList<DevelopmentAward> DevelopmentAwards
        => State?.DevelopmentAwards ?? (IReadOnlyList<DevelopmentAward>)Array.Empty<DevelopmentAward>();

    /// <summary>The round open today, or null.</summary>
    public DevelopmentLicenceRound? OpenDevelopmentRound()
        => State?.DevelopmentRounds?.FirstOrDefault(r => r.IsOpenOn(State.Day));

    /// <summary>
    /// The conversion fee of one licence on <paramref name="day"/> (NOK): the
    /// catalogue's January 2026 fee (V:2) indexed by the game's CPI (2 % a
    /// year, Norges Bank's target) for the months since January 2026; the game
    /// starts on 1 January 2027.
    /// </summary>
    public decimal ConversionFeePerLicenceNok(int day)
    {
        double months = 12.0 + (double)SimCalendar.YearIndex(day) * 12.0 + SimCalendar.MonthIndex(day);
        double index = Math.Pow(1.0 + LicenceCal.CpiAnnualRate, months / 12.0);
        return Math.Round(CatalogCal.ConversionFeeNokPerLicence * (decimal)index, 0, MidpointRounding.AwayFromZero);
    }

    // ─── The day (OffshoreTickSystem, every game) ──────────────────────────

    /// <summary>
    /// The licences' day: the round opens or closes, decided applications
    /// become awards, rival applications are decided, unbuilt awards lapse,
    /// conversion cases follow their criteria, approved conversions ask their
    /// payment and unpaid ones lapse. A fjord-only game without the research
    /// does nothing here; a round that opens draws from no game stream.
    /// </summary>
    internal void RunDevelopmentLicenceDay()
    {
        if (State == null) return;
        int today = State.Day;
        MaybeOpenDevelopmentRound(today);
        if (State.DevelopmentRounds.Count == 0) return;
        foreach (var round in State.DevelopmentRounds)
        {
            if (round.Status == DevelopmentRoundStatus.Open && today > round.ClosesDay)
                round.Status = DevelopmentRoundStatus.Closed;
            DecideRivalApplications(round, today);
        }
        TurnApprovedApplicationsIntoAwards(today);
        LapseUnbuiltAwards(today);
        FollowConversionCases(today);
    }

    private void MaybeOpenDevelopmentRound(int today)
    {
        if (State.DevelopmentRounds.Count > 0 || !RoundGateMet()) return;
        var round = new DevelopmentLicenceRound
        {
            Id = $"dlr_{State.DevelopmentRounds.Count + 1}",
            OpenedDay = today,
            ClosesDay = today + LicenceCal.RoundOpenDays,
            RivalAwards = RivalOffshoreActive,
        };
        // A private stream: the field is the seed's, whatever else happens in
        // the game, and no game stream moves (the shipped scenarios' traces).
        var stream = new SimRngStream(SimRng.Mix((ulong)(uint)Rng.Seed ^ StableHash.Of("development_round", round.Id)));
        var roster = CompetitorAI?.Competitors?.Select(c => (c.Id, c.Name)).ToList() ?? new List<(string, string)>();
        round.Rivals = DevelopmentLicenceRound.DrawField(stream.NextDouble, today, LicenceCal, roster, atLeastOne: round.RivalAwards);
        State.DevelopmentRounds.Add(round);
        PublishGameEvent(new GameEventRequest(GameEventType.DevLicenceRoundOpened, today)
        {
            Variant = round.Id,
            Message = SimText.Nb($"Søknadsrunden for utviklingstillatelser er åpen til dag {round.ClosesDay}: {round.Rivals.Count} konkurrenter ventes å søke"),
        });
    }

    /// <summary>
    /// The company's gate for the round (spec §2C, §6.1): offshore-farming
    /// research done, a first harvest sold, and the escape-free months.
    /// </summary>
    internal bool RoundGateMet()
    {
        if (Research?.Nodes == null || !Research.Nodes.TryGetValue(DeepSeaResearchId, out var node) || !node.Unlocked) return false;
        if ((State?.HarvestBatchesSold ?? 0) < 1) return false;
        return EscapeFreeDays() >= CatalogCal.Of1.RoundEscapeFreeDays;
    }

    private void DecideRivalApplications(DevelopmentLicenceRound round, int today)
    {
        foreach (var rival in round.Rivals)
        {
            if (rival.Decided || today < rival.DecisionDay) continue;
            rival.Decided = true;
            if (!round.RivalAwards || !ReferenceEquals(rival, StrongestRival(round))) continue;
            // The round's one rival award takes off_2 (spec §4), else the next free slot it fits.
            var plot = SitePlots.FirstOrDefault(p => p.Id == "off_2" && p.IsFree)
                       ?? FirstFreePlot(p => p.Kind == PlotKind.OffshoreSlot);
            if (plot == null) continue;
            rival.Awarded = true;
            rival.PlotId = plot.Id;
            ClaimPlot(plot, "rival:" + rival.RivalId);
            var award = NewAward(round, rival.RivalId, rival.Concept, plot.Id, today, rival.Score, "");
            State.EventLog.Add(new EventRecord
            {
                Day = today,
                Name = "Utviklingstillatelser til konkurrent",
                Category = EventCategory.Competitor,
                Description = SimText.Nb($"{rival.RivalName} fikk {award.Licences} utviklingstillatelser og tar {plot.Name}"),
            });
            State.TrimEventLog();
            PublishGameEvent(new GameEventRequest(GameEventType.DevLicenceAwarded, today)
            {
                Variant = award.Id,
                Message = SimText.Nb($"{rival.RivalName} fikk utviklingstillatelser og tar {plot.Name}"),
            });
        }
    }

    /// <summary>The round's strongest rival dossier (the one rival award with <c>rivalOffshore</c> on), or null.</summary>
    internal static RivalApplication? StrongestRival(DevelopmentLicenceRound round)
        => round.Rivals.OrderByDescending(r => r.Score).ThenBy(r => r.SubmittedDay).ThenBy(r => r.RivalId, StringComparer.Ordinal).FirstOrDefault();

    private DevelopmentAward NewAward(DevelopmentLicenceRound? round, string rivalId, string concept, string plotId, int day, double score, string caseId)
    {
        int licences = CatalogCal.Of1.Licences;
        var award = new DevelopmentAward
        {
            Id = $"da_{State.DevelopmentAwards.Count + 1}",
            RoundId = round?.Id ?? "",
            RivalId = rivalId,
            Concept = concept,
            PlotId = plotId,
            AwardedDay = day,
            ConstructionDeadlineDay = day + LicenceCal.ConstructionStartDays,
            Licences = licences,
            LicenceTonnes = licences * CatalogCal.LicenceSizeTonnesMtb,
            CaseId = caseId,
            Score = score,
        };
        State.DevelopmentAwards.Add(award);
        return award;
    }

    /// <summary>The company's applications approved by the permit system become awards with their licences in the register.</summary>
    private void TurnApprovedApplicationsIntoAwards(int today)
    {
        foreach (var round in State.DevelopmentRounds)
        {
            foreach (string caseId in round.CompanyCaseIds)
            {
                var permit = State.PermitCases.FirstOrDefault(c => c.Id == caseId);
                if (permit == null || permit.Status != PermitStatus.Approved) continue;
                if (State.DevelopmentAwards.Any(a => a.CaseId == caseId)) continue;
                var plot = FindPlot(permit.PlotId) ?? SitePlots.FirstOrDefault(p => p.Id == permit.PlotId);
                if (plot == null || !plot.IsFree) continue; // the slot went to a rival first: no award to build on
                double score = permit.Conditions.TryGetValue("score", out var s) ? s : 0.0;
                var award = NewAward(round, "", DevelopmentConcepts.Of1, permit.PlotId, permit.DecidedDay, score, caseId);
                GrantAwardLicences(award);
                State.EventLog.Add(new EventRecord
                {
                    Day = today,
                    Name = "Utviklingstillatelser tildelt",
                    Category = EventCategory.Regulation,
                    Description = SimText.Nb($"Selskapet fikk {award.Licences} utviklingstillatelser ({award.LicenceTonnes:N0} t MTB) for {plot.Name}: byggingen må starte innen dag {award.ConstructionDeadlineDay}"),
                });
                State.TrimEventLog();
                PublishGameEvent(new GameEventRequest(GameEventType.DevLicenceAwarded, today)
                {
                    Variant = award.Id,
                    Message = SimText.Nb($"{award.Licences} utviklingstillatelser tildelt for {plot.Name}: start byggingen innen dag {award.ConstructionDeadlineDay}"),
                });
            }
        }
    }

    /// <summary>The award's development licences into the register: unbound, never pooled, expiring after 15 years.</summary>
    private void GrantAwardLicences(DevelopmentAward award)
    {
        int expires = award.AwardedDay + LicenceCal.DevelopmentLicenceYears * SimCalendar.DaysPerYear;
        for (int i = 0; i < award.Licences; i++)
        {
            var licence = GrantLicence(State, State.HomeProductionArea, CatalogCal.LicenceSizeTonnesMtb, "", LicenceOrigin.DevelopmentRound);
            licence.GrantedDay = award.AwardedDay;
            licence.Kind = LicenceKind.Development;
            licence.ExpiresDay = expires;
            licence.AwardId = award.Id;
        }
    }

    /// <summary>
    /// Test seam (F2-07's build tests, F2-09's licence tests): an award for
    /// <paramref name="plotId"/> granted today with its licences, as an
    /// approved application gives one, without a round. Never called by the game.
    /// </summary>
    internal DevelopmentAward AwardDevelopmentLicencesForTests(string plotId, string concept = DevelopmentConcepts.Of1)
    {
        var award = NewAward(null, "", concept, plotId, State.Day, 1.0, "");
        GrantAwardLicences(award);
        return award;
    }

    /// <summary>An award with no construction started by its deadline lapses, and its licences expire with it (never deleted).</summary>
    private void LapseUnbuiltAwards(int today)
    {
        foreach (var award in State.DevelopmentAwards)
        {
            if (!award.IsCompany || award.Status != DevelopmentAwardStatus.AwaitingConstruction || today <= award.ConstructionDeadlineDay) continue;
            award.Status = DevelopmentAwardStatus.Lapsed;
            foreach (var licence in State.LicenceHoldings.Where(l => l.AwardId == award.Id && l.BoundUnitId.Length == 0))
                licence.ExpiresDay = Math.Min(licence.ExpiresDay, today);
            State.EventLog.Add(new EventRecord
            {
                Day = today,
                Name = "Utviklingstillatelser bortfalt",
                Category = EventCategory.Regulation,
                Description = SimText.Nb($"Byggingen startet ikke innen dag {award.ConstructionDeadlineDay}: utviklingstillatelsene for {award.PlotId} er bortfalt"),
            });
            State.TrimEventLog();
        }
    }

    /// <summary>The company's award a unit of <paramref name="concept"/> may be built on at <paramref name="plotId"/> today, or null.</summary>
    public DevelopmentAward? BuildableAward(string plotId, string concept)
        => State?.DevelopmentAwards?.FirstOrDefault(a => a.IsCompany && a.Status == DevelopmentAwardStatus.AwaitingConstruction
                                                       && a.PlotId == plotId && a.Concept == concept && State.Day <= a.ConstructionDeadlineDay);

    /// <summary>The value of the catalogue's development-licence gate: the company's awards of the variant waiting for their unit.</summary>
    internal int DevelopmentAwardsWaiting(string variantId)
        => State?.DevelopmentAwards?.Count(a => a.IsCompany && a.Status == DevelopmentAwardStatus.AwaitingConstruction
                                               && a.Concept == variantId && State.Day <= a.ConstructionDeadlineDay) ?? 0;

    /// <summary>Bind an award's licences to the unit built on it (the build verb).</summary>
    internal void BindAwardToUnit(DevelopmentAward award, SeaSite site)
    {
        award.Status = DevelopmentAwardStatus.Built;
        award.UnitId = site.Id;
        foreach (var licence in State.LicenceHoldings.Where(l => l.AwardId == award.Id))
            licence.BoundUnitId = site.Id;
        var unit = site.Offshore!;
        unit.HoldsLicences = true;
        unit.AwardId = award.Id;
    }

    // ─── The application ──────────────────────────────────────────────────

    /// <summary>
    /// The score a dossier for <paramref name="concept"/> on
    /// <paramref name="plotId"/> filed today would get, against the open
    /// round's rival field (spec §6.1). Null when no round is open. Pure.
    /// </summary>
    public DevelopmentScoreBreakdown? PreviewDevelopmentApplication(string concept, string plotId)
    {
        var round = OpenDevelopmentRound();
        return round == null ? null : ScoreApplication(round, concept ?? "", State.Day);
    }

    private DevelopmentScoreBreakdown ScoreApplication(DevelopmentLicenceRound round, string concept, int day)
    {
        var cal = LicenceCal;
        bool first = !round.Rivals.Any(r => r.Concept == concept && r.SubmittedDay < day)
                     && !round.CompanyCaseIds.Any(id => State.PermitCases.Any(c => c.Id == id && c.SubmittedDay < day));
        double novelty = first ? cal.NoveltyBonus : 0.0;
        double months = EscapeFreeDays() / (SimCalendar.DaysPerYear / 12.0);
        double escapeFree = cal.EscapeFreeWeight * Math.Min(1.0, months / cal.EscapeFreeFullMonths);
        double compliance = LiceComplianceShare();
        double lice = cal.LiceComplianceWeight * compliance;
        decimal capex = CatalogCal.Of1.UnitCapexNok * (decimal)CatalogCal.Of1.ConsortiumEquityShare;
        decimal need = capex * (decimal)cal.FinancingShareOfEquityCapex;
        double coverage = need <= 0m ? 1.0 : (double)((FinanceManager?.Balance ?? 0m) / need);
        double financing = cal.FinancingWeight * Math.Clamp(coverage, 0.0, 1.0);
        double total = novelty + escapeFree + lice + financing;
        var field = round.FieldScores();
        double bar = round.AwardBar(cal);
        double max = field.Count == 0 ? 0.0 : field[^1];
        string band = total >= bar && total >= max ? "strong" : total >= bar ? "middle" : "weak";
        return new DevelopmentScoreBreakdown(novelty, escapeFree, lice, financing, total,
            field.Count == 0 ? 0.0 : field[0], round.FieldMedian(), max, bar, band, total >= bar, months, compliance, coverage);
    }

    /// <summary>The share of the company's stocked fjord sites whose last lice count was under their limit (1 with none stocked).</summary>
    private double LiceComplianceShare()
    {
        int stocked = 0, under = 0;
        foreach (var site in FjordSites())
        {
            if (!site.Pens.Any(p => p.TotalCount > 0)) continue;
            stocked++;
            if (site.LastLiceCountDay < 0 || site.LastLiceCount <= LiceLimitAtSite(site)) under++;
        }
        return stocked == 0 ? 1.0 : under / (double)stocked;
    }

    /// <summary>
    /// File a development-licence application (spec §3.4 <c>ApplyDevelopmentLicence(concept, plotId)</c>):
    /// a permit case decided after 180 days on the score it is filed with
    /// against the round's bar. Fails before any change: engine_not_ready,
    /// round_closed, invalid_concept (only an OF1-class dossier in stage 2),
    /// plot_not_found, not_an_offshore_slot, plot_taken, exposure_exceeds_design,
    /// gate_unmet, already_applied (a pending application).
    /// </summary>
    public CommandResult ApplyDevelopmentLicence(string concept, string plotId)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var round = OpenDevelopmentRound();
        if (round == null)
            return CommandResult.Fail(OffshoreReasons.RoundClosed, "No development-licence round is open");
        if (concept != DevelopmentConcepts.Of1)
            return CommandResult.Fail(OffshoreReasons.InvalidConcept, $"Concept '{concept}' cannot be applied for (of1)");
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId && !p.IsOverflow);
        if (plot == null) return CommandResult.Fail(OffshoreReasons.PlotNotFound, $"No plot '{plotId}'");
        if (plot.Kind != PlotKind.OffshoreSlot) return CommandResult.Fail(OffshoreReasons.NotAnOffshoreSlot, $"'{plotId}' is no offshore slot");
        if (!plot.IsFree) return CommandResult.Fail(OffshoreReasons.PlotTaken, $"'{plotId}' is taken");
        if (!plot.AcceptsDesignHs(OffshoreUnit.DefaultDesignHsM(OffshoreType.SemiSubmersible)))
            return CommandResult.Fail(OffshoreReasons.ExposureExceedsDesign, $"'{plotId}''s sea is above an OF1-class unit's design");
        if (!RoundGateMet())
            return CommandResult.Fail(OffshoreReasons.GateUnmet, "The company does not meet the round's gate today");
        if (round.CompanyCaseIds.Any(id => State.PermitCases.Any(c => c.Id == id && c.IsPending)))
            return CommandResult.Fail(OffshoreReasons.AlreadyApplied, "An application is already being decided");
        var score = ScoreApplication(round, concept, State.Day);
        var terms = new PermitTerms(LicenceCal.AwardDelayDays, LicenceCal.AwardDelayDays, score.WouldBeAwarded ? 0.0 : 1.0);
        var conditions = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["score"] = score.Total,
            ["bar"] = score.AwardBar,
            ["novelty"] = score.Novelty,
            ["escape_free"] = score.EscapeFree,
            ["lice_compliance"] = score.LiceCompliance,
            ["financing"] = score.Financing,
        };
        var filed = SubmitPermitCase(PermitKind.DevelopmentLicence, plot.Id, "", terms, conditions: conditions);
        if (!filed.Ok) return filed;
        round.CompanyCaseIds.Add(filed.Id);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Søknad om utviklingstillatelser",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"Søknad for {plot.Name} sendt (poeng {score.Total:F2} mot terskel {score.AwardBar:F2}); vedtak dag {State.Day + LicenceCal.AwardDelayDays}"),
        });
        State.TrimEventLog();
        return CommandResult.Success($"Development-licence application {filed.Id} filed for {plot.Id}: score {score.Total:F2} against {score.AwardBar:F2}",
            score.Total, State.Day + LicenceCal.AwardDelayDays, filed.Id);
    }

    // ─── The unit's licences ──────────────────────────────────────────────

    /// <summary>The licences bound to (or, released, built on by) a unit, in register order.</summary>
    public IReadOnlyList<LicenceHolding> LicencesOfUnit(SeaSite site)
    {
        string award = site.Offshore?.AwardId ?? "";
        return State?.LicenceHoldings?.Where(l => l.BoundUnitId == site.Id || (award.Length > 0 && l.AwardId == award)).ToList()
               ?? (IReadOnlyList<LicenceHolding>)Array.Empty<LicenceHolding>();
    }

    /// <summary>
    /// The MTB an offshore unit may hold (t): a legacy unit its locality MTB as
    /// before F2-09; a unit on licences the MTB of its bound licences in force
    /// (development licences before expiry, converted ones once placed), never
    /// above its locality MTB.
    /// </summary>
    public double UnitMtbTonnes(SeaSite site)
    {
        var unit = site.Offshore;
        if (unit == null || !unit.HoldsLicences) return site.MABTonnes;
        int area = ProductionAreaOfSite(site);
        double tonnes = 0.0;
        foreach (var licence in State.LicenceHoldings)
        {
            if (licence.BoundUnitId != site.Id || !licence.IsInForce(State.Day)) continue;
            bool usable = licence.Kind == LicenceKind.Development
                          || (licence.Kind == LicenceKind.Commercial && !licence.AwaitingPlacement && licence.PooledArea == area);
            if (usable) tonnes += licence.MtbTonnes;
        }
        return Math.Min(site.MABTonnes, tonnes);
    }

    /// <summary>True when every licence bound to a unit on licences is converted and placed (it then counts in its area's pool and traffic light).</summary>
    internal bool UnitLicencesPlaced(SeaSite site)
    {
        if (site.Offshore is not { HoldsLicences: true }) return false;
        var bound = State.LicenceHoldings.Where(l => l.BoundUnitId == site.Id).ToList();
        return bound.Count > 0 && bound.All(l => l.Kind == LicenceKind.Commercial && !l.AwaitingPlacement);
    }

    /// <summary>
    /// A unit on licences may only be stocked while its licences cover it:
    /// licences_unplaced (converted, not yet placed), licence_expired (no
    /// licence in force behind it); null otherwise (a legacy unit always).
    /// </summary>
    internal CommandResult? OffshoreLicenceStockingRefusal(SeaSite site)
    {
        var unit = site.Offshore;
        if (unit == null || !unit.HoldsLicences) return null;
        var bound = State.LicenceHoldings.Where(l => l.BoundUnitId == site.Id).ToList();
        if (bound.Any(l => l.AwaitingPlacement))
            return CommandResult.Fail(OffshoreReasons.LicencesUnplaced, $"{site.Label}'s converted licences must be placed in a production area first");
        if (UnitMtbTonnes(site) <= 0.0)
            return bound.Any(l => l.Kind == LicenceKind.Commercial)
                ? CommandResult.Fail(OffshoreReasons.LicencesUnplaced, $"{site.Label}'s licences are placed in another production area")
                : CommandResult.Fail(OffshoreReasons.LicenceExpired, $"{site.Label} holds no licence in force");
        return null;
    }

    /// <summary>
    /// Whether a harvest from an offshore unit counts toward the resource rent
    /// (spec §3.6, keyed on the licence, not the location): a legacy unit as
    /// before F2-09 (yes); a unit on development licences no (V:3); once its
    /// licences are converted and placed, yes wherever it sits.
    /// </summary>
    internal bool OffshoreUnitRentLiable(string unitId)
    {
        var site = FindOffshoreUnit(unitId);
        if (site?.Offshore == null || !site.Offshore.HoldsLicences) return true;
        return UnitLicencesPlaced(site);
    }

    // ─── Conversion ───────────────────────────────────────────────────────

    /// <summary>
    /// The conversion checklist of a unit on development licences (spec §2C,
    /// F2-09 acceptance 4), each criterion with its value and target. Pure.
    /// </summary>
    public IReadOnlyList<ConversionCriterion> ConversionChecklist(SeaSite site)
    {
        var unit = site.Offshore;
        var cal = LicenceCal;
        var list = new List<ConversionCriterion>();
        if (unit == null) return list;
        list.Add(new ConversionCriterion("completed_cycles", unit.CompletedCycles, cal.ConversionMinCompletedCycles,
            unit.CompletedCycles >= cal.ConversionMinCompletedCycles));
        double unitMortality = SiteLifetimeMortalityShare(site);
        double areaMortality = AreaLiveMortalityShare(ProductionAreaOfSite(site));
        list.Add(new ConversionCriterion("mortality", unitMortality, areaMortality, unitMortality <= areaMortality));
        int escapeFree = UnitEscapeFreeDays(site);
        list.Add(new ConversionCriterion("escape_free_days", escapeFree, cal.ConversionEscapeFreeDays, escapeFree >= cal.ConversionEscapeFreeDays));
        bool standing = unit.HandlingStage is HandlingStage.Ordered or HandlingStage.Withdrawal || WithdrawalCaseOf(unit) is { Status: not SiteWithdrawalStatus.Lifted };
        list.Add(new ConversionCriterion("no_order_or_withdrawal", standing ? 0 : 1, 1, !standing));
        var development = LicencesOfUnit(site).Where(l => l.Kind == LicenceKind.Development).ToList();
        bool inForce = development.Count > 0 && development.All(l => l.IsInForce(State.Day));
        list.Add(new ConversionCriterion("licences_in_force", development.Count(l => l.IsInForce(State.Day)), development.Count, inForce));
        return list;
    }

    /// <summary>
    /// Share of the fish that passed through a site that died there: the
    /// mortality ledger's deaths at the site over its deaths, removals
    /// (harvests, escapes, transfers) and the fish alive there now.
    /// </summary>
    internal double SiteLifetimeMortalityShare(SeaSite site)
    {
        var ledger = MortalityLedger.ForSite(site.Id);
        double deaths = ledger.Total.Fish;
        double removed = MortalityCauses.AllRemovals.Sum(r => (double)ledger.Removed(r).Fish);
        double alive = site.Pens.Sum(p => (double)p.TotalCount);
        double passed = deaths + removed + alive;
        return passed <= 0.0 ? 0.0 : deaths / passed;
    }

    /// <summary>
    /// The production area's live mortality (spec §2B: "compared live with the
    /// game's own PO mortality, never as a fixed ratio"): the same share over
    /// the company's fjord sites in the area; the national figure (O:R55) when
    /// none has had fish.
    /// </summary>
    internal double AreaLiveMortalityShare(int productionArea)
    {
        double deaths = 0.0, passed = 0.0;
        foreach (var fjord in FjordSites())
        {
            if (ProductionAreaOfSite(fjord) != productionArea) continue;
            var ledger = MortalityLedger.ForSite(fjord.Id);
            double d = ledger.Total.Fish;
            deaths += d;
            passed += d + MortalityCauses.AllRemovals.Sum(r => (double)ledger.Removed(r).Fish) + fjord.Pens.Sum(p => (double)p.TotalCount);
        }
        return passed <= 0.0 ? LicenceCal.FallbackAreaMortality : deaths / passed;
    }

    /// <summary>Days since the unit's last escape (its age when it had none).</summary>
    private int UnitEscapeFreeDays(SeaSite site)
    {
        int last = site.EscapeIncidents.Count == 0 ? int.MinValue : site.EscapeIncidents.Max(e => e.Day);
        int since = site.Offshore?.CommissionedDay >= 0 ? site.Offshore.CommissionedDay : 0;
        return last == int.MinValue ? Math.Max(0, State.Day - since) : Math.Max(0, State.Day - last);
    }

    /// <summary>The unit's conversion case, or null.</summary>
    public PermitCase? ConversionCaseOf(SeaSite site)
    {
        string id = site.Offshore?.ConversionCaseId ?? "";
        return id.Length == 0 ? null : State?.PermitCases?.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>
    /// File the conversion of a unit's development licences (spec §3.4
    /// <c>RequestConversion(siteId)</c>): decided after 60-120 days on the
    /// criteria met on that day. Fails: not_found, licence_missing (no
    /// development licences behind it), already_applied (a conversion pending,
    /// approved or done).
    /// </summary>
    public CommandResult RequestConversion(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!LicencesOfUnit(site).Any(l => l.Kind == LicenceKind.Development))
            return CommandResult.Fail(OffshoreReasons.LicenceMissing, $"{site.Label} holds no development licences");
        var existing = ConversionCaseOf(site);
        if (existing != null && existing.Status is not (PermitStatus.Lapsed or PermitStatus.Withdrawn))
            return CommandResult.Fail(OffshoreReasons.AlreadyApplied, $"{site.Label}'s conversion is {existing.Status}");
        bool met = ConversionChecklist(site).All(c => c.Met);
        var terms = new PermitTerms(LicenceCal.ConversionDecisionDaysMin, LicenceCal.ConversionDecisionDaysMax, met ? 0.0 : 1.0);
        var filed = SubmitPermitCase(PermitKind.Conversion, site.PlotId, site.Id, terms);
        if (!filed.Ok) return filed;
        unit.ConversionCaseId = filed.Id;
        return CommandResult.Success($"{site.Label}: conversion {filed.Id} filed, decided on day {filed.Value2}", met ? 1.0 : 0.0, filed.Value2, filed.Id);
    }

    /// <summary>Appeal a refused conversion (spec §2C: Havfarm-class was refused in 2021 and granted on appeal in 2022): decided after 180 days on the criteria met then. Fails: not_found, not_appealable.</summary>
    public CommandResult AppealConversion(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var permit = site == null ? null : ConversionCaseOf(site);
        if (site == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (permit == null || permit.Status != PermitStatus.Refused)
            return CommandResult.Fail(OffshoreReasons.NotAppealable, $"{site.Label} has no refused conversion");
        bool met = ConversionChecklist(site).All(c => c.Met);
        return AppealPermitCase(permit.Id, new PermitTerms(LicenceCal.ConversionAppealDays, LicenceCal.ConversionAppealDays, met ? 0.0 : 1.0));
    }

    /// <summary>
    /// The fee an approved conversion asks on <paramref name="day"/>: each
    /// development licence at the indexed fee, plus any MTB taken above a
    /// licence's base size at the fixed growth price (V:2).
    /// </summary>
    public decimal ConversionFeeNok(SeaSite site, int day)
    {
        var development = LicencesOfUnit(site).Where(l => l.Kind == LicenceKind.Development).ToList();
        double growth = development.Sum(l => Math.Max(0.0, l.MtbTonnes - CatalogCal.LicenceSizeTonnesMtb));
        return development.Count * ConversionFeePerLicenceNok(day) + (decimal)growth * LicenceCal.FixedGrowthPriceNokPerTonne;
    }

    /// <summary>
    /// Pay an approved conversion (spec §3.4 <c>PayConversion(siteId)</c>): the
    /// licences become ordinary licences awaiting placement. Fails: not_found,
    /// nothing_due, payment_lapsed, insufficient_funds.
    /// </summary>
    public CommandResult PayConversion(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        var permit = ConversionCaseOf(site);
        if (permit?.Status == PermitStatus.Lapsed)
            return CommandResult.Fail(OffshoreReasons.PaymentLapsed, $"{site.Label}'s conversion lapsed unpaid");
        if (permit == null || permit.Status != PermitStatus.Approved || unit.ConversionPaymentDueDay < 0)
            return CommandResult.Fail(OffshoreReasons.NothingDue, $"{site.Label} has no conversion to pay");
        decimal fee = unit.ConversionFeeDueNok;
        if (!TryPayCost(fee, CostBooking.Capex, "licence_conversion"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the conversion costs {fee:N0} NOK", (double)fee);
        var licences = LicencesOfUnit(site).Where(l => l.Kind == LicenceKind.Development).ToList();
        decimal each = licences.Count == 0 ? 0m : Math.Round(fee / licences.Count, 0);
        foreach (var licence in licences)
        {
            licence.Kind = LicenceKind.Commercial;
            licence.AwaitingPlacement = true;
            licence.ExpiresDay = int.MaxValue;
            licence.PricePaidNok = each;
            licence.PlacedZoneId = "";
        }
        unit.ConversionPaymentDueDay = -1;
        unit.ConversionFeeDueNok = 0m;
        Pacing.Resolve(GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.ConversionPaymentDue).Category, site.Id, ""));
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Konvertering betalt",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{licences.Count} utviklingstillatelser for {site.Label} er konvertert for {fee:N0} kr: plasser dem i et produksjonsområde før bruk"),
            BalanceChange = -fee,
        });
        State.TrimEventLog();
        return CommandResult.Success($"{site.Label}: {licences.Count} licences converted for {fee:N0} NOK", (double)fee, licences.Count, site.Id);
    }

    /// <summary>
    /// Place converted licences in a production area (spec §3.4
    /// <c>PlaceLicences(holdingIds, zoneId)</c>): they pool in the zone's
    /// production area from then on, count in its traffic light and pay the
    /// resource rent; a unit in that area uses the ones bound to it. Fails:
    /// not_found (an id that is no converted licence awaiting placement),
    /// zone_not_found, zone_full (a red zone takes no growth).
    /// </summary>
    public CommandResult PlaceLicences(IReadOnlyCollection<string> holdingIds, string zoneId)
    {
        if (State == null) return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var ids = holdingIds?.ToList() ?? new List<string>();
        var licences = ids.Select(id => State.LicenceHoldings.FirstOrDefault(l => l.Id == id)).ToList();
        if (ids.Count == 0 || licences.Any(l => l == null || !l.AwaitingPlacement))
            return CommandResult.Fail(OffshoreReasons.NotFound, "Only converted licences awaiting placement can be placed");
        if (!IsKnownZone(zoneId))
            return CommandResult.Fail(OffshoreReasons.ZoneNotFound, $"No zone '{zoneId}'");
        if (ZoneTrafficLightColor(zoneId) == ZoneColor.Red)
            return CommandResult.Fail(OffshoreReasons.ZoneFull, $"'{zoneId}' is red: it takes no new capacity");
        int area = ProductionAreaOfZoneId(zoneId);
        foreach (var licence in licences)
        {
            licence!.AwaitingPlacement = false;
            licence.PlacedZoneId = zoneId;
            licence.PooledArea = area;
        }
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Tillatelser plassert",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{licences.Count} konverterte tillatelser ({licences.Sum(l => l!.MtbTonnes):N0} t MTB) er plassert i {ZoneLabelOf(zoneId)}"),
        });
        State.TrimEventLog();
        return CommandResult.Success($"{licences.Count} licences placed in {zoneId} (area {area})", licences.Sum(l => l!.MtbTonnes), area);
    }

    /// <summary>The production area of a game zone or a catalogue zone (the home area for an unknown one).</summary>
    private int ProductionAreaOfZoneId(string zoneId)
    {
        var zone = FindZone(zoneId);
        if (zone != null) return ProductionAreaOf(zone);
        return ExpansionZones.TryGet(zoneId, out var definition) ? definition.ProductionArea : State.HomeProductionArea;
    }

    /// <summary>
    /// The conversion cases' day: a pending case (first decision or appeal)
    /// follows the criteria met today (decided on its day by them); an approved
    /// case asks its fee (indexed on its decision day) within 90 days; an
    /// unpaid one lapses.
    /// </summary>
    private void FollowConversionCases(int today)
    {
        foreach (var site in OffshoreUnits)
        {
            var unit = site.Offshore!;
            var permit = ConversionCaseOf(site);
            if (permit == null) continue;
            if (permit.IsPending)
            {
                permit.RefusalProbability = ConversionChecklist(site).All(c => c.Met) ? 0.0 : 1.0;
                continue;
            }
            if (permit.Status == PermitStatus.Approved && unit.ConversionPaymentDueDay < 0
                && LicencesOfUnit(site).Any(l => l.Kind == LicenceKind.Development) && permit.DecidedDay >= 0
                && !(permit.Conditions.TryGetValue("payment_asked", out var asked) && asked > 0))
            {
                permit.Conditions["payment_asked"] = 1.0;
                unit.ConversionPaymentDueDay = permit.DecidedDay + LicenceCal.ConversionPaymentDays;
                unit.ConversionFeeDueNok = ConversionFeeNok(site, permit.DecidedDay);
                PublishGameEvent(new GameEventRequest(GameEventType.ConversionDecision, today)
                {
                    SiteId = site.Id,
                    UnitId = site.Id,
                    Variant = permit.Id,
                    Message = SimText.Nb($"Konverteringen for {site.Label} er innvilget: {unit.ConversionFeeDueNok:N0} kr betales innen dag {unit.ConversionPaymentDueDay}"),
                });
            }
            if (permit.Status == PermitStatus.Approved && unit.ConversionPaymentDueDay >= 0)
            {
                if (today > unit.ConversionPaymentDueDay)
                {
                    permit.Status = PermitStatus.Lapsed;
                    unit.ConversionPaymentDueDay = -1;
                    unit.ConversionFeeDueNok = 0m;
                    Pacing.Resolve(GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.ConversionPaymentDue).Category, site.Id, ""));
                    State.EventLog.Add(new EventRecord
                    {
                        Day = today,
                        Name = "Konvertering bortfalt",
                        Category = EventCategory.Regulation,
                        Description = SimText.Nb($"Vederlaget for konverteringen av {site.Label} ble ikke betalt i tide: tilsagnet er bortfalt"),
                    });
                    State.TrimEventLog();
                    continue;
                }
                PublishGameEvent(new GameEventRequest(GameEventType.ConversionPaymentDue, today)
                {
                    SiteId = site.Id,
                    UnitId = site.Id,
                    DeadlineDay = unit.ConversionPaymentDueDay,
                    Value = (double)unit.ConversionFeeDueNok,
                    Message = SimText.Nb($"Betal {unit.ConversionFeeDueNok:N0} kr for konverteringen av {site.Label} innen dag {unit.ConversionPaymentDueDay}"),
                });
            }
        }
    }
}

/// <summary>One criterion of a conversion's checklist: its key, the unit's value, the target and whether it is met.</summary>
public sealed record ConversionCriterion(string Key, double Value, double Target, bool Met);
