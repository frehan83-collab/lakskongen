using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Land
{
    /// <summary>What a lot projection runs: a unit, its strategy and plot, the water it draws and the window measured.</summary>
    public sealed record LandProjectionSettings
    {
        /// <summary>The unit projected.</summary>
        public LandTemplate Template { get; init; } = LandTemplate.HfsLot;

        /// <summary>The Vannstrategi held through the run.</summary>
        public LandWaterStrategy Strategy { get; init; }

        /// <summary>The Vannstrategi of the warm-up days; <see cref="Strategy"/> when null.</summary>
        public LandWaterStrategy? WarmupStrategy { get; init; }

        /// <summary>The plot's intake side.</summary>
        public LandIntakeSite Site { get; init; }

        /// <summary>True when the lot has heat recovery.</summary>
        public bool HeatRecovery { get; init; } = true;

        /// <summary>True when the oxygen comes from an on-site generator.</summary>
        public bool OnSiteO2Generator { get; init; }

        /// <summary>Production area whose climatology the shallow intake draws when <see cref="ShallowWaterC"/> is not given.</summary>
        public int ProductionArea { get; init; } = ProductionAreas.NationalReference;

        /// <summary>The shallow intake's temperature per sim day (°C); the area's climatology when null.</summary>
        public Func<int, double>? ShallowWaterC { get; init; }

        /// <summary>First measured sim day.</summary>
        public int StartDay { get; init; }

        /// <summary>Days measured.</summary>
        public int Days { get; init; } = Core.SimCalendar.DaysPerYear;

        /// <summary>Days run before the window, from staggered first stockings, so the window sees a lot in steady state.</summary>
        public int WarmupDays { get; init; } = 420;

        /// <summary>The stocking plan; the template's at the strategy's setpoint when null.</summary>
        public LandStockingPlan? Plan { get; init; }

        /// <summary>Peak density the plan stocks each tank for (kg/m³); the template's when null.</summary>
        public double? PlanPeakDensityKgM3 { get; init; }

        /// <summary>True to keep every measured day's record in <see cref="LandProjectionResult.Daily"/>.</summary>
        public bool RecordDays { get; init; }
    }

    /// <summary>One measured day of a projection.</summary>
    public readonly record struct LandProjectionDay(
        int Day,
        double ShallowC,
        double IntakeC,
        double TankC,
        double DeepShare,
        bool PumpLimited,
        bool HeatCapacityBound,
        LandEnergyDay Energy,
        double LoxKg,
        double FeedKg,
        double BiomassKg,
        double MeanCo2MgL,
        double MaxCo2MgL,
        double MeanAppetite);

    /// <summary>The totals of a projection over its measured window.</summary>
    public sealed record LandProjectionResult
    {
        /// <summary>Days measured.</summary>
        public int Days { get; init; }

        /// <summary>Energy summed over the window.</summary>
        public LandEnergyDay Energy { get; init; }

        /// <summary>Liquid oxygen bought (kg).</summary>
        public double LoxKg { get; init; }

        /// <summary>Feed eaten (kg).</summary>
        public double FeedKg { get; init; }

        /// <summary>Live fish harvested (kg).</summary>
        public double HarvestedKg { get; init; }

        /// <summary>Live smolt weight stocked (kg).</summary>
        public double StockedKg { get; init; }

        /// <summary>Standing biomass at the window's start (kg).</summary>
        public double StandingStartKg { get; init; }

        /// <summary>Standing biomass at the window's end (kg).</summary>
        public double StandingEndKg { get; init; }

        /// <summary>Biomass-days over the window (kg·day): mean standing biomass × days.</summary>
        public double BiomassDaysKg { get; init; }

        /// <summary>Water volume of the lot (m³).</summary>
        public double LotVolumeM3 { get; init; }

        /// <summary>Feed-weighted mean CO2 over the window (mg/L).</summary>
        public double MeanCo2MgL { get; init; }

        /// <summary>Highest CO2 in any tank on any day (mg/L).</summary>
        public double MaxCo2MgL { get; init; }

        /// <summary>Highest density any tank stood at on any measured day (kg/m³).</summary>
        public double MaxTankDensityKgM3 { get; init; }

        /// <summary>Biomass-weighted mean appetite factor over the window.</summary>
        public double MeanAppetite { get; init; }

        /// <summary>Days the intake pumps limited the circulation.</summary>
        public int PumpLimitedDays { get; init; }

        /// <summary>Days the heat pumps ran at capacity and the tanks missed the setpoint.</summary>
        public int HeatBoundDays { get; init; }

        /// <summary>Mean tank temperature over the window (°C).</summary>
        public double MeanTankC { get; init; }

        /// <summary>Every measured day, when recorded.</summary>
        public IReadOnlyList<LandProjectionDay> Daily { get; init; } = Array.Empty<LandProjectionDay>();

        /// <summary>
        /// Live weight produced in the window, harvest-volume measure (kg):
        /// harvested plus the change in standing biomass. Salmon Evolution's
        /// kWh per kg LW and O2 per kg LW are on this basis ([L:R2]).
        /// </summary>
        public double ProducedKg => HarvestedKg + StandingEndKg - StandingStartKg;

        /// <summary>Net growth in the window (kg): <see cref="ProducedKg"/> less the smolt weight stocked.</summary>
        public double NetGrowthKg => ProducedKg - StockedKg;

        /// <summary>
        /// Electricity on the meter per kg of live-weight biomass growth
        /// (kWh/kg): the basis of Salmon Evolution's "kWh/kg biomass (LW)"
        /// ([L:R2]), the F2-12 energy anchors.
        /// </summary>
        public double KWhPerKgGrowth => NetGrowthKg > 0.0 ? Energy.TotalKWh / NetGrowthKg : double.PositiveInfinity;

        /// <summary>Electricity on the meter per kg LW produced, smolt weight included (kWh/kg).</summary>
        public double KWhPerKgProduced => ProducedKg > 0.0 ? Energy.TotalKWh / ProducedKg : double.PositiveInfinity;

        /// <summary>
        /// Liquid oxygen bought per kg of live-weight biomass growth (kg O2/kg):
        /// the basis of [L:R2]'s "oxygen consumption (biomass growth (LW))".
        /// </summary>
        public double LoxPerKgGrowth => NetGrowthKg > 0.0 ? LoxKg / NetGrowthKg : double.PositiveInfinity;

        /// <summary>Economic FCR: feed per kg of net growth.</summary>
        public double EconomicFcr => NetGrowthKg > 0.0 ? FeedKg / NetGrowthKg : double.PositiveInfinity;

        /// <summary>Mean standing density over the window (kg/m³ of lot volume).</summary>
        public double MeanDensityKgM3 => Days > 0 && LotVolumeM3 > 0.0 ? BiomassDaysKg / Days / LotVolumeM3 : 0.0;

        /// <summary>Feed per kg of standing biomass per day (the mean specific feeding rate).</summary>
        public double FeedPerBiomassDay => BiomassDaysKg > 0.0 ? FeedKg / BiomassDaysKg : 0.0;

        /// <summary>Production index: live weight produced per m³ of tank per year (kg/m³/yr).</summary>
        public double ProductionIndexKgM3Yr => Days > 0 && LotVolumeM3 > 0.0
            ? ProducedKg / LotVolumeM3 * Core.SimCalendar.DaysPerYear / Days
            : 0.0;
    }

    /// <summary>
    /// Runs one land lot day by day (roadmap F2-12): every tank stocked on the
    /// plan, staggered so the lot reaches a steady rotation, its fish grown on
    /// the S2-03 growth law at the tank water of the previous day's land tick
    /// (appetite from CO2 and TAN, density stress, continuous light), and the
    /// lot's water, heat and energy solved after the day's feeding, as the
    /// engine orders it. Pure and deterministic (no RNG): the Vannstrategi
    /// preview, the profit sweep and the F2-12 anchors run on it.
    /// </summary>
    public static class LandLotProjector
    {
        /// <summary>Project with the default growth law.</summary>
        public static LandProjectionResult Run(LandProjectionSettings settings, LandCalibration calibration)
            => Run(settings, calibration, TgcGrowthLaw.Default);

        /// <summary>Project on <paramref name="law"/>.</summary>
        public static LandProjectionResult Run(LandProjectionSettings settings, LandCalibration calibration, TgcGrowthLaw law)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (law == null) throw new ArgumentNullException(nameof(law));

            var template = calibration.For(settings.Template);
            var lot = LandSiteTemplates.BuildLot("projection", 1, settings.Template, calibration);
            lot.HeatRecovery = settings.HeatRecovery;
            lot.BiofilterMaturity = 1.0;
            foreach (var tank in lot.Tanks)
                tank.TemperatureC = settings.Strategy.SetpointC;

            var plan = settings.Plan ?? LandStockingPlan.For(template, calibration.Production) with
            {
                TemperatureC = settings.Strategy.SetpointC,
            };
            var forecast = LandStockingPlanner.Project(law, plan);
            double peak = settings.PlanPeakDensityKgM3 ?? template.PlanPeakDensityKgM3;
            int n = lot.Tanks.Count;
            var counts = new double[n];
            var weights = new double[n];
            var cvs = new double[n];
            var fallow = new int[n];
            var firstStock = new int[n];
            var planned = new int[n];
            int firstDay = settings.StartDay - Math.Max(0, settings.WarmupDays);
            int rotation = forecast.CycleDays + calibration.Production.FallowDays;
            for (int k = 0; k < n; k++)
            {
                planned[k] = forecast.MaxSmolts(lot.Tanks[k].VolumeM3, peak);
                firstStock[k] = firstDay + (int)Math.Round((double)k * rotation / n);
                fallow[k] = -1; // never stocked yet
            }

            var area = ProductionAreas.HasClimatology(settings.ProductionArea)
                ? settings.ProductionArea
                : ProductionAreas.NationalReference;
            Func<int, double> shallow = settings.ShallowWaterC ?? (day => ProductionAreas.ClimatologyC(area, day));
            var production = calibration.Production;
            var loads = new TankLoad[n];
            var daily = settings.RecordDays ? new List<LandProjectionDay>(Math.Max(0, settings.Days)) : null;

            LandEnergyDay energy = default;
            double lox = 0.0, feedSum = 0.0, harvested = 0.0, stocked = 0.0, biomassDays = 0.0;
            double standingStart = 0.0, co2Weighted = 0.0, maxCo2 = 0.0, appetiteWeighted = 0.0, appetiteBiomass = 0.0;
            double tankCSum = 0.0;
            int pumpLimited = 0, heatBound = 0;
            double maxTankDensity = 0.0;
            int endDay = settings.StartDay + Math.Max(0, settings.Days);

            for (int day = firstDay; day < endDay; day++)
            {
                bool measuring = day >= settings.StartDay;
                if (day == settings.StartDay)
                    standingStart = Standing(counts, weights);

                // Stock the tanks whose turn it is.
                for (int k = 0; k < n; k++)
                {
                    bool due = fallow[k] == 0 || (fallow[k] < 0 && day >= firstStock[k]);
                    if (!due || counts[k] > 0.0) continue;
                    counts[k] = planned[k];
                    weights[k] = plan.ReleaseWeightKg;
                    cvs[k] = law.Calibration.SmoltWeightCv;
                    fallow[k] = int.MaxValue;
                    if (measuring) stocked += counts[k] * weights[k];
                }

                // Biology: grow and feed on yesterday's tank water.
                double biomassToday = 0.0, appetiteToday = 0.0;
                for (int k = 0; k < n; k++)
                {
                    if (!(counts[k] > 0.0))
                    {
                        loads[k] = default;
                        if (fallow[k] > 0 && fallow[k] != int.MaxValue) fallow[k]--;
                        continue;
                    }
                    var tank = lot.Tanks[k];
                    double density = counts[k] * weights[k] / tank.VolumeM3;
                    double doSaturation = WaterQuality.CalculateDOSaturation(tank.DoMgL, tank.TemperatureC);
                    var conditions = new GrowthConditions(tank.TemperatureC,
                        AppetiteModifier: tank.AppetiteFactor * WaterQuality.CalculateDOSaturationModifier(doSaturation),
                        GrowthMultiplier: plan.GrowthMultiplier,
                        EnvironmentMultiplier: LandWaterModel.DensityGrowthMultiplier(density, calibration.Density),
                        DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
                    double fcr = production.BiologicalFcrAt2Kg * law.FcrSizeFactor(weights[k]);
                    var grown = law.GrowOneDay(weights[k], cvs[k], LifeStage.Seawater, conditions, fcr);
                    double feed = counts[k] * grown.EatenFeedKg;
                    weights[k] = grown.WeightKg;
                    cvs[k] = grown.WeightCv;
                    counts[k] *= forecast.DailySurvival;
                    double biomass = counts[k] * weights[k];
                    loads[k] = new TankLoad(biomass, feed);
                    if (measuring) maxTankDensity = Math.Max(maxTankDensity, biomass / tank.VolumeM3);
                    biomassToday += biomass;
                    appetiteToday += biomass * tank.AppetiteFactor;
                    if (weights[k] >= plan.HarvestWeightKg)
                    {
                        if (measuring) harvested += biomass;
                        counts[k] = 0.0;
                        fallow[k] = production.FallowDays; // the tank held and fed the fish today
                    }
                }

                // The land tick on the day's feed.
                var strategy = measuring ? settings.Strategy : settings.WarmupStrategy ?? settings.Strategy;
                var result = LandLotDaySolver.Solve(new LandLotDayInput(lot, strategy, settings.Site,
                    shallow(day), loads, OnSiteO2Generator: settings.OnSiteO2Generator), calibration);
                LandLotDaySolver.Apply(lot, result);

                if (!measuring) continue;
                energy += result.Energy;
                lox += result.LoxPurchasedKg;
                feedSum += result.FeedKg;
                biomassDays += Standing(counts, weights);
                co2Weighted += result.MeanCo2MgL * result.FeedKg;
                maxCo2 = Math.Max(maxCo2, result.MaxCo2MgL);
                appetiteWeighted += appetiteToday;
                appetiteBiomass += biomassToday;
                tankCSum += result.Heat.TankC;
                if (result.Flows.PumpLimited) pumpLimited++;
                if (result.Heat.CapacityBound) heatBound++;
                daily?.Add(new LandProjectionDay(day, shallow(day), result.IntakeC, result.Heat.TankC, result.DeepShare,
                    result.Flows.PumpLimited, result.Heat.CapacityBound, result.Energy, result.LoxPurchasedKg, result.FeedKg,
                    biomassToday, result.MeanCo2MgL, result.MaxCo2MgL,
                    biomassToday > 0.0 ? appetiteToday / biomassToday : 1.0));
            }

            int days = Math.Max(0, settings.Days);
            double biomassTotal = biomassDays;
            return new LandProjectionResult
            {
                Days = days,
                Energy = energy,
                LoxKg = lox,
                FeedKg = feedSum,
                HarvestedKg = harvested,
                StockedKg = stocked,
                StandingStartKg = standingStart,
                StandingEndKg = Standing(counts, weights),
                BiomassDaysKg = biomassTotal,
                LotVolumeM3 = lot.VolumeM3,
                MeanCo2MgL = feedSum > 0.0 ? co2Weighted / feedSum : 0.0,
                MaxCo2MgL = maxCo2,
                MaxTankDensityKgM3 = maxTankDensity,
                MeanAppetite = appetiteBiomass > 0.0 ? appetiteWeighted / appetiteBiomass : 1.0,
                PumpLimitedDays = pumpLimited,
                HeatBoundDays = heatBound,
                MeanTankC = days > 0 ? tankCSum / days : settings.Strategy.SetpointC,
                Daily = daily ?? (IReadOnlyList<LandProjectionDay>)Array.Empty<LandProjectionDay>(),
            };
        }

        private static double Standing(double[] counts, double[] weights)
        {
            double kg = 0.0;
            for (int k = 0; k < counts.Length; k++) kg += counts[k] * weights[k];
            return kg;
        }
    }
}
