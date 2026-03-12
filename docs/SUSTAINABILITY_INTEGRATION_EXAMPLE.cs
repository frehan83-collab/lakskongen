// SUSTAINABILITY_INTEGRATION_EXAMPLE.cs
// Example integration of SustainabilityManager into SimulationEngine
// Author: Camilla Johansen
// Date: March 12, 2026

using System;
using System.IO;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Sustainability;
using LakskongenSim.Health;
using LakskongenSim.Farm;
using LakskongenSim.Logistics;

namespace LakskongenSim.Examples
{
    /// <summary>
    /// Example: How to integrate SustainabilityManager into your simulation engine.
    /// Copy relevant parts into your actual SimulationEngine.cs.
    /// </summary>
    public class SustainabilityIntegrationExample
    {
        private SustainabilityManager _sustainabilityManager;
        private GameState _gameState;
        
        // Existing systems (example references)
        private DiseaseSystem _diseaseSystem;
        private FeedSystem _feedSystem;
        private LogisticsManager _logisticsManager;

        public void Initialize()
        {
            // 1. Load configuration from JSON
            var configPath = "src/LakskongenSim/Data/SustainabilityConfig.json";
            var configJson = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<SustainabilityConfig>(configJson);

            // 2. Create SustainabilityManager
            _sustainabilityManager = new SustainabilityManager(config);

            // 3. Subscribe to events for UI updates
            _sustainabilityManager.OnScoreChanged += OnSustainabilityScoreChanged;
            _sustainabilityManager.OnCertificationChanged += OnCertificationChanged;

            Console.WriteLine("✅ Sustainability system initialized");
        }

        public void SimulationTick(int currentDay)
        {
            // Update sustainability score weekly (every 7 days)
            if (currentDay % 7 == 0)
            {
                UpdateSustainabilityScore();
            }

            // TODO: Other simulation logic...
        }

        private void UpdateSustainabilityScore()
        {
            // Calculate new score from current game state
            _sustainabilityManager.UpdateScore(_gameState);

            // Apply certification benefits
            ApplyCertificationBenefits();
        }

        private void ApplyCertificationBenefits()
        {
            var level = _sustainabilityManager.CurrentCertification;
            var multiplier = _sustainabilityManager.GetPriceMultiplier(level);

            // 1. Apply price multiplier to market sales
            // Example: MarketSystem.SetPriceMultiplier(multiplier);

            // 2. Apply ESG investment bonus (at 80+ score)
            if (_sustainabilityManager.CurrentScore >= 80)
            {
                // 30% discount on green tech investments
                // Example: InvestmentSystem.SetGreenTechDiscount(0.30m);
            }

            // 3. Apply government grant multiplier (at 95+ score)
            if (level == CertificationLevel.SustainabilityLeader)
            {
                // 1.5x on all government grants
                // Example: GrantSystem.SetMultiplier(1.5m);
            }

            // 4. Apply Non-Compliant penalties (at <50 score)
            if (level == CertificationLevel.NonCompliant)
            {
                // Quarterly fine: 50,000 NOK
                // Example: FinanceManager.Spend(50_000m);
                // Example: NewsSystem.PublishHeadline("Environmental Violation: Farm Fined");
            }

            // 5. Market access gating
            UpdateMarketAccess(level);
        }

        private void UpdateMarketAccess(CertificationLevel level)
        {
            // Gate premium markets based on certification
            switch (level)
            {
                case CertificationLevel.NonCompliant:
                    // Can only sell to: Domestic, Russia
                    // Block: EU, USA, Japan, premium buyers
                    // Example: MarketSystem.SetAccessibleMarkets(["Domestic", "Russia"]);
                    break;

                case CertificationLevel.BasicCompliance:
                    // Add: EU, USA, China (standard markets)
                    // Example: MarketSystem.SetAccessibleMarkets(["Domestic", "EU", "USA", "Russia", "China"]);
                    break;

                case CertificationLevel.ASCCertified:
                    // Add: Whole Foods, Waitrose (premium retail)
                    // Example: MarketSystem.AddPremiumBuyers(["WholeFoods", "Waitrose"]);
                    break;

                case CertificationLevel.OrganicCertified:
                case CertificationLevel.SustainabilityLeader:
                    // Add: Japan Premium, Michelin restaurants (ultra-premium)
                    // Example: MarketSystem.AddPremiumBuyers(["JapanPremium", "MichelinRestaurants"]);
                    break;
            }
        }

        // Event handlers
        private void OnSustainabilityScoreChanged(int score, CertificationLevel level)
        {
            Console.WriteLine($"🌱 Sustainability Score: {score}/100 ({level})");

            // Update UI
            // Example (Godot bridge):
            // var panel = GetNode<SustainabilityPanel>("UI/SustainabilityPanel");
            // var categories = _sustainabilityManager.GetComponentScores(_gameState);
            // panel.Call("update_display", score, level.ToString(), categories);

            // Trigger achievements
            if (score >= 95)
            {
                // Achievement: "Sustainability Leader"
                Console.WriteLine("🏆 Achievement Unlocked: Sustainability Leader!");
            }
        }

