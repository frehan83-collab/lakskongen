using System;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Data;

namespace LakskongenSim.Core;

/// <summary>
/// Daily growth and feeding of every holding on the S2-03 growth law
/// (<see cref="TgcGrowthLaw"/>). The law takes each cohort's water
/// (<see cref="HoldingWater"/>, from <see cref="HoldingWaterOf(FishPopulation)"/>),
/// the holding's ration (<see cref="FishPopulation.Ration"/>) and its husbandry
/// as inputs, so a sea pen, an offshore pen and a land-based tank at a
/// controlled temperature grow, eat and spread in size by the same model.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// What a holding's water and husbandry do to its fish today, built by the
    /// daily biology tick from the holding (zone chemistry, site gills, density,
    /// containment, watch mode).
    /// </summary>
    /// <param name="Water">The water the fish live in.</param>
    /// <param name="AppetiteModifier">Dissolved-oxygen saturation × gill state (0-1): share of a healthy appetite.</param>
    /// <param name="DensityGrowthMultiplier">Stocking-density stress on the growth potential (1 = none).</param>
    /// <param name="ContainmentGrowthMultiplier">Containment rung's growth effect (1 = open net).</param>
    /// <param name="StockingDensityKgM3">Effective density the FCR model reads (kg/m³).</param>
    /// <param name="SpreadQuality">Feed-spreading precision (0-1).</param>
    internal readonly record struct HoldingHusbandry(
        HoldingWater Water,
        double AppetiteModifier,
        double DensityGrowthMultiplier,
        double ContainmentGrowthMultiplier,
        double StockingDensityKgM3,
        double SpreadQuality);

    /// <summary>
    /// Hours of light a holding in <paramref name="water"/> gets today: a land
    /// tank runs continuous light (Nofima CtrlAQUA 2023: 24 h light for post-smolt
    /// growth in RAS); a sea or offshore pen the natural day length at its
    /// production area's latitude (<see cref="ProductionAreas.LatitudeDeg"/>).
    /// </summary>
    public double DayLengthHoursOf(in HoldingWater water)
    {
        if (water.Kind == HoldingKind.LandTank) return TgcGrowthLaw.ContinuousLightHours;
        int area = ProductionAreas.HasClimatology(water.ProductionArea) ? water.ProductionArea : ProductionAreas.NationalReference;
        return DayLength.Hours(ProductionAreas.LatitudeDeg(area), SimCalendar.DayOfYear(State?.Day ?? 0));
    }

    /// <summary>
    /// Grow every cohort of <paramref name="population"/> one day on the growth
    /// law and book the feed they ate (<see cref="FeedSystem.FeedRation"/>).
    /// Ages and deaths are the caller's (<see cref="FishPopulation.AdvanceDays"/>).
    /// </summary>
    internal FeedResult GrowAndFeedOneDay(FishPopulation population, in HoldingHusbandry husbandry)
    {
        var law = Growth.Law;
        double temperature = husbandry.Water.TemperatureC;
        double ration = population.Ration?.EffectiveFraction ?? 1.0;
        double researchGrowth = 1.0 + GrowthRateBonus;
        double dayLength = DayLengthHoursOf(husbandry.Water);

        double grownKg = 0.0;
        double eatenKg = 0.0;
        for (int ci = 0; ci < population.Cohorts.Count; ci++)
        {
            var cohort = population.Cohorts[ci];
            var conditions = new GrowthConditions(
                temperature,
                ration,
                husbandry.AppetiteModifier,
                CohortGrowthMultiplier(cohort) * husbandry.ContainmentGrowthMultiplier * researchGrowth,
                husbandry.DensityGrowthMultiplier,
                husbandry.SpreadQuality,
                dayLength);
            double satiationFcr = Feed.SatiationFcr(temperature, cohort.Stage,
                law.FcrSizeFactor(cohort.MeanWeightKg), husbandry.StockingDensityKgM3,
                husbandry.AppetiteModifier, FCRReduction);

            var day = law.GrowOneDay(cohort.MeanWeightKg, cohort.WeightCv, cohort.Stage, conditions, satiationFcr);
            cohort.MeanWeightKg = day.WeightKg;
            cohort.WeightCv = day.WeightCv;
            grownKg += cohort.Count * Math.Max(0.0, day.GrowthKg);
            eatenKg += cohort.Count * day.EatenFeedKg;
        }

        double appetite = law.ThermalAppetiteIndex(temperature) * husbandry.AppetiteModifier;
        return Feed.FeedRation(population, eatenKg, grownKg, husbandry.SpreadQuality, FeedWasteReduction, appetite);
    }

    /// <summary>
    /// Genetic and smolt-quality growth of a cohort: the genome's growth
    /// multiplier, inbreeding depression (Wave 6: zero inbreeding = 1.0) and the
    /// post-smolt quality carryover (P5).
    /// </summary>
    private static double CohortGrowthMultiplier(FishPopulation.Cohort cohort)
    {
        double genetic = cohort.Genetics?.GetGrowthMultiplier() ?? 1.0;
        double inbreeding = 1.0;
        if (cohort.Genetics != null && cohort.Genetics.InbreedingCoefficient > 0.0)
        {
            inbreeding = 1.0 - GenomicSelection.InbreedingDepressionPenalty(
                GeneticTrait.GrowthRate, cohort.Genetics.InbreedingCoefficient);
        }
        double quality = SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier;
        return genetic * inbreeding * quality;
    }
}
