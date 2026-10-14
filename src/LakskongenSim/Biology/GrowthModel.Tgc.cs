using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Biology
{
    public partial class GrowthModel
    {
        /// <summary>
        /// The growth law the engine grows every cohort on (roadmap S2-03): the
        /// thermal growth coefficient, appetite and ration response of
        /// <see cref="TgcGrowthLaw"/>, on the engine's calibration.
        /// </summary>
        public TgcGrowthLaw Law { get; private set; } = TgcGrowthLaw.Default;

        /// <summary>Run the growth law on <paramref name="calibration"/> (the engine's registry).</summary>
        public void ApplyCalibration(GrowthCalibration calibration)
        {
            Law = new TgcGrowthLaw(calibration ?? throw new ArgumentNullException(nameof(calibration)));
        }
    }
}