        private void OnCertificationChanged(CertificationLevel newLevel)
        {
            Console.WriteLine($"📜 Certification Changed: {newLevel}");

            // Show notification/popup
            // Example: NotificationSystem.Show($"New Certification: {newLevel}");

            // News headline
            switch (newLevel)
            {
                case CertificationLevel.ASCCertified:
                    // NewsSystem.PublishHeadline("Farm Achieves ASC Certification!");
                    break;
                case CertificationLevel.OrganicCertified:
                    // NewsSystem.PublishHeadline("Organic Certification Awarded!");
                    break;
                case CertificationLevel.SustainabilityLeader:
                    // NewsSystem.PublishHeadline("Industry Leader: Sustainability Excellence!");
                    break;
            }
        }

        // ===== INTEGRATION POINT EXAMPLES =====
        // Add these properties/methods to existing systems:

        /// <summary>
        /// EXAMPLE: Add to Health/SeaLiceModel.cs
        /// </summary>
        public class SeaLiceModelExtension
        {
            // Add this property to expose average lice count
            public double AverageLicePerFish { get; private set; }

            // Update this property in Tick() method
            public void Tick(int days)
            {
                // ... existing lice calculation logic ...
                
                // Calculate average
                // AverageLicePerFish = totalLice / totalFish;
            }
        }

        /// <summary>
        /// EXAMPLE: Add to Farm/SeaSite.cs
        /// </summary>
        public class SeaSiteExtension
        {
            // Add these properties
            public int EscapesThisYear { get; set; } = 0;
            public bool IsClosedContainment { get; set; } = false;
            public double CurrentDensity { get; private set; }
            public double MaxAllowedDensity { get; set; } = 25.0; // kg/m³ (Norwegian standard)

            // Add method to track escape events
            public void RecordEscape(int fishCount)
            {
                EscapesThisYear += fishCount;
                // Trigger event for sustainability system
            }

            // Reset annually (e.g., January 1)
            public void ResetAnnualCounters()
            {
                EscapesThisYear = 0;
            }
        }

        /// <summary>
        /// EXAMPLE: Add to Farm/FeedSystem.cs
        /// </summary>
        public class FeedSystemExtension
        {
            // Add these properties
            public double SustainabilityRating { get; private set; } // 0.0-1.0
            public bool UsesInsectMeal { get; set; }
            public bool UsesAlgae { get; set; }

            // Calculate sustainability rating based on feed composition
            public void UpdateSustainabilityRating()
            {
                // Example calculation:
                // var rating = 0.0;
                // rating += fishMealPercentage * 0.4;  // Wild-caught = 40% sustainable
                // rating += byproductPercentage * 0.7; // Byproduct = 70% sustainable
                // rating += soyPercentage * 0.8;       // Plant = 80% sustainable
                // rating += insectMealPercentage * 0.95; // Insect = 95% sustainable
                // rating += algaePercentage * 1.0;     // Algae = 100% sustainable
                // SustainabilityRating = rating;
            }
        }

        /// <summary>
        /// EXAMPLE: Add to Logistics/LogisticsManager.cs
        /// </summary>
        public class LogisticsManagerExtension
        {
            // Add method to calculate carbon footprint score
            public double CarbonFootprintScore()
            {
                // Calculate based on:
                // 1. Renewable energy percentage (0-10 points)
                var renewableEnergy = 0.3; // 30% renewable
                var energyScore = renewableEnergy * 10.0;

                // 2. Transport efficiency (0-3 points)
                var localProcessing = true; // vs long-haul shipping
                var transportScore = localProcessing ? 3.0 : 1.0;

                // 3. Vessel fuel type (0-2 points)
                var electricVessels = false; // diesel vs electric
                var fuelScore = electricVessels ? 2.0 : 0.0;

                // Total score (0-15 points → normalized to 0.0-1.0)
                var totalScore = (energyScore + transportScore + fuelScore) / 15.0;
                return Math.Clamp(totalScore, 0.0, 1.0);
            }

            // Track vehicle fuel consumption
            public void RecordFuelUsage(string vehicleId, double liters, string fuelType)
            {
                // Store for carbon footprint calculation
            }
        }
    }

    // ===== TESTING EXAMPLE =====

    public class SustainabilitySystemTest
    {
        public static void RunTest()
        {
            // 1. Load config
            var config = new SustainabilityConfig();
            var manager = new SustainabilityManager(config);

            // 2. Create test game state
            var testState = new GameState
            {
                Day = 365,
                Balance = 5_000_000m,
                Populations = new(),
                SeaSites = new()
            };

            // Add test data
            // testState.Populations.Add(...);
            // testState.SeaSites.Add(...);

            // 3. Calculate score
            var score = manager.CalculateScore(testState);
            var level = manager.GetCertificationLevel(score);
            var multiplier = manager.GetPriceMultiplier(level);

            Console.WriteLine($"Test Results:");
            Console.WriteLine($"  Score: {score}/100");
            Console.WriteLine($"  Certification: {level}");
            Console.WriteLine($"  Price Multiplier: {multiplier:P0}");

            // 4. Test component scores
            var components = manager.GetComponentScores(testState);
            Console.WriteLine($"  Component Breakdown:");
            foreach (var (category, points) in components)
            {
                Console.WriteLine($"    {category}: {points} points");
            }

            // 5. Test UpdateScore event
            manager.OnScoreChanged += (s, l) => Console.WriteLine($"Event: Score changed to {s} ({l})");
            manager.UpdateScore(testState);
        }
    }
}
