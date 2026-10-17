using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Data;
using LakskongenSim.Farm;

namespace LakskongenSim.Core
{
    /// <summary>One step of the first season (data in <c>godot/data/tutorial_steps.json</c>).</summary>
    public class TutorialStep
    {
        public string Id { get; init; } = "";
        public int StepNumber { get; init; }

        /// <summary><see cref="TutorialStepKind"/>: "do" (the player acts) or "wait" (the world moves).</summary>
        public string Kind { get; init; } = TutorialStepKind.Do;

        /// <summary><see cref="TutorialPace"/>: how the clock behaves while the step waits.</summary>
        public string Pace { get; init; } = TutorialPace.Hold;

        [JsonPropertyName("triggerCondition")]
        public string TriggerConditionString { get; init; } = "";

        [JsonPropertyName("completionCondition")]
        public string CompletionConditionString { get; init; } = "";

        /// <summary>The control the UI highlights (a target id the GDScript resolver knows).</summary>
        [JsonPropertyName("highlightTarget")]
        public string HighlightTarget { get; init; } = "";

        /// <summary>Tabs that open (in the tutorial scenario) once this step is reached.</summary>
        public List<string> Unlocks { get; init; } = new();

        /// <summary>Translation key of the step's title.</summary>
        [JsonIgnore]
        public string Title => KeyPrefix + "_TITLE";

        /// <summary>Translation key of what the step shows (the why).</summary>
        [JsonIgnore]
        public string Description => KeyPrefix + "_TEXT";

        /// <summary>Translation key of what the player does now.</summary>
        [JsonIgnore]
        public string Hint => KeyPrefix + "_DO";

        [JsonIgnore]
        private string KeyPrefix => "TUTORIAL_" + Id.ToUpperInvariant();

        [JsonIgnore]
        public Func<TutorialSystem, bool>? TriggerCondition { get; set; }

        [JsonIgnore]
        public Func<TutorialSystem, bool>? CompletionCondition { get; set; }

        /// <summary>Day this step was completed, or null if not yet completed.</summary>
        [JsonIgnore]
        public int? CompletedOnDay { get; internal set; }
    }

    /// <summary>
    /// The learn-by-doing first season (roadmap U2-02, owner decision D19):
    /// eighteen steps that each teach one verb by asking the player to do it.
    /// A step completes on a real UI event the screens send (<see cref="Mark"/>)
    /// or on an engine state only the player's verb or the world produces
    /// (<see cref="TutorialConditions"/>); never on a day count. It runs only in
    /// a scenario whose win condition is the tutorial (Første sesong): anywhere
    /// else it neither advances nor gates a panel. Steps pay nothing (D16);
    /// completing them all is the scenario's win, skipping them is not
    /// (<see cref="IsEarned"/>).
    ///
    /// The engine checks the steps at the end of every day
    /// (TutorialAnalyticsTickSystem) and the bridge checks them again after the
    /// player's commands, so a step the player finishes while the clock stands
    /// completes at once.
    /// </summary>
    #pragma warning disable CS0618 // Type or member is obsolete
    public class TutorialSystem
    {
        /// <summary>Embedded copy of <c>godot/data/tutorial_steps.json</c> (the one source of the steps).</summary>
        public const string EmbeddedResourceName = "LakskongenSim.Core.tutorial_steps.json";

        /// <summary>
        /// Mean live weight at which the growth step hands over to the harvest
        /// planner: 4.0 kg (the first-season track's harvest step since F1014).
        /// A 4 kg fish gives about 3.4 kg gutted, the lower edge of the 3-4 kg
        /// class; the planner then shows what waiting 2, 4 and 8 weeks adds.
        /// </summary>
        public const double HarvestTeachingWeightKg = 4.0;

        /// <summary>
        /// Months of the new site's running cost the funding step wants in the
        /// bank beside the build's price: 3. The build takes the cash and the
        /// new site has no fish to sell until its first harvest; three months
        /// of wages and upkeep keep the company off the overdraft while the
        /// first site grows its next generation (S1-03's liquidity ladder warns
        /// at a 60-day runway).
        /// </summary>
        public const int FundingReserveMonths = 3;

        private static IReadOnlyList<TutorialStep>? _cachedTemplate;

        private readonly SimulationEngine _engine;
        private readonly List<TutorialStep> _steps;
        private int _currentStepIndex;
        private string? _pendingMark;

        /// <summary>Fired when the player completes a tutorial step.</summary>
        public event Action<TutorialStep>? OnStepCompleted;

