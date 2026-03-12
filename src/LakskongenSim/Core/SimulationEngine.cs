using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Core
{
    /// <summary>
    /// High-level orchestrator that connects subsystems and advances the simulation.
    /// </summary>
    public class SimulationEngine
    {
        private readonly List<object> _systems = new();
        public TimeManager Time { get; } = new();
        public GameState State { get; } = new();
        
        // Sustainability system
        public SustainabilityManager SustainabilityManager { get; private set; }
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Update weekly
        
        // Connected systems (set externally or via RegisterSystem)
        public Market.MarketSimulation MarketSimulation { get; set; }
        public Finance.FinanceManager FinanceManager { get; set; }

        /// <summary>
        /// Initialize the simulation engine and all subsystems.
        /// </summary>
        public void Initialize()
        {
            // Load sustainability configuration
            var configPath = Path.Combine("src", "LakskongenSim", "Data", "SustainabilityConfig.json");
            if (!File.Exists(configPath))
            {
                // Fallback: try relative to current directory
                configPath = Path.Combine("Data", "SustainabilityConfig.json");
            }

            SustainabilityConfig config;
            if (File.Exists(configPath))
            {
                var configJson = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<SustainabilityConfig>(configJson) ?? new SustainabilityConfig();
            }
            else
            {
                // Use default config if file not found
                config = new SustainabilityConfig();
                Console.WriteLine("⚠️ SustainabilityConfig.json not found, using defaults");
            }

            // Create sustainability manager
            SustainabilityManager = new SustainabilityManager(config);

            // Subscribe to sustainability events
            SustainabilityManager.OnScoreChanged += OnSustainabilityScoreChanged;
            SustainabilityManager.OnCertificationChanged += OnCertificationChanged;

            Console.WriteLine("✅ Sustainability system initialized");
        }

        /// <summary>
        /// Register a system to be managed. Systems should subscribe to TimeManager themselves or expose Tick.
        /// </summary>
        public void RegisterSystem(object system)
        {
            _systems.Add(system);
        }

        /// <summary>
        /// Advance the whole simulation by given days.
        /// </summary>
        public void Advance(int days)
        {
            Time.AdvanceDays(days);
            
            // Update sustainability score weekly
            if (SustainabilityManager != null && State.Day - _lastSustainabilityUpdate >= SUSTAINABILITY_UPDATE_INTERVAL)
            {
                SustainabilityManager.UpdateScore(State);
                _lastSustainabilityUpdate = State.Day;
            }
            
            State.Day += days;
        }

        // Event handlers for sustainability system
        private void OnSustainabilityScoreChanged(int score, CertificationLevel level)
        {
            Console.WriteLine($"🌱 Sustainability Score: {score}/100 ({level})");
            
            // Update finance bonuses based on score
            if (FinanceManager != null)
            {
                FinanceManager.UpdateSustainabilityBonuses(score);
            }
            
            // Trigger achievements
            if (score >= 95 && level == CertificationLevel.SustainabilityLeader)
            {
                Console.WriteLine("🏆 Achievement Unlocked: Sustainability Leader!");
            }
        }

        private void OnCertificationChanged(CertificationLevel newLevel)
        {
            Console.WriteLine($"📜 Certification Changed: {newLevel}");
            
            // Update market system with new certification
            if (MarketSimulation != null)
            {
                var multiplier = SustainabilityManager.GetPriceMultiplier(newLevel);
                MarketSimulation.UpdateCertificationLevel(newLevel, multiplier);
            }
            
            // Apply financial bonuses/penalties
            ApplyCertificationBonuses(newLevel);
            
            // Log news headlines for major certifications
            switch (newLevel)
            {
                case CertificationLevel.ASCCertified:
                    Console.WriteLine("📰 News: Farm Achieves ASC Certification!");
                    break;
                case CertificationLevel.OrganicCertified:
                    Console.WriteLine("📰 News: Organic Certification Awarded!");
                    break;
                case CertificationLevel.SustainabilityLeader:
                    Console.WriteLine("📰 News: Industry Leader - Sustainability Excellence!");
                    break;
                case CertificationLevel.NonCompliant:
                    Console.WriteLine("⚠️ News: Environmental Compliance Warning!");
                    break;
            }
        }
        
        /// <summary>
        /// Apply financial bonuses and penalties based on certification level.
        /// </summary>
        private void ApplyCertificationBonuses(CertificationLevel level)
        {
            if (FinanceManager == null || SustainabilityManager == null) return;
            
            var score = SustainabilityManager.CurrentScore;
            
            // Non-Compliant penalty: 50,000 NOK quarterly fine
            if (level == CertificationLevel.NonCompliant)
            {
                if (State.Day % 90 == 0) // Every quarter
                {
                    FinanceManager.Spend(50_000m);
                    Console.WriteLine("💸 Non-compliance fine: 50,000 NOK");
                }
            }
            
            // ESG Investment bonus at 80+ score (applied when making green tech investments)
            // This would be checked in investment systems, not applied here directly
            if (score >= 80)
            {
                Console.WriteLine("✅ ESG Investment Bonus Active: 30% discount on green technology");
            }
            
            // Government grant multiplier at 95+ score
            if (level == CertificationLevel.SustainabilityLeader)
            {
                Console.WriteLine("🏅 Government Grant Multiplier Active: 1.5x on all grants");
            }
        }
    }
}