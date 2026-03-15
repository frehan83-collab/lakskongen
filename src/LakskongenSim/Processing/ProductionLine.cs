using System;
using System.Collections.Generic;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Represents a specific production line within a processing plant
    /// Can be configured for different products and upgraded over time
    /// </summary>
    public class ProductionLine
    {
        public string LineId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public ProductType ConfiguredProduct { get; set; }
        
        // Line characteristics
        public TechnologyTier TechnologyLevel { get; set; } = TechnologyTier.Manual;
        public SkillLevel AverageSkillLevel { get; set; } = SkillLevel.Competent;
        public int CapacityKgPerHour { get; set; }
        
        // Staffing
        public int SkilledWorkers { get; set; }
        public int UnskilledWorkers { get; set; }
        public decimal HourlyWageCostNOK { get; set; }
        
        // Maintenance and reliability
        public decimal MaintenanceCostPerMonth { get; set; }
        public decimal UptimePercent { get; set; } = 0.95m;  // 95% default uptime
        public int DaysSinceLastMaintenance { get; set; } = 0;
        
        // Economic tracking
        public decimal TotalCapex { get; set; }  // Total invested in this line
        public decimal CumulativeRevenue { get; set; }
        public decimal CumulativeProfit { get; set; }
        
        // Quality control
        public bool HasQualityControlSystem { get; set; } = false;
        public bool HasCertification { get; set; } = false;
        
        /// <summary>
        /// Process a batch through this line
        /// </summary>
        public ProductionResult ProcessBatch(
            int inputKg,
            decimal inputQuality,
            decimal rawMaterialCostPerKg,
            decimal temperatureControl = 1.0m,
            int freshnessHours = 0)
        {
            if (!ProductCatalog.Products.TryGetValue(ConfiguredProduct, out var productDef))
                throw new InvalidOperationException($"Invalid product configuration: {ConfiguredProduct}");
            
            // Check if line is operational (uptime)
            if (!IsOperational())
            {
                return new ProductionResult
                {
                    Success = false,
                    FailureReason = "Line down for maintenance",
                    InputKg = inputKg
                };
            }
            
            // Calculate yields
            int grossOutputKg = YieldCalculator.CalculateOutputKg(
                ConfiguredProduct, 
                inputKg, 
                inputQuality, 
                AverageSkillLevel, 
                TechnologyLevel, 
                temperatureControl, 
                freshnessHours);
            
            int netOutputKg = YieldCalculator.CalculateNetOutput(
                ConfiguredProduct, 
                grossOutputKg, 
                inputQuality, 
                AverageSkillLevel);
            
            int rejectedKg = grossOutputKg - netOutputKg;
            
            // Calculate costs
            decimal rawMaterialCost = inputKg * rawMaterialCostPerKg;
            decimal laborCost = CalculateLaborCost(inputKg);
            decimal variableOpex = productDef.VariableOpexPerKg * netOutputKg;
            decimal totalCost = rawMaterialCost + laborCost + variableOpex;
            
            // Calculate revenue
            decimal pricePerKg = productDef.BasePricePerKg;
            if (HasQualityControlSystem)
                pricePerKg *= 1.05m; // 5% premium for QC
            if (HasCertification && productDef.RequiresCertification)
                pricePerKg *= 1.08m; // 8% premium for certification
            
            decimal revenue = netOutputKg * pricePerKg;
            decimal profit = revenue - totalCost;
            
            // Update cumulative tracking
            CumulativeRevenue += revenue;
            CumulativeProfit += profit;
            DaysSinceLastMaintenance++;
            
            return new ProductionResult
            {
                Success = true,
                InputKg = inputKg,
                GrossOutputKg = grossOutputKg,
                NetOutputKg = netOutputKg,
                RejectedKg = rejectedKg,
                Revenue = revenue,
                RawMaterialCost = rawMaterialCost,
                LaborCost = laborCost,
                VariableOpex = variableOpex,
                TotalCost = totalCost,
                Profit = profit,
                PricePerKg = pricePerKg
            };
        }
        
        /// <summary>
        /// Calculate labor cost for this batch
        /// </summary>
        private decimal CalculateLaborCost(int inputKg)
        {
            if (!ProductCatalog.Products.TryGetValue(ConfiguredProduct, out var productDef))
                return 0m;
            
            // Base labor cost from catalog
            decimal baseCost = productDef.LaborCostPerKg * inputKg;
            
            // Apply technology labor reduction
            decimal reduction = YieldCalculator.GetLaborReduction(TechnologyLevel);
            
            return baseCost * (1.0m - reduction);
        }
        
        /// <summary>
        /// Check if line is currently operational
        /// </summary>
        public bool IsOperational()
        {
            // Degraded uptime if overdue for maintenance
            decimal uptimeMultiplier = 1.0m;
            if (DaysSinceLastMaintenance > 30)
                uptimeMultiplier = 0.8m;
            else if (DaysSinceLastMaintenance > 60)
                uptimeMultiplier = 0.6m;
            
            decimal effectiveUptime = UptimePercent * uptimeMultiplier;
            
            // Simulate random downtime (deterministic for testing, can add RNG later)
            return effectiveUptime >= 0.5m;
        }
        
        /// <summary>
        /// Perform maintenance on the line
        /// </summary>
        public MaintenanceResult PerformMaintenance()
        {
            decimal cost = MaintenanceCostPerMonth;
            
            // Overdue maintenance is more expensive
            if (DaysSinceLastMaintenance > 30)
                cost *= 1.5m;
            
            DaysSinceLastMaintenance = 0;
            UptimePercent = 0.98m; // Refreshed uptime
            
            return new MaintenanceResult
            {
                Cost = cost,
                UptimeRestored = UptimePercent,
                DaysExtended = 30
            };
        }
        
        /// <summary>
        /// Calculate ROI for this production line
        /// </summary>
        public decimal CalculateROI()
        {
            if (TotalCapex <= 0) return 0m;
            return CumulativeProfit / TotalCapex;
        }
        
        /// <summary>
        /// Calculate payback period in years (approximate)
        /// </summary>
        public decimal EstimatePaybackYears(decimal avgAnnualProfit)
        {
            if (avgAnnualProfit <= 0) return decimal.MaxValue;
            return TotalCapex / avgAnnualProfit;
        }
    }
    
    /// <summary>
    /// Result of a production batch operation
    /// </summary>
    public class ProductionResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        
        public int InputKg { get; set; }
        public int GrossOutputKg { get; set; }
        public int NetOutputKg { get; set; }
        public int RejectedKg { get; set; }
        
        public decimal Revenue { get; set; }
        public decimal RawMaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal VariableOpex { get; set; }
        public decimal TotalCost { get; set; }
        public decimal Profit { get; set; }
        public decimal PricePerKg { get; set; }
        
        public decimal Margin => Revenue > 0 ? (Profit / Revenue) : 0m;
        public decimal YieldPercent => InputKg > 0 ? ((decimal)NetOutputKg / InputKg) : 0m;
    }
    
    /// <summary>
    /// Result of maintenance operation
    /// </summary>
    public class MaintenanceResult
    {
        public decimal Cost { get; set; }
        public decimal UptimeRestored { get; set; }
        public int DaysExtended { get; set; }
    }
}
