using System;
using System.Collections.Generic;
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
    /// <param name="Recipe">What the holding's feed recipe does to the FCR and the feed price (U2-04; the standard feed changes nothing).</param>
    internal readonly record struct HoldingHusbandry(
        HoldingWater Water,
        double AppetiteModifier,
        double DensityGrowthMultiplier,
        double ContainmentGrowthMultiplier,
        double StockingDensityKgM3,
        double SpreadQuality,
        FeedRecipeEffect Recipe);

    /// <summary>
    /// Fish one density proxy pen (<see cref="PenVolumeProxyM3"/>) holds: the
    /// pen at the legal density (<c>capacity.maxStockingDensityKgPerM3</c>,
    /// akvakulturdriftsforskriften § 25) when its fish weigh
    /// <c>capacity.densityReferenceWeightKg</c>. 10,000 m³ × 25 kg/m³ / 5 kg =
    /// 50,000 fish, S2-03's commercial pen.
    /// </summary>
    internal double FishPerDensityProxyPen
        => PenVolumeProxyM3 * _calibration.Capacity.MaxStockingDensityKgPerM3
            / Math.Max(1e-9, _calibration.Capacity.DensityReferenceWeightKg);

    /// <summary>
    /// Water (m³) the fish of <paramref name="population"/> stand in for the
    /// density stress on growth and FCR: one proxy pen, or as many proxy pens
    /// as its fish fill at <see cref="FishPerDensityProxyPen"/> (fractional,
    /// so the density never jumps). A population of up to 50,000 fish keeps
    /// the one 10,000 m³ pen it always had; the 170,000-560,000 smolts a site
    /// stocks to its S2-01a plan room in one go are spread like a farmer
    /// spreads them over pens, instead of standing at 90-280 kg/m³ in one.
    /// S2-01b replaces the proxy with real pens (≤ 200,000 fish each,
    /// akvakulturdriftsforskriften § 47a).
    /// </summary>
    internal double StockingVolumeM3(FishPopulation population)
        => PenVolumeProxyM3 * Math.Max(1.0, population.TotalCount / FishPerDensityProxyPen);

    /// <summary>
    /// Water (m³) the density stress on growth and FCR reads for
    /// <paramref name="population"/> in <paramref name="holding"/>: the volume
    /// of the sea pen that holds it (S2-01b, <see cref="Farm.Pen.VolumeM3"/>);
    /// the density proxy (<see cref="StockingVolumeM3"/>) for fish in no pen
    /// (offshore pens and land tanks until F2-03/F2-12 give them units, and
    /// fixtures that add populations to a site's list directly).
    /// </summary>
    internal double DensityVolumeM3(in PopulationHolding holding, FishPopulation population)
        => holding.Pen is { VolumeM3: > 0.0 } pen ? pen.VolumeM3 : StockingVolumeM3(population);

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
        // S2-04: fish fasting before slaughter get nothing, whatever the
        // feeding plan says; they lose a little weight on the law's
        // below-maintenance branch and their gut empties (degree-days).
        double ration = population.IsFasting ? 0.0 : population.Ration?.EffectiveFraction ?? 1.0;
        TrackHarvestCondition(population, husbandry.Water);
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
            // U2-04: a recipe's energy sets how many kilos of feed the same
            // growth takes (the standard feed multiplies nothing).
            if (husbandry.Recipe.FcrFactor != 1.0)
                satiationFcr *= husbandry.Recipe.FcrFactor;

            var day = law.GrowOneDay(cohort.MeanWeightKg, cohort.WeightCv, cohort.Stage, conditions, satiationFcr);
            cohort.MeanWeightKg = day.WeightKg;
            cohort.WeightCv = day.WeightCv;
            grownKg += cohort.Count * Math.Max(0.0, day.GrowthKg);
            eatenKg += cohort.Count * day.EatenFeedKg;
        }

        double appetite = law.ThermalAppetiteIndex(temperature) * husbandry.AppetiteModifier;
        return Feed.FeedRation(population, eatenKg, grownKg, husbandry.SpreadQuality, FeedWasteReduction, appetite,
            husbandry.Recipe.PriceFactor);
    }

    /// <summary>
    /// Mean weight and weight CV of all fish in <paramref name="pens"/> (S2-03):
    /// the pooled spread of their cohorts, within-cohort spread (each cohort's
    /// CV, or the CV of a standard cohort of its weight when not yet known)
    /// plus the spread between cohorts. What a harvest grading or a weight
    /// histogram of the site reads. (0, 0) for empty pens.
    /// </summary>
    public (double MeanWeightKg, double WeightCv) SizeSpreadOf(IEnumerable<FishPopulation> pens)
    {
        if (pens == null) throw new ArgumentNullException(nameof(pens));
        var law = Growth?.Law ?? TgcGrowthLaw.Default;
        double n = 0, sum = 0, sumSquares = 0;
        foreach (var pen in pens)
            foreach (var cohort in pen.Cohorts)
            {
                if (cohort.Count <= 0) continue;
                double mean = cohort.MeanWeightKg;
                double cv = cohort.WeightCv > 0.0 ? cohort.WeightCv : law.InitialWeightCv(mean);
                double sd = cv * mean;
                n += cohort.Count;
                sum += cohort.Count * mean;
                sumSquares += cohort.Count * (sd * sd + mean * mean);
            }
        if (n <= 0 || sum <= 0) return (0.0, 0.0);
        double overall = sum / n;
        double variance = Math.Max(0.0, sumSquares / n - overall * overall);
        return (overall, Math.Sqrt(variance) / overall);
    }

    /// <summary>
    /// Weight and weight CV of a cohort of <paramref name="releaseWeightKg"/>
    /// smolts released <paramref name="daysAtSea"/> days before
    /// <paramref name="firstDay"/> when that day comes: grown on
    /// <paramref name="law"/> in production area <paramref name="productionArea"/>'s
    /// normal sea temperature (its climatology, no anomaly) and natural day
    /// length, healthy and fed to appetite. The starting stock of a scenario farm
    /// (<c>days_at_sea</c>, S2-03). Pure: draws no random numbers.
    /// </summary>
    public static (double WeightKg, double WeightCv) GrowOnClimatology(TgcGrowthLaw law, int productionArea,
        int firstDay, int daysAtSea, double releaseWeightKg, double growthMultiplier = 1.0)
    {
        if (law == null) throw new ArgumentNullException(nameof(law));
        double weight = releaseWeightKg;
        double cv = law.Calibration.SmoltWeightCv;
        if (daysAtSea <= 0) return (weight, cv);
        int area = ProductionAreas.HasClimatology(productionArea) ? productionArea : ProductionAreas.NationalReference;
        double latitude = ProductionAreas.LatitudeDeg(area);
        for (int day = firstDay - daysAtSea; day < firstDay; day++)
        {
            var conditions = new GrowthConditions(ProductionAreas.ClimatologyC(area, day),
                GrowthMultiplier: growthMultiplier,
                DayLengthHours: DayLength.Hours(latitude, SimCalendar.DayOfYear(day)));
            var grown = law.GrowOneDay(weight, cv, LifeStage.Seawater, conditions, satiationFcr: 0.0);
            weight = grown.WeightKg;
            cv = grown.WeightCv;
        }
        return (weight, cv);
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
