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
            var run = new LotRun(settings, calibration, law);
            int endDay = settings.StartDay + Math.Max(0, settings.Days);
            for (int day = run.FirstDay; day < endDay; day++)
                run.Step(day);
            return run.Result();
        }

        /// <summary>One projection's lot, its tanks' fish and the window's running totals.</summary>
        private sealed class LotRun
        {
            private readonly LandProjectionSettings _settings;
            private readonly LandCalibration _calibration;
            private readonly TgcGrowthLaw _law;
            private readonly LandLot _lot;
            private readonly LandStockingPlan _plan;
            private readonly LandStockingForecast _forecast;
            private readonly Func<int, double> _shallow;
            private readonly double[] _counts, _weights, _cvs;
            private readonly int[] _fallow, _firstStock, _planned;
            private readonly TankLoad[] _loads;
            private readonly List<LandProjectionDay>? _daily;

            /// <summary>A tank that holds fish: no fallow countdown runs.</summary>
            private const int Stocked = int.MaxValue;
            /// <summary>A tank not yet stocked for the first time.</summary>
            private const int NeverStocked = -1;

            private LandEnergyDay _energy;
            private double _lox, _feed, _harvested, _stocked, _biomassDays, _standingStart;
            private double _co2Weighted, _maxCo2, _appetiteWeighted, _appetiteBiomass, _tankCSum, _maxTankDensity;
            private int _pumpLimited, _heatBound;

            public int FirstDay { get; }

            public LotRun(LandProjectionSettings settings, LandCalibration calibration, TgcGrowthLaw law)
            {
                _settings = settings;
                _calibration = calibration;
                _law = law;
                var template = calibration.For(settings.Template);
                _lot = LandSiteTemplates.BuildLot("projection", 1, settings.Template, calibration);
                _lot.HeatRecovery = settings.HeatRecovery;
                _lot.BiofilterMaturity = 1.0;
                foreach (var tank in _lot.Tanks)
                    tank.TemperatureC = settings.Strategy.SetpointC;

                _plan = settings.Plan ?? LandStockingPlan.For(template, calibration.Production) with
                {
                    TemperatureC = settings.Strategy.SetpointC,
                };
                _forecast = LandStockingPlanner.Project(law, _plan);
                double peak = settings.PlanPeakDensityKgM3 ?? template.PlanPeakDensityKgM3;
                int n = _lot.Tanks.Count;
                _counts = new double[n];
                _weights = new double[n];
                _cvs = new double[n];
                _fallow = new int[n];
                _firstStock = new int[n];
                _planned = new int[n];
                _loads = new TankLoad[n];
                FirstDay = settings.StartDay - Math.Max(0, settings.WarmupDays);
                int rotation = _forecast.CycleDays + calibration.Production.FallowDays;
                for (int k = 0; k < n; k++)
                {
                    _planned[k] = _forecast.MaxSmolts(_lot.Tanks[k].VolumeM3, peak);
                    _firstStock[k] = FirstDay + (int)Math.Round((double)k * rotation / n);
                    _fallow[k] = NeverStocked;
                }

                int area = ProductionAreas.HasClimatology(settings.ProductionArea)
                    ? settings.ProductionArea
                    : ProductionAreas.NationalReference;
                _shallow = settings.ShallowWaterC ?? (day => ProductionAreas.ClimatologyC(area, day));
                _daily = settings.RecordDays ? new List<LandProjectionDay>(Math.Max(0, settings.Days)) : null;
            }

            /// <summary>One day: stock the tanks whose turn it is, grow and feed, then the land tick.</summary>
            public void Step(int day)
            {
                bool measuring = day >= _settings.StartDay;
                if (day == _settings.StartDay)
                    _standingStart = Standing();
                StockDueTanks(day, measuring);
                var (biomass, appetite) = GrowAndFeed(measuring);
                var strategy = measuring ? _settings.Strategy : _settings.WarmupStrategy ?? _settings.Strategy;
                var result = LandLotDaySolver.Solve(new LandLotDayInput(_lot, strategy, _settings.Site, _shallow(day),
                    _loads, OnSiteO2Generator: _settings.OnSiteO2Generator), _calibration);
                LandLotDaySolver.Apply(_lot, result);
                if (measuring)
                    Record(day, result, biomass, appetite);
            }

            private void StockDueTanks(int day, bool measuring)
            {
                for (int k = 0; k < _counts.Length; k++)
                {
                    bool due = _fallow[k] == 0 || (_fallow[k] == NeverStocked && day >= _firstStock[k]);
                    if (!due || _counts[k] > 0.0) continue;
                    _counts[k] = _planned[k];
                    _weights[k] = _plan.ReleaseWeightKg;
                    _cvs[k] = _law.Calibration.SmoltWeightCv;
                    _fallow[k] = Stocked;
                    if (measuring) _stocked += _counts[k] * _weights[k];
                }
            }

            /// <summary>
            /// The biology on yesterday's tank water: every stocked tank grows and
            /// eats on the growth law, loses the plan's daily share, and is
            /// harvested (after the day's feeding) once its fish reach the
            /// harvest weight. Returns the day's biomass and appetite-weighted biomass.
            /// </summary>
            private (double Biomass, double AppetiteBiomass) GrowAndFeed(bool measuring)
            {
                double biomassToday = 0.0, appetiteToday = 0.0;
                for (int k = 0; k < _counts.Length; k++)
                {
                    if (!(_counts[k] > 0.0))
                    {
                        _loads[k] = default;
                        if (_fallow[k] > 0 && _fallow[k] != Stocked) _fallow[k]--;
                        continue;
                    }
                    var tank = _lot.Tanks[k];
                    double density = _counts[k] * _weights[k] / tank.VolumeM3;
                    double doSaturation = WaterQuality.CalculateDOSaturation(tank.DoMgL, tank.TemperatureC);
                    var conditions = new GrowthConditions(tank.TemperatureC,
                        AppetiteModifier: tank.AppetiteFactor * WaterQuality.CalculateDOSaturationModifier(doSaturation),
                        GrowthMultiplier: _plan.GrowthMultiplier,
                        EnvironmentMultiplier: LandWaterModel.DensityGrowthMultiplier(density, _calibration.Density),
                        DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
                    double fcr = _calibration.Production.BiologicalFcrAt2Kg * _law.FcrSizeFactor(_weights[k]);
                    var grown = _law.GrowOneDay(_weights[k], _cvs[k], LifeStage.Seawater, conditions, fcr);
                    double feed = _counts[k] * grown.EatenFeedKg;
                    _weights[k] = grown.WeightKg;
                    _cvs[k] = grown.WeightCv;
                    _counts[k] *= _forecast.DailySurvival;
                    double biomass = _counts[k] * _weights[k];
                    _loads[k] = new TankLoad(biomass, feed);
                    if (measuring) _maxTankDensity = Math.Max(_maxTankDensity, biomass / tank.VolumeM3);
                    biomassToday += biomass;
                    appetiteToday += biomass * tank.AppetiteFactor;
                    if (_weights[k] < _plan.HarvestWeightKg) continue;
                    if (measuring) _harvested += biomass;
                    _counts[k] = 0.0;
                    _fallow[k] = _calibration.Production.FallowDays; // the tank held and fed the fish today
                }
                return (biomassToday, appetiteToday);
            }

            private void Record(int day, LandLotDayResult result, double biomassToday, double appetiteToday)
            {
                _energy += result.Energy;
                _lox += result.LoxPurchasedKg;
                _feed += result.FeedKg;
                _biomassDays += Standing();
                _co2Weighted += result.MeanCo2MgL * result.FeedKg;
                _maxCo2 = Math.Max(_maxCo2, result.MaxCo2MgL);
                _appetiteWeighted += appetiteToday;
                _appetiteBiomass += biomassToday;
                _tankCSum += result.Heat.TankC;
                if (result.Flows.PumpLimited) _pumpLimited++;
                if (result.Heat.CapacityBound) _heatBound++;
                _daily?.Add(new LandProjectionDay(day, _shallow(day), result.IntakeC, result.Heat.TankC, result.DeepShare,
                    result.Flows.PumpLimited, result.Heat.CapacityBound, result.Energy, result.LoxPurchasedKg, result.FeedKg,
                    biomassToday, result.MeanCo2MgL, result.MaxCo2MgL,
                    biomassToday > 0.0 ? appetiteToday / biomassToday : 1.0));
            }

            public LandProjectionResult Result()
            {
                int days = Math.Max(0, _settings.Days);
                return new LandProjectionResult
                {
                    Days = days,
                    Energy = _energy,
                    LoxKg = _lox,
                    FeedKg = _feed,
                    HarvestedKg = _harvested,
                    StockedKg = _stocked,
                    StandingStartKg = _standingStart,
                    StandingEndKg = Standing(),
                    BiomassDaysKg = _biomassDays,
                    LotVolumeM3 = _lot.VolumeM3,
                    MeanCo2MgL = _feed > 0.0 ? _co2Weighted / _feed : 0.0,
                    MaxCo2MgL = _maxCo2,
                    MaxTankDensityKgM3 = _maxTankDensity,
                    MeanAppetite = _appetiteBiomass > 0.0 ? _appetiteWeighted / _appetiteBiomass : 1.0,
                    PumpLimitedDays = _pumpLimited,
                    HeatBoundDays = _heatBound,
                    MeanTankC = days > 0 ? _tankCSum / days : _settings.Strategy.SetpointC,
                    Daily = _daily ?? (IReadOnlyList<LandProjectionDay>)Array.Empty<LandProjectionDay>(),
                };
            }

            private double Standing()
            {
                double kg = 0.0;
                for (int k = 0; k < _counts.Length; k++) kg += _counts[k] * _weights[k];
                return kg;
            }
        }
    }
}
