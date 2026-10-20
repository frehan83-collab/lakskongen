using System.Collections.Generic;
using LakskongenSim.Farm.Land;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// The land lane's additive state on a land site (roadmap F2-13 and F2-15,
    /// docs/FARM_TYPES_SPEC.md §3.2: "SalinityPpt (post-smolt) … Competence,
    /// CompetenceLevers, CompletedCycles, UtilisationCap, CohortPlan"). Every
    /// field has a default that is the site before F2-13/F2-15, so an older
    /// save loads unchanged.
    /// </summary>
    public partial class RASFacility
    {
        /// <summary>
        /// Salinity of the site's tank water (ppt, F2-13): full seawater, or a
        /// blend of river and fjord water on a pad with a river intake
        /// (<c>SetSalinity</c>, 12-35 ppt). Read by the tanks' water, the
        /// SuperSmolt seawater record, desmoltification and the H2S sulphate factor.
        /// </summary>
        public double SalinityPpt { get; set; } = Environment.HoldingWater.SeawaterSalinityPsu;

        /// <summary>Transfers of tank fish to sea the site has booked and the boat has not yet carried (F2-13).</summary>
        public List<LandTransferJob> Transfers { get; set; } = new();

        /// <summary>The competence levers pulled and the cycles completed (F2-15; grow-out sites).</summary>
        public LandCompetence Competence { get; set; } = new();

        /// <summary>
        /// Share of the appetite the site's feeding plan gives its tanks (F2-15,
        /// <c>SetLandFeedPlan</c>; 1 = to appetite).
        /// </summary>
        public double FeedRationFraction { get; set; } = 1.0;

        /// <summary>Harvests planned into ISO weeks (F2-15, <c>PlanLandHarvest</c>), oldest first.</summary>
        public List<LandHarvestPlan> HarvestPlans { get; set; } = new();

        /// <summary>Harvest plans made at this site: the sequence number in each plan's id.</summary>
        public int HarvestPlansMade { get; set; }

        /// <summary>The site's own books since it was built (F2-15).</summary>
        public LandSiteAccount Account { get; set; } = new();

        /// <summary>The account as it stood when the current sim year began.</summary>
        public LandSiteAccount AccountAtYearStart { get; set; } = new();

        /// <summary>The last complete sim year's window of the account (empty before the first year ends).</summary>
        public LandSiteAccount LastYear { get; set; } = new();
    }
}
