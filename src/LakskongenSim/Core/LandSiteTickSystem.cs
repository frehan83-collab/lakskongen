using LakskongenSim.Biology;

namespace LakskongenSim.Core;

/// <summary>
/// The land sites' daily tick (docs/FARM_TYPES_SPEC.md §3.3, F2-02: moved
/// verbatim out of <c>FarmOpexTickSystem</c> and <c>ProcessCarbonLedger</c>):
/// every land site (a <see cref="Farm.RASFacility"/>, grow-out or post-smolt
/// since F2-03) runs its day on its own <c>ras_facility</c> stream, adds its
/// opex to the day's facility opex and books its carbon; its fish are the
/// game's populations, so the biology grows them and the ledger books their
/// deaths. The land lane (F2-12 water and energy, F2-15 biology, F2-16
/// incidents) grows it here.
/// <para>
/// FarmOpex runs the phases in the order land sites, offshore units, then
/// <see cref="TickPostSmoltModules"/> (the pre-F2 post-smolt list, which F2-03's
/// normalization at the start of every step leaves empty): the day's facility
/// opex is one running decimal sum, and a decimal sum rounds at 28-29
/// significant digits, so its order is part of the result.
/// </para>
/// </summary>
internal static class LandSiteTickSystem
{
    /// <summary>Each land facility's tanks: its day on its own stream (water, oxygen, CO2, biofilter, energy), its opex.</summary>
    public static void TickTanks(SimulationEngine engine)
    {
        foreach (var rasFacility in engine.RASFacilities)
        {
            var stream = engine.Rng.Stream(SimRngStreams.RASFacility,
                string.IsNullOrEmpty(rasFacility.Id) ? rasFacility.Name : rasFacility.Id);
            rasFacility.SetRng(stream);
            if (rasFacility.HasLots)
            {
                // F2-12: a site with lots runs the land physics (water, heat,
                // oxygen and electricity per lot) and books its own opex lines.
                engine.TickLandSiteDay(rasFacility, stream);
                continue;
            }
            var rasResult = rasFacility.TickDay(engine.State.Day, engine.Weather.CurrentTemperatureC);
            engine._dailyFarmSystemsOpex += rasResult.TotalDailyOPEX;
            engine.AttributeUnitCost(rasFacility.Id, Environment.HoldingKind.LandTank, rasResult.TotalDailyOPEX); // F2-04 ring-fence
        }
    }

    /// <summary>
    /// Each pre-F2 post-smolt facility: its batches' day, its opex, and the
    /// batches' deaths (a top-down survival rate booked per facility, S2-05).
    /// Since F2-03 a post-smolt site is a land site ticked with the others and
    /// this list is empty in every step (<see cref="SimulationEngine.MigrateFacilities"/>
    /// runs before it); the call stays in FarmOpex, which F2-02 froze.
    /// </summary>
    public static void TickPostSmoltModules(SimulationEngine engine)
    {
        foreach (var postSmoltFacility in engine.PostSmoltFacilities)
        {
            var postSmoltResult = postSmoltFacility.TickDay(engine.State.Day);
            engine._dailyFarmSystemsOpex += postSmoltResult.TotalDailyOPEX;
            engine.AttributeUnitCost(postSmoltFacility.Id, Environment.HoldingKind.LandTank, postSmoltResult.TotalDailyOPEX); // F2-04 ring-fence
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
            // F2-12 (spec §3.6): the liquid oxygen an F2 site buys was made off
            // its meter, Scope 3 at the same grid factor.
            if (ras.HasLots && ras.DailyLoxUpstreamKWh > 0.0)
                ras.CarbonTracker.AddEmission("Scope3_Oxygen", ras.Id,
                    ras.DailyLoxUpstreamKWh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor, day);
        }
    }
}