        /// <summary>The steps in order.</summary>
        public IReadOnlyList<TutorialStep> Steps => _steps;

        /// <summary>The active tutorial step, or null when the tutorial is finished.</summary>
        public TutorialStep? CurrentStep =>
            _currentStepIndex < _steps.Count ? _steps[_currentStepIndex] : null;

        /// <summary>True once every step is done or the tutorial was skipped.</summary>
        public bool IsComplete => _currentStepIndex >= _steps.Count;

        /// <summary>True after the player skipped the tutorial (saved).</summary>
        public bool IsSkipped => _engine.State.TutorialSkipped;

        /// <summary>True when every step was completed by play: the Tutorial win condition (D19: a skip is no win).</summary>
        public bool IsEarned => IsComplete && !IsSkipped;

        /// <summary>True in a scenario whose win condition is the tutorial (Første sesong, D19).</summary>
        public bool InTutorialScenario => _engine.Scenario?.ActiveScenario?.WinCondition == WinCondition.Tutorial;

        /// <summary>True while the tutorial runs: the tutorial scenario, not yet complete or skipped.</summary>
        public bool IsActive => InTutorialScenario && !IsComplete;

        /// <summary>1-based number of the current step (returns TotalSteps after completion).</summary>
        public int CurrentStepNumber =>
            _currentStepIndex < _steps.Count ? _steps[_currentStepIndex].StepNumber : _steps.Count;

        public int TotalSteps => _steps.Count;

        /// <summary>Steps marked complete (by play, or all of them after a skip).</summary>
        public int CompletedCount => _steps.Count(s => s.CompletedOnDay.HasValue);

        /// <summary>Steps the player completed by play (a skip adds none).</summary>
        public int EarnedCount => _steps.Count(s => _engine.State.CompletedTutorialStepIds.Contains(s.Id));

        /// <summary>Share of the tutorial completed by play (0-1): the Tutorial goal's progress.</summary>
        public double Progress01 => _steps.Count == 0 ? 1.0 : EarnedCount / (double)_steps.Count;

        public TutorialSystem(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _steps = CloneSteps(Template());
            foreach (var step in _steps)
            {
                step.TriggerCondition = TutorialConditions.Parse(step.TriggerConditionString, step.Id, isCompletion: false);
                step.CompletionCondition = TutorialConditions.Parse(step.CompletionConditionString, step.Id, isCompletion: true);
            }
            RestoreTutorialState();
        }

        // ── Steps (embedded data) ─────────────────────────────────────────────

        /// <summary>
        /// The steps as <c>tutorial_steps.json</c> lists them, validated: ids
        /// unique, numbered 1..n in order, a known kind and pace, and conditions
        /// from the vocabulary (an unknown one throws). Loaded from the embedded
        /// copy, so the game, the tests and an exported build read the same file.
        /// </summary>
        public static IReadOnlyList<TutorialStep> Template()
        {
            if (_cachedTemplate != null) return _cachedTemplate;
            using var stream = typeof(TutorialSystem).Assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' is missing");
            using var reader = new StreamReader(stream);
            _cachedTemplate = ParseSteps(reader.ReadToEnd());
            return _cachedTemplate;
        }

        /// <summary>Parse and validate a <c>tutorial_steps.json</c> text (throws <see cref="InvalidDataException"/> on a defect).</summary>
        public static IReadOnlyList<TutorialStep> ParseSteps(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var steps = JsonSerializer.Deserialize<List<TutorialStep>>(json, options)
                ?? throw new InvalidDataException("tutorial_steps.json is empty");
            if (steps.Count == 0)
                throw new InvalidDataException("tutorial_steps.json has no steps");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
                    throw new InvalidDataException($"tutorial step {i + 1}: missing or duplicate id '{step.Id}'");
                if (step.StepNumber != i + 1)
                    throw new InvalidDataException($"tutorial step '{step.Id}': number {step.StepNumber}, expected {i + 1}");
                if (!TutorialStepKind.All.Contains(step.Kind))
                    throw new InvalidDataException($"tutorial step '{step.Id}': unknown kind '{step.Kind}'");
                if (!TutorialPace.All.Contains(step.Pace))
                    throw new InvalidDataException($"tutorial step '{step.Id}': unknown pace '{step.Pace}'");
                TutorialConditions.Parse(step.TriggerConditionString, step.Id, isCompletion: false);
                TutorialConditions.Parse(step.CompletionConditionString, step.Id, isCompletion: true);
            }
            return steps;
        }

