using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// A farm-gate production cycle on the player path (roadmap S2-04 acceptance 4:
/// the EBIT/kg band test runs on the PlayerScriptProbe path). A commercial farm
/// of two eight-pen sites in PO4 water, events on, played only through the
/// verbs the game's UI uses: stock smolts in every pen in mid-April, delouse at
/// the trigger, order each site's harvest by the probe's D2 rule (from 4.5 kg
/// on a day spot is at or above its 30-day mean, at 6 kg whatever the price)
/// through the harvest pipeline (fasting, wellboat slot, slaughterhouse
/// capacity). EBIT comes from the engine's own ledger (revenue − cost of goods
/// sold − operating expenses, before interest and tax) between stocking and
/// the last slaughtered batch, per kilo of gutted fish sold — the way SalMar
/// and Mowi report farming EBIT/kg.
/// </summary>
public static class PlayerScriptFarmCycle
{
    /// <summary>Sites in the farm.</summary>
    public const int Sites = 2;

    /// <summary>Pens per site (a typical Norwegian site holds 6-10).</summary>
    public const int PensPerSite = 8;

    /// <summary>Smolts stocked per pen (600,000 a site; a pen holds at most 200,000).</summary>
    public const int SmoltsPerPen = 75_000;

    /// <summary>Site MTB (t): four 780 t licences.</summary>
    public const double SiteMabTonnes = 4 * 780.0;

    /// <summary>Production area of the farm's water.</summary>
    public const int ProductionArea = 4;

    /// <summary>Stocking day: mid-April, an S1 release (as SimProbe and the S2-03 acceptance).</summary>
    public const int ReleaseDay = 105;

    /// <summary>Cash at the start: enough to run the cycle without borrowing (EBIT is before interest anyway).</summary>
    public const decimal StartingCashNok = 250_000_000m;

    /// <summary>Adult female lice per fish at which the probe delouses a site.</summary>
    public const double LiceTreatmentTrigger = 0.4;

    /// <summary>Last day the cycle may run.</summary>
    public const int HorizonDays = 1200;

    public sealed class FarmCycleResult
    {
        public int Seed;
        public int DaysToLastSlaughter;
        public int Smolts;
        public int FishSlaughtered;
        public double LiveKg;
        public double HogKg;
        public decimal RevenueNok;
        public decimal CogsNok;
        public decimal OpexNok;
        public decimal HarvestCostNok;
        public decimal FeedCostNok;
        public decimal SpotTimesHogKg;
        public double SuperiorKg;
        public int Orders;
        public int Treatments;
        public List<double> OrderWeightsKg = new();

        /// <summary>EBIT (NOK): revenue − cost of goods sold − operating expenses over the cycle.</summary>
        public decimal EbitNok => RevenueNok - CogsNok - OpexNok;

        /// <summary>EBIT per kilo of gutted fish sold (NOK/kg HOG).</summary>
        public double EbitPerKgHog => HogKg > 0 ? (double)EbitNok / HogKg : 0.0;

        /// <summary>Production cost per kilo of gutted fish sold (NOK/kg HOG).</summary>
        public double CostPerKgHog => HogKg > 0 ? (double)(CogsNok + OpexNok) / HogKg : 0.0;

        /// <summary>Gross sales per kilo of gutted fish (NOK/kg HOG).</summary>
        public double RevenuePerKgHog => HogKg > 0 ? (double)RevenueNok / HogKg : 0.0;

        /// <summary>Gross sales per HOG kilo over the spot of the slaughter days.</summary>
        public double PriceAchievement => SpotTimesHogKg > 0m ? (double)(RevenueNok / SpotTimesHogKg) : 0.0;

        /// <summary>Fish lost between stocking and slaughter (%).</summary>
        public double MortalityPct => Smolts > 0 ? 100.0 * (Smolts - FishSlaughtered) / Smolts : 0.0;

        public double SuperiorShare => HogKg > 0 ? SuperiorKg / HogKg : 0.0;
    }

