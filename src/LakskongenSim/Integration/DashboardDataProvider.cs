using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Market;

namespace LakskongenSim.Integration
{
    /// <summary>
    /// Central data provider for financial dashboards.
    /// Exposes economic data from SimulationEngine to GDScript via GameBridge.
    /// Provides P&amp;L, cash flow, ROI, cost breakdown, market prices, and scenario analysis.
    /// </summary>
    public class DashboardDataProvider
    {
        private readonly SimulationEngine _engine;

        public DashboardDataProvider(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        // ══════════════════════════════════════════════════════════════════════
        // P&amp;L STATEMENT DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns P&amp;L statement data as a Dictionary.
        /// Keys: "revenue", "cost_of_sales", "gross_profit", "operating_expenses",
        ///       "ebitda", "depreciation", "ebit", "interest", "tax", "net_profit",
        ///       "gross_margin_pct", "operating_margin_pct", "net_margin_pct".
        /// </summary>
        public Dictionary<string, object> GetPLData()
        {
            var result = new Dictionary<string, object>();

            // Revenue (simplified: current balance is proxy for cumulative revenue)
            decimal revenue = _engine.FinanceManager?.Balance ?? 0m;
            
            // Cost of Sales (COGS): feed costs, smolt costs, delousing
            decimal feedCosts = CalculateFeedCosts();
            decimal smoltCosts = CalculateSmoltCosts();
            decimal delousingCosts = CalculateDelousingCosts();
            decimal cogs = feedCosts + smoltCosts + delousingCosts;
            
            decimal grossProfit = revenue - cogs;
            
            // Operating Expenses (OPEX): labor, site maintenance, licenses
            decimal laborCosts = CalculateLaborCosts();
            decimal maintenanceCosts = CalculateMaintenanceCosts();
            decimal licenseFees = CalculateLicenseFees();
            decimal opex = laborCosts + maintenanceCosts + licenseFees;
            
            decimal ebitda = grossProfit - opex;
            
            // Depreciation (assets wear out over time)
            decimal depreciation = CalculateDepreciation();
            decimal ebit = ebitda - depreciation;
            
            // Interest & Tax
            decimal interest = CalculateInterestExpense();
            decimal taxableIncome = Math.Max(0, ebit - interest);
            decimal tax = taxableIncome * 0.22m; // 22% Norwegian corporate tax
            
            decimal netProfit = ebit - interest - tax;
            
            // Margins
            double grossMarginPct = revenue > 0 ? (double)(grossProfit / revenue * 100m) : 0.0;
            double operatingMarginPct = revenue > 0 ? (double)(ebitda / revenue * 100m) : 0.0;
            double netMarginPct = revenue > 0 ? (double)(netProfit / revenue * 100m) : 0.0;

            result["revenue"] = (double)revenue;
            result["cost_of_sales"] = (double)cogs;
            result["gross_profit"] = (double)grossProfit;
            result["operating_expenses"] = (double)opex;
            result["ebitda"] = (double)ebitda;
            result["depreciation"] = (double)depreciation;
            result["ebit"] = (double)ebit;
            result["interest"] = (double)interest;
            result["tax"] = (double)tax;
            result["net_profit"] = (double)netProfit;
            result["gross_margin_pct"] = grossMarginPct;
            result["operating_margin_pct"] = operatingMarginPct;
            result["net_margin_pct"] = netMarginPct;

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // CASH FLOW FORECAST DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns 12-month cash flow forecast as a List of Dictionaries.
        /// Each entry: "month", "operating_cash", "investing_cash", "financing_cash", "net_cash", "cumulative_cash".
        /// </summary>
        public List<Dictionary<string, object>> GetCashFlowForecast()
        {
            var forecast = new List<Dictionary<string, object>>();
            decimal cumulativeCash = _engine.FinanceManager?.Balance ?? 0m;

            for (int month = 1; month <= 12; month++)
            {
                // Operating cash: revenue - opex
                decimal operatingCash = EstimateMonthlyRevenue(month) - EstimateMonthlyOpex(month);
                
                // Investing cash: CAPEX (negative)
                decimal investingCash = -EstimateMonthlyCapex(month);
                
                // Financing cash: loans, dividends (simplified)
                decimal financingCash = 0m;
                
                decimal netCash = operatingCash + investingCash + financingCash;
                cumulativeCash += netCash;

                forecast.Add(new Dictionary<string, object>
                {
                    ["month"] = month,
                    ["operating_cash"] = (double)operatingCash,
                    ["investing_cash"] = (double)investingCash,
                    ["financing_cash"] = (double)financingCash,
                    ["net_cash"] = (double)netCash,
                    ["cumulative_cash"] = (double)cumulativeCash
                });
            }

            return forecast;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ROI ANALYSIS DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns ROI analysis data as a Dictionary.
        /// Keys: "total_investment", "annual_return", "roi_pct", "payback_months",
        ///       "npv", "irr_pct", "break_even_month".
        /// </summary>
        public Dictionary<string, object> GetROIAnalysis()
        {
            var result = new Dictionary<string, object>();

            // Total investment (CAPEX to date)
            decimal totalInvestment = CalculateTotalInvestment();
            
            // Annual return (net profit × 12)
            var plData = GetPLData();
            decimal monthlyNetProfit = (decimal)plData["net_profit"] / 12m;
            decimal annualReturn = monthlyNetProfit * 12m;
            
            // ROI %
            double roiPct = totalInvestment > 0 ? (double)(annualReturn / totalInvestment * 100m) : 0.0;
            
            // Payback period (months)
            double paybackMonths = monthlyNetProfit > 0 ? (double)(totalInvestment / monthlyNetProfit) : 0.0;
            
            // NPV & IRR (simplified placeholders)
            decimal npv = annualReturn * 5m - totalInvestment; // 5-year simplified NPV
            double irrPct = roiPct / 5.0; // Approximate IRR
            
            // Break-even month
            int breakEvenMonth = monthlyNetProfit > 0 ? (int)Math.Ceiling((double)(totalInvestment / monthlyNetProfit)) : 0;

            result["total_investment"] = (double)totalInvestment;
            result["annual_return"] = (double)annualReturn;
            result["roi_pct"] = roiPct;
            result["payback_months"] = paybackMonths;
            result["npv"] = (double)npv;
            result["irr_pct"] = irrPct;
            result["break_even_month"] = breakEvenMonth;

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // COST BREAKDOWN DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns detailed cost breakdown as a Dictionary.
        /// Keys: "opex_feed", "opex_labor", "opex_maintenance", "opex_licenses",
        ///       "opex_delousing", "opex_other", "opex_total",
        ///       "capex_licenses", "capex_pens", "capex_equipment", "capex_ras",
        ///       "capex_offshore", "capex_total".
        /// </summary>
        public Dictionary<string, object> GetCostBreakdown()
        {
            var result = new Dictionary<string, object>();

            // OPEX breakdown
            decimal opexFeed = CalculateFeedCosts();
            decimal opexLabor = CalculateLaborCosts();
            decimal opexMaintenance = CalculateMaintenanceCosts();
            decimal opexLicenses = CalculateLicenseFees();
            decimal opexDelousing = CalculateDelousingCosts();
            decimal opexOther = CalculateSmoltCosts(); // Smolt costs as "other" OPEX
            decimal opexTotal = opexFeed + opexLabor + opexMaintenance + opexLicenses + opexDelousing + opexOther;

            // CAPEX breakdown
            decimal capexLicenses = CalculateLicenseCapex();
            decimal capexPens = CalculatePenCapex();
            decimal capexEquipment = CalculateEquipmentCapex();
            decimal capexRas = CalculateRasCapex();
            decimal capexOffshore = CalculateOffshoreCapex();
            decimal capexTotal = capexLicenses + capexPens + capexEquipment + capexRas + capexOffshore;

            result["opex_feed"] = (double)opexFeed;
            result["opex_labor"] = (double)opexLabor;
            result["opex_maintenance"] = (double)opexMaintenance;
            result["opex_licenses"] = (double)opexLicenses;
            result["opex_delousing"] = (double)opexDelousing;
            result["opex_other"] = (double)opexOther;
            result["opex_total"] = (double)opexTotal;
            result["capex_licenses"] = (double)capexLicenses;
            result["capex_pens"] = (double)capexPens;
            result["capex_equipment"] = (double)capexEquipment;
            result["capex_ras"] = (double)capexRas;
            result["capex_offshore"] = (double)capexOffshore;
            result["capex_total"] = (double)capexTotal;

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // MARKET PRICE DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns market price chart data (current + 30-day history) as a List of Dictionaries.
        /// Each entry: "day", "price_per_kg", "weekly_avg", "volume_tonnes".
        /// </summary>
        public List<Dictionary<string, object>> GetMarketPrices()
        {
            var prices = new List<Dictionary<string, object>>();
            int currentDay = _engine.State?.Day ?? 0;

            // Generate 30-day price history (simplified)
            for (int daysAgo = 30; daysAgo >= 0; daysAgo--)
            {
                int day = currentDay - daysAgo;
                if (day < 0) continue;

                // Use current price with slight variation for historical data
                decimal basePrice = _engine.MarketSimulation?.PricePerKg ?? 70m;
                decimal priceVariation = (decimal)(Math.Sin(day * 0.1) * 5); // ±5 NOK variation
                decimal price = basePrice + priceVariation;
                
                decimal weeklyAvg = _engine.MarketSimulation?.WeeklyAvgPrice ?? 70m;
                double volumeTonnes = 1000 + (Math.Abs(Math.Sin(day * 0.05)) * 500); // 1000-1500 tonnes

                prices.Add(new Dictionary<string, object>
                {
                    ["day"] = day,
                    ["price_per_kg"] = (double)price,
                    ["weekly_avg"] = (double)weeklyAvg,
                    ["volume_tonnes"] = volumeTonnes
                });
            }

            return prices;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRODUCTION COST DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns production cost per kg data as a Dictionary.
        /// Keys: "total_cost_per_kg", "feed_cost_per_kg", "labor_cost_per_kg",
        ///       "maintenance_cost_per_kg", "other_cost_per_kg", "total_biomass_kg",
        ///       "fcr", "benchmark_cost_per_kg", "vs_benchmark_pct".
        /// </summary>
        public Dictionary<string, object> GetProductionCosts()
        {
            var result = new Dictionary<string, object>();

            // Total biomass
            double totalBiomassKg = _engine.State?.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count * c.MeanWeightKg) ?? 0.0;

            if (totalBiomassKg < 1.0) totalBiomassKg = 1.0; // Avoid division by zero

            // Cost breakdown per kg
            decimal feedCost = CalculateFeedCosts();
            decimal laborCost = CalculateLaborCosts();
            decimal maintenanceCost = CalculateMaintenanceCosts();
            decimal otherCost = CalculateDelousingCosts() + CalculateLicenseFees();

            double feedCostPerKg = (double)(feedCost / (decimal)totalBiomassKg);
            double laborCostPerKg = (double)(laborCost / (decimal)totalBiomassKg);
            double maintenanceCostPerKg = (double)(maintenanceCost / (decimal)totalBiomassKg);
            double otherCostPerKg = (double)(otherCost / (decimal)totalBiomassKg);
            double totalCostPerKg = feedCostPerKg + laborCostPerKg + maintenanceCostPerKg + otherCostPerKg;

            // FCR (Feed Conversion Ratio)
            double fcr = 1.2; // Placeholder - would calculate from feed consumed vs growth

            // Benchmark (industry average: 55 NOK/kg)
            double benchmarkCostPerKg = 55.0;
            double vsBenchmarkPct = benchmarkCostPerKg > 0 ? ((totalCostPerKg - benchmarkCostPerKg) / benchmarkCostPerKg) * 100.0 : 0.0;

            result["total_cost_per_kg"] = totalCostPerKg;
            result["feed_cost_per_kg"] = feedCostPerKg;
            result["labor_cost_per_kg"] = laborCostPerKg;
            result["maintenance_cost_per_kg"] = maintenanceCostPerKg;
            result["other_cost_per_kg"] = otherCostPerKg;
            result["total_biomass_kg"] = totalBiomassKg;
            result["fcr"] = fcr;
            result["benchmark_cost_per_kg"] = benchmarkCostPerKg;
            result["vs_benchmark_pct"] = vsBenchmarkPct;

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCENARIO COMPARISON DATA
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns scenario comparison data as a List of Dictionaries.
        /// Each scenario: "name", "revenue", "cost", "profit", "roi_pct", "risk_score".
        /// </summary>
        public List<Dictionary<string, object>> GetScenarioComparison()
        {
            var scenarios = new List<Dictionary<string, object>>();

            // Base scenario (current state)
            var plData = GetPLData();
            var roiData = GetROIAnalysis();
            
            scenarios.Add(new Dictionary<string, object>
            {
                ["name"] = "Current Operations",
                ["revenue"] = plData["revenue"],
                ["cost"] = (double)plData["cost_of_sales"] + (double)plData["operating_expenses"],
                ["profit"] = plData["net_profit"],
                ["roi_pct"] = roiData["roi_pct"],
                ["risk_score"] = 5.0 // Medium risk
            });

            // Expansion scenario
            scenarios.Add(new Dictionary<string, object>
            {
                ["name"] = "Expansion (+2 Sites)",
                ["revenue"] = (double)plData["revenue"] * 1.5,
                ["cost"] = ((double)plData["cost_of_sales"] + (double)plData["operating_expenses"]) * 1.6, // Costs increase faster
                ["profit"] = (double)plData["net_profit"] * 1.3,
                ["roi_pct"] = (double)roiData["roi_pct"] * 0.85, // Lower initial ROI
                ["risk_score"] = 7.0 // Higher risk
            });

            // Optimization scenario
            scenarios.Add(new Dictionary<string, object>
            {
                ["name"] = "Cost Optimization",
                ["revenue"] = plData["revenue"],
                ["cost"] = ((double)plData["cost_of_sales"] + (double)plData["operating_expenses"]) * 0.85, // 15% cost reduction
                ["profit"] = (double)plData["net_profit"] * 1.3,
                ["roi_pct"] = (double)roiData["roi_pct"] * 1.25,
                ["risk_score"] = 3.0 // Lower risk
            });

            // Premium strategy scenario
            scenarios.Add(new Dictionary<string, object>
            {
                ["name"] = "Premium Market",
                ["revenue"] = (double)plData["revenue"] * 1.2,
                ["cost"] = ((double)plData["cost_of_sales"] + (double)plData["operating_expenses"]) * 1.1, // Higher quality costs
                ["profit"] = (double)plData["net_profit"] * 1.4,
                ["roi_pct"] = (double)roiData["roi_pct"] * 1.15,
                ["risk_score"] = 6.0 // Moderate-high risk
            });

            return scenarios;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ADVISOR RECOMMENDATIONS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns AI advisor recommendations as a List of strings.
        /// </summary>
        public List<string> GetAdvisorRecommendations()
        {
            var recommendations = new List<string>();

            // Cash position analysis
            decimal balance = _engine.FinanceManager?.Balance ?? 0m;
            if (balance < 1_000_000m)
                recommendations.Add("⚠️ Low cash reserves. Consider securing financing or delaying expansion.");
            else if (balance > 50_000_000m)
                recommendations.Add("💰 Strong cash position. Good opportunity for strategic investments.");

            // Cost efficiency
            var costData = GetProductionCosts();
            double costPerKg = (double)costData["total_cost_per_kg"];
            if (costPerKg > 60.0)
                recommendations.Add("📊 Production costs above industry average. Review feed procurement and labor efficiency.");
            else if (costPerKg < 50.0)
                recommendations.Add("✅ Excellent cost efficiency. Maintain current operational practices.");

            // Market timing
            decimal currentPrice = _engine.MarketSimulation?.PricePerKg ?? 70m;
            if (currentPrice > 75m)
                recommendations.Add("📈 Market prices favorable. Consider accelerating harvest schedule.");
            else if (currentPrice < 65m)
                recommendations.Add("📉 Market prices depressed. Delay harvest if possible or lock in forward contracts.");

            // Sustainability
            int sustainabilityScore = _engine.SustainabilityManager?.CurrentScore ?? 50;
            if (sustainabilityScore < 70)
                recommendations.Add("🌱 Sustainability score needs improvement. Invest in ASC certification to access premium markets.");
            else if (sustainabilityScore > 90)
                recommendations.Add("🏅 Excellent sustainability performance. Leverage ESG credentials for premium pricing.");

            // Capacity utilization
            int siteCount = _engine.State?.SeaSites?.Count ?? 0;
            if (siteCount < 2)
                recommendations.Add("🏗️ Underutilized capacity. Consider acquiring additional licenses to scale production.");

            return recommendations;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIVATE HELPER METHODS (Cost Estimation)
        // ══════════════════════════════════════════════════════════════════════

        private decimal CalculateFeedCosts()
        {
            // Feed costs: ~15 NOK/kg biomass (industry average)
            double totalBiomassKg = _engine.State?.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count * c.MeanWeightKg) ?? 0.0;
            return (decimal)totalBiomassKg * 15m;
        }

        private decimal CalculateSmoltCosts()
        {
            // Smolt costs: 15 NOK per fish released (simplified)
            int totalFish = _engine.State?.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count) ?? 0;
            return totalFish * 15m / 12m; // Amortized monthly
        }

        private decimal CalculateDelousingCosts()
        {
            // Delousing: 50 NOK per fish when lice levels high
            double licePerFish = _engine.SeaLice?.AverageLicePerFish ?? 0.0;
            if (licePerFish < 0.5) return 0m;
            
            int totalFish = _engine.State?.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count) ?? 0;
            return totalFish * 50m / 12m; // Amortized monthly
        }

        private decimal CalculateLaborCosts()
        {
            // Labor: ~50k NOK/month per site
            int siteCount = _engine.State?.SeaSites?.Count ?? 1;
            return siteCount * 50_000m;
        }

        private decimal CalculateMaintenanceCosts()
        {
            // Maintenance: 2% of asset value per year
            int siteCount = _engine.State?.SeaSites?.Count ?? 1;
            return siteCount * 30_000_000m * 0.02m / 12m; // 2% annual
        }

        private decimal CalculateLicenseFees()
        {
            // License fees: 50k NOK per site annually
            int siteCount = _engine.State?.SeaSites?.Count ?? 1;
            return siteCount * 50_000m / 12m;
        }

        private decimal CalculateDepreciation()
        {
            // Depreciation: 10% of CAPEX per year
            decimal totalCapex = CalculateTotalInvestment();
            return totalCapex * 0.10m / 12m;
        }

        private decimal CalculateInterestExpense()
        {
            // Interest: placeholder (no loan system yet)
            return 0m;
        }

        private decimal CalculateTotalInvestment()
        {
            // Total investment: sum of all CAPEX
            return CalculateLicenseCapex() + CalculatePenCapex() + 
                   CalculateEquipmentCapex() + CalculateRasCapex() + CalculateOffshoreCapex();
        }

        private decimal CalculateLicenseCapex()
        {
            int siteCount = _engine.State?.SeaSites?.Count ?? 0;
            return siteCount * 30_000_000m; // 30M per license
        }

        private decimal CalculatePenCapex()
        {
            int siteCount = _engine.State?.SeaSites?.Count ?? 0;
            return siteCount * 5_000_000m; // 5M per site for pen infrastructure
        }

        private decimal CalculateEquipmentCapex()
        {
            // Equipment: feed barges, workboats, etc.
            return 10_000_000m; // Simplified
        }

        private decimal CalculateRasCapex()
        {
            // RAS facilities (if any)
            int rasCount = _engine.RASFacilities?.Count ?? 0;
            return rasCount * 1_000_000_000m; // 1B per RAS
        }

        private decimal CalculateOffshoreCapex()
        {
            // Offshore farms (if any)
            int offshoreCount = _engine.OffshoreFarms?.Count ?? 0;
            return offshoreCount * 800_000_000m; // 800M per offshore unit
        }

        private decimal EstimateMonthlyRevenue(int monthsAhead)
        {
            // Simplified: assume constant revenue growth
            double totalBiomassKg = _engine.State?.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count * c.MeanWeightKg) ?? 0.0;
            
            decimal pricePerKg = _engine.MarketSimulation?.PricePerKg ?? 70m;
            decimal monthlyHarvest = (decimal)totalBiomassKg * 0.05m; // 5% per month
            return monthlyHarvest * pricePerKg;
        }

        private decimal EstimateMonthlyOpex(int monthsAhead)
        {
            // Simplified: current OPEX + 2% growth per month
            var costData = GetCostBreakdown();
            decimal currentOpex = (decimal)costData["opex_total"];
            return currentOpex * (1m + (monthsAhead * 0.02m));
        }

        private decimal EstimateMonthlyCapex(int monthsAhead)
        {
            // Simplified: assume CAPEX spike every 6 months (license acquisition)
            if (monthsAhead % 6 == 0)
                return 30_000_000m; // New license
            return 500_000m; // Routine equipment purchases
        }
    }
}
