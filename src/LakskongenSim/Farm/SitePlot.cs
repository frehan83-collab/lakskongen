using System;
using System.Text.Json.Serialization;

namespace LakskongenSim.Farm
{
    /// <summary>What a site plot is for (docs/FARM_TYPES_SPEC.md §3.2, §4).</summary>
    public enum PlotKind
    {
        /// <summary>A flat shore pad for a land site: grow-out lots and post-smolt modules.</summary>
        LandPad = 0,
        /// <summary>An open-sea slot for an offshore unit.</summary>
        OffshoreSlot = 1,
    }

    /// <summary>
    /// How exposed an offshore slot is: "Skjermet", "Ytre kyst", "Havkanten",
    /// "Havområde" (spec §4).
    /// </summary>
    public enum ExposureClass
    {
        /// <summary>Inside the skerries ("Skjermet").</summary>
        Sheltered = 0,
        /// <summary>The outer coast ("Ytre kyst").</summary>
        OuterCoast = 1,
        /// <summary>The exposed outer coast ("Havkanten").</summary>
        OuterCoastExposed = 2,
        /// <summary>The open ocean outside the production-area system ("Havområde").</summary>
        OpenOcean = 3,
    }

    /// <summary>
    /// The gameplay attributes of one plot, as <c>godot/data/site_plots.json</c>
    /// gives them (roadmap F2-03; spec §3.2 and §4): where it lies, what it can
    /// take and what the sea or the ground there is like. Immutable: a game's
    /// plots carry their state beside it (<see cref="SitePlot"/>), so a save
    /// never stores, and a catalogue update never fights, an attribute.
    /// </summary>
    public sealed record SitePlotTemplate
    {
        /// <summary>Plot id: "pad_a", "off_1", "overflow_land_1"…</summary>
        public string Id { get; init; } = "";
        /// <summary>The place name the player reads ("Storholmen").</summary>
        public string Name { get; init; } = "";
        /// <summary>Land pad or offshore slot.</summary>
        public PlotKind Kind { get; init; }
        /// <summary>
        /// "region_po": the plot lies in the scenario's production area;
        /// "hav": outside the production-area system (spec §4, hav_1).
        /// </summary>
        public string ZoneRole { get; init; } = SitePlot.RegionPoRole;
        /// <summary>True for a reserved overflow plot: used only by a migrated save that holds more units than the named plots (spec §3.8 step 4).</summary>
        public bool IsOverflow { get; init; }
        /// <summary>Centre, x (world metres, shared with the terrain).</summary>
        public double WorldX { get; init; }
        /// <summary>Centre, z (world metres, shared with the terrain).</summary>
        public double WorldZ { get; init; }
        /// <summary>Logistics distance (km); the 3D world compresses an offshore slot's.</summary>
        public double RealDistanceKm { get; init; }

        // ── Land pads ──────────────────────────────────────────────────────
        /// <summary>Pad width along x (m).</summary>
        public double PadWidthM { get; init; }
        /// <summary>Pad depth along z (m).</summary>
        public double PadDepthM { get; init; }
        /// <summary>Grow-out lots the pad holds (HFS lots; 0 on a post-smolt pad).</summary>
        public int PhaseLots { get; init; }
        /// <summary>Post-smolt modules the pad holds (2-tank modules, spec §2A).</summary>
        public int PostSmoltModules { get; init; }
        /// <summary>Pumping lift (m): pad level + tank water + freeboard (spec §3.2).</summary>
        public double StaticLiftM { get; init; } = 13.0;
        /// <summary>
        /// Length of the deep-water intake (m): computed from the terrain
        /// (<see cref="Environment.ISitePlotGeometry.IntakeReachM"/>), never
        /// authored; 0 until a geometry gives it.
        /// </summary>
        public double DeepIntakeReachM { get; init; }
        /// <summary>Pump capacity per HFS lot (m³/s).</summary>
        public double PumpCapacityM3sPerLot { get; init; } = 5.0;
        /// <summary>Grid capacity at the pad (MW).</summary>
        public double GridCapacityMW { get; init; }
        /// <summary>Grid feeds the pad starts with.</summary>
        public int GridFeeds { get; init; } = 1;
        /// <summary>Nature sensitivity 0-1 (raises the refusal risk of the permits, F2-14).</summary>
        public double NatureSensitivity { get; init; }
        /// <summary>True when the plot is zoned for aquaculture at the start (a scenario lever).</summary>
        public bool PreZoned { get; init; }
        /// <summary>True when a river intake lets the pad blend brackish water (pad_delta).</summary>
        public bool HasRiverIntake { get; init; }
        /// <summary>Cost of blasting the pad (NOK).</summary>
        public decimal BlastCostNOK { get; init; }

