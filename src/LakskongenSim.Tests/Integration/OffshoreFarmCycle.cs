using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The offshore twin of <see cref="PlayerScriptFarmCycle"/> (F2-07 acceptance 7):
/// the same company in the same PO 4 water, events on, the same smolts in
/// mid-April and the same harvest rule (from 4.5 kg on a day spot is at or
/// above its 30-day mean, at 6 kg whatever the price), but the fish stand in
/// one OF1-class unit on off_1 (three zones, the legacy scenario build so the
/// unit is ready at release) run by its Driftsplan defaults alone, with its
/// planned harvests through the S2-04 pipeline (the offshore wellboat, gated
/// by the unit's sea). Operating cost per kilo of gutted fish sold, ex feed,
/// ex smolts and ex depreciation, comes from the engine's ledger between
/// stocking and the last batch, as the fjord farm's does.
/// </summary>
public static class OffshoreFarmCycle
{
    /// <summary>The ledger lines left out of "opex ex feed": the feed, its spill and the smolts (depreciation is no P&amp;L cost line here).</summary>
    public static readonly IReadOnlySet<string> NotOpex = new HashSet<string>(StringComparer.Ordinal) { "feed", "feed_spill", "smolts", "tax", "interest" };

    public sealed class CycleCost
    {
        public int Seed;
        public bool Offshore;
        public double HogKg;
        public decimal OpexExFeedNok;
        public int Days;
        public Dictionary<string, decimal> Lines = new(StringComparer.Ordinal);

        /// <summary>Operating cost ex feed, ex smolts, per kilo of gutted fish sold (NOK/kg HOG).</summary>
        public double OpexPerKgHog => HogKg > 0 ? (double)OpexExFeedNok / HogKg : double.NaN;

        /// <summary>
        /// Every operating cost line with the feed, its spill and the smolts
        /// kept (tax and interest left out; depreciation is no cash line), per
        /// kilo of gutted fish sold (NOK/kg HOG): the basis of [O:R37]'s
        /// "driftskostnader (kr per kg)" 56 against conventional production's
        /// about 43 (Menon 131/2023, its scenario table, after Fiskeridir's 2021
        /// profitability survey). Information beside acceptance 7's ex-feed band.
        /// </summary>
        public double AllInPerKgHog => HogKg > 0
            ? (double)Lines.Where(kv => kv.Key is not ("tax" or "interest")).Sum(kv => kv.Value) / HogKg
            : double.NaN;
    }

    /// <summary>
    /// Run one cycle: <paramref name="offshore"/> true for the unit, false for
    /// the fjord farm (<see cref="PlayerScriptFarmCycle"/>'s two eight-pen
    /// sites, played by the same rules). Deterministic.
    /// </summary>
    public static CycleCost Run(int seed, bool offshore)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(PlayerScriptFarmCycle.StartingCashNok * 10) };
        engine.Initialize(enableEvents: true, seed: seed);
        engine.State.SeaSites.Clear();
        engine.State.HomeProductionArea = PlayerScriptFarmCycle.ProductionArea;
        var sites = new List<string>();
        if (offshore)
        {
            var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
            Assert.True(built.Ok, built.Message);
            var unit = engine.OffshoreUnits.Single(u => u.Id == built.Id);
            unit.Offshore!.ConstructionDays = PlayerScriptFarmCycle.ReleaseDay - 2;
            sites.Add(unit.Id);
        }
        else
        {
            var zone = new Zone("po4_farm") { ProductionArea = PlayerScriptFarmCycle.ProductionArea };
            engine.State.Zones.Add(zone);
            for (int i = 0; i < PlayerScriptFarmCycle.Sites; i++)
            {
                engine.State.SeaSites.Add(new SeaSite($"farm_{i + 1}", zone.Id)
                {
                    MABTonnes = PlayerScriptFarmCycle.SiteMabTonnes,
                    NumberOfPens = PlayerScriptFarmCycle.PensPerSite,
                });
                sites.Add($"farm_{i + 1}");
            }
        }
        engine.Advance(PlayerScriptFarmCycle.ReleaseDay);
        var cost = new CycleCost { Seed = seed, Offshore = offshore };
        engine.PnlBooked += (day, category, amount, income) =>
        {
            if (income) return;
            cost.Lines[category] = (cost.Lines.TryGetValue(category, out var v) ? v : 0m) - amount;
        };

        int smolts = PlayerScriptFarmCycle.Sites * PlayerScriptFarmCycle.PensPerSite * PlayerScriptFarmCycle.SmoltsPerPen;
        if (offshore)
        {
            var unit = engine.FindOffshoreUnit(sites[0])!;
            Assert.True(unit.Offshore!.IsOperational, "the unit is not ready at release");
            int zones = unit.PhysicalPens.Count;
            for (int z = 0; z < zones; z++)
            {
                var stock = engine.StockOffshoreFarm(unit.Id, smolts / zones, "standard");
                Assert.True(stock.Ok, $"seed {seed}: stocking the unit failed: {stock.ErrorKey}");
            }
        }
        else
        {
            foreach (var siteId in sites)
                for (int p = 0; p < PlayerScriptFarmCycle.PensPerSite; p++)
                    Assert.True(engine.StockSmolts(siteId, PlayerScriptFarmCycle.SmoltsPerPen, "standard").Ok);
        }

        var ordered = new HashSet<string>();
        var orderIds = new HashSet<string>();
        var spot = new List<double>();
        int start = engine.State.Day;
        while (engine.State.Day < PlayerScriptFarmCycle.HorizonDays)
        {
            engine.Advance(1);
            engine.DrainDayNotes();
            spot.Add((double)engine.MarketSimulation.PricePerKg);
            double spotMean = spot.Skip(Math.Max(0, spot.Count - PlayerScriptProbe.SpotMeanWindowDays)).Average();
            foreach (var siteId in sites)
            {
                var site = engine.State.SeaSites.First(s => s.Id == siteId);
                int fish = site.Pens.Sum(p => p.TotalCount);
                if (fish == 0 || ordered.Contains(siteId)) continue;
                if (!offshore && site.Lice.AdultFemalePerFish >= PlayerScriptFarmCycle.LiceTreatmentTrigger)
                    engine.DelouseSite(siteId, "hydrolicer");
                double weightKg = site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish;
                if (weightKg < PlayerScriptProbe.HarvestTriggerWeightKg) continue;
                if (weightKg < PlayerScriptProbe.ForcedHarvestWeightKg && spot[^1] < spotMean) continue;
                var order = engine.OrderHarvest(siteId);
                Assert.True(order.Ok, $"seed {seed}: harvest order at {siteId} failed: {order.ErrorKey}");
                ordered.Add(siteId);
                orderIds.Add(order.Id);
            }
            if (ordered.Count == sites.Count && engine.HarvestOrders.Count == 0) break;
        }
        Assert.True(ordered.Count == sites.Count && engine.HarvestOrders.Count == 0, $"seed {seed}: the cycle did not finish");
        cost.HogKg = engine.HarvestReports.Where(r => orderIds.Contains(r.OrderId)).Sum(r => r.HogKg);
        cost.OpexExFeedNok = cost.Lines.Where(kv => !NotOpex.Contains(kv.Key)).Sum(kv => kv.Value);
        cost.Days = engine.State.Day - start;
        return cost;
    }
}
