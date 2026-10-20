namespace LakskongenSim.Farm
{
    /// <summary>
    /// What a land site (<see cref="RASFacility"/>) produces (roadmap F2-03,
    /// docs/FARM_TYPES_SPEC.md §3.1 decision 1: everything on land is a
    /// <see cref="RASFacility"/>; a pre-F2 post-smolt facility becomes one with
    /// the post-smolt template). The lot templates (HFS lot, RAS hall,
    /// post-smolt module) are the land lane's (F2-12); this is the site's role.
    /// </summary>
    public enum LandSiteKind
    {
        /// <summary>Grow-out to harvest (HFS or RAS lots; every pre-F2 RAS facility).</summary>
        GrowOut = 0,
        /// <summary>Post-smolt modules: smolts grown on land for the company's sea sites.</summary>
        PostSmolt = 1,
    }
}
