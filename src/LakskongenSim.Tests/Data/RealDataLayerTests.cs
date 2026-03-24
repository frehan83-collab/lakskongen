using System;
using Xunit;
using LakskongenSim.Data.RealData.Layers;

namespace LakskongenSim.Tests.DataLayers
{
    public class RealDataLayerTests
    {
        private readonly SalinityLayer _salinity = new();
        private readonly OxygenLayer _oxygen = new();

        // Salinity Tests

        [Fact]
        public void Salinity_CoastalZone_InNorwegianRange()
        {
            double sal = _salinity.GetSalinity("PO04", new DateTime(2024, 6, 15));
            Assert.InRange(sal, 20.0, 36.0);
        }

        [Fact]
        public void Salinity_SpringSnowmelt_LowerThanWinter()
        {
            double winter = _salinity.GetSalinity("PO04", new DateTime(2024, 1, 15));
            double spring = _salinity.GetSalinity("PO04", new DateTime(2024, 5, 30));
            Assert.True(spring <= winter, $"Spring ({spring:F1}) should be <= winter ({winter:F1}) due to snowmelt");
        }

        [Fact]
        public void Salinity_DeepWater_HigherThanSurface()
        {
            double surface = _salinity.GetSalinity("PO04", new DateTime(2024, 6, 15), 2.0);
            double deep = _salinity.GetSalinity("PO04", new DateTime(2024, 6, 15), 50.0);
            Assert.True(deep >= surface, $"Deep ({deep:F1}) should be >= surface ({surface:F1})");
        }

        [Fact]
        public void Salinity_AllZones_ValidRange()
        {
            for (int i = 1; i <= 13; i++)
            {
                string zone = $"PO{i:D2}";
                double sal = _salinity.GetSalinity(zone, new DateTime(2024, 8, 1));
                Assert.InRange(sal, 20.0, 36.0);
            }
        }

        [Fact]
        public void Salinity_OptimalForSalmon_ValidRange()
        {
            Assert.True(_salinity.IsOptimalForSalmon(30.0));
            Assert.False(_salinity.IsOptimalForSalmon(15.0));
            Assert.False(_salinity.IsOptimalForSalmon(37.0));
        }

        // Oxygen Tests

        [Fact]
        public void Oxygen_CoastalZone_InRange()
        {
            double o2 = _oxygen.GetOxygen("PO04", new DateTime(2024, 6, 15));
            Assert.InRange(o2, 2.0, 14.0);
        }

        [Fact]
        public void Oxygen_SummerLower_ThanWinter()
        {
            double winter = _oxygen.GetOxygen("PO04", new DateTime(2024, 1, 15));
            double summer = _oxygen.GetOxygen("PO04", new DateTime(2024, 7, 20));
            Assert.True(summer <= winter, $"Summer O2 ({summer:F1}) should be <= winter ({winter:F1})");
        }

        [Fact]
        public void Oxygen_DeepWater_LowerThanSurface()
        {
            double surface = _oxygen.GetOxygen("PO04", new DateTime(2024, 6, 15), 2.0);
            double deep = _oxygen.GetOxygen("PO04", new DateTime(2024, 6, 15), 50.0);
            Assert.True(deep <= surface, $"Deep O2 ({deep:F1}) should be <= surface ({surface:F1})");
        }

        [Fact]
        public void Oxygen_NorthernZones_HigherThanSouthern()
        {
            double south = _oxygen.GetOxygen("PO01", new DateTime(2024, 8, 1));
            double north = _oxygen.GetOxygen("PO13", new DateTime(2024, 8, 1));
            Assert.True(north >= south, $"Northern O2 ({north:F1}) should be >= southern ({south:F1})");
        }

        [Fact]
        public void Oxygen_StressLevels_Correct()
        {
            Assert.Equal("Optimal", _oxygen.GetStressLevel(9.0));
            Assert.Equal("Suboptimal", _oxygen.GetStressLevel(7.0));
            Assert.Equal("Stressed", _oxygen.GetStressLevel(5.0));
            Assert.Equal("Critical", _oxygen.GetStressLevel(3.0));
            Assert.Equal("Lethal", _oxygen.GetStressLevel(1.0));
        }
    }
}
