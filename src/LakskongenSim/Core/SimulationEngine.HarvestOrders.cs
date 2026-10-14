using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Events;
using LakskongenSim.Harvest;

namespace LakskongenSim.Core;

/// <summary>
/// Planned harvests (roadmap S2-04): a quote, an order that starts the fast
/// and books a wellboat slot, and the daily slaughter of the orders' fish
/// within each capacity pool (the player's slaughterhouse contract for sea and
/// offshore fish, a land farm's own hall). Pure arithmetic, no random numbers.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The open harvest orders, oldest slot first.</summary>
    public IReadOnlyList<HarvestOrder> HarvestOrders
        => State?.HarvestOrders is { } orders
            ? orders.OrderBy(o => o.SlotDay).ThenBy(o => o.Sequence).ToList()
            : Array.Empty<HarvestOrder>();

    /// <summary>The latest harvest reports, newest last (one per slaughtered batch).</summary>
    public IReadOnlyList<HarvestReport> HarvestReports
        => State?.HarvestReports ?? (IReadOnlyList<HarvestReport>)Array.Empty<HarvestReport>();

    /// <summary>
    /// What harvesting the chosen pens of <paramref name="holdingId"/> would
    /// bring (all pens with fish when none are listed; <paramref name="fraction"/>
    /// of each pen's fish): the lot at today's weights and reference price, the
    /// harvest cost and, for a planned harvest, the fasting days, the wellboat
    /// slot and the slaughter days behind the orders already queued. Moves
    /// nothing and draws no random numbers.
    /// </summary>
    public HarvestQuote QuoteHarvest(string holdingId, IReadOnlyCollection<string>? penIds = null,
        double fraction = 1.0, bool express = false)
    {
        if (State == null)
            return new HarvestQuote { ErrorKey = "engine_not_ready", Message = "Engine not initialized", HoldingId = holdingId };
        var holding = FindHarvestHolding(holdingId);
        if (holding == null)
            return new HarvestQuote { ErrorKey = "holding_not_found", Message = $"'{holdingId}' not found", HoldingId = holdingId };
        if (!ValidFraction(fraction))
            return new HarvestQuote { ErrorKey = "invalid_fraction", Message = $"Harvest share must be in (0, 1] (got {fraction})", HoldingId = holdingId };
        var pens = SelectHarvestPens(holding, penIds, out var errorKey, out var errorMessage);
        if (pens == null)
            return new HarvestQuote { ErrorKey = errorKey, Message = errorMessage, HoldingId = holdingId };

        var lot = new HarvestLot(HarvestCalibration);
        foreach (var pen in pens)
            AddPenToLot(lot, pen, FishToHarvest(pen.TotalCount, fraction), holding, takeAs: null);
        if (lot.LiveKg < 0.1)
            return new HarvestQuote { ErrorKey = "holding_empty", Message = $"'{holdingId}' has no fish to harvest", HoldingId = holdingId };

        var route = holding.Route;
        var cal = HarvestCalibration;
        decimal spot = MarketSimulation?.PricePerKg ?? 0m;
        decimal hog = (decimal)lot.HogKg;
        var mode = express ? HarvestMode.Express : HarvestMode.Planned;
        string? busyPen = express ? null : PenInOpenOrder(pens);

        int fastingDays = 0, first = State.Day, last = State.Day;
        if (!express)
        {
            fastingDays = EstimatedFastingDays(pens);
            int slot = State.Day + Math.Max(route.LeadDays, fastingDays);
            (first, last) = EstimateSlaughterDays(route, slot, lot.HogKg / 1000.0);
        }

        return new HarvestQuote
        {
            Ok = busyPen == null,
            ErrorKey = busyPen == null ? "" : "pen_in_order",
            Message = busyPen == null ? "" : $"Pen '{busyPen}' is already in a harvest order",
            HoldingId = holding.Id,
            Route = route.Kind,
            Mode = mode,
            Fish = lot.Fish,
            LiveKg = lot.LiveKg,
            HogKg = lot.HogKg,
            MeanLiveWeightKg = lot.MeanLiveWeightKg,
            WeightCv = lot.WeightCv,
            HogKgBySizeClass = lot.HogKgBySizeClassArray(),
            SuperiorShare = lot.SuperiorShare,
            OrdinaryShare = lot.OrdinaryShare,
            ProductionShare = lot.ProductionShare,
            SizeFactor = lot.SizeFactor,
            GradeFactor = lot.GradeFactor,
            ReferencePriceNok = spot,
            GrossRevenueNok = (decimal)lot.ValueKg * spot,
            TransportCostNok = hog * route.TransportCostPerKgHogNok,
            SlaughterCostNok = hog * route.SlaughterCostPerKgHogNok,
            SurchargeNok = express && route.NeedsWellboat ? hog * cal.ExpressSurchargePerKgHogNok : 0m,
            FastingDays = fastingDays,
            FirstSlaughterDay = first,
            LastSlaughterDay = last,
            DailyCapacityTonnesHog = route.DailyCapacityTonnesHog
        };
    }

    /// <summary>
    /// Order a planned harvest (S2-04): the chosen pens (all pens with fish when
    /// none are listed) stop being fed today; from the wellboat slot (at least
    /// <see cref="Calibration.HarvestCalibration.WellboatLeadDays"/> away, and
    /// not before the fast is done) the slaughterhouse takes
    /// <paramref name="fraction"/> of each pen's fish, batch by batch within its
    /// daily capacity, at each day's price. Fish left in a pen after a partial
    /// harvest are fed again when the order completes. Value = live kg planned,
    /// Value2 = gross sale at today's price, Id = the order id. Fails before any
    /// mutation: engine_not_ready / holding_not_found / invalid_fraction /
    /// pen_not_found / holding_empty / pen_in_order.
    /// </summary>
    public CommandResult OrderHarvest(string holdingId, IReadOnlyCollection<string>? penIds = null, double fraction = 1.0)
    {
        var quote = QuoteHarvest(holdingId, penIds, fraction);
        if (!quote.Ok)
            return CommandResult.Fail(quote.ErrorKey, quote.Message);
        var holding = FindHarvestHolding(holdingId)!;
        var pens = SelectHarvestPens(holding, penIds, out _, out _)!;

        State.HarvestOrdersPlaced++;
        var order = new HarvestOrder
        {
            Id = HarvestOrder.ComposeId(State.Day, holding.Id, State.HarvestOrdersPlaced),
            HoldingId = holding.Id,
            HoldingKind = holding.Kind,
            Route = holding.Route.Kind,
            Fraction = fraction,
            OrderedDay = State.Day,
            SlotDay = State.Day + Math.Max(holding.Route.LeadDays, quote.FastingDays),
            Sequence = State.HarvestOrdersPlaced,
            Status = HarvestOrderStatus.Fasting
        };
        foreach (var pen in pens)
            order.Pens.Add(new HarvestOrderPen
            {
                PopulationId = pen.Id,
                FishToHarvest = FishToHarvest(pen.TotalCount, fraction)
            });
        State.HarvestOrders.Add(order);
        StartFastingAhead(order, holding);

        string what = fraction >= 1.0 ? $"{quote.Fish:N0} fisk" : $"{quote.Fish:N0} fisk ({fraction:P0} av {pens.Count} merder)";
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Slakting bestilt",
            Category = EventCategory.Market,
            Description = SimText.Nb(
                $"Slakting bestilt på {holding.Label}: {what}, ca. {quote.HogKg:N0} kg sløyd. Brønnbåten kommer dag {order.SlotDay}; slakteriet tar {holding.Route.DailyCapacityTonnesHog:N0} tonn om dagen, og hver merd sultes i dagene før den slaktes"),
            BalanceChange = 0m
        });
        State.TrimEventLog();

        return CommandResult.Success(
            $"Harvest ordered at {holding.Id}: {quote.Fish:N0} fish, slot day {order.SlotDay}",
            quote.LiveKg, (double)quote.GrossRevenueNok, order.Id);
    }

    /// <summary>
    /// Call off an open harvest order: no more fish are slaughtered and the
    /// pens are fed again. Batches already slaughtered stay sold. Fails with
    /// order_not_found when no open order has <paramref name="orderId"/>.
    /// </summary>
    public CommandResult CancelHarvestOrder(string orderId)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var order = State.HarvestOrders.FirstOrDefault(o => o.Id == orderId && o.IsOpen);
        if (order == null)
            return CommandResult.Fail("order_not_found", $"No open harvest order '{orderId}'");

        order.Status = HarvestOrderStatus.Cancelled;
        var holding = FindHarvestHolding(order.HoldingId);
        if (holding != null)
            foreach (var orderPen in order.Pens)
            {
                var pen = holding.Pens.FirstOrDefault(p => p.Id == orderPen.PopulationId);
                if (pen != null) StopFasting(pen);
            }
        State.HarvestOrders.Remove(order);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Slakting avlyst",
            Category = EventCategory.Market,
            Description = SimText.Nb($"Slaktingen på {holding?.Label ?? order.HoldingId} er avlyst; fisken fôres igjen"),
            BalanceChange = 0m
        });
        return CommandResult.Success($"Harvest order {orderId} cancelled", order.FishHarvested, id: orderId);
    }

    /// <summary>
    /// Post-tick (S2-04): slaughter today's batches. Orders are served oldest
    /// slot first; from its slot day an order takes its pens in turn, each once
    /// it has fasted long enough, up to what is left of its capacity pool
    /// today; each batch is sold at today's price. Pens start fasting just
    /// ahead of their turn (<see cref="StartFastingAhead"/>), so a site harvested
    /// over two weeks is not starved for two weeks.
    /// </summary>
    internal void ProcessHarvestOrders()
    {
        if (State?.HarvestOrders == null || State.HarvestOrders.Count == 0) return;
        var capacityLeft = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var order in State.HarvestOrders.Where(o => o.IsOpen)
                     .OrderBy(o => o.SlotDay).ThenBy(o => o.Sequence).ToList())
        {
            var holding = FindHarvestHolding(order.HoldingId);
            if (holding == null)
            {
                order.Status = HarvestOrderStatus.Cancelled; // the holding is gone (sold, removed)
                continue;
            }
            var owing = ResolveOwingPens(order, holding);
            if (owing.Count > 0 && State.Day >= order.SlotDay)
            {
                var route = holding.Route;
                double left = capacityLeft.TryGetValue(route.CapacityPool, out var l) ? l : route.DailyCapacityTonnesHog;
                if (left > 1e-9)
                {
                    double soldTonnes = SlaughterBatch(order, holding, owing, left);
                    capacityLeft[route.CapacityPool] = left - soldTonnes;
                }
            }
            if (order.FishRemaining == 0)
                CompleteHarvestOrder(order, holding);
            else
                StartFastingAhead(order, holding);
        }
        State.HarvestOrders.RemoveAll(o => !o.IsOpen);
    }

    /// <summary>
    /// Start the fast of each pen of <paramref name="order"/> whose turn at the
    /// slaughterhouse comes within its fasting time (plus a day): the pens ahead
    /// of it, at the route's daily capacity, set when its turn is.
    /// </summary>
    private void StartFastingAhead(HarvestOrder order, HarvestHolding holding)
    {
        double capacityKg = Math.Max(1e-6, holding.Route.DailyCapacityTonnesHog * 1000.0);
        // The order's first slaughter day behind the orders queued ahead of it in its pool.
        var queue = SimulatePoolQueue(holding.Route, extra: null);
        int first = queue.TryGetValue(order.Id, out var days) ? days.First : order.SlotDay;
        double daysToSlot = Math.Max(0, first - State.Day);
        double hogAheadKg = 0.0;
        foreach (var o in ResolveOwingPens(order, holding))
        {
            double turnInDays = daysToSlot + hogAheadKg / capacityKg;
            if (!o.Pen.IsFasting && turnInDays <= EstimatedFastingDays(new[] { o.Pen }) + 1)
                StartFasting(o.Pen);
            int owed = Math.Min(o.OrderPen.FishRemaining, o.Pen.TotalCount);
            hogAheadKg += owed * o.Pen.GetTotalBiomassKg() / o.Pen.TotalCount * HarvestCalibration.HogYieldFraction;
        }
    }

    private readonly record struct OwingPen(HarvestOrderPen OrderPen, FishPopulation Pen);

    /// <summary>The order's pens that still owe fish and still hold some; the rest are marked done.</summary>
    private static List<OwingPen> ResolveOwingPens(HarvestOrder order, HarvestHolding holding)
    {
        var owing = new List<OwingPen>();
        foreach (var orderPen in order.Pens)
        {
            if (orderPen.FishRemaining <= 0) { orderPen.Done = true; continue; }
            var pen = holding.Pens.FirstOrDefault(p => p.Id == orderPen.PopulationId);
            if (pen == null || pen.TotalCount <= 0) { orderPen.Done = true; continue; }
            owing.Add(new OwingPen(orderPen, pen));
        }
        return owing;
    }

    /// <summary>
    /// Slaughter one batch of <paramref name="order"/> within
    /// <paramref name="capacityTonnesHog"/>: the pens in turn, each once its
    /// fast is complete (a pen still fasting is passed over today), until the
    /// capacity is used. Returns the gutted tonnes sold.
    /// </summary>
    private double SlaughterBatch(HarvestOrder order, HarvestHolding holding, List<OwingPen> owing, double capacityTonnesHog)
    {
        double yield = HarvestCalibration.HogYieldFraction;
        double roomKg = capacityTonnesHog * 1000.0;
        var lot = new HarvestLot(HarvestCalibration);
        foreach (var o in owing)
        {
            if (roomKg <= 0.0) break;
            if (!FastingComplete(o.Pen)) continue;
            int owed = Math.Min(o.OrderPen.FishRemaining, o.Pen.TotalCount);
            double hogPerFish = o.Pen.GetTotalBiomassKg() / o.Pen.TotalCount * yield;
            int take = hogPerFish > 0.0 ? (int)Math.Min(owed, Math.Floor(roomKg / hogPerFish)) : owed;
            if (take <= 0) break;
            double before = lot.HogKg;
            AddPenToLot(lot, o.Pen, take, holding, FishRemoval.Harvest);
            roomKg -= lot.HogKg - before;
            o.OrderPen.FishHarvested += take;
            if (o.OrderPen.FishRemaining <= 0 || o.Pen.TotalCount <= 0) o.OrderPen.Done = true;
        }
        if (lot.Fish <= 0) return 0.0;

        var report = SellLot(lot, holding, HarvestMode.Planned, order.Id);
        RemoveEmptyPens(holding);
        order.Status = HarvestOrderStatus.Harvesting;
        order.FishHarvested += lot.Fish;
        order.LiveKg += lot.LiveKg;
        order.HogKg += lot.HogKg;
        order.GrossRevenueNok += report.GrossRevenueNok;
        order.HarvestCostNok += report.HarvestCostNok;
        _dayNotes.Add(new DayNote
        {
            Kind = "harvest_batch",
            SiteId = holding.Id,
            Success = true,
            Message = report.Id,
            Value = lot.HogKg
        });
        return lot.HogKg / 1000.0;
    }

    /// <summary>Close a finished order: pens with fish left (a partial harvest) are fed again.</summary>
    private void CompleteHarvestOrder(HarvestOrder order, HarvestHolding holding)
    {
        order.Status = HarvestOrderStatus.Completed;
        order.CompletedDay = State.Day;
        foreach (var orderPen in order.Pens)
        {
            orderPen.Done = true;
            var pen = holding.Pens.FirstOrDefault(p => p.Id == orderPen.PopulationId);
            if (pen != null && pen.TotalCount > 0) StopFasting(pen);
        }
        if (order.FishHarvested > 0)
            State.EventLog.Add(new EventRecord
            {
                Day = State.Day,
                Name = "Slakting ferdig",
                Category = EventCategory.Market,
                Description = SimText.Nb(
                    $"Slaktingen på {holding.Label} er ferdig: {order.FishHarvested:N0} fisk, {order.HogKg:N0} kg sløyd, {order.GrossRevenueNok:N0} kr brutto, {order.HarvestCostNok:N0} kr slakting og transport"),
                BalanceChange = 0m
            });
        _dayNotes.Add(new DayNote
        {
            Kind = "harvest_completed",
            SiteId = holding.Id,
            Success = order.FishHarvested > 0,
            Message = order.Id,
            Value = order.HogKg
        });
    }

    /// <summary>The id of a pen in <paramref name="pens"/> that an open order already holds, or null.</summary>
    private string? PenInOpenOrder(IEnumerable<FishPopulation> pens)
    {
        var held = new HashSet<string>(State.HarvestOrders.Where(o => o.IsOpen)
            .SelectMany(o => o.Pens.Where(p => p.FishRemaining > 0).Select(p => p.PopulationId)), StringComparer.Ordinal);
        return pens.FirstOrDefault(p => held.Contains(p.Id))?.Id;
    }

    /// <summary>Days until the slowest of <paramref name="pens"/> has fasted long enough, at today's water.</summary>
    private int EstimatedFastingDays(IEnumerable<FishPopulation> pens)
    {
        var cal = HarvestCalibration;
        int days = 0;
        foreach (var pen in pens)
        {
            double fastedDays = pen.IsFasting ? State.Day - pen.FastingSinceDay : 0;
            double fastedDegreeDays = pen.IsFasting ? pen.FastingDegreeDays : 0.0;
            double temperature = Math.Max(0.5, HoldingWaterOf(pen).TemperatureC);
            double byDegrees = Math.Ceiling(Math.Max(0.0, cal.FastingDegreeDays - fastedDegreeDays) / temperature - 1e-9);
            double byDays = Math.Max(0.0, cal.MinFastingDays - fastedDays);
            days = Math.Max(days, (int)Math.Max(byDegrees, byDays));
        }
        return days;
    }


    /// <summary>
    /// First and last slaughter day of <paramref name="tonnesHog"/> gutted
    /// tonnes on <paramref name="route"/> from <paramref name="slotDay"/>, behind
    /// the open orders of the same capacity pool (at today's weights).
    /// </summary>
    private (int First, int Last) EstimateSlaughterDays(HarvestRoute route, int slotDay, double tonnesHog)
    {
        const string quoted = "\u0000quote";
        var days = SimulatePoolQueue(route, (quoted, slotDay, tonnesHog));
        return days.TryGetValue(quoted, out var d) ? d : (slotDay, slotDay);
    }

    /// <summary>
    /// The first and last slaughter day of every open order in
    /// <paramref name="route"/>'s capacity pool (plus <paramref name="extra"/>, a
    /// quoted harvest queued last), served oldest slot first within the daily
    /// capacity at today's weights, from the next slaughter day (tomorrow's
    /// post-tick) on. Pure: moves nothing.
    /// </summary>
    private Dictionary<string, (int First, int Last)> SimulatePoolQueue(HarvestRoute route,
        (string Id, int Slot, double Tonnes)? extra)
    {
        var queue = new List<(string Id, int Slot, double Tonnes)>();
        foreach (var order in State.HarvestOrders.Where(o => o.IsOpen).OrderBy(o => o.SlotDay).ThenBy(o => o.Sequence))
        {
            var holding = FindHarvestHolding(order.HoldingId);
            if (holding == null || holding.Route.CapacityPool != route.CapacityPool) continue;
            double owedKg = 0.0;
            foreach (var orderPen in order.Pens)
            {
                var pen = holding.Pens.FirstOrDefault(p => p.Id == orderPen.PopulationId);
                if (pen == null || pen.TotalCount <= 0 || orderPen.FishRemaining <= 0) continue;
                owedKg += Math.Min(orderPen.FishRemaining, pen.TotalCount) * pen.GetTotalBiomassKg() / pen.TotalCount;
            }
            queue.Add((order.Id, Math.Max(order.SlotDay, State.Day + 1), owedKg * HarvestCalibration.HogYieldFraction / 1000.0));
        }
        if (extra is { } e)
            queue.Add((e.Id, Math.Max(e.Slot, State.Day + 1), e.Tonnes));

        var result = new Dictionary<string, (int First, int Last)>(StringComparer.Ordinal);
        if (queue.Count == 0) return result;
        double capacity = Math.Max(1e-6, route.DailyCapacityTonnesHog);
        var left = queue.Select(q => q.Tonnes).ToArray();
        var first = Enumerable.Repeat(-1, queue.Count).ToArray();
        var last = queue.Select(q => q.Slot).ToArray();
        int start = queue.Min(q => q.Slot);
        for (int day = start; day < start + 3650 && left.Any(t => t > 1e-9); day++)
        {
            double room = capacity;
            for (int i = 0; i < queue.Count && room > 1e-9; i++)
            {
                if (queue[i].Slot > day || left[i] <= 1e-9) continue;
                double take = Math.Min(room, left[i]);
                left[i] -= take;
                room -= take;
                if (first[i] < 0) first[i] = day;
                last[i] = day;
            }
        }
        for (int i = 0; i < queue.Count; i++)
            result[queue[i].Id] = (first[i] < 0 ? queue[i].Slot : first[i], last[i]);
        return result;
    }
}
