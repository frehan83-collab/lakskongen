using System;
using System.Collections.Generic;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Explicit Norwegian traffic-light (trafikklys) regulatory state machine.
    ///
    /// Models production-zone status (green/yellow/red per zone) with Maximum
    /// Allowable Biomass (MAB) consequences, per the Norwegian
    /// Directorate of Fisheries scheme:
    /// - GREEN: lice pressure low → MAB may grow +6%.
    /// - YELLOW: hold → MAB unchanged.
    /// - RED: lice pressure high → MAB must shrink −6%.
    ///
    /// The MAB factors intentionally match the legacy event-only tweaks
    /// (±6% in NorwegianEvents traffic-light-green/red) so enabling the
    /// machine never changes game balance by itself.
    ///
    /// Gating: <see cref="UseStateMachineForEvents"/> defaults to false.
    /// With the gate off, NorwegianEvents keeps its exact legacy behavior and
    /// this machine is a passive, queryable observer. With the gate on, the
    /// green/red regulatory events delegate MAB changes through
    /// <see cref="ApplyGreenStatus"/> / <see cref="ApplyRedStatus"/>.
    /// </summary>
    public class TrafficLightSystem
    {
        /// <summary>MAB factor applied on GREEN status (+6%, matches legacy event).</summary>
        public const double GreenMabFactor = 1.06;

        /// <summary>MAB factor applied on YELLOW status (hold).</summary>
        public const double YellowMabFactor = 1.0;

        /// <summary>MAB factor applied on RED status (−6%, mirrors the green uplift).</summary>
        public const double RedMabFactor = 0.94;

        /// <summary>Sea-lice load (adult females/fish) at or below which a zone assesses GREEN.</summary>
        public const double GreenLiceThreshold = 0.5;

        /// <summary>Sea-lice load above which a zone assesses RED.</summary>
        public const double RedLiceThreshold = 1.0;

        /// <summary>
        /// When true, traffic-light regulatory events route MAB changes through
        /// this state machine. Defaults to false so default game balance and
        /// event outcomes are identical to the legacy event-only behavior.
        /// </summary>
        public bool UseStateMachineForEvents { get; set; } = false;

        private readonly Dictionary<string, ZoneColor> _zoneColors = new();

        /// <summary>Persistence: the per-zone color cache implements the
        /// "MAB consequence applies ONCE per color change" rule — it must
        /// survive a save or a loaded game re-applies the consequence
        /// (Phase 1 wave 6).</summary>
        internal Dictionary<string, string> ExportColors()
            => _zoneColors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

        internal void ImportColors(Dictionary<string, string> colors)
        {
            _zoneColors.Clear();
            foreach (var kvp in colors)
                if (Enum.TryParse<ZoneColor>(kvp.Value, out var parsed))
                    _zoneColors[kvp.Key] = parsed;
        }

        /// <summary>Raised whenever a zone's traffic-light color changes.</summary>
        public event Action<string, ZoneColor, ZoneColor>? OnZoneStatusChanged;

        /// <summary>Current color for a zone; unknown zones default to YELLOW (hold).</summary>
        public ZoneColor GetZoneColor(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return ZoneColor.Yellow;
            return _zoneColors.TryGetValue(zoneId, out var color) ? color : ZoneColor.Yellow;
        }

        /// <summary>MAB factor for a traffic-light color.</summary>
        public static double MabFactorFor(ZoneColor color) => color switch
        {
            ZoneColor.Green => GreenMabFactor,
            ZoneColor.Red => RedMabFactor,
            _ => YellowMabFactor,
        };

        /// <summary>
        /// Transition a zone to a new color. No-op (same color) does not raise
        /// the change event. Returns the previous color.
        /// </summary>
        public ZoneColor SetZoneColor(string zoneId, ZoneColor color)
        {
            var previous = GetZoneColor(zoneId);
            _zoneColors[zoneId] = color;
            if (previous != color)
                OnZoneStatusChanged?.Invoke(zoneId, previous, color);
            return previous;
        }

        /// <summary>
        /// Assess a zone from its sea-lice load and transition accordingly:
        /// ≤ <see cref="GreenLiceThreshold"/> → GREEN, ≥
        /// <see cref="RedLiceThreshold"/> → RED, otherwise YELLOW.
        /// Returns the assessed color.
        /// </summary>
        public ZoneColor AssessZone(string zoneId, double licePerFish)
        {
            ZoneColor assessed = licePerFish <= GreenLiceThreshold
                ? ZoneColor.Green
                : licePerFish >= RedLiceThreshold ? ZoneColor.Red : ZoneColor.Yellow;
            SetZoneColor(zoneId, assessed);
            return assessed;
        }

        /// <summary>
        /// Apply the current color's MAB consequence to a zone's carrying
        /// capacity. Returns the factor applied.
        /// </summary>
        public double ApplyMABConsequence(Zone zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            double factor = MabFactorFor(GetZoneColor(zone.Id));
            zone.CarryingCapacityTonnes *= factor;
            return factor;
        }

        /// <summary>
        /// Assess a zone from lice load and immediately apply the MAB
        /// consequence. Returns the assessed color.
        /// </summary>
        public ZoneColor ApplyAssessmentToZone(Zone zone, double licePerFish)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            var color = AssessZone(zone.Id, licePerFish);
            ApplyMABConsequence(zone);
            return color;
        }

        // ─── Event delegation (gate-aware) ──────────────────────────────
        //
        // Called by NorwegianEvents traffic-light-green/red handlers.
        // Gate off (default): legacy paths run verbatim so default outcomes
        // are identical. Gate on: the same numeric MAB change flows through
        // the tracked state machine instead.

        /// <summary>
        /// GREEN upgrade across all zones. Gate off → legacy ×1.06 loop,
        /// untouched. Gate on → zones tracked GREEN, same ×1.06 via machine.
        /// </summary>
        public void ApplyGreenStatus(Core.GameState gs)
        {
            if (gs == null) throw new ArgumentNullException(nameof(gs));
            if (UseStateMachineForEvents)
            {
                foreach (var zone in gs.Zones)
                {
                    SetZoneColor(zone.Id, ZoneColor.Green);
                    ApplyMABConsequence(zone);
                }
            }
            else
            {
                // Legacy behavior, kept verbatim (×1.06 on every zone).
                foreach (var zone in gs.Zones)
                {
                    zone.CarryingCapacityTonnes *= 1.06;
                }
            }
        }

        /// <summary>
        /// RED downgrade across all zones. Gate off → legacy behavior (no MAB
        /// change; the event's fine is settled separately by the handler).
        /// Gate on → zones tracked RED with the symmetric ×0.94 consequence.
        /// </summary>
        public void ApplyRedStatus(Core.GameState gs)
        {
            if (gs == null) throw new ArgumentNullException(nameof(gs));
            if (!UseStateMachineForEvents) return; // legacy: MAB untouched
            foreach (var zone in gs.Zones)
            {
                SetZoneColor(zone.Id, ZoneColor.Red);
                ApplyMABConsequence(zone);
            }
        }
    }
}
