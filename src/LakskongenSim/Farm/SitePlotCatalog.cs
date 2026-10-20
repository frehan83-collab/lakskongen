using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// The farm-type plots of the game (roadmap F2-03; docs/FARM_TYPES_SPEC.md
    /// §3.2 and §4): the land pads and offshore slots a land site or an offshore
    /// unit is built on, with their gameplay attributes from
    /// <c>godot/data/site_plots.json</c> (embedded in the library, so the engine,
    /// the tests and the exported game read the same values with no working
    /// directory assumptions) and the reserved overflow plots a migrated save
    /// uses when it holds more units than the named plots (§3.8 step 4).
    /// <para>
    /// Geometry seam: the positions in the file are the procedural fallback. A
    /// baked region (G2-01 <c>meta.json</c>) or the terrain itself is an
    /// <see cref="ISitePlotGeometry"/>; <see cref="WithGeometry"/> takes the
    /// positions, pad sizes, water depths and the computed deep-water intake
    /// from it, joined by plot id. Pure and immutable: no engine state, no RNG.
    /// </para>
    /// </summary>
    public sealed class SitePlotCatalog
    {
        /// <summary>File name in the game's data directory.</summary>
        public const string FileName = "site_plots.json";

        /// <summary>Logical name of the embedded copy (LakskongenSim.csproj).</summary>
        public const string EmbeddedResourceName = "LakskongenSim.Farm.site_plots.json";

        private static readonly Lazy<SitePlotCatalog> LazyDefault = new(LoadEmbedded);

        private readonly Dictionary<string, SitePlotTemplate> _byId;
        private readonly OverflowRow _overflowLand;
        private readonly OverflowRow _overflowOffshore;

        /// <summary>Catalogue data version (the file's "version").</summary>
        public string Version { get; }

        /// <summary>The named plots in catalogue order: land pads, then offshore slots (no overflow plot).</summary>
        public IReadOnlyList<SitePlotTemplate> Plots { get; }

        /// <summary>Overflow plots reserved in the file for each kind (more are generated past them).</summary>
        public int ReservedOverflow(PlotKind kind) => RowOf(kind).Positions.Count;

        private SitePlotCatalog(string version, IReadOnlyList<SitePlotTemplate> plots, OverflowRow overflowLand,
            OverflowRow overflowOffshore)
        {
            Version = version;
            Plots = plots;
            _overflowLand = overflowLand;
            _overflowOffshore = overflowOffshore;
            _byId = new Dictionary<string, SitePlotTemplate>(StringComparer.Ordinal);
            foreach (var plot in plots)
                _byId.Add(plot.Id, plot);
        }

        /// <summary>The game's catalogue: the embedded <c>site_plots.json</c>.</summary>
        public static SitePlotCatalog Default => LazyDefault.Value;

        /// <summary>The named land pads, in catalogue order.</summary>
        public IEnumerable<SitePlotTemplate> LandPads => Plots.Where(p => p.Kind == PlotKind.LandPad);

        /// <summary>The named offshore slots, in catalogue order.</summary>
        public IEnumerable<SitePlotTemplate> OffshoreSlots => Plots.Where(p => p.Kind == PlotKind.OffshoreSlot);

        /// <summary>
        /// The plot with <paramref name="id"/>: a named plot, or an overflow
        /// plot by its id ("overflow_land_3"), reserved or past the reserved ones.
        /// Null when the id is neither.
        /// </summary>
        public SitePlotTemplate? Find(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId.TryGetValue(id, out var plot)) return plot;
            foreach (var row in new[] { _overflowLand, _overflowOffshore })
            {
                if (!id.StartsWith(row.IdPrefix, StringComparison.Ordinal)) continue;
                if (int.TryParse(id.AsSpan(row.IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    && index >= 1 && id == row.IdPrefix + index.ToString(CultureInfo.InvariantCulture))
                    return Overflow(row.Kind, index);
            }
            return null;
        }

        /// <summary>
        /// Overflow plot <paramref name="index"/> (1-based) of <paramref name="kind"/>:
        /// the neutral attributes of the file's overflow row, at its reserved
        /// position, or past the reserved positions at the last one plus a step
        /// per extra plot (so no migrated unit is ever left without a plot).
        /// </summary>
        public SitePlotTemplate Overflow(PlotKind kind, int index)
        {
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index), index, "Overflow plots are numbered from 1");
            var row = RowOf(kind);
            (double x, double z) position;
            if (index <= row.Positions.Count)
                position = row.Positions[index - 1];
            else
            {
                var last = row.Positions[^1];
                int extra = index - row.Positions.Count;
                position = (last.X + extra * row.ExtraStep.X, last.Z + extra * row.ExtraStep.Z);
            }
            string number = index.ToString(CultureInfo.InvariantCulture);
            return row.Template with
            {
                Id = row.IdPrefix + number,
                Name = $"{row.Template.Name} {number}",
                WorldX = position.x,
                WorldZ = position.z,
                IsOverflow = true,
                // A land pad's logistics distance is its 3D distance from the
                // fjord centre (land is not compressed), as for the named pads.
                RealDistanceKm = kind == PlotKind.LandPad
                    ? Math.Round(Math.Sqrt(position.x * position.x + position.z * position.z) / 1000.0, 1)
                    : row.Template.RealDistanceKm,
            };
        }

        private OverflowRow RowOf(PlotKind kind) => kind == PlotKind.LandPad ? _overflowLand : _overflowOffshore;

        /// <summary>
        /// The catalogue with <paramref name="geometry"/>'s shapes: each land pad
        /// the geometry knows takes its centre, size and computed deep-water
        /// intake, each offshore slot its position and water depth (joined by
        /// id). Plots the geometry does not know keep the file's fallback.
        /// </summary>
        public SitePlotCatalog WithGeometry(ISitePlotGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            var pads = geometry.LandPadSlots().ToDictionary(p => p.Id, StringComparer.Ordinal);
            var slots = geometry.OffshoreSlots().ToDictionary(s => s.Id, StringComparer.Ordinal);
            var joined = new List<SitePlotTemplate>(Plots.Count);
            foreach (var plot in Plots)
            {
                if (plot.Kind == PlotKind.LandPad && pads.TryGetValue(plot.Id, out var pad))
                    joined.Add(plot with
                    {
                        WorldX = pad.CentreX,
                        WorldZ = pad.CentreZ,
                        PadWidthM = pad.WidthM,
                        PadDepthM = pad.DepthM,
                        DeepIntakeReachM = geometry.IntakeReachM(pad),
                    });
                else if (plot.Kind == PlotKind.OffshoreSlot && slots.TryGetValue(plot.Id, out var slot))
                    joined.Add(plot with { WorldX = slot.X, WorldZ = slot.Z, WaterDepthM = slot.WaterDepthM });
                else
                    joined.Add(plot);
            }
            return new SitePlotCatalog(Version, joined, _overflowLand, _overflowOffshore);
        }

        // ── Loading ─────────────────────────────────────────────────────────

        private static SitePlotCatalog LoadEmbedded()
        {
            using var stream = typeof(SitePlotCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' is missing");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>
        /// Parse a <c>site_plots.json</c> text, validating it: unique ids, the
        /// attributes each kind needs, a lice band with min ≤ max, overflow rows
        /// with reserved positions. Throws <see cref="InvalidDataException"/>
        /// naming the plot and the field on any defect.
        /// </summary>
        public static SitePlotCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("site_plots.json is empty");
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string version = RequiredString(root, "version", "catalogue");

            var plots = new List<SitePlotTemplate>();
            foreach (var pad in RequiredArray(root, "land_pads"))
                plots.Add(ParseLandPad(pad, isOverflow: false));
            foreach (var slot in RequiredArray(root, "offshore_slots"))
                plots.Add(ParseOffshoreSlot(slot, isOverflow: false));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var plot in plots)
                if (!seen.Add(plot.Id))
                    throw new InvalidDataException($"site_plots.json: plot id '{plot.Id}' appears twice");

            var overflowLand = ParseOverflow(Required(root, "overflow_land", "catalogue"), PlotKind.LandPad);
            var overflowOffshore = ParseOverflow(Required(root, "overflow_offshore", "catalogue"), PlotKind.OffshoreSlot);
            foreach (var row in new[] { overflowLand, overflowOffshore })
                foreach (var plot in plots)
                    if (plot.Id.StartsWith(row.IdPrefix, StringComparison.Ordinal))
                        throw new InvalidDataException($"site_plots.json: named plot '{plot.Id}' uses the overflow prefix '{row.IdPrefix}'");
            if (overflowLand.IdPrefix == overflowOffshore.IdPrefix)
                throw new InvalidDataException("site_plots.json: the two overflow rows share an id prefix");

            return new SitePlotCatalog(version, plots, overflowLand, overflowOffshore);
        }

        private static SitePlotTemplate ParseLandPad(JsonElement e, bool isOverflow, string? idOverride = null)
        {
            string id = idOverride ?? RequiredString(e, "id", "land pad");
            string where = $"land pad '{id}'";
            var plot = new SitePlotTemplate
            {
                Id = id,
                Name = RequiredString(e, "name", where),
                Kind = PlotKind.LandPad,
                ZoneRole = ZoneRoleOf(e, where),
                IsOverflow = isOverflow,
                WorldX = isOverflow ? 0.0 : RequiredNumber(e, "x", where),
                WorldZ = isOverflow ? 0.0 : RequiredNumber(e, "z", where),
                RealDistanceKm = isOverflow ? 0.0 : RequiredNumber(e, "real_distance_km", where),
                PadWidthM = Positive(RequiredNumber(e, "width_m", where), "width_m", where),
                PadDepthM = Positive(RequiredNumber(e, "depth_m", where), "depth_m", where),
                PhaseLots = NonNegative(RequiredInt(e, "lots", where), "lots", where),
                PostSmoltModules = NonNegative(RequiredInt(e, "post_smolt_modules", where), "post_smolt_modules", where),
                StaticLiftM = Positive(RequiredNumber(e, "static_lift_m", where), "static_lift_m", where),
                PumpCapacityM3sPerLot = Positive(RequiredNumber(e, "pump_capacity_m3s_per_lot", where), "pump_capacity_m3s_per_lot", where),
                GridCapacityMW = Positive(RequiredNumber(e, "grid_capacity_mw", where), "grid_capacity_mw", where),
                GridFeeds = Math.Max(1, RequiredInt(e, "grid_feeds", where)),
                NatureSensitivity = InRange(RequiredNumber(e, "nature_sensitivity", where), 0.0, 1.0, "nature_sensitivity", where),
                PreZoned = RequiredBool(e, "pre_zoned", where),
                HasRiverIntake = RequiredBool(e, "has_river_intake", where),
                BlastCostNOK = (decimal)NonNegative(RequiredNumber(e, "blast_cost_nok", where), "blast_cost_nok", where),
            };
            if (e.TryGetProperty("deep_intake_reach_m", out _))
                throw new InvalidDataException($"site_plots.json: {where} authors deep_intake_reach_m; it is computed from the terrain");
            if (plot.PhaseLots == 0 && plot.PostSmoltModules == 0)
                throw new InvalidDataException($"site_plots.json: {where} holds neither grow-out lots nor post-smolt modules");
            return plot;
        }

        private static SitePlotTemplate ParseOffshoreSlot(JsonElement e, bool isOverflow, string? idOverride = null)
        {
            string id = idOverride ?? RequiredString(e, "id", "offshore slot");
            string where = $"offshore slot '{id}'";
            string exposureText = RequiredString(e, "exposure", where);
            if (!Enum.TryParse<ExposureClass>(exposureText, ignoreCase: false, out var exposure) || !Enum.IsDefined(exposure))
                throw new InvalidDataException($"site_plots.json: {where} has an unknown exposure '{exposureText}'");
            double liceMin = InRange(RequiredNumber(e, "lice_external_min", where), 0.0, 1.0, "lice_external_min", where);
            double liceMax = InRange(RequiredNumber(e, "lice_external_max", where), 0.0, 1.0, "lice_external_max", where);
            if (liceMin > liceMax)
                throw new InvalidDataException($"site_plots.json: {where} has lice_external_min {liceMin} above lice_external_max {liceMax}");
            return new SitePlotTemplate
            {
                Id = id,
                Name = RequiredString(e, "name", where),
                Kind = PlotKind.OffshoreSlot,
                ZoneRole = ZoneRoleOf(e, where),
                IsOverflow = isOverflow,
                WorldX = isOverflow ? 0.0 : RequiredNumber(e, "x", where),
                WorldZ = isOverflow ? 0.0 : RequiredNumber(e, "z", where),
                RealDistanceKm = Positive(RequiredNumber(e, "real_distance_km", where), "real_distance_km", where),
                Exposure = exposure,
                WaterDepthM = Positive(RequiredNumber(e, "water_depth_m", where), "water_depth_m", where),
                Hs100M = Positive(RequiredNumber(e, "hs100_m", where), "hs100_m", where),
                PlotHsScale = Positive(RequiredNumber(e, "plot_hs_scale", where), "plot_hs_scale", where),
                LiceExternalMin = liceMin,
                LiceExternalMax = liceMax,
            };
        }

        private static OverflowRow ParseOverflow(JsonElement e, PlotKind kind)
        {
            string where = kind == PlotKind.LandPad ? "overflow_land" : "overflow_offshore";
            string prefix = RequiredString(e, "id_prefix", where);
            var template = kind == PlotKind.LandPad
                ? ParseLandPad(e, isOverflow: true, idOverride: prefix)
                : ParseOffshoreSlot(e, isOverflow: true, idOverride: prefix);
            if (kind == PlotKind.LandPad)
            {
                // A land unit's distance follows its position (the overflow row has no single one).
                template = template with { RealDistanceKm = 0.0 };
            }
            var positions = new List<(double X, double Z)>();
            foreach (var p in RequiredArray(e, "positions"))
                positions.Add(PairOf(p, where, "positions"));
            if (positions.Count == 0)
                throw new InvalidDataException($"site_plots.json: {where} reserves no positions");
            var step = PairOf(Required(e, "extra_step", where), where, "extra_step");
            if (step.X == 0.0 && step.Z == 0.0)
                throw new InvalidDataException($"site_plots.json: {where} has a zero extra_step (overflow plots past the reserved ones would overlap)");
            return new OverflowRow(kind, prefix, template, positions, step);
        }

        private sealed record OverflowRow(PlotKind Kind, string IdPrefix, SitePlotTemplate Template,
            IReadOnlyList<(double X, double Z)> Positions, (double X, double Z) ExtraStep);

        // ── JSON helpers ────────────────────────────────────────────────────

        private static JsonElement Required(JsonElement e, string name, string where)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var value))
                throw new InvalidDataException($"site_plots.json: {where} is missing '{name}'");
            return value;
        }

        private static JsonElement.ArrayEnumerator RequiredArray(JsonElement e, string name)
        {
            var value = Required(e, name, "catalogue");
            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"site_plots.json: '{name}' is not an array");
            return value.EnumerateArray();
        }

        private static string RequiredString(JsonElement e, string name, string where)
        {
            var value = Required(e, name, where);
            string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"site_plots.json: {where} has an empty or non-text '{name}'");
            return text;
        }

        private static double RequiredNumber(JsonElement e, string name, string where)
        {
            var value = Required(e, name, where);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
                throw new InvalidDataException($"site_plots.json: {where} has a non-numeric '{name}'");
            return number;
        }

        private static int RequiredInt(JsonElement e, string name, string where)
        {
            var value = Required(e, name, where);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
                throw new InvalidDataException($"site_plots.json: {where} has a non-integer '{name}'");
            return number;
        }

        private static bool RequiredBool(JsonElement e, string name, string where)
        {
            var value = Required(e, name, where);
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException($"site_plots.json: {where} has a non-boolean '{name}'"),
            };
        }

        private static string ZoneRoleOf(JsonElement e, string where)
        {
            string role = RequiredString(e, "zone_role", where);
            if (role != SitePlot.RegionPoRole && role != SitePlot.HavRole)
                throw new InvalidDataException($"site_plots.json: {where} has an unknown zone_role '{role}'");
            return role;
        }

        private static (double X, double Z) PairOf(JsonElement e, string where, string name)
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
                throw new InvalidDataException($"site_plots.json: {where} '{name}' needs [x, z] pairs");
            double x = e[0].GetDouble(), z = e[1].GetDouble();
            if (!double.IsFinite(x) || !double.IsFinite(z))
                throw new InvalidDataException($"site_plots.json: {where} '{name}' holds a non-finite coordinate");
            return (x, z);
        }

        private static double Positive(double value, string name, string where)
            => value > 0.0 ? value : throw new InvalidDataException($"site_plots.json: {where} needs a positive '{name}' (got {value})");

        private static double NonNegative(double value, string name, string where)
            => value >= 0.0 ? value : throw new InvalidDataException($"site_plots.json: {where} needs a non-negative '{name}' (got {value})");

        private static int NonNegative(int value, string name, string where)
            => value >= 0 ? value : throw new InvalidDataException($"site_plots.json: {where} needs a non-negative '{name}' (got {value})");

        private static double InRange(double value, double min, double max, string name, string where)
            => value >= min && value <= max
                ? value
                : throw new InvalidDataException($"site_plots.json: {where} needs '{name}' in [{min}, {max}] (got {value})");
    }
}
