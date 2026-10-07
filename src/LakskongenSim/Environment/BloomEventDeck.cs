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