        // ── Offshore slots ─────────────────────────────────────────────────
        /// <summary>Exposure class of the slot.</summary>
        public ExposureClass Exposure { get; init; }
        /// <summary>Water depth at the slot (m).</summary>
        public double WaterDepthM { get; init; }
        /// <summary>100-year significant wave height (m): a unit may sit here only if its design Hs reaches it.</summary>
        public double Hs100M { get; init; }
        /// <summary>Scale of the slot's sea state on the reference climate (F2-06).</summary>
        public double PlotHsScale { get; init; } = 1.0;
        /// <summary>Lowest multiplier on the external lice pressure at the slot.</summary>
        public double LiceExternalMin { get; init; }
        /// <summary>Highest multiplier on the external lice pressure (equal to the lowest unless the slot is uncertain).</summary>
        public double LiceExternalMax { get; init; }

        /// <summary>True for a land pad.</summary>
        [JsonIgnore] public bool IsLand => Kind == PlotKind.LandPad;

        /// <summary>True for an offshore slot.</summary>
        [JsonIgnore] public bool IsOffshore => Kind == PlotKind.OffshoreSlot;

        /// <summary>A plot the catalogue does not know (a save from a newer catalogue): no attributes.</summary>
        public static SitePlotTemplate Unknown(string id) => new() { Id = id ?? "", Name = id ?? "" };
    }

    /// <summary>
    /// One plot of a game (roadmap F2-03; docs/FARM_TYPES_SPEC.md §3.2): a land
    /// pad or an offshore slot that a unit is built on. The gameplay attributes
    /// come from the catalogue (<see cref="SitePlotCatalog"/>, joined by
    /// <see cref="Id"/>) and are not saved; the game's state of the plot is.
    /// <para>
    /// Zone: every plot is its own water area (<see cref="ZoneId"/> = its id).
    /// A land tank's water is the land site's own; an offshore unit sits in the
    /// open sea outside the fjord's zones. The fjord systems that pool by zone
    /// (the zone's water chemistry and biomass load, the traffic light, the
    /// bloom deck, cross-site lice at 0.4) therefore never see a land site or an
    /// offshore unit; their production area is the scenario's (the home area,
    /// <c>SimulationEngine.ProductionAreaOfZone</c>), which is what the lice
    /// limits and the capacity rules read (F2-03 RUN_LOG decision).
    /// </para>
    /// </summary>
    public sealed class SitePlot
    {
        /// <summary><see cref="SitePlotTemplate.ZoneRole"/> of a plot in the scenario's production area.</summary>
        public const string RegionPoRole = "region_po";

        /// <summary><see cref="SitePlotTemplate.ZoneRole"/> of a plot outside the production-area system.</summary>
        public const string HavRole = "hav";

        /// <summary>Plot id (the key a save keeps): "pad_a", "off_1", "overflow_land_1"…</summary>
        public string Id { get; init; } = "";

        /// <summary>The catalogue's attributes of this plot (not saved; joined by <see cref="Id"/>).</summary>
        [JsonIgnore] public SitePlotTemplate Template { get; init; } = SitePlotTemplate.Unknown("");

        // ── State (saved) ───────────────────────────────────────────────────

        /// <summary>The plot's water area: its own id (see the class remarks).</summary>
        public string ZoneId { get; set; } = "";

        /// <summary>Grid feeds at the pad: the catalogue's, plus a second feed a grid project adds (F2-14).</summary>
        public int GridFeeds { get; set; } = 1;

        /// <summary>True once the plot is zoned for aquaculture (the catalogue's start, or a zoning case, F2-14).</summary>
        public bool PreZoned { get; set; }

        /// <summary>The unit on the plot: "" (free), a unit id, or "rival:&lt;id&gt;".</summary>
        public string OwnerUnitId { get; set; } = "";

        /// <summary>
        /// The external lice multiplier drawn for an uncertain slot (hav_1) and
        /// shown once a unit operates there; NaN until drawn (F2-08 draws it).
        /// </summary>
        public double LiceExternalRevealed { get; set; } = double.NaN;

        /// <summary>Sim day the site approval was withdrawn (the aquaculture plot is lost), -1 when it stands (F2-08).</summary>
        public int WithdrawnFromDay { get; set; } = -1;

        // ── Attributes (the catalogue's) ────────────────────────────────────

