namespace LakskongenSim.Core;

/// <summary>
/// Scenario start as an engine verb plus its save/load wiring. The bridge used
/// to activate the scenario and then materialize its starting state WITHOUT the
/// engine, so insurance, the resource-rent flag and the starting disease were
/// never applied (F244); and nothing saved the active scenario, so a loaded run
/// could never be won or lost again (F243).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// Activate a scenario on the current game: reset cash to its starting
    /// capital and materialize its sites, research, buildings, insurance,
    /// mitigations, blooms, starting disease and tax rules.
    /// </summary>
    public CommandResult StartScenario(string scenarioId)
    {
        if (State == null || Scenario == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var scenario = Scenario.GetScenario(scenarioId ?? "");
        if (scenario == null)
            return CommandResult.Fail("unknown_scenario", $"Unknown scenario '{scenarioId}'");
        if (!scenario.IsUnlocked)
            return CommandResult.Fail("scenario_locked", $"Scenario '{scenario.Name}' is locked");

        Scenario.StartScenario(scenario.Id);
        scenario.ApplyStartingState(State, this);
        SyncScenarioToState();
        return CommandResult.Success($"Scenario '{scenario.Name}' started", id: scenario.Id);
    }

    /// <summary>Mirror the live scenario progress into GameState (save path).</summary>
    private void SyncScenarioToState()
    {
        if (Scenario == null) return;
        State.ActiveScenarioId = Scenario.ActiveScenario?.Id;
        State.ScenarioStartDay = Scenario.ScenarioStartDay;
        State.ScenarioIsWon = Scenario.IsWon;
        State.ScenarioIsLost = Scenario.IsLost;
        State.ScenarioLossReason = Scenario.LossReason;
    }

    /// <summary>Re-activate the saved scenario on the live system (load path).</summary>
    private void RestoreScenarioFromState(GameState loadedState)
    {
        Scenario?.RestoreActiveState(loadedState.ActiveScenarioId, loadedState.ScenarioStartDay,
            loadedState.ScenarioIsWon, loadedState.ScenarioIsLost, loadedState.ScenarioLossReason);
    }
}