    /// <summary>Run the cycle for <paramref name="seed"/>. Deterministic.</summary>
    public static FarmCycleResult Run(int seed)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(StartingCashNok);
        engine.Initialize(enableEvents: true, seed: seed);
        engine.State.SeaSites.Clear();
        engine.State.HomeProductionArea = ProductionArea;
        var zone = new Zone("po4_farm") { ProductionArea = ProductionArea };
        engine.State.Zones.Add(zone);
        for (int i = 0; i < Sites; i++)
            engine.State.SeaSites.Add(new SeaSite($"farm_{i + 1}", zone.Id)
            {
                MABTonnes = SiteMabTonnes,
                NumberOfPens = PensPerSite
            });

        engine.Advance(ReleaseDay);
        var fs = engine.FinancialStatements;
        decimal revenue0 = fs.LifetimeRevenue, cogs0 = fs.LifetimeCOGS, opex0 = fs.LifetimeOperatingExpenses;
        var result = new FarmCycleResult { Seed = seed };

        // ===== Stock every pen through the player verb =====
        foreach (var site in engine.State.SeaSites)
            for (int p = 0; p < PensPerSite; p++)
            {
                var stock = engine.StockSmolts(site.Id, SmoltsPerPen, "standard");
                Assert.True(stock.Ok, $"seed {seed}: stocking {site.Id} failed: {stock.ErrorKey}");
                result.Smolts += SmoltsPerPen;
            }

        var ordered = new HashSet<string>();
        var orderIds = new HashSet<string>();
        var spotHistory = new List<double>();
        while (engine.State.Day < HorizonDays)
        {
            engine.Advance(1);
            engine.DrainDayNotes();
            spotHistory.Add((double)engine.MarketSimulation.PricePerKg);
            Assert.True(engine.FinanceManager.Balance > 0m, $"seed {seed}: the farm ran out of cash on day {engine.State.Day}");

            double spotToday = spotHistory[^1];
            double spotMean = spotHistory.Skip(Math.Max(0, spotHistory.Count - PlayerScriptProbe.SpotMeanWindowDays)).Average();
            foreach (var site in engine.State.SeaSites)
            {
                int fish = site.Pens.Sum(p => p.TotalCount);
                if (fish == 0 || ordered.Contains(site.Id)) continue;
                if (site.Lice.AdultFemalePerFish >= LiceTreatmentTrigger && engine.DelouseSite(site.Id, "hydrolicer").Ok)
                    result.Treatments++;
                double weightKg = site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish;
                if (weightKg < PlayerScriptProbe.HarvestTriggerWeightKg) continue;
                if (weightKg < PlayerScriptProbe.ForcedHarvestWeightKg && spotToday < spotMean) continue;
                var order = engine.OrderHarvest(site.Id);
                Assert.True(order.Ok, $"seed {seed}: harvest order at {site.Id} failed: {order.ErrorKey}");
                ordered.Add(site.Id);
                orderIds.Add(order.Id);
                result.Orders++;
                result.OrderWeightsKg.Add(weightKg);
            }

            if (ordered.Count == Sites && engine.HarvestOrders.Count == 0)
                break;
        }
        Assert.True(ordered.Count == Sites && engine.HarvestOrders.Count == 0,
            $"seed {seed}: the cycle did not finish by day {HorizonDays}");

        foreach (var report in engine.HarvestReports.Where(r => orderIds.Contains(r.OrderId)))
        {
            result.FishSlaughtered += report.Fish;
            result.LiveKg += report.LiveKg;
            result.HogKg += report.HogKg;
            result.HarvestCostNok += report.HarvestCostNok;
            result.SpotTimesHogKg += report.ReferencePriceNok * (decimal)report.HogKg;
            result.SuperiorKg += report.SuperiorShare * report.HogKg;
        }
        result.DaysToLastSlaughter = engine.State.Day - ReleaseDay;
        result.RevenueNok = fs.LifetimeRevenue - revenue0;
        result.CogsNok = fs.LifetimeCOGS - cogs0;
        result.OpexNok = fs.LifetimeOperatingExpenses - opex0;
        return result;
    }
}
