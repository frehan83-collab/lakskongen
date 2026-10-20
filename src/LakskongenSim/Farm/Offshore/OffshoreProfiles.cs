using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>
    /// The unit classes the player builds (F2-07, spec §2A and §3.9 "of1",
    /// "havfarm"): the class's calibration record and its build phases. The
    /// zones, design Hs, MTB and volume stay <see cref="OffshoreUnit"/>'s class
    /// profile (F2-03).
    /// </summary>
    public static class OffshoreProfiles
    {
        /// <summary>The construction phase keys, in build order (spec §6.3 "Offshore phase").</summary>
        public const string YardContractPhase = "yard_contract";
        public const string HullPhase = "hull";
        public const string VoyagePhase = "voyage";
        public const string TowPhase = "tow";
        public const string MooringPhase = "mooring";
        public const string CommissioningPhase = "commissioning";

        /// <summary>The phase keys in order.</summary>
        public static readonly IReadOnlyList<string> PhaseKeys = new[]
        {
            YardContractPhase, HullPhase, VoyagePhase, TowPhase, MooringPhase, CommissioningPhase,
        };

        /// <summary>
        /// The calibration record of a structure class the player can build:
        /// OF1-class (semi-submersible) or Havfarm-class (ship). The legacy
        /// submersible concept is load-only (spec §8) and has none.
        /// </summary>
        public static OffshoreClassCalibration? ClassOf(OffshoreType type, OffshoreCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return type switch
            {
                OffshoreType.SemiSubmersible => calibration.Units.Of1,
                OffshoreType.ShipShaped => calibration.Units.Havfarm,
                _ => null,
            };
        }

        /// <summary>The catalogue variant id of a class ("of1", "havfarm").</summary>
        public static string VariantOf(OffshoreType type) => type == OffshoreType.ShipShaped ? FarmTypeIds.Havfarm : FarmTypeIds.Of1;

        /// <summary>
        /// The build phases of a class (spec §6.3): the yard contract (signing
        /// payment), the hull at the yard, the delivery voyage (delivery
        /// payment), the tow to the plot (started by the player, the tugs are
        /// chartered, held for the tow's sea-state days), mooring (the spread's
        /// payment) and commissioning. The shares are the payment schedule's
        /// and add up to 1.
        /// </summary>
        public static IReadOnlyList<ConstructionPhase> PhasesFor(OffshoreClassCalibration unit,
            OffshorePaymentScheduleCalibration payments, OffshoreOperabilityCalibration operability)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (payments == null) throw new ArgumentNullException(nameof(payments));
            if (operability == null) throw new ArgumentNullException(nameof(operability));
            return new[]
            {
                new ConstructionPhase { Key = YardContractPhase, PlannedDays = unit.YardContractDays, CapexShare = payments.ContractShare },
                new ConstructionPhase { Key = HullPhase, PlannedDays = unit.HullDays, CapexShare = payments.HullShare },
                new ConstructionPhase { Key = VoyagePhase, PlannedDays = unit.VoyageDays, CapexShare = payments.DeliveryShare },
                new ConstructionPhase
                {
                    Key = TowPhase, PlannedDays = operability.TowHoldDays, CapexShare = 0.0,
                    MaxHsM = operability.TowHsM, PlayerStarted = true,
                },
                new ConstructionPhase { Key = MooringPhase, PlannedDays = unit.MooringDays, CapexShare = payments.MooringShare },
                new ConstructionPhase { Key = CommissioningPhase, PlannedDays = unit.CommissioningDays, CapexShare = 0.0 },
            };
        }

        /// <summary>Planned days from the contract to commissioning (the tow at its held days).</summary>
        public static int PlannedBuildDays(OffshoreClassCalibration unit, OffshoreOperabilityCalibration operability)
            => unit.YardContractDays + unit.HullDays + unit.VoyageDays + operability.TowHoldDays + unit.MooringDays + unit.CommissioningDays;
    }
}
