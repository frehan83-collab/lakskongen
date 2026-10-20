using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Mortality model applying daily mortality rates.
    /// Uses biologically-accurate 7-stage salmon lifecycle.
    /// Disease-aware: integrates disease mortality to prevent double-application.
    /// 
    /// Mareano/Geonorge Environmental Integration:
    /// - Temperature stress increases mortality (above 16°C or below 4°C)
    /// - Low oxygen (below 5 mg/L) causes direct mortality
    /// - Poor environmental quality increases stress-related mortality
    /// - Zone-specific risk profiles applied from Norwegian production zone data
    /// </summary>
    public class MortalityModel
    {
        // Disease mortality rates per population (injected by IntegratedDiseaseSystem)
        private readonly Dictionary<string, double> _diseaseRates = new Dictionary<string, double>();
        private int _callCount = 0; // For debugging

        /// <summary>Maximum total daily mortality rate applied in one tick.</summary>
        private const double MaxTotalDailyMortalityRate = 0.95;

        /// <summary>Maximum disease mortality rate accepted per population per day.</summary>
        private const double MaxDiseaseDailyMortalityRate = 0.5;
        
        // Environmental stress tracking per population
        private readonly Dictionary<string, EnvironmentalStressState> _environmentalStress = new();

        /// <summary>
        /// Every death and live removal of the populations this model serves,
        /// by cause (S2-05). Populations book through
        /// <see cref="FishPopulation.RemoveDead"/> / <see cref="FishPopulation.RemoveAlive"/>;
        /// the engine opens each simulated day on it.
        /// </summary>
        public MortalityLedger Ledger { get; } = new();

        // Sea-phase mortality anchor. `CalibrationRegistry.Health.SeaMortalityPerCycle`
        // (real NVI 2024 ~15.4%, band 14-16%) is mapped to a daily hazard over the
        // calibrated sea grow-out duration. The sea stages (Seawater/Harvest) use this
        // anchor instead of the freshwater `GameConstants.BaselineMortalityPerDay`,
        // while disease/event mortality stays additive on top (see SetDiseaseMortalityRate).
        //
        // v1.0.3: when events are enabled the anchor is split into a husbandry
        // baseline (1 - diseaseShare) plus an explicit disease slice that the SIR
        // disease system contributes through SetDiseaseMortalityRate; the two sum
        // back to the calibrated whole-cycle anchor. With events off the full anchor
        // stays in the baseline so a deterministic no-disease run is unchanged.
        private double _seaMortalityPerCycle = CalibrationRegistry.Default.Health.SeaMortalityPerCycle;
        private double _seaPhaseDays = CalibrationRegistry.Default.Biology.GrowthToHarvestDays;
        private double _seaDiseaseShare = 0.0;
        private bool _seaDiseaseEnabled = false;
        private double _seaDailyRate = ComputeSeaDailyRate(
            CalibrationRegistry.Default.Health.SeaMortalityPerCycle,
            CalibrationRegistry.Default.Biology.GrowthToHarvestDays);

        /// <summary>
        /// Map a whole-cycle sea mortality fraction to a per-day hazard:
        /// <c>1 - (1 - perCycle)^(1/days)</c>. Whole-cycle fraction is the
        /// survival-consistent form (not perCycle/days).
        /// </summary>
        internal static double ComputeSeaDailyRate(double perCycle, double days)
        {
            if (days <= 0) return 0.0;
            double clamped = Math.Clamp(perCycle, 0.0, 0.99);
            return 1.0 - Math.Pow(1.0 - clamped, 1.0 / days);
        }

        /// <summary>
        /// Wire the calibrated whole-cycle sea mortality anchor into the live
        /// model. Called by the engine after <c>CalibrationRegistry</c> load so
        /// the runtime value is the registry value, not a code default.
        /// </summary>
        /// <param name="perCycle">Whole-cycle sea mortality anchor (fraction).</param>
        /// <param name="seaPhaseDays">Calibrated sea grow-out duration (days).</param>
        /// <param name="diseaseShare">Disease-attributable share of the anchor (0-1).</param>
        /// <param name="diseaseEnabled">When true the disease slice is removed from
        /// the baseline and left to the SIR disease system; when false the full
        /// anchor stays in the baseline (deterministic no-disease runs).</param>
        public void ConfigureSeaMortality(double perCycle, double seaPhaseDays,
            double diseaseShare = 0.0, bool diseaseEnabled = false)
        {
            _seaMortalityPerCycle = perCycle;
            _seaPhaseDays = seaPhaseDays > 0 ? seaPhaseDays : _seaPhaseDays;
            _seaDiseaseShare = Math.Clamp(diseaseShare, 0.0, 1.0);
            _seaDiseaseEnabled = diseaseEnabled;
            double nonDiseasePerCycle = diseaseEnabled
                ? perCycle * (1.0 - _seaDiseaseShare)
                : perCycle;
            _seaDailyRate = ComputeSeaDailyRate(nonDiseasePerCycle, _seaPhaseDays);
        }

        /// <summary>Per-day sea-phase baseline hazard derived from the per-cycle anchor.</summary>
        public double SeaDailyMortalityRate => _seaDailyRate;

        /// <summary>Whole-cycle sea mortality anchor currently configured.</summary>
        public double SeaMortalityPerCycle => _seaMortalityPerCycle;

        /// <summary>Disease share of the sea anchor currently configured (0-1).</summary>
        public double SeaMortalityDiseaseShare => _seaDiseaseShare;

        /// <summary>Whether the explicit disease slice is currently enabled.</summary>
        public bool IsSeaDiseaseEnabled => _seaDiseaseEnabled;

        /// <summary>Baseline daily rate for a stage: sea stages use the calibrated anchor, freshwater keeps the legacy constant.</summary>
        private double BaselineDailyRate(LifeStage stage)
            => stage is LifeStage.Seawater or LifeStage.Harvest
                ? _seaDailyRate
                : LakskongenSim.Core.GameConstants.BaselineMortalityPerDay;

        /// <summary>
        /// Stage multiplier on the baseline hazard, from real salmon mortality
        /// patterns: egg 5, alevin 4 (yolk sac), fry 3 (first feeding), parr 2,
        /// smolt 2.5 (smoltification stress), seawater 1.2, harvest size 1.
        /// </summary>
        private static double StageMultiplier(LifeStage stage) => stage switch
        {
            LifeStage.Egg => 5.0,
            LifeStage.Alevin => 4.0,
            LifeStage.Fry => 3.0,
            LifeStage.Parr => 2.0,
            LifeStage.Smolt => 2.5,
            LifeStage.Seawater => 1.2,
            LifeStage.Harvest => 1.0,
            _ => 1.0,
        };

        /// <summary>Disease hazard injected for a population today (0 when none).</summary>
        private double DiseaseRateFor(string populationId)
            => !string.IsNullOrEmpty(populationId) && _diseaseRates.TryGetValue(populationId, out double rate)
                ? rate
                : 0.0;

        /// <summary>Survivors of <paramref name="count"/> fish after <paramref name="days"/> days at a daily hazard: count × (1 − rate)^days, rounded.</summary>
        private static int Survivors(int count, int days, double dailyRate)
            => Math.Max(0, (int)Math.Round(count * Math.Pow(1.0 - dailyRate, days)));

        /// <summary>
        /// Apply mortality over days, return surviving count (rounded to int).
        /// Combines baseline, stage, and disease mortality in a single application.
        /// A count-only function: nothing is booked. Populations die through
        /// <see cref="ApplyMortality(FishPopulation, FishPopulation.Cohort, int, double, double, double)"/>,
        /// which books every death by cause.
        /// </summary>
        public int ApplyMortality(int count, int days, LifeStage stage, string populationId = "", double mortalityReduction = 0.0, double diseaseResistanceBonus = 0.0)
        {
            if (count <= 0 || days <= 0) return count;

            double baseRate = BaselineDailyRate(stage);
            double stageMultiplier = StageMultiplier(stage);
            double diseaseRate = DiseaseRateFor(populationId);

            // Research removes a fraction of each hazard once (review F1027: the
            // reduction was subtracted from the total and then overwritten, so it
            // never acted; as an absolute 0.02-0.10/day it would also have zeroed a
            // ~3e-4/day sea hazard).
            double totalRate = ResearchAdjustedRate(baseRate * stageMultiplier, diseaseRate, 0.0,
                mortalityReduction, diseaseResistanceBonus);

            int survivors = Survivors(count, days, totalRate);

            // Log to sim log for debugging (first 10 calls)
            if (_callCount < 10)
            {
                SimLog.Debug($"[MortalityModel] Call #{_callCount}: BaseRate={baseRate:F7}, Stage={stage}, StageMult={stageMultiplier:F2}, TotalRate={totalRate:F7}, Count={count}, Survivors={survivors}");
                _callCount++;
            }

            return survivors;
        }

        /// <summary>
        /// Apply the daily hazards to one cohort of <paramref name="population"/>
        /// and book the deaths (S2-05): the same survivors as
        /// <see cref="ApplyMortality(int, int, LifeStage, string, double, double)"/>,
        /// with the deaths split between the baseline anchor
        /// (<see cref="MortalityCause.BaselineAnchor"/>) and the disease hazard
        /// (<see cref="MortalityCause.Disease"/>) in proportion to the two
        /// research-adjusted hazards (<see cref="MortalityLedger.Split"/>).
        /// <paramref name="anchorHazardMultiplier"/> scales the baseline anchor
        /// hazard (the holding's and the smolt quality's terms, F2 hooks; 1 = the
        /// calibrated anchor, exactly as before F2).
        /// </summary>
        internal void ApplyMortality(FishPopulation population, FishPopulation.Cohort cohort, int days,
            double mortalityReduction, double diseaseResistanceBonus, double anchorHazardMultiplier = 1.0)
        {
            int count = cohort.Count;
            if (count <= 0 || days <= 0) return;

            var hazards = ResearchAdjustedHazards(
                BaselineDailyRate(cohort.Stage) * StageMultiplier(cohort.Stage) * anchorHazardMultiplier,
                DiseaseRateFor(population.Id), 0.0, mortalityReduction, diseaseResistanceBonus);
            int deaths = count - Survivors(count, days, hazards.Total);
            if (deaths <= 0) return;

            var split = MortalityLedger.Split(deaths, new[] { hazards.Baseline, hazards.Disease });
            population.RemoveDead(cohort, split[0], MortalityCause.BaselineAnchor);
            population.RemoveDead(cohort, split[1], MortalityCause.Disease);
        }

        /// <summary>
        /// Set disease mortality rate for a population.
        /// Called by IntegratedDiseaseSystem before FishPopulation.AdvanceDays().
        /// </summary>
        public void SetDiseaseMortalityRate(string populationId, double dailyRate)
        {
            if (string.IsNullOrEmpty(populationId)) return;
            double clampedRate = Math.Clamp(dailyRate, 0.0, MaxDiseaseDailyMortalityRate); // Cap at 50% per day
            System.Diagnostics.Debug.WriteLine($"[MortalityModel] SET DISEASE RATE: PopId={populationId}, Rate={clampedRate:F6}");
            _diseaseRates[populationId] = clampedRate;
        }

        /// <summary>
        /// Clear disease mortality rates (call at start of each tick).
        /// </summary>
        public void ClearDiseaseMortalityRates()
        {
            _diseaseRates.Clear();
        }

        /// <summary>
        /// Apply mortality with full environmental data integration.
        /// Incorporates temperature stress, oxygen stress, and zone-specific risk factors.
        /// </summary>
        public int ApplyMortalityWithEnvironment(
            int count, 
            int days, 
            LifeStage stage, 
            EnvironmentalDataPoint environmentalData,
            string populationId = "",
            double mortalityReduction = 0.0,
            double diseaseResistanceBonus = 0.0)
        {
            if (count <= 0 || days <= 0) return count;
            
            double baseRate = BaselineDailyRate(stage);
            double stageMultiplier = StageMultiplier(stage);
            double diseaseRate = DiseaseRateFor(populationId);

            // Calculate environmental stress mortality
            double environmentalMortality = CalculateEnvironmentalMortality(environmentalData, stage);
            
            double totalRate = ResearchAdjustedRate(baseRate * stageMultiplier, diseaseRate, environmentalMortality,
                mortalityReduction, diseaseResistanceBonus);

            return Survivors(count, days, totalRate);
        }
        
        /// <summary>
        /// Daily hazard after research: <paramref name="mortalityReduction"/> removes
        /// that fraction of the baseline hazard and <paramref name="diseaseResistanceBonus"/>
        /// that fraction of the disease hazard (each capped at
        /// <see cref="Core.ResearchEffectKeys.MaxReduction"/>); environmental stress is
        /// not reduced. Clamped to [0, <see cref="MaxTotalDailyMortalityRate"/>].
        /// </summary>
        internal static double ResearchAdjustedRate(double baselineHazard, double diseaseHazard,
            double environmentalHazard, double mortalityReduction, double diseaseResistanceBonus)
            => ResearchAdjustedHazards(baselineHazard, diseaseHazard, environmentalHazard,
                mortalityReduction, diseaseResistanceBonus).Total;

        /// <summary>
        /// The research-adjusted hazard components behind
        /// <see cref="ResearchAdjustedRate"/>: the baseline and disease hazards
        /// after research, the environmental hazard as given, and their clamped
        /// sum (the daily rate applied). The ledger splits deaths by the components.
        /// </summary>
        internal static (double Baseline, double Disease, double Environment, double Total) ResearchAdjustedHazards(
            double baselineHazard, double diseaseHazard, double environmentalHazard,
            double mortalityReduction, double diseaseResistanceBonus)
        {
            double baseline = baselineHazard * (1.0 - Core.ResearchEffectKeys.ClampReduction(mortalityReduction));
            double disease = Math.Max(0.0, diseaseHazard) * (1.0 - Core.ResearchEffectKeys.ClampReduction(diseaseResistanceBonus));
            double total = Math.Clamp(baseline + disease + environmentalHazard, 0.0, MaxTotalDailyMortalityRate);
            return (baseline, disease, environmentalHazard, total);
        }

        /// <summary>
        /// Calculate mortality rate contribution from environmental stress.
        /// Based on scientifically-validated thresholds for Atlantic salmon.
        /// </summary>
        private double CalculateEnvironmentalMortality(EnvironmentalDataPoint data, LifeStage stage)
        {
            double mortality = 0.0;
            
            // Temperature stress mortality
            // Critical thresholds vary by life stage
            double criticalHighTemp = stage == LifeStage.Seawater ? 18.0 : 16.0;
            double criticalLowTemp = 2.0;
            
            if (data.TemperatureAtCageDepth > criticalHighTemp)
            {
                double excess = data.TemperatureAtCageDepth - criticalHighTemp;
                mortality += 0.001 * excess; // 0.1% per degree above critical
            }
            else if (data.TemperatureAtCageDepth < criticalLowTemp)
            {
                double deficit = criticalLowTemp - data.TemperatureAtCageDepth;
                mortality += 0.002 * deficit; // 0.2% per degree below critical (cold is worse)
            }
            
            // Oxygen stress mortality (direct hypoxia)
            const double lethalOxygen = 3.0; // mg/L - lethal threshold
            const double criticalOxygen = 5.0; // mg/L - critical threshold
            
            if (data.OxygenAtCageDepth < lethalOxygen)
            {
                // Rapid mortality below lethal threshold
                double severity = (lethalOxygen - data.OxygenAtCageDepth) / lethalOxygen;
                mortality += 0.01 * severity; // 1% base + severity
            }
            else if (data.OxygenAtCageDepth < criticalOxygen)
            {
                // Sublethal stress increases mortality
                double deficit = criticalOxygen - data.OxygenAtCageDepth;
                mortality += 0.0005 * deficit; // 0.05% per mg/L below critical
            }
            
            // Environmental quality score impact
            // Poor overall environment increases stress-related mortality
            if (data.EnvironmentalQualityScore < 0.5)
            {
                mortality += 0.001 * (0.5 - data.EnvironmentalQualityScore); // Up to 0.05% extra
            }
            
            // Current stress (extreme currents)
            if (data.CurrentSpeedAtCageDepth > 0.6)
            {
                // Very strong currents cause physical stress and mortality
                mortality += 0.0003 * (data.CurrentSpeedAtCageDepth - 0.6); // 0.03% per 0.1 m/s excess
            }
            
            return Math.Min(mortality, 0.05); // Cap environmental mortality at 5% per day
        }
        
        /// <summary>
        /// Set environmental stress state for a population.
        /// Tracks accumulated stress over time.
        /// </summary>
        public void SetEnvironmentalStress(string populationId, EnvironmentalDataPoint data)
        {
            if (string.IsNullOrEmpty(populationId)) return;
            
            if (!_environmentalStress.TryGetValue(populationId, out var state))
            {
                state = new EnvironmentalStressState();
                _environmentalStress[populationId] = state;
            }
            
            // Update accumulated stress
            state.AccumulatedThermalStress += Math.Max(0, data.TemperatureAtCageDepth - 14.0);
            state.AccumulatedOxygenStress += Math.Max(0, 6.0 - data.OxygenAtCageDepth);
            state.DaysOfStress++;
            state.LastEnvironmentalQuality = data.EnvironmentalQualityScore;
        }
        
        /// <summary>
        /// Get accumulated stress mortality multiplier for a population.
        /// Chronic stress increases mortality risk.
        /// </summary>
        public double GetStressMortalityMultiplier(string populationId)
        {
            if (!_environmentalStress.TryGetValue(populationId, out var state))
                return 1.0;
            
            // Chronic stress increases mortality
            double thermalStressFactor = Math.Min(0.5, state.AccumulatedThermalStress / 100.0);
            double oxygenStressFactor = Math.Min(0.5, state.AccumulatedOxygenStress / 50.0);
            
            return 1.0 + thermalStressFactor + oxygenStressFactor;
        }
        
        /// <summary>
        /// Clear environmental stress tracking (e.g., when population is moved).
        /// </summary>
        public void ClearEnvironmentalStress(string populationId)
        {
            _environmentalStress.Remove(populationId);
        }

        /// <summary>
        /// Capture live model state for saves: per-population disease rates
        /// plus accumulated environmental stress. Without this, loaded games
        /// silently drop disease/stress pressure on the first post-load tick.
        /// </summary>
        public MortalityModelSnapshot GetSaveSnapshot()
        {
            var snapshot = new MortalityModelSnapshot { Ledger = Ledger.GetSaveSnapshot() };
            foreach (var kvp in _diseaseRates)
                snapshot.DiseaseRates[kvp.Key] = kvp.Value;
            foreach (var kvp in _environmentalStress)
                snapshot.StressStates[kvp.Key] = new EnvironmentalStressSnapshot
                {
                    AccumulatedThermalStress = kvp.Value.AccumulatedThermalStress,
                    AccumulatedOxygenStress = kvp.Value.AccumulatedOxygenStress,
                    DaysOfStress = kvp.Value.DaysOfStress,
                    LastEnvironmentalQuality = kvp.Value.LastEnvironmentalQuality,
                };
            return snapshot;
        }

        /// <summary>
        /// Restore live model state after a load so disease and chronic
        /// stress pressure resume exactly. Accepts null (clears state) for
        /// saves written before this snapshot existed. Null dictionaries
        /// inside a snapshot are treated as empty (hand-edited-save safety).
        /// </summary>
        public void RestoreFromSave(MortalityModelSnapshot? snapshot)
        {
            _diseaseRates.Clear();
            _environmentalStress.Clear();
            Ledger.RestoreFromSave(snapshot?.Ledger);
            if (snapshot == null) return;

            if (snapshot.DiseaseRates != null)
                foreach (var kvp in snapshot.DiseaseRates)
                    _diseaseRates[kvp.Key] = Math.Clamp(kvp.Value, 0.0, MaxDiseaseDailyMortalityRate);

            if (snapshot.StressStates != null)
                foreach (var kvp in snapshot.StressStates)
                {
                    if (kvp.Value == null) continue;
                    _environmentalStress[kvp.Key] = new EnvironmentalStressState
                    {
                        AccumulatedThermalStress = kvp.Value.AccumulatedThermalStress,
                        AccumulatedOxygenStress = kvp.Value.AccumulatedOxygenStress,
                        DaysOfStress = kvp.Value.DaysOfStress,
                        LastEnvironmentalQuality = kvp.Value.LastEnvironmentalQuality,
                    };
                }
        }
        
        /// <summary>
        /// Tracks environmental stress accumulation for a population.
        /// </summary>
        private class EnvironmentalStressState
        {
            public double AccumulatedThermalStress { get; set; }
            public double AccumulatedOxygenStress { get; set; }
            public int DaysOfStress { get; set; }
            public double LastEnvironmentalQuality { get; set; } = 1.0;
        }
    }

    /// <summary>
    /// Serializable accumulated-stress state for one population.
    /// Public DTO mirror of MortalityModel's private tracking state.
    /// </summary>
    public sealed class EnvironmentalStressSnapshot
    {
        public double AccumulatedThermalStress { get; set; }
        public double AccumulatedOxygenStress { get; set; }
        public int DaysOfStress { get; set; }
        public double LastEnvironmentalQuality { get; set; } = 1.0;
    }

    /// <summary>
    /// Serializable save snapshot for <see cref="MortalityModel"/>: live
    /// per-population disease rates plus accumulated environmental stress.
    /// </summary>
    public sealed class MortalityModelSnapshot
    {
        public Dictionary<string, double> DiseaseRates { get; set; } = new();
        public Dictionary<string, EnvironmentalStressSnapshot> StressStates { get; set; } = new();

        /// <summary>The mortality ledger (S2-05); null in saves written before it existed.</summary>
        public MortalityLedgerSnapshot? Ledger { get; set; }
    }
}