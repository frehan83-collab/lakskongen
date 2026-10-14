using System.Linq;
using LakskongenSim.Biology;

namespace LakskongenSim.Core;

/// <summary>
/// Mortality by cause (S2-05, roadmap D4). The engine opens each simulated
/// day on the <see cref="MortalityLedger"/>, resolves each population's
/// environment from whatever holds it (<see cref="ResolveCohortEnvironment"/>)
/// and applies the environment hazards (<see cref="MortalityHazards"/>);
/// every death is booked through <see cref="FishPopulation.RemoveDead"/>.
/// <para>
/// Farm-type seam: a hazard reads only the <see cref="CohortEnvironment"/>.
/// Land-based tanks pass their own controlled water (a RAS tank at 12 °C
/// takes no cold-sea stress; a pump or power failure that lets its water
/// drift would), offshore structures pass the sea plus their exposure.
/// New farm types extend <see cref="ResolveCohortEnvironment"/> and add a
/// <see cref="MortalityCause"/>; the ledger, the probe and the bridge pick
/// the cause up without change.
/// </para>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Every death and live removal, by cause, per site and per day (S2-05).</summary>
    public MortalityLedger MortalityLedger => Mortality.Ledger;

    /// <summary>
    /// The mortality breakdown the bridge shows (the P&amp;L story's "where did
    /// the fish go"): one site since the game started when
    /// <paramref name="siteId"/> is given, else the company over the last
    /// <paramref name="lastDays"/> days (at most <see cref="MortalityLedger.HistoryDays"/>)
    /// when it is positive, else the company since the game started.
    /// </summary>
    public MortalityBreakdown GetMortalityBreakdown(string? siteId = null, int lastDays = 0)
    {
        if (!string.IsNullOrEmpty(siteId)) return MortalityLedger.ForSite(siteId);
        if (lastDays > 0)
            return MortalityLedger.ForDays(State.Day - System.Math.Min(lastDays, MortalityLedger.HistoryDays) + 1, State.Day);
        return MortalityLedger.Cumulative();
    }

    /// <summary>
    /// Open the next simulated day on the ledger (called at the start of each
    /// step of <see cref="Advance"/>): bookings during the step carry the day
    /// the player sees after it and are valued at today's spot price.
    /// </summary>
    internal void BeginMortalityDay()
        => Mortality?.Ledger.BeginDay(State.Day + 1, MarketSimulation?.PricePerKg ?? _balancing.BaseSalmonPricePerKg);

    /// <summary>
    /// The water and exposure <paramref name="population"/> lives in today,
    /// from whatever holds it: a RAS facility's tank (its controlled water
    /// temperature, oxygen and CO2), an offshore farm's pen (the sea, the
    /// zone's oxygen plus the farm's current-fed boost, the farm's wave
    /// height) or a sea site's pen (the sea and its zone's water).
    /// <paramref name="seaTemperatureC"/> overrides the sea temperature (by
    /// default the population's own production-area water,
    /// <see cref="HoldingWaterOf(FishPopulation)"/>); it never changes a
    /// land-based tank's water.
    /// </summary>
    public CohortEnvironment ResolveCohortEnvironment(FishPopulation population, double? seaTemperatureC = null)
    {
        // The sea of the population's own production area (S2-02) unless the
        // caller pins one temperature for every holding.
        double seaTemp = seaTemperatureC ?? HoldingWaterOf(population).TemperatureC;

        foreach (var ras in RASFacilities)
            if (ras.Tanks.Contains(population))
                return new CohortEnvironment(FishHolding.LandBasedTank, ras.Id,
                    ras.CurrentWaterTemp, ras.OxygenLevel, ras.CO2Level, WaveHeightM: 0.0);

        double zoneOxygen = State.Zones.FirstOrDefault(z => z.Id == population.ZoneId)?.WaterChemistry?.OxygenO2
            ?? double.NaN;

        foreach (var farm in OffshoreFarms)
            if (farm.Pens.Contains(population))
                return new CohortEnvironment(FishHolding.OffshoreStructure, farm.Id,
                    seaTemp, zoneOxygen + farm.OxygenBoost, double.NaN, farm.CurrentWaveHeight);

        return new CohortEnvironment(FishHolding.SeaPen, population.SiteId,
            seaTemp, zoneOxygen, double.NaN, Weather?.CurrentWaveHeight ?? 0.0);
    }

    /// <summary>
    /// Water temperature outside the stress-free band (4-16 °C) kills
    /// 0.005 % of the fish per °C of deviation per day
    /// (<see cref="Calibration.MortalityCalibration.TemperatureStressDailyFractionPerDegreeC"/>,
    /// booked as <see cref="MortalityCause.Temperature"/>). Each population is
    /// judged at its own water (<see cref="ResolveCohortEnvironment"/>) with
    /// <paramref name="seaTempC"/> as the sea temperature: sea pens and
    /// offshore pens take it, land-based RAS tanks keep their controlled
    /// water and are spared while it stays in the band (they joined
    /// State.Populations with facility production and used to lose ~10 fish
    /// a day per 100,000 in a 2 °C winter sea).
    /// </summary>
    internal void ApplySeaTemperatureStress(double seaTempC)
    {
        foreach (var population in State.Populations)
            ApplyTemperatureStressHazard(population, ResolveCohortEnvironment(population, seaTempC));
    }

    /// <summary>
    /// One population's temperature-stress deaths for a day in
    /// <paramref name="environment"/>, booked as <see cref="MortalityCause.Temperature"/>.
    /// The daily tick reaches it through <see cref="ApplyHoldingWaterTemperatureStress"/>
    /// (each population in its own production area's water, S2-02).
    /// </summary>
    internal void ApplyTemperatureStressHazard(FishPopulation population, CohortEnvironment environment)
    {
        double fraction = MortalityHazards.TemperatureStressFraction(environment, _calibration.Mortality);
        if (fraction <= 0.0) return;
        foreach (var cohort in population.Cohorts)
            population.RemoveDead(cohort, MortalityHazards.Deaths(cohort.Count, fraction), MortalityCause.Temperature);
    }
}
