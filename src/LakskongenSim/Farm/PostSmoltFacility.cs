using System;
using System.Collections.Generic;
namespace LakskongenSim.Farm
{
    /// <summary>
    /// Modern Post-Smolt / Large Smolt Facility (RAS-based on land).
    /// Based on: SalMar InnovaMar, Benchmark Genetics, Bakkafrost Scotland.
    /// 
    /// Purpose: Grow smolt to 200-500g before sea transfer, reducing:
    /// - Sea phase by 3-6 months (less lice exposure)
    /// - Mortality during vulnerable early sea phase
    /// - Overall production cost per kg harvested
    /// 
    /// Industry trend: Norway moving from 80-100g to 200-500g transfer weight.
    /// Some facilities targeting 1000g+ post-smolt ("large smolt" strategy).
    /// </summary>
    public class PostSmoltFacility
    {
        // ─── Identity ───
        public string Id { get; init; } = "";
        public string Name { get; set; } = "Post-Smolt Anlegg";
        public string ZoneId { get; set; } = "";

        // ─── Capacity ───
        /// <summary>Annual smolt output capacity.</summary>
        public int AnnualSmoltCapacity { get; set; } = 10_000_000; // 10M smolt/year
        
        /// <summary>Target transfer weight (grams). Industry moving toward 200-500g.</summary>
        public double TargetTransferWeightG { get; set; } = 300; // Default 300g post-smolt
        
        /// <summary>Current batches in production.</summary>
        public List<SmoltBatch> Batches { get; set; } = new();
        
        /// <summary>Number of tank sections.</summary>
        public int TankSections { get; set; } = 8;
        
        /// <summary>Water temperature (°C). Controlled environment.</summary>
        public double WaterTemp { get; set; } = 13.0; // Optimal for post-smolt growth

        // ─── Growth Parameters ───
        /// <summary>Daily growth rate (%/day of body weight). Post-smolt at optimal temp.</summary>
        public double DailyGrowthRate { get; set; } = 0.015; // 1.5% BW/day at optimal conditions
        
        /// <summary>Survival rate through post-smolt phase.</summary>
        public double SurvivalRate { get; set; } = 0.97; // 97% — better than sea (93-95%)
        
        /// <summary>FCR in controlled conditions.</summary>
        public double FCR { get; set; } = 0.95; // Better than sea phase (1.15) due to control

        // ─── Light Program ───
        /// <summary>Current photoperiod (hours of light). Controls smoltification.</summary>
        public double PhotoperiodHours { get; set; } = 16; // Long day = growth phase
        
        /// <summary>Has the short-day window been applied? (Required for smoltification)</summary>
        public bool ShortDayApplied { get; set; } = false;
        
        /// <summary>Days in short-day window (6 weeks typical).</summary>
        public int ShortDayDuration { get; set; } = 42;

        // ─── Vaccination ───
        /// <summary>Are fish vaccinated before transfer?</summary>
        public bool VaccinationCompleted { get; set; } = false;
        
        /// <summary>Cost per fish for vaccination.</summary>
        public decimal VaccinationCostPerFish { get; set; } = 3.5m; // NOK 3-4 per fish
        
        /// <summary>Days post-vaccination before transfer allowed.</summary>
        public int VaccinationRecoveryDays { get; set; } = 400; // Degree-days: ~400 at 12°C = 33 days

        // ─── Financial ───
        public decimal CAPEX { get; set; }
        public double ConstructionProgress { get; set; } = 0.0;
        public int ConstructionDays { get; set; } = 548; // ~1.5 years
        public int ConstructionStartDay { get; set; } = -1;
        
        public bool IsOperational => ConstructionProgress >= 1.0;

        public int EmployeeCount { get; set; } = 20;

        // ─── Events (not serialized) ───
        public event Action<string>? OnAlert;
        public event Action<string>? OnBatchReady;

