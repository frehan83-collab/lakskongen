using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The offshore units' daily tick (docs/FARM_TYPES_SPEC.md §3.3; F2-02 moved it
/// out of <c>FarmOpexTickSystem</c> and <c>ProcessCarbonLedger</c>, F2-03 runs
/// it on the offshore sea sites): the open sea's phase right after the
/// weather (F2-06), and every offshore unit's day in FarmOpex (F2-07,
/// <see cref="SimulationEngine.RunOffshoreUnitDay"/>: construction and the tow,
/// crew, bookings and charter, silo and silage, structure and nets, the
/// teething deck, sensors, the alarm load, its own lines at its equity share,
/// the partner's settlement), then its carbon on its sea site's tracker.
/// F2-08 adds the incidents here. FarmOpex runs the units between the land
/// sites and the (empty) legacy post-smolt list (<see cref="LandSiteTickSystem"/>).
/// </summary>
internal static class OffshoreTickSystem
{
    /// <summary>
    /// The Metocean phase (F2-06, right after the weather): the game's open sea
    /// advances to the weather's day (8 uniforms from <c>offshore_metocean</c>)
    /// and every offshore unit takes its plot's Hs, which the biology's
    /// appetite, the hazards and the unit's own day read. Nothing happens in a
    /// game that never had an offshore unit.
    /// </summary>
    public static void AdvanceSea(SimulationEngine engine)
    {
        engine.AdvanceOffshoreSea();
        engine.CommissionReadyLegacyUnits(); // a legacy or migrated unit fills its silo before its fish eat
    }

    /// <summary>
    /// Each offshore unit's day (in the site list's order, F2-07): 8 uniforms
    /// from <c>offshore_farm/&lt;unit&gt;</c> every day, whatever happens, then
    /// its construction, logistics and lines (booked through the ledger with
    /// their own categories, not the day's facility opex sum).
    /// </summary>
    public static void Tick(SimulationEngine engine)
    {
        engine.RunDevelopmentLicenceDay(); // F2-09: the round, awards, conversion cases (every game; draws no game stream)
        foreach (var site in engine.State.SeaSites.ToList())
        {
            if (site.Kind != SiteKind.Offshore || site.Offshore == null) continue;
            engine.RunOffshoreUnitDay(site);
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

            // Scope 1: generator fuel, the litres the unit's energy line pays for (F2-07)
            double fuelL = engine.OffshoreFuelLitresToday(site);
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
