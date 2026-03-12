using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Dynamic water chemistry engine modeling nitrogen-phosphorus-carbonate cycles.
    /// Uses ordinary differential equations (ODEs) with temperature dependence (Q10=2.0).
    /// Affects fish growth (oxygen stress, ammonia toxicity) and disease susceptibility.
    /// </summary>
    public class WaterChemistry
    {
        // State variables (concentrations in mg/L or mmol/L)
        public double AmmoniaNH4 { get; private set; }  // Ammonium (toxic)
        public double NitriteNO2 { get; private set; }  // Intermediate nitrification
        public double NitrateNO3 { get; private set; }  // Final nitrification product
        public double PhosphorusPO4 { get; private set; } // Phosphorus (from fish waste)
        public double OxygenO2 { get; private set; }    // Dissolved oxygen
        public double pH { get; private set; }          // pH (from carbonate equilibrium)
        
        // Parameters (Norwegian fjord typical values)
        private const double OxygenReaerationRate = 0.05;   // mg/L/day reoxygenation
        private const double NitrificationRateNH4 = 0.02;   // NH4→NO2 rate at 10°C
        private const double NitrificationRateNO2 = 0.015;  // NO2→NO3 rate at 10°C
        private const double PhosphorusAdsorptionRate = 0.03; // PO4 adsorption to sediments
        private const double Q10TemperatureFactor = 2.0;    // Biological rate doubles per 10°C
        
        // Toxic thresholds (Norwegian aquaculture standards)
        private const double AmmoniaToxicThreshold = 0.5;   // mg/L NH4 toxic to salmon
        private const double OxygenStressThreshold = 5.0;   // mg/L O2 minimum for growth
        private const double pHMin = 6.5;                   // Minimum acceptable pH
        private const double pHMax = 8.5;                   // Maximum acceptable pH
        
        private double _temperature; // Current water temperature (°C)
        
        /// <summary>
        /// Initialize water chemistry with Norwegian fjord baseline values.
        /// </summary>
        public WaterChemistry(double temperature = 10.0)
        {
            _temperature = temperature;
            
            // Baseline Norwegian fjord concentrations
            AmmoniaNH4 = 0.05;    // mg/L (low background)
            NitriteNO2 = 0.01;    // mg/L
            NitrateNO3 = 0.5;     // mg/L
            PhosphorusPO4 = 0.1;  // mg/L
            OxygenO2 = 8.0;       // mg/L (saturated at 10°C)
            pH = 7.8;             // Typical fjord pH
        }
        
        /// <summary>
        /// Update chemistry for one day with fish waste inputs.
        /// Fish waste: NH4 and PO4 from feed conversion (FCR 1.1-1.3).
        /// </summary>
        /// <param name="fishBiomassKg">Total fish biomass in zone (kg)</param>
        /// <param name="feedConversionRatio">FCR (kg feed/kg fish growth)</param>
        /// <param name="waterFlowRate">Water exchange rate (m³/day)</param>
        public void UpdateOneDay(double fishBiomassKg, double feedConversionRatio, double waterFlowRate)
        {
            // Temperature-dependent rate modifiers (Q10=2.0)
            double tempFactor = Math.Pow(Q10TemperatureFactor, (_temperature - 10.0) / 10.0);
            
            // Fish waste inputs (based on Norwegian aquaculture data)
            // NH4 excretion: 0.05 g NH4/kg fish/day
            // PO4 excretion: 0.02 g PO4/kg fish/day
            double ammoniaInput = fishBiomassKg * 0.05 * tempFactor;
            double phosphorusInput = fishBiomassKg * 0.02 * tempFactor;
            
            // Oxygen consumption: 0.1 mg O2/kg fish/day + nitrification oxygen demand
            double oxygenConsumptionFish = fishBiomassKg * 0.1 * tempFactor;
            double oxygenConsumptionNitrification = (NitrificationRateNH4 * AmmoniaNH4 + NitrificationRateNO2 * NitriteNO2) * 4.3; // O2 per NH4
            
            // ODE updates (Euler integration, dt=1 day)
            
            // Ammonium: input + nitrification loss + flow dilution
            AmmoniaNH4 += ammoniaInput - (NitrificationRateNH4 * tempFactor * AmmoniaNH4) - (AmmoniaNH4 * waterFlowRate * 0.01);
            
            // Nitrite: nitrification gain/loss + flow dilution
            NitriteNO2 += (NitrificationRateNH4 * tempFactor * AmmoniaNH4) - (NitrificationRateNO2 * tempFactor * NitriteNO2) - (NitriteNO2 * waterFlowRate * 0.01);
            
            // Nitrate: nitrification gain + flow dilution
            NitrateNO3 += (NitrificationRateNO2 * tempFactor * NitriteNO2) - (NitrateNO3 * waterFlowRate * 0.01);
            
            // Phosphorus: input + adsorption + flow dilution
            PhosphorusPO4 += phosphorusInput - (PhosphorusAdsorptionRate * tempFactor * PhosphorusPO4) - (PhosphorusPO4 * waterFlowRate * 0.01);
            
            // Oxygen: reaeration - consumption - flow effect
            OxygenO2 += OxygenReaerationRate - oxygenConsumptionFish - oxygenConsumptionNitrification - (OxygenO2 * waterFlowRate * 0.01);
            
            // pH calculation simplified from carbonate equilibrium
            // pH decreases with ammonia accumulation, increases with photosynthesis
            double pHChange = -0.01 * AmmoniaNH4 + 0.005 * OxygenO2; // Simplified relationship
            pH += pHChange;
            
            // Clamp values to realistic ranges
            AmmoniaNH4 = Math.Max(0, Math.Min(AmmoniaNH4, 10.0));
            NitriteNO2 = Math.Max(0, Math.Min(NitriteNO2, 2.0));
            NitrateNO3 = Math.Max(0, Math.Min(NitrateNO3, 20.0));
            PhosphorusPO4 = Math.Max(0, Math.Min(PhosphorusPO4, 5.0));
            OxygenO2 = Math.Max(0, Math.Min(OxygenO2, 14.0));
            pH = Math.Max(pHMin, Math.Min(pH, pHMax));
        }
        
        /// <summary>
        /// Calculate growth penalty due to water chemistry stress.
        /// Returns multiplier (0.0-1.0) applied to fish growth rate.
        /// </summary>
        public double CalculateGrowthPenalty()
        {
            double penalty = 1.0;
            
            // Ammonia toxicity (linear reduction above threshold)
            if (AmmoniaNH4 > AmmoniaToxicThreshold)
                penalty *= Math.Max(0.5, 1.0 - (AmmoniaNH4 - AmmoniaToxicThreshold) * 0.2);
            
            // Oxygen stress (growth reduction below threshold)
            if (OxygenO2 < OxygenStressThreshold)
                penalty *= Math.Max(0.3, OxygenO2 / OxygenStressThreshold);
            
            // pH stress (outside optimal 6.5-8.5 range)
            if (pH < 6.5 || pH > 8.5)
                penalty *= 0.8;
            
            return penalty;
        }
        
        /// <summary>
        /// Calculate disease susceptibility multiplier from water quality.
        /// Poor water quality increases susceptibility to bacterial diseases.
        /// </summary>
        public double CalculateDiseaseSusceptibilityMultiplier()
        {
            double multiplier = 1.0;
            
            // High ammonia increases bacterial disease susceptibility
            multiplier += AmmoniaNH4 * 0.2;
            
            // Low oxygen increases stress-related disease susceptibility
            if (OxygenO2 < OxygenStressThreshold)
                multiplier += (OxygenStressThreshold - OxygenO2) * 0.1;
            
            // Extreme pH increases susceptibility
            if (pH < 6.5 || pH > 8.5)
                multiplier *= 1.2;
            
            return multiplier;
        }
        
        /// <summary>
        /// Get current water chemistry status for player display/decision-making.
        /// </summary>
        public WaterChemistryStatus GetStatus()
        {
            return new WaterChemistryStatus
            {
                AmmoniaNH4 = AmmoniaNH4,
                NitriteNO2 = NitriteNO2,
                NitrateNO3 = NitrateNO3,
                PhosphorusPO4 = PhosphorusPO4,
                OxygenO2 = OxygenO2,
                pH = pH,
                Temperature = _temperature,
                GrowthPenalty = CalculateGrowthPenalty(),
                DiseaseSusceptibility = CalculateDiseaseSusceptibilityMultiplier(),
                IsCritical = AmmoniaNH4 > AmmoniaToxicThreshold || OxygenO2 < OxygenStressThreshold || pH < 6.5 || pH > 8.5
            };
        }
        
        /// <summary>
        /// Set temperature (affects all biological rates via Q10).
        /// </summary>
        public void SetTemperature(double temperature)
        {
            _temperature = temperature;
        }
    }
    
    /// <summary>
    /// Water chemistry status snapshot for UI display and player decisions.
    /// </summary>
    public struct WaterChemistryStatus
    {
        public double AmmoniaNH4;
        public double NitriteNO2;
        public double NitrateNO3;
        public double PhosphorusPO4;
        public double OxygenO2;
        public double pH;
        public double Temperature;
        public double GrowthPenalty;
        public double DiseaseSusceptibility;
        public bool IsCritical;
    }
}