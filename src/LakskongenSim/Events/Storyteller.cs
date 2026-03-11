using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Market;

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

        public Storyteller(TimeManager time, GameState state, int? seed = null)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _time.OnAdvanceDays += HandleAdvance;
            BuildDefaultEvents();
        }

        public void RegisterEvent(EventDefinition def)
        {
            _events.Add(def);
        }

        private void HandleAdvance(int days)
        {
            for (int i = 0; i < days; i++)
            {
                _state.Day += 1;
                TryMaybeTrigger(_state.Day);
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

                    // TODO: hook into Environment/Health systems to apply direct biological impacts
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
                    // TODO: integrate with DiseaseSystem to increase prevalence, spawn choices, timed decisions
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
    }
}
