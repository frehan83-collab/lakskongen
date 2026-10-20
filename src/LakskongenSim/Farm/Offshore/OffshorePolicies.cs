using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core
{
    /// <summary>The sea lane's Driftsplan policies (F2-07), added through F2-05's partial method.</summary>
    public static partial class DriftsplanPolicies
    {
        static partial void AddOffshorePolicies(List<IDriftsplanPolicy> policies) => OffshorePolicies.AddTo(policies);
    }
}

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>
    /// The offshore unit's standing orders (roadmap F2-07, docs/FARM_TYPES_SPEC.md
    /// §1 pillar 5 "every periodic task has a Driftsplan default", §6.5 "Driftsplan
    /// auto-booking: silo buffer target, crew rota, inspection cadence"): feed
    /// resupply into the first green window when the silo runs below a buffer,
    /// the crew rota with the helicopter as the fallback, ROV net inspection,
    /// ensilage collection and sensor service. All five are on by default for
    /// an offshore unit (<see cref="SimulationEngine.ApplyDriftsplanDefaults"/>
    /// when it is built or commissioned). Each issues the player's own verb
    /// and never reads more than the player sees: the silo, the crew clock,
    /// the forecast.
    /// </summary>
    public static class OffshorePolicies
    {
        /// <summary>Kind of <see cref="ResupplyPolicy"/>.</summary>
        public const string Resupply = "offshore_resupply";

        /// <summary>Kind of <see cref="CrewRotaPolicy"/>.</summary>
        public const string CrewRota = "offshore_crew_rota";

        /// <summary>Kind of <see cref="NetInspectionPolicy"/>.</summary>
        public const string NetInspection = "offshore_net_inspection";

        /// <summary>Kind of <see cref="SilagePolicy"/>.</summary>
        public const string Silage = "offshore_silage";

        /// <summary>Kind of <see cref="SensorServicePolicy"/>.</summary>
        public const string SensorService = "offshore_sensor_service";

        /// <summary>A job that waits for the unit to be commissioned.</summary>
        public const string TriggerUnitCommissioned = "unit_commissioned";

        /// <summary>A job that waits for the silo to run below the order's days of feed.</summary>
        public const string TriggerSiloBelow = "silo_below";

        /// <summary>A job that waits for the vessel already booked.</summary>
        public const string TriggerVesselBooked = "vessel_booked";

        /// <summary>A job that waits for the ensilage tank to fill to the order's share.</summary>
        public const string TriggerSilageAbove = "silage_above";

        /// <summary>A job that waits for a sensor package to install.</summary>
        public const string TriggerNoSensors = "no_sensors";

        /// <summary>Vessel choice ids (the policies' "vessel" setting).</summary>
        public const string VesselStandard = "standard";
        public const string VesselDp = "dp_walk_to_work";

        /// <summary>Kind of <see cref="HandlingPolicy"/> (F2-08).</summary>
        public const string Handling = "offshore_handling";

        /// <summary>A job that waits for a handling requirement to open (F2-08).</summary>
        public const string TriggerRequirementOpen = "requirement_open";

        /// <summary>A job that waits for the S2-04 harvest order already placed at the unit (F2-08).</summary>
        public const string TriggerHarvestOrdered = "harvest_ordered";

        /// <summary>A delousing that waits for the water to reach its treatment's minimum temperature (F2-08).</summary>
        public const string TriggerWaterTooCold = "water_too_cold";

        /// <summary>Fish-handling vessel choice ids (the handling policy's "handling_vessel" setting).</summary>
        public const string VesselOffshoreWellboat = "offshore_wellboat";

        internal static void AddTo(List<IDriftsplanPolicy> policies)
        {
            policies.Add(new ResupplyPolicy());
            policies.Add(new CrewRotaPolicy());
            policies.Add(new NetInspectionPolicy());
            policies.Add(new SilagePolicy());
            policies.Add(new SensorServicePolicy());
            policies.Add(new HandlingPolicy());
        }

        internal static readonly IReadOnlyList<DriftsplanUnitKind> Units = new[] { DriftsplanUnitKind.OffshoreUnit };

        /// <summary>The commissioned offshore unit with <paramref name="unitId"/>, or null.</summary>
        internal static SeaSite? Commissioned(SimulationEngine engine, string unitId)
        {
            var site = engine.FindOffshoreUnit(unitId);
            return site?.Offshore is { IsCommissioned: true } ? site : null;
        }

        internal static DriftsplanJob Waiting(string policy, string unitId, string verb, string command, string trigger, double value = 0.0) => new()
        {
            Policy = policy,
            UnitId = unitId,
            Day = -1,
            Verb = verb,
            Command = command,
            Trigger = trigger,
            TriggerValue = value,
        };

        /// <summary>The vessel of a "vessel" choice.</summary>
        internal static VesselClass VesselOf(string choice) => choice == VesselDp ? VesselClass.DpWalkToWork : VesselClass.Standard;

        /// <summary>The probability setting every booking policy shares (spec §6.5: P ≥ 0.8 is a green window).</summary>
        internal static DriftsplanSetting MinProbabilitySetting() => new()
        {
            Key = "min_probability",
            Min = 0.5,
            Max = 0.95,
            Default = SimulationEngine.GreenWindowProbability,
            Unit = "share",
            Source = "GB: spec §6.5, a cell with P ≥ 0.8 is a solid green window",
        };

        /// <summary>The vessel setting of a booking policy (a standard service vessel or a DP walk-to-work vessel at about twice the day rate).</summary>
        internal static DriftsplanSetting VesselSetting() => new()
        {
            Key = "vessel",
            Kind = DriftsplanSettingKind.Choice,
            Choices = new[] { "standard", "dp_walk_to_work" },
            DefaultChoice = VesselStandard,
            Source = "GB: the standard service vessel at its calibrated day rate; DP reaches 3.0 m at about twice the price (spec §3.3)",
        };

        internal static DriftsplanJob BookGreenWindow(string policy, string unitId, int day, OffshoreOp op, VesselClass vessel, double minProbability)
        {
            string verb = "book_first_green_window";
            string id = unitId;
            return new DriftsplanJob
            {
                Policy = policy,
                UnitId = unitId,
                Day = day,
                Verb = verb,
                Command = $"{verb} {unitId} {op.ToString().ToLowerInvariant()} {vessel.ToString().ToLowerInvariant()}",
                Issue = engine => engine.BookFirstGreenWindow(id, op, vessel, minProbability),
            };
        }
    }

    /// <summary>
    /// "Fôrpåfyll": when the silo holds fewer than <c>silo_days</c> days of feed at
    /// the recent rate, book a feed run into the first forecast day at P ≥
    /// <c>min_probability</c> (spec §3.3 "the Driftsplan default books it at P ≥ 0.8
    /// when the silo falls below 10 days").
    /// </summary>
    public sealed class ResupplyPolicy : IDriftsplanPolicy
    {
        /// <summary>
        /// Days of feed that trigger a resupply: 10 (3-30). GB: spec §3.3; the
        /// longest January spell without a window at off_1 is a median 7 days
        /// and a p90 of 10 (F2-06 finding), so 10 days of buffer outlasts most.
        /// </summary>
        public static readonly DriftsplanSetting SiloDays = new()
        {
            Key = "silo_days",
            Min = 3,
            Max = 30,
            Default = 10,
            Unit = "days",
            Source = "GB: spec §3.3 (book below 10 days of feed); off_1's longest winter spell without a window is a median 7 and a p90 10 days (F2-06 finding)",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { SiloDays, OffshorePolicies.MinProbabilitySetting(), OffshorePolicies.VesselSetting() };

        public string Kind => OffshorePolicies.Resupply;
        public string Title => "fôrpåfyll";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => kind == DriftsplanUnitKind.OffshoreUnit;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} resupply";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            double threshold = context.Number(SiloDays.Key);
            if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.Resupply))
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerVesselBooked);
            if (SimulationEngine.SiloDaysOf(unit) >= threshold)
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerSiloBelow, threshold);
            return OffshorePolicies.BookGreenWindow(Kind, site.Id, context.Today, OffshoreOp.Resupply,
                OffshorePolicies.VesselOf(context.Choice("vessel")), context.Number("min_probability"));
        }
    }

    /// <summary>
    /// "Mannskapsbytte": book the crew change into the first green window from
    /// the day it is due (every <c>interval_days</c>); when the change is
    /// <c>helicopter_after_days</c> overdue, call the helicopter for tomorrow
    /// (spec §3.3 "the helicopter is the fallback").
    /// </summary>
    public sealed class CrewRotaPolicy : IDriftsplanPolicy
    {
        /// <summary>Days between crew changes: 7 (3-21), the 9-on/12-off rota in 3 shifts [O:R3] (derived).</summary>
        public static readonly DriftsplanSetting IntervalDays = new()
        {
            Key = "interval_days",
            Min = 3,
            Max = 21,
            Default = 7,
            WholeNumber = true,
            Unit = "days",
            Source = "O:R3: OF1's 9-on/12-off rota in 3 shifts, a change a week (derived)",
        };

        /// <summary>Days overdue before the helicopter flies: 2 (0-14).</summary>
        public static readonly DriftsplanSetting HelicopterAfterDays = new()
        {
            Key = "helicopter_after_days",
            Min = 0,
            Max = 14,
            Default = 2,
            WholeNumber = true,
            Unit = "days",
            Source = "GB: spec §3.3, the helicopter is the fallback (150,000 NOK a change, wind under 25 m/s, visibility 1.5 km)",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { IntervalDays, HelicopterAfterDays, OffshorePolicies.MinProbabilitySetting(), OffshorePolicies.VesselSetting() };

        public string Kind => OffshorePolicies.CrewRota;
        public string Title => "mannskapsbytte";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => kind == DriftsplanUnitKind.OffshoreUnit;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} crewchange";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            string id = site.Id;
            var open = unit.Bookings.Where(b => b.IsOpen && b.Op == OffshoreOp.CrewChange).ToList();
            int overdue = unit.DaysSinceCrewRotation - (int)Math.Round(context.Number(IntervalDays.Key));
            if (open.Count > 0)
            {
                if (overdue >= (int)Math.Round(context.Number(HelicopterAfterDays.Key)) && open.All(b => b.Vessel != VesselClass.Helicopter))
                {
                    const string heli = "book_offshore_operation";
                    int tomorrow = context.Today + 1;
                    return new DriftsplanJob
                    {
                        Policy = Kind,
                        UnitId = id,
                        Day = context.Today,
                        Verb = heli,
                        Command = $"{heli} {id} crewchange helicopter {tomorrow}",
                        Issue = engine => engine.BookOffshoreOperation(id, OffshoreOp.CrewChange, tomorrow, VesselClass.Helicopter),
                    };
                }
                return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerVesselBooked);
            }
            int interval = (int)Math.Round(context.Number(IntervalDays.Key));
            int due = context.Today + Math.Max(0, interval - unit.DaysSinceCrewRotation);
            int issue = Math.Max(context.Today, due - 1);
            return OffshorePolicies.BookGreenWindow(Kind, id, issue, OffshoreOp.CrewChange,
                OffshorePolicies.VesselOf(context.Choice("vessel")), context.Number("min_probability"));
        }
    }

    /// <summary>
    /// "Notinspeksjon": book the ROV net inspection (from the unit's crane, 3.0 m)
    /// into the first green window every <c>interval_days</c>; the nets and the
    /// structure are then known as of that day.
    /// </summary>
    public sealed class NetInspectionPolicy : IDriftsplanPolicy
    {
        /// <summary>
        /// Days between inspections: 30 (7-90). GB: nets are cleaned and
        /// inspected weekly to monthly by ROV (O:R37, Menon 131/2023 §2.5.4);
        /// spec §3.3 counts an inspection overdue after 90 days.
        /// </summary>
        public static readonly DriftsplanSetting IntervalDays = new()
        {
            Key = "interval_days",
            Min = 7,
            Max = 90,
            Default = 30,
            WholeNumber = true,
            Unit = "days",
            Source = "O:R37 §2.5.4: nets are cleaned and inspected by ROV weekly to monthly; monthly here",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { IntervalDays, OffshorePolicies.MinProbabilitySetting() };

        public string Kind => OffshorePolicies.NetInspection;
        public string Title => "notinspeksjon";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => kind == DriftsplanUnitKind.OffshoreUnit;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} netinspection";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.NetInspection))
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerVesselBooked);
            int interval = (int)Math.Round(context.Number(IntervalDays.Key));
            int issue = Math.Max(context.Today, unit.NetKnownAsOfDay + interval - 1);
            return OffshorePolicies.BookGreenWindow(Kind, site.Id, issue, OffshoreOp.NetInspection, VesselClass.Standard,
                context.Number("min_probability"));
        }
    }

    /// <summary>"Ensilasje": when the ensilage tank is <c>fill_share</c> full, book its collection into the first green window.</summary>
    public sealed class SilagePolicy : IDriftsplanPolicy
    {
        /// <summary>Share of the tank that triggers a collection: 0.7 (0.3-0.95).</summary>
        public static readonly DriftsplanSetting FillShare = new()
        {
            Key = "fill_share",
            Min = 0.3,
            Max = 0.95,
            Default = 0.7,
            Unit = "share",
            Source = "GB: room for a bad week's dead fish after the order is booked",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { FillShare, OffshorePolicies.MinProbabilitySetting(), OffshorePolicies.VesselSetting() };

        public string Kind => OffshorePolicies.Silage;
        public string Title => "ensilasjehenting";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => kind == DriftsplanUnitKind.OffshoreUnit;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} silage";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.Silage))
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerVesselBooked);
            double level = context.Number(FillShare.Key);
            if (unit.SilageCapacityTonnes <= 0.0 || unit.SilageTonnes < level * unit.SilageCapacityTonnes)
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerSilageAbove, level);
            return OffshorePolicies.BookGreenWindow(Kind, site.Id, context.Today, OffshoreOp.Silage,
                OffshorePolicies.VesselOf(context.Choice("vessel")), context.Number("min_probability"));
        }
    }

    /// <summary>"Sensorservice": service the sensor packages <c>lead_days</c> before their service falls due (the crew on board, no vessel).</summary>
    public sealed class SensorServicePolicy : IDriftsplanPolicy
    {
        /// <summary>Days ahead of the due date a service is done: 14 (0-60).</summary>
        public static readonly DriftsplanSetting LeadDays = new()
        {
            Key = "lead_days",
            Min = 0,
            Max = 60,
            Default = 14,
            WholeNumber = true,
            Unit = "days",
            Source = "GB: two weeks ahead keeps every package inside its service and off the alarm load (spec §3.3)",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings = new[] { LeadDays };

        public string Kind => OffshorePolicies.SensorService;
        public string Title => "sensorservice";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => kind == DriftsplanUnitKind.OffshoreUnit;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "service_sensors";
            string command = $"{verb} {context.UnitId}";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            if (unit.Sensors.Count == 0)
                return OffshorePolicies.Waiting(Kind, site.Id, verb, command, OffshorePolicies.TriggerNoSensors);
            int lead = (int)Math.Round(context.Number(LeadDays.Key));
            int interval = context.Engine.OffshoreCal.Sensors.ServiceIntervalDays;
            int due = unit.Sensors.Min(s => s.LastServiceDay) + interval;
            string id = site.Id;
            return new DriftsplanJob
            {
                Policy = Kind,
                UnitId = id,
                Day = Math.Max(context.Today, due - lead),
                Verb = verb,
                Command = command,
                Issue = engine => engine.ServiceSensors(id, lead),
            };
        }
    }

    /// <summary>
    /// "Behandle ved krav" (F2-08, spec §3.3 "Handling requirement", §6.6; the
    /// policy F2-08 acceptance 5 measures): when the unit stands under a
    /// handling requirement, book what answers it into the first window at P ≥
    /// <c>min_probability</c>: a delousing with the chosen fish-handling vessel
    /// (a standard wellboat, 2.0 m, or an offshore-class wellboat, 2.5 m at 1.5×
    /// the day rate), a planned harvest of the heaviest zone for MTB relief, the
    /// ROV for an overdue net inspection; a harvest order already placed waits
    /// for its wellboat. Off by default, as the fjord's treatment order is
    /// (F2-05): the player decides to hand the exception to a standing order.
    /// Reads only the gauges the player sees and never draws.
    /// </summary>
    public sealed class HandlingPolicy : IDriftsplanPolicy
    {
        /// <summary>The fish-handling vessel: a standard wellboat or an offshore-class wellboat (2.5 m, 1.5× the day rate).</summary>
        public static readonly DriftsplanSetting HandlingVessel = new()
        {
            Key = "handling_vessel",
            Kind = DriftsplanSettingKind.Choice,
            Choices = new[] { OffshorePolicies.VesselStandard, OffshorePolicies.VesselOffshoreWellboat },
            DefaultChoice = OffshorePolicies.VesselStandard,
            Source = "O:R37: a wellboat handles fish to 2.0 m; GB: an offshore-class wellboat to 2.5 m at 1.5x the day rate (spec §3.3)",
        };

        private static readonly IReadOnlyList<DriftsplanSetting> AllSettings =
            new[] { HandlingVessel, OffshorePolicies.MinProbabilitySetting() };

        public string Kind => OffshorePolicies.Handling;
        public string Title => "behandling ved krav";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => OffshorePolicies.Units;
        public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
        public bool IsDefaultFor(DriftsplanUnitKind kind) => false;

        /// <summary>The treatment the policy books: the wellboat's pressure-water treatment (the booking verb's default).</summary>
        internal const LakskongenSim.Data.DelousingMethod DelousingMethod = LakskongenSim.Data.DelousingMethod.Hydrolicer;

        /// <summary>Its name as the booking verb takes it.</summary>
        internal const string DelousingMethodName = "hydrolicer";

        /// <summary>The fish-handling vessel of a "handling_vessel" choice.</summary>
        internal static VesselClass HandlingVesselOf(string choice)
            => choice == OffshorePolicies.VesselOffshoreWellboat ? VesselClass.OffshoreWellboat : VesselClass.Standard;

        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            const string verb = "book_first_green_window";
            string command = $"{verb} {context.UnitId} handling";
            var site = OffshorePolicies.Commissioned(context.Engine, context.UnitId);
            if (site == null)
                return OffshorePolicies.Waiting(Kind, context.UnitId, verb, command, OffshorePolicies.TriggerUnitCommissioned);
            var unit = site.Offshore!;
            string id = site.Id;
            double p = context.Number("min_probability");
            switch (unit.Requirement)
            {
                case HandlingRequirement.Delouse:
                    if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.Delousing))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerVesselBooked);
                    // The wellboat's mechanical treatment works from its minimum water temperature
                    // (a booking in colder water would only fail on its window day): wait for it.
                    if (context.Engine.SeaTemperatureAtSite(site) < DelousingSystem.GetProfile(DelousingMethod).MinTemperatureC)
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerWaterTooCold);
                    var vessel = HandlingVesselOf(context.Choice(HandlingVessel.Key));
                    return new DriftsplanJob
                    {
                        Policy = Kind,
                        UnitId = id,
                        Day = context.Today,
                        Verb = verb,
                        Command = $"{verb} {id} delousing {vessel.ToString().ToLowerInvariant()}",
                        Issue = engine => engine.BookFirstGreenWindow(id, OffshoreOp.Delousing, vessel, p, detail: DelousingMethodName),
                    };
                case HandlingRequirement.MtbRelief:
                    if (context.Engine.HarvestOrders.Any(o => o.HoldingId == id))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerHarvestOrdered);
                    string? heaviest = site.PhysicalPens
                        .Select(pen => (Pen: pen, Pop: site.PopulationIn(pen)))
                        .Where(z => z.Pop != null && z.Pop.TotalCount > 0)
                        .OrderByDescending(z => z.Pop!.GetTotalBiomassKg()).ThenBy(z => z.Pen.Number)
                        .Select(z => z.Pen.Id).FirstOrDefault();
                    if (heaviest == null)
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerRequirementOpen);
                    const string harvestVerb = "order_harvest";
                    string zone = heaviest;
                    return new DriftsplanJob
                    {
                        Policy = Kind,
                        UnitId = id,
                        Day = context.Today,
                        Verb = harvestVerb,
                        Command = $"{harvestVerb} {id} {zone}",
                        Issue = engine => engine.OrderHarvest(id, new[] { zone }),
                    };
                case HandlingRequirement.Harvest:
                    return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerHarvestOrdered);
                case HandlingRequirement.NetInspection:
                    if (unit.Bookings.Any(b => b.IsOpen && b.Op == OffshoreOp.NetInspection))
                        return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerVesselBooked);
                    return OffshorePolicies.BookGreenWindow(Kind, id, context.Today, OffshoreOp.NetInspection, VesselClass.Standard, p);
                default:
                    return OffshorePolicies.Waiting(Kind, id, verb, command, OffshorePolicies.TriggerRequirementOpen);
            }
        }
    }
}
