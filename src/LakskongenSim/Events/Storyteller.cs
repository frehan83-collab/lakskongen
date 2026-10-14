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
        private readonly SimRngStream _rng;
        internal SimRngStream RngForSnapshot => _rng;
        private readonly List<EventDefinition> _events = new();
        private readonly Dictionary<string, int> _cooldowns = new();

        // Minimum days between any two events to avoid stacking
        public int MinSpacingDays { get; set; } = 3;
        private int _lastEventDay = -9999;
        internal int LastEventDayForSnapshot { get => _lastEventDay; set => _lastEventDay = value; }

        /// <summary>Persistence (Phase 1 wave 6): per-event cooldowns must
        /// survive a save or cooled-down events re-fire immediately after
        /// a load, diverging weather/market/disease consequences.</summary>
        internal System.Collections.Generic.Dictionary<string, int> CooldownsForSnapshot
        {
            get => new System.Collections.Generic.Dictionary<string, int>(_cooldowns);
            set { _cooldowns.Clear(); foreach (var kvp in value) _cooldowns[kvp.Key] = kvp.Value; }
        }

        // Tunables
        public double BaseDailyChance { get; set; } = 0.03; // baseline chance per day to attempt an event
        public double MaxDailyChance { get; set; } = 0.25;  // cap as game progresses

        public event Action<EventRecord>? OnEventRaised;

        /// <summary>
        /// Scale (0-1] for an event's cash COST, set by the engine from farm size.
        /// Event costs are sized for a standard commercial site; a smaller farm
        /// takes a proportionally smaller hit (a one-pen tutorial farm paid
        /// ~20M NOK of flat event costs in four years). Gains are not scaled.
        /// </summary>
        public Func<double>? CostScale { get; set; }

        /// <summary>Storm events, held back while a calm-weather scenario runs.</summary>
        public static readonly IReadOnlySet<string> StormEventIds = new HashSet<string> { "storm-damage" };

        /// <summary>True while the active scenario promises calm weather.</summary>
        public bool SuppressStormEvents { get; set; }

        /// <summary>
        /// Raised after an event's effect ran, with the cash it actually moved
        /// on the FinanceManager (0 when none), so the engine ledger can book it.
        /// </summary>
        public event Action<EventRecord, decimal>? OnEventCashSettled;

        public Storyteller(TimeManager time, GameState state, Finance.FinanceManager? financeManager = null, int? seed = null, bool enableDefaultEvents = true, SimRngStream? rng = null)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
            _rng = rng ?? (seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded()).Stream(SimRngStreams.Storyteller);
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

        /// <summary>Read-only access to all registered event definitions (for testing/mod verification).</summary>
        public IReadOnlyList<EventDefinition> GetEventDefinitions() => _events;

        /// <summary>Detach from the clock so a discarded Storyteller stops
        /// firing events. SimulationEngine calls this before replacing the
        /// instance on re-initialization (previously they stacked).</summary>
        public void Detach()
        {
            _time.OnAdvanceDays -= HandleAdvance;
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
            double progressScale = Math.Min(1.0, currentDay / (10.0 * LakskongenSim.Core.SimCalendar.DaysPerYear)); // 10 years -> full
            double dailyChance = BaseDailyChance + (MaxDailyChance - BaseDailyChance) * progressScale;

            if (_rng.NextDouble() > dailyChance) return;

            // collect applicable events
            var pool = _events.Where(e => IsAvailable(e, currentDay)).ToList();
            if (!pool.Any()) return;

            var chosen = WeightedPick(pool);
            if (chosen == null) return;

            Fire(chosen, currentDay);
        }

        /// <summary>
        /// Run one event: apply its effect, scale its cost to the farm, report
        /// the cash it moved, start its cooldown and log it. The daily roll uses
        /// this; tests call it to fire a specific event through the same path.
        /// </summary>
        internal EventRecord Fire(EventDefinition chosen, int currentDay)
        {
            var record = new EventRecord
            {
                Day = currentDay,
                Id = chosen.Id,
                Name = chosen.Name,
                Category = chosen.Category
            };

            // apply effect (measure the cash it moves so the ledger books it)
            decimal cashBefore = _financeManager?.Balance ?? 0m;
            try
            {
                chosen.Apply?.Invoke(_state, record);
            }
            catch (Exception ex)
            {
                record.Description = $"Event application threw: {ex.Message}";
            }
            if (_financeManager != null)
                OnEventCashSettled?.Invoke(record, ScaleEventCost(record, _financeManager.Balance - cashBefore));

            // apply cooldowns and pacing
            _cooldowns[chosen.Id] = currentDay + chosen.CooldownDays;
            _lastEventDay = currentDay;

            // log into game state and expose to listeners
            _state.EventLog.Add(record);
            _state.TrimEventLog();
            OnEventRaised?.Invoke(record);
            return record;
        }

        /// <summary>
        /// Shrink an event's cost to the farm (<see cref="CostScale"/>): the
        /// unscaled share is credited back and the record shows the cost paid.
        /// Returns the net cash the event moved.
        /// </summary>
        private decimal ScaleEventCost(EventRecord record, decimal delta)
        {
            if (delta >= 0m || CostScale == null || _financeManager == null) return delta;
            double scale = Math.Clamp(CostScale(), 0.0, 1.0);
            if (scale >= 1.0) return delta;
            decimal relief = -delta * (decimal)(1.0 - scale);
            _financeManager.AddRevenue(relief);
            if (record.BalanceChange < 0m)
                record.BalanceChange = Math.Round(record.BalanceChange * (decimal)scale);
            return delta + relief;
        }

        private bool IsAvailable(EventDefinition def, int day)
        {
            if (day < def.MinDay || day > def.MaxDay) return false;
            if (SuppressStormEvents && StormEventIds.Contains(def.Id)) return false;
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
                Name = "Algeoppblomstring",
                Category = EventCategory.Weather,
                Weight = 1.0,
                CooldownDays = 90,
                Condition = gs => gs.Zones != null && gs.Zones.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Skadelige alger svekker fôrutnyttelsen og øker dødeligheten i flere dager.";
                    decimal cost = 100000m;
                    rec.BalanceChange = -cost;
                    
                    // Use FinanceManager if available, otherwise fall back to direct balance modification (for tests)
                    if (_financeManager != null)
                    {
                        // Costs apply even into debt (as NorwegianEvents): a broke
                        // player must not dodge them.
                        if (!_financeManager.Spend(cost))
                        {
                            _financeManager.ForceSpend(cost);
                            rec.Description += " (Ikke nok penger: oppryddingen er betalt på kreditt)";
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
                Name = "Sykdomsutbrudd",
                Category = EventCategory.Disease,
                Weight = 1.2,
                CooldownDays = 60,
                Condition = gs => gs.Populations != null && gs.Populations.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En smittsom sykdom sprer seg mellom merdene — du må bestemme hvordan du svarer.";
                    decimal cost = 50000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        // Costs apply even into debt (as NorwegianEvents): a broke
                        // player must not dodge them.
                        if (!_financeManager.Spend(cost))
                        {
                            _financeManager.ForceSpend(cost);
                            rec.Description += " (Ikke nok penger: behandlingen er betalt på kreditt)";
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
                Name = "Prisfall i markedet",
                Category = EventCategory.Market,
                Weight = 0.8,
                CooldownDays = 120,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Overskudd i verdensmarkedet presser prisen ned. Eksportprisene faller en periode.";
                    decimal cost = 20000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        // Costs apply even into debt (as NorwegianEvents): a broke
                        // player must not dodge them.
                        if (!_financeManager.Spend(cost))
                        {
                            _financeManager.ForceSpend(cost);
                            rec.Description += " (Ikke nok penger: tapet er betalt på kreditt)";
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
                Name = "Utstyrssvikt",
                Category = EventCategory.Equipment,
                Weight = 0.6,
                CooldownDays = 45,
                Condition = gs => gs.SeaSites != null && gs.SeaSites.Count > 0,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Viktig utstyr har sviktet og må repareres eller byttes.";
                    decimal cost = 75000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        // Costs apply even into debt (as NorwegianEvents): a broke
                        // player must not dodge them.
                        if (!_financeManager.Spend(cost))
                        {
                            _financeManager.ForceSpend(cost);
                            rec.Description += " (Ikke nok penger: reparasjonen er betalt på kreditt)";
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
                Name = "Tilbud fra investor",
                Category = EventCategory.Investor,
                Weight = 0.4,
                CooldownDays = 180,
                Condition = gs => _financeManager != null ? _financeManager.Balance < 1_000_000m : gs.Balance < 1_000_000m,
                Apply = (gs, rec) =>
                {
                    rec.Description = "En investor tilbyr ny kapital — med betingelser.";
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
                Name = "Nye regler",
                Category = EventCategory.Regulation,
                Weight = 0.3,
                CooldownDays = 365,
                Condition = gs => true,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Nye regler gir høyere kostnader og endrer konsesjonsreglene.";
                    decimal cost = 120000m;
                    rec.BalanceChange = -cost;
                    
                    if (_financeManager != null)
                    {
                        // Costs apply even into debt (as NorwegianEvents): a broke
                        // player must not dodge them.
                        if (!_financeManager.Spend(cost))
                        {
                            _financeManager.ForceSpend(cost);
                            rec.Description += " (Ikke nok penger: kostnaden er betalt på kreditt)";
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
            affectedZone.TriggerAlgalBloom();
            affectedZone.NutrientLevel = 3.0; // nutrient pulse that fed the bloom
            
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
                Name = "Sykdom i bestanden",
                Category = EventCategory.Disease,
                Description = Core.SimText.Nb($"Sykdomsutbrudd i en fiskegruppe. Anslagsvis {estimatedInfected:N0} fisk er utsatt.")
            });
            state.TrimEventLog();

            SimLog.Info($"Disease outbreak in population {targetPop.Id} - mortality will be applied by IntegratedDiseaseSystem");
            
            // NOTE: The actual disease spread and mortality is handled by ApplyDiseaseConsequence
            // in SimulationEngine, which calls IntegratedDisease.Update() with extra days.
        }
    }
    #pragma warning restore CS0618
}
