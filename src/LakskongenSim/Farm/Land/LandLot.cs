using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm.Land
{
    /// <summary>The land units a site is built from (spec §3.2).</summary>
    public enum LandTemplate
    {
        /// <summary>A full hybrid flow-through lot: 12 grow-out + 2 pre-grow-out tanks.</summary>
        HfsLot = 0,
        /// <summary>The HFS pilot lot: 4 grow-out + 1 pre-grow-out tank.</summary>
        HfsPilotLot = 1,
        /// <summary>A RAS hall (stage 3).</summary>
        RasHall = 2,
        /// <summary>A post-smolt module: 2 seawater RAS tanks.</summary>
        PostSmoltModule = 3,
    }

    /// <summary>
    /// One unit of a land site (roadmap F2-12, spec §3.2): an HFS lot, a RAS
    /// hall or a post-smolt module with its tanks, its heat pumps and its
    /// biofilter. Lots are what a site expands by (F2-14); each runs its own
    /// water loop and treatment, so the land tick solves one lot at a time.
    /// </summary>
    public sealed class LandLot
    {
        /// <summary>Lot id: "&lt;site&gt;/lot1".</summary>
        public string Id { get; init; } = "";

        /// <summary>Which template the lot was built from.</summary>
        public LandTemplate Template { get; init; }

        /// <summary>The lot's tanks, grow-out first, numbered across the site.</summary>
        public List<LandTank> Tanks { get; set; } = new();

        /// <summary>Electrical input capacity of the lot's heat pumps (MW).</summary>
        public double HeatPumpMWe { get; set; }

        /// <summary>True once heat recovery from the outlet water is installed (spec §3.4 InstallHeatRecovery).</summary>
        public bool HeatRecovery { get; set; }

        /// <summary>The lot's biofilter (<see cref="BiofilterType.None"/> on an HFS lot).</summary>
        public BiofilterType Biofilter { get; set; } = BiofilterType.None;

        /// <summary>Biofilter maturity: share of its capacity a young filter reaches (0-1).</summary>
        public double BiofilterMaturity { get; set; } = 1.0;

        /// <summary>Water volume of all the lot's tanks (m³).</summary>
        public double VolumeM3
        {
            get
            {
                double v = 0.0;
                foreach (var tank in Tanks) v += tank.VolumeM3;
                return v;
            }
        }

        /// <summary>The id of lot <paramref name="index"/> (1-based) at <paramref name="siteId"/>.</summary>
        public static string ComposeId(string siteId, int index) => $"{siteId}/lot{index}";
    }
}
