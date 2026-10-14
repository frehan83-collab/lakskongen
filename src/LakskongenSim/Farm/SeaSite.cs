using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LakskongenSim.Biology;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Represents a sea site where fish are kept in pens.
    /// </summary>
    public class SeaSite
    {
        public string Id { get; init; }
        public string ZoneId { get; set; } = ""; // Links to Environment.Zone

        /// <summary>The name the player reads (the scenario's site name, or a
        /// generated place name for a site the player builds). Empty on saves
        /// from before it existed; <see cref="Label"/> falls back then.</summary>
        public string DisplayName { get; set; } = "";

        /// <summary><see cref="DisplayName"/>, or a readable form of the id: the
        /// UI never shows a raw id such as "po3_nord".</summary>
        [JsonIgnore] public string Label =>
            string.IsNullOrWhiteSpace(DisplayName) ? SiteNames.Humanize(Id) : DisplayName;
        public List<Biology.FishPopulation> Pens { get; set; } = new();

        // License & MAB (Maximum Allowed Biomass)
        public double MABTonnes { get; set; } = 780.0; // Standard license: 780 tonnes
        /// <summary>Live standing biomass of every pen (tonnes). Computed, never cached:
        /// a stocking-time cache made MAB enforcement dead on the tick path (F1129).</summary>
        [JsonIgnore] public double CurrentBiomassTonnes => Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
        [JsonIgnore] public bool IsOverMAB => CurrentBiomassTonnes > MABTonnes;

        // Sustainability integration properties
        /// <summary>Fish escaped since the game started. Despite the name it is
        /// never reset (the balance probe reads it as the cumulative count);
        /// the escape score and the UI use <see cref="EscapeIncidents"/>.</summary>
        public int EscapesThisYear { get; set; } = 0;
        /// <summary>This site's escape incidents, kept for the rolling year
        /// the escape score counts (F457).</summary>
        public List<EscapeIncident> EscapeIncidents { get; set; } = new();
        public bool IsClosedContainment { get; set; } = false;

        /// <summary>Containment tech level 0-5 (Phase 4.1). Old saves default 0;
        /// skirt-owners migrate to 1, closed-flagged to 3. Downgrade not allowed.</summary>
        public ContainmentLevel Containment { get; set; } = ContainmentLevel.OpenNet;

        /// <summary>Red-zone MAB credit applied (Phase 4.1). Reverted exactly on
        /// zone exit so re-entries never compound.</summary>
        public bool RedZoneCreditApplied { get; set; } = false;

        /// <summary>ROC watch mode (Phase 4.2): "watch" (25 pts), "scan" (10),
        /// "auto" (0 + 8% feed spill + slower escape response).</summary>
        public string WatchMode { get; set; } = "watch";

        /// <summary>Gill health 0-100 (Phase 4.3). Blooms damage, recovery heals.
        /// Throttles appetite, inflates FCR, worsens treatment mortality.</summary>
        public double GillScore { get; set; } = 100.0;

        /// <summary>Bubble curtain installed (Phase 4.3): jelly ×0.23.</summary>
        public bool HasBubbleCurtain { get; set; } = false;

        /// <summary>O2 diffuser installed (Phase 4.3): hypoxia ×0.3, algae ×0.7.</summary>
        public bool HasO2Diffuser { get; set; } = false;

        /// <summary>P8 permeable barrier installed at this site (None = none).
        /// Retrofit liner (e.g. Smoltscreen) that blocks lice/jellyfish by flow.</summary>
        public BarrierKind Barrier { get; set; } = BarrierKind.None;

        /// <summary>Sim day of the latest stocking at this site, -1 when unknown
        /// (saves from before it existed). The UI holds back the FCR alarm for
        /// freshly stocked fish (roadmap U1-08).</summary>
        public int LastStockedDay { get; set; } = -1;

        /// <summary>P8 days since the installed barrier was last cleaned. Drives
        /// biofouling efficiency decay once past the cleaning interval.</summary>
        public int BarrierDaysSinceCleaning { get; set; } = 0;

        /// <summary>Net/cage integrity 0-100. Storms degrade; RepairNets restores.
        /// Below 40 during storms risks escapes. Old saves migrate 0 → 100 on load.</summary>
        public double NetIntegrityPct { get; set; } = 100.0;

        /// <summary>Lice skirt installed (Phase 1.5): -settlement, daily OPEX.</summary>
        public bool HasLiceSkirt { get; set; } = false;

        /// <summary>Seal deterrent installed (Phase 1.5): predator mitigation, daily OPEX.</summary>
        public bool HasSealDeterrent { get; set; } = false;

        // Phase 5.3: Sludge valorization
        /// <summary>Accumulated sludge dry solids (kg) awaiting valorization.</summary>
        public double SludgeAccumulatedKgDs { get; set; } = 0.0;

        /// <summary>Total sludge valorized this production cycle (kg DS).</summary>
        public double SludgeValorizedKgDs { get; set; } = 0.0;

        /// <summary>Day of last sludge valorization.</summary>
        public int LastSludgeValorizationDay { get; set; } = -1;

        /// <summary>Whether this site is connected to a centralized sludge hub.</summary>
        public bool HasSludgeHubConnection { get; set; } = false;

        // Phase 5.4: Carbon ledger
        /// <summary>Carbon emissions/credits tracker for this site.</summary>
        public CarbonTracker CarbonTracker { get; set; } = new();

        /// <summary>Live stocking density (kg/m³) from the pens' current biomass.</summary>
        [JsonIgnore] public float CurrentStockingDensity => PenVolume > 0f
            ? (float)(Pens.Sum(p => p.GetTotalBiomassKg()) / PenVolume)
            : 0f;
        public float MaxAllowedDensity { get; set; } = 25.0f; // kg/m³ (Norwegian standard)
        public float PenVolume { get; set; } = 50000f; // m³ (default pen volume)
        public int NumberOfPens { get; set; } = 8; // Typical Norwegian site: 6-10 pens

        // ── Per-site sea lice (Phase 2.1) ─────────────────────────────────
        /// <summary>Live lice model for THIS site (stage-structured).
        /// Serialized since Phase 1 wave 6: the model carries accumulated
        /// state (stage structure, resistance, treatment effects) that the
        /// weekly sync can only approximate — "re-seed on load" broke exact
        /// save/load continuation. Old saves without lice data still fall
        /// back to the self-start path.</summary>
        public Health.SeaLiceModel Lice { get; set; } = new Health.SeaLiceModel();

        /// <summary>Adult females per fish at the last weekly count.</summary>
        public double LastLiceCount { get; set; } = 0.0;
        /// <summary>Sim day of the last weekly lice count (-1 = never).</summary>
        public int LastLiceCountDay { get; set; } = -1;
        /// <summary>Consecutive weekly counts above the legal limit.</summary>
        public int ConsecutiveLiceBreaches { get; set; } = 0;
        /// <summary>Mattilsynet treatment deadline (sim day), -1 = no order.</summary>
        public int TreatmentOrderDueDay { get; set; } = -1;

        /// <summary>Mattilsynet over-MAB deadline (sim day), -1 = no order.</summary>
        public int MattilsynetDeadlineDay { get; set; } = -1;

        /// <summary>Whether a Mattilsynet over-MAB order is active on this site.</summary>
        public bool HasActiveMattilsynetOrder => MattilsynetDeadlineDay > 0;

        /// <summary>Whether a forced harvest has been executed for the current order.</summary>
        public bool MattilsynetOrderFulfilled { get; set; } = false;

        public event Action<Biology.FishPopulation>? OnStocked;
        public event Action<int>? OnEscapeEvent;
        public event Action? OnMABExceeded;

        /// <summary>Parameterless constructor for JSON deserialization.</summary>
        public SeaSite() { Id = ""; }

        public SeaSite(string id, string zoneId = "")
        {
            Id = id;
            ZoneId = zoneId;
        }

        public void Stock(Biology.FishPopulation pop)
        {
            pop.ZoneId = ZoneId; // Link population to this site's zone
            pop.SiteId = Id;     // Direct site reference for O(1) lookup
            Pens.Add(pop);
            OnStocked?.Invoke(pop);

            // Check MAB limit
            if (IsOverMAB)
            {
                OnMABExceeded?.Invoke();
            }
        }

        /// <summary>Advance all pens by days.</summary>
        public void AdvanceDays(int days)
        {
            foreach (var p in Pens) p.AdvanceDays(days);
        }

        /// <summary>Days an escape incident counts towards the escape score.</summary>
        public const int EscapeWindowDays = 365;

        /// <summary>Record an escape on <paramref name="day"/>: one incident for
        /// the escape score's rolling year, plus its fish. The engine passes
        /// the current day; the default keeps the old one-argument callers.</summary>
        public void RecordEscape(int fishCount, int day = 0)
        {
            EscapesThisYear += fishCount;
            EscapeIncidents.RemoveAll(e => e.Day <= day - EscapeWindowDays);
            EscapeIncidents.Add(new EscapeIncident { Day = day, Fish = fishCount });
            OnEscapeEvent?.Invoke(fishCount);
        }

        /// <summary>Escape incidents in the 365 days up to <paramref name="today"/>.</summary>
        public int EscapeIncidentsInLastYear(int today)
            => EscapeIncidents.Count(e => e.Day > today - EscapeWindowDays);

        /// <summary>Fish escaped in the 365 days up to <paramref name="today"/>.</summary>
        public int EscapedFishInLastYear(int today)
            => EscapeIncidents.Where(e => e.Day > today - EscapeWindowDays).Sum(e => e.Fish);

    }

    /// <summary>One escape from a site: the day and the number of fish (F457).</summary>
    public sealed class EscapeIncident
    {
        public int Day { get; set; }
        public int Fish { get; set; }
    }
}
