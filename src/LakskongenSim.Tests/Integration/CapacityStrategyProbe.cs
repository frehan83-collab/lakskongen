using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>The capacity strategies the probe plays (S2-01a acceptance 1; roadmap D10's "overstock").</summary>
public enum CapacityStrategy
{
    /// <summary>
    /// Stocks each site to the most the production plan allows
    /// (<see cref="SimulationEngine.MaxStockableSmolts"/>), sells by the probe's
    /// harvest rule (roadmap D2) and harvests the heaviest site early when the
    /// area or a site stands at <see cref="CapacityStrategyProbe.ReliefFraction"/>
    /// of its MTB (MTB relief, as a farmer does).
    /// </summary>
    Compliant,

    /// <summary>
    /// The pre-S2-01 exploit: stocks <see cref="CapacityStrategyProbe.OverstockFactor"/>
    /// times the compliant count (only the smolts' own weight used to be checked,
    /// so three times the fish grew into three times the licence), then sells by
    /// the same rule. It insists: a refused request is not scaled back, and
    /// when only money refuses it, it borrows the smolts' price.
    /// </summary>
    Overstock,

    /// <summary>
    /// Stocks the compliant count, never relieves MTB and grows each site on
    /// for <see cref="CapacityStrategyProbe.GrowOnHoldDays"/> days after it
    /// reaches the harvest trigger: more kilos per fish, standing biomass over
    /// the caps when the fish outgrow the plan.
    /// </summary>
    GrowOn,
}

/// <summary>
/// Scripted capacity careers on <c>po3_red_sone</c> (2 licences, 1,560 t in
/// PO3; two 780 t localities) through the same commands the game uses, events
/// on. Every strategy shares the seed and the scenario; only the capacity
/// behaviour differs. <c>regulated: false</c> replays the same script under
/// the rules before S2-01 (no licence register, per-site check of the smolts'
/// own weight) for the before/after measurement.
/// </summary>
public static class CapacityStrategyProbe
{
    /// <summary>The compliant player harvests the heaviest site when standing biomass reaches this share of a cap.</summary>
    public const double ReliefFraction = 0.95;

    /// <summary>The overstocker asks for this many times the compliant count.</summary>
    public const int OverstockFactor = 3;

    /// <summary>
    /// Days the grow-on player holds a site after it reaches the harvest
    /// trigger (<see cref="PlayerScriptProbe.HarvestTriggerWeightKg"/>), whatever
    /// MTB says (it still sells at the forced weight).
    /// </summary>
    public const int GrowOnHoldDays = 60;

    /// <summary>Latest day a career may run.</summary>
    public const int HorizonDays = 1100;

    /// <summary>The PO3 area of the scenario's sites.</summary>
    private const int Area = 3;

    public sealed class Result
    {
        public CapacityStrategy Strategy;
        public int Seed;
        public bool Regulated;
        public decimal EndingEquityNok;
        public double HarvestedKg;
        public decimal HarvestRevenueNok;
        public int StockedSmolts;
        public int RefusedRequests;
        public string RefusedStockingKey = "";
        public bool RefusedStockingMovedCash;
        public int Breaches;
        public decimal FeesNok;
        public decimal ConfiscatedNok;
        public double ForcedHarvestKg;
        public decimal ForcedHarvestRevenueNok;
        public double PeakIllegalKg;
        public double PeakStandingTonnes;
        public double AreaMtbTonnes;
        public int DaysRun;
    }

    public static Result Run(int seed, CapacityStrategy strategy, bool regulated = true)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        var scenario = engine.Scenario.GetScenario(PlayerScriptProbe.CareerScenarioId);
        Assert.NotNull(scenario);
        scenario!.ApplyStartingState(engine.State, engine);
        Assert.True(engine.CapacityRulesActive, "a scenario start must open the licence register");

        var sites = engine.State.SeaSites.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        // The compliant count is the plan's room under the v1 rules, also for
        // the unregulated replay, so every strategy starts from the same number.
        var room = sites.ToDictionary(s => s.Id, s => engine.MaxStockableSmolts(s.Id, "standard"), StringComparer.Ordinal);
        if (!regulated) engine.State.CapacityRules = CapacityRegime.Unregulated;

        var result = new Result
        {
            Strategy = strategy, Seed = seed, Regulated = regulated, AreaMtbTonnes = engine.AreaLicenceMtbTonnes(Area)
        };

        foreach (var site in sites)
        {
            int count = strategy == CapacityStrategy.Overstock ? OverstockFactor * room[site.Id] : room[site.Id];
            if (count <= 0) continue;
            decimal cash = engine.FinanceManager.Balance;
            var stocked = engine.StockSmolts(site.Id, count, "standard");
            if (!stocked.Ok && stocked.ErrorKey == "insufficient_funds" && strategy == CapacityStrategy.Overstock)
                stocked = BorrowAndStock(engine, site, count);
            if (stocked.Ok)
            {
                result.StockedSmolts += count;
                continue;
            }
            Assert.Equal(CapacityStrategy.Overstock, strategy);
            result.RefusedRequests++;
            result.RefusedStockingKey = stocked.ErrorKey;
            result.RefusedStockingMovedCash |= engine.FinanceManager.Balance != cash;
        }

        var spot = new List<double>();
        var triggerDay = new Dictionary<string, int>(StringComparer.Ordinal);
        while (engine.State.Day < HorizonDays && engine.State.SeaSites.Any(s => Fish(s) > 0))
        {
            engine.Advance(1);
            spot.Add((double)engine.MarketSimulation.PricePerKg);
            engine.DrainDayNotes();
            result.PeakStandingTonnes = Math.Max(result.PeakStandingTonnes, engine.AreaStandingBiomassTonnes(Area));
            double today = spot[^1];
            double mean = spot.Skip(Math.Max(0, spot.Count - PlayerScriptProbe.SpotMeanWindowDays)).Average();

            foreach (var site in engine.State.SeaSites.OrderBy(s => s.Id, StringComparer.Ordinal).ToList())
            {
                if (Fish(site) == 0) continue;
                double w = MeanWeightKg(site);
                if (w >= PlayerScriptProbe.HarvestTriggerWeightKg && !triggerDay.ContainsKey(site.Id))
                    triggerDay[site.Id] = engine.State.Day;
                bool forced = w >= PlayerScriptProbe.ForcedHarvestWeightKg;
                bool sell = strategy == CapacityStrategy.GrowOn
                    ? forced || (triggerDay.TryGetValue(site.Id, out int reached) && engine.State.Day >= reached + GrowOnHoldDays)
                    : forced || (w >= PlayerScriptProbe.HarvestTriggerWeightKg && today >= mean);
                if (!sell) continue;
                Harvest(engine, site, result);
                triggerDay.Remove(site.Id);
            }

            if (strategy == CapacityStrategy.Compliant)
                RelieveMtb(engine, result);
        }

        result.DaysRun = engine.State.Day;
        result.EndingEquityNok = engine.FinanceManager.Balance - engine.TotalDebtNok;
        foreach (var b in engine.State.CapacityBreaches)
        {
            result.Breaches++;
            result.FeesNok += b.FineNok;
            result.ConfiscatedNok += b.ConfiscatedNok;
            result.ForcedHarvestKg += b.ForcedHarvestKg;
            result.ForcedHarvestRevenueNok += b.ForcedHarvestRevenueNok;
            result.PeakIllegalKg = Math.Max(result.PeakIllegalKg, b.PeakIllegalKg);
        }
        return result;
    }

    /// <summary>
    /// MTB relief: when the area or a site stands at <see cref="ReliefFraction"/>
    /// of its cap, sell the heaviest site that holds fish (a full site first).
    /// </summary>
    private static void RelieveMtb(SimulationEngine engine, Result result)
    {
        bool areaFull = engine.AreaStandingBiomassTonnes(Area) >= ReliefFraction * engine.AreaLicenceMtbTonnes(Area);
        var full = engine.State.SeaSites.Where(s => Fish(s) > 0 && s.CurrentBiomassTonnes >= ReliefFraction * s.MABTonnes).ToList();
        if (!areaFull && full.Count == 0) return;
        var target = (full.Count > 0 ? full : engine.State.SeaSites.Where(s => Fish(s) > 0).ToList())
            .OrderByDescending(MeanWeightKg).ThenBy(s => s.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (target != null) Harvest(engine, target, result);
    }

    /// <summary>
    /// The overstocker insists on its count: when the only refusal is money
    /// (rules that check no plan), it borrows the smolts' price, in whole
    /// millions with a million of margin, and stocks again. Wave-2 integration:
    /// S2-03's grown starting stock leaves 173,555 smolts of plan room per po3
    /// site, so three times that costs 6.2M a site against 10M of starting
    /// cash. Under the licence register the capacity refusal comes before the
    /// money check (StockSmolts), so a regulated overstocker never borrows.
    /// </summary>
    private static CommandResult BorrowAndStock(SimulationEngine engine, SeaSite site, int count)
    {
        decimal price = count * engine.Calibration.Economy.SmoltCostPerFishNOK;
        double loan = Math.Ceiling((double)price / 1_000_000.0 + 1.0) * 1_000_000.0;
        var borrowed = engine.TakeLoan(loan, 36, 0.07);
        Assert.True(borrowed.Ok, $"the overstocker's loan of {loan:N0} NOK failed: {borrowed.ErrorKey}");
        return engine.StockSmolts(site.Id, count, "standard");
    }

    private static void Harvest(SimulationEngine engine, SeaSite site, Result result)
    {
        var cmd = engine.HarvestSite(site.Id);
        Assert.True(cmd.Ok, $"harvest of {site.Id} failed: {cmd.ErrorKey}");
        result.HarvestedKg += cmd.Value;
        result.HarvestRevenueNok += (decimal)cmd.Value2;
    }

    private static int Fish(SeaSite site) => site.Pens.Sum(p => p.TotalCount);

    private static double MeanWeightKg(SeaSite site)
    {
        int fish = Fish(site);
        return fish > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish : 0.0;
    }

    /// <summary>One line per career for the evidence log.</summary>
    public static string Format(Result r)
        => $"seed {r.Seed,2} {(r.Regulated ? "v1 " : "old")} {r.Strategy,-9} equity {r.EndingEquityNok / 1_000_000m,8:F2}M  " +
           $"harvested {r.HarvestedKg / 1000.0,7:F1} t  stocked {r.StockedSmolts,7:N0}  refused {r.RefusedRequests} '{r.RefusedStockingKey}'  " +
           $"peak {r.PeakStandingTonnes,7:F1}/{r.AreaMtbTonnes:F0} t  breaches {r.Breaches} illegal {r.PeakIllegalKg / 1000.0:F1} t " +
           $"fee {r.FeesNok / 1_000_000m:F2}M conf {r.ConfiscatedNok / 1_000_000m:F2}M forced {r.ForcedHarvestKg / 1000.0:F1} t " +
           $"({r.ForcedHarvestRevenueNok / 1_000_000m:F2}M)  days {r.DaysRun}";
}
