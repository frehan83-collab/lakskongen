using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// The post-smolt bridge on seawater (roadmap F2-13, docs/FARM_TYPES_SPEC.md
    /// §2B-2C, §3.3): the quality tier a land-grown fish carries to sea, its
    /// desmoltification at low salinity, the H2S sulphate factor of its water
    /// and the wellboat days of a transfer. Pure: no state, no random numbers.
    /// </summary>
    public static class PostSmoltBridge
    {
        /// <summary>
        /// The tier a fish of <paramref name="meanWeightKg"/> carries to sea:
        /// SuperSmolt from the calibration's 400 g when it stood in at least
        /// 25 ppt on each of the last 30 days (<paramref name="seawaterDays"/>),
        /// a large smolt from 200 g (and a heavy fish without the seawater
        /// record), else standard (spec F2-13 acceptance 2).
        /// </summary>
        public static SmoltQualityTier TierOf(double meanWeightKg, int seawaterDays, LandSmoltCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (meanWeightKg >= calibration.SuperSmoltMinWeightKg && seawaterDays >= calibration.SeawaterRecordDays)
                return SmoltQualityTier.SuperSmolt;
            return meanWeightKg >= calibration.LargeMinWeightKg ? SmoltQualityTier.Large : SmoltQualityTier.Standard;
        }

        /// <summary>True when fish have stood under the desmoltification salinity for more than the calibration's days in a row.</summary>
        public static bool IsDesmoltified(int lowSalinityDays, LandSmoltCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return lowSalinityDays > calibration.DesmoltDays;
        }

        /// <summary>True when <paramref name="salinityPpt"/> counts toward the SuperSmolt seawater record.</summary>
        public static bool CountsAsSeawater(double salinityPpt, LandSmoltCalibration calibration)
            => salinityPpt >= (calibration ?? throw new ArgumentNullException(nameof(calibration))).SuperSmoltMinSalinityPpt;

        /// <summary>True when <paramref name="salinityPpt"/> is under the desmoltification salinity.</summary>
        public static bool DesmoltifiesAt(double salinityPpt, LandSmoltCalibration calibration)
            => salinityPpt < (calibration ?? throw new ArgumentNullException(nameof(calibration))).DesmoltSalinityPpt;

        /// <summary>
        /// The H2S sulphate factor of water of <paramref name="salinityPpt"/>
        /// (spec §3.3: salinity / 35, [V:6]): the share of seawater's sulphate
        /// a sulphate-reducing biofilm can turn to H2S, 0-1. F2-16's H2S index
        /// multiplies its daily rise by it.
        /// </summary>
        public static double SulphateFactor(double salinityPpt, LandSmoltCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (!double.IsFinite(salinityPpt) || calibration.SulphateReferenceSalinityPpt <= 0.0) return 1.0;
            return Math.Clamp(salinityPpt / calibration.SulphateReferenceSalinityPpt, 0.0, 1.0);
        }

        /// <summary>
        /// Wellboat days a transfer of <paramref name="tonnesLive"/> takes: one
        /// day per started day's load (at least one). A count above
        /// <see cref="LandSmoltCalibration.MaxTransferDays"/> is a transfer no
        /// wellboat slot carries.
        /// </summary>
        public static int TransferDays(double tonnesLive, LandSmoltCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (!(tonnesLive > 0.0)) return 1;
            double perDay = Math.Max(1e-9, calibration.TransferTonnesPerDay);
            return Math.Max(1, (int)Math.Ceiling(tonnesLive / perDay - 1e-9));
        }

        /// <summary>
        /// Share of the fish a transfer to sea kills in handling: the cohort
        /// transfer's post-smolt rate for the fish's weight (Phase 5.3,
        /// <see cref="CohortTransferSystem.MortalityRatePct"/>, offshore and
        /// post-smolt stocking), × the calibration's multiplier when the fish
        /// have desmoltified (spec F2-13 acceptance 3).
        /// </summary>
        public static double TransferMortalityShare(double meanWeightKg, bool desmoltified, bool hasVeterinarian,
            LandSmoltCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double share = CohortTransferSystem.MortalityRatePct(meanWeightKg,
                CohortTransferSystem.TransferReason.OffshoreStocking, hasVeterinarian) / 100.0;
            if (desmoltified) share *= calibration.DesmoltTransferMortalityMultiplier;
            return Math.Clamp(share, 0.0, 1.0);
        }
    }

    /// <summary>
    /// A booked transfer of a land tank's fish to a sea site (spec §3.4
    /// <c>TransferTankToSea</c>): a wellboat job of kind "transfer" with the
    /// tank, the destination and the fish. The engine checks the destination's
    /// MTB when it books the boat and again when the boat arrives on
    /// <see cref="Logistics.WellboatJob.DueDay"/>.
    /// </summary>
    public sealed class LandTransferJob
    {
        /// <summary>Wellboat job kind of a land-to-sea transfer.</summary>
        public const string Kind = "transfer";

        /// <summary>The wellboat hire: booking day, site, kind "transfer", sequence, due day and hire.</summary>
        public Logistics.WellboatJob Boat { get; set; } = new() { Kind = Kind };

        /// <summary>The tank the fish leave ("ras_1/t03").</summary>
        public string TankId { get; set; } = "";

        /// <summary>The population in the tank when booked (the fish the boat takes).</summary>
        public string PopulationId { get; set; } = "";

        /// <summary>The sea site the fish go to.</summary>
        public string DestinationSiteId { get; set; } = "";

        /// <summary>Fish booked.</summary>
        public int Fish { get; set; }

        /// <summary>Wellboat days the transfer takes (1-3).</summary>
        public int Days { get; set; } = 1;

        /// <summary>The strain the fish were stocked as ("standard", "robust" …).</summary>
        public string StrainId { get; set; } = "";

        /// <summary>Mean live weight when booked (kg).</summary>
        public double BookedWeightKg { get; set; }

        /// <summary>The tier the fish would carry if they left on the booking day.</summary>
        public SmoltQualityTier BookedTier { get; set; }

        /// <summary>The job id: the wellboat's.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public string Id => Boat.Id;
    }
}
