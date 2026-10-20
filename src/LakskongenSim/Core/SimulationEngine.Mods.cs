using System;
using System.IO;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Phase 4.6 — modding parity (engine side).
///
/// Discovery is owned by the composition root (the Godot bridge resolves
/// user://mods) which hands the chosen directory to <see cref="LoadMods"/>.
/// Every subsequent <see cref="SimulationEngine.Initialize"/> re-applies the
/// configured mods onto the freshly-built systems, so this is the single path
/// mod data takes into the simulation:
/// <list type="bullet">
/// <item>events → Storyteller (additive; mod events are registered alongside
/// the built-in roster).</item>
/// <item>research nodes → ResearchSystem.AddModNodes (core nodes win on ID
/// collision).</item>
/// <item>scenarios → ScenarioSystem.RegisterModScenarios (core scenarios are
/// never overwritten).</item>
/// </list>
/// Fish species are parsed by the loader but intentionally not consumed:
/// the engine simulates Atlantic salmon only (documented non-goal).
/// </summary>
public partial class SimulationEngine
{
    private string? _modsDirectory;
    private ModLoader? _modLoader;
    private object? _modsAppliedForState;

    /// <summary>The mod loader bound via <see cref="LoadMods"/> (null when no
    /// mods directory has been configured).</summary>
    public ModLoader? ModLoader => _modLoader;

    /// <summary>
    /// Configure the mods directory. Applied immediately when the engine is
    /// initialized; otherwise applied by the next <c>Initialize</c> call.
    /// Missing directories are remembered but a no-op until they exist.
    /// </summary>
    public void LoadMods(string? modsDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(modsDirectory)
            ? null
            : Path.GetFullPath(modsDirectory);
        // Another directory needs its own loader (the old one keeps reading
        // the old folder).
        if (directory != _modsDirectory)
        {
            _modLoader = null;
            _modsAppliedForState = null;
        }
        _modsDirectory = directory;

        if (_modsDirectory == null)
            return;

        if (Storyteller != null && Research != null && Scenario != null)
            ApplyMods();
    }

    /// <summary>
    /// Default mods-directory resolution for headless/dev runs: a
    /// <c>mods/</c> folder beside the executable. The Godot bridge resolves
    /// its own (user://mods) and passes it explicitly.
    /// </summary>
    public static string? ResolveModsDirectory()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "mods");
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Re-apply the configured mods onto the current systems. Idempotent per
    /// engine state: calling it twice between two <c>Initialize</c> calls is
    /// a no-op, so events never double-register.
    /// </summary>
    private void ApplyMods()
    {
        if (_modsDirectory == null) return;
        if (ReferenceEquals(_modsAppliedForState, State)) return;
        if (!Directory.Exists(_modsDirectory)) return;

        try
        {
            _modLoader ??= new ModLoader(_modsDirectory);
            var loaded = _modLoader.DiscoverAndLoad();

            // Events → Storyteller (additive).
            if (Storyteller != null && FinanceManager != null)
            {
                foreach (var evt in _modLoader.BuildEventDefinitions(FinanceManager))
                    Storyteller.RegisterEvent(evt);
            }

            // Research nodes → ResearchSystem (core wins on ID collision).
            Research?.AddModNodes(_modLoader.GetAllResearchNodes());

            // Scenarios → ScenarioSystem (core scenarios are never overwritten).
            Scenario?.RegisterModScenarios(_modLoader.GetAllScenarios());

            _modsAppliedForState = State;

            SimLog.Info(
                $"🧩 Mods applied from {_modsDirectory}: {loaded.Count} mod(s), " +
                $"{_modLoader.LoadErrors.Count} error(s)");
        }
        catch (Exception ex)
        {
            SimLog.Warn($"Mods: failed to apply from {_modsDirectory}: {ex.Message}");
        }
    }
}
