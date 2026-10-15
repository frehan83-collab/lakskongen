using System.Linq;
using LakskongenSim.Environment;

namespace LakskongenSim.Core;

/// <summary>
/// The offshore units' daily tick (docs/FARM_TYPES_SPEC.md §3.3, F2-02: moved
/// verbatim out of <c>FarmOpexTickSystem</c> and <c>ProcessCarbonLedger</c>):
/// every offshore farm runs its day on its own <c>offshore_farm</c> stream
/// (storm lockdown, structure, crew rotation), adds its opex to the day's
/// facility opex and books its carbon. The sea lane (F2-06 metocean, F2-07
/// operations and logistics, F2-08 incidents) grows it here. FarmOpex runs it
/// between the land tanks and the post-smolt modules
/// (<see cref="LandSiteTickSystem"/>: the opex sum's order).
/// </summary>
internal static class OffshoreTickSystem
{
    /// <summary>Each offshore farm's day on its own stream and its opex.</summary>
    public static void Tick(SimulationEngine engine)
    {
        foreach (var offshoreFarm in engine.OffshoreFarms)
        {
            offshoreFarm.SetRng(engine.Rng.Stream(SimRngStreams.OffshoreFarm,
                string.IsNullOrEmpty(offshoreFarm.Id) ? offshoreFarm.Name : offshoreFarm.Id));
            var offshoreResult = offshoreFarm.TickDay(engine.State.Day, engine.Weather.CurrentWindSpeedMs, engine.Weather.CurrentWaveHeight, engine.Weather.CurrentState == WeatherState.Storm);
            engine._dailyFarmSystemsOpex += offshoreResult.TotalDailyOPEX;
        }
    }

    /// <summary>
    /// The offshore farms' carbon for the day (Phase 5.4): generator fuel
    /// (Scope 1) and grid or shore power (Scope 2) from a base plus the standing
    /// biomass, cut by research (<paramref name="energyEmissionFactor"/>); the
    /// crew helicopter on rotation days and the supply vessel (Scope 3). Called
    /// by the carbon ledger in its pre-F2 place (after the land facilities).
    /// </summary>
    public static void AccountCarbon(SimulationEngine engine, double energyEmissionFactor, int day)
    {
        foreach (var off in engine.OffshoreFarms)
        {
            if (!off.IsOperational) continue;
            double biomassTonnes = off.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;

            // Scope 1: Generator fuel (higher than fjord sites)
            double fuelL = 200.0 + biomassTonnes * 1.0; // base + per tonne
            double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter * energyEmissionFactor;
            off.CarbonTracker.AddEmission("Scope1_Fuel", off.Id, fuelCO2e, day);

            // Scope 2: Grid/shore power (assume partial)
            double elecKwh = 500.0 + biomassTonnes * 2.0;
            double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
            off.CarbonTracker.AddEmission("Scope2_Electricity", off.Id, elecCO2e, day);

            // Scope 3: Crew rotation (helicopter) - only on rotation days
            if (off.DaysSinceCrewRotation == 0 && off.EmployeeCount > 0)
            {
                double heliCO2e = Farm.CarbonLedger.OffshoreCrewRotationEmissionsKgCO2e(off.EmployeeCount);
                off.CarbonTracker.AddEmission("Scope3_Transport", off.Id + "_crew", heliCO2e, day);
            }

            // Scope 3: Supply vessel (daily average)
            double vesselCO2e = Farm.CarbonLedger.OffshoreSupplyVesselEmissionsKgCO2e(3); // 3 trips/week
            off.CarbonTracker.AddEmission("Scope3_Transport", off.Id + "_vessel", vesselCO2e, day);
        }
    }
}
