using Godot;
using LakskongenSim.Core;
using LakskongenSim.Models;
using System;

/// <summary>
/// Singleton bridge between the simulation engine and Godot.
/// Manages GameState, processes simulation ticks, and emits Godot signals for UI updates.
/// </summary>
public partial class GameBridge : Node
{
    [Signal]
    public delegate void GameStateUpdatedEventHandler();
    
    [Signal]
    public delegate void SimulationTickedEventHandler(int day);
    
    [Signal]
    public delegate void GameStartedEventHandler();
    
    [Signal]
    public delegate void GameEndedEventHandler(string reason);

    public SimulationEngine SimulationEngine { get; private set; }
    public GameState GameState => SimulationEngine?.GameState;
    
    private Timer _simulationTimer;
    private float _timeSpeed = 1.0f;
    private bool _isPaused = false;

    public override void _Ready()
    {
        // Initialize simulation engine
        SimulationEngine = new SimulationEngine();
        
        // Set up simulation timer
        _simulationTimer = new Timer();
        _simulationTimer.WaitTime = 1.0f; // 1 second = 1 day by default
        _simulationTimer.Timeout += OnSimulationTick;
        AddChild(_simulationTimer);
        
        GD.Print("GameBridge initialized");
    }

    /// <summary>
    /// Start a new game simulation
    /// </summary>
    public void StartNewGame(string playerName, int startingCapital = 1000000)
    {
        SimulationEngine.StartNewGame(playerName, startingCapital);
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);
        
        if (!_isPaused)
        {
            _simulationTimer.Start();
        }
        
        GD.Print($"New game started for {playerName} with {startingCapital:C} starting capital");
    }

    /// <summary>
    /// Load an existing game
    /// </summary>
    public void LoadGame(GameState gameState)
    {
        SimulationEngine.LoadGame(gameState);
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);
        
        if (!_isPaused)
        {
            _simulationTimer.Start();
        }
        
        GD.Print($"Game loaded for day {gameState.CurrentDay}");
    }

    /// <summary>
    /// Set simulation time speed
    /// </summary>
    public void SetTimeSpeed(float speed)
    {
        _timeSpeed = speed;
        _simulationTimer.WaitTime = 1.0f / speed; // Faster speed = shorter timer
        GD.Print($"Time speed set to {speed}x");
    }

    /// <summary>
    /// Pause or unpause the simulation
    /// </summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        
        if (_isPaused)
        {
            _simulationTimer.Stop();
        }
        else if (GameState != null)
        {
            _simulationTimer.Start();
        }
        
        GD.Print($"Game {(_isPaused ? "paused" : "resumed")}");
    }

    /// <summary>
    /// Advance simulation by one day manually
    /// </summary>
    public void AdvanceDay()
    {
        if (SimulationEngine == null || GameState == null) return;
        
        ProcessSimulationTick();
    }

    private void OnSimulationTick()
    {
        ProcessSimulationTick();
    }

    private void ProcessSimulationTick()
    {
        if (SimulationEngine == null || GameState == null) return;

        try
        {
            SimulationEngine.ProcessDay();
            
            EmitSignal(SignalName.SimulationTicked, GameState.CurrentDay);
            EmitSignal(SignalName.GameStateUpdated);
            
            // Check for game over conditions
            if (GameState.Cash < -1000000) // 1M debt limit
            {
                EndGame("Bankruptcy - too much debt!");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error processing simulation tick: {ex.Message}");
            EndGame($"Simulation error: {ex.Message}");
        }
    }

    private void EndGame(string reason)
    {
        _simulationTimer.Stop();
        EmitSignal(SignalName.GameEnded, reason);
        GD.Print($"Game ended: {reason}");
    }

    public override void _ExitTree()
    {
        _simulationTimer?.Stop();
        _simulationTimer?.QueueFree();
    }
}