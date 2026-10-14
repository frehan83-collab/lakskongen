using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;
using LakskongenSim.Events;

namespace LakskongenSim.Core;

/// <summary>
/// The engine's side of the typed event bus (roadmap U2-06). The engine
/// reports what happens (storm damage, a wellboat's work, a bank warning, a
/// traffic light turning, an insurance payout, a storyteller event) as typed
/// events; the snapshot layer reports the conditions that hold. Neither the
/// bus nor this partial draws from an RNG stream or changes the game state,
/// and the bus is not saved (<see cref="Reset"/> empties it).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// A storm whose impact severity reaches this is reported as critical
    /// (pause and a card); a lighter one is a warning. The same line the
    /// presentation layer drew before the bus (ConsequenceDirector, SimBridge).
    /// </summary>
    public const double CriticalStormEventSeverity = 0.8;

    /// <summary>The typed event bus the player is told through.</summary>
    public GameEventBus GameEvents { get; } = new();

    /// <summary>Report an event on the bus.</summary>
    public GameEventPublishResult PublishGameEvent(GameEventRequest request) => GameEvents.Publish(request);

    /// <summary>
    /// The day notes of an advance as typed events (called as the ledger is
    /// drained, when every note carries its own day). Bankruptcy ends the game
    /// through its own path; licence auctions report through their own flow.
    /// </summary>
    private void PublishNoteEvents(IEnumerable<DayNote> notes)
    {
        foreach (var note in notes)
        {
            switch (note.Kind)
            {
                case "storm_impact":
                    PublishGameEvent(new GameEventRequest(GameEventType.StormDamage, note.Day)
                    {
                        Severity = note.Severity >= CriticalStormEventSeverity
                            ? GameEventSeverity.Critical
                            : GameEventSeverity.Warning,
                        Message = note.Message,
                        Value = note.Severity
                    });
                    break;
                case "wellboat_delouse":
                case "wellboat_harvest":
                case "wellboat_rov":
                case "wellboat_freshwater":
                    PublishWellboatEvent(note);
                    break;
                case "solvency_warning":
                    PublishSolvencyEvent(note);
                    break;
            }
        }
    }

    private void PublishWellboatEvent(DayNote note)
    {
        string kind = note.Kind["wellboat_".Length..];
        // ROV and freshwater notes carry their own Norwegian report; delouse
        // and harvest notes carry the job kind (or the command's error).
        bool ownText = kind is "rov" or "freshwater";
        string message = ownText
            ? note.Message
            : note.Success
                ? SimText.Nb($"Brønnbåten utførte {WellboatJobName(kind)} på {SiteLabelOf(note.SiteId)}")
                : SimText.Nb($"Brønnbåten kunne ikke utføre {WellboatJobName(kind)} på {SiteLabelOf(note.SiteId)}");
        PublishGameEvent(new GameEventRequest(
            note.Success ? GameEventType.WellboatCompleted : GameEventType.WellboatReturned, note.Day)
        {
            SiteId = note.SiteId,
            Variant = kind,
            Message = message
        });
    }

    private void PublishSolvencyEvent(DayNote note)
    {
        var type = (int)note.Severity switch
        {
            1 => GameEventType.SolvencyOverdraft,
            2 => GameEventType.SolvencyBankWarning,
            _ => GameEventType.SolvencyFinalWarning
        };
        // The rung's text is the event-log record it wrote (id = the rung's key).
        string message = State.EventLog.LastOrDefault(r => r.Id == note.Message)?.Description ?? "";
        PublishGameEvent(new GameEventRequest(type, note.Day) { Message = message, Value = note.Value });
    }

    /// <summary>
    /// A storyteller event: a warning when it cost the player money or hit the
    /// fish (disease, plankton), otherwise information. Judged by its typed
    /// category and its cash, never by its text.
    /// </summary>
    private void PublishStoryEvent(EventRecord record)
    {
        bool hurts = record.BalanceChange < 0m
            || record.Category is EventCategory.Disease or EventCategory.PlanktonBloom or EventCategory.PlanktonCrash;
        PublishGameEvent(new GameEventRequest(GameEventType.StoryEvent, record.Day)
        {
            Severity = hurts ? GameEventSeverity.Warning : GameEventSeverity.Info,
            Variant = record.Id,
            Headline = record.Name,
            Message = record.Description,
            Value = (double)record.BalanceChange
        });
    }

    /// <summary>A zone's traffic light turned: red is a warning, the rest information.</summary>
    private void PublishTrafficLightEvent(string zoneId, ZoneColor after, string description)
    {
        PublishGameEvent(new GameEventRequest(
            after == ZoneColor.Red ? GameEventType.TrafficLightRed : GameEventType.TrafficLightChanged, State.Day)
        {
            ZoneId = zoneId,
            Message = description
        });
    }
}
