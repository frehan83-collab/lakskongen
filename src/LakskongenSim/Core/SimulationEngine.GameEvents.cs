using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The engine's side of the typed event bus (roadmap U2-06). The engine
/// reports what happens (an escape, storm damage, a wellboat's work, a bank
/// warning, a traffic light turning, an insurance payout, a storyteller
/// event) as typed
/// events; the snapshot layer reports the conditions that hold. Neither the
/// bus nor this partial draws from an RNG stream or changes the game state,
/// and the bus is not saved (<see cref="Reset"/> empties it).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The typed event bus the player is told through.</summary>
    public GameEventBus GameEvents { get; } = new();

    private PacingGovernor? _pacing;

    /// <summary>
    /// When the game clock slows down and stops by itself (roadmap U2-07):
    /// every counterable deadline the bus delivers is watched, and "Til neste
    /// hendelse" ends at the next warning or critical. Emptied with the bus
    /// (a new or loaded game); presentation state like the bus.
    /// </summary>
    public PacingGovernor Pacing
    {
        get
        {
            if (_pacing == null)
            {
                _pacing = new PacingGovernor();
                GameEvents.Cleared += _pacing.Clear;
            }
            return _pacing;
        }
    }

    /// <summary>Report an event on the bus; what it delivers also reaches the pacing rules.</summary>
    public GameEventPublishResult PublishGameEvent(GameEventRequest request)
    {
        var result = GameEvents.Publish(request);
        if (result.Outcome != GameEventOutcome.Suppressed)
            Pacing.Observe(result.Event);
        return result;
    }

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
                    // The log: the forecast was the warning (storm_forecast),
                    // and what needs an answer after a storm is its own event
                    // (an escape, worn nets, mortality).
                    PublishGameEvent(new GameEventRequest(GameEventType.StormDamage, note.Day)
                    {
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
                case "liquidity_warning":
                case "bank_call":
                case "rescue_loan":
                    PublishLiquidityEvent(note);   // U2-03
                    break;
            }
        }
        PublishStandingOrders();
    }

    /// <summary>
    /// Mattilsynet's orders with a deadline (U2-07): a lice-treatment order
    /// (two counts over the limit: delouse within 14 days or Mattilsynet
    /// delouses at the player's cost) and an over-MTB order (a red zone: get
    /// under MTB within 14 days or the fish is harvested at a penalty price).
    /// They were only written to the event log. Reported like conditions on
    /// every drain while they stand, with the order's own deadline, so the
    /// deadline rule slows and stops the clock before it; an order that was
    /// met, lifted or enforced stops being watched.
    /// </summary>
    private void PublishStandingOrders()
    {
        if (State?.SeaSites == null) return;
        foreach (var site in State.SeaSites)
        {
            if (site.TreatmentOrderDueDay >= 0)
            {
                PublishGameEvent(new GameEventRequest(GameEventType.LiceTreatmentOrder, State.Day)
                {
                    SiteId = site.Id,
                    DeadlineDay = site.TreatmentOrderDueDay,
                    Value = site.LastLiceCount,
                    Message = SimText.Nb($"Mattilsynet: {site.Label} har vært over lusegrensen to tellinger på rad ({site.LastLiceCount:F2} voksne hunnlus per fisk). Avlus før fristen, ellers avluser Mattilsynet for din regning.")
                });
            }
            else
            {
                Pacing.Resolve(StandingOrderKey(GameEventType.LiceTreatmentOrder, site.Id));
            }

            if (site.HasActiveMattilsynetOrder && !site.MattilsynetOrderFulfilled)
            {
                PublishGameEvent(new GameEventRequest(GameEventType.BiomassOrder, State.Day)
                {
                    SiteId = site.Id,
                    DeadlineDay = site.MattilsynetDeadlineDay,
                    Value = site.CurrentBiomassTonnes,
                    Message = SimText.Nb($"Mattilsynet: {site.Label} er over MTB ({site.CurrentBiomassTonnes:F1} t mot {site.MABTonnes:F1} t tillatt) i en rød sone. Slakt eller avlus ned før fristen, ellers tvangsslaktes fisken til straffepris.")
                });
            }
            else
            {
                Pacing.Resolve(StandingOrderKey(GameEventType.BiomassOrder, site.Id));
            }
        }
    }

    /// <summary>The bus key (and pacing key) of a site's standing order.</summary>
    internal static string StandingOrderKey(GameEventType type, string siteId) =>
        GameEventBus.KeyOf(GameEventCatalog.Describe(type).Category, siteId, "");

    /// <summary>Fish escaped from a site: critical (fines, Mattilsynet, the nets).</summary>
    private void PublishEscapeEvent(SeaSite site, int escapees, string description)
    {
        PublishGameEvent(new GameEventRequest(GameEventType.Escape, State.Day)
        {
            SiteId = site.Id,
            Message = description,
            Value = escapees
        });
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
