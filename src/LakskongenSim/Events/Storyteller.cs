using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Lightweight AI Storyteller / Event Director inspired by RimWorld.
    /// - Registers a set of event definitions
    /// - Subscribes to the TimeManager and occasionally fires events
    /// - Tracks cooldowns and simple pacing to avoid stacking disasters
    /// </summary>
    public class Storyteller
    {
        private readonly TimeManager _time;
        private readonly GameState _state;
        private readonly Random _rng;
        private readonly List<EventDefinition> _events = new();
        private readonly Dictionary<string, int> _cooldowns = new();

        // Minimum days between any two events to avoid stacking
        public int MinSpacingDays { get; set; } = 3;
        private int _lastEventDay = -9999;

        // Tunables
        public double BaseDailyChance { get; set; } = 0.03; // baseline chance per day to attempt an event
        public double MaxDailyChance { get; set; } = 0.25;  // cap as game progresses

        public event Action<EventRecord>? OnEventRaised;

        public Storyteller(TimeManager time, GameState state, int? seed = null, bool enableDefaultEvents = true)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _time.OnAdvanceDays += HandleAdvance;
            if (enableDefaultEvents)
            {
                BuildDefaultEvents();
            }
        }

        public void RegisterEvent(EventDefinition def)
        {
            _events.Add(def);
        }

        private void HandleAdvance(int days)
        {
            // Note: State.Day is incremented by SimulationEngine (single source of truth)
            // We just need to check for events on each day tick
            for (int i = 0; i < days; i++)
            {
                TryMaybeTrigger(_state.Day + i);
            }
        }

        private void TryMaybeTrigger(int currentDay)
        {
            // pacing: avoid firing if we had a recent event
            if (currentDay - _lastEventDay < MinSpacingDays) return;

            // scale chance with progress (simple linear scaling by day)
            double progressScale = Math.Min(1.0, currentDay / 3650.0); // 10 years -> full
            double dailyChance = BaseDailyChance + (MaxDailyChance - BaseDailyChance) * progressScale;

            if (_rng.NextDouble() > dailyChance) return;

            // collect applicable events
            var pool = _events.Where(e => IsAvailable(e, currentDay)).ToList();
            if (!pool.Any()) return;

            var chosen = WeightedPick(pool);
            if (chosen == null) return;

            var record = new EventRecord
            {
                Day = currentDay,
                Id = chosen.Id,
                Name = chosen.Name,
                Category = chosen.Category
            };

            // apply effect
            try
            {
                chosen.Apply?.Invoke(_state, record);
            }
            catch (Exception ex)
            {
                record.Description = $"Event application threw: {ex.Message}";
            }

            // apply cooldowns and pacing
            _cooldowns[chosen.Id] = currentDay + chosen.CooldownDays;
            _lastEventDay = currentDay;

            // log into game state and expose to listeners
            _state.EventLog.Add(record);
            OnEventRaised?.Invoke(record);
        }

        private bool IsAvailable(EventDefinition def, int day)
        {
            if (day < def.MinDay || day > def.MaxDay) return false;
            if (_cooldowns.TryGetValue(def.Id, out var availableDay) && day < availableDay) return false;
            if (def.Condition != null && !def.Condition(_state)) return false;
            return true;
        }

        private EventDefinition? WeightedPick(List<EventDefinition> pool)
        {
            double total = pool.Sum(p => Math.Max(0.0, p.Weight));
            if (total <= 0) return null;
            double pick = _rng.NextDouble() * total;
            double acc = 0.0;
            foreach (var p in pool)
            {
                acc += p.Weight;
                if (pick <= acc) return p;
            }
            return pool.Last();
        }

        private void BuildDefaultEvents()
        {
            // Weather: Algal bloom — reduces growth / increases mortality temporarily
            RegisterEvent(new EventDefinition
            {
                Id = "weather-algal-bloom",
                Name = "Algal bloom",
                Category = EventCategory.Weather,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => gs.Zones != null && gs.Zones.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Harmful algal bloom reduces feed conversion and increases mortality for several days.";
                    // Simple effects: reduce balance (clean-up costs) and mark populations (crude)
                    rec.BalanceChange = -100000m;
                    gs.Balance += rec.BalanceChange;

                    // Apply direct biological impacts via zone system
                    ApplyBiologicalImpacts(gs);
                }
            });

            // Disease outbreak
            RegisterEvent(new EventDefinition
            {
                Id = "disease-outbreak",
                Name = "Disease outbreak",
                Category = EventCategory.Disease,
                Weight = 1.2,
                CooldownDays = 60,
                Condition = gs => gs.Populations != null && gs.Populations.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "A contagious disease spreads among pens — you must decide how to respond.";
                    rec.BalanceChange = -50000m;
                    gs.Balance += rec.BalanceChange;
                    
                    // Integrate with disease and health systems
                    ProcessDiseaseEvents(gs);
                }
            });

            // Market crash
            RegisterEvent(new EventDefinition
            {
                Id = "market-crash",
                Name = "Market price crash",
                Category = EventCategory.Market,
                Weight = 0.8,
                CooldownDays = 120,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Global market glut causes price to drop. Export prices fall temporarily.";
                    rec.BalanceChange = -20000m;
                    gs.Balance += rec.BalanceChange;
                    // If we have MarketSimulation accessible, we could push a shock there. Attempt to find one in registrered systems is out-of-scope here.
                }
            });

            // Equipment failure
            RegisterEvent(new EventDefinition
            {
                Id = "equipment-failure",
                Name = "Equipment failure",
                Category = EventCategory.Equipment,
                Weight = 0.6,
                CooldownDays = 45,
                Condition = gs => gs.SeaSites != null && gs.SeaSites.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Essential equipment failed requiring repair or replacement.";
                    rec.BalanceChange = -75000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            // Investor offer (positive)
            RegisterEvent(new EventDefinition
            {
                Id = "investor-offer",
                Name = "Investor offer",
                Category = EventCategory.Investor,
                Weight = 0.4,
                CooldownDays = 180,
                Condition = gs => gs.Balance < 1_000_000m, // more likely when small
                Apply = (gs, rec) =>
                {
                    rec.Description = "An investor offers a capital injection with strings attached.";
                    rec.BalanceChange = +250000m;
                    gs.Balance += rec.BalanceChange;
                }
            });

            // Regulatory change
            RegisterEvent(new EventDefinition
            {
                Id = "regulatory-change",
                Name = "Regulatory change",
                Category = EventCategory.Regulation,
                Weight = 0.3,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "New regulations increase compliance costs and change licensing rules.";
                    rec.BalanceChange = -120000m;
                    gs.Balance += rec.BalanceChange;
                }
            });
        }
        
        /// <summary>
        /// Apply direct biological impacts from environmental events to zones and populations.
        /// Called by events like algal blooms to affect fish growth and mortality.
        /// </summary>
        private void ApplyBiologicalImpacts(GameState state)
        {
            if (state.Zones == null || state.Populations == null) return;
            
            // Trigger algal bloom in a random zone
            var zonesWithFish = state.Zones.Where(z => z.CurrentFishBiomassKg > 0).ToList();
            if (!zonesWithFish.Any()) return;
            
            var affectedZone = zonesWithFish[_rng.Next(zonesWithFish.Count)];
            affectedZone.AlgaeBloomActive = true;
            affectedZone.NutrientLevel = 3.0; // push above bloom threshold
            
            // Apply mortality to populations in the affected zone
            var populationsInZone = state.Populations.Where(p => p.ZoneId == affectedZone.Id).ToList();
            foreach (var pop in populationsInZone)
            {
                // Apply 5-15% mortality from algal toxins and oxygen depletion
                double mortalityRate = 0.05 + _rng.NextDouble() * 0.10;
                int deaths = (int)(pop.TotalCount * mortalityRate);
                
                // Apply mortality across cohorts proportionally
                if (pop.Cohorts != null && pop.Cohorts.Count > 0)
                {
                    double totalBefore = pop.TotalCount;
                    for (int i = 0; i < pop.Cohorts.Count; i++)
                    {
                        int cohortDeaths = (int)(deaths * (pop.Cohorts[i].Count / totalBefore));
                        int newCount = Math.Max(0, pop.Cohorts[i].Count - cohortDeaths);
                        pop.Cohorts[i].Count = newCount;
                    }
                }
            }
        }
        
        /// <summary>
        /// Process disease events by checking population health status and triggering appropriate responses.
        /// Integrates with disease system to increase prevalence and spawn player choices.
        /// </summary>
        private void ProcessDiseaseEvents(GameState state)
        {
            if (state.Populations == null || state.Populations.Count == 0) return;
            
            // Select a random population to infect
            var targetPop = state.Populations[_rng.Next(state.Populations.Count)];
            
            // Increase disease prevalence in the population
            // (This assumes Population has disease tracking - if not, we apply mortality directly)
            double infectionRate = 0.10 + _rng.NextDouble() * 0.20; // 10-30% infection rate
            int infectedCount = (int)(targetPop.TotalCount * infectionRate);
            
            // Apply disease mortality (5-10% of infected fish die if untreated)
            double diseaseMortality = 0.05 + _rng.NextDouble() * 0.05;
            int diseaseDeaths = (int)(infectedCount * diseaseMortality);
            
            // Apply mortality across cohorts
            if (targetPop.Cohorts != null && targetPop.Cohorts.Count > 0)
            {
                double totalBefore = targetPop.TotalCount;
                for (int i = 0; i < targetPop.Cohorts.Count; i++)
                {
                    int cohortDeaths = (int)(diseaseDeaths * (targetPop.Cohorts[i].Count / totalBefore));
                    int newCount = Math.Max(0, targetPop.Cohorts[i].Count - cohortDeaths);
                    targetPop.Cohorts[i].Count = newCount;
                }
            }
            
            // Future: Spawn player choice events (treat vs cull vs quarantine)
            // For now, apply immediate effects and log the incident
            state.EventLog.Add(new EventRecord
            {
                Day = state.Day,
                Id = "disease-impact",
                Name = "Disease Impact",
                Category = EventCategory.Disease,
                Description = $"Disease outbreak affected {infectedCount} fish in population {targetPop.Id}. {diseaseDeaths} deaths occurred."
            });
        }
    }
}
