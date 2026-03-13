using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Campaign and scenario system for Lakskongen.
    /// Provides 5 pre-built scenarios with different starting conditions, goals, and challenges.
    /// </summary>
    public class ScenarioSystem
    {
        private readonly Dictionary<string, Scenario> _scenarios = new();
        private Scenario? _currentScenario;

        public Scenario? CurrentScenario => _currentScenario;
        public IReadOnlyDictionary<string, Scenario> AllScenarios => _scenarios;

        public event Action<Scenario>? OnScenarioStarted;
        public event Action<Scenario, bool>? OnScenarioCompleted;

        public ScenarioSystem()
        {
            RegisterAllScenarios();
        }

        /// <summary>Start a scenario by ID.</summary>
        public bool StartScenario(string scenarioId)
        {
            if (!_scenarios.TryGetValue(scenarioId, out var scenario))
            {
                Console.WriteLine($"❌ Scenario not found: {scenarioId}");
                return false;
            }

            _currentScenario = scenario;
            Console.WriteLine($"🎮 Starting scenario: {scenario.Name}");
            Console.WriteLine($"   {scenario.Description}");
            Console.WriteLine($"   Starting capital: {scenario.StartingCapitalNOK:N0} NOK");
            Console.WriteLine($"   Region: {scenario.StartingRegion}");
            Console.WriteLine($"   Difficulty: {new string('⭐', scenario.Difficulty)}");
            Console.WriteLine($"   Goal: {scenario.WinCondition}");

            OnScenarioStarted?.Invoke(scenario);
            return true;
        }

        /// <summary>Check if current scenario win condition is met.</summary>
        public bool CheckWinCondition(GameState gameState)
        {
            if (_currentScenario == null) return false;
            if (_currentScenario.WinConditionCheck == null) return false;

            bool won = _currentScenario.WinConditionCheck(gameState);
            if (won)
            {
                Console.WriteLine($"🎉 Scenario completed: {_currentScenario.Name}!");
                OnScenarioCompleted?.Invoke(_currentScenario, true);
            }

            return won;
        }

        /// <summary>Check if scenario has failed (time limit exceeded, etc.).</summary>
        public bool CheckFailCondition(GameState gameState)
        {
            if (_currentScenario == null) return false;

            // Time limit check
            if (_currentScenario.TimeLimitDays.HasValue && gameState.Day > _currentScenario.TimeLimitDays.Value)
            {
                Console.WriteLine($"⏰ Time limit exceeded for scenario: {_currentScenario.Name}");
                OnScenarioCompleted?.Invoke(_currentScenario, false);
                return true;
            }

            return false;
        }

        public Scenario? GetScenario(string id) => _scenarios.GetValueOrDefault(id);

        // ══════════════════════════════════════════════════════════════════
        // SCENARIO DEFINITIONS
        // ══════════════════════════════════════════════════════════════════

        private void RegisterAllScenarios()
        {
            // ─── SCENARIO 1: FØRSTE SESONG (Tutorial) ───
            Register(new Scenario
            {
                Id = "første-sesong",
                Name = "Første Sesong",
                Description = "Din første sesong som laksebonde. Lær grunnleggende drift med et enkelt anlegg, rolig vær og stabile markeder. Perfekt for nybegynnere.",
                StartingCapitalNOK = 500_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 1,
                WinCondition = "Overlev første året med positiv kontantstrøm (>100,000 NOK) og høst ditt første laksekorhort.",
                SpecialRules = new List<string>
                {
                    "Værforhold: Kun Calm og Moderate (ingen stormer)",
                    "Markedspris: Stabil 71.50 NOK/kg ±5%",
                    "1 sea site tilgjengelig ved start",
                    "Redusert risiko for sykdomsutbrudd (50% normal sats)",
                    "Tutorial-meldinger aktivert"
                },
                TimeLimitDays = 365 * 2, // 2 years to complete
                StartingSites = 1,
                StartingLicenses = 0,
                WinConditionCheck = gs => gs.Day >= 365 && gs.Balance > 100_000m && gs.Populations.Any(p => p.Cohorts.Any(c => c.Stage == LifeStage.Harvest))
            });

            // ─── SCENARIO 2: LOFOTEN EVENTYR ───
            Register(new Scenario
            {
                Id = "lofoten-eventyr",
                Name = "Lofoten Eventyr",
                Description = "Start et oppdrettsanlegg i det værharde Nordland. Håndter brutale vinterstormer og lave temperaturer, men dra nytte av premium sashimi-priser fra japanske kjøpere.",
                StartingCapitalNOK = 750_000m,
                StartingRegion = ProductionRegion.Nordland,
                Difficulty = 3,
                WinCondition = "Oppnå 3M NOK i eksportinntekter til Japan-markedet innen 3 år.",
                SpecialRules = new List<string>
                {
                    "Vintersesongen (Des-Feb): 60% sjanse for Storm eller Severe værforhold",
                    "Temperatur: 2-4°C lavere enn normalt (langsommere vekst)",
                    "Japansk marked: +40% premiumpriser for Superior-grade laks",
                    "Økt dødelighetspåslag fra kulde og stormer (+15%)",
                    "2 sea sites tilgjengelig ved start"
                },
                TimeLimitDays = 365 * 3,
                StartingSites = 2,
                StartingLicenses = 1,
                WinConditionCheck = gs => false // TODO: Track export revenue by market when market system integrated
            });

            // ─── SCENARIO 3: LAKSEBARONENE (Competition) ───
            Register(new Scenario
            {
                Id = "laksebaronene",
                Name = "Laksebaronene",
                Description = "Konkurranse-scenario: Du starter på lik linje med 3 AI-konkurrenter. Den første som oppnår 10M NOK i netto fortjeneste vinner. Bruk strategisk markedsføring, oppkjøp og innovasjon.",
                StartingCapitalNOK = 1_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 4,
                WinCondition = "Bli den første til å nå 10M NOK i netto fortjeneste før konkurrentene.",
                SpecialRules = new List<string>
                {
                    "3 AI-konkurrenter starter samtidig med samme kapital",
                    "Markedspriser påvirkes av totalt tilbud fra alle produsenter",
                    "Konkurrenter kan kjøpe opp ledige lokaliteter",
                    "Mulighet til å kjøpe opp konkursrammede konkurrenter",
                    "Ukentlig leaderboard viser relativ posisjon",
                    "Standard værforhold og markedsvolatilitet"
                },
                TimeLimitDays = 365 * 5,
                StartingSites = 1,
                StartingLicenses = 1,
                WinConditionCheck = gs => gs.Balance >= 11_000_000m
            });

            // ─── SCENARIO 4: BÆREKRAFT MESTER ───
            Register(new Scenario
            {
                Id = "bærekraft-mester",
                Name = "Bærekraft Mester",
                Description = "Vinn ved å bli en bærekraftleder - ikke gjennom profitt, men ved å oppnå ASC Sustainability Leader-sertifisering. Balanser lønnsomhet med miljøvern.",
                StartingCapitalNOK = 800_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 4,
                WinCondition = "Oppnå 'Sustainability Leader'-sertifisering (95+ sustainability score) før dag 1825.",
                SpecialRules = new List<string>
                {
                    "Sustainability score starter på 50 (industrinorm)",
                    "Forbrukere gir +15% premium for ASC-sertifisert laks",
                    "Grønne obligasjoner tilgjengelig (lavere rente for bærekraftige operasjoner)",
                    "Strengere straffer for luseutbrudd og rømming (-30% score)",
                    "Bonus for bruk av rensefisk, landbasert smoltproduksjon og lavkarbon fôr",
                    "Må opprettholde lønnsomhet (balance > 0)"
                },
                TimeLimitDays = 365 * 5,
                StartingSites = 1,
                StartingLicenses = 1,
                WinConditionCheck = gs => false // TODO: Check sustainability score when system is integrated
            });

            // ─── SCENARIO 5: KRISE MANAGER ───
            Register(new Scenario
            {
                Id = "krise-manager",
                Name = "Krise Manager",
                Description = "Du overtar en mislykket oppdrettsbedrift: ILA-utbrudd pågår, 2M NOK i gjeld, og Mattilsynet har gitt varsel om sanksjoner. Overlev krisen og gjenoppbygg.",
                StartingCapitalNOK = -2_000_000m, // Starting in debt!
                StartingRegion = ProductionRegion.Rogaland,
                Difficulty = 5,
                WinCondition = "Overlev til dag 730 med positiv balanse (>500k NOK) og ingen aktive regulatoriske sanksjoner.",
                SpecialRules = new List<string>
                {
                    "Starter med -2M NOK gjeld (må betale 50k NOK/måned i renter)",
                    "ILA-utbrudd aktiv ved start (30% dødlighet, karantene i 90 dager)",
                    "Mattilsynet warning: Må redusere lusenivå til <0.2 per fisk innen 60 dager eller få bøter",
                    "1 sea site tilgjengelig, men underbemmanet og dårlig vedlikeholdt",
                    "Begrenset tilgang til kreditt (maks 500k NOK ekstra lån)",
                    "Ingen nye lisenser tilgjengelig før gjeld er nedbetalt"
                },
                TimeLimitDays = 730, // 2 years
                StartingSites = 1,
                StartingLicenses = 0,
                WinConditionCheck = gs => gs.Day >= 730 && gs.Balance > 500_000m
            });
        }

        private void Register(Scenario scenario) => _scenarios[scenario.Id] = scenario;
    }

    /// <summary>
    /// Represents a gameplay scenario with starting conditions and win conditions.
    /// </summary>
    public class Scenario
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal StartingCapitalNOK { get; set; }
        public ProductionRegion StartingRegion { get; set; }
        public int Difficulty { get; set; } // 1-5 stars
        public string WinCondition { get; set; } = "";
        public List<string> SpecialRules { get; set; } = new();
        public int? TimeLimitDays { get; set; }
        
        // Starting assets
        public int StartingSites { get; set; }
        public int StartingLicenses { get; set; }

        // Win condition evaluator
        public Func<GameState, bool>? WinConditionCheck { get; set; }
    }
}
