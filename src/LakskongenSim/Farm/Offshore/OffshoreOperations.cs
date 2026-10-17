using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>Where a booked vessel operation stands (F2-07, spec §3.4 "BookOffshoreOperation").</summary>
    public enum OffshoreBookingStatus
    {
        /// <summary>Booked for a day still ahead: no charter yet.</summary>
        Booked = 0,
        /// <summary>The booked day has come and the sea ruled the operation out: the vessel waits, chartered.</summary>
        Waiting = 1,
        /// <summary>A fish-handling vessel alongside with its pumping paused (Hs above the handling limit, within the alongside limit).</summary>
        Paused = 2,
        /// <summary>A fish-handling vessel that left the unit (Hs above the alongside limit) and shelters, chartered.</summary>
        Sheltering = 3,
        /// <summary>Done on <see cref="OffshoreBooking.CompletedDay"/>.</summary>
        Done = 4,
        /// <summary>The window came but the operation could not do its work (no fish, a full zone); the charter of that day is paid.</summary>
        Failed = 5,
        /// <summary>Cancelled by the player before it was done (charter already paid stays paid).</summary>
        Cancelled = 6,
    }

    /// <summary>
    /// A vessel operation booked at an offshore unit (F2-07, spec §3.3 "Opex:
    /// charter paid from the booked day, including slipped days", §3.4). The
    /// vessel is chartered from <see cref="BookedDay"/> until the operation is
    /// done, failed or cancelled: on every day the unit's sea is within the
    /// operation's limit for the vessel it is done, otherwise it slips one day,
    /// charter paid. Saved on the unit.
    /// </summary>
    public sealed class OffshoreBooking
    {
        /// <summary>Deterministic id "&lt;unit&gt;/ob_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The operation.</summary>
        public OffshoreOp Op { get; set; }

        /// <summary>The vessel booked.</summary>
        public VesselClass Vessel { get; set; }

        /// <summary>The day the player (or the Driftsplan) issued the booking.</summary>
        public int IssuedDay { get; set; }

        /// <summary>The day the vessel is booked for: chartered from this day.</summary>
        public int BookedDay { get; set; }

        /// <summary>Where it stands.</summary>
        public OffshoreBookingStatus Status { get; set; } = OffshoreBookingStatus.Booked;

        /// <summary>The day it was done or failed (-1 while open).</summary>
        public int CompletedDay { get; set; } = -1;

        /// <summary>Days of charter paid so far.</summary>
        public int DaysCharged { get; set; }

        /// <summary>Charter paid so far (NOK, the company's share).</summary>
        public decimal ChargedNok { get; set; }

        /// <summary>What a stocking brings: smolts (0 for other operations).</summary>
        public int Count { get; set; }

        /// <summary>A stocking's strain or a delousing's method ("" for other operations).</summary>
        public string Detail { get; set; } = "";

        /// <summary>Why a failed operation could not do its work ("" otherwise).</summary>
        public string FailureKey { get; set; } = "";

        /// <summary>True while the booking still charters or waits for its day.</summary>
        [JsonIgnore] public bool IsOpen => Status is OffshoreBookingStatus.Booked or OffshoreBookingStatus.Waiting
            or OffshoreBookingStatus.Paused or OffshoreBookingStatus.Sheltering;
    }

    /// <summary>The kinds of teething defect of the OF1-class deck (spec §3.3 "Teething deck").</summary>
    public enum TeethingDefectKind
    {
        /// <summary>The harvest zone crushes fish (+3 pp mortality while it fires, O:R3).</summary>
        HarvestZoneCrush = 0,
        /// <summary>Cable-chain wear (O:R3): the structure fatigues twice as fast while it fires.</summary>
        CableChainWear = 1,
        /// <summary>The net system needs changing (O:R4): the nets wear twice as fast while it fires.</summary>
        NetSystemChange = 2,
        /// <summary>A ballast-pipe leak (O:R10): the tilt hazard ×3 while unfixed (F2-08).</summary>
        BallastPipeLeak = 3,
        /// <summary>"Biologien flytter ikke med": winter ulcers, +4-8 pp mortality while it fires.</summary>
        WinterUlcers = 4,
    }

    /// <summary>
    /// One teething defect of a new unit (spec §3.3): drawn at commissioning
    /// with its reveal day in the first cycle; a shakedown may reveal it
    /// earlier. Revealed and unfixed while the unit holds fish, it fires.
    /// </summary>
    public sealed class TeethingDefect
    {
        /// <summary>Id "&lt;unit&gt;/td_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>What is wrong.</summary>
        public TeethingDefectKind Kind { get; set; }

        /// <summary>The deck slot (1-4) whose daily uniform reveals it in a shakedown.</summary>
        public int Slot { get; set; }

        /// <summary>Days after the first stocking the defect shows itself if no shakedown found it.</summary>
        public int RevealOffsetDays { get; set; }

        /// <summary>Extra mortality over a cycle while it fires (percentage points; 0 for a defect that kills no fish).</summary>
        public double MortalityPp { get; set; }

        /// <summary>The day it was revealed (-1 while hidden).</summary>
        public int RevealedDay { get; set; } = -1;

        /// <summary>The day its fix started (-1 when none).</summary>
        public int FixStartedDay { get; set; } = -1;

        /// <summary>The day its fix is done (-1 when none started).</summary>
        public int FixedDay { get; set; } = -1;

        /// <summary>The first day it fired (revealed, unfixed, fish in the unit); -1 when it never did.</summary>
        public int FiredDay { get; set; } = -1;

        /// <summary>True once revealed.</summary>
        [JsonIgnore] public bool IsRevealed => RevealedDay >= 0;

        /// <summary>True once its fix is done on or before <paramref name="day"/>.</summary>
        public bool IsFixedBy(int day) => FixedDay >= 0 && FixedDay <= day;
    }

    /// <summary>The sensor packages an offshore unit can carry (spec §3.6 "Information quality").</summary>
    public enum SensorPackageKind
    {
        /// <summary>Motion reference unit: the structure is known continuously.</summary>
        Mru = 0,
        /// <summary>Subsea cameras: the nets are known continuously.</summary>
        SubseaCam = 1,
        /// <summary>Upward echo sounders: biomass known to ±3 %.</summary>
        Echo = 2,
        /// <summary>AIS and marine broadband to a shore control centre.</summary>
        AisBroadband = 3,
    }

    /// <summary>A sensor package installed on a unit, with its last service.</summary>
    public sealed class SensorPackage
    {
        /// <summary>The package.</summary>
        public SensorPackageKind Kind { get; set; }

        /// <summary>The day it was installed.</summary>
        public int InstalledDay { get; set; }

        /// <summary>The day it was last serviced (its install day before the first service).</summary>
        public int LastServiceDay { get; set; }

        /// <summary>True when its service is overdue on <paramref name="day"/>.</summary>
        public bool IsPastService(int day, int intervalDays) => day - LastServiceDay > intervalDays;
    }

    /// <summary>
    /// The price of a chartered vessel for an operation (F2-07): a service
    /// vessel or a DP walk-to-work vessel by the day for resupply, silage, crew
    /// changes and net work; a wellboat (or an offshore-class wellboat) by the
    /// day for fish handling; a helicopter per crew change, paid when it flies.
    /// Pure.
    /// </summary>
    public static class OffshoreCharter
    {
        /// <summary>
        /// The day rate (NOK, the whole vessel) of <paramref name="vessel"/> for
        /// <paramref name="op"/>; 0 for the helicopter (paid per change) and for
        /// feeding (the silo, no vessel).
        /// </summary>
        public static decimal DayRateNok(OffshoreOp op, VesselClass vessel, OffshoreCostsCalibration costs)
        {
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (op == OffshoreOp.Feeding || vessel == VesselClass.Helicopter) return 0m;
            if (OperabilityWindows.IsFishHandling(op))
                return vessel == VesselClass.OffshoreWellboat
                    ? costs.WellboatNokPerDay * (decimal)costs.OffshoreWellboatPremium
                    : costs.WellboatNokPerDay;
            if (vessel == VesselClass.DpWalkToWork) return costs.DpVesselNokPerDay;
            return op switch
            {
                OffshoreOp.CrewChange => costs.CrewBoatNokPerDay,  // a crew transfer boat
                OffshoreOp.NetInspection => 0m,                    // the ROV works from the unit's crane
                _ => costs.ServiceVesselNokPerDay,                 // resupply, silage: a service vessel
            };
        }

        /// <summary>
        /// The price paid when the operation is done (NOK): the helicopter's crew
        /// change, an ROV inspection from the unit; 0 for a chartered vessel.
        /// </summary>
        public static decimal PerOperationNok(OffshoreOp op, VesselClass vessel, OffshoreCostsCalibration costs)
        {
            if (vessel == VesselClass.Helicopter && op == OffshoreOp.CrewChange) return costs.HelicopterNokPerChange;
            if (op == OffshoreOp.NetInspection && vessel == VesselClass.Standard) return costs.RovInspectionNok;
            return 0m;
        }
    }

    /// <summary>
    /// What an offshore unit's state reads to the player (F2-07, spec §6.5
    /// gauges): today's sea, the silo and silage, the crew clock, the alarm
    /// load and its drivers, the next window of each operation, the open
    /// bookings, the shakedown and the teething deck, the sensors, the
    /// structure and nets as last known, and the partner's share.
    /// </summary>
    /// <param name="SiteId">The unit.</param>
    /// <param name="Operational">True once commissioned.</param>
    /// <param name="HsTodayM">Today's Hs at the plot (m).</param>
    /// <param name="AppetiteToday">The share of appetite today's sea leaves (spec §3.3).</param>
    /// <param name="SiloTonnes">Feed in the silo (t).</param>
    /// <param name="SiloCapacityTonnes">Silo capacity (t).</param>
    /// <param name="SiloDays">Days of feed in the silo at the recent feeding rate (+∞ while the unit feeds nothing).</param>
    /// <param name="SiloEmptyDays">Days the fish could not feed for an empty silo, since the unit was built.</param>
    /// <param name="SilageTonnes">Ensilage in the tank (t).</param>
    /// <param name="SilageCapacityTonnes">Ensilage tank capacity (t).</param>
    /// <param name="CrewDaysSinceChange">Days since the last crew change.</param>
    /// <param name="CrewChangeIntervalDays">Days between crew changes.</param>
    /// <param name="CrewOverdueDays">Days the crew change is overdue.</param>
    /// <param name="AlarmLoad">The alarm load 0-1.</param>
    /// <param name="AlarmDrivers">The alarm load's terms (base, defects, sensors, crew).</param>
    /// <param name="NextWindows">Each board row's first forecast day at P ≥ 0.8 (-1 when none in the horizon) and its probability.</param>
    /// <param name="Bookings">The unit's bookings, open ones first.</param>
    /// <param name="ShakedownEndDay">The day the shakedown ends (-1 when none chosen).</param>
    /// <param name="Defects">The teething deck as the player knows it (hidden defects counted, not named).</param>
    /// <param name="HiddenDefects">Defects not yet revealed.</param>
    /// <param name="Sensors">The installed sensor packages.</param>
    /// <param name="StructureKnown">Structural integrity as last known (0-1).</param>
    /// <param name="StructureKnownAsOfDay">The day it was last known.</param>
    /// <param name="NetKnownPct">Net integrity as last known (%).</param>
    /// <param name="NetKnownAsOfDay">The day it was last known.</param>
    /// <param name="EquityShare">The company's share (1, or 0.35 in a consortium).</param>
    public sealed record OffshoreUnitStateView(string SiteId, bool Operational, double HsTodayM, double AppetiteToday,
        double SiloTonnes, double SiloCapacityTonnes, double SiloDays, int SiloEmptyDays, double SilageTonnes,
        double SilageCapacityTonnes, int CrewDaysSinceChange, int CrewChangeIntervalDays, int CrewOverdueDays,
        double AlarmLoad, IReadOnlyList<(string Driver, double Value)> AlarmDrivers,
        IReadOnlyList<(string Row, int Day, double Probability)> NextWindows, IReadOnlyList<OffshoreBooking> Bookings,
        int ShakedownEndDay, IReadOnlyList<TeethingDefect> Defects, int HiddenDefects, IReadOnlyList<SensorPackage> Sensors,
        double StructureKnown, int StructureKnownAsOfDay, double NetKnownPct, int NetKnownAsOfDay, double EquityShare);
}
