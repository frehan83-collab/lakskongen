using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The sea lane's F2-08/F2-09 views for the bridge and the window board
/// (spec §6.5, F2-33 acceptance 5: days without a handling window against
/// 21/45/75, MTB headroom, the tilt hazard with its drivers; the licence
/// path of §6.1 acts 3-4). Pure projections: none draws or changes state.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// An offshore unit's handling, incident and counter-verb state (spec
    /// §3.3 "Handling overdue", "Incidents"; §6.5). Null for an unknown site.
    /// </summary>
    public OffshoreHandlingView? GetOffshoreHandlingState(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null || State == null) return null;
        int today = State.Day;
        var h = OffshoreCal.Handling;
        var op = unit.Requirement == HandlingRequirement.None ? OffshoreOp.Harvest : OperationFor(unit.Requirement);
        var window = FirstWindow(site, op, VesselClass.Standard, GreenWindowProbability);
        double mtb = UnitMtbTonnes(site);
        double biomass = site.CurrentBiomassTonnes;
        var withdrawal = WithdrawalCaseOf(unit);
        var tilt = TiltHazardDrivers(site);
        double hazard = TiltHazardModel.Of(tilt);
        var incident = ActiveIncidentOf(site);
        var review = OffshoreCal.AlarmReview;
        return new OffshoreHandlingView(
            site.Id,
            RequirementKey(unit.Requirement),
            unit.RequirementSinceDay,
            unit.HandlingOverdueDays,
            unit.DaysWithoutHandlingWindow,
            h.WarnDays, h.OrderDays, h.WithdrawalDays,
            unit.HandlingStage.ToString(),
            unit.OrderDay,
            unit.HandlingAnchorDay,
            unit.LastHandlingDoneDay,
            OperationRowOf(op),
            window.Probability >= GreenWindowProbability ? window.Day : -1,
            Math.Max(0.0, window.Probability),
            mtb, biomass, ProjectedUnitBiomassTonnes(site, h.MtbProjectionDays), h.MtbReliefShare * mtb, mtb - biomass,
            unit.NetKnownAsOfDay, unit.NetKnownAsOfDay + h.NetInspectionIntervalDays,
            unit.NetKnownAsOfDay + h.NetInspectionIntervalDays + h.NetInspectionOverdueDays,
            withdrawal?.Id ?? "", withdrawal?.Status.ToString() ?? "", withdrawal?.EffectiveDay ?? -1,
            withdrawal?.Status == SiteWithdrawalStatus.Decided,
            tilt, hazard, 1.0 - Math.Pow(1.0 - Math.Clamp(hazard, 0.0, 1.0), SimCalendar.DaysPerYear),
            incident?.Id ?? "", incident?.Kind.ToString() ?? "",
            unit.AlarmReviewStartedDay, unit.AlarmReviewStartedDay < 0 ? -1 : unit.AlarmReviewStartedDay + review.Days,
            AlarmLoadModel.ReviewRunning(unit, today, review), AlarmLoadModel.ReviewInForce(unit, today, review),
            review.CostNok * (decimal)unit.EquityShare,
            unit.HatchProcedureLevel, unit.ProcedureUpgradeDoneDay > today ? unit.ProcedureUpgradeDoneDay : -1,
            OffshoreCal.Procedures.CostNok * (decimal)unit.EquityShare,
            unit.ReinforcementDoneDay, unit.DesignHsM + unit.DesignHsBonusM,
            OffshoreCal.Reinforcement.CapexNok * (decimal)unit.EquityShare);
    }

    /// <summary>The board row an operation is read on with the standard vessel ("" when the board has none).</summary>
    private static string OperationRowOf(OffshoreOp op)
    {
        if (op == OffshoreOp.Harvest) op = OffshoreOp.Delousing; // harvest and delousing share the wellboat row
        foreach (var (id, rowOp, vessel) in SeaBoardRows)
            if (rowOp == op && vessel == VesselClass.Standard) return id;
        return "";
    }

    /// <summary>
    /// The development-licence round as the player sees it (spec §6.1 act 3):
    /// the open round (or the last one, or none yet), the company's gate, the
    /// rival field's band and bar, the company's applications and every award.
    /// Never null once the game runs.
    /// </summary>
    public DevelopmentRoundView? GetDevelopmentRoundView()
    {
        if (State == null) return null;
        var round = OpenDevelopmentRound() ?? State.DevelopmentRounds.LastOrDefault();
        int today = State.Day;
        bool research = Research?.Nodes != null && Research.Nodes.TryGetValue(DeepSeaResearchId, out var node) && node.Unlocked;
        var applications = round == null
            ? new List<PermitCase>()
            : round.CompanyCaseIds.Select(id => State.PermitCases.FirstOrDefault(c => c.Id == id)).Where(c => c != null).Select(c => c!).ToList();
        var awards = new List<DevelopmentAwardView>();
        foreach (var award in State.DevelopmentAwards)
        {
            string holder = "";
            if (!award.IsCompany)
            {
                var r = State.DevelopmentRounds.SelectMany(x => x.Rivals).FirstOrDefault(x => x.RivalId == award.RivalId);
                holder = r?.RivalName ?? award.RivalId;
            }
            awards.Add(new DevelopmentAwardView(award.Id, award.RoundId, award.Concept, award.PlotId, award.IsCompany, holder,
                award.AwardedDay, award.ConstructionDeadlineDay, award.Licences, award.LicenceTonnes, award.Status.ToString(), award.UnitId));
        }
        var field = round?.FieldScores() ?? Array.Empty<double>();
        return new DevelopmentRoundView(
            round?.Id ?? "", round?.OpenedDay ?? -1, round?.ClosesDay ?? -1, round?.IsOpenOn(today) == true,
            round?.RivalAwards ?? RivalOffshoreActive, round?.Rivals.Count ?? 0, round?.Rivals.Count(r => r.SubmittedDay <= today) ?? 0,
            field.Count == 0 ? 0.0 : field[0], round?.FieldMedian() ?? 0.0, field.Count == 0 ? 0.0 : field[^1],
            round?.AwardBar(LicenceCal) ?? LicenceCal.AwardFloorScore,
            RoundGateMet(), research, State.HarvestBatchesSold, EscapeFreeDays(), CatalogCal.Of1.RoundEscapeFreeDays,
            applications, awards);
    }

    /// <summary>
    /// A unit's licences and their conversion (spec §2C, §6.1 act 4): the
    /// licences behind it, the MTB they give, the checklist, the conversion
    /// case with its payment, and whether it pools and pays the rent. Null
    /// for an unknown site.
    /// </summary>
    public OffshoreLicenceView? GetOffshoreLicenceView(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null || State == null) return null;
        var permit = ConversionCaseOf(site);
        int today = State.Day;
        return new OffshoreLicenceView(site.Id, unit.HoldsLicences, unit.AwardId, UnitMtbTonnes(site), UnitLicencesPlaced(site),
            OffshoreUnitRentLiable(site.Id), UnitCountsInItsAreaPool(site), ProductionAreaOfSite(site),
            LicencesOfUnit(site), ConversionChecklist(site),
            permit?.Id ?? "", permit?.Status.ToString() ?? "", permit?.DecisionDay ?? -1, permit?.DecidedDay ?? -1,
            permit?.Status == PermitStatus.Refused,
            unit.ConversionPaymentDueDay, unit.ConversionFeeDueNok, ConversionFeeNok(site, today), ConversionFeePerLicenceNok(today));
    }
}

