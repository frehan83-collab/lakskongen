using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Seasonal bloom deck (Phase 4.3): jellyfish (Sep-Jan), harmful algae
    /// (May-Jul, warm), hypoxia sags (warm + calm). Weekly roll per zone;
    /// damage lands on gills + direct mortality. Countermeasures blunt it.
    /// Pure logic; ticked by SimulationEngine.
    /// </summary>
    public static class BloomEventDeck
    {
        public const double JellyGillPerWeek = 12.0;
        public const double AlgaeGillPerWeek = 16.0;
        public const double HypoxiaGillPerWeek = 8.0;
        public const double GillRecoveryPerWeek = 6.0;

        public static bool InJellySeason(int dayOfYear) => dayOfYear >= 244 || dayOfYear <= 31;
        public static bool InAlgaeSeason(int dayOfYear) => dayOfYear >= 120 && dayOfYear <= 212;

        /// <summary>Countermeasure damage multiplier for a kind at a site.</summary>
        public static double CounterMult(Farm.SeaSite site, string kind) =>
            kind switch
            {
                "jellyfish" => site.HasBubbleCurtain ? 0.23 : 1.0,
                "algae" => site.HasO2Diffuser ? 0.7 : 1.0,
                "hypoxia" => site.HasO2Diffuser ? 0.3 : 1.0,
                _ => 1.0
            };

        /// <summary>
        /// The <c>hypoxia</c> card is an oxygen sag in the cage (wave 2
        /// sim-a-1): in warm, calm weeks the fish's own respiration draws the
        /// cage's water below the ambient oxygen. Returns the fraction of the
        /// ambient oxygen the site's pens lose while <paramref name="bloom"/>
        /// runs: severity x <paramref name="maxDepth"/>
        /// (<c>WaterOxygenCalibration.HypoxiaSagMaxDepth</c>), scaled by an O2
        /// diffuser (<see cref="CounterMult"/>, oxygenation) and the
        /// containment's oxygen demand (<see cref="ContainmentMult"/>), at most
        /// the whole. Zero for any other kind. The pens' water carries it
        /// (<c>SimulationEngine.HoldingWaterOf</c>), so appetite, growth and
        /// the low-oxygen hazard all read the same sag.
        /// </summary>
        public static double HypoxiaSagDepth(Farm.SeaSite site, BloomEvent bloom, double maxDepth)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            if (bloom == null || bloom.Kind != "hypoxia") return 0.0;
            double depth = Math.Clamp(bloom.Severity01, 0.0, 1.0) * Math.Max(0.0, maxDepth)
                * CounterMult(site, bloom.Kind) * ContainmentMult(site, bloom.Kind);
            return Math.Clamp(depth, 0.0, 1.0);
        }

        /// <summary>Containment dodge: closed rungs ignore jelly/algae, suffer hypoxia more.</summary>
        public static double ContainmentMult(Farm.SeaSite site, string kind)
        {
            if (site.Containment >= Farm.ContainmentLevel.SemiClosed && kind != "hypoxia")
                return 0.0;
            if (kind == "hypoxia")
                return Farm.ContainmentTech.ModifiersFor(site.Containment).OxygenDemandMult;
            return 1.0;
        }
    }
}
