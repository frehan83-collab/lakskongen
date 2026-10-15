using System.Globalization;
using LakskongenSim.Core;
using LakskongenSim.Farm;

namespace StateHashProbe;

/// <summary>
/// The player of the golden traces (F2-01): a scenario-agnostic sea farmer
/// that decides from the game state only, so every shipped scenario runs the
/// harvest pipeline, stocking under the capacity and pen checks, delousing and
/// research, not just the passive biology. It never builds a site or a
/// facility: F2-03 turns the facility verbs into the new unit kinds and must
/// leave these traces unchanged. Its rules are part of the trace format
/// (<see cref="Version"/>): changing one is a re-record with a diff report.
/// </summary>
public sealed class GoldenPlayer
{
    /// <summary>Recorded in every trace header; bump it when a rule changes.</summary>
    public const string Version = "golden-player/1";

    /// <summary>A site whose adult female lice per fish exceed this is deloused (the Mattilsynet limit is 0.5).</summary>
    public const double DelouseAboveAdultFemales = 0.4;

    /// <summary>Days between two delousings of one site (a failed or weak one is not repeated daily).</summary>
    public const int DelouseIntervalDays = 14;

    /// <summary>A site at or above this mean live weight gets a planned harvest order (roadmap D2's trigger weight).</summary>
    public const double HarvestAtMeanWeightKg = 4.5;

    /// <summary>Days a site lies empty before it is restocked (a short fallow).</summary>
    public const int FallowDays = 30;

    /// <summary>Restocking is tried on days divisible by this (the capacity check projects a whole cycle).</summary>
    public const int RestockEveryDays = 7;

    /// <summary>Smallest stocking worth a wellboat trip.</summary>
    public const int MinimumStocking = 10_000;

    /// <summary>Stockings are rounded down to whole thousands of smolts.</summary>
    public const int StockingStep = 1_000;

    /// <summary>Cash the player keeps back when it buys smolts or research (NOK).</summary>
    public const decimal CashReserveNok = 1_000_000m;

    /// <summary>The delousing method (works in any water; the tutorial's minimal player uses it too).</summary>
    public const string DelouseMethod = "hydrolicer";

    private readonly Dictionary<string, int> _lastDelouseDay = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastDayWithFish = new(StringComparer.Ordinal);
    private bool _researchStarted;

    /// <summary>
    /// Play the start of <paramref name="day"/>: issue this day's commands and
    /// return them with their outcome ("verb args: ok" or ": error_key").
    /// </summary>
    public List<string> Act(SimulationEngine engine, int day)
    {
        var commands = new List<string>();
        foreach (var site in engine.State.SeaSites.OrderBy(s => s.Id, StringComparer.Ordinal))
            ActOnSite(engine, site, day, commands);
        StartResearchOnce(engine, commands);
        return commands;
    }

    private void ActOnSite(SimulationEngine engine, SeaSite site, int day, List<string> commands)
    {
        int fish = site.Pens.Sum(p => p.TotalCount);
        bool ordered = engine.HarvestOrders.Any(o => o.HoldingId == site.Id);
        if (fish > 0)
        {
            _lastDayWithFish[site.Id] = day;
            if (site.Lice.AdultFemalePerFish > DelouseAboveAdultFemales && DaysSince(_lastDelouseDay, site.Id, day) >= DelouseIntervalDays)
            {
                _lastDelouseDay[site.Id] = day;
                commands.Add(Outcome($"delouse {site.Id} {DelouseMethod}", engine.DelouseSite(site.Id, DelouseMethod)));
            }
            double meanKg = site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish;
            if (!ordered && meanKg >= HarvestAtMeanWeightKg)
                commands.Add(Outcome($"order_harvest {site.Id}", engine.OrderHarvest(site.Id)));
            return;
        }
        if (ordered || day % RestockEveryDays != 0 || DaysSince(_lastDayWithFish, site.Id, day) < FallowDays)
            return;
        int smolts = SmoltsToStock(engine, site);
        if (smolts >= MinimumStocking)
            commands.Add(Outcome($"stock {site.Id} {smolts.ToString(CultureInfo.InvariantCulture)}", engine.StockSmolts(site.Id, smolts)));
    }

    /// <summary>
    /// As many smolts as the capacity and pen checks allow and the cash above
    /// the reserve carries through the cycle: each smolt's price plus the feed
    /// that grows it to <see cref="HarvestAtMeanWeightKg"/> (calibrated feed
    /// cost per kilo produced), so the farm can feed until the harvest pays.
    /// </summary>
    private static int SmoltsToStock(SimulationEngine engine, SeaSite site)
    {
        decimal perSmolt = engine.Calibration.Economy.SmoltCostPerFishNOK
            + (decimal)HarvestAtMeanWeightKg * engine.Calibration.FeedCostPerKgProducedNOK;
        decimal spendable = engine.FinanceManager.Balance - CashReserveNok;
        if (perSmolt <= 0m || spendable <= 0m) return 0;
        long affordable = (long)Math.Floor(spendable / perSmolt);
        long allowed = Math.Min(affordable, engine.MaxStockableSmolts(site.Id));
        return (int)(allowed / StockingStep * StockingStep);
    }

    /// <summary>After the first harvest has paid, start the cheapest research the company can afford, once.</summary>
    private void StartResearchOnce(SimulationEngine engine, List<string> commands)
    {
        if (_researchStarted || engine.State.HarvestBatchesSold == 0) return;
        var node = engine.Research.Nodes.Values
            .Where(n => engine.Research.CanResearch(n.Id))
            .OrderBy(n => n.CostNOK).ThenBy(n => n.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (node == null || engine.FinanceManager.Balance < node.CostNOK + CashReserveNok) return;
        _researchStarted = true;
        commands.Add(Outcome($"research {node.Id}", engine.StartResearchProject(node.Id)));
    }

    /// <summary>Days since the day recorded for <paramref name="siteId"/> (a site never seen counts as long ago).</summary>
    private static int DaysSince(Dictionary<string, int> days, string siteId, int today)
        => days.TryGetValue(siteId, out int seen) ? today - seen : int.MaxValue;

    private static string Outcome(string command, CommandResult result)
        => result.Ok ? command + ": ok" : command + ": " + result.ErrorKey;
}
