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

        decimal balanceBefore = FinanceManager?.Balance ?? 0m;
        Scenario.StartScenario(scenario.Id);
        // The scenario resets cash to its starting capital. On day 0 that IS the
        // opening balance; mid-game it is booked as a capital adjustment so the
        // lifetime totals keep reconciling with cash.
        if (State.Day == 0)
            OpenBooks();
        else
        {
            BookCapitalAdjustment(balanceBefore);
            OpenStory();   // U2-03: the scenario's story starts with it
            State.Liquidity = new Finance.LiquidityState();   // and the bank reads its cash afresh
        }
        scenario.ApplyStartingState(State, this);
        ApplyScenarioRules(scenario);
        SyncScenarioToState();
        // The HUD temperature reports the scenario's home production area (S2-02).
        SyncSeaTemperatureReference();
        SelectGeoPlotCatalog();
        return CommandResult.Success($"Scenario '{scenario.Name}' started", id: scenario.Id);
    }

    /// <summary>
    /// Progress toward the active scenario's goal, measured as the win check
    /// judges it (null when no scenario is active). The goal HUD reads this.
    /// </summary>
    public ScenarioGoalStatus? GetScenarioGoalStatus()
        => Scenario?.GetGoalStatus(State?.SustainabilityScore ?? 0, Tutorial?.Progress01 ?? 0.0);

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
        ApplyScenarioRules(Scenario?.ActiveScenario);
        AssignScenarioProductionAreas(Scenario?.ActiveScenario);   // pre-S2-02 saves
        SyncSeaTemperatureReference();
    }

    /// <summary>Storm and severe weather transitions under a "harsh_storms" scenario.</summary>
    internal const double HarshStormFrequency = 1.5;

    /// <summary>Price noise and shock probability under an "easy_market" scenario.</summary>
    internal const double EasyMarketVolatilityScale = 0.5;

    /// <summary>Export price premium under a "premium_prices" scenario.</summary>
    internal const double PremiumPriceMultiplier = 1.10;

    /// <summary>Price multiplier from the active scenario's rules (1 = none).</summary>
    internal double ScenarioPriceMultiplier { get; private set; } = 1.0;

    /// <summary>
    /// Apply the active scenario's special rules to the live systems. They used
    /// to be labels only: the tutorial advertised calm weather and stable prices
    /// and got storms (550k NOK of repairs by day 150) and full volatility.
    /// Rules: calm_weather, harsh_storms, easy_market, premium_prices.
    /// </summary>
    private void ApplyScenarioRules(ScenarioDefinition? scenario)
    {
        bool Has(string key) => scenario?.SpecialRules != null
            && scenario.SpecialRules.TryGetValue(key, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        Weather?.ApplyScenarioWeather(
            Has("calm_weather") ? LakskongenSim.Environment.WeatherState.Moderate : LakskongenSim.Environment.WeatherState.Severe,
            Has("harsh_storms") ? HarshStormFrequency : 1.0);
        if (MarketSimulation != null)
            MarketSimulation.VolatilityScale = Has("easy_market") ? EasyMarketVolatilityScale : 1.0;
        ScenarioPriceMultiplier = Has("premium_prices") ? PremiumPriceMultiplier : 1.0;
        if (Storyteller != null)
            Storyteller.SuppressStormEvents = Has("calm_weather");
    }
}
