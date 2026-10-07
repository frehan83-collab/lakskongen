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
    /// 
    /// NOTE: Event Apply lambdas use GameState.Balance as fallback for tests that don't provide FinanceManager.
    /// This is intentional for backward compatibility.
    /// </summary>
    #pragma warning disable CS0618 // Type or member is obsolete (GameState.Balance fallbacks)
    public class Storyteller
    {
        private readonly TimeManager _time;
        private readonly GameState _state;
        private readonly Finance.FinanceManager? _financeManager;
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

        public Storyteller(TimeManager time, GameState state, Finance.FinanceManager? financeManager = null, int? seed = null, bool enableDefaultEvents = true)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
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
            _state.TrimEventLog();
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
                    decimal cost = 100000m;
                    rec.BalanceChange = -cost;
                    
                    // Use FinanceManager if available, otherwise fall back to direct balance modification (for tests)
                    if (_financeManager != null)
                    {
                        if (!_financeManager.Spend(cost))
                        {
                            rec.Description += " (Insufficient funds for cleanup!)";
                        }
                    }
                    else
                    {
                        gs.Balance -= cost; // Fallback for tests without FinanceManager
                    }

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
                    decimal cost = 50000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        if (!_financeManager.Spend(cost))
                        {
                            rec.Description += " (Insufficient funds for treatment!)";
                        }
                    }
                    else
                    {
                        gs.Balance -= cost;
                    }
                    
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
                    decimal cost = 20000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        if (!_financeManager.Spend(cost))
                        {
                            rec.Description += " (Insufficient funds for market shock penalty!)";
                        }
                    }
                    else
                    {
                        gs.Balance -= cost;
                    }
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
                    decimal cost = 75000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        if (!_financeManager.Spend(cost))
                        {
                            rec.Description += " (Insufficient funds for repairs!)";
                        }
                    }
                    else
                    {
                        gs.Balance -= cost;
                    }
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
                Condition = gs => _financeManager != null ? _financeManager.Balance < 1_000_000m : gs.Balance < 1_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "An investor offers a capital injection with strings attached.";
                    decimal revenue = 250000m;
                    rec.BalanceChange = revenue;
                    
                    if (_financeManager != null)
                    {
                        _financeManager.AddRevenue(revenue);
                    }
                    else
                    {
                        gs.Balance += revenue;
                    }
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
                    decimal cost = 120000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        if (!_financeManager.Spend(cost))
                        {
                            rec.Description += " (Insufficient funds for compliance!)";
                        }
                    }
                    else
                    {
                        gs.Balance -= cost;
                    }
                }
            });
        }
        
        /// <summary>
        /// Apply environmental state changes from events like algal blooms.
        /// Does NOT directly kill fish - mortality is applied by the zone/population advancement.
        /// This prevents double-mortality bugs.
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
            
            // NOTE: Do NOT directly modify cohort counts here.
            // The algal bloom state will be picked up by:
            // 1. WaterChemistry updates during Zone.AdvanceDays()
            // 2. MortalityModel applying higher rates due to poor water quality
            // 3. IntegratedDiseaseSystem increasing disease pressure in affected zone
            
            // This approach prevents double-mortality (event kills + disease system kills)
            SimLog.Info($"Algal bloom triggered in zone {affectedZone.Id} - mortality will be applied by simulation systems");
        }
        
        /// <summary>
        /// Mark population for increased disease pressure.
        /// Does NOT directly kill fish - mortality is applied by MortalityModel via disease rates.
        /// This prevents double-mortality (direct kills + disease system kills).
        /// </summary>
        private void ProcessDiseaseEvents(GameState state)
        {
            if (state.Populations == null || state.Populations.Count == 0) return;
            
            // Select a random population for outbreak
            var targetPop = state.Populations[_rng.Next(state.Populations.Count)];
            
            // Mark population for treatment tracking
            // The actual mortality will be applied by:
            // 1. IntegratedDiseaseSystem.Update() increasing prevalence
            // 2. MortalityModel.SetDiseaseMortalityRate() before AdvanceDays
            // 3. FishPopulation.AdvanceDays() applying the combined mortality
            
            // Estimate impact for event logging
            double estimatedInfectionRate = 0.10 + _rng.NextDouble() * 0.20; // 10-30%
            int estimatedInfected = (int)(targetPop.TotalCount * estimatedInfectionRate);
            
            // Log the outbreak for player notification
            state.EventLog.Add(new EventRecord
            {
                Day = state.Day,
                Id = "disease-impact",
                Name = "Disease Impact",
                Category = EventCategory.Disease,
                Description = $"Disease outbreak in population {targetPop.Id}. Estimated {estimatedInfected} fish at risk. Mortality will be applied by disease system."
            });
            state.TrimEventLog();

            SimLog.Info($"Disease outbreak in population {targetPop.Id} - mortality will be applied by IntegratedDiseaseSystem");
            
            // NOTE: The actual disease spread and mortality is handled by ApplyDiseaseConsequence
            // in SimulationEngine, which calls IntegratedDisease.Update() with extra days.
        }
    }
    #pragma warning restore CS0618
}
