using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// QA staging seams (roadmap F2-20): what the farm-type QA set needs from the
/// engine that no player verb gives at once. Never called by the game: the
/// bridge's QA verbs (GameBridge.FacilityVisuals.cs) reach them for
/// UiFlowTests, the capture scenes and the perf harness only
/// (tests/test_facility_presenter.py fails on any other caller).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// QA: contract an offshore unit of <paramref name="type"/> on
    /// <paramref name="plotId"/> as <see cref="BuildOffshoreUnit"/> does
    /// (managed, full equity: the signing payment, the site, the construction
    /// project with the tow waiting for the player), with the farm-type
    /// unlock gates waived (research, a harvest sold, the escape-free year:
    /// a QA scenario's events may hold the last one back for years) and, when
    /// the company holds none, a development-licence award for the plot as an
    /// approved application gives one (F2-09's test seam). Every other check
    /// stands: plot_not_found, not_an_offshore_slot, plot_taken,
    /// exposure_exceeds_design, invalid_type, insufficient_funds.
    /// </summary>
    public CommandResult QaContractOffshoreUnit(string plotId, OffshoreType type)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        string refusal = OffshoreBuildRefusal(plotId, type, 1.0, out var plot);
        if (refusal == OffshoreReasons.NotUnlocked || refusal == OffshoreReasons.LicenceMissing)
            refusal = "";
        if (refusal.Length > 0 || plot == null)
            return CommandResult.Fail(refusal.Length > 0 ? refusal : OffshoreReasons.PlotNotFound,
                $"Cannot build a {type} unit on '{plotId}': {refusal}");
        string concept = OffshoreProfiles.VariantOf(type);
        if (BuildableAward(plot.Id, concept) == null)
            AwardDevelopmentLicencesForTests(plot.Id, concept);
        return ContractOffshoreUnit(plot, type, ContractForm.Managed, 1.0);
    }
}
