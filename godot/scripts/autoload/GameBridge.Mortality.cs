using Godot;
using System.Collections.Generic;
using LakskongenSim.Biology;

// S2-05 — the mortality ledger for the P&L story ("where did the fish go").
// Thin Godot-facing projection: the engine owns the ledger and chooses the
// slice (SimulationEngine.GetMortalityBreakdown); this partial only maps it
// onto Godot dictionaries. Labels are translation keys (nb/en in
// translations.csv), never text.
public partial class GameBridge : Node
{
    /// <summary>
    /// Deaths by cause in the NVI categories, plus the fish that left alive.
    /// Scope: one site since the game started when <paramref name="siteId"/>
    /// is set; else the company over the last <paramref name="lastDays"/> days
    /// when positive; else the company since the game started.
    /// Keys: "success", "scope" ("site" | "days" | "company"), "site_id",
    /// "from_day", "to_day", "total_deaths", "total_kg", "total_value_nok",
    /// "categories" / "causes" (Array of {id, category, nvi_code, label_key,
    /// fish, kg, value_nok, share}; every category and cause, zeros included)
    /// and "left_alive" (escape, harvest, forced_harvest, transfer_out).
    /// </summary>
    public Godot.Collections.Dictionary GetMortalityBreakdown(string siteId = "", int lastDays = 0)
    {
        var result = new Godot.Collections.Dictionary();
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            result["categories"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["causes"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["left_alive"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return result;
        }

        int today = engine.State.Day;
        bool site = !string.IsNullOrEmpty(siteId);
        int days = site ? 0 : System.Math.Min(lastDays, MortalityLedger.HistoryDays);
        var breakdown = engine.GetMortalityBreakdown(site ? siteId : null, days);

        result["success"] = true;
        result["scope"] = site ? "site" : days > 0 ? "days" : "company";
        result["site_id"] = site ? siteId : "";
        result["from_day"] = days > 0 ? today - days + 1 : 0;
        result["to_day"] = today;
        result["total_deaths"] = breakdown.Total.Fish;
        result["total_kg"] = breakdown.Total.Kg;
        result["total_value_nok"] = (double)breakdown.Total.ValueNok;
        result["categories"] = MortalityRows(breakdown.CategoryRows());
        result["causes"] = MortalityRows(breakdown.CauseRows());
        result["left_alive"] = MortalityRows(breakdown.RemovalRows());
        return result;
    }

    private static Godot.Collections.Array<Godot.Collections.Dictionary> MortalityRows(IReadOnlyList<MortalityRow> rows)
    {
        var array = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var row in rows)
            array.Add(new Godot.Collections.Dictionary
            {
                ["id"] = row.Id,
                ["category"] = row.Category,
                ["nvi_code"] = row.NviCode,
                ["label_key"] = row.LabelKey,
                ["fish"] = row.Fish,
                ["kg"] = row.Kg,
                ["value_nok"] = (double)row.ValueNok,
                ["share"] = row.Share,
            });
        return array;
    }
}
