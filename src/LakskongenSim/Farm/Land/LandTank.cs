using System;

namespace LakskongenSim.Farm.Land
{
    /// <summary>What a land tank is for (spec §3.2).</summary>
    public enum TankRole
    {
        /// <summary>A post-smolt tank of a post-smolt module.</summary>
        PostSmolt = 0,
        /// <summary>A smaller pre-grow-out tank of an HFS lot.</summary>
        PreGrowOut = 1,
        /// <summary>A grow-out tank.</summary>
        GrowOut = 2,
    }

    /// <summary>What nitrifies a unit's ammonia (spec §3.2; F2-16 adds the H2S effect of the type).</summary>
    public enum BiofilterType
    {
        /// <summary>No biofilter: ammonia leaves with the new water (hybrid flow-through, [L:R7]).</summary>
        None = 0,
        /// <summary>A moving-bed bioreactor.</summary>
        MovingBed = 1,
        /// <summary>A fixed-bed biofilter.</summary>
        FixedBed = 2,
    }

    /// <summary>
    /// One round tank of a land unit (roadmap F2-12, docs/FARM_TYPES_SPEC.md
    /// §3.2). It wraps S2-01b's <see cref="Farm.Pen"/> for its id, volume and
    /// occupancy, so the density stress, the stocking check and the harvest
    /// verbs read a land tank like a sea pen; the water fields are the land
    /// tick's daily result (<see cref="LandLotDaySolver"/>), and
    /// <see cref="AppetiteFactor"/> is what the biology reads the next day.
    /// </summary>
    public sealed class LandTank
    {
        /// <summary>
        /// The tank as a production unit: id <c>"&lt;site&gt;/tNN"</c>, number,
        /// water volume (<see cref="Pen.VolumeM3"/>) and the population in it.
        /// The collar circumference is the tank's circumference and the wall
        /// depth its water depth, so a renderer reads it like a pen.
        /// </summary>
        public Pen Pen { get; init; } = new();

        /// <summary>What the tank is for.</summary>
        public TankRole Role { get; init; } = TankRole.GrowOut;

        /// <summary>Inside diameter (m).</summary>
        public double DiameterM { get; init; }

        /// <summary>Water depth (m).</summary>
        public double WaterDepthM { get; init; }

        /// <summary>
        /// Biological zone the tank's water belongs to: its own id in an HFS lot
        /// (each tank is a separate zone, [L:R1]), the lot's id where tanks share
        /// one loop (RAS hall, post-smolt module).
        /// </summary>
        public string BioZoneId { get; init; } = "";

        /// <summary>Dissolved oxygen at the tank outlet (mg/L).</summary>
        public double DoMgL { get; set; } = 9.0;

        /// <summary>Dissolved CO2 (mg/L).</summary>
        public double Co2MgL { get; set; } = 6.0;

        /// <summary>Total ammonia nitrogen (mg TAN-N/L).</summary>
        public double TanMgL { get; set; } = 0.2;

        /// <summary>Nitrate nitrogen (mg NO3-N/L).</summary>
        public double No3NMgL { get; set; } = 5.0;

        /// <summary>Water temperature (°C).</summary>
        public double TemperatureC { get; set; } = 12.0;

        /// <summary>
        /// Share of a healthy appetite the tank's CO2 and TAN leave the fish
        /// (0-1): written by the land tick, read by the biology the next day
        /// (spec §3.2). Oxygen's effect on appetite stays with the engine's
        /// dissolved-oxygen path (<see cref="Environment.HoldingWater.OxygenMgPerL"/>).
        /// </summary>
        public double AppetiteFactor { get; set; } = 1.0;

        // ─── The fish in the tank (F2-13, F2-15; reset when the tank empties) ───

        /// <summary>The strain the tank's fish were stocked as ("" when empty).</summary>
        public string StrainId { get; set; } = "";

        /// <summary>Day the tank's fish were stocked (-1 when empty).</summary>
        public int StockedDay { get; set; } = -1;

        /// <summary>
        /// Days in a row the fish have stood in at least the SuperSmolt salinity
        /// (F2-13: the seawater record a SuperSmolt needs).
        /// </summary>
        public int SeawaterDays { get; set; }

        /// <summary>Days in a row the fish have stood under the desmoltification salinity (F2-13).</summary>
        public int LowSalinityDays { get; set; }

        /// <summary>The fish's plan (F2-15: the growth they should make, and how far behind they are); null when empty.</summary>
        public LandCohortPlan? Plan { get; set; }

        /// <summary>Forget the fish (the tank emptied): their record and plan go with them.</summary>
        public void ClearFishRecord()
        {
            StrainId = "";
            StockedDay = -1;
            SeawaterDays = 0;
            LowSalinityDays = 0;
            Plan = null;
        }

        /// <summary>Take <paramref name="other"/>'s fish record (grading moves the fish with their record and plan).</summary>
        public void TakeFishRecordOf(LandTank other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            StrainId = other.StrainId;
            StockedDay = other.StockedDay;
            SeawaterDays = other.SeawaterDays;
            LowSalinityDays = other.LowSalinityDays;
            Plan = other.Plan?.Clone();
        }

        /// <summary>Water volume (m³): the pen's.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public double VolumeM3 => Pen.VolumeM3;

        /// <summary>Tank id: the pen's.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public string Id => Pen.Id;

        /// <summary>The id of tank <paramref name="number"/> at <paramref name="siteId"/>: "ras_1/t07".</summary>
        public static string ComposeId(string siteId, int number) => $"{siteId}/t{number:D2}";

        /// <summary>Water volume (m³) of a round tank: π (D/2)² × depth.</summary>
        public static double VolumeOf(double diameterM, double waterDepthM)
        {
            double d = Math.Max(0.0, diameterM);
            return Math.PI * d * d / 4.0 * Math.Max(0.0, waterDepthM);
        }

        /// <summary>
        /// A tank of <paramref name="diameterM"/> and <paramref name="waterDepthM"/>
        /// numbered <paramref name="number"/> at <paramref name="siteId"/>, empty,
        /// in bio-zone <paramref name="bioZoneId"/> (its own id when empty).
        /// </summary>
        public static LandTank Build(string siteId, int number, TankRole role, double diameterM, double waterDepthM,
            string bioZoneId = "")
        {
            string id = ComposeId(siteId, number);
            var pen = new Pen
            {
                Id = id,
                Number = number,
                CircumferenceM = Math.PI * Math.Max(0.0, diameterM),
                NetWallDepthM = Math.Max(0.0, waterDepthM),
                ConeDepthM = 0.0,
                VolumeM3 = VolumeOf(diameterM, waterDepthM),
            };
            return new LandTank
            {
                Pen = pen,
                Role = role,
                DiameterM = diameterM,
                WaterDepthM = waterDepthM,
                BioZoneId = string.IsNullOrEmpty(bioZoneId) ? id : bioZoneId,
            };
        }
    }
}
