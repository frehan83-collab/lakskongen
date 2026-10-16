using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.FarmTypes.cs — partial class slice (roadmap F2-04; sim·core's).
// <summary>
// The farm-type catalogue (docs/FARM_TYPES_SPEC.md §1, §2, §6.1-§6.2): three
// families (Sjø, Land, Hav), seven variants, the same axes on every card with
// the reference values' source keys, and every unlock gate with its status in
// this game. A thin projection of SimulationEngine.GetFarmTypeCatalog (pure
// read); the UI shows these numbers and holds no farm-type number of its own
// (tests/test_farm_type_catalog.py greps godot/scripts/ui for them).
// </summary>
/// <summary>
/// GameBridge partial — F2-04 farm-type catalogue (sim·core's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The farm-type catalogue. Keys: "axes" (Array[String], the card's axis ids
    /// in order) and "families" (Array of Dictionary: "id", "name_key",
    /// "variants"). A variant: "id", "family", "name_key", "stage" (1 ships
    /// first, 3 later), "capacity_basis" ("hog" or "live_post_smolt"),
    /// "unlocked" (every gate but money met), "affordable" (the money gate met),
    /// "axes" (Dictionary by axis id: "median" and "bad" (float or null),
    /// "unit", "sources" (Array[String] of source keys: dossier keys such as
    /// "L:R1", "O:R4", "V:2", "code" or "GB"), "derived" (bool), "note" (a reason
    /// key when the value is null)) and "gates" (Array of Dictionary: "kind"
    /// (the FarmTypeGateKind name), "subject" (a research node or variant id),
    /// "required", "current", "met").
    /// </summary>
    public Godot.Collections.Dictionary GetFarmTypeCatalog()
    {
        var result = new Godot.Collections.Dictionary();
        var axes = new Godot.Collections.Array<string>();
        foreach (var axis in Farm.FarmTypeIds.Axes)
            axes.Add(axis);
        result["axes"] = axes;
        var families = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        result["families"] = families;
        if (SimulationEngine?.State == null) return result;

        foreach (var family in SimulationEngine.GetFarmTypeCatalog())
        {
            var variants = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var status in family.Variants)
                variants.Add(FarmTypeVariantDictionary(status));
            families.Add(new Godot.Collections.Dictionary
            {
                ["id"] = family.Family.Id,
                ["name_key"] = family.Family.NameKey,
                ["variants"] = variants,
            });
        }
        return result;
    }

    private static Godot.Collections.Dictionary FarmTypeVariantDictionary(FarmTypeVariantStatus status)
    {
        var variant = status.Variant;
        var axes = new Godot.Collections.Dictionary();
        foreach (var axis in variant.Axes)
        {
            var sources = new Godot.Collections.Array<string>();
            foreach (var key in axis.Sources)
                sources.Add(key);
            axes[axis.Axis] = new Godot.Collections.Dictionary
            {
                ["median"] = axis.Median.HasValue ? Variant.From(axis.Median.Value) : default,
                ["bad"] = axis.Bad.HasValue ? Variant.From(axis.Bad.Value) : default,
                ["unit"] = axis.Unit,
                ["sources"] = sources,
                ["derived"] = axis.Derived,
                ["note"] = axis.NoteKey,
            };
        }
        var gates = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var gate in status.Gates)
        {
            gates.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = gate.Gate.Kind.ToString(),
                ["subject"] = gate.Gate.Subject,
                ["required"] = gate.Gate.Required,
                ["current"] = gate.Current,
                ["met"] = gate.Met,
            });
        }
        return new Godot.Collections.Dictionary
        {
            ["id"] = variant.Id,
            ["family"] = variant.FamilyId,
            ["name_key"] = variant.NameKey,
            ["stage"] = variant.Stage,
            ["capacity_basis"] = variant.CapacityBasis,
            ["unlocked"] = status.Unlocked,
            ["affordable"] = status.Affordable,
            ["axes"] = axes,
            ["gates"] = gates,
        };
    }
}