        /// <summary>The place name the player reads.</summary>
        [JsonIgnore] public string Name => Template.Name;
        /// <summary>Land pad or offshore slot.</summary>
        [JsonIgnore] public PlotKind Kind => Template.Kind;
        /// <summary>"region_po" or "hav".</summary>
        [JsonIgnore] public string ZoneRole => Template.ZoneRole;
        /// <summary>True for a reserved overflow plot.</summary>
        [JsonIgnore] public bool IsOverflow => Template.IsOverflow;
        /// <summary>Centre, x (world metres).</summary>
        [JsonIgnore] public double WorldX => Template.WorldX;
        /// <summary>Centre, z (world metres).</summary>
        [JsonIgnore] public double WorldZ => Template.WorldZ;
        /// <summary>Logistics distance (km).</summary>
        [JsonIgnore] public double RealDistanceKm => Template.RealDistanceKm;
        /// <summary>Pad width along x (m).</summary>
        [JsonIgnore] public double PadWidthM => Template.PadWidthM;
        /// <summary>Pad depth along z (m).</summary>
        [JsonIgnore] public double PadDepthM => Template.PadDepthM;
        /// <summary>Grow-out lots the pad holds.</summary>
        [JsonIgnore] public int PhaseLots => Template.PhaseLots;
        /// <summary>Post-smolt modules the pad holds.</summary>
        [JsonIgnore] public int PostSmoltModules => Template.PostSmoltModules;
        /// <summary>Pumping lift (m).</summary>
        [JsonIgnore] public double StaticLiftM => Template.StaticLiftM;
        /// <summary>Deep-water intake length (m), computed from the terrain; 0 when unknown.</summary>
        [JsonIgnore] public double DeepIntakeReachM => Template.DeepIntakeReachM;
        /// <summary>Pump capacity per lot (m³/s).</summary>
        [JsonIgnore] public double PumpCapacityM3sPerLot => Template.PumpCapacityM3sPerLot;
        /// <summary>Grid capacity (MW).</summary>
        [JsonIgnore] public double GridCapacityMW => Template.GridCapacityMW;
        /// <summary>Nature sensitivity 0-1.</summary>
        [JsonIgnore] public double NatureSensitivity => Template.NatureSensitivity;
        /// <summary>True when the pad has a river intake.</summary>
        [JsonIgnore] public bool HasRiverIntake => Template.HasRiverIntake;
        /// <summary>Blasting cost (NOK).</summary>
        [JsonIgnore] public decimal BlastCostNOK => Template.BlastCostNOK;
        /// <summary>Exposure class.</summary>
        [JsonIgnore] public ExposureClass Exposure => Template.Exposure;
        /// <summary>Water depth (m).</summary>
        [JsonIgnore] public double WaterDepthM => Template.WaterDepthM;
        /// <summary>100-year Hs (m).</summary>
        [JsonIgnore] public double Hs100M => Template.Hs100M;
        /// <summary>Sea-state scale on the reference climate.</summary>
        [JsonIgnore] public double PlotHsScale => Template.PlotHsScale;
        /// <summary>Lowest external lice multiplier.</summary>
        [JsonIgnore] public double LiceExternalMin => Template.LiceExternalMin;
        /// <summary>Highest external lice multiplier.</summary>
        [JsonIgnore] public double LiceExternalMax => Template.LiceExternalMax;

        // ── Derived ────────────────────────────────────────────────────────

        /// <summary>True when no unit holds the plot and its approval stands.</summary>
        [JsonIgnore] public bool IsFree => string.IsNullOrEmpty(OwnerUnitId) && WithdrawnFromDay < 0;

        /// <summary>
        /// The multiplier on the external lice pressure at the plot: the
        /// revealed draw, else the band's middle (a certain plot's band is one value).
        /// </summary>
        [JsonIgnore] public double LiceExternalMultiplier => !double.IsNaN(LiceExternalRevealed)
            ? LiceExternalRevealed
            : 0.5 * (LiceExternalMin + LiceExternalMax);

        /// <summary>
        /// True when a unit rated for <paramref name="designHsM"/> (plus any
        /// reinforcement) may sit on this slot: its design Hs reaches the slot's
        /// 100-year Hs (spec §4 "Design rating"). Always false on a land pad.
        /// </summary>
        public bool AcceptsDesignHs(double designHsM) => Kind == PlotKind.OffshoreSlot && designHsM >= Hs100M;

        /// <summary>The plot as a new game holds it: the catalogue's attributes and starting state, unowned.</summary>
        public static SitePlot NewGame(SitePlotTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            return new SitePlot
            {
                Id = template.Id,
                Template = template,
                ZoneId = template.Id,
                GridFeeds = template.GridFeeds,
                PreZoned = template.PreZoned,
            };
        }

        /// <summary>
        /// This saved state on <paramref name="template"/>'s attributes (a load:
        /// the save keeps the state, the catalogue gives the attributes).
        /// </summary>
        public SitePlot WithTemplate(SitePlotTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            return new SitePlot
            {
                Id = Id,
                Template = template,
                ZoneId = string.IsNullOrEmpty(ZoneId) ? Id : ZoneId,
                GridFeeds = GridFeeds,
                PreZoned = PreZoned,
                OwnerUnitId = OwnerUnitId ?? "",
                LiceExternalRevealed = LiceExternalRevealed,
                WithdrawnFromDay = WithdrawnFromDay,
            };
        }
    }
}
