using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The offshore units' daily tick (docs/FARM_TYPES_SPEC.md §3.3; F2-02 moved it
/// out of <c>FarmOpexTickSystem</c> and <c>ProcessCarbonLedger</c>, F2-03 runs
/// it on the offshore sea sites): every offshore unit runs its day
/// (<see cref="Farm.Offshore.OffshoreUnit.TickDay"/>: construction, storm
/// lockdown, structure, crew rotation), adds its opex to the day's facility
/// opex and books its carbon on its sea site's tracker. The sea lane (F2-06
/// metocean, F2-07 operations and logistics, F2-08 incidents) grows it here.
/// FarmOpex runs it between the land sites and the (empty) legacy post-smolt
/// list (<see cref="LandSiteTickSystem"/>: the opex sum's order).
/// </summary>
internal static class OffshoreTickSystem
{
    /// <summary>Each offshore unit's day (in the site list's order) and its opex. Draws no random number.</summary>
    public static void Tick(SimulationEngine engine)
    {
        bool storm = engine.Weather.CurrentState == WeatherState.Storm;
        foreach (var site in engine.State.SeaSites)
        {
            if (site.Kind != SiteKind.Offshore || site.Offshore == null) continue;
            var result = site.Offshore.TickDay(engine.State.Day, engine.Weather.CurrentWaveHeight, storm, site.MABTonnes);
            engine._dailyFarmSystemsOpex += result.TotalDailyOPEX;
        }
    }

    /// <summary>
    /// The offshore units' carbon for the day (Phase 5.4): generator fuel
    /// (Scope 1) and grid or shore power (Scope 2) from a base plus the standing
    /// biomass, cut by research (<paramref name="energyEmissionFactor"/>); the
    /// crew helicopter on rotation days and the supply vessel (Scope 3). Called
    /// by the carbon ledger in its pre-F2 place (after the land sites), on the
    /// unit's sea site's tracker; the fjord-site emissions leave offshore units
    /// out (these are theirs).
    /// </summary>
    public static void AccountCarbon(SimulationEngine engine, double energyEmissionFactor, int day)
    {
        foreach (var site in engine.State.SeaSites)
        {
            var off = site.Offshore;
            if (site.Kind != SiteKind.Offshore || off == null || !off.IsOperational) continue;
            double biomassTonnes = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;

            // Scope 1: Generator fuel (higher than fjord sites)
            double fuelL = 200.0 + biomassTonnes * 1.0; // base + per tonne
            double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter * energyEmissionFactor;
            site.CarbonTracker.AddEmission("Scope1_Fuel", site.Id, fuelCO2e, day);

            // Scope 2: Grid/shore power (assume partial)
            double elecKwh = 500.0 + biomassTonnes * 2.0;
            double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
            site.CarbonTracker.AddEmission("Scope2_Electricity", site.Id, elecCO2e, day);

            // Scope 3: Crew rotation (helicopter) - only on rotation days
            if (off.DaysSinceCrewRotation == 0 && off.EmployeeCount > 0)
            {
                double heliCO2e = Farm.CarbonLedger.OffshoreCrewRotationEmissionsKgCO2e(off.EmployeeCount);
                site.CarbonTracker.AddEmission("Scope3_Transport", site.Id + "_crew", heliCO2e, day);
            }

            // Scope 3: Supply vessel (daily average)
            double vesselCO2e = Farm.CarbonLedger.OffshoreSupplyVesselEmissionsKgCO2e(3); // 3 trips/week
            site.CarbonTracker.AddEmission("Scope3_Transport", site.Id + "_vessel", vesselCO2e, day);
        }
    }
}