/// <summary>
/// An offshore unit's handling state (<see cref="SimulationEngine.GetOffshoreHandlingState"/>).
/// Requirement as <see cref="SimulationEngine.RequirementKey"/>; Stage the
/// <see cref="HandlingStage"/> name; days -1 when none.
/// </summary>
public sealed record OffshoreHandlingView(
    string SiteId,
    string Requirement,
    int RequirementSinceDay,
    int OverdueDays,
    int DaysWithoutWindow,
    int WarnDays,
    int OrderDays,
    int WithdrawalDays,
    string Stage,
    int OrderDay,
    int ClockAnchorDay,
    int LastHandlingDay,
    string WindowRow,
    int NextWindowDay,
    double NextWindowProbability,
    double MtbTonnes,
    double BiomassTonnes,
    double ProjectedBiomassTonnes,
    double ReliefThresholdTonnes,
    double HeadroomTonnes,
    int NetKnownDay,
    int NetInspectionDueDay,
    int NetInspectionOverdueDay,
    string WithdrawalCaseId,
    string WithdrawalStatus,
    int WithdrawalEffectiveDay,
    bool WithdrawalAppealable,
    IReadOnlyList<OffshoreDriver> TiltDrivers,
    double TiltHazardPerDay,
    double TiltHazardPerYear,
    string ActiveIncidentId,
    string ActiveIncidentKind,
    int AlarmReviewStartedDay,
    int AlarmReviewDoneDay,
    bool AlarmReviewRunning,
    bool AlarmReviewInForce,
    decimal AlarmReviewCostNok,
    double HatchProcedureLevel,
    int ProcedureUpgradeDoneDay,
    decimal ProcedureUpgradeCostNok,
    int ReinforcementDoneDay,
    double DesignHsM,
    decimal ReinforcementCapexNok);

/// <summary>One development-licence award as the player sees it (Holder "" for the company's own, else the rival's name).</summary>
public sealed record DevelopmentAwardView(string Id, string RoundId, string Concept, string PlotId, bool IsCompany, string Holder,
    int AwardedDay, int ConstructionDeadlineDay, int Licences, double LicenceTonnes, string Status, string UnitId);

/// <summary>The development-licence round (<see cref="SimulationEngine.GetDevelopmentRoundView"/>); Id "" before any round opened.</summary>
public sealed record DevelopmentRoundView(
    string Id,
    int OpenedDay,
    int ClosesDay,
    bool Open,
    bool RivalAwards,
    int RivalsExpected,
    int RivalsFiled,
    double FieldMin,
    double FieldMedian,
    double FieldMax,
    double AwardBar,
    bool GateMet,
    bool ResearchDone,
    int HarvestsSold,
    int EscapeFreeDays,
    int EscapeFreeTarget,
    IReadOnlyList<PermitCase> Applications,
    IReadOnlyList<DevelopmentAwardView> Awards);

/// <summary>A unit's licences and conversion (<see cref="SimulationEngine.GetOffshoreLicenceView"/>).</summary>
public sealed record OffshoreLicenceView(
    string SiteId,
    bool HoldsLicences,
    string AwardId,
    double MtbTonnes,
    bool Placed,
    bool RentLiable,
    bool CountsInAreaPool,
    int ProductionArea,
    IReadOnlyList<LicenceHolding> Licences,
    IReadOnlyList<ConversionCriterion> Checklist,
    string ConversionCaseId,
    string ConversionStatus,
    int ConversionDecisionDay,
    int ConversionDecidedDay,
    bool ConversionAppealable,
    int PaymentDueDay,
    decimal FeeDueNok,
    decimal FeeTodayNok,
    decimal FeePerLicenceTodayNok);
