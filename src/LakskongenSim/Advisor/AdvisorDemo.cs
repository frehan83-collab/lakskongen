using System;
using System.Linq;
using LakskongenSim.Advisor;
using LakskongenSim.Environment;
using LakskongenSim.Market;
using LakskongenSim.Finance;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Biology;

namespace LakskongenSim.Demo
{
    /// <summary>
    /// Demonstration of the Intelligent Advisor System functionality.
    /// Shows various recommendation scenarios and system capabilities.
    /// </summary>
    public static class AdvisorDemo
    {
        public static void RunDemo()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        LAKSKONGEN INTELLIGENT ADVISOR DEMO                   ║");
            Console.WriteLine("║        Decision Support System v1.0                         ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

            // Setup simulation components
            var gameState = CreateSampleGameState();
            var envData = new MareanoEnvironmentalDataProvider(DateTime.Now);
            var market = new MarketSimulation();
            var finance = new FinanceManager(5_000_000m);

            // Initialize advisor
            var advisor = new IntelligentAdvisor(
                envData,
                market,
                finance,
                gameState,
                null, // No storyteller for demo
                null  // No persistence for demo
            );

            // Subscribe to events
            advisor.OnRecommendationGenerated += rec =>
            {
                Console.WriteLine($"\n📋 NY ANBEFALING GENERERT:");
                DisplayRecommendation(rec);
            };

            advisor.OnAlertGenerated += alert =>
            {
                Console.WriteLine($"\n{alert.SeverityEmoji} NY ALARM:");
                Console.WriteLine($"   {alert.Title}");
                Console.WriteLine($"   {alert.Message}\n");
            };

            Console.WriteLine("Scenarie 1: Normal drift med gode markedsforhold\n");
            Console.WriteLine(new string('-', 60));
            
            // Set up good market conditions
            market.ApplyExternalShock(1.0, 1.2); // High demand
            
            var recs1 = advisor.AnalyzeAndGenerateRecommendations();
            Console.WriteLine($"\nGenererte {recs1.Count} anbefalinger.\n");

            Console.WriteLine("\nScenarie 2: Kritisk oksygenmangel\n");
            Console.WriteLine(new string('-', 60));
            
            // Simulate critical oxygen by affecting zone
            var zone = gameState.Zones.First();
            zone.WaterChemistry.OxygenO2 = 4.5; // Critical low
            
            var recs2 = advisor.AnalyzeAndGenerateRecommendations();
            Console.WriteLine($"\nGenererte {recs2.Count} anbefalinger.\n");

            Console.WriteLine("\nScenarie 3: Høye laksepriser\n");
            Console.WriteLine(new string('-', 60));
            
            // Reset zone
            zone.WaterChemistry.OxygenO2 = 8.0;
            
            // Simulate high prices
            for (int i = 0; i < 30; i++)
            {
                market.AdvanceDays(1);
            }
            
            var recs3 = advisor.AnalyzeAndGenerateRecommendations();
            Console.WriteLine($"\nGenererte {recs3.Count} anbefalinger.\n");

            Console.WriteLine("\nScenarie 4: Høy lakselus-belastning\n");
            Console.WriteLine(new string('-', 60));
            
            // Add sea lice to population
            foreach (var site in gameState.SeaSites)
            {
                foreach (var pen in site.Pens)
                {
                    pen.SeaLiceLoad = 3.5; // Above threshold
                }
            }
            
            var recs4 = advisor.AnalyzeAndGenerateRecommendations();
            Console.WriteLine($"\nGenererte {recs4.Count} anbefalinger.\n");

            Console.WriteLine("\nScenarie 5: Lav likviditet\n");
            Console.WriteLine(new string('-', 60));
            
            // Reset sea lice
            foreach (var site in gameState.SeaSites)
            {
                foreach (var pen in site.Pens)
                {
                    pen.SeaLiceLoad = 0.5;
                }
            }
            
            // Drain cash
            finance.ForceSpend(4_950_000m);
            
            var recs5 = advisor.AnalyzeAndGenerateRecommendations();
            Console.WriteLine($"\nGenererte {recs5.Count} anbefalinger.\n");

            // Display summary
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RÅDGIVER OPPSUMMERING");
            Console.WriteLine(new string('=', 60));
            
            var summary = advisor.GetSummary();
            Console.WriteLine($"\nStatus: {summary.StatusText}");
            Console.WriteLine($"Aktive anbefalinger: {summary.TotalActiveRecommendations}");
            Console.WriteLine($"Kritiske alarmer: {summary.CriticalAlerts}");
            Console.WriteLine($"Advarsler: {summary.WarningAlerts}");
            Console.WriteLine($"Muligheter: {summary.Opportunities}");
            Console.WriteLine($"Hastetiltak: {summary.UrgentActions}");

            if (summary.TopPriority != null)
            {
                Console.WriteLine($"\n🔥 HØYESTE PRIORITET:");
                DisplayRecommendation(summary.TopPriority);
            }

            // Display performance metrics
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RÅDGIVER YTELSE");
            Console.WriteLine(new string('=', 60));
            
            var metrics = advisor.GetPerformanceMetrics();
            Console.WriteLine($"Totale anbefalinger: {metrics.TotalRecommendations}");
            Console.WriteLine($"Fulgt: {metrics.FollowedCount}");
            Console.WriteLine($"Suksessrate: {metrics.SuccessRate:P1}");
            Console.WriteLine($"Gjennomsnittlig tillit: {metrics.AverageConfidence:P1}");

            // Display decision patterns
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("BESLUTNINGSMØNSTER");
            Console.WriteLine(new string('=', 60));
            
            var patterns = advisor.GetDecisionPatterns();
            Console.WriteLine($"Totale beslutninger: {patterns.TotalDecisions}");
            Console.WriteLine($"Følger råd: {patterns.FollowRate:P1}");
            Console.WriteLine($"Gjennomsnittlig svartid: {patterns.AverageResponseTimeDays:F1} dager");

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("DEMO FULLFØRT");
            Console.WriteLine(new string('=', 60));
        }

