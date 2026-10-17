using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>What an offshore unit must get done at sea before a deadline (F2-08).</summary>
    public enum HandlingRequirement
    {
        /// <summary>Nothing is due.</summary>
        None = 0,
        /// <summary>The lice count calls for a delousing.</summary>
        Delouse = 1,
        /// <summary>The fish are due for harvest.</summary>
        Harvest = 2,
        /// <summary>The standing biomass must come down under the MTB.</summary>
        MtbRelief = 3,
    }

    /// <summary>
    /// The offshore structure of an offshore <see cref="SeaSite"/> (roadmap
    /// F2-03; docs/FARM_TYPES_SPEC.md §3.1 decision 1 and §3.2): a sea site with
    /// <see cref="SiteKind.Offshore"/> carries one, and its zones are the site's
    /// <see cref="SeaSite.PhysicalPens"/> (3 for an OF1-class semi-submersible, 6
    /// for a Havfarm-class ship). Its fish are the site's populations, so they
    /// live inside the site lice model, wellboats, escapes, harvest and the
    /// sustainability score like any sea site's (the site-kind audit,
    /// <c>docs/run/evidence/f2_03_sitekind_audit.md</c>, decides each system).
    /// <para>
    /// Its carbon is its sea site's (<see cref="SeaSite.CarbonTracker"/>: the
    /// unit's fuel, power, crew transport and supply vessel, and the sludge
    /// credits), one tracker per sea site.
    /// </para>
    /// <para>
    /// Built from the live members of the pre-F2 <see cref="OffshoreFarm"/>
    /// (which is now a load-only record a save's migration reads,
    /// <c>SimulationEngine.FacilityMigration.cs</c>); the rename with migration
    /// is the rating, <see cref="DesignHsM"/> (the 100-year significant wave
    /// height a slot may reach) where the old class had a 12 m "max wave". The
    /// shell's new members are the lanes' (F2-06/F2-07 operations and
    /// logistics, F2-08 incidents and handling, F2-09 licences); F2-07/F2-08 add
    /// the sensor packages, teething defects and the active incident with their
    /// own types.
    /// </para>
    /// </summary>
    public sealed class OffshoreUnit
    {
        // ── Carried over from OffshoreFarm (same names and meaning) ──────────

        /// <summary>Structure class: SemiSubmersible = OF1-class, ShipShaped = Havfarm-class, Submersible = legacy only.</summary>
        public OffshoreType FarmType { get; set; } = OffshoreType.SemiSubmersible;

        /// <summary>Structural integrity 0-1: wave damage takes it down; a repair restores it.</summary>
        public double StructuralIntegrity { get; set; } = 1.0;

        /// <summary>Today's significant wave height at the unit (m).</summary>
        public double CurrentWaveHeight { get; set; }

        /// <summary>True on a day no vessel can stay alongside the unit: its plot's Hs above the wellboat-alongside limit (F2-07, 5.0 m [O:R37]); feeding goes on.</summary>
        public bool IsStormLockdown { get; set; }

        /// <summary>Days since the last crew change.</summary>
        public int DaysSinceCrewRotation { get; set; }

        /// <summary>Days between crew changes: 7 (9-on/12-off in 3 shifts [O:R3], derived); a pre-F2 farm's 14 is migrated.</summary>
        public int CrewRotationDays { get; set; } = DefaultCrewRotationDays;

        /// <summary>Capital cost paid for the unit (NOK).</summary>
        public decimal CAPEX { get; set; }

        /// <summary>Construction progress 0-1; the unit takes fish at 1.</summary>
        public double ConstructionProgress { get; set; }

        /// <summary>Days the construction takes.</summary>
        public int ConstructionDays { get; set; } = LegacyConstructionDays;

        /// <summary>Sim day the construction started, -1 when it has not.</summary>
        public int ConstructionStartDay { get; set; } = -1;

        /// <summary>True once the construction is complete.</summary>
        [JsonIgnore] public bool IsOperational => ConstructionProgress >= 1.0;

        /// <summary>Crew in rotation (12 [O:R13]).</summary>
        public int EmployeeCount { get; set; } = DefaultEmployeeCount;

        /// <summary>
        /// Oxygen (mg/L) the current-fed exchange through the open structure
        /// adds to the area's sea water at the fish (wave 2 sim-a-1; read by
        /// <c>OffshoreHooks.PenWater</c>).
        /// </summary>
        public double OxygenBoost { get; set; } = DefaultOxygenBoostMgPerL;

        // ── Renamed with migration ──────────────────────────────────────────

        /// <summary>
        /// The 100-year significant wave height (m) the unit is designed for: a
        /// unit may sit on a slot only if this (plus <see cref="DesignHsBonusM"/>)
        /// reaches the slot's Hs100 (spec §4). Was <c>OffshoreFarm.MaxWaveHeightM</c> = 12 m.
        /// </summary>
        public double DesignHsM { get; set; } = 5.5;

        // ── New (F2-03 shell; the sea lane fills them) ──────────────────────

        /// <summary>The company's share of the unit: 1, or 0.35 for a consortium unit [GB] (F2-07).</summary>
        public double EquityShare { get; set; } = 1.0;

        /// <summary>The construction project building the unit, "" when none (F2-04).</summary>
        public string ConstructionProjectId { get; set; } = "";

        /// <summary>Feed in the unit's silo (t) (F2-07).</summary>
        public double SiloTonnes { get; set; }

        /// <summary>Silo capacity (t): 700 [GB; OF1's silo capacity is unpublished] (F2-07).</summary>
        public double SiloCapacityTonnes { get; set; } = 700;

        /// <summary>Ensilage in the unit's tank (t) (F2-07).</summary>
        public double SilageTonnes { get; set; }

        /// <summary>Ensilage tank capacity (t): 120 [GB] (F2-07).</summary>
        public double SilageCapacityTonnes { get; set; } = 120;

        /// <summary>What is due at sea (F2-08).</summary>
        public HandlingRequirement Requirement { get; set; } = HandlingRequirement.None;

        /// <summary>Days the due handling has been overdue (F2-08).</summary>
        public int HandlingOverdueDays { get; set; }

        /// <summary>Hatch procedure level 0-1, raised by procedure upgrades (F2-08).</summary>
        public double HatchProcedureLevel { get; set; } = 0.6;

        /// <summary>Alarm load 0-1 (emergent, F2-08).</summary>
        public double AlarmLoad { get; set; } = 0.15;

        /// <summary>Sim day the shakedown ends, -1 when none was chosen (F2-07).</summary>
        public int ShakedownEndDay { get; set; } = -1;

        /// <summary>Sim day the structure's state is known as of (F2-08).</summary>
        public int StructureKnownAsOfDay { get; set; }

        /// <summary>Sim day the nets' state is known as of (F2-08).</summary>
        public int NetKnownAsOfDay { get; set; }

        /// <summary>Production cycles the unit has completed (the development-licence gate, F2-09).</summary>
        public int CompletedCycles { get; set; }

        /// <summary>The dispensation case that waives 200,000 fish per pen, "" when none (F2-07/F2-09).</summary>
        public string DispensationCaseId { get; set; } = "";

        /// <summary>Design Hs added by the "Forsterkning" reinforcement (m): +1.0 [GB] (F2-07).</summary>
        public double DesignHsBonusM { get; set; }

        // ── Class profiles (until F2-04's facilities calibration) ──────────

        /// <summary>Crew change interval (days): 9-on/12-off in 3 shifts [O:R3] (derived).</summary>
        public const int DefaultCrewRotationDays = 7;

        /// <summary>A pre-F2 offshore farm's crew change interval (days), migrated to <see cref="DefaultCrewRotationDays"/> (spec §3.8).</summary>
        public const int LegacyCrewRotationDays = 14;

        /// <summary>Crew in rotation on an OF1-class unit [O:R13].</summary>
        public const int DefaultEmployeeCount = 12;

        /// <summary>Oxygen the pre-F2 offshore farm's exchange added (mg/L; OffshoreFarm.OxygenBoost, wave 2).</summary>
        public const double DefaultOxygenBoostMgPerL = 1.5;

        /// <summary>Construction days of the pre-F2 offshore farm (≈ 3 years; F2-07 replaces it with the yard, voyage and mooring phases).</summary>
        public const int LegacyConstructionDays = 1095;

        /// <summary>
        /// Zones (pens) of a structure class (spec §3.1 and §3.8): an OF1-class
        /// semi-submersible has 3 [O:R1, O:R6], a Havfarm-class ship 6
        /// [O:R23, O:R25]; the legacy submersible concept is given 3.
        /// </summary>
        public static int ZonesFor(OffshoreType type) => type switch
        {
            OffshoreType.ShipShaped => 6,
            _ => 3,
        };

        /// <summary>
        /// Design Hs (m) of a structure class: OF1-class 5.5 [O:R6, O:R15],
        /// Havfarm-class 8.0 (spec §4); the legacy submersible concept is rated
        /// as OF1-class [GB].
        /// </summary>
        public static double DefaultDesignHsM(OffshoreType type) => type switch
        {
            OffshoreType.ShipShaped => 8.0,
            _ => 5.5,
        };

        /// <summary>
        /// The unit's MTB (t): OF1-class 6,240 (8 × 780 t [O:R4]), Havfarm-class
        /// 10,000 [O:R22, O:R23]; the legacy submersible concept as OF1-class [GB].
        /// The unit's locality MTB until F2-09 binds its development licences.
        /// </summary>
        public static double DefaultMtbTonnes(OffshoreType type) => type switch
        {
            OffshoreType.ShipShaped => 10_000.0,
            _ => 6_240.0,
        };

        /// <summary>
        /// Volume of the unit's zones together (m³): OF1-class 250,000 [O:R1, O:R6],
        /// Havfarm-class 6 × 69,000 [O:R23, O:R25]; the legacy submersible as OF1-class [GB].
        /// </summary>
        public static double DefaultVolumeM3(OffshoreType type) => type switch
        {
            OffshoreType.ShipShaped => 6 * 69_000.0,
            _ => 250_000.0,
        };

        /// <summary>
        /// Depth (m) of the equivalent collar a zone is drawn with in the pen
        /// views: OF1's 250,000 m³ over the Ø110 m ring's area (π × 55²) is 26.3 m
        /// (derived [O:R1, O:R6]). The game reads a zone's volume, never this.
        /// </summary>
        public const double ZoneEquivalentDepthM = 26.3;

        /// <summary>
        /// Capital cost of the pre-F2 offshore farm (NOK; OffshoreFarm.GetCAPEX):
        /// 700 M semi-submersible, 900 M ship, 1.2 bn submersible. The build verb
        /// is a test and scenario path (spec §3.1 decision 5); F2-04/F2-07 price
        /// the new build from the facilities calibration (720 M + 157 M, 1,000 M).
        /// </summary>
        public static decimal LegacyCapexNok(OffshoreType type) => type switch
        {
            OffshoreType.ShipShaped => 900_000_000m,
            OffshoreType.Submersible => 1_200_000_000m,
            _ => 700_000_000m,
        };

        /// <summary>
        /// The zones of a unit of <paramref name="type"/> at <paramref name="siteId"/>
        /// holding <paramref name="totalVolumeM3"/> together: <see cref="ZonesFor"/>
        /// pens of equal volume, numbered from 1, empty.
        /// </summary>
        public static List<Pen> BuildZones(string siteId, OffshoreType type, double totalVolumeM3)
        {
            if (string.IsNullOrEmpty(siteId)) throw new ArgumentException("A unit's zones need its site id", nameof(siteId));
            int zones = ZonesFor(type);
            double volume = Math.Max(0.0, totalVolumeM3) / zones;
            var geometry = new PenGeometry(PenGeometry.CircumferenceFor(volume, ZoneEquivalentDepthM, 0.0), ZoneEquivalentDepthM, 0.0);
            var pens = new List<Pen>(zones);
            for (int number = 1; number <= zones; number++)
            {
                var pen = Pen.Build(siteId, number, geometry);
                pen.VolumeM3 = volume; // the source volume, exactly
                pens.Add(pen);
            }
            return pens;
        }


        // ── F2-07: logistics, operations, teething, sensors (the unit's day is
        //    OffshoreTickSystem's; these are its saved state) ─────────────────

        /// <summary>The shakedown the player chose (days; -1 none chosen, 0 none wanted): first stocking waits this long after commissioning.</summary>
        public int ShakedownDays { get; set; } = -1;

        /// <summary>The day the unit was commissioned (its silo filled, its deck drawn); -1 before.</summary>
        public int CommissionedDay { get; set; } = -1;

        /// <summary>True for a unit built through the player's build verb: it draws a teething deck at commissioning (the legacy and scenario path does not).</summary>
        public bool HasTeethingDeck { get; set; }

        /// <summary>Days the crew change is overdue (past the rota's interval).</summary>
        public int CrewOverdueDays { get; set; }

        /// <summary>The sea day the unit last fed its fish from the silo (-1 never).</summary>
        public int LastFeedDay { get; set; } = -1;

        /// <summary>Feed taken from the silo on <see cref="LastFeedDay"/> (t).</summary>
        public double FeedTodayTonnes { get; set; }

        /// <summary>Feed taken on each of the last days, oldest first (t; the silo's days-of-feed figure).</summary>
        public List<double> RecentFeedTonnes { get; set; } = new();

        /// <summary>Days the unit held fish and could not feed them for an empty silo.</summary>
        public int SiloEmptyDays { get; set; }

        /// <summary>Kilos of dead fish already put in the ensilage tank (the mortality ledger's total at the unit).</summary>
        public double DeadKgCollected { get; set; }

        /// <summary>The unit's vessel operations, in booking order (done ones kept for the record).</summary>
        public List<OffshoreBooking> Bookings { get; set; } = new();

        /// <summary>Number of the next booking ("&lt;unit&gt;/ob_N").</summary>
        public int NextBookingNumber { get; set; } = 1;

        /// <summary>The day the current tow started (-1 when no tow runs).</summary>
        public int TowStartedDay { get; set; } = -1;

        /// <summary>The last day the unit shelters after a failed tow window (-1 when it does not).</summary>
        public int TowShelterUntilDay { get; set; } = -1;

        /// <summary>Tow windows tried.</summary>
        public int TowAttempts { get; set; }

        /// <summary>Tow windows that failed (each a shelter).</summary>
        public int TowFailures { get; set; }

        /// <summary>The teething deck (spec §3.3), in slot order.</summary>
        public List<TeethingDefect> TeethingDefects { get; set; } = new();

        /// <summary>The installed sensor packages.</summary>
        public List<SensorPackage> Sensors { get; set; } = new();

        /// <summary>Structural integrity as the player last knew it (0-1; live with the motion sensor package).</summary>
        public double StructureKnown { get; set; } = 1.0;

        /// <summary>Net integrity as the player last knew it (%; live with the subsea cameras).</summary>
        public double NetKnownPct { get; set; } = 100.0;

        /// <summary>The sea day of the unit's first stocking (-1 before): cycle 1's teething clock starts here.</summary>
        public int FirstStockedDay { get; set; } = -1;

        /// <summary>
        /// A consortium unit's open settlement with its partner (NOK): the
        /// partner's share of the lines its systems booked in full (feed, smolts,
        /// fees) less its share of the harvest's net, settled once a day.
        /// </summary>
        public decimal PartnerPendingNok { get; set; }

        /// <summary>The last sea day whose harvest reports the partner settlement has taken.</summary>
        public int PartnerSettledThroughDay { get; set; } = -1;

        /// <summary>True for a consortium unit (the company holds less than all of it).</summary>
        [JsonIgnore] public bool IsConsortium => EquityShare < 1.0;

        /// <summary>True when the unit stands commissioned (its logistics run).</summary>
        [JsonIgnore] public bool IsCommissioned => CommissionedDay >= 0;

        // ── The legacy build path (spec §3.1 decision 5) ─────────────────────

        /// <summary>Start the unit's construction on <paramref name="day"/>, paying <paramref name="capex"/> (the legacy verb: progress by days, no phases).</summary>
        public void StartConstruction(int day, decimal capex)
        {
            ConstructionStartDay = day;
            ConstructionProgress = 0.0;
            CAPEX = capex;
        }

        /// <summary>
        /// Advance a legacy unit's construction (no project): progress by elapsed
        /// days; true on the day it completes.
        /// </summary>
        public bool AdvanceLegacyConstruction(int day)
        {
            if (IsOperational || ConstructionStartDay < 0 || !string.IsNullOrEmpty(ConstructionProjectId)) return false;
            int elapsed = day - ConstructionStartDay;
            ConstructionProgress = Math.Min(1.0, (double)elapsed / Math.Max(1, ConstructionDays));
            return IsOperational;
        }
    }
}