        private static List<TutorialStep> CloneSteps(IReadOnlyList<TutorialStep> template) => template
            .Select(s => new TutorialStep
            {
                Id = s.Id,
                StepNumber = s.StepNumber,
                Kind = s.Kind,
                Pace = s.Pace,
                TriggerConditionString = s.TriggerConditionString,
                CompletionConditionString = s.CompletionConditionString,
                HighlightTarget = s.HighlightTarget,
                Unlocks = new List<string>(s.Unlocks),
            })
            .ToList();

        // ── Saved progress ────────────────────────────────────────────────────

        /// <summary>
        /// Restore progress from the state (a new game, or a loaded one). A save
        /// from before this track (its step ids are not this track's) keeps its
        /// count of finished steps, as this track's first steps.
        /// </summary>
        private void RestoreTutorialState()
        {
            var state = _engine.State;
            foreach (var step in _steps)
                step.CompletedOnDay = null;
            _pendingMark = null;
            if (state.TutorialSkipped)
            {
                MarkAllComplete();
                return;
            }
            int index = Math.Clamp(state.TutorialCurrentStepIndex, 0, _steps.Count);
            var known = new HashSet<string>(_steps.Select(s => s.Id), StringComparer.Ordinal);
            if (state.CompletedTutorialStepIds.Any(id => !known.Contains(id)))
            {
                state.CompletedTutorialStepIds.Clear();
                foreach (var step in _steps.Take(index))
                    state.CompletedTutorialStepIds.Add(step.Id);
            }
            _currentStepIndex = index;
            foreach (var step in _steps.Take(index))
                step.CompletedOnDay = state.Day; // the real day is not saved
        }

        /// <summary>
        /// Re-run the restore AFTER AdoptSavedState copied the saved progress
        /// (the constructor ran against a fresh state).
        /// </summary>
        internal void ResyncFromState() => RestoreTutorialState();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Skip the tutorial: every step is marked done WITHOUT completion events
        /// and the skip is saved; a skipped tutorial is no win (D19).
        /// </summary>
        public void Skip()
        {
            _engine.State.TutorialSkipped = true;
            MarkAllComplete();
        }

        private void MarkAllComplete()
        {
            foreach (var step in _steps)
                step.CompletedOnDay ??= _engine.State.Day;
            _currentStepIndex = _steps.Count;
            _engine.State.TutorialCurrentStepIndex = _currentStepIndex;
            _pendingMark = null;
        }

        /// <summary>
        /// A UI event the player caused (<see cref="TutorialMarks"/>). It completes
        /// the current step if that step waits for exactly this event; an event
        /// for another step is not remembered (each step is shown, then done).
        /// Returns true when a step completed.
        /// </summary>
        public bool Mark(string mark)
        {
            if (!IsActive || string.IsNullOrEmpty(mark)) return false;
            _pendingMark = mark;
            try
            {
                return CheckProgress();
            }
            finally
            {
                _pendingMark = null;
            }
        }

        /// <summary>
        /// Evaluate the current step and advance while its condition holds
        /// (chaining through steps already satisfied). Runs at the end of every
        /// day and after the player's commands. Returns true when a step completed.
        /// </summary>
        public bool CheckProgress()
        {
            if (!IsActive) return false;
            bool any = false;
            while (!IsComplete)
            {
                var step = CurrentStep!;
                if (step.TriggerCondition != null && !step.TriggerCondition(this)) break;
                if (step.CompletionCondition == null || !step.CompletionCondition(this)) break;
                CompleteStep(step);
                any = true;
            }
            if (any && IsEarned)
                _engine.Scenario?.CheckWinConditions(_engine.State.SustainabilityScore, tutorialComplete: true);
            return any;
        }

        /// <summary>
        /// The current step's progress toward its goal where it has one (the
        /// growth step's mean weight, the funding step's cash), else null.
        /// </summary>
        public TutorialProgress? CurrentProgress()
        {
            if (!IsActive) return null;
            return CurrentStep!.CompletionConditionString switch
            {
                "harvest_weight_reached" => new TutorialProgress(AverageFishWeightKg, HarvestTeachingWeightKg, "kg"),
                "build_funded" => new TutorialProgress((double)LiveBalance, (double)FundingTargetNok(), "nok"),
                _ => null,
            };
        }

        /// <summary>Tabs open in the tutorial scenario now: none is gated outside it or after it.</summary>
        public bool IsTabUnlocked(string tabId)
        {
            if (!IsActive) return true;
            int unlockedAt = _steps.FindIndex(s => s.Unlocks.Contains(tabId, StringComparer.Ordinal));
            return unlockedAt < 0 || unlockedAt <= _currentStepIndex;
        }

