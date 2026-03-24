using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Represents a farm site with its zone-specific parameters and economic data.
    /// </summary>
    public class FarmSite
    {
        public string Id { get; init; }
        public string Name { get; set; }
        public string ZoneId { get; set; }
        public int PenCount { get; set; }
        public double TotalBiomassKg { get; set; }
        public decimal Revenue { get; set; }
        public decimal Costs { get; set; }
        
        // Zone-specific environmental parameters
        public double WaterTemperature { get; set; }
        public double CurrentSpeed { get; set; }
        public double OxygenLevel { get; set; }
        public double FeedConversionRatio { get; set; }
        public double DiseasePressure { get; set; }
        
        // Zone-specific economic parameters
        public decimal LicenseCostPerTon { get; set; }
        public decimal FeedCostPerKg { get; set; }
        public decimal LaborCostPerMonth { get; set; }
        public decimal MarketPriceBonus { get; set; } // Regional price premium/discount
        
        /// <summary>Profit for this farm site.</summary>
        public decimal Profit => Revenue - Costs;
        
        /// <summary>Parameterless constructor for JSON deserialization.</summary>
        public FarmSite() { Id = string.Empty; Name = string.Empty; ZoneId = string.Empty; }
        
        public FarmSite(string id, string name, string zoneId, int penCount)
        {
            Id = id;
            Name = name;
            ZoneId = zoneId;
            PenCount = penCount;
            TotalBiomassKg = 0;
            Revenue = 0;
            Costs = 0;
            
            // Default environmental parameters
            WaterTemperature = 10.0;
            CurrentSpeed = 0.15;
            OxygenLevel = 8.0;
            FeedConversionRatio = 1.2;
            DiseasePressure = 1.0;
            
            // Default economic parameters
            LicenseCostPerTon = 5000m;
            FeedCostPerKg = 15m;
            LaborCostPerMonth = 150000m;
            MarketPriceBonus = 0m;
        }
    }
    
    /// <summary>
    /// Manages multiple farm sites across different Norwegian zones.
    /// Supports up to 10 farm sites.
    /// </summary>
    public class MultiFarmManager
    {
        public const int MaxFarmSites = 10;
        
        private readonly List<FarmSite> _farmSites;
        
        public IReadOnlyList<FarmSite> FarmSites => _farmSites.AsReadOnly();
        public int FarmCount => _farmSites.Count;
        public bool CanAddMore => _farmSites.Count < MaxFarmSites;
        
        public MultiFarmManager()
        {
            _farmSites = new List<FarmSite>();
        }
        
        /// <summary>
        /// Adds a new farm site to the manager.
        /// </summary>
        /// <param name="farm">The farm site to add.</param>
        /// <returns>True if the farm was added successfully, false if at capacity or duplicate ID.</returns>
        public bool AddFarm(FarmSite farm)
        {
            if (_farmSites.Count >= MaxFarmSites)
            {
                return false;
            }
            
            if (_farmSites.Any(f => f.Id == farm.Id))
            {
                return false;
            }
            
            _farmSites.Add(farm);
            return true;
        }
        
        /// <summary>
        /// Adds a new farm site with the specified parameters.
        /// </summary>
        public FarmSite? AddFarm(string id, string name, string zoneId, int penCount)
        {
            if (_farmSites.Count >= MaxFarmSites)
            {
                return null;
            }
            
            if (_farmSites.Any(f => f.Id == id))
            {
                return null;
            }
            
            var farm = new FarmSite(id, name, zoneId, penCount);
            _farmSites.Add(farm);
            return farm;
        }
        
        /// <summary>
        /// Removes a farm site by its ID.
        /// </summary>
        /// <param name="farmId">The ID of the farm to remove.</param>
        /// <returns>True if the farm was removed, false if not found.</returns>
        public bool RemoveFarm(string farmId)
        {
            var farm = _farmSites.FirstOrDefault(f => f.Id == farmId);
            if (farm == null)
            {
                return false;
            }
            
            _farmSites.Remove(farm);
            return true;
        }
        
        /// <summary>
        /// Gets a farm site by its ID.
        /// </summary>
        public FarmSite? GetFarm(string farmId)
        {
            return _farmSites.FirstOrDefault(f => f.Id == farmId);
        }
        
        /// <summary>
        /// Gets the total biomass across all farm sites (in kg).
        /// </summary>
        public double GetTotalBiomass()
        {
            return _farmSites.Sum(f => f.TotalBiomassKg);
        }
        
        /// <summary>
        /// Gets the total revenue across all farm sites.
        /// </summary>
        public decimal GetTotalRevenue()
        {
            return _farmSites.Sum(f => f.Revenue);
        }
        
        /// <summary>
        /// Gets the total costs across all farm sites.
        /// </summary>
        public decimal GetTotalCosts()
        {
            return _farmSites.Sum(f => f.Costs);
        }
        
        /// <summary>
        /// Gets the total profit across all farm sites.
        /// </summary>
        public decimal GetTotalProfit()
        {
            return _farmSites.Sum(f => f.Profit);
        }
        
        /// <summary>
        /// Gets profit aggregated by zone.
        /// </summary>
        /// <returns>Dictionary mapping zone IDs to their total profit.</returns>
        public Dictionary<string, decimal> GetProfitByZone()
        {
            return _farmSites
                .GroupBy(f => f.ZoneId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(f => f.Profit)
                );
        }
        
        /// <summary>
        /// Gets performance metrics (revenue, costs, profit) aggregated by zone.
        /// </summary>
        public Dictionary<string, ZonePerformance> GetPerformanceByZone()
        {
            return _farmSites
                .GroupBy(f => f.ZoneId)
                .ToDictionary(
                    g => g.Key,
                    g => new ZonePerformance
                    {
                        ZoneId = g.Key,
                        FarmCount = g.Count(),
                        TotalBiomassKg = g.Sum(f => f.TotalBiomassKg),
                        TotalRevenue = g.Sum(f => f.Revenue),
                        TotalCosts = g.Sum(f => f.Costs),
                        TotalProfit = g.Sum(f => f.Profit)
                    }
                );
        }
        
        /// <summary>
        /// Gets the best performing farm by profit.
        /// </summary>
        /// <returns>The farm with highest profit, or null if no farms.</returns>
        public FarmSite? GetBestPerformingFarm()
        {
            if (_farmSites.Count == 0)
            {
                return null;
            }
            
            return _farmSites.OrderByDescending(f => f.Profit).First();
        }
        
        /// <summary>
        /// Gets the worst performing farm by profit.
        /// </summary>
        /// <returns>The farm with lowest profit, or null if no farms.</returns>
        public FarmSite? GetWorstPerformingFarm()
        {
            if (_farmSites.Count == 0)
            {
                return null;
            }
            
            return _farmSites.OrderBy(f => f.Profit).First();
        }
        
        /// <summary>
        /// Gets farms sorted by profit (descending).
        /// </summary>
        public List<FarmSite> GetFarmsByProfitRanking()
        {
            return _farmSites.OrderByDescending(f => f.Profit).ToList();
        }
        
        /// <summary>
        /// Updates the biomass for a specific farm.
        /// </summary>
        public bool UpdateFarmBiomass(string farmId, double newBiomassKg)
        {
            var farm = _farmSites.FirstOrDefault(f => f.Id == farmId);
            if (farm == null)
            {
                return false;
            }
            
            farm.TotalBiomassKg = newBiomassKg;
            return true;
        }
        
        /// <summary>
        /// Updates the revenue and costs for a specific farm.
        /// </summary>
        public bool UpdateFarmFinancials(string farmId, decimal revenue, decimal costs)
        {
            var farm = _farmSites.FirstOrDefault(f => f.Id == farmId);
            if (farm == null)
            {
                return false;
            }
            
            farm.Revenue = revenue;
            farm.Costs = costs;
            return true;
        }
        
        /// <summary>
        /// Clears all farm sites.
        /// </summary>
        public void ClearAllFarms()
        {
            _farmSites.Clear();
        }
        
        /// <summary>
        /// Gets farms located in a specific zone.
        /// </summary>
        public List<FarmSite> GetFarmsInZone(string zoneId)
        {
            return _farmSites.Where(f => f.ZoneId == zoneId).ToList();
        }
        
        /// <summary>
        /// Gets the count of farms in a specific zone.
        /// </summary>
        public int GetFarmCountInZone(string zoneId)
        {
            return _farmSites.Count(f => f.ZoneId == zoneId);
        }
        
        /// <summary>
        /// Calculates average profit per pen across all farms.
        /// </summary>
        public decimal GetAverageProfitPerPen()
        {
            var totalPens = _farmSites.Sum(f => f.PenCount);
            if (totalPens == 0)
            {
                return 0;
            }
            
            return GetTotalProfit() / totalPens;
        }
        
        /// <summary>
        /// Gets performance summary for all farms.
        /// </summary>
        public MultiFarmPerformanceSummary GetPerformanceSummary()
        {
            return new MultiFarmPerformanceSummary
            {
                TotalFarms = _farmSites.Count,
                TotalPens = _farmSites.Sum(f => f.PenCount),
                TotalBiomassKg = GetTotalBiomass(),
                TotalRevenue = GetTotalRevenue(),
                TotalCosts = GetTotalCosts(),
                TotalProfit = GetTotalProfit(),
                AverageProfitPerFarm = _farmSites.Count > 0 ? GetTotalProfit() / _farmSites.Count : 0,
                BestPerformingFarmId = GetBestPerformingFarm()?.Id,
                WorstPerformingFarmId = GetWorstPerformingFarm()?.Id
            };
        }
    }
    
    /// <summary>
    /// Performance metrics for a specific zone.
    /// </summary>
    public struct ZonePerformance
    {
        public string ZoneId;
        public int FarmCount;
        public double TotalBiomassKg;
        public decimal TotalRevenue;
        public decimal TotalCosts;
        public decimal TotalProfit;
    }
    
    /// <summary>
    /// Summary of performance across all farms.
    /// </summary>
    public struct MultiFarmPerformanceSummary
    {
        public int TotalFarms;
        public int TotalPens;
        public double TotalBiomassKg;
        public decimal TotalRevenue;
        public decimal TotalCosts;
        public decimal TotalProfit;
        public decimal AverageProfitPerFarm;
        public string? BestPerformingFarmId;
        public string? WorstPerformingFarmId;
    }
}