        /// <summary>
        /// Daily tick: grow batches, check transfer readiness, manage light program.
        /// </summary>
        public PostSmoltTickResult TickDay(int currentDay)
        {
            var result = new PostSmoltTickResult();

            // Construction phase
            if (!IsOperational)
            {
                if (ConstructionStartDay >= 0)
                {
                    int elapsed = currentDay - ConstructionStartDay;
                    ConstructionProgress = Math.Min(1.0, (double)elapsed / ConstructionDays);
                    if (IsOperational)
                    {
                        OnAlert?.Invoke($"Post-smolt anlegget '{Name}' er operativt!");
                        result.MilestoneReached = true;
                    }
                }
                return result;
            }

            // ─── Grow Batches ───
            foreach (var batch in Batches)
            {
                if (batch.IsReadyForTransfer) continue;

                // Temperature-dependent growth (degree-days model)
                double tempGrowthFactor = WaterTemp / 12.0; // Normalized to optimal
                double dailyGrowth = batch.AverageWeightG * DailyGrowthRate * tempGrowthFactor;
                batch.AverageWeightG += dailyGrowth;
                batch.DegreeDays += WaterTemp;
                batch.DaysInFacility++;

                // Mortality (very low in controlled environment)
                double dailyMortality = (1.0 - Math.Pow(SurvivalRate, 1.0 / 180)); // Spread over ~180 days
                int deaths = (int)(batch.Count * dailyMortality);
                batch.Count = Math.Max(0, batch.Count - deaths);
                result.TotalMortality += deaths;

                // Feed consumption
                double biomassKg = batch.Count * batch.AverageWeightG / 1000.0;
                double feedKg = biomassKg * DailyGrowthRate * FCR;
                batch.TotalFeedConsumedKg += feedKg;
                result.TotalFeedKg += feedKg;

                // ─── Check Transfer Readiness ───
                if (batch.AverageWeightG >= TargetTransferWeightG && batch.VaccinationComplete)
                {
                    batch.IsReadyForTransfer = true;
                    result.BatchesReady++;
                    OnBatchReady?.Invoke($"Batch '{batch.Id}' er klar for sjøutsett! Snittevekt: {batch.AverageWeightG:F0}g, Antall: {batch.Count:N0}");
                }
                // Warn if weight reached but not vaccinated
                else if (batch.AverageWeightG >= TargetTransferWeightG && !batch.VaccinationComplete)
                {
                    OnAlert?.Invoke($"Batch '{batch.Id}' har nådd målvekt men mangler vaksinering!");
                }
            }

            // ─── Operating Costs ───
            result.DailyStaffCost = (decimal)EmployeeCount * 600_000m / 365m;
            result.DailyEnergyCost = 30_000m; // RAS-based, significant energy
            result.DailyFeedCost = (decimal)result.TotalFeedKg * 12m; // ~12 NOK/kg smolt feed
            result.TotalDailyOPEX = result.DailyStaffCost + result.DailyEnergyCost + result.DailyFeedCost;

            return result;
        }

        /// <summary>Start a new smolt batch.</summary>
        public SmoltBatch StartNewBatch(string id, int count, double startWeightG = 80)
        {
            var batch = new SmoltBatch
            {
                Id = id,
                Count = count,
                AverageWeightG = startWeightG,
                StartDate = DateTime.Now
            };
            Batches.Add(batch);
            return batch;
        }

        /// <summary>Vaccinate a batch. Returns total cost.</summary>
        public decimal VaccinateBatch(string batchId)
        {
            var batch = Batches.Find(b => b.Id == batchId);
            if (batch == null) return 0;
            
            batch.VaccinationComplete = true;
            decimal cost = batch.Count * VaccinationCostPerFish;
            OnAlert?.Invoke($"Batch '{batchId}' vaksinert. Kostnad: {cost:N0} NOK");
            return cost;
        }

        /// <summary>Transfer a batch to sea (removes from facility, returns the batch).</summary>
        public SmoltBatch? TransferToSea(string batchId)
        {
            var batch = Batches.Find(b => b.Id == batchId && b.IsReadyForTransfer);
            if (batch == null) return null;
            Batches.Remove(batch);
            return batch;
        }

        /// <summary>Start construction.</summary>
        public void StartConstruction(int currentDay)
        {
            ConstructionStartDay = currentDay;
            ConstructionProgress = 0.0;
            CAPEX = GetCAPEX();
        }

        /// <summary>CAPEX based on capacity.</summary>
        public decimal GetCAPEX()
        {
            // ~150-300M NOK for modern post-smolt facility
            return (decimal)(AnnualSmoltCapacity / 10_000_000.0) * 250_000_000m;
        }
    }

    /// <summary>
    /// A batch of smolt being grown in the post-smolt facility.
    /// </summary>
    public class SmoltBatch
    {
        public string Id { get; set; } = "";
        public int Count { get; set; }
        public double AverageWeightG { get; set; } = 80; // Start at 80g
        public double DegreeDays { get; set; } = 0;
        public int DaysInFacility { get; set; } = 0;
        public double TotalFeedConsumedKg { get; set; } = 0;
        public bool VaccinationComplete { get; set; } = false;
        public bool IsReadyForTransfer { get; set; } = false;
        public DateTime StartDate { get; set; }
    }

    public class PostSmoltTickResult
    {
        public int TotalMortality { get; set; }
        public double TotalFeedKg { get; set; }
        public int BatchesReady { get; set; }
        public decimal DailyStaffCost { get; set; }
        public decimal DailyEnergyCost { get; set; }
        public decimal DailyFeedCost { get; set; }
        public decimal TotalDailyOPEX { get; set; }
        public bool MilestoneReached { get; set; }
    }
}