        // ── Internal helpers used by conditions ───────────────────────────────

        internal GameState State => _engine.State;

        internal bool HasPendingMark(string mark) => string.Equals(_pendingMark, mark, StringComparison.Ordinal);

        /// <summary>The company's fjord localities, the scenario's first.</summary>
        internal IReadOnlyList<SeaSite> FjordLocalities => _engine.FjordSites().ToList();

        /// <summary>
        /// Live cash from FinanceManager (single source of truth).
        /// State.Balance is a day-end mirror and goes stale after mid-day
        /// transactions — conditions must read this instead.
        /// </summary>
        internal decimal LiveBalance => _engine.FinanceManager?.Balance ?? State.Balance;

        /// <summary>
        /// Fish have been sold (every harvest path books TotalHarvestRevenueNok).
        /// The sales total also counts sludge income, which the weekly
        /// valorization books automatically, so it completed these steps before
        /// any harvest.
        /// </summary>
        internal static bool HasSold(TutorialSystem t)
            => t.State.TotalHarvestRevenueNok > 0m || t.State.ProcessingSnapshots.Count >= 1;

        internal int TotalFishCount =>
            _engine.State.Populations.SelectMany(p => p.Cohorts).Sum(c => c.Count);

        internal double AverageFishWeightKg
        {
            get
            {
                var cohorts = _engine.State.Populations.SelectMany(p => p.Cohorts).ToList();
                int total = cohorts.Sum(c => c.Count);
                if (total == 0) return 0.0;
                return cohorts.Sum(c => c.Count * c.MeanWeightKg) / total;
            }
        }

        /// <summary>The site holds a standing "Avlus ved telling" order (F2-05).</summary>
        internal bool HasLiceOrder(string siteId) => State.Driftsplan.Any(e =>
            e.UnitId == siteId && e.Policy == FjordPolicies.DelouseOnCount);

        /// <summary>A fjord site was stocked on or after the day of the first harvest and holds fish.</summary>
        internal bool RestockedAfterFirstHarvest
        {
            get
            {
                if (!HasSold(this) || State.HarvestReports.Count == 0) return false;
                int firstHarvest = State.HarvestReports.Min(r => r.Day);
                return FjordLocalities.Any(s => s.LastStockedDay >= firstHarvest && s.Pens.Sum(p => p.TotalCount) > 0);
            }
        }

        /// <summary>
        /// A research project runs, or one beyond the scenario's starting
        /// research is done. Reads the live research system (the state's copy
        /// is synced at the end of the day, after the player's command).
        /// </summary>
        internal bool ResearchStartedByPlayer
        {
            get
            {
                var research = _engine.Research;
                string? active = research != null ? research.ActiveResearchId : State.ActiveResearchId;
                if (!string.IsNullOrEmpty(active)) return true;
                var unlocked = research != null ? research.GetUnlockedIds() : State.UnlockedResearchIds;
                var starting = _engine.Scenario?.ActiveScenario?.StartingResearch ?? Array.Empty<string>();
                return unlocked.Any(id => !starting.Contains(id, StringComparer.Ordinal));
            }
        }

        /// <summary>The company owes the bank (a loan it took; Første sesong starts debt-free).</summary>
        internal bool HasBorrowed => State.ActiveLoans.Any(l => l.Principal > 0m);

        /// <summary>
        /// Cash the funding step asks for: the second site's price in the first
        /// site's zone (<see cref="SimulationEngine.PlanSiteBuild"/>) plus
        /// <see cref="FundingReserveMonths"/> months of its running cost.
        /// </summary>
        internal decimal FundingTargetNok()
        {
            var home = FjordLocalities.FirstOrDefault();
            if (home == null) return 0m;
            var plan = _engine.PlanSiteBuild(home.ZoneId);
            return plan.CostNok + plan.RunningCostPerMonthNok * FundingReserveMonths;
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void CompleteStep(TutorialStep step)
        {
            step.CompletedOnDay = _engine.State.Day;
            _currentStepIndex++;
            _pendingMark = null;

            // Persist completion to GameState
            _engine.State.TutorialCurrentStepIndex = _currentStepIndex;
            _engine.State.CompletedTutorialStepIds.Add(step.Id);

            SimLog.Info($"Tutorial step {step.StepNumber}/{TotalSteps} complete: {step.Id}");

            OnStepCompleted?.Invoke(step);
        }
    }
    #pragma warning restore CS0618
}
