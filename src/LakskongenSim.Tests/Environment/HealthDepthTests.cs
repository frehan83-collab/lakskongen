using System;
using Xunit;
using LakskongenSim.Health;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// Deterministic tests for the HEALTH-depth models:
    /// delousing harm/compounding, thermal lice coupling, and gill-health warning.
    /// </summary>
    public class HealthDepthTests
    {
        // ---------- DelousingHarmModel ----------

        [Fact]
        public void Delousing_RepeatedRecentTreatments_CompoundInjury()
        {
            var rested = new DelousingInjuryState { DaysSinceLastTreatment = 30 };
            var afterFirst = DelousingHarmModel.ApplyTreatment(rested, DelousingMethod.Thermal);

            var immediate = DelousingHarmModel.ApplyTreatment(afterFirst, DelousingMethod.Thermal);

            var delayedInput = afterFirst with { DaysSinceLastTreatment = 30 };
            var delayed = DelousingHarmModel.ApplyTreatment(delayedInput, DelousingMethod.Thermal);

            Assert.True(immediate.InjuryLoad > delayed.InjuryLoad);
            Assert.True(immediate.CumulativeWelfareCost > afterFirst.CumulativeWelfareCost);
            Assert.Equal(2, immediate.TreatmentsToDate);
        }

        [Fact]
        public void Delousing_HarmlessMethods_AreLessInjuriousThanThermal()
        {
            var med = DelousingHarmModel.Evaluate(DelousingMethod.Medicinal);
            var fresh = DelousingHarmModel.Evaluate(DelousingMethod.Freshwater);
            var thermal = DelousingHarmModel.Evaluate(DelousingMethod.Thermal);
            var hydro = DelousingHarmModel.Evaluate(DelousingMethod.Hydrolicer);

            Assert.True(med.InjuryRate < fresh.InjuryRate);
            Assert.True(fresh.InjuryRate < thermal.InjuryRate);
            Assert.True(thermal.InjuryRate <= hydro.InjuryRate);
            Assert.True(med.HandlingMortalityDelta < thermal.HandlingMortalityDelta);
            Assert.True(med.WelfareCost < thermal.WelfareCost);
        }

        [Fact]
        public void Delousing_ElevatedBaselineMortality_IsMonotonicAndCapped()
        {
            Assert.Equal(0.0, DelousingHarmModel.ElevatedBaselineMortality(0.0), precision: 9);
            double low = DelousingHarmModel.ElevatedBaselineMortality(1.0);
            double high = DelousingHarmModel.ElevatedBaselineMortality(2.0);
            Assert.True(high > low);
            Assert.True(DelousingHarmModel.ElevatedBaselineMortality(100.0) <= DelousingHarmModel.MaxInjuryMortality);
        }

        [Fact]
        public void Delousing_AdvanceDay_DecaysLoadAndAdvancesClock()
        {
            var state = new DelousingInjuryState { InjuryLoad = 1.0, DaysSinceLastTreatment = 0 };
            var next = DelousingHarmModel.AdvanceDay(state);
            Assert.True(next.InjuryLoad < state.InjuryLoad);
            Assert.Equal(1, next.DaysSinceLastTreatment);
        }

        // ---------- ThermalLiceCoupling ----------

        [Fact]
        public void Lice_WarmerWater_AcceleratesDevelopment()
        {
            double cold = ThermalLiceCoupling.DevelopmentMultiplier(5.0);
            double refTemp = ThermalLiceCoupling.DevelopmentMultiplier(10.0);
            double warm = ThermalLiceCoupling.DevelopmentMultiplier(15.0);

            Assert.Equal(1.0, refTemp, precision: 9);
            Assert.True(warm > refTemp);
            Assert.True(refTemp > cold);
        }

        [Fact]
        public void Lice_WarmerWater_ShortensDaysToMaturity()
        {
            double warmDays = ThermalLiceCoupling.DaysToAccumulate(200.0, 15.0);
            double coolDays = ThermalLiceCoupling.DaysToAccumulate(200.0, 10.0);
            Assert.True(warmDays < coolDays);
            Assert.True(double.IsPositiveInfinity(ThermalLiceCoupling.DaysToAccumulate(200.0, 3.0)));
        }

        [Fact]
        public void Lice_WarmingTrend_IncreasesMultiplierAndTemperature()
        {
            double day0 = ThermalLiceCoupling.DevelopmentMultiplierWithWarming(10.0, 0.05, 0);
            double day5y = ThermalLiceCoupling.DevelopmentMultiplierWithWarming(10.0, 0.05, 365 * 5);
            Assert.True(day5y > day0);
            Assert.True(ThermalLiceCoupling.EffectiveTemperature(10.0, 0.05, 365 * 5) > 10.0);
        }

        [Fact]
        public void Lice_SyncSeaLiceTemperature_UsesWarmingAdjustedValue()
        {
            var model = new SeaLiceModel();
            ThermalLiceCoupling.SyncSeaLiceTemperature(model, 10.0, 0.05, 365 * 10);
            Assert.True(model.TemperatureC > 10.0);
        }

        // ---------- GillHealthWarning ----------

        [Fact]
        public void Gill_OptimalConditions_NoWarning()
        {
            var result = GillHealthWarning.Evaluate(10.0, 9.0, 5.0, 0.20, 0.0);
            Assert.Equal(0.0, result.Risk01, precision: 9);
            Assert.False(result.IsWarning);
            Assert.False(result.IsCritical);
            Assert.Equal("No significant gill-health risk", result.Reason);
        }

        [Fact]
        public void Gill_ExtremeConditions_AreCritical()
        {
            var result = GillHealthWarning.Evaluate(22.0, 3.0, 40.0, 0.0, 1.0);
            Assert.Equal(1.0, result.Risk01, precision: 9);
            Assert.True(result.IsWarning);
            Assert.True(result.IsCritical);
        }

        [Fact]
        public void Gill_WarningThreshold_IsCrossedByModerateCompoundStressor()
        {
            double below = GillHealthWarning.Evaluate(18.0, 5.0, 20.0, 0.05, 0.5).Risk01;
            double above = GillHealthWarning.Evaluate(19.0, 4.5, 25.0, 0.04, 0.6).Risk01;

            Assert.True(below < GillHealthWarning.WarningThreshold);
            Assert.True(above >= GillHealthWarning.WarningThreshold);
        }

        [Fact]
        public void Gill_DominantDriver_IsReportedAsReason()
        {
            var result = GillHealthWarning.Evaluate(10.0, 3.0, 5.0, 0.20, 0.0);
            Assert.Equal("Low dissolved oxygen", result.Reason);
        }
    }
}