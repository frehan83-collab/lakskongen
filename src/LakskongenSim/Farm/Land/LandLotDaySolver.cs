using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Land
{
    /// <summary>What the land tick knows about one lot today.</summary>
    /// <param name="Lot">The lot (its tanks' yesterday's nitrate is read; nothing is written).</param>
    /// <param name="Strategy">The unit's Vannstrategi.</param>
    /// <param name="Site">The plot's intake side.</param>
    /// <param name="ShallowWaterC">Today's temperature of the shallow (25 m) intake water (°C): the zone's sea (S2-02).</param>
    /// <param name="Loads">Fish and feed per tank today, in <see cref="LandLot.Tanks"/> order.</param>
    /// <param name="SalinityPsu">Salinity of the intake (PSU).</param>
    /// <param name="BiofilterHealth">Health of the biofilter (0-1): <c>RASFacility.BiofilterEfficiency</c> when wired.</param>
    /// <param name="OxygenAvailableKg">Liquid oxygen on hand today (kg); infinite when the supply never runs short.</param>
    /// <param name="OnSiteO2Generator">True when the oxygen comes from an on-site generator (its electricity is on the meter).</param>
    public sealed record LandLotDayInput(
        LandLot Lot,
        LandWaterStrategy Strategy,
        LandIntakeSite Site,
        double ShallowWaterC,
        IReadOnlyList<TankLoad> Loads,
        double SalinityPsu = HoldingWater.SeawaterSalinityPsu,
        double BiofilterHealth = 1.0,
        double OxygenAvailableKg = double.PositiveInfinity,
        bool OnSiteO2Generator = false);

    /// <summary>One lot's day: flows, temperatures, energy, oxygen and each tank's water.</summary>
    public sealed record LandLotDayResult(
        LandFlows Flows,
        double DeepShare,
        double IntakeC,
        LandHeatBalance Heat,
        LandEnergyDay Energy,
        IReadOnlyList<TankWater> Tanks,
        double FishO2Kg,
        double OxygenNeedKg,
        double OxygenInjectedKg,
        double LoxPurchasedKg,
        double FeedKg,
        double BiomassKg,
        double StockedVolumeM3)
    {
        /// <summary>Highest CO2 in a stocked tank (mg/L), 0 when none is stocked.</summary>
        public double MaxCo2MgL { get; init; }

        /// <summary>Feed-weighted mean CO2 over the stocked tanks (mg/L).</summary>
        public double MeanCo2MgL { get; init; }
    }

    /// <summary>
    /// Solves one lot for one day (roadmap F2-12): flows from the reuse and the
    /// pumps, the intake blend, the heat balance with the internal gains of
    /// pumps, UV and fish, every stocked tank's water at the tank temperature,
    /// the oxygen to inject and the energy. Pure: it reads the lot and the
    /// input and writes nothing; <see cref="Apply"/> writes the tanks' water.
    /// Order of the engine (spec §3.3): the biology feeds the fish first, then
    /// this tick runs on the day's feed, and its appetite factors are read by
    /// the next day's biology.
    /// </summary>
    public static class LandLotDaySolver
    {
        /// <summary>Solve the lot's day.</summary>
        public static LandLotDayResult Solve(LandLotDayInput input, LandCalibration calibration)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            var lot = input.Lot ?? throw new ArgumentException("The input names no lot", nameof(input));
            var template = calibration.For(lot.Template);
            var tanks = lot.Tanks;
            if (input.Loads == null || input.Loads.Count != tanks.Count)
                throw new ArgumentException("One load per tank, in tank order", nameof(input));

            double stockedVolume = 0.0, feed = 0.0, biomass = 0.0, fishO2 = 0.0;
            for (int i = 0; i < tanks.Count; i++)
            {
                var load = input.Loads[i];
                if (!load.Stocked) continue;
                stockedVolume += tanks[i].VolumeM3;
                feed += Math.Max(0.0, load.FeedKg);
                biomass += load.BiomassKg;
                fishO2 += calibration.Oxygen.KgO2PerKgFeed * Math.Max(0.0, load.FeedKg);
            }

            var energyCal = calibration.Energy;
            var strategy = input.Strategy;
            var flows = LandWaterModel.Flows(stockedVolume, template, strategy.Reuse, input.Site.PumpCapacityM3sPerLot,
                calibration.Intake);
            double loopPumping = LandEnergyModel.LoopPumpingKWh(flows.ReusedM3, energyCal);
            double treatment = LandEnergyModel.TreatmentKWh(flows.ReusedM3, template);
            double uv = LandEnergyModel.UvKWh(flows.NewM3, input.Site, energyCal);
            double internalGain = energyCal.PumpHeatToWaterShare * (loopPumping + uv)
                + LandEnergyModel.MetabolicHeatKWh(fishO2, calibration.Oxygen);

            // Blending toward the setpoint aims the intake below it by the rise
            // the pumps and the fish give the water, so no chiller removes it.
            double deepC = calibration.Intake.DeepWaterTemperatureC;
            double perK = flows.NewM3 * energyCal.HeatKWhPerM3PerK;
            double blendTarget = strategy.SetpointC - (perK > 0.0 ? internalGain / perK : 0.0);
            double w = LandWaterModel.DeepShare(strategy, input.ShallowWaterC, deepC, blendTarget);
            double intakeC = LandWaterModel.IntakeTemperatureC(input.ShallowWaterC, deepC, w);
            double intakePumping = LandEnergyModel.IntakePumpingKWh(flows.NewM3, w, input.Site, energyCal);
            var heat = LandEnergyModel.HeatBalance(flows.NewM3, intakeC, strategy.SetpointC, internalGain,
                lot.HeatPumpMWe, lot.HeatRecovery, energyCal);

            // The biofilter: installed per lot (capacity on the lot's whole
            // volume), shared by the stocked tanks by their reused water.
            double biofilterKg = template.BiofilterKgTanPerM3Day * lot.VolumeM3
                * Math.Clamp(lot.BiofilterMaturity, 0.0, 1.0) * Math.Clamp(input.BiofilterHealth, 0.0, 1.0);

            // First pass: each tank's oxygen need at a full supply.
            var water = new TankWater[tanks.Count];
            double need = 0.0;
            for (int i = 0; i < tanks.Count; i++)
            {
                if (!input.Loads[i].Stocked) continue;
                water[i] = SolveOne(i, input, flows, stockedVolume, heat.TankC, intakeC, template, biofilterKg,
                    calibration, 1.0);
                need += water[i].OxygenNeedKg;
            }

            // Liquid oxygen short: every tank gets the same share of its need.
            double eff = Math.Clamp(calibration.Oxygen.InjectionEfficiency, 1e-3, 1.0);
            double dissolvable = double.IsPositiveInfinity(input.OxygenAvailableKg)
                ? double.PositiveInfinity
                : Math.Max(0.0, input.OxygenAvailableKg) * eff;
            double share = need > 0.0 && dissolvable < need ? dissolvable / need : 1.0;
            double injected = 0.0, maxCo2 = 0.0, co2Weighted = 0.0;
            var result = new List<TankWater>(tanks.Count);
            for (int i = 0; i < tanks.Count; i++)
            {
                if (!input.Loads[i].Stocked)
                {
                    result.Add(Idle(tanks[i]));
                    continue;
                }
                if (share < 1.0)
                    water[i] = SolveOne(i, input, flows, stockedVolume, heat.TankC, intakeC, template, biofilterKg,
                        calibration, share);
                injected += water[i].OxygenInjectedKg;
                maxCo2 = Math.Max(maxCo2, water[i].Co2MgL);
                co2Weighted += water[i].Co2MgL * Math.Max(0.0, input.Loads[i].FeedKg);
                result.Add(water[i]);
            }
            double lox = injected / eff;
            var (onSite, upstream) = LandEnergyModel.OxygenProductionKWh(lox, input.OnSiteO2Generator, calibration.Oxygen);
            var energy = new LandEnergyDay(intakePumping, loopPumping, treatment, uv, heat.HeatingKWh, heat.CoolingKWh,
                template.BaseKWhPerDay, onSite, upstream);
            return new LandLotDayResult(flows, w, intakeC, heat, energy, result, fishO2, need, injected, lox, feed, biomass,
                stockedVolume)
            {
                MaxCo2MgL = maxCo2,
                MeanCo2MgL = feed > 0.0 ? co2Weighted / feed : 0.0,
            };
        }

        /// <summary>Write the day's water into the lot's tanks (what the next day's biology reads).</summary>
        public static void Apply(LandLot lot, LandLotDayResult day)
        {
            if (lot == null) throw new ArgumentNullException(nameof(lot));
            if (day == null) throw new ArgumentNullException(nameof(day));
            if (day.Tanks.Count != lot.Tanks.Count)
                throw new ArgumentException("The day was solved for another lot", nameof(day));
            for (int i = 0; i < lot.Tanks.Count; i++)
            {
                var tank = lot.Tanks[i];
                var w = day.Tanks[i];
                tank.TemperatureC = day.Heat.TankC;
                tank.DoMgL = w.DoMgL;
                tank.Co2MgL = w.Co2MgL;
                tank.TanMgL = w.TanMgL;
                tank.No3NMgL = w.No3NMgL;
                tank.AppetiteFactor = w.AppetiteFactor;
            }
        }

        private static TankWater SolveOne(int index, LandLotDayInput input, in LandFlows flows, double stockedVolume,
            double tankC, double intakeC, LandTemplateCalibration template, double biofilterKg, LandCalibration calibration,
            double oxygenShare)
        {
            var tank = input.Lot.Tanks[index];
            var tankFlows = LandWaterModel.ShareOf(flows, tank.VolumeM3, stockedVolume);
            double tankBiofilter = flows.ReusedM3 > 0.0 ? biofilterKg * tankFlows.ReusedM3 / flows.ReusedM3 : 0.0;
            return LandWaterModel.SolveTank(input.Loads[index], tankFlows, tank.VolumeM3, tankC, intakeC, input.SalinityPsu,
                template, tankBiofilter, tank.No3NMgL, calibration, oxygenShare);
        }

        /// <summary>An empty tank's water: unchanged, full appetite for the fish stocked next.</summary>
        private static TankWater Idle(LandTank tank)
            => new(tank.DoMgL, tank.Co2MgL, tank.TanMgL, tank.No3NMgL, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0);
    }
}
