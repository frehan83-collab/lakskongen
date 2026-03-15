using System;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Skill level affects processing efficiency and quality
    /// </summary>
    public enum SkillLevel
    {
        Untrained,    // -15% yield
        Basic,        // -5% yield
        Competent,    // 0% (baseline)
        Skilled,      // +3% yield
        Expert,       // +7% yield
        Master        // +12% yield
    }
    
    /// <summary>
    /// Technology tier provides automation and precision improvements
    /// </summary>
    public enum TechnologyTier
    {
        Manual,       // Baseline
        SemiAuto,     // +5% yield, -20% labor
        Automated,    // +10% yield, -40% labor
        FullyAuto,    // +15% yield, -60% labor
        AIOptimized   // +20% yield, -70% labor, predictive quality
    }
    
    /// <summary>
    /// Calculates actual yields accounting for quality, skill, technology, and process conditions
    /// </summary>
    public static class YieldCalculator
    {
        /// <summary>
        /// Calculate final yield percentage for a processing operation
        /// </summary>
        /// <param name="product">Product being produced</param>
        /// <param name="inputQuality">Input fish quality (0-1, where 1.0 = perfect)</param>
        /// <param name="skillLevel">Worker skill level</param>
        /// <param name="techTier">Processing technology tier</param>
        /// <param name="temperatureControl">Temperature stability (0-1, 1.0 = perfect control)</param>
        /// <param name="freshnessHours">Hours since harvest (affects yield for premium products)</param>
        /// <returns>Actual yield percentage (0-1)</returns>
        public static decimal Calculate(
            ProductType product,
            decimal inputQuality,
            SkillLevel skillLevel = SkillLevel.Competent,
            TechnologyTier techTier = TechnologyTier.Manual,
            decimal temperatureControl = 1.0m,
            int freshnessHours = 0)
        {
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                throw new ArgumentException($"Unknown product: {product}");
            
            // Start with base yield adjusted for input quality
            decimal baseYield = ProductCatalog.CalculateActualYield(product, inputQuality);
            
            // Apply skill modifier
            decimal skillModifier = GetSkillModifier(skillLevel);
            
            // Apply technology modifier
            decimal techModifier = GetTechnologyModifier(techTier);
            
            // Temperature control affects quality-sensitive products
            decimal tempPenalty = 0m;
            if (def.Position == MarketPosition.Premium || def.Position == MarketPosition.Luxury)
            {
                tempPenalty = (1.0m - temperatureControl) * 0.08m; // Up to 8% loss
            }
            
            // Freshness matters for premium products
            decimal freshnessPenalty = CalculateFreshnessPenalty(product, freshnessHours);
            
            // Combine all factors
            decimal finalYield = baseYield * (1.0m + skillModifier + techModifier) - tempPenalty - freshnessPenalty;
            
            // Clamp to realistic range
            return Math.Max(0.05m, Math.Min(finalYield, def.BaseYieldPercent * 1.2m));
        }
        
        /// <summary>
        /// Calculate output kg from input kg using yield calculator
        /// </summary>
        public static int CalculateOutputKg(
            ProductType product,
            int inputKg,
            decimal inputQuality,
            SkillLevel skillLevel = SkillLevel.Competent,
            TechnologyTier techTier = TechnologyTier.Manual,
            decimal temperatureControl = 1.0m,
            int freshnessHours = 0)
        {
            decimal yieldPercent = Calculate(product, inputQuality, skillLevel, techTier, temperatureControl, freshnessHours);
            return (int)Math.Round(inputKg * (double)yieldPercent);
        }
        
        /// <summary>
        /// Get skill level modifier (additive to base yield)
        /// </summary>
        private static decimal GetSkillModifier(SkillLevel skill)
        {
            return skill switch
            {
                SkillLevel.Untrained => -0.15m,
                SkillLevel.Basic => -0.05m,
                SkillLevel.Competent => 0.00m,
                SkillLevel.Skilled => 0.03m,
                SkillLevel.Expert => 0.07m,
                SkillLevel.Master => 0.12m,
                _ => 0.00m
            };
        }
        
        /// <summary>
        /// Get technology tier modifier (additive to base yield)
        /// </summary>
        public static decimal GetTechnologyModifier(TechnologyTier tech)
        {
            return tech switch
            {
                TechnologyTier.Manual => 0.00m,
                TechnologyTier.SemiAuto => 0.05m,
                TechnologyTier.Automated => 0.10m,
                TechnologyTier.FullyAuto => 0.15m,
                TechnologyTier.AIOptimized => 0.20m,
                _ => 0.00m
            };
        }
        
        /// <summary>
        /// Calculate yield penalty from freshness degradation
        /// </summary>
        private static decimal CalculateFreshnessPenalty(ProductType product, int hoursOld)
        {
            if (hoursOld <= 0) return 0m;
            
            // Premium products degrade faster
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                return 0m;
            
            decimal degradeRate = def.Position switch
            {
                MarketPosition.Luxury => 0.008m,      // 0.8% per hour
                MarketPosition.Premium => 0.005m,     // 0.5% per hour
                MarketPosition.Differentiated => 0.002m,
                MarketPosition.Commodity => 0.001m,
                _ => 0.001m
            };
            
            // Freshness penalty caps at 24 hours
            decimal hours = Math.Min(hoursOld, 24);
            return hours * degradeRate;
        }
        
        /// <summary>
        /// Calculate labor cost reduction from technology
        /// </summary>
        public static decimal GetLaborReduction(TechnologyTier tech)
        {
            return tech switch
            {
                TechnologyTier.Manual => 0.00m,
                TechnologyTier.SemiAuto => 0.20m,
                TechnologyTier.Automated => 0.40m,
                TechnologyTier.FullyAuto => 0.60m,
                TechnologyTier.AIOptimized => 0.70m,
                _ => 0.00m
            };
        }
        
        /// <summary>
        /// Calculate reject rate (spoilage/quality failure) including random variation
        /// </summary>
        public static decimal CalculateRejectRate(ProductType product, decimal qualityScore, SkillLevel skill)
        {
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                return 0.05m;
            
            decimal baseReject = def.RejectRatePercent;
            
            // Poor quality increases rejects
            decimal qualityPenalty = (1.0m - qualityScore) * 0.10m;
            
            // Skill reduces rejects
            decimal skillReduction = GetSkillModifier(skill) * 0.5m;
            
            return Math.Max(0.01m, baseReject + qualityPenalty - skillReduction);
        }
        
        /// <summary>
        /// Calculate total effective output after rejects
        /// </summary>
        public static int CalculateNetOutput(
            ProductType product,
            int grossOutputKg,
            decimal qualityScore,
            SkillLevel skill)
        {
            decimal rejectRate = CalculateRejectRate(product, qualityScore, skill);
            int rejectedKg = (int)Math.Round(grossOutputKg * (double)rejectRate);
            return Math.Max(0, grossOutputKg - rejectedKg);
        }
    }
}
