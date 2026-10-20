using Godot;
using LakskongenSim.Core;

// GameBridge.Tutorial.cs — partial class slice (see project docs).
// <summary>
// The learn-by-doing first season (roadmap U2-02): the screens tell the
// tutorial what the player did (TutorialMark), the card reads the current
// step with its target, pace and progress (GetTutorialStatus), the dots and
// the tab gate read the step list (GetTutorialSteps, IsTutorialTabUnlocked).
// The tutorial runs only in a tutorial scenario (Første sesong, D19).
// </summary>
public partial class GameBridge
{
    /// <summary>True while the tutorial runs (the tutorial scenario, neither finished nor skipped).</summary>
    public bool TutorialActive => SimulationEngine?.Tutorial?.IsActive ?? false;

    /// <summary>True after the player skipped the tutorial (no win, D19).</summary>
    public bool TutorialSkipped => SimulationEngine?.Tutorial?.IsSkipped ?? false;

    /// <summary>True when every step was completed by play (the Tutorial win condition).</summary>
    public bool TutorialEarned => SimulationEngine?.Tutorial?.IsEarned ?? false;

    /// <summary>
    /// A UI event the player caused ("tab:pens", "inbox", "clock",
    /// "harvest_report", "map_area_home"). Completes the current step when it
    /// waits for exactly this event. True when a step completed.
    /// </summary>
    public bool TutorialMark(string mark)
    {
        var tutorial = SimulationEngine?.Tutorial;
        if (tutorial == null || !tutorial.Mark(mark ?? "")) return false;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Check the current step against the game now (after the player's
    /// commands, while the clock may stand). True when a step completed.
    /// </summary>
    public bool TutorialCheck()
    {
        var tutorial = SimulationEngine?.Tutorial;
        if (tutorial == null || !tutorial.CheckProgress()) return false;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>True when the tab may be opened (only the tutorial scenario gates tabs, until the step that teaches them).</summary>
    public bool IsTutorialTabUnlocked(string tabId) => SimulationEngine?.Tutorial?.IsTabUnlocked(tabId ?? "") ?? true;

    /// <summary>
    /// The current step for the card. Keys: active, skipped, earned, complete,
    /// in_scenario, step (1-based), total, completed (steps done by play), id,
    /// kind ("do"|"wait"), pace ("hold"|"slow"|"run"), target (highlight id),
    /// title_key, text_key, do_key, unlocks (Array), progress ({} or value,
    /// goal, unit "kg"|"nok").
    /// </summary>
    public Godot.Collections.Dictionary GetTutorialStatus()
    {
        var result = new Godot.Collections.Dictionary();
        var tutorial = SimulationEngine?.Tutorial;
        result["active"] = tutorial?.IsActive ?? false;
        result["skipped"] = tutorial?.IsSkipped ?? false;
        result["earned"] = tutorial?.IsEarned ?? false;
        result["complete"] = tutorial?.IsComplete ?? false;
        result["in_scenario"] = tutorial?.InTutorialScenario ?? false;
        result["total"] = tutorial?.TotalSteps ?? 0;
        result["completed"] = tutorial?.EarnedCount ?? 0;
        result["step"] = tutorial?.CurrentStepNumber ?? 0;
        var step = tutorial?.CurrentStep;
        result["id"] = step?.Id ?? "";
        result["kind"] = step?.Kind ?? "";
        result["pace"] = step?.Pace ?? "";
        result["target"] = step?.HighlightTarget ?? "";
        result["title_key"] = step?.Title ?? "";
        result["text_key"] = step?.Description ?? "";
        result["do_key"] = step?.Hint ?? "";
        var unlocks = new Godot.Collections.Array<string>();
        foreach (var tab in step?.Unlocks ?? new System.Collections.Generic.List<string>())
            unlocks.Add(tab);
        result["unlocks"] = unlocks;
        var progress = new Godot.Collections.Dictionary();
        if (tutorial?.CurrentProgress() is { } p)
        {
            progress["value"] = p.Value;
            progress["goal"] = p.Goal;
            progress["unit"] = p.Unit;
        }
        result["progress"] = progress;
        return result;
    }

    /// <summary>The steps in order ({number, id, kind, pace, target, title_key, unlocks, done}).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTutorialSteps()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var tutorial = SimulationEngine?.Tutorial;
        if (tutorial == null) return list;
        foreach (var step in tutorial.Steps)
        {
            var unlocks = new Godot.Collections.Array<string>();
            foreach (var tab in step.Unlocks)
                unlocks.Add(tab);
            list.Add(new Godot.Collections.Dictionary
            {
                ["number"] = step.StepNumber,
                ["id"] = step.Id,
                ["kind"] = step.Kind,
                ["pace"] = step.Pace,
                ["target"] = step.HighlightTarget,
                ["title_key"] = step.Title,
                ["unlocks"] = unlocks,
                ["done"] = step.CompletedOnDay.HasValue,
            });
        }
        return list;
    }
}
