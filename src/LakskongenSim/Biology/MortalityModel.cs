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
        /// Apply mortality over days, return surviving count (rounded to int).
        /// Combines baseline, stage, and disease mortality in a single application.
        /// </summary>
        public int ApplyMortality(int count, int days, LifeStage stage, string populationId = "")
        {
            if (count <= 0 || days <= 0) return count;
            
            double baseRate = BaselineDailyRate(stage);
            
            // Stage modifiers based on real salmon mortality patterns
            double stageMultiplier = 1.0;
            switch (stage)
            {
                case LifeStage.Egg: stageMultiplier = 5.0; break;       // Highest mortality in egg stage
                case LifeStage.Alevin: stageMultiplier = 4.0; break;    // Vulnerable yolk sac stage
                case LifeStage.Fry: stageMultiplier = 3.0; break;       // First feeding critical period
                case LifeStage.Parr: stageMultiplier = 2.0; break;      // Freshwater predation/disease
                case LifeStage.Smolt: stageMultiplier = 2.5; break;     // Smoltification stress
                case LifeStage.Seawater: stageMultiplier = 1.2; break;  // Sea lice, disease exposure
                case LifeStage.Harvest: stageMultiplier = 1.0; break;   // Stable if well-managed
            }
            
            // Get disease mortality rate (if any) for this population
            double diseaseRate = 0.0;
            if (!string.IsNullOrEmpty(populationId) && _diseaseRates.TryGetValue(populationId, out double rate))
            {
                diseaseRate = rate;
            }
            
            // Combine all mortality sources: baseline * stage_multiplier + disease
            double totalRate = (baseRate * stageMultiplier) + diseaseRate;
            
            // Clamp to prevent negative or >100% mortality
            totalRate = Math.Clamp(totalRate, 0.0, MaxTotalDailyMortalityRate); // Max 95% mortality per day
            
            // survivors = count * (1-rate)^days
            var survivors = count * Math.Pow(1.0 - totalRate, days);
            
            // Log to sim log for debugging (first 10 calls)
            if (_callCount < 10)
            {
                SimLog.Debug($"[MortalityModel] Call #{_callCount}: BaseRate={baseRate:F7}, Stage={stage}, StageMult={stageMultiplier:F2}, TotalRate={totalRate:F7}, Count={count}, Survivors={survivors:F0}");
                _callCount++;
            }
            
            return Math.Max(0, (int)Math.Round(survivors));
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
            string populationId = "")
        {
            if (count <= 0 || days <= 0) return count;
            
            double baseRate = BaselineDailyRate(stage);
            
            // Stage modifiers based on real salmon mortality patterns
            double stageMultiplier = 1.0;
            switch (stage)
            {
                case LifeStage.Egg: stageMultiplier = 5.0; break;
                case LifeStage.Alevin: stageMultiplier = 4.0; break;
                case LifeStage.Fry: stageMultiplier = 3.0; break;
                case LifeStage.Parr: stageMultiplier = 2.0; break;
                case LifeStage.Smolt: stageMultiplier = 2.5; break;
                case LifeStage.Seawater: stageMultiplier = 1.2; break;
                case LifeStage.Harvest: stageMultiplier = 1.0; break;
            }
            
            // Get disease mortality rate
            double diseaseRate = 0.0;
            if (!string.IsNullOrEmpty(populationId) && _diseaseRates.TryGetValue(populationId, out double rate))
            {
                diseaseRate = rate;
            }
            
            // Calculate environmental stress mortality
            double environmentalMortality = CalculateEnvironmentalMortality(environmentalData, stage);
            
            // Combine all mortality sources
            double totalRate = (baseRate * stageMultiplier) + diseaseRate + environmentalMortality;
            
            // Clamp to prevent negative or >100% mortality
            totalRate = Math.Clamp(totalRate, 0.0, MaxTotalDailyMortalityRate);
            
            // survivors = count * (1-rate)^days
            var survivors = count * Math.Pow(1.0 - totalRate, days);
            
            return Math.Max(0, (int)Math.Round(survivors));
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
            var snapshot = new MortalityModelSnapshot();
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
    }
}