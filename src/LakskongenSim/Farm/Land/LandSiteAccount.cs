namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// A land site's own books (roadmap F2-15, the farming cost of spec §2A):
    /// what its fish cost by line and what they sold for, summed since the
    /// site was built. Every amount is also booked in the company's ledger
    /// (the site's lines are ring-fenced there, F2-04); this account only reads
    /// them per site, so the farming cost per kg HOG (ex depreciation) and the
    /// energy share can be shown and measured. A window is the difference of
    /// two snapshots (<see cref="Minus"/>).
    /// </summary>
    public sealed class LandSiteAccount
    {
        /// <summary>Sim day the snapshot was taken (the running account: the last day it was updated).</summary>
        public int Day { get; set; }

        /// <summary>Feed fed (NOK).</summary>
        public decimal FeedNok { get; set; }

        /// <summary>Smolts bought for the tanks (NOK).</summary>
        public decimal SmoltNok { get; set; }

        /// <summary>Electricity (NOK).</summary>
        public decimal EnergyNok { get; set; }

        /// <summary>Liquid oxygen (NOK).</summary>
        public decimal OxygenNok { get; set; }

        /// <summary>Operating staff (NOK).</summary>
        public decimal StaffNok { get; set; }

        /// <summary>Service, maintenance and repairs (NOK).</summary>
        public decimal MaintenanceNok { get; set; }

        /// <summary>Fish, building and equipment insurance (NOK).</summary>
        public decimal InsuranceNok { get; set; }

        /// <summary>Sludge, laboratory, chemicals, office (NOK).</summary>
        public decimal OtherNok { get; set; }

        /// <summary>Competence levers: operations manager, training, consultancy (NOK).</summary>
        public decimal CompetenceNok { get; set; }

        /// <summary>Slaughter, gutting, packing and transport of the harvests (NOK).</summary>
        public decimal HarvestNok { get; set; }

        /// <summary>Wellboat hire of transfers to sea (NOK).</summary>
        public decimal TransferNok { get; set; }

        /// <summary>Gross sales of the harvests (NOK).</summary>
        public decimal HarvestRevenueNok { get; set; }

        /// <summary>Post-smolt sold (NOK).</summary>
        public decimal PostSmoltRevenueNok { get; set; }

        /// <summary>Gutted kilos harvested.</summary>
        public double HarvestedHogKg { get; set; }

        /// <summary>Live kilos harvested.</summary>
        public double HarvestedLiveKg { get; set; }

        /// <summary>Fish harvested.</summary>
        public long HarvestedFish { get; set; }

        /// <summary>Live kilos of post-smolt sold.</summary>
        public double SoldLiveKg { get; set; }

        /// <summary>Fish moved to sea.</summary>
        public long TransferredFish { get; set; }

        /// <summary>Electricity on the meter (kWh).</summary>
        public double EnergyKWh { get; set; }

        /// <summary>Feed fed (kg).</summary>
        public double FeedKg { get; set; }

        /// <summary>
        /// The farming cost: every running cost of the fish, ex depreciation
        /// (feed, smolts, electricity, oxygen, staff, maintenance, insurance,
        /// the other lines, the competence levers and the harvest).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal FarmingCostNok => FeedNok + SmoltNok + EnergyNok + OxygenNok + StaffNok + MaintenanceNok
            + InsuranceNok + OtherNok + CompetenceNok + HarvestNok;

        /// <summary>Farming cost per kg HOG harvested (NOK/kg; 0 without a harvest).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double FarmingCostPerKgHog => HarvestedHogKg > 0.0 ? (double)FarmingCostNok / HarvestedHogKg : 0.0;

        /// <summary>Electricity's share of the farming cost (0-1).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double EnergyShare => FarmingCostNok > 0m ? (double)(EnergyNok / FarmingCostNok) : 0.0;

        /// <summary>A copy (a snapshot).</summary>
        public LandSiteAccount Clone() => (LandSiteAccount)MemberwiseClone();

        /// <summary>This account less <paramref name="earlier"/>: the window between the two snapshots.</summary>
        public LandSiteAccount Minus(LandSiteAccount earlier)
        {
            if (earlier == null) return Clone();
            return new LandSiteAccount
            {
                Day = Day,
                FeedNok = FeedNok - earlier.FeedNok,
                SmoltNok = SmoltNok - earlier.SmoltNok,
                EnergyNok = EnergyNok - earlier.EnergyNok,
                OxygenNok = OxygenNok - earlier.OxygenNok,
                StaffNok = StaffNok - earlier.StaffNok,
                MaintenanceNok = MaintenanceNok - earlier.MaintenanceNok,
                InsuranceNok = InsuranceNok - earlier.InsuranceNok,
                OtherNok = OtherNok - earlier.OtherNok,
                CompetenceNok = CompetenceNok - earlier.CompetenceNok,
                HarvestNok = HarvestNok - earlier.HarvestNok,
                TransferNok = TransferNok - earlier.TransferNok,
                HarvestRevenueNok = HarvestRevenueNok - earlier.HarvestRevenueNok,
                PostSmoltRevenueNok = PostSmoltRevenueNok - earlier.PostSmoltRevenueNok,
                HarvestedHogKg = HarvestedHogKg - earlier.HarvestedHogKg,
                HarvestedLiveKg = HarvestedLiveKg - earlier.HarvestedLiveKg,
                HarvestedFish = HarvestedFish - earlier.HarvestedFish,
                SoldLiveKg = SoldLiveKg - earlier.SoldLiveKg,
                TransferredFish = TransferredFish - earlier.TransferredFish,
                EnergyKWh = EnergyKWh - earlier.EnergyKWh,
                FeedKg = FeedKg - earlier.FeedKg,
            };
        }
    }
}
