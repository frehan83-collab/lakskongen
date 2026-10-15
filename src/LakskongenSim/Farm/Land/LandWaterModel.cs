using System;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// The player's water strategy for a land unit ("Vannstrategi", spec §3.4
    /// <c>SetVannstrategi</c>): how much of the loop flow is reused, how the
    /// shallow (25 m) and deep (95 m) intakes are blended, and the tank
    /// temperature the heat pumps hold.
    /// </summary>
    /// <param name="Reuse">Reuse r: share of the loop flow that is reused (0-<see cref="LandIntakeCalibration.MaxReuse"/>).</param>
    /// <param name="DeepIntakeShare">Share w of the new water drawn from the deep intake (0-1); ignored while <paramref name="BlendTowardSetpoint"/>.</param>
    /// <param name="SetpointC">Tank temperature the heat pumps aim for (°C).</param>
    /// <param name="BlendTowardSetpoint">True: every day the blend is the one whose temperature is nearest the setpoint (Salmon Evolution's practice, [L:R2]).</param>
    public readonly record struct LandWaterStrategy(double Reuse, double DeepIntakeShare, double SetpointC,
        bool BlendTowardSetpoint = false);

    /// <summary>
    /// The intake side of a land plot a unit stands on (spec §3.2
    /// <c>SitePlot</c>): the lift from the sea, the two intake lines and the
    /// pumps. F2-03's plot catalogue supplies it (deep line = the computed
    /// <c>IntakeReachM</c>, F2-19).
    /// </summary>
    /// <param name="StaticLiftM">Lift from the sea to the tank water (m).</param>
    /// <param name="ShallowLineKm">Length of the 25 m intake line (km).</param>
    /// <param name="DeepLineKm">Length of the 95 m intake line (km).</param>
    /// <param name="PumpCapacityM3sPerLot">Intake pump capacity of one lot (m³/s).</param>
    /// <param name="Disinfection">True when the intake water is UV-disinfected (mandatory for new plants, [L:L2]).</param>
    public readonly record struct LandIntakeSite(double StaticLiftM, double ShallowLineKm, double DeepLineKm,
        double PumpCapacityM3sPerLot, bool Disinfection = true)
    {
        /// <summary>A plot with the calibration's lift and pumps and the given intake lines.</summary>
        public static LandIntakeSite FromCalibration(LandEnergyCalibration energy, double shallowLineKm, double deepLineKm,
            bool disinfection = true)
        {
            if (energy == null) throw new ArgumentNullException(nameof(energy));
            return new LandIntakeSite(energy.StaticLiftM, shallowLineKm, deepLineKm, energy.PumpCapacityM3sPerLot, disinfection);
        }
    }

    /// <summary>A lot's water flows for one day (m³/day).</summary>
    /// <param name="DesignLoopM3">Loop flow at the design reuse: the template's tank volumes a day over the stocked tanks.</param>
    /// <param name="NewM3">New water pumped in.</param>
    /// <param name="ReusedM3">Water reused.</param>
    /// <param name="StrippedM3">Reused water that passes the CO2 strippers (the rest returns unstripped).</param>
    /// <param name="PumpLimited">True when the intake pumps could not deliver the new water the reuse asks for.</param>
    public readonly record struct LandFlows(double DesignLoopM3, double NewM3, double ReusedM3, double StrippedM3,
        bool PumpLimited)
    {
        /// <summary>Water through the tanks (m³/day).</summary>
        public double TotalM3 => NewM3 + ReusedM3;

        /// <summary>No flow (an empty lot).</summary>
        public static LandFlows None => new(0.0, 0.0, 0.0, 0.0, false);
    }

    /// <summary>What one tank holds and eats today (the biology's output, the land tick's input).</summary>
    /// <param name="BiomassKg">Live fish in the tank (kg).</param>
    /// <param name="FeedKg">Feed eaten today (kg).</param>
    public readonly record struct TankLoad(double BiomassKg, double FeedKg)
    {
        /// <summary>True when the tank holds fish.</summary>
        public bool Stocked => BiomassKg > 0.0;
    }

    /// <summary>The flows one tank gets: its share of the lot's by volume (m³/day).</summary>
    public readonly record struct TankFlows(double NewM3, double ReusedM3, double StrippedM3)
    {
        /// <summary>Share of the reused water that is stripped (0-1).</summary>
        public double StrippedShare => ReusedM3 > 0.0 ? Math.Clamp(StrippedM3 / ReusedM3, 0.0, 1.0) : 0.0;
    }

    /// <summary>One tank's water after the day (spec §3.3).</summary>
    /// <param name="DoMgL">Dissolved oxygen at the outlet (mg/L).</param>
    /// <param name="Co2MgL">Dissolved CO2 (mg/L).</param>
    /// <param name="TanMgL">Total ammonia nitrogen (mg/L).</param>
    /// <param name="No3NMgL">Nitrate nitrogen (mg/L).</param>
    /// <param name="AppetiteFactor">Appetite the CO2 and TAN leave (0-1).</param>
    /// <param name="FishO2Kg">Oxygen the fish used (kg).</param>
    /// <param name="NitrificationO2Kg">Oxygen the biofilter took from the water (kg).</param>
    /// <param name="TanRemovedKg">TAN-N the biofilter nitrified (kg).</param>
    /// <param name="OxygenNeedKg">Oxygen that had to be dissolved by injection to hold the outlet DO (kg).</param>
    /// <param name="OxygenInjectedKg">Oxygen dissolved by injection (kg): the need, or less when liquid oxygen ran short.</param>
    public readonly record struct TankWater(double DoMgL, double Co2MgL, double TanMgL, double No3NMgL,
        double AppetiteFactor, double FishO2Kg, double NitrificationO2Kg, double TanRemovedKg, double OxygenNeedKg,
        double OxygenInjectedKg);

    /// <summary>
    /// The water of a land unit (roadmap F2-12, spec §3.3): flows, the intake
    /// blend, and each tank's oxygen, CO2, ammonia and nitrate as steady-state
    /// mass balances over one day. Pure and deterministic: no RNG, no engine
    /// state; the land tick, the stocking preview and the Vannstrategi sweep
    /// call the same functions.
    /// </summary>
    public static class LandWaterModel
    {
        private const double GramsPerKg = 1000.0;

        /// <summary>Seconds in a day: m³/s to m³/day.</summary>
        public const double SecondsPerDay = 86_400.0;

        // ── Flows ───────────────────────────────────────────────────────────

        /// <summary>
        /// The lot's flows for <paramref name="stockedVolumeM3"/> of stocked
        /// tanks at reuse <paramref name="reuse"/> (spec §3.3 "Flows"): the loop
        /// flow is the template's tank volumes a day; new water (1 − r) of it,
        /// as far as the intake pumps reach. When they cannot deliver, the reuse
        /// share is kept and the circulation falls (the cost of low reuse). The
        /// strippers take reused water up to their design share of the loop
        /// flow; the rest returns unstripped.
        /// </summary>
        public static LandFlows Flows(double stockedVolumeM3, LandTemplateCalibration template, double reuse,
            double pumpCapacityM3s, LandIntakeCalibration intake)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (intake == null) throw new ArgumentNullException(nameof(intake));
            if (!(stockedVolumeM3 > 0.0)) return LandFlows.None;
            double r = Math.Clamp(double.IsFinite(reuse) ? reuse : 0.0, 0.0, intake.MaxReuse);
            double design = Math.Max(0.0, template.LoopVolumesPerDay) * stockedVolumeM3;
            double capacity = Math.Max(0.0, pumpCapacityM3s) * SecondsPerDay;
            double wantNew = (1.0 - r) * design;
            double newM3, reused;
            bool limited = wantNew > capacity;
            if (limited)
            {
                newM3 = capacity;
                reused = newM3 * r / (1.0 - r);
            }
            else
            {
                newM3 = wantNew;
                reused = r * design;
            }
            double stripped = Math.Min(reused, Math.Clamp(template.StripperDesignReuse, 0.0, 1.0) * design);
            return new LandFlows(design, newM3, reused, stripped, limited);
        }

        /// <summary>The share of the lot's flows a tank of <paramref name="volumeM3"/> gets (by volume).</summary>
        public static TankFlows ShareOf(in LandFlows flows, double volumeM3, double stockedVolumeM3)
        {
            if (!(stockedVolumeM3 > 0.0) || !(volumeM3 > 0.0)) return default;
            double s = volumeM3 / stockedVolumeM3;
            return new TankFlows(flows.NewM3 * s, flows.ReusedM3 * s, flows.StrippedM3 * s);
        }

        // ── Intake ──────────────────────────────────────────────────────────

        /// <summary>
        /// The deep-intake share the strategy draws today: its fixed w, or the
        /// blend of the shallow (<paramref name="shallowC"/>) and deep
        /// (<paramref name="deepC"/>) water whose temperature is nearest
        /// <paramref name="targetC"/> (the setpoint when not given; the solver
        /// passes the setpoint less the rise the water gets in the loop).
        /// </summary>
        public static double DeepShare(in LandWaterStrategy strategy, double shallowC, double deepC, double? targetC = null)
        {
            if (!strategy.BlendTowardSetpoint)
                return Math.Clamp(double.IsFinite(strategy.DeepIntakeShare) ? strategy.DeepIntakeShare : 0.0, 0.0, 1.0);
            double span = deepC - shallowC;
            if (Math.Abs(span) < 1e-9) return 0.0;
            double target = targetC is double t && double.IsFinite(t) ? t : strategy.SetpointC;
            return Math.Clamp((target - shallowC) / span, 0.0, 1.0);
        }

        /// <summary>Intake temperature (°C) of a blend: T = (1 − w)·T25 + w·T95 (spec §3.3).</summary>
        public static double IntakeTemperatureC(double shallowC, double deepC, double deepShare)
        {
            double w = Math.Clamp(deepShare, 0.0, 1.0);
            return (1.0 - w) * shallowC + w * deepC;
        }

        // ── Tank chemistry ──────────────────────────────────────────────────

        /// <summary>
        /// One tank's water today (spec §3.3), as steady-state mass balances
        /// over the day (the tanks turn over in hours), except nitrate, which
        /// relaxes toward its steady state with the tank's new-water exchange.
        /// <list type="bullet">
        /// <item>Oxygen: the fish use <c>k·feed</c>; the biofilter takes
        /// 4.57 g per g TAN-N it nitrifies from the reused water; the new water
        /// brings its saturation at the intake temperature down to the outlet
        /// DO; the strippers bring the stripped share of the reused water a
        /// share η of the way back to air saturation; injection dissolves the
        /// rest. With <paramref name="oxygenSupplyShare"/> &lt; 1 (liquid oxygen
        /// short) the outlet DO falls to what the supply holds.</item>
        /// <item>CO2: C = (C_new·Q_new + 1.375·O2_fish)/(Q_new + η·Q_stripped).</item>
        /// <item>TAN: excretion leaves with the new water and through the
        /// biofilter (one-pass efficiency on the reused water, capped by its
        /// capacity).</item>
        /// </list>
        /// </summary>
        /// <param name="load">Fish and feed in the tank today.</param>
        /// <param name="flows">The tank's share of the lot's flows.</param>
        /// <param name="volumeM3">Tank water volume (m³).</param>
        /// <param name="tankC">Tank temperature (°C).</param>
        /// <param name="intakeC">Intake water temperature (°C), before heating.</param>
        /// <param name="salinityPsu">Salinity (PSU).</param>
        /// <param name="template">The unit's template (stripping efficiency).</param>
        /// <param name="biofilterKgTanPerDay">TAN-N the tank's share of the biofilter can nitrify today (kg).</param>
        /// <param name="previousNo3NMgL">The tank's nitrate yesterday (mg N/L).</param>
        /// <param name="calibration">Land calibration.</param>
        /// <param name="oxygenSupplyShare">Share of the oxygen need the injection can dissolve (1 = full supply).</param>
        public static TankWater SolveTank(in TankLoad load, in TankFlows flows, double volumeM3, double tankC,
            double intakeC, double salinityPsu, LandTemplateCalibration template, double biofilterKgTanPerDay,
            double previousNo3NMgL, LandCalibration calibration, double oxygenSupplyShare = 1.0)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double feed = Math.Max(0.0, load.FeedKg);
            var q = new TankFlows(Math.Max(0.0, flows.NewM3), Math.Max(0.0, flows.ReusedM3),
                Math.Clamp(flows.StrippedM3, 0.0, Math.Max(0.0, flows.ReusedM3)));
            double eta = Math.Clamp(template.StripEfficiency, 0.0, 1.0);

            var (tan, removedG) = Ammonia(calibration.Tan.KgTanPerKgFeed * feed * GramsPerKg,
                Math.Max(0.0, biofilterKgTanPerDay) * GramsPerKg, calibration.Tan, q);
            double fishO2G = calibration.Oxygen.KgO2PerKgFeed * feed * GramsPerKg;
            double nitrO2G = calibration.Oxygen.NitrificationGO2PerGTan * removedG;
            var (doTank, needG, injectedG) = Oxygen(fishO2G, nitrO2G, q, eta, tankC, intakeC, salinityPsu,
                calibration.Oxygen, oxygenSupplyShare);
            double co2 = Co2(calibration.Co2.Co2PerO2 * fishO2G, q, eta, calibration.Intake.Co2MgL);
            double no3 = Nitrate(previousNo3NMgL, removedG, q.NewM3, volumeM3, calibration.Intake.No3NMgL);
            double appetite = Co2AppetiteFactor(co2, calibration.Co2) * TanAppetiteFactor(tan, calibration.Tan);
            return new TankWater(doTank, co2, tan, no3, appetite, fishO2G / GramsPerKg, nitrO2G / GramsPerKg,
                removedG / GramsPerKg, needG / GramsPerKg, injectedG / GramsPerKg);
        }

        /// <summary>
        /// Ammonia (g/day, mg/L): excretion E leaves with the new water and
        /// through the biofilter, E = C·Q_new + min(capacity, ε·C·Q_rec).
        /// </summary>
        private static (double TanMgL, double RemovedG) Ammonia(double excretedG, double capacityG, LandTanCalibration tan,
            in TankFlows q)
        {
            if (excretedG <= 0.0) return (0.0, 0.0);
            double passEff = Math.Clamp(tan.PassRemovalEfficiency, 0.0, 1.0);
            double sink = q.NewM3 + (capacityG > 0.0 ? passEff * q.ReusedM3 : 0.0);
            if (sink <= 0.0) return (double.PositiveInfinity, 0.0);
            double c = excretedG / sink;
            double removed = capacityG > 0.0 ? passEff * c * q.ReusedM3 : 0.0;
            if (removed <= capacityG) return (c, removed);
            return (q.NewM3 > 0.0 ? (excretedG - capacityG) / q.NewM3 : double.PositiveInfinity, capacityG);
        }

        /// <summary>
        /// Oxygen (g/day, mg/L): the fish and the unrestored nitrification draw
        /// on what the new water brings down to the outlet DO, what the strippers
        /// give the reused water back toward air saturation, and the injection.
        /// The injection dissolves the need at a full supply; short of it, the
        /// outlet falls to the DO at which the balance closes.
        /// </summary>
        private static (double DoMgL, double NeedG, double InjectedG) Oxygen(double fishO2G, double nitrificationO2G,
            in TankFlows q, double eta, double tankC, double intakeC, double salinityPsu, LandOxygenCalibration oxygen,
            double supplyShare)
        {
            double doSatTank = WaterQuality.CalculateOxygenSaturation(tankC, salinityPsu);
            double doIn = WaterQuality.CalculateOxygenSaturation(intakeC, salinityPsu);
            double doOut = oxygen.TankOutletDoMgL;
            double unrestored = nitrificationO2G * (1.0 - eta * q.StrippedShare);
            double need = Math.Max(0.0,
                fishO2G + unrestored - q.NewM3 * (doIn - doOut) - eta * q.StrippedM3 * (doSatTank - doOut));
            double share = Math.Clamp(double.IsFinite(supplyShare) ? supplyShare : 1.0, 0.0, 1.0);
            double injected = need * share;
            if (share >= 1.0 || need <= 0.0) return (doOut, need, injected);
            double denominator = q.NewM3 + eta * q.StrippedM3;
            double doTank = denominator > 0.0
                ? (q.NewM3 * doIn + eta * q.StrippedM3 * doSatTank + injected - fishO2G - unrestored) / denominator
                : 0.0;
            return (Math.Clamp(doTank, 0.0, doOut), need, injected);
        }

        /// <summary>CO2 (mg/L): C = (C_new·Q_new + P)/(Q_new + η·Q_stripped).</summary>
        private static double Co2(double producedG, in TankFlows q, double eta, double intakeMgL)
        {
            double sink = q.NewM3 + eta * q.StrippedM3;
            if (sink > 0.0) return (intakeMgL * q.NewM3 + producedG) / sink;
            return producedG > 0.0 ? double.PositiveInfinity : intakeMgL;
        }

        /// <summary>Nitrate (mg N/L): relaxes toward the dilution steady state at the tank's new-water exchange.</summary>
        private static double Nitrate(double previousMgL, double removedG, double newM3, double volumeM3, double intakeMgL)
        {
            if (!(volumeM3 > 0.0)) return previousMgL;
            if (!(newM3 > 0.0)) return previousMgL + removedG / volumeM3;
            double steady = intakeMgL + removedG / newM3;
            return steady + (previousMgL - steady) * Math.Exp(-newM3 / volumeM3);
        }

        // ── Effects on the fish ─────────────────────────────────────────────

        /// <summary>
        /// Appetite at <paramref name="co2MgL"/> of CO2: 1 up to the full level
        /// (12 mg/L, [L:S1]), falling linearly through the limit's value (0.6 at
        /// 20 mg/L) and on, never below 0.
        /// </summary>
        public static double Co2AppetiteFactor(double co2MgL, LandCo2Calibration co2)
        {
            if (co2 == null) throw new ArgumentNullException(nameof(co2));
            if (!(co2MgL > co2.AppetiteFullMgL)) return 1.0;
            if (double.IsPositiveInfinity(co2MgL)) return 0.0;
            double span = co2.AppetiteLimitMgL - co2.AppetiteFullMgL;
            if (span <= 0.0) return Math.Clamp(co2.AppetiteAtLimit, 0.0, 1.0);
            double slope = (1.0 - co2.AppetiteAtLimit) / span;
            return Math.Clamp(1.0 - slope * (co2MgL - co2.AppetiteFullMgL), 0.0, 1.0);
        }

        /// <summary>Appetite at <paramref name="tanMgL"/> of TAN: 1 up to the limit, then the slope, never below 0.</summary>
        public static double TanAppetiteFactor(double tanMgL, LandTanCalibration tan)
        {
            if (tan == null) throw new ArgumentNullException(nameof(tan));
            if (!(tanMgL > tan.AppetiteLimitMgL)) return 1.0;
            if (double.IsPositiveInfinity(tanMgL)) return 0.0;
            return Math.Clamp(1.0 - tan.AppetiteLossPerMgL * (tanMgL - tan.AppetiteLimitMgL), 0.0, 1.0);
        }

        /// <summary>
        /// Growth multiplier at <paramref name="densityKgM3"/> (spec §3.3
        /// "Density"): 1 up to the knee (50 kg/m³, [L:S2]), falling linearly to
        /// 0.85 at 85 kg/m³ and on along the same line, never below 0.
        /// </summary>
        public static double DensityGrowthMultiplier(double densityKgM3, LandDensityCalibration density)
        {
            if (density == null) throw new ArgumentNullException(nameof(density));
            if (!(densityKgM3 > density.KneeKgM3)) return 1.0;
            double span = density.ReferenceKgM3 - density.KneeKgM3;
            if (span <= 0.0) return Math.Clamp(density.GrowthAtReference, 0.0, 1.0);
            double slope = (1.0 - density.GrowthAtReference) / span;
            return Math.Clamp(1.0 - slope * (densityKgM3 - density.KneeKgM3), 0.0, 1.0);
        }
    }
}
