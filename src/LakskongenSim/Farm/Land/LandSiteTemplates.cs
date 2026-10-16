using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// The land units the player builds (roadmap F2-12, spec §2A and §3.2):
    /// the hybrid flow-through (HFS) lot and its pilot, the RAS hall and the
    /// post-smolt module, each built from its <see cref="LandTemplateCalibration"/>.
    /// Pure: building a lot draws no random numbers and reads no engine state.
    /// </summary>
    public static class LandSiteTemplates
    {
        /// <summary>
        /// The biofilter a template is built with: none on an HFS lot (the new
        /// water carries the ammonia out, [L:R7]), a fixed bed in the RAS hall
        /// (spec F2-16 #11), a moving bed in the post-smolt module.
        /// </summary>
        public static BiofilterType DefaultBiofilter(LandTemplate template) => template switch
        {
            LandTemplate.HfsLot or LandTemplate.HfsPilotLot => BiofilterType.None,
            LandTemplate.RasHall => BiofilterType.FixedBed,
            LandTemplate.PostSmoltModule => BiofilterType.MovingBed,
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown land template"),
        };

        /// <summary>
        /// True where every tank is its own biological zone (HFS, [L:R1]);
        /// false where the tanks share one loop (RAS hall, post-smolt module).
        /// </summary>
        public static bool TanksAreSeparateZones(LandTemplate template)
            => template is LandTemplate.HfsLot or LandTemplate.HfsPilotLot;

        /// <summary>
        /// The Vannstrategi a new unit starts with: the template's reuse, the
        /// intake blended toward the setpoint every day (Salmon Evolution's two
        /// intakes, [L:R2]), and the template's setpoint.
        /// </summary>
        public static LandWaterStrategy DefaultStrategy(LandTemplateCalibration template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            return new LandWaterStrategy(template.DefaultReuse, 0.5, template.DefaultSetpointC, BlendTowardSetpoint: true);
        }

        /// <summary>
        /// Build lot <paramref name="lotIndex"/> (1-based) of <paramref name="template"/>
        /// at <paramref name="siteId"/>, its tanks numbered from
        /// <paramref name="firstTankNumber"/>: grow-out tanks first, then
        /// pre-grow-out tanks. The biofilter starts at
        /// <see cref="LandTanCalibration.MaturityAtStart"/> when the template has one.
        /// </summary>
        public static LandLot BuildLot(string siteId, int lotIndex, LandTemplate template, LandCalibration calibration,
            int firstTankNumber = 1)
        {
            if (string.IsNullOrEmpty(siteId)) throw new ArgumentException("A lot needs its site's id", nameof(siteId));
            if (lotIndex < 1) throw new ArgumentOutOfRangeException(nameof(lotIndex), lotIndex, "Lots are numbered from 1");
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            var t = calibration.For(template);
            var lot = new LandLot
            {
                Id = LandLot.ComposeId(siteId, lotIndex),
                Template = template,
                HeatPumpMWe = t.HeatPumpMWe,
                Biofilter = DefaultBiofilter(template),
            };
            lot.BiofilterMaturity = lot.Biofilter == BiofilterType.None ? 1.0 : calibration.Tan.MaturityAtStart;
            bool separate = TanksAreSeparateZones(template);
            var growRole = template == LandTemplate.PostSmoltModule ? TankRole.PostSmolt : TankRole.GrowOut;
            int number = Math.Max(1, firstTankNumber);
            for (int k = 0; k < t.GrowOutTanks; k++, number++)
                lot.Tanks.Add(LandTank.Build(siteId, number, growRole, t.GrowOutDiameterM, t.WaterDepthM,
                    separate ? "" : lot.Id));
            for (int k = 0; k < t.PreGrowTanks; k++, number++)
                lot.Tanks.Add(LandTank.Build(siteId, number, TankRole.PreGrowOut, t.PreGrowDiameterM, t.WaterDepthM,
                    separate ? "" : lot.Id));
            foreach (var tank in lot.Tanks)
                tank.TemperatureC = t.DefaultSetpointC;
            return lot;
        }

        /// <summary>
        /// Biofilter maturity <paramref name="daysSinceStart"/> days after the
        /// filter was started: linear from the start maturity to 1 over the
        /// template's maturation days (spec §3.3).
        /// </summary>
        public static double BiofilterMaturity(int daysSinceStart, LandTemplateCalibration template, LandTanCalibration tan)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (tan == null) throw new ArgumentNullException(nameof(tan));
            if (template.BiofilterMaturationDays <= 0) return 1.0;
            double t = Math.Clamp((double)Math.Max(0, daysSinceStart) / template.BiofilterMaturationDays, 0.0, 1.0);
            return tan.MaturityAtStart + (1.0 - tan.MaturityAtStart) * t;
        }
    }
}
