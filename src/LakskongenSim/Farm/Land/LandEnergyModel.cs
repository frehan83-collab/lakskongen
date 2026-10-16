using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// A lot's electricity for one day, built from its physical parts (spec
    /// §3.3 "Energy, built from its parts"), in kWh. <see cref="LoxUpstreamKWh"/>
    /// is the purchased liquid oxygen's production energy: off the site's meter
    /// (Scope 3, spec §3.6), so it is not in <see cref="TotalKWh"/>.
    /// </summary>
    /// <param name="IntakePumpingKWh">Lifting the new water from the sea and through the intake lines.</param>
    /// <param name="LoopPumpingKWh">Pumping the reused water round the loop.</param>
    /// <param name="TreatmentKWh">Treating the reused water: drum filters, degassing, oxygenation.</param>
    /// <param name="UvKWh">UV disinfection of the intake water.</param>
    /// <param name="HeatingKWh">Heat pumps raising the new water toward the setpoint.</param>
    /// <param name="CoolingKWh">Chillers lowering it toward the setpoint.</param>
    /// <param name="BaseKWh">Lights, feeders, sludge handling, buildings.</param>
    /// <param name="OxygenGeneratorKWh">An on-site oxygen generator's electricity (Scope 2).</param>
    /// <param name="LoxUpstreamKWh">Production energy of the liquid oxygen bought (Scope 3, not on the meter).</param>
    public readonly record struct LandEnergyDay(
        double IntakePumpingKWh,
        double LoopPumpingKWh,
        double TreatmentKWh,
        double UvKWh,
        double HeatingKWh,
        double CoolingKWh,
        double BaseKWh,
        double OxygenGeneratorKWh,
        double LoxUpstreamKWh)
    {
        /// <summary>Electricity on the site's meter (kWh): every part but the purchased oxygen's upstream energy.</summary>
        public double TotalKWh => IntakePumpingKWh + LoopPumpingKWh + TreatmentKWh + UvKWh + HeatingKWh + CoolingKWh
            + BaseKWh + OxygenGeneratorKWh;

        /// <summary>The sum of two days (or a day added to a running total).</summary>
        public static LandEnergyDay operator +(LandEnergyDay a, LandEnergyDay b) => new(
            a.IntakePumpingKWh + b.IntakePumpingKWh,
            a.LoopPumpingKWh + b.LoopPumpingKWh,
            a.TreatmentKWh + b.TreatmentKWh,
            a.UvKWh + b.UvKWh,
            a.HeatingKWh + b.HeatingKWh,
            a.CoolingKWh + b.CoolingKWh,
            a.BaseKWh + b.BaseKWh,
            a.OxygenGeneratorKWh + b.OxygenGeneratorKWh,
            a.LoxUpstreamKWh + b.LoxUpstreamKWh);
    }

    /// <summary>The heat balance of a lot's water for one day.</summary>
    /// <param name="TankC">The temperature the tanks reach (°C).</param>
    /// <param name="HeatingKWh">Electricity the heat pumps used to heat (kWh).</param>
    /// <param name="CoolingKWh">Electricity the chillers used to cool (kWh).</param>
    /// <param name="HeatDeliveredKWh">Heat put into the water (kWh, negative when cooled).</param>
    /// <param name="InternalGainKWh">Heat the pumps, UV and fish put into the water (kWh).</param>
    /// <param name="CapacityBound">True when the heat pumps ran at their capacity and the tanks missed the setpoint.</param>
    public readonly record struct LandHeatBalance(double TankC, double HeatingKWh, double CoolingKWh,
        double HeatDeliveredKWh, double InternalGainKWh, bool CapacityBound);

    /// <summary>
    /// The electricity of a land unit (roadmap F2-12, spec §3.3): pumping
    /// (static lift, intake-line friction, the reuse loop), treatment, UV,
    /// heating and cooling against the intake with the lot's heat-pump
    /// capacity, the base load and oxygen production. Pure and deterministic.
    /// </summary>
    public static class LandEnergyModel
    {
        private const double StandardGravity = 9.80665;
        private const double JoulesPerKWh = 3.6e6;
        private const double HoursPerDay = 24.0;
        private const double KWPerMW = 1000.0;

        /// <summary>Electricity (kWh) to pump <paramref name="volumeM3"/> of seawater up <paramref name="headM"/>: ρgVH/η.</summary>
        public static double PumpingKWh(double volumeM3, double headM, LandEnergyCalibration energy)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            if (!(volumeM3 > 0.0) || !(headM > 0.0)) return 0.0;
            double eta = Math.Clamp(energy.PumpEfficiency, 1e-3, 1.0);
            return energy.SeawaterDensityKgM3 * StandardGravity * volumeM3 * headM / eta / JoulesPerKWh;
        }

        /// <summary>
        /// Friction head (m) of an intake line of <paramref name="lineKm"/>
        /// carrying <paramref name="flowM3"/> a day: the calibration's head per km
        /// at the lot's full pump capacity, scaled by the square of the flow
        /// (Darcy-Weisbach).
        /// </summary>
        public static double LineFrictionM(double flowM3, double lineKm, double pumpCapacityM3s, LandEnergyCalibration energy)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            double capacity = Math.Max(0.0, pumpCapacityM3s) * LandWaterModel.SecondsPerDay;
            if (!(flowM3 > 0.0) || !(capacity > 0.0) || !(lineKm > 0.0)) return 0.0;
            double load = flowM3 / capacity;
            return energy.FrictionMPerKmAtCapacity * lineKm * load * load;
        }

        /// <summary>
        /// Electricity (kWh) to bring <paramref name="newM3"/> of new water in:
        /// each intake line lifts its share (1 − w shallow, w deep) up the static
        /// lift plus its own friction.
        /// </summary>
        public static double IntakePumpingKWh(double newM3, double deepShare, in LandIntakeSite site, LandEnergyCalibration energy)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            if (!(newM3 > 0.0)) return 0.0;
            double w = Math.Clamp(deepShare, 0.0, 1.0);
            double shallow = (1.0 - w) * newM3, deep = w * newM3;
            double lift = Math.Max(0.0, site.StaticLiftM);
            return PumpingKWh(shallow, lift + LineFrictionM(shallow, site.ShallowLineKm, site.PumpCapacityM3sPerLot, energy), energy)
                 + PumpingKWh(deep, lift + LineFrictionM(deep, site.DeepLineKm, site.PumpCapacityM3sPerLot, energy), energy);
        }

        /// <summary>Electricity (kWh) to pump <paramref name="reusedM3"/> round the reuse loop.</summary>
        public static double LoopPumpingKWh(double reusedM3, LandEnergyCalibration energy)
            => PumpingKWh(reusedM3, energy?.LoopHeadM ?? throw new ArgumentNullException(nameof(energy)), energy);

        /// <summary>Electricity (kWh) to treat <paramref name="reusedM3"/> of reused water.</summary>
        public static double TreatmentKWh(double reusedM3, LandTemplateCalibration template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            return Math.Max(0.0, reusedM3) * Math.Max(0.0, template.TreatKWhPerM3Reused);
        }

        /// <summary>Electricity (kWh) to UV-disinfect <paramref name="newM3"/> of intake water (none without disinfection).</summary>
        public static double UvKWh(double newM3, in LandIntakeSite site, LandEnergyCalibration energy)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            return site.Disinfection ? Math.Max(0.0, newM3) * Math.Max(0.0, energy.IntakeUvKWhPerM3) : 0.0;
        }

        /// <summary>
        /// The day's heat balance of a lot (spec §3.3: "Tank T moves toward the
        /// setpoint within the lot's heat-pump MW"). The new water enters at
        /// <paramref name="intakeC"/> and leaves at the tank temperature, so the
        /// tanks hold T_in + (heat delivered + internal gain) / (Q_new·c). The
        /// heat pumps deliver what the setpoint needs, up to their electrical
        /// capacity × COP (the heat-recovery COP when installed); the water may
        /// float up to <see cref="LandEnergyCalibration.CoolingDeadbandK"/> above
        /// the setpoint, and the chillers cool what lies above that at the
        /// cooling COP. Reused water keeps its heat.
        /// </summary>
        public static LandHeatBalance HeatBalance(double newM3, double intakeC, double setpointC, double internalGainKWh,
            double heatPumpMWe, bool heatRecovery, LandEnergyCalibration energy)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            double gain = Math.Max(0.0, internalGainKWh);
            double perK = Math.Max(0.0, newM3) * energy.HeatKWhPerM3PerK;
            if (!(perK > 0.0))
                return new LandHeatBalance(setpointC, 0.0, 0.0, 0.0, gain, false);
            double needed = perK * (setpointC - intakeC) - gain;
            double electricCapacity = Math.Max(0.0, heatPumpMWe) * KWPerMW * HoursPerDay;
            double band = perK * Math.Max(0.0, energy.CoolingDeadbandK);
            double heating = 0.0, cooling = 0.0, delivered;
            bool bound = false;
            if (needed < 0.0 && -needed <= band)
            {
                // Within the band above the setpoint: the water floats.
                delivered = 0.0;
            }
            else if (needed >= 0.0)
            {
                double cop = Math.Max(1e-3, heatRecovery ? energy.CopWithHeatRecovery : energy.Cop);
                double capacity = electricCapacity * cop;
                delivered = Math.Min(needed, capacity);
                bound = needed > capacity;
                heating = delivered / cop;
            }
            else
            {
                // Cool down to the top of the band.
                double cop = Math.Max(1e-3, energy.CoolingCop);
                double capacity = electricCapacity * cop;
                double wanted = -needed - band;
                double removed = Math.Min(wanted, capacity);
                bound = wanted > capacity;
                cooling = removed / cop;
                delivered = -removed;
            }
            double tank = intakeC + (delivered + gain) / perK;
            return new LandHeatBalance(tank, heating, cooling, delivered, gain, bound);
        }

        /// <summary>Heat (kWh) the fish release using <paramref name="fishO2Kg"/> of oxygen (oxycaloric equivalent).</summary>
        public static double MetabolicHeatKWh(double fishO2Kg, LandOxygenCalibration oxygen)
        {
            if (oxygen == null) throw new ArgumentNullException(nameof(oxygen));
            return Math.Max(0.0, fishO2Kg) * oxygen.OxycaloricKWhPerKgO2;
        }

        /// <summary>
        /// Electricity (kWh) to make <paramref name="oxygenKg"/> of oxygen: on the
        /// meter with an on-site generator, otherwise the purchased liquid
        /// oxygen's upstream energy (Scope 3).
        /// </summary>
        public static (double OnSiteKWh, double UpstreamKWh) OxygenProductionKWh(double oxygenKg, bool onSiteGenerator,
            LandOxygenCalibration oxygen)
        {
            if (oxygen == null) throw new ArgumentNullException(nameof(oxygen));
            double kg = Math.Max(0.0, oxygenKg);
            return onSiteGenerator
                ? (kg * oxygen.OnSiteGeneratorKWhPerKgO2, 0.0)
                : (0.0, kg * oxygen.PurchasedLoxUpstreamKWhPerKg);
        }
    }
}
