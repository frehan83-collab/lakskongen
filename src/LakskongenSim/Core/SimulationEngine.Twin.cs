using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Health;

namespace LakskongenSim.Core;

/// <summary>
/// Twin-lite (U2-04, U2-05): one population's cohorts stepped forward on the
/// tick's growth law without touching the game. The feeding plan's 30-day
/// projection and the harvest planner's "wait N weeks" share it, so both
/// read the water, appetite, density, spreading and day length the biology
/// tick would. Pure: it copies what it reads, draws no random numbers and
/// writes nothing back.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>What one projected day of a population did (kilos summed over its cohorts).</summary>
    private readonly record struct TwinDay(double GrowthKg, double EatenKg, double DeliveredKg, double TemperatureC);

    /// <summary>
    /// A population's cohorts as numbers the projection can move: mean weight,
    /// weight CV and the fish expected alive and still in the pen (fractional:
    /// expected deaths, fish taken to slaughter), in the holding the step
    /// resolves today, with
    /// today's appetite (oxygen, gills healing while no bloom stands), the
    /// pen's density as the fish grow, the site's spreading and the day length.
    /// </summary>
    private sealed class PopulationTwin
    {
        private readonly SimulationEngine _engine;
        private readonly FishPopulation _population;
        private readonly HoldingWater _water;
        private readonly Zone? _zone;
        private readonly double _volume;
        private readonly ContainmentModifiers _containment;
        private readonly double _oxygenAppetite;
        private readonly double _gillRecoveryPerDay;
        private readonly double _spread;
        private readonly double _research;
        private readonly double _deliveredPerEaten;
        private readonly TgcGrowthLaw _law;
        private double _gill;

        public PopulationTwin(SimulationEngine engine, FishPopulation population, SeaSite? site)
        {
            _engine = engine;
            _population = population;
            var holding = engine.HoldingOf(population);
            _water = engine.WaterOf(holding, population);
            _zone = holding.Zone;
            _volume = engine.DensityVolumeM3(holding, population);
            _containment = ContainmentTech.ModifiersFor(site?.Containment ?? ContainmentLevel.OpenNet);
            _law = engine.Growth.Law;

            // Appetite as the tick builds it: today's oxygen saturation, and the
            // gills, which heal week by week while no bloom stands at the site
            // (BloomEventDeck.GillRecoveryPerWeek; a new bloom is not foreseen).
            _oxygenAppetite = 1.0;
            if (_zone != null)
                _oxygenAppetite = WaterQuality.CalculateDOSaturationModifier(WaterQuality.CalculateDOSaturation(
                    _water.OxygenMgPerL, _water.TemperatureC, _water.SalinityPsu));
            _gill = site?.GillScore ?? 100.0;
            _gillRecoveryPerDay = site == null || engine.State.ActiveBlooms.Any(b => b.SiteId == site.Id)
                ? 0.0 : BloomEventDeck.GillRecoveryPerWeek / SimCalendar.DaysPerWeek;
            _spread = engine.SpreadQualityOf(site);
            _research = 1.0 + engine.GrowthRateBonus;
            _deliveredPerEaten = 1.0 + (FeedSystem.PrecisionFCRMultiplier(_spread) - 1.0)
                * (1.0 - ResearchEffectKeys.ClampReduction(engine.FeedWasteReduction));

            int n = population.Cohorts.Count;
            Weights = new double[n];
            Cvs = new double[n];
            Counts = new double[n];
            for (int ci = 0; ci < n; ci++)
            {
                Weights[ci] = population.Cohorts[ci].MeanWeightKg;
                Cvs[ci] = population.Cohorts[ci].WeightCv;
                Counts[ci] = population.Cohorts[ci].Count;
            }
        }

        /// <summary>The population projected.</summary>
        public FishPopulation Population => _population;

        /// <summary>The holding's water today.</summary>
        public HoldingWater Water => _water;

        /// <summary>Mean weight of each cohort (kg).</summary>
        public double[] Weights { get; }

        /// <summary>Weight CV of each cohort.</summary>
        public double[] Cvs { get; }

        /// <summary>Fish of each cohort expected alive and still in the pen (fractional: expected deaths).</summary>
        public double[] Counts { get; }

        /// <summary>Fish of cohort <paramref name="ci"/> expected alive (fractional).</summary>
        public double CountOf(int ci) => Counts[ci];

        /// <summary>Expected deaths of one day at <paramref name="dailyRate"/> (a share of the fish).</summary>
        public void ApplyDeaths(double dailyRate)
        {
            if (!(dailyRate > 0.0)) return;
            double keep = 1.0 - Math.Min(1.0, dailyRate);
            for (int ci = 0; ci < Counts.Length; ci++) Counts[ci] *= keep;
        }

        /// <summary>Fish expected alive in the population (fractional).</summary>
        public double Fish
        {
            get
            {
                double fish = 0.0;
                for (int ci = 0; ci < Weights.Length; ci++) fish += CountOf(ci);
                return fish;
            }
        }

        /// <summary>Live kilos expected in the population.</summary>
        public double BiomassKg
        {
            get
            {
                double biomass = 0.0;
                for (int ci = 0; ci < Weights.Length; ci++) biomass += CountOf(ci) * Weights[ci];
                return biomass;
            }
        }

        /// <summary>
        /// The water temperature expected on projected day <paramref name="dayAhead"/>
        /// (0 = the next simulated day; <see cref="ExpectedTemperature"/>).
        /// </summary>
        public double TemperatureOn(int dayAhead) => _engine.ExpectedTemperature(_water, _zone, dayAhead);

        /// <summary>
        /// Grow every cohort one day (projected day <paramref name="dayAhead"/>)
        /// at <paramref name="ration"/> of appetite (0 = fasting) on
        /// <paramref name="effect"/>'s feed. The numbers are the biology tick's:
        /// the same law, conditions and satiation FCR.
        /// </summary>
        public TwinDay Step(int dayAhead, double ration, in FeedRecipeEffect effect)
        {
            var engine = _engine;
            double appetite = _oxygenAppetite * (0.6 + 0.4 * (_gill / 100.0));
            _gill = Math.Min(100.0, _gill + _gillRecoveryPerDay);
            double temperature = TemperatureOn(dayAhead);
            double biomass = 0.0;
            for (int ci = 0; ci < Weights.Length; ci++)
                biomass += CountOf(ci) * Weights[ci];
            double densityForO2 = (_zone != null ? biomass / _volume : DefaultStockingDensityKgM3) * _containment.OxygenDemandMult;
            double densityGrowth = _zone != null ? WaterQuality.CalculateStockingDensityModifier(densityForO2) : 1.0;
            double dayLength = _water.Kind == HoldingKind.LandTank
                ? TgcGrowthLaw.ContinuousLightHours
                : DayLength.Hours(ProductionAreas.LatitudeDeg(AreaWithClimatology(_water.ProductionArea)),
                    SimCalendar.DayOfYear(engine.State.Day + dayAhead));

            double growthKg = 0.0, eatenKg = 0.0, deliveredKg = 0.0;
            for (int ci = 0; ci < Weights.Length; ci++)
            {
                var cohort = _population.Cohorts[ci];
                double count = CountOf(ci);
                var conditions = new GrowthConditions(temperature, ration, appetite,
                    CohortGrowthMultiplier(cohort) * _containment.GrowthMult * _research,
                    densityGrowth, _spread, dayLength);
                double satiationFcr = engine.Feed.SatiationFcr(temperature, cohort.Stage, _law.FcrSizeFactor(Weights[ci]),
                    densityForO2, appetite, engine.FCRReduction) * effect.FcrFactor;
                var grown = _law.GrowOneDay(Weights[ci], Cvs[ci], cohort.Stage, conditions, satiationFcr);
                growthKg += count * Math.Max(0.0, grown.GrowthKg);
                eatenKg += count * grown.EatenFeedKg;
                deliveredKg += count * grown.EatenFeedKg * _deliveredPerEaten;
                Weights[ci] = grown.WeightKg;
                Cvs[ci] = grown.WeightCv;
            }
            return new TwinDay(growthKg, eatenKg, deliveredKg, temperature);
        }
    }
}
