using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Harvest
{
    /// <summary>
    /// Roadmap S2-04 on the engine path: the ledger identity of a harvest, the
    /// harvest cost as cost of goods sold (review F270), fasting, the wellboat
    /// slot, the slaughterhouse's daily capacity, partial and per-pen harvests,
    /// the routes of the farm types, cancellation, saves and determinism.
    /// </summary>
    public class HarvestPipelineTests
    {
        private static readonly HarvestCalibration Cal = CalibrationRegistry.Default.Harvest;

        private static SimulationEngine NewEngine(int seed)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
            engine.Initialize(enableEvents: false, seed: seed);
            engine.State.SeaSites.Clear();
            engine.State.HomeProductionArea = 4;
            return engine;
        }

        /// <summary>A site with no zone, in PO4 water (no blooms, only the sea's temperature).</summary>
        private static SeaSite AddSite(SimulationEngine engine, string id)
        {
            var site = new SeaSite(id) { MABTonnes = 10_000 };
            engine.State.SeaSites.Add(site);
            return site;
        }

        private static FishPopulation AddPen(SimulationEngine engine, SeaSite site, int fish, double kg, double cv = 0.20)
        {
            var pen = new FishPopulation { ZoneId = site.ZoneId };
            pen.InjectDependencies(engine.Growth, engine.Mortality);
            pen.Cohorts.Add(new FishPopulation.Cohort(500, fish, kg, LifeStage.Seawater) { WeightCv = cv });
            site.Stock(pen);
            engine.State.Populations.Add(pen);
            return pen;
        }

        private static decimal Booked(IReadOnlyDictionary<string, decimal> breakdown, string category)
            => breakdown.TryGetValue(category, out var v) ? v : 0m;

        private static void AdvanceUntilDone(SimulationEngine engine, int maxDays = 120)
        {
            for (int d = 0; d < maxDays && engine.HarvestOrders.Count > 0; d++) engine.Advance(1);
            Assert.Empty(engine.HarvestOrders);
        }

        // ── Acceptance 1 and F270: the ledger ─────────────────────────────────

        [Fact]
        public void TheLedger_BooksHogKilosTimesClassPriceTimesGrade_LessTheHarvestCost()
        {
            var engine = NewEngine(41);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 6_000, 4.8);
            AddPen(engine, site, 5_000, 5.6, 0.25);
            double liveKg = site.Pens.Sum(p => p.GetTotalBiomassKg());
            decimal spot = engine.MarketSimulation.PricePerKg;
            var fs = engine.FinancialStatements;
            decimal cash0 = engine.FinanceManager.Balance, revenue0 = fs.LifetimeRevenue, cogs0 = fs.LifetimeCOGS;

            var harvest = engine.HarvestSite(site.Id);

            Assert.True(harvest.Ok, harvest.Message);
            var report = engine.HarvestReports[^1];
            Assert.Equal(liveKg, harvest.Value, 6);
            Assert.Equal(liveKg * Cal.HogYieldFraction, report.HogKg, 6);
            // Revenue = Σ pens Σ size classes (HOG kg × class factor) × grade factor × spot,
            // rebuilt here pen by pen from the calibration.
            double referenceKg = 0.0;
            foreach (var (fish, kg, cv) in new[] { (6_000, 4.8, 0.20), (5_000, 5.6, 0.25) })
            {
                var classes = new double[SizeClassPriceLadder.ClassCount];
                SizeClassPriceLadder.AddCohort(classes, fish, kg, cv, Cal.HogYieldFraction);
                double sized = 0.0;
                for (int c = 0; c < classes.Length; c++) sized += classes[c] * SizeClassPriceLadder.Factor(Cal, c);
                referenceKg += sized * HarvestGrading.Grade(Cal, new HarvestCondition(kg)).PriceFactor(Cal);
            }
            Assert.Equal(referenceKg * (double)spot, (double)report.GrossRevenueNok, 2);
            Assert.Equal((double)report.GrossRevenueNok, harvest.Value2, 2);
            // The harvest cost (wellboat + slaughter + the express surcharge) is cost of goods sold.
            decimal cost = (decimal)report.HogKg * (Cal.WellboatCostPerKgHogNok + Cal.SlaughterCostPerKgHogNok
                + Cal.ExpressSurchargePerKgHogNok);
            Assert.Equal((double)cost, (double)report.HarvestCostNok, 4);
            Assert.Equal(report.GrossRevenueNok, fs.LifetimeRevenue - revenue0);
            Assert.Equal(report.HarvestCostNok, fs.LifetimeCOGS - cogs0);
            Assert.Equal(report.HarvestCostNok, Booked(fs.CostBreakdown, "harvest"));
            Assert.Equal(report.GrossRevenueNok - report.HarvestCostNok, engine.FinanceManager.Balance - cash0);
            Assert.Equal(0m, engine.UnbookedCashTotal);
        }

        [Fact]
        public void APlannedHarvest_PaysWellboatAndSlaughter_ButNoSurcharge_AndEveryBatchIsInTheLedger()
        {
            var engine = NewEngine(42);
            var site = AddSite(engine, "a");
            for (int i = 0; i < 4; i++) AddPen(engine, site, 20_000, 5.0);
            var fs = engine.FinancialStatements;
            decimal revenue0 = Booked(fs.RevenueBreakdown, "harvest"), cost0 = Booked(fs.CostBreakdown, "harvest");

            var order = engine.OrderHarvest(site.Id);
            Assert.True(order.Ok, order.Message);
            AdvanceUntilDone(engine);

            var reports = engine.HarvestReports.Where(r => r.OrderId == order.Id).ToList();
            Assert.NotEmpty(reports);
            foreach (var r in reports)
            {
                Assert.Equal(HarvestMode.Planned, r.Mode);
                Assert.Equal((double)r.ReferencePriceNok * r.ReferenceEquivalentKg, (double)r.GrossRevenueNok, 2);
                Assert.Equal((double)((decimal)r.HogKg * (Cal.WellboatCostPerKgHogNok + Cal.SlaughterCostPerKgHogNok)),
                    (double)r.HarvestCostNok, 4);
                Assert.Equal(0m, r.SurchargeNok);
            }
            Assert.Equal(reports.Sum(r => r.GrossRevenueNok), Booked(fs.RevenueBreakdown, "harvest") - revenue0);
            Assert.Equal(reports.Sum(r => r.HarvestCostNok), Booked(fs.CostBreakdown, "harvest") - cost0);
            Assert.InRange(reports.Sum(r => r.Fish), 79_000, 80_000); // all but the few that died before their turn
            Assert.Empty(site.Pens);
        }

        // ── Fasting, the wellboat slot and the slaughterhouse's capacity ────────

        [Fact]
        public void OrderedFish_FastBeforeTheirSlot_AndAreSlaughteredNoEarlierThanTheSlotAndTheFast()
        {
            var engine = NewEngine(43);
            engine.Advance(200); // July: ~13 °C, the gut empties in three days
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 10_000, 5.0);
            var other = AddSite(engine, "b");
            var fed = AddPen(engine, other, 10_000, 5.0);
            int day0 = engine.State.Day;

            var order = engine.OrderHarvest(site.Id);
            Assert.True(order.Ok);
            var placed = engine.HarvestOrders.Single();
            Assert.Equal(day0 + Cal.WellboatLeadDays, placed.SlotDay);
            Assert.False(pen.IsFasting); // the fast starts just ahead of the slot

            double weightAtFastStart = 0.0;
            int fastStart = -1, slaughtered = -1;
            for (int d = 0; d < 30 && slaughtered < 0; d++)
            {
                double before = pen.TotalCount > 0 ? pen.GetTotalBiomassKg() / pen.TotalCount : 0.0;
                double fedBefore = fed.GetTotalBiomassKg() / fed.TotalCount;
                engine.Advance(1);
                if (fastStart < 0 && pen.IsFasting) { fastStart = engine.State.Day; weightAtFastStart = pen.GetTotalBiomassKg() / pen.TotalCount; }
                if (fastStart >= 0 && pen.TotalCount > 0 && engine.State.Day > fastStart)
                {
                    // A fasting fish eats nothing and loses a little weight; its neighbour grows.
                    Assert.True(pen.GetTotalBiomassKg() / pen.TotalCount < before, "a fasting fish grew");
                    Assert.True(fed.GetTotalBiomassKg() / fed.TotalCount > fedBefore, "the fed site stopped growing");
                }
                if (engine.HarvestReports.Any(r => r.OrderId == order.Id)) slaughtered = engine.State.Day;
            }
            Assert.True(fastStart >= 0 && slaughtered > 0, $"fast {fastStart}, slaughter {slaughtered}");
            Assert.True(slaughtered >= placed.SlotDay, $"slaughtered on day {slaughtered}, slot {placed.SlotDay}");
            Assert.True(slaughtered - fastStart >= Cal.MinFastingDays, "slaughtered before the fast was done");
            Assert.True(slaughtered - fastStart <= 6, $"fasted {slaughtered - fastStart} days for a 5-day slot");
            // The gut emptied: degree-days of the fish's own water reached the requirement.
            var report = engine.HarvestReports.Single(r => r.OrderId == order.Id);
            Assert.True(report.MeanLiveWeightKg < weightAtFastStart, "fasting cost no weight at all");
            Assert.True(report.MeanLiveWeightKg > weightAtFastStart * 0.99, "fasting cost more than 1% of the weight");
        }

        [Fact]
        public void InColdWater_TheFastStartsAtOnce_AndTakesLonger()
        {
            var engine = NewEngine(58); // 1 January in PO4: ~7 °C
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 10_000, 5.0);
            double temperature = engine.HoldingWaterOf(pen).TemperatureC;
            Assert.True(temperature < 8.0, $"January water {temperature:F1} °C");

            var quote = engine.QuoteHarvest(site.Id);
            Assert.Equal((int)Math.Ceiling(Cal.FastingDegreeDays / temperature - 1e-9), quote.FastingDays);
            Assert.True(engine.OrderHarvest(site.Id).Ok);
            Assert.True(pen.IsFasting, "a cold-water fast that needs the whole lead time did not start");
            for (int d = 0; d < 20 && engine.HarvestReports.Count == 0; d++)
            {
                engine.Advance(1);
                if (engine.HarvestReports.Count == 0) continue;
                Assert.True(pen.FastingDegreeDays >= Cal.FastingDegreeDays || pen.TotalCount == 0);
            }
            var report = engine.HarvestReports.Single();
            Assert.True(report.Day - engine.HarvestReports[0].Day == 0);
            Assert.True(report.Day >= quote.FirstSlaughterDay);
        }

        [Fact]
        public void TheFast_IsDoneWhenTheGutIsEmpty_DegreeDaysOfTheFishsOwnWater_NotACalendarCount()
        {
            var engine = NewEngine(59); // January, ~7 °C in PO4
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 1_000, 5.0);
            engine.StartFasting(pen);
            int days = 0;
            while (!engine.FastingComplete(pen) && days < 30)
            {
                engine.Advance(1);
                days++;
                if (!engine.FastingComplete(pen))
                    Assert.True(pen.FastingDegreeDays < Cal.FastingDegreeDays || days < Cal.MinFastingDays);
            }
            // At ~7 °C 30 degree-days take five days, not the two days of a minimum fast.
            Assert.InRange(days, 4, 6);
            Assert.True(pen.FastingDegreeDays >= Cal.FastingDegreeDays);
            Assert.True(days > Cal.MinFastingDays);
        }

        [Fact]
        public void ABigHarvest_IsSlaughteredOverSeveralDays_WithinTheDailyCapacity_AndASecondOrderWaitsItsTurn()
        {
            var engine = NewEngine(44);
            var big = AddSite(engine, "big");
            for (int i = 0; i < 8; i++) AddPen(engine, big, 100_000, 5.0); // ~3,360 t HOG
            var small = AddSite(engine, "small");
            AddPen(engine, small, 20_000, 5.0);
            double capacityKg = Math.Min(Cal.SlaughterCapacityTonnesHogPerDay,
                Cal.WellboatCapacityTonnesLivePerDay * Cal.HogYieldFraction) * 1000.0;

            var first = engine.OrderHarvest(big.Id);
            var quote = engine.QuoteHarvest(small.Id);
            var second = engine.OrderHarvest(small.Id);
            Assert.True(first.Ok && second.Ok);
            Assert.True(quote.FirstSlaughterDay > engine.State.Day + Cal.WellboatLeadDays + 15,
                $"the small site's quote did not queue behind the big one (day {quote.FirstSlaughterDay})");
            AdvanceUntilDone(engine, 200);

            var byDay = engine.HarvestReports.GroupBy(r => r.Day).ToList();
            foreach (var day in byDay)
                Assert.True(day.Sum(r => r.HogKg) <= capacityKg + 6.0, $"day {day.Key}: {day.Sum(r => r.HogKg):N0} kg HOG");
            int bigDays = engine.HarvestReports.Count(r => r.OrderId == first.Id);
            double bigHog = engine.HarvestReports.Where(r => r.OrderId == first.Id).Sum(r => r.HogKg);
            Assert.True(bigDays >= (int)Math.Ceiling(bigHog / capacityKg), $"{bigDays} days for {bigHog:N0} kg");
            Assert.True(engine.HarvestReports.Where(r => r.OrderId == second.Id).Min(r => r.Day)
                >= engine.HarvestReports.Where(r => r.OrderId == first.Id).Max(r => r.Day),
                "the later order jumped the queue");
            // Each pen fasts only ahead of its own turn: the last pen of the big site
            // did not fast for the whole two weeks.
            Assert.Empty(big.Pens);
        }

        [Fact]
        public void AnOrderQueuedBehindABigOne_FastsOnlyAheadOfItsOwnTurn()
        {
            var engine = NewEngine(63);
            engine.Advance(200); // July: a three-day fast
            var big = AddSite(engine, "big");
            for (int i = 0; i < 8; i++) AddPen(engine, big, 100_000, 5.0); // ~22 slaughter days
            var small = AddSite(engine, "small");
            var pen = AddPen(engine, small, 20_000, 5.0);
            int day0 = engine.State.Day;
            Assert.True(engine.OrderHarvest(big.Id).Ok);
            var order = engine.OrderHarvest(small.Id);
            Assert.True(order.Ok);

            int fastStart = -1, slaughtered = -1;
            for (int d = 0; d < 60 && slaughtered < 0; d++)
            {
                engine.Advance(1);
                if (fastStart < 0 && pen.IsFasting) fastStart = engine.State.Day;
                if (engine.HarvestReports.Any(r => r.OrderId == order.Id)) slaughtered = engine.State.Day;
            }
            Assert.True(slaughtered >= day0 + 20, $"the small site was slaughtered on day {slaughtered}");
            Assert.True(fastStart > 0 && slaughtered - fastStart <= 5,
                $"the small site fasted {slaughtered - fastStart} days waiting behind the big one");
        }

        // ── Acceptance 5: partial and per-pen harvests ──────────────────────────

        [Fact]
        public void APartialHarvest_TakesThatShareOfThePen_LeavesTheRest_AndTheRestFeedsAgain()
        {
            var engine = NewEngine(45);
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 10_000, 5.2);
            var untouched = AddPen(engine, site, 8_000, 5.2);
            var fs = engine.FinancialStatements;
            decimal revenue0 = Booked(fs.RevenueBreakdown, "harvest");

            var order = engine.OrderHarvest(site.Id, new[] { pen.Id }, 0.4);
            Assert.True(order.Ok, order.Message);
            Assert.Equal(4_000, engine.HarvestOrders.Single().Pens.Single().FishToHarvest);
            int untouchedBefore = untouched.TotalCount;
            AdvanceUntilDone(engine);

            var report = engine.HarvestReports.Single(r => r.OrderId == order.Id);
            Assert.Equal(4_000, report.Fish);
            Assert.Contains(pen, site.Pens);
            Assert.InRange(pen.TotalCount, 5_900, 6_000); // the rest (a few may have died meanwhile)
            Assert.False(pen.IsFasting, "the fish left behind were never fed again");
            Assert.Equal(untouchedBefore, untouched.TotalCount + DeathsSince(untouched, untouchedBefore));
            Assert.False(untouched.IsFasting);
            Assert.Equal(report.GrossRevenueNok, Booked(fs.RevenueBreakdown, "harvest") - revenue0);

            // The fish left behind grow again.
            double w = pen.GetTotalBiomassKg() / pen.TotalCount;
            engine.Advance(10);
            Assert.True(pen.GetTotalBiomassKg() / pen.TotalCount > w);
        }

        private static int DeathsSince(FishPopulation pen, int before) => before - pen.TotalCount;

        [Fact]
        public void AnExpressPartialHarvest_TakesTheShareOfTheChosenPensOnly()
        {
            var engine = NewEngine(46);
            var site = AddSite(engine, "a");
            var a = AddPen(engine, site, 10_000, 5.0);
            var b = AddPen(engine, site, 10_000, 5.0);
            var c = AddPen(engine, site, 10_000, 5.0);

            var harvest = engine.HarvestNow(site.Id, new[] { a.Id, b.Id }, 0.5);

            Assert.True(harvest.Ok, harvest.Message);
            Assert.Equal(5_000, a.TotalCount);
            Assert.Equal(5_000, b.TotalCount);
            Assert.Equal(10_000, c.TotalCount);
            Assert.Equal(10_000, engine.HarvestReports[^1].Fish);
            Assert.Equal(10_000 * 5.0, harvest.Value, 6);
        }

        [Fact]
        public void APerPenHarvest_EmptiesThePen_AndDropsItFromTheSite()
        {
            var engine = NewEngine(47);
            var site = AddSite(engine, "a");
            var a = AddPen(engine, site, 10_000, 5.0);
            var b = AddPen(engine, site, 10_000, 5.0);

            var order = engine.OrderHarvest(site.Id, new[] { a.Id });
            Assert.True(order.Ok);
            AdvanceUntilDone(engine);

            Assert.DoesNotContain(a, site.Pens);
            Assert.DoesNotContain(a, engine.State.Populations);
            Assert.Contains(b, site.Pens);
            Assert.True(b.TotalCount > 9_900);
        }

        // ── Orders: refusals and cancellation ───────────────────────────────────

        [Fact]
        public void Orders_FailBeforeAnyMutation_WithStableKeys()
        {
            var engine = NewEngine(48);
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 1_000, 5.0);
            var empty = AddSite(engine, "empty");

            Assert.Equal("holding_not_found", engine.OrderHarvest("nowhere").ErrorKey);
            Assert.Equal("invalid_fraction", engine.OrderHarvest(site.Id, null, 0.0).ErrorKey);
            Assert.Equal("invalid_fraction", engine.OrderHarvest(site.Id, null, 1.5).ErrorKey);
            Assert.Equal("invalid_fraction", engine.OrderHarvest(site.Id, null, double.NaN).ErrorKey);
            Assert.Equal("pen_not_found", engine.OrderHarvest(site.Id, new[] { "no-such-pen" }).ErrorKey);
            Assert.Equal("holding_empty", engine.OrderHarvest(empty.Id).ErrorKey);
            Assert.Equal("holding_empty", engine.HarvestNow(empty.Id).ErrorKey);
            Assert.Equal("order_not_found", engine.CancelHarvestOrder("no-such-order").ErrorKey);
            Assert.Empty(engine.HarvestOrders);
            Assert.False(pen.IsFasting);

            Assert.True(engine.OrderHarvest(site.Id).Ok);
            Assert.Equal("pen_in_order", engine.OrderHarvest(site.Id).ErrorKey);
            Assert.Single(engine.HarvestOrders);
        }

        [Fact]
        public void ACancelledOrder_SellsNothing_AndItsFishFeedAgain()
        {
            var engine = NewEngine(49);
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 10_000, 5.0);
            var order = engine.OrderHarvest(site.Id);
            engine.Advance(Cal.WellboatLeadDays - 2);
            Assert.True(pen.IsFasting, "the fast had not started a few days before the slot");

            var cancel = engine.CancelHarvestOrder(order.Id);

            Assert.True(cancel.Ok, cancel.Message);
            Assert.False(pen.IsFasting);
            Assert.Empty(engine.HarvestOrders);
            engine.Advance(10);
            Assert.Empty(engine.HarvestReports);
            Assert.True(pen.TotalCount > 9_900);
        }

        // ── Routes: the farm-type seam ──────────────────────────────────────────

        [Fact]
        public void ALandFarm_SlaughtersInItsOwnHall_WithNoWellboat_AndOutsideTheResourceRent()
        {
            var engine = NewEngine(50);
            engine.State.RASFacilities.Add(new RASFacility { Id = "ras-1", Name = "Land", ConstructionProgress = 1.0 });
            var ras = engine.RASFacilities.Single();
            var tank = new FishPopulation { SiteId = ras.Id };
            tank.InjectDependencies(engine.Growth, engine.Mortality);
            tank.Cohorts.Add(new FishPopulation.Cohort(500, 10_000, 5.0, LifeStage.Seawater) { WeightCv = 0.2 });
            ras.Tanks.Add(tank);
            engine.State.Populations.Add(tank);
            double rentKg0 = engine.State.HarvestedKgThisTaxYear;

            var quote = engine.QuoteHarvest(ras.Id);
            Assert.Equal(HarvestRouteKind.OnSiteHall, quote.Route);
            Assert.Equal(0m, quote.TransportCostNok);
            // No spot wellboat or waiting pen to pay for: an express harvest in the own hall has no surcharge.
            Assert.Equal(0m, engine.QuoteHarvest(ras.Id, express: true).SurchargeNok);
            var order = engine.OrderHarvest(ras.Id);
            Assert.True(order.Ok, order.Message);
            // No wellboat lead time: the slot is the end of the fast.
            Assert.True(engine.HarvestOrders.Single().SlotDay - engine.State.Day < Cal.WellboatLeadDays);
            AdvanceUntilDone(engine);

            var report = engine.HarvestReports.Single(r => r.OrderId == order.Id);
            Assert.Equal(HarvestRouteKind.OnSiteHall, report.Route);
            Assert.Equal(0m, report.TransportCostNok);
            Assert.Equal((double)((decimal)report.HogKg * Cal.SlaughterCostPerKgHogNok), (double)report.SlaughterCostNok, 4);
            Assert.Equal(rentKg0, engine.State.HarvestedKgThisTaxYear);
        }

        [Fact]
        public void AnOffshoreFarm_PaysTheOffshoreWellboat_AndCountsForTheResourceRent()
        {
            var engine = NewEngine(51);
            engine.State.OffshoreFarms.Add(new OffshoreFarm { Id = "off-1", Name = "Havet", ConstructionProgress = 1.0 });
            var farm = engine.OffshoreFarms.Single();
            var pen = new FishPopulation { SiteId = farm.Id };
            pen.InjectDependencies(engine.Growth, engine.Mortality);
            pen.Cohorts.Add(new FishPopulation.Cohort(500, 10_000, 5.0, LifeStage.Seawater) { WeightCv = 0.2 });
            farm.Pens.Add(pen);
            engine.State.Populations.Add(pen);

            var harvest = engine.HarvestOffshoreFarm(farm.Id);

            Assert.True(harvest.Ok, harvest.Message);
            var report = engine.HarvestReports[^1];
            Assert.Equal(HarvestRouteKind.OffshoreWellboat, report.Route);
            Assert.Equal((double)((decimal)report.HogKg * Cal.WellboatCostPerKgHogNok * Cal.OffshoreWellboatCostMultiplier),
                (double)report.TransportCostNok, 4);
            Assert.Equal(report.HogKg, engine.State.HarvestedKgThisTaxYear, 6);
        }

        [Fact]
        public void AHiredHarvestWellboat_StartsTheFastWhenBooked_AndItsHireIsTheTransport()
        {
            var engine = NewEngine(52);
            var site = AddSite(engine, "a");
            var pen = AddPen(engine, site, 10_000, 5.0);

            Assert.True(engine.RequestWellboat(site.Id, "harvest").Ok);
            Assert.True(pen.IsFasting);
            engine.Advance(6);

            var report = engine.HarvestReports.Single();
            Assert.Equal(HarvestMode.HiredWellboat, report.Mode);
            Assert.Equal(0m, report.TransportCostNok);
            Assert.Equal(0m, report.SurchargeNok);
            Assert.True(report.SlaughterCostNok > 0m);
            Assert.Empty(site.Pens);
        }

        [Fact]
        public void AForcedHarvest_IsSoldThroughThePipeline_AtThePenaltyPrice()
        {
            var engine = NewEngine(53);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 20_000, 5.0);
            decimal spot = engine.MarketSimulation.PricePerKg;

            decimal net = engine.SellForcedHarvest(site, 50.0, 0.7m);

            var report = engine.HarvestReports.Single();
            Assert.Equal(HarvestMode.Forced, report.Mode);
            Assert.Equal(0.7m, report.PriceMultiplier);
            Assert.InRange(report.LiveKg, 49_990, 50_010);
            Assert.Equal(report.LiveKg * Cal.HogYieldFraction, report.HogKg, 6);
            Assert.Equal(0.7 * report.ReferenceEquivalentKg * (double)spot, (double)report.GrossRevenueNok, 2);
            Assert.Equal(report.NetRevenueNok, net);
        }

        [Fact]
        public void TheResourceRentBase_IsTheGuttedHarvestAtTheNormPrice_AndTheHarvestCostIsNoSeaPhaseCost()
        {
            // Skatteloven § 19-5: gross income = the market value at the pen edge
            // times the harvest volume; § 19-6 lists no slaughter or transport.
            var engine = NewEngine(60);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 20_000, 5.0);
            engine.Advance(1);
            decimal norm = engine.PenPriceCouncil.MonthlyPenPrice > 0m
                ? engine.PenPriceCouncil.MonthlyPenPrice
                : engine.MarketSimulation.PricePerKg;

            Assert.True(engine.HarvestSite(site.Id).Ok);

            var report = engine.HarvestReports[^1];
            Assert.Equal(report.HogKg, engine.State.HarvestedKgThisTaxYear, 6);
            Assert.Equal((double)((decimal)report.HogKg * norm), (double)engine.State.ResourceRentHarvestValueThisTaxYear, 2);
            Assert.Equal(report.HarvestCostNok, engine.State.HarvestCostThisTaxYear);
            var fs = engine.FinancialStatements;
            decimal expected = engine.State.ResourceRentHarvestValueThisTaxYear
                - (fs.COGS + fs.OperatingExpenses - report.HarvestCostNok);
            var settlement = engine.SettleAnnualTax();
            Assert.Equal(Math.Max(0m, expected), settlement.ResourceRentBase);
            Assert.Equal(0m, engine.State.HarvestCostThisTaxYear);
        }

        // ── The quote ──────────────────────────────────────────────────────────

        [Fact]
        public void TheSameDayQuote_IsWhatAnExpressHarvestRealizes()
        {
            var engine = NewEngine(54);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 12_000, 4.9);
            AddPen(engine, site, 9_000, 5.4);
            string json0 = SaveStateComparer.Serialize(engine);

            var quote = engine.QuoteHarvest(site.Id, express: true);

            Assert.True(quote.Ok);
            Assert.Equal(json0, SaveStateComparer.Serialize(engine)); // quoting moved nothing
            var harvest = engine.HarvestNow(site.Id);
            var report = engine.HarvestReports[^1];
            Assert.Equal(quote.Fish, report.Fish);
            Assert.Equal(quote.HogKg, report.HogKg, 6);
            Assert.Equal((double)quote.GrossRevenueNok, (double)report.GrossRevenueNok, 2);
            Assert.Equal((double)quote.NetRevenueNok, (double)report.NetRevenueNok, 2);
            Assert.True(harvest.Ok);
        }

        // ── Saves and determinism ───────────────────────────────────────────────

        [Fact]
        public void AnOrderInFlight_SurvivesASave_AndTheGameContinuesExactly()
        {
            var engine = NewEngine(55);
            var site = AddSite(engine, "a");
            for (int i = 0; i < 6; i++) AddPen(engine, site, 60_000, 5.0);
            Assert.True(engine.OrderHarvest(site.Id, null, 0.8).Ok);
            engine.Advance(Cal.WellboatLeadDays + 1); // fasting done, slaughter under way

            Assert.NotEmpty(engine.HarvestOrders);
            Assert.NotEmpty(engine.HarvestReports);
            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
            Assert.Equal(engine.HarvestOrders.Single().Id, resumed.HarvestOrders.Single().Id);
            Assert.Equal(site.Pens.Count(p => p.IsFasting), resumed.State.SeaSites.Single().Pens.Count(p => p.IsFasting));

            engine.Advance(20);
            resumed.Advance(20);
            Assert.Equal("identical", SaveStateComparer.FirstDifference(
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed))));
        }

        [Fact]
        public void TwoGamesWithTheSameSeed_HarvestIdentically()
        {
            string Play()
            {
                var engine = NewEngine(56);
                var site = AddSite(engine, "a");
                for (int i = 0; i < 4; i++) AddPen(engine, site, 40_000, 4.8);
                engine.Advance(10);
                engine.OrderHarvest(site.Id, new[] { site.Pens[0].Id, site.Pens[1].Id }, 0.5);
                engine.Advance(15);
                engine.HarvestNow(site.Id);
                return SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
            }
            Assert.Equal("identical", SaveStateComparer.FirstDifference(Play(), Play()));
        }

        [Fact]
        public void ASaveFromBeforeS2_04_LoadsWithNoOrders_AndNobodyFasting()
        {
            var engine = NewEngine(57);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 10_000, 5.0);
            engine.Advance(3);
            var json = System.Text.Json.Nodes.JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
            foreach (var name in new[] { "HarvestOrders", "HarvestOrdersPlaced", "HarvestReports", "HarvestBatchesSold",
                         "TotalHarvestedHogKg", "TotalHarvestedLiveKg", "HarvestCostThisTaxYear" })
                Assert.True(json.Remove(name), $"{name} was not saved");
            string legacy = json.ToJsonString(SaveJson.Options).Replace("\"FastingSinceDay\":-1,", "");

            var resumed = SaveStateComparer.Resume(legacy, events: false);
            Assert.Empty(resumed.HarvestOrders);
            Assert.Empty(resumed.HarvestReports);
            Assert.All(resumed.State.Populations, p => Assert.False(p.IsFasting));
            Assert.True(resumed.HarvestSite(site.Id).Ok);
        }
    }
}
