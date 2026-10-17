using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Farm;
using LakskongenSim.Harvest;
using LakskongenSim.Health;

namespace LakskongenSim.Core;

/// <summary>
/// The harvest planner's projection (roadmap U2-05): what a planned harvest
/// ordered today, or after waiting some weeks, is expected to bring. It runs
/// the planned-harvest pipeline (S2-04) forward on copies: the fish grow on
/// the tick's law (<see cref="PopulationTwin"/>) until each pen fasts ahead of
/// its turn, the wellboat slot and the slaughterhouse's daily capacity set the
/// slaughter days behind the orders already queued, and every batch is priced
/// as <see cref="SellLot"/> prices it (contracts first, then the price the
/// market is expected to pay that day, the processing plant's premium).
/// Twin-lite: no random draws, nothing written (the live game and its RNG
/// streams are untouched, acceptance 3).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Longest wait the planner projects (days).</summary>
    public const int MaxHarvestWaitDays = 182;

    /// <summary>Days of the mortality ledger the projection's expected deaths are read from.</summary>
    public const int ProjectionMortalityWindowDays = 30;

    /// <summary>Days a projection may run past the order before it gives up (a queue that never clears).</summary>
    private const int ProjectionHorizonDays = 3650;

    /// <summary>
    /// Project a planned harvest of <paramref name="holdingId"/>'s chosen pens
    /// (all pens with fish when none are listed; <paramref name="fraction"/>
    /// of each pen's fish) ordered after <paramref name="waitDays"/> days
    /// (0 = today). See <see cref="HarvestProjection"/>. Fails as
    /// <see cref="QuoteHarvest"/> does, plus <c>invalid_wait</c> outside
    /// 0..<see cref="MaxHarvestWaitDays"/>.
    /// </summary>
    public HarvestProjection ProjectHarvest(string holdingId, IReadOnlyCollection<string>? penIds = null,
        double fraction = 1.0, int waitDays = 0)
    {
        if (State == null || Growth == null)
            return new HarvestProjection { ErrorKey = "engine_not_ready", Message = "Engine not initialized", HoldingId = holdingId };
        if (waitDays < 0 || waitDays > MaxHarvestWaitDays)
            return new HarvestProjection { ErrorKey = "invalid_wait", Message = $"Wait {waitDays} days is outside 0 to {MaxHarvestWaitDays}", HoldingId = holdingId };
        var quote = QuoteHarvest(holdingId, penIds, fraction);
        if (!quote.Ok)
            return new HarvestProjection { ErrorKey = quote.ErrorKey, Message = quote.Message, HoldingId = holdingId };
        var holding = FindHarvestHolding(holdingId)!;
        var pens = SelectHarvestPens(holding, penIds, out _, out _)!;
        return new HarvestTwinRun(this, holding, pens, fraction, waitDays).Run();
    }

    /// <summary>
    /// The daily share of the farm's fish expected to die on a normal day: the
    /// median of the deaths booked on the mortality ledger on each of the last
    /// <see cref="ProjectionMortalityWindowDays"/> days, per fish now (0
    /// without fish or history). The median keeps a past outbreak, storm or
    /// bloom from being projected forward; like the feeding plan's projection,
    /// the planner does not foresee a new one.
    /// </summary>
    public double ExpectedDailyMortality()
    {
        if (State == null || Mortality == null) return 0.0;
        long fish = 0;
        foreach (var population in State.Populations) fish += population.TotalCount;
        if (fish <= 0) return 0.0;
        int window = Math.Min(ProjectionMortalityWindowDays, Math.Max(1, State.Day));
        var rates = new double[window];
        for (int i = 0; i < window; i++)
            rates[i] = Mortality.Ledger.ForDays(State.Day - i, State.Day - i).Total.Fish / (double)fish;
        Array.Sort(rates);
        return rates[window / 2];
    }

    /// <summary>
    /// The contract week on post-tick day <paramref name="day"/>: today's week
    /// plus the weekly expiry ticks (<see cref="ContractExpiryTickSystem"/>)
    /// the in-day steps up to that day will run.
    /// </summary>
    private int ContractWeekOn(int day)
    {
        int week = ContractSystem?.CurrentWeek ?? 0;
        var clock = _inDayPhases.OfType<ContractExpiryTickSystem>().Cast<ITickClock>().FirstOrDefault();
        if (clock == null) return week;
        int last = clock.ClockValue;
        int lastInDay = day - 1;
        return lastInDay - last >= SimCalendar.DaysPerWeek ? week + (lastInDay - last) / SimCalendar.DaysPerWeek : week;
    }

    /// <summary>
    /// The fillet grade the processing plant gives fish from <paramref name="site"/>
    /// on <paramref name="day"/> (<see cref="FilletGradeOf"/> on that day: a plan
    /// that has run out by then grades by the company recipe).
    /// </summary>
    private string FilletGradeOn(SeaSite? site, int day)
    {
        if (site?.FeedPlan is { } plan && plan.HoldsOn(day))
        {
            var effect = FeedRecipeEffect.Of(plan.Recipe, _calibration.FeedPlan);
            return Processing.FeedRecipe.Grade(effect.ProteinPct, effect.AstaxanthinPpm);
        }
        return Processing.FeedRecipe.Grade(State.RecipeProteinPct, State.RecipeAstaxPpm);
    }

    /// <summary>A contract's room as a sale sees it: kilos left, price, last week it runs.</summary>
    private sealed class ContractRoom
    {
        public int RemainingKg;
        public decimal PricePerKg;
        public int LastWeek;
    }

    /// <summary>The active contracts in the order a sale fills them (oldest signed first).</summary>
    private List<ContractRoom> ContractRooms()
        => ContractSystem == null
            ? new List<ContractRoom>()
            : ContractSystem.ActiveContracts.OrderBy(c => c.SignedAtWeek)
                .Select(c => new ContractRoom
                {
                    RemainingKg = c.RemainingKg,
                    PricePerKg = c.PricePerKg,
                    LastWeek = c.SignedAtWeek + c.DurationWeeks
                }).ToList();

    /// <summary>
    /// Fill <paramref name="rooms"/> with <paramref name="hogKg"/> gutted kilos
    /// as <see cref="SellLot"/> does (whole kilos, oldest contract first; a
    /// contract expired by <paramref name="week"/> is gone): the kilos taken
    /// and the payment at the contracts' prices times <paramref name="valueFactor"/>.
    /// A null week fills every room (a sale today).
    /// </summary>
    private static (double Kg, decimal Revenue) FillContracts(List<ContractRoom> rooms, double hogKg, decimal valueFactor, int? week)
    {
        double remaining = hogKg;
        double taken = 0.0;
        decimal revenue = 0m;
        foreach (var room in rooms)
        {
            if (remaining <= 0.1) break;
            if (week is int w && w > room.LastWeek) continue;
            int deliver = (int)Math.Min(remaining, room.RemainingKg);
            if (deliver <= 0) continue;
            revenue += deliver * room.PricePerKg * valueFactor;
            room.RemainingKg -= deliver;
            taken += deliver;
            remaining -= deliver;
        }
        return (taken, revenue);
    }

    /// <summary>
    /// The gutted tonnes the open orders of <paramref name="route"/>'s pool
    /// take on each day (at today's weights, oldest slot first), counting only
    /// the orders a new order with slot <paramref name="slot"/> queues behind.
    /// </summary>
    private Dictionary<int, double> PoolUseAhead(HarvestRoute route, int slot)
    {
        var use = new Dictionary<int, double>();
        var queue = new List<(int Slot, double Tonnes)>();
        foreach (var order in State.HarvestOrders.Where(o => o.IsOpen).OrderBy(o => o.SlotDay).ThenBy(o => o.Sequence))
        {
            if (order.SlotDay > slot) continue;
            var holding = FindHarvestHolding(order.HoldingId);
            if (holding == null || holding.Route.CapacityPool != route.CapacityPool) continue;
            double owedKg = 0.0;
            foreach (var orderPen in order.Pens)
            {
                var pen = holding.Pens.FirstOrDefault(p => p.Id == orderPen.PopulationId);
                if (pen == null || pen.TotalCount <= 0 || orderPen.FishRemaining <= 0) continue;
                owedKg += Math.Min(orderPen.FishRemaining, pen.TotalCount) * pen.GetTotalBiomassKg() / pen.TotalCount;
            }
            queue.Add((Math.Max(order.SlotDay, State.Day + 1), owedKg * HarvestCalibration.HogYieldFraction / 1000.0));
        }
        if (queue.Count == 0) return use;
        double capacity = Math.Max(1e-6, route.DailyCapacityTonnesHog);
        var left = queue.Select(q => q.Tonnes).ToArray();
        int start = queue.Min(q => q.Slot);
        for (int day = start; day < start + ProjectionHorizonDays && left.Any(t => t > 1e-9); day++)
        {
            double room = capacity;
            for (int i = 0; i < queue.Count && room > 1e-9; i++)
            {
                if (queue[i].Slot > day || left[i] <= 1e-9) continue;
                double take = Math.Min(room, left[i]);
                left[i] -= take;
                room -= take;
                use[day] = use.GetValueOrDefault(day) + take;
            }
        }
        return use;
    }

    /// <summary>One pen of a projected harvest: its twin, its condition and its place in the order.</summary>
    private sealed class PenRun
    {
        public PenRun(PopulationTwin twin, FishPopulation population)
        {
            Twin = twin;
            Population = population;
            ColdExposure = population.ColdExposure;
            Harm = population.TreatmentHarm;
        }

        public PopulationTwin Twin { get; }
        public FishPopulation Population { get; }
        public double ColdExposure;
        public DelousingInjuryState Harm;
        public bool Fasting;
        public int FastingSinceDay = -1;
        public double FastingDegreeDays;
        /// <summary>Fish the order still owes from this pen.</summary>
        public int Owed;
        /// <summary>The water temperature of the last projected day (°C).</summary>
        public double LastTemperatureC;
        public bool Done;

        /// <summary>Whole fish in the pen (the projection's fractional count, rounded).</summary>
        public int Fish => (int)Math.Round(Twin.Fish, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// One projection run (<see cref="ProjectHarvest"/>): steps the chosen pens
    /// day by day through the wait, the order, the fasts and the slaughter.
    /// </summary>
    private sealed class HarvestTwinRun
    {
        private readonly SimulationEngine _e;
        private readonly HarvestHolding _holding;
        private readonly List<PenRun> _pens;
        private readonly double _fraction;
        private readonly int _wait;
        private readonly double _dailyDeaths;
        private readonly double _lice;
        private readonly double _spill;
        private double _feedKg;
        private decimal _feedCost;
        private double _expectedDeaths;

        public HarvestTwinRun(SimulationEngine engine, HarvestHolding holding, List<FishPopulation> pens, double fraction, int wait)
        {
            _e = engine;
            _holding = holding;
            _fraction = fraction;
            _wait = wait;
            _pens = pens.Select(p => new PenRun(new PopulationTwin(engine, p, holding.Site), p)).ToList();
            _dailyDeaths = engine.ExpectedDailyMortality();
            _lice = holding.Site?.Lice.AdultFemalePerFish ?? 0.0;
            _spill = Optimization.RemoteOpsCentre.FeedSpillFraction(holding.Site?.WatchMode);
        }

        private HarvestCalibration Cal => _e.HarvestCalibration;

        /// <summary>The share of appetite and the feed a site's plan gives on in-day day <paramref name="day"/>.</summary>
        private (double Ration, FeedRecipeEffect Effect) FeedOn(int day)
        {
            var cal = _e._calibration.FeedPlan;
            if (_holding.Site?.FeedPlan is { } plan && plan.HoldsOn(day))
                return (plan.RationFraction, FeedRecipeEffect.Of(plan.Recipe, cal));
            return (1.0, FeedRecipeEffect.Standard(cal));
        }

        /// <summary>
        /// The in-day part of projected step <paramref name="k"/> for one pen:
        /// expected deaths, then a day of growth (nothing eaten while fasting),
        /// the fast's degree-days, the cold-water memory and the healing of
        /// delousing injuries, as the biology tick does.
        /// </summary>
        private void StepPen(PenRun pen, int k)
        {
            double before = pen.Twin.Fish;
            pen.Twin.ApplyDeaths(_dailyDeaths);
            _expectedDeaths += before - pen.Twin.Fish;
            var (ration, effect) = FeedOn(_e.State.Day + k);
            var day = pen.Twin.Step(k, pen.Fasting ? 0.0 : ration, effect);
            if (!pen.Fasting)
            {
                _feedKg += day.DeliveredKg;
                _feedCost += (decimal)(day.DeliveredKg * (1.0 + _spill)) * _e.Feed.FeedCostPerKg * (decimal)effect.PriceFactor;
            }
            else
            {
                pen.FastingDegreeDays += Math.Max(0.0, day.TemperatureC);
            }
            pen.ColdExposure = HarvestGrading.NextColdExposure(Cal, pen.ColdExposure, day.TemperatureC);
            pen.Harm = DelousingHarmModel.AdvanceDay(pen.Harm);
            pen.LastTemperatureC = day.TemperatureC;
        }

        /// <summary>Days a pen not yet fasting needs at <paramref name="temperatureC"/> (EstimatedFastingDays).</summary>
        private int FastingDaysAt(double temperatureC)
        {
            double t = Math.Max(0.5, temperatureC);
            double byDegrees = Math.Ceiling(Math.Max(0.0, Cal.FastingDegreeDays) / t - 1e-9);
            return (int)Math.Max(byDegrees, Math.Max(0.0, Cal.MinFastingDays));
        }

        private bool FastingComplete(PenRun pen, int day)
            => pen.Fasting && day - pen.FastingSinceDay >= Cal.MinFastingDays
               && pen.FastingDegreeDays >= Cal.FastingDegreeDays - 1e-9;

        private double HogPerFish(PenRun pen)
        {
            double fish = pen.Twin.Fish;
            return fish > 0.0 ? pen.Twin.BiomassKg / fish * Cal.HogYieldFraction : 0.0;
        }

        /// <summary>
        /// Start the fast of each owing pen whose turn comes within its fasting
        /// time plus a day (StartFastingAhead): the pens ahead of it at the
        /// route's daily capacity, from the order's next slaughter day.
        /// </summary>
        private void FastAhead(int today, int slot, Dictionary<int, double> poolUse)
        {
            var route = _holding.Route;
            double capacity = route.DailyCapacityTonnesHog;
            int first = Math.Max(slot, today + 1);
            for (int guard = 0; guard < ProjectionHorizonDays && capacity - poolUse.GetValueOrDefault(first) <= 1e-9; guard++)
                first++;
            double capacityKg = Math.Max(1e-6, capacity * 1000.0);
            double daysToSlot = Math.Max(0, first - today);
            double hogAheadKg = 0.0;
            foreach (var pen in _pens)
            {
                if (pen.Done) continue;
                double turnInDays = daysToSlot + hogAheadKg / capacityKg;
                if (!pen.Fasting && turnInDays <= FastingDaysAt(pen.LastTemperatureC) + 1)
                {
                    pen.Fasting = true;
                    pen.FastingSinceDay = today;
                    pen.FastingDegreeDays = 0.0;
                }
                int owed = Math.Min(pen.Owed, pen.Fish);
                hogAheadKg += owed * HogPerFish(pen);
            }
        }

        /// <summary>Whole fish to take from each cohort for <paramref name="fish"/> fish (PlanCohortTakes on the projected counts).</summary>
        private static int[] CohortTakes(PopulationTwin twin, int fish)
        {
            int n = twin.Counts.Length;
            var whole = new int[n];
            for (int ci = 0; ci < n; ci++) whole[ci] = Math.Max(0, (int)Math.Round(twin.Counts[ci], MidpointRounding.AwayFromZero));
            int total = whole.Sum();
            var takes = new int[n];
            if (fish <= 0 || total <= 0) return takes;
            if (fish >= total) return whole;
            var remainders = new double[n];
            int assigned = 0;
            for (int ci = 0; ci < n; ci++)
            {
                double exact = (double)whole[ci] * fish / total;
                takes[ci] = (int)Math.Floor(exact);
                remainders[ci] = exact - takes[ci];
                assigned += takes[ci];
            }
            while (assigned < fish)
            {
                int best = -1;
                for (int ci = 0; ci < n; ci++)
                    if (takes[ci] < whole[ci] && (best < 0 || remainders[ci] > remainders[best])) best = ci;
                if (best < 0) break;
                takes[best]++;
                remainders[best] = -1.0;
                assigned++;
            }
            return takes;
        }

        /// <summary>One day's batch (SlaughterBatch): the pens in turn, each once its fast is done, within the room.</summary>
        private HarvestLot? Slaughter(int day, double roomTonnes, HarvestLot total)
        {
            double roomKg = roomTonnes * 1000.0;
            var lot = new HarvestLot(Cal);
            foreach (var pen in _pens)
            {
                if (pen.Done) continue;
                if (roomKg <= 0.0) break;
                if (!FastingComplete(pen, day)) continue;
                int owed = Math.Min(pen.Owed, pen.Fish);
                double hogPerFish = HogPerFish(pen);
                int take = hogPerFish > 0.0 ? (int)Math.Min(owed, Math.Floor(roomKg / hogPerFish)) : owed;
                if (take <= 0) break;
                double before = lot.HogKg;
                var condition = new HarvestCondition(pen.Twin.Fish > 0.0 ? pen.Twin.BiomassKg / pen.Twin.Fish : 0.0,
                    pen.ColdExposure, pen.Harm.InjuryLoad, _lice);
                var takes = CohortTakes(pen.Twin, take);
                int taken = 0;
                for (int ci = 0; ci < takes.Length; ci++)
                {
                    if (takes[ci] <= 0) continue;
                    double cv = pen.Twin.Cvs[ci] > 0.0 ? pen.Twin.Cvs[ci]
                        : (_e.Growth?.Law ?? TgcGrowthLaw.Default).InitialWeightCv(pen.Twin.Weights[ci]);
                    lot.AddFish(takes[ci], pen.Twin.Weights[ci], cv, condition);
                    total.AddFish(takes[ci], pen.Twin.Weights[ci], cv, condition);
                    pen.Twin.Counts[ci] = Math.Max(0.0, pen.Twin.Counts[ci] - takes[ci]);
                    taken += takes[ci];
                }
                roomKg -= lot.HogKg - before;
                pen.Owed -= taken;
                if (pen.Owed <= 0 || pen.Fish <= 0) pen.Done = true;
            }
            return lot.Fish > 0 ? lot : null;
        }

        public HarvestProjection Run()
        {
            var e = _e;
            var route = _holding.Route;
            int today = e.State.Day;

            // The wait: every pen feeds as its plan says (k = 0 is the next step).
            for (int k = 0; k < _wait; k++)
                foreach (var pen in _pens) StepPen(pen, k);

            // The order, on the day the wait ends.
            int orderDay = today + _wait;
            foreach (var pen in _pens)
            {
                pen.Owed = FishToHarvest(pen.Fish, _fraction);
                if (_wait == 0) pen.LastTemperatureC = e.HoldingWaterOf(pen.Population).TemperatureC;
                pen.Done = pen.Owed <= 0;
            }
            int fastingDays = _pens.Where(p => !p.Done).Select(p => FastingDaysAt(p.LastTemperatureC)).DefaultIfEmpty(0).Max();
            int slot = orderDay + Math.Max(route.LeadDays, fastingDays);
            var poolUse = e.PoolUseAhead(route, slot);
            FastAhead(orderDay, slot, poolUse);

            // The fasts and the slaughter, step by step.
            var total = new HarvestLot(Cal);
            var batches = new List<(int Day, HarvestLot Lot)>();
            for (int k = _wait; k < _wait + ProjectionHorizonDays && _pens.Any(p => !p.Done); k++)
            {
                foreach (var pen in _pens.Where(p => !p.Done)) StepPen(pen, k);
                int day = today + k + 1;
                if (day >= slot)
                {
                    double room = route.DailyCapacityTonnesHog - poolUse.GetValueOrDefault(day);
                    if (room > 1e-9 && Slaughter(day, room, total) is { } lot)
                        batches.Add((day, lot));
                }
                foreach (var pen in _pens.Where(p => !p.Done && p.Fish <= 0)) pen.Done = true;
                if (_pens.Any(p => !p.Done)) FastAhead(day, slot, poolUse);
            }

            return Price(today, orderDay, fastingDays, slot, total, batches, poolUse);
        }

        /// <summary>
        /// Price every batch as <see cref="SellLot"/> would on its day: the
        /// contracts still running (the orders ahead fill them first), then the
        /// price the market is expected to pay, the processing plant's premium.
        /// </summary>
        private HarvestProjection Price(int today, int orderDay, int fastingDays, int slot, HarvestLot total,
            List<(int Day, HarvestLot Lot)> batches, Dictionary<int, double> poolUse)
        {
            var e = _e;
            var route = _holding.Route;
            int horizon = Math.Max(batches.Count > 0 ? batches[^1].Day - today : 0,
                poolUse.Count > 0 ? poolUse.Keys.Max() - today : 0);
            var tonnes = new double[Math.Max(0, horizon)];
            foreach (var (day, use) in poolUse)
                if (day - today - 1 is int i && i >= 0 && i < tonnes.Length) tonnes[i] += use;
            foreach (var (day, lot) in batches)
                if (day - today - 1 is int i && i >= 0 && i < tonnes.Length) tonnes[i] += lot.HogKg / 1000.0;
            var prices = e.MarketSimulation?.ExpectedPrices(tonnes.Length, tonnes) ?? new decimal[tonnes.Length];

            var rooms = e.ContractRooms();
            double contractKg = 0.0, spotKg = 0.0;
            decimal contractRevenue = 0m, spotRevenue = 0m, gross = 0m, priceKg = 0m;
            decimal vapUsed = 1m;
            var days = new List<HarvestProjectionDay>();
            var orderDays = batches.ToDictionary(b => b.Day, b => b.Lot);
            foreach (var day in poolUse.Keys.Concat(orderDays.Keys).Distinct().OrderBy(d => d))
            {
                int week = e.ContractWeekOn(day);
                if (poolUse.TryGetValue(day, out var aheadTonnes))
                    FillContracts(rooms, aheadTonnes * 1000.0, 1m, week); // the orders ahead take their kilos first
                if (!orderDays.TryGetValue(day, out var lot)) continue;
                int i = day - today - 1;
                decimal price = i >= 0 && i < prices.Length ? prices[i] : e.MarketSimulation?.PricePerKg ?? 0m;
                decimal valueFactor = (decimal)lot.ValueFactor;
                var (kg, revenue) = FillContracts(rooms, lot.HogKg, valueFactor, week);
                double spot = lot.HogKg - kg;
                decimal spotSale = (decimal)spot * valueFactor * price;
                decimal vap = e.State.OwnsProcessingPlant
                    ? Processing.FeedRecipe.VapMult(e.FilletGradeOn(_holding.Site, day)) : 1m;
                decimal batchGross = (revenue + spotSale) * vap;
                contractKg += kg;
                contractRevenue += revenue;
                spotKg += spot;
                spotRevenue += spotSale;
                gross += batchGross;
                priceKg += (decimal)lot.HogKg * price;
                vapUsed = vap;
                days.Add(new HarvestProjectionDay(day, lot.Fish, lot.HogKg, price, batchGross));
            }

            decimal hog = (decimal)total.HogKg;
            return new HarvestProjection
            {
                Ok = total.Fish > 0,
                ErrorKey = total.Fish > 0 ? "" : "holding_empty",
                Message = total.Fish > 0 ? "" : $"'{_holding.Id}' has no fish to harvest",
                HoldingId = _holding.Id,
                Route = route.Kind,
                WaitDays = _wait,
                OrderDay = orderDay,
                FastingDays = fastingDays,
                SlotDay = slot,
                FirstSlaughterDay = batches.Count > 0 ? batches[0].Day : slot,
                LastSlaughterDay = batches.Count > 0 ? batches[^1].Day : slot,
                Fish = total.Fish,
                LiveKg = total.LiveKg,
                HogKg = total.HogKg,
                MeanLiveWeightKg = total.MeanLiveWeightKg,
                WeightCv = total.WeightCv,
                HogKgBySizeClass = total.HogKgBySizeClassArray(),
                SuperiorShare = total.SuperiorShare,
                OrdinaryShare = total.OrdinaryShare,
                ProductionShare = total.ProductionShare,
                SizeFactor = total.SizeFactor,
                GradeFactor = total.GradeFactor,
                SpotTodayNok = e.MarketSimulation?.PricePerKg ?? 0m,
                ExpectedReferencePriceNok = hog > 0m ? priceKg / hog : e.MarketSimulation?.PricePerKg ?? 0m,
                ContractHogKg = contractKg,
                ContractRevenueNok = contractRevenue,
                SpotHogKg = spotKg,
                SpotRevenueNok = spotRevenue,
                VapMultiplier = vapUsed,
                GrossRevenueNok = gross,
                TransportCostNok = hog * route.TransportCostPerKgHogNok,
                SlaughterCostNok = hog * route.SlaughterCostPerKgHogNok,
                FeedKg = _feedKg,
                FeedCostNok = _feedCost,
                DailyMortality = _dailyDeaths,
                ExpectedDeaths = _expectedDeaths,
                Days = days
            };
        }
    }
}
