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
        /// <summary>
        /// The fish populations at the site (a legacy name: each population is
        /// the fish of one stocking, held in one of <see cref="PhysicalPens"/>).
        /// </summary>
        public List<Biology.FishPopulation> Pens { get; set; } = new();

        /// <summary>
        /// The site's production units (S2-01b): the pens of its mooring frame,
        /// numbered from 1, each empty or holding one population
        /// (<see cref="Pen.PopulationId"/>). A site built through the id
        /// constructor gets the calibration's standard layout; a scenario site
        /// gets its locality's layout from the scenario data; a save from before
        /// S2-01b gets one pen per population on load
        /// (<c>SimulationEngine.MigratePensAfterLoad</c>).
        /// </summary>
        public List<Pen> PhysicalPens { get; set; } = new();

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

        /// <summary>
        /// Live stocking density (kg/m³) of the site's fish over the legacy
        /// <see cref="PenVolume"/> (one pen's volume for all its fish). The
        /// figure a production unit is held to is <see cref="DensestPenKgPerM3"/>.
        /// </summary>
        [JsonIgnore] public float CurrentStockingDensity => PenVolume > 0f
            ? (float)(Pens.Sum(p => p.GetTotalBiomassKg()) / PenVolume)
            : 0f;

        /// <summary>
        /// Density (kg/m³) of the site's densest occupied pen (S2-01b): the
        /// figure akvakulturdriftsforskriften § 25 holds each production unit
        /// to. A site whose populations sit in no pen (a fixture that adds them
        /// to <see cref="Pens"/> directly) falls back to
        /// <see cref="CurrentStockingDensity"/>.
        /// </summary>
        [JsonIgnore] public double DensestPenKgPerM3
        {
            get
            {
                double densest = -1.0;
                foreach (var pen in PhysicalPens)
                {
                    var population = PopulationIn(pen);
                    if (population == null) continue;
                    densest = Math.Max(densest, pen.DensityKgPerM3(population.GetTotalBiomassKg()));
                }
                return densest >= 0.0 ? densest : CurrentStockingDensity;
            }
        }
        public float MaxAllowedDensity { get; set; } = 25.0f; // kg/m³ (Norwegian standard)
        /// <summary>Legacy pen volume (m³) of the site's old density figure; the real volumes are <see cref="PhysicalPens"/>'.</summary>
        public float PenVolume { get; set; } = 50000f; // m³ (default pen volume)
        /// <summary>
        /// The size the site's crew and upkeep scale with, in pens
        /// (<c>SimulationEngine.SiteSizeScale</c>: a scenario site's stocked pens,
        /// 8 for a built site). Not the pen count: that is
        /// <see cref="PhysicalPens"/>' (S2-01b).
        /// </summary>
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

        /// <summary>
        /// A site with the calibration's standard pen layout
        /// (<see cref="PenLayout.Standard"/>): the engine replaces it with the
        /// locality's layout where it knows one (a scenario site).
        /// </summary>
        public SeaSite(string id, string zoneId = "")
        {
            Id = id;
            ZoneId = zoneId;
            PhysicalPens = PenLayout.Standard(Calibration.CalibrationRegistry.Default.Pens).Build(id);
        }

        // ── Pens (S2-01b) ────────────────────────────────────────────────────

        /// <summary>The pen holding <paramref name="population"/>, or null.</summary>
        public Pen? PenOf(Biology.FishPopulation population)
        {
            if (population == null || string.IsNullOrEmpty(population.Id)) return null;
            foreach (var pen in PhysicalPens)
                if (pen.PopulationId == population.Id) return pen;
            return null;
        }

        /// <summary>The population in <paramref name="pen"/> (one of <see cref="Pens"/>), or null when the pen is empty.</summary>
        public Biology.FishPopulation? PopulationIn(Pen pen)
        {
            if (pen == null || pen.IsEmpty) return null;
            foreach (var population in Pens)
                if (population.Id == pen.PopulationId) return population;
            return null;
        }

        /// <summary>
        /// True when <paramref name="pen"/> can take a stocking: it holds no
        /// population of the site, or only one without a live fish (all
        /// harvested or dead, waiting to be cleared).
        /// </summary>
        public bool IsPenFree(Pen pen)
        {
            if (pen == null) return false;
            var population = PopulationIn(pen);
            return population == null || population.TotalCount <= 0;
        }

        /// <summary>
        /// The pen named by <paramref name="key"/>: its id ("po3_nord/p03") or
        /// its number ("3"). Null when the site has no such pen.
        /// </summary>
        public Pen? FindPen(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var pen in PhysicalPens)
                if (pen.Id == key) return pen;
            if (int.TryParse(key, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int number))
                foreach (var pen in PhysicalPens)
                    if (pen.Number == number) return pen;
            return null;
        }

        /// <summary>
        /// Free the pen of <paramref name="population"/> (its fish are all
        /// harvested or dead): the pen lies empty until the next stocking.
        /// </summary>
        public void ReleasePen(Biology.FishPopulation population)
        {
            var pen = PenOf(population);
            if (pen != null) pen.PopulationId = "";
        }

        /// <summary>
        /// Stock <paramref name="pop"/> at the site, in <paramref name="pen"/>
        /// when given (one of <see cref="PhysicalPens"/>), else in the first free
        /// pen. The engine's stocking verbs choose and check the pen (S2-01b); a
        /// low-level caller with every pen taken (a test fixture) gets one more
        /// pen of the site's first pen's net, so every population at a site
        /// stands in a pen.
        /// </summary>
        public void Stock(Biology.FishPopulation pop, Pen? pen = null)
        {
            pop.ZoneId = ZoneId; // Link population to this site's zone
            pop.SiteId = Id;     // Direct site reference for O(1) lookup
            Pens.Add(pop);
            AssignPen(pop, pen);
            OnStocked?.Invoke(pop);

            // Check MAB limit
            if (IsOverMAB)
            {
                OnMABExceeded?.Invoke();
            }
        }

        private void AssignPen(Biology.FishPopulation pop, Pen? pen)
        {
            if (string.IsNullOrEmpty(pop.Id) || PenOf(pop) != null) return;
            if (pen == null)
            {
                foreach (var candidate in PhysicalPens)
                    if (IsPenFree(candidate)) { pen = candidate; break; }
            }
            if (pen == null)
            {
                var geometry = PhysicalPens.Count > 0
                    ? new PenGeometry(PhysicalPens[0].CircumferenceM, PhysicalPens[0].NetWallDepthM, PhysicalPens[0].ConeDepthM)
                    : PenGeometry.Standard(Calibration.CalibrationRegistry.Default.Pens);
                pen = Pen.Build(Id, PhysicalPens.Count + 1, geometry);
                PhysicalPens.Add(pen);
            }
            pen.PopulationId = pop.Id;
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
