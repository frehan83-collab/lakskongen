using System;
using System.Collections.Generic;
using Xunit;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.Farm
{
    public class MultiFarmManagerTests
    {
        [Fact]
        public void AddFarm_ValidFarm_AddsSuccessfully()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Tromsø Site", "zone-north-1", 8);
            
            // Act
            var result = manager.AddFarm(farm);
            
            // Assert
            Assert.True(result);
            Assert.Equal(1, manager.FarmCount);
            Assert.Contains(farm, manager.FarmSites);
        }
        
        [Fact]
        public void AddFarm_WithParameters_CreatesAndAddsFarm()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var farm = manager.AddFarm("farm1", "Tromsø Site", "zone-north-1", 8);
            
            // Assert
            Assert.NotNull(farm);
            Assert.Equal("farm1", farm.Id);
            Assert.Equal("Tromsø Site", farm.Name);
            Assert.Equal("zone-north-1", farm.ZoneId);
            Assert.Equal(8, farm.PenCount);
        }
        
        [Fact]
        public void AddFarm_DuplicateId_ReturnsFalse()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6);
            var farm2 = new FarmSite("farm1", "Site B", "zone-2", 8); // Same ID
            
            // Act
            manager.AddFarm(farm1);
            var result = manager.AddFarm(farm2);
            
            // Assert
            Assert.False(result);
            Assert.Equal(1, manager.FarmCount);
        }
        
        [Fact]
        public void AddFarm_ExceedsMaxCapacity_ReturnsFalse()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act - Add 10 farms (max capacity)
            for (int i = 0; i < 10; i++)
            {
                var farm = new FarmSite($"farm{i}", $"Site {i}", $"zone-{i}", 6);
                Assert.True(manager.AddFarm(farm));
            }
            
            // Try to add 11th farm
            var extraFarm = new FarmSite("extra", "Extra Site", "zone-extra", 6);
            var result = manager.AddFarm(extraFarm);
            
            // Assert
            Assert.False(result);
            Assert.Equal(10, manager.FarmCount);
            Assert.False(manager.CanAddMore);
        }
        
        [Fact]
        public void RemoveFarm_ExistingFarm_RemovesSuccessfully()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Tromsø Site", "zone-north-1", 8);
            manager.AddFarm(farm);
            
            // Act
            var result = manager.RemoveFarm("farm1");
            
            // Assert
            Assert.True(result);
            Assert.Equal(0, manager.FarmCount);
        }
        
        [Fact]
        public void RemoveFarm_NonExistingFarm_ReturnsFalse()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Tromsø Site", "zone-north-1", 8);
            manager.AddFarm(farm);
            
            // Act
            var result = manager.RemoveFarm("non-existing");
            
            // Assert
            Assert.False(result);
            Assert.Equal(1, manager.FarmCount);
        }
        
        [Fact]
        public void GetTotalBiomass_MultipleFarms_SumsCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { TotalBiomassKg = 50000 };
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { TotalBiomassKg = 75000 };
            var farm3 = new FarmSite("farm3", "Site C", "zone-1", 10) { TotalBiomassKg = 100000 };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var totalBiomass = manager.GetTotalBiomass();
            
            // Assert
            Assert.Equal(225000.0, totalBiomass);
        }
        
        [Fact]
        public void GetTotalBiomass_EmptyManager_ReturnsZero()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var totalBiomass = manager.GetTotalBiomass();
            
            // Assert
            Assert.Equal(0.0, totalBiomass);
        }
        
        [Fact]
        public void GetTotalRevenue_MultipleFarms_SumsCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m };
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 7500000m };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var totalRevenue = manager.GetTotalRevenue();
            
            // Assert
            Assert.Equal(12500000m, totalRevenue);
        }
        
        [Fact]
        public void GetTotalCosts_MultipleFarms_SumsCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Costs = 3000000m };
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Costs = 4500000m };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var totalCosts = manager.GetTotalCosts();
            
            // Assert
            Assert.Equal(7500000m, totalCosts);
        }
        
        [Fact]
        public void GetTotalProfit_CalculatesCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m, Costs = 3000000m };
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 7500000m, Costs = 4500000m };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var totalProfit = manager.GetTotalProfit();
            var expectedProfit = (5000000m - 3000000m) + (7500000m - 4500000m);
            
            // Assert
            Assert.Equal(expectedProfit, totalProfit);
        }
        
        [Fact]
        public void GetProfitByZone_GroupsByZoneCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-north", 6) { Revenue = 5000000m, Costs = 3000000m };
            var farm2 = new FarmSite("farm2", "Site B", "zone-south", 8) { Revenue = 7500000m, Costs = 4500000m };
            var farm3 = new FarmSite("farm3", "Site C", "zone-north", 10) { Revenue = 6000000m, Costs = 3500000m };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var profitByZone = manager.GetProfitByZone();
            
            // Assert
            Assert.Equal(2, profitByZone.Count);
            Assert.Equal(4500000m, profitByZone["zone-north"]); // (5M-3M) + (6M-3.5M) = 4.5M
            Assert.Equal(3000000m, profitByZone["zone-south"]); // (7.5M-4.5M) = 3M
        }
        
        [Fact]
        public void GetProfitByZone_EmptyManager_ReturnsEmptyDictionary()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var profitByZone = manager.GetProfitByZone();
            
            // Assert
            Assert.Empty(profitByZone);
        }
        
        [Fact]
        public void GetBestPerformingFarm_ReturnsHighestProfit()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m, Costs = 3000000m }; // 2M profit
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 10000000m, Costs = 4000000m }; // 6M profit - best
            var farm3 = new FarmSite("farm3", "Site C", "zone-1", 10) { Revenue = 6000000m, Costs = 3500000m }; // 2.5M profit
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var bestFarm = manager.GetBestPerformingFarm();
            
            // Assert
            Assert.NotNull(bestFarm);
            Assert.Equal("farm2", bestFarm.Id);
            Assert.Equal(6000000m, bestFarm.Profit);
        }
        
        [Fact]
        public void GetBestPerformingFarm_EmptyManager_ReturnsNull()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var bestFarm = manager.GetBestPerformingFarm();
            
            // Assert
            Assert.Null(bestFarm);
        }
        
        [Fact]
        public void GetWorstPerformingFarm_ReturnsLowestProfit()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m, Costs = 3000000m }; // 2M profit
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 10000000m, Costs = 4000000m }; // 6M profit
            var farm3 = new FarmSite("farm3", "Site C", "zone-1", 10) { Revenue = 4000000m, Costs = 3500000m }; // 0.5M profit - worst
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var worstFarm = manager.GetWorstPerformingFarm();
            
            // Assert
            Assert.NotNull(worstFarm);
            Assert.Equal("farm3", worstFarm.Id);
            Assert.Equal(500000m, worstFarm.Profit);
        }
        
        [Fact]
        public void GetWorstPerformingFarm_EmptyManager_ReturnsNull()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var worstFarm = manager.GetWorstPerformingFarm();
            
            // Assert
            Assert.Null(worstFarm);
        }
        
        [Fact]
        public void GetWorstPerformingFarm_NegativeProfits_WorksCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m, Costs = 3000000m }; // 2M profit
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 3000000m, Costs = 5000000m }; // -2M profit - worst
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var worstFarm = manager.GetWorstPerformingFarm();
            
            // Assert
            Assert.NotNull(worstFarm);
            Assert.Equal("farm2", worstFarm.Id);
            Assert.Equal(-2000000m, worstFarm.Profit);
        }
        
        [Fact]
        public void GetFarmsByProfitRanking_ReturnsCorrectOrder()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) { Revenue = 5000000m, Costs = 3000000m }; // 2M profit
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) { Revenue = 10000000m, Costs = 4000000m }; // 6M profit - best
            var farm3 = new FarmSite("farm3", "Site C", "zone-1", 10) { Revenue = 4000000m, Costs = 3500000m }; // 0.5M profit - worst
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var ranked = manager.GetFarmsByProfitRanking();
            
            // Assert
            Assert.Equal(3, ranked.Count);
            Assert.Equal("farm2", ranked[0].Id); // 6M
            Assert.Equal("farm1", ranked[1].Id); // 2M
            Assert.Equal("farm3", ranked[2].Id); // 0.5M
        }
        
        [Fact]
        public void UpdateFarmBiomass_ExistingFarm_UpdatesSuccessfully()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Site A", "zone-1", 6) { TotalBiomassKg = 10000 };
            manager.AddFarm(farm);
            
            // Act
            var result = manager.UpdateFarmBiomass("farm1", 25000);
            
            // Assert
            Assert.True(result);
            Assert.Equal(25000.0, farm.TotalBiomassKg);
        }
        
        [Fact]
        public void UpdateFarmBiomass_NonExistingFarm_ReturnsFalse()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var result = manager.UpdateFarmBiomass("non-existing", 25000);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public void UpdateFarmFinancials_ExistingFarm_UpdatesSuccessfully()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Site A", "zone-1", 6);
            manager.AddFarm(farm);
            
            // Act
            var result = manager.UpdateFarmFinancials("farm1", 5000000m, 3000000m);
            
            // Assert
            Assert.True(result);
            Assert.Equal(5000000m, farm.Revenue);
            Assert.Equal(3000000m, farm.Costs);
            Assert.Equal(2000000m, farm.Profit);
        }
        
        [Fact]
        public void GetFarmsInZone_ReturnsCorrectFarms()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-north", 6);
            var farm2 = new FarmSite("farm2", "Site B", "zone-south", 8);
            var farm3 = new FarmSite("farm3", "Site C", "zone-north", 10);
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            manager.AddFarm(farm3);
            
            // Act
            var northFarms = manager.GetFarmsInZone("zone-north");
            
            // Assert
            Assert.Equal(2, northFarms.Count);
            Assert.Contains(farm1, northFarms);
            Assert.Contains(farm3, northFarms);
        }
        
        [Fact]
        public void GetPerformanceByZone_ReturnsDetailedMetrics()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-north", 6) 
            { 
                TotalBiomassKg = 50000, 
                Revenue = 5000000m, 
                Costs = 3000000m 
            };
            var farm2 = new FarmSite("farm2", "Site B", "zone-south", 8) 
            { 
                TotalBiomassKg = 75000, 
                Revenue = 7500000m, 
                Costs = 4500000m 
            };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var performance = manager.GetPerformanceByZone();
            
            // Assert
            Assert.Equal(2, performance.Count);
            Assert.Equal(1, performance["zone-north"].FarmCount);
            Assert.Equal(50000.0, performance["zone-north"].TotalBiomassKg);
            Assert.Equal(2000000m, performance["zone-north"].TotalProfit);
            Assert.Equal(1, performance["zone-south"].FarmCount);
            Assert.Equal(75000.0, performance["zone-south"].TotalBiomassKg);
            Assert.Equal(3000000m, performance["zone-south"].TotalProfit);
        }
        
        [Fact]
        public void GetPerformanceSummary_ReturnsCompleteSummary()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 6) 
            { 
                TotalBiomassKg = 50000, 
                Revenue = 5000000m, 
                Costs = 3000000m 
            };
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 8) 
            { 
                TotalBiomassKg = 75000, 
                Revenue = 4000000m, 
                Costs = 3500000m 
            };
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var summary = manager.GetPerformanceSummary();
            
            // Assert
            Assert.Equal(2, summary.TotalFarms);
            Assert.Equal(14, summary.TotalPens); // 6 + 8
            Assert.Equal(125000.0, summary.TotalBiomassKg);
            Assert.Equal(9000000m, summary.TotalRevenue);
            Assert.Equal(6500000m, summary.TotalCosts);
            Assert.Equal(2500000m, summary.TotalProfit);
            Assert.Equal(1250000m, summary.AverageProfitPerFarm); // 2.5M / 2
            Assert.Equal("farm1", summary.BestPerformingFarmId); // 2M profit vs 0.5M
            Assert.Equal("farm2", summary.WorstPerformingFarmId);
        }
        
        [Fact]
        public void ClearAllFarms_RemovesAllFarms()
        {
            // Arrange
            var manager = new MultiFarmManager();
            manager.AddFarm(new FarmSite("farm1", "Site A", "zone-1", 6));
            manager.AddFarm(new FarmSite("farm2", "Site B", "zone-2", 8));
            
            // Act
            manager.ClearAllFarms();
            
            // Assert
            Assert.Equal(0, manager.FarmCount);
            Assert.Empty(manager.FarmSites);
        }
        
        [Fact]
        public void GetFarm_ExistingFarm_ReturnsFarm()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm = new FarmSite("farm1", "Site A", "zone-1", 6);
            manager.AddFarm(farm);
            
            // Act
            var result = manager.GetFarm("farm1");
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal("farm1", result.Id);
        }
        
        [Fact]
        public void GetFarm_NonExistingFarm_ReturnsNull()
        {
            // Arrange
            var manager = new MultiFarmManager();
            
            // Act
            var result = manager.GetFarm("non-existing");
            
            // Assert
            Assert.Null(result);
        }
        
        [Fact]
        public void GetAverageProfitPerPen_CalculatesCorrectly()
        {
            // Arrange
            var manager = new MultiFarmManager();
            var farm1 = new FarmSite("farm1", "Site A", "zone-1", 4) { Revenue = 4000000m, Costs = 2000000m }; // 2M profit, 4 pens
            var farm2 = new FarmSite("farm2", "Site B", "zone-2", 6) { Revenue = 6000000m, Costs = 3000000m }; // 3M profit, 6 pens
            // Total: 5M profit, 10 pens = 500K per pen
            
            manager.AddFarm(farm1);
            manager.AddFarm(farm2);
            
            // Act
            var avgProfitPerPen = manager.GetAverageProfitPerPen();
            
            // Assert
            Assert.Equal(500000m, avgProfitPerPen);
        }
        
        [Fact]
        public void FarmSite_Profit_CalculatesCorrectly()
        {
            // Arrange
            var farm = new FarmSite("farm1", "Site A", "zone-1", 6)
            {
                Revenue = 5000000m,
                Costs = 3000000m
            };
            
            // Act & Assert
            Assert.Equal(2000000m, farm.Profit);
        }
        
        [Fact]
        public void FarmSite_DefaultValues_AreSetCorrectly()
        {
            // Arrange & Act
            var farm = new FarmSite("farm1", "Site A", "zone-1", 6);
            
            // Assert
            Assert.Equal(0.0, farm.TotalBiomassKg);
            Assert.Equal(0m, farm.Revenue);
            Assert.Equal(0m, farm.Costs);
            Assert.Equal(10.0, farm.WaterTemperature);
            Assert.Equal(0.15, farm.CurrentSpeed);
            Assert.Equal(8.0, farm.OxygenLevel);
            Assert.Equal(1.2, farm.FeedConversionRatio);
            Assert.Equal(1.0, farm.DiseasePressure);
            Assert.Equal(5000m, farm.LicenseCostPerTon);
            Assert.Equal(LakskongenSim.Calibration.CalibrationRegistry.Default.Feed.FeedPricePerKgFeedNOK, farm.FeedCostPerKg);
            Assert.Equal(150000m, farm.LaborCostPerMonth);
            Assert.Equal(0m, farm.MarketPriceBonus);
        }
    }
}
