using LakskongenSim.Biology;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Low oxygen at the fish (wave 2 sim-a-1). Every source of low oxygen kills
/// through one hazard read at the cohort's own water
/// (<see cref="MortalityHazards.LowOxygenFraction"/> on
/// <see cref="ResolveCohortEnvironment"/>), booked as
/// <see cref="MortalityCause.LowOxygen"/>:
/// <list type="bullet">
/// <item>a plankton crash collapses a zone's oxygen and the
/// <c>plankton-crash</c> event kills at each pen's own oxygen;</item>
/// <item>the bloom deck's <c>hypoxia</c> card is an in-cage oxygen sag: while
/// it runs, the site's pens' water carries the sag
/// (<see cref="HypoxiaSagDepthAt"/>, through <see cref="WaterOf"/>), so
/// appetite and growth read it every day, and its weekly tick kills only the
/// fish whose water is below the acute limit (3 mg/L, US EPA 1986). Before,
/// the card killed 3 % x severity of the site's fish per weekly tick whatever
/// the water held: in PO3's water that was every low-oxygen death in the po3
/// careers (65,225 of 525,255 deaths over 20 seeds, 12.4 %, at zone oxygen
/// 7.6-8.2 mg/L) against the 1 % NVI books for the whole environment class
/// in PO1-PO4 in 2024.</item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// Fraction of the ambient oxygen <paramref name="site"/>'s pens lose to a
    /// running oxygen sag (the bloom deck's <c>hypoxia</c> card), 0 when none
    /// runs (<see cref="LakskongenSim.Environment.BloomEventDeck.HypoxiaSagDepth"/>).
    /// </summary>
    internal double HypoxiaSagDepthAt(SeaSite site)
    {
        var blooms = State?.ActiveBlooms;
        if (blooms == null || blooms.Count == 0) return 0.0;
        double depth = 0.0;
        for (int i = 0; i < blooms.Count; i++)
        {
            var bloom = blooms[i];
            if (bloom.SiteId != site.Id || bloom.Kind != "hypoxia" || bloom.IsExpired(State!.Day)) continue;
            double d = LakskongenSim.Environment.BloomEventDeck.HypoxiaSagDepth(site, bloom, _calibration.WaterOxygen.HypoxiaSagMaxDepth);
            if (d > depth) depth = d;
        }
        return depth;
    }

    /// <summary>
    /// The plankton bloom's crash takes the oxygen its carbon needs to be
    /// respired, from the calibration registry
    /// (<see cref="Calibration.WaterOxygenCalibration.BloomCrashOxygenDemandAtCapacityMgL"/>),
    /// whatever the bloom config file holds.
    /// </summary>
    internal void ApplyWaterOxygenCalibration()
    {
        if (PlanktonBloom != null)
            PlanktonBloom.Config.CrashOxygenDemandAtCapacityMgL = _calibration.WaterOxygen.BloomCrashOxygenDemandAtCapacityMgL;
    }

    /// <summary>
    /// Fraction of <paramref name="population"/> one exposure to its own
    /// water's oxygen kills (<see cref="MortalityHazards.LowOxygenFraction"/>):
    /// the plankton-crash event and an oxygen sag's weekly tick read it.
    /// </summary>
    internal double LowOxygenDeathFraction(FishPopulation population)
        => MortalityHazards.LowOxygenFraction(ResolveCohortEnvironment(population), _calibration.Mortality);
}
