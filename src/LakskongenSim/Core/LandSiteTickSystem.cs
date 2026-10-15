using LakskongenSim.Biology;

namespace LakskongenSim.Core;

/// <summary>
/// The land sites' daily tick (docs/FARM_TYPES_SPEC.md §3.3, F2-02: moved
/// verbatim out of <c>FarmOpexTickSystem</c> and <c>ProcessCarbonLedger</c>):
/// every land-based facility (today's <see cref="Farm.RASFacility"/> and
/// <see cref="Farm.PostSmoltFacility"/>, which F2-03 turns into one land site
/// kind) runs its day on its own <c>ras_facility</c> stream, adds its opex to
/// the day's facility opex and books its deaths and its carbon. The land lane
/// (F2-12 water and energy, F2-15 biology, F2-16 incidents) grows it here.
/// <para>
/// FarmOpex runs the phases in the pre-F2 order (land tanks, offshore farms,
/// post-smolt modules): the day's facility opex is one running decimal sum,
/// and a decimal sum rounds at 28-29 significant digits, so its order is part
/// of the result.
/// </para>
/// </summary>
internal static class LandSiteTickSystem
{
    /// <summary>Each land facility's tanks: its day on its own stream (water, oxygen, CO2, biofilter, energy), its opex.</summary>
    public static void TickTanks(SimulationEngine engine)
    {
        foreach (var rasFacility in engine.RASFacilities)
        {
            rasFacility.SetRng(engine.Rng.Stream(SimRngStreams.RASFacility,
                string.IsNullOrEmpty(rasFacility.Id) ? rasFacility.Name : rasFacility.Id));
            var rasResult = rasFacility.TickDay(engine.State.Day, engine.Weather.CurrentTemperatureC);
            engine._dailyFarmSystemsOpex += rasResult.TotalDailyOPEX;
        }
    }

    /// <summary>
    /// Each post-smolt module: its batches' day, its opex, and the batches'
    /// deaths. Post-smolt batches are not cohorts; their deaths (a top-down
    /// survival rate) are booked per facility (S2-05).
    /// </summary>
    public static void TickPostSmoltModules(SimulationEngine engine)
    {
        foreach (var postSmoltFacility in engine.PostSmoltFacilities)
        {
            var postSmoltResult = postSmoltFacility.TickDay(engine.State.Day);
            engine._dailyFarmSystemsOpex += postSmoltResult.TotalDailyOPEX;
            engine.MortalityLedger.BookHoldingDeaths(postSmoltFacility.Id, postSmoltResult.TotalMortality,
                postSmoltResult.TotalMortalityKg, MortalityCause.BaselineAnchor);
        }
    }

    /// <summary>
    /// The land facilities' carbon for the day (Phase 5.4): Scope 2 electricity
    /// of every operational facility at the grid factor, cut by research
    /// (<paramref name="energyEmissionFactor"/>). Called by the carbon ledger in
    /// its pre-F2 place (after the sea sites and the feed, before offshore).
    /// </summary>
    public static void AccountCarbon(SimulationEngine engine, double energyEmissionFactor, int day)
    {
        foreach (var ras in engine.RASFacilities)
        {
            if (!ras.IsOperational) continue;
            double elecCO2e = ras.DailyEnergyKWh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
            ras.CarbonTracker.AddEmission("Scope2_Electricity", ras.Id, elecCO2e, day);
        }
    }
}
