using LakskongenSim.Events;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The typed events of sim·core's farm-type mechanics (roadmap F2-05, spec
/// §3.5): a construction phase that ends (information), a managed build's
/// cost overrun (a warning: the bill is owed) and a permit decision
/// (information). Each names its unit (<see cref="GameEvent.UnitId"/>), so
/// the notification meter counts it under the unit and the UI says which
/// unit it is about. Presentation only, like every bus report: no stream,
/// no game state.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>A project's phase <paramref name="phaseIndex"/> ended today, after paying <paramref name="overrunNok"/> of overrun.</summary>
    private void PublishConstructionPhaseEvents(ConstructionProject project, int phaseIndex, decimal overrunNok)
    {
        string unit = UnitLabel(project.UnitId);
        int phases = project.Phases.Count;
        bool complete = phaseIndex + 1 >= phases;
        PublishGameEvent(new GameEventRequest(GameEventType.ConstructionPhaseComplete, State.Day)
        {
            UnitId = project.UnitId,
            Variant = $"{project.Id}/{phaseIndex + 1}",
            Value = phaseIndex + 1,
            Message = complete
                ? SimText.Nb($"{unit} er ferdig bygget: alle {phases} byggefaser er fullført, og anlegget er satt i drift.")
                : SimText.Nb($"Byggefase {phaseIndex + 1} av {phases} på {unit} er fullført."),
        });
        if (overrunNok > 0m)
        {
            PublishGameEvent(new GameEventRequest(GameEventType.CapexOverrun, State.Day)
            {
                UnitId = project.UnitId,
                Variant = project.Id,
                Value = (double)overrunNok,
                Message = SimText.Nb($"Byggefase {phaseIndex + 1} av {phases} på {unit} gikk over budsjettet: {overrunNok:N0} kr betales på kreditt."),
            });
        }
    }

    /// <summary>A permit case was decided today.</summary>
    private void PublishPermitDecision(PermitCase permit)
    {
        bool approved = permit.Status == PermitStatus.Approved;
        string what = PermitName(permit.Kind);
        string subject = string.IsNullOrEmpty(permit.UnitId) ? "" : SimText.Nb($" for {UnitLabel(permit.UnitId)}");
        PublishGameEvent(new GameEventRequest(GameEventType.PermitDecision, State.Day)
        {
            UnitId = permit.UnitId,
            Variant = $"{permit.Id}/{permit.Round}",
            Value = approved ? 1.0 : 0.0,
            Message = approved
                ? SimText.Nb($"Søknaden om {what}{subject} er innvilget.")
                : SimText.Nb($"Søknaden om {what}{subject} er avslått. Avslaget kan påklages."),
        });
    }

    /// <summary>The Norwegian name of a permit kind as the player reads it.</summary>
    private static string PermitName(PermitKind kind) => kind switch
    {
        PermitKind.Zoning => "reguleringsplan",
        PermitKind.LandAquaculture => "akvakulturtillatelse på land",
        PermitKind.Discharge => "utslippstillatelse",
        PermitKind.SeaWorks => "tillatelse til tiltak i sjø",
        PermitKind.Biosecurity => "godkjenning av smittevern",
        PermitKind.GridConnection => "nettilknytning",
        PermitKind.DevelopmentLicence => "utviklingstillatelse",
        PermitKind.Conversion => "konvertering av utviklingstillatelser",
        PermitKind.Dispensation => "dispensasjon",
        PermitKind.OffshoreArea => "tildeling i havområde",
        _ => "tillatelse",
    };
}