        private static void DisplayRecommendation(Recommendation rec)
        {
            Console.WriteLine($"   ID: {rec.Id}");
            Console.WriteLine($"   Type: {rec.Type}");
            Console.WriteLine($"   Kategori: {rec.Category}");
            Console.WriteLine($"   Tittel: {rec.Title}");
            Console.WriteLine($"   Beskrivelse: {rec.Description}");
            Console.WriteLine($"   Anbefalt handling: {rec.RecommendedAction}");
            Console.WriteLine($"   Hastegrad: {rec.UrgencyLabel} ({rec.UrgencyRating}/5)");
            Console.WriteLine($"   Tillit: {rec.ConfidenceLabel} ({rec.ConfidenceLevel:P0})");
            Console.WriteLine($"   Forventet effekt: {rec.FormattedImpact}");
            Console.WriteLine($"   Tidshorisont: {rec.TimeHorizon}");
            
            if (rec.TargetZoneId != null)
                Console.WriteLine($"   Sone: {rec.TargetZoneId}");
            if (rec.TargetSiteId != null)
                Console.WriteLine($"   Lokalitet: {rec.TargetSiteId}");
        }

        private static GameState CreateSampleGameState()
        {
            var state = new GameState
            {
                Day = 365
            };

            // Create sample zones
            var zone1 = new Zone
            {
                Id = "PO03",
                WaterChemistry = new WaterChemistry
                {
                    Temperature = 11.0,
                    OxygenO2 = 8.5,
                    pH = 7.8
                },
                CurrentFishBiomassKg = 900_000
            };
            state.Zones.Add(zone1);

            // Create sample sea site
            var site = new SeaSite("SITE-001", "PO03")
            {
                MABTonnes = 780,
                CurrentStockingDensity = 18.0f,
                PenVolume = 50000f,
                NumberOfPens = 8
            };

            // Create sample population
            var population = new FishPopulation
            {
                Id = "POP-001",
                ZoneId = "PO03"
            };

            // Add cohorts
            population.AddCohort(new FishPopulation.Cohort(
                ageDays: 400,
                count: 50_000,
                meanWeightKg: 4.2,
                stage: Data.LifeStage.Harvest
            ));

            population.AddCohort(new FishPopulation.Cohort(
                ageDays: 380,
                count: 45_000,
                meanWeightKg: 4.0,
                stage: Data.LifeStage.Harvest
            ));

            population.SeaLiceLoad = 0.5;
            site.Pens.Add(population);
            state.SeaSites.Add(site);

            return state;
        }
    }
}
