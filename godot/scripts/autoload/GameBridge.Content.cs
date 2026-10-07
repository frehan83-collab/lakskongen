using Godot;

/// <summary>
/// Wave 7 content pass: additive read-only GameBridge extensions used by the
/// Morning Briefing "Depth" line. Kept in a separate partial file so the main
/// GameBridge.cs hub is left untouched.
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// P15: accumulated player-supply pressure on the spot price in [0, 1].
    /// 0 = no market impact, 1 = maximum impact (-8% price cap). Returns 0 when
    /// the simulation engine has not been initialised.
    /// </summary>
    public double GetPlayerSupplyPressure()
        => SimulationEngine?.MarketSimulation?.PlayerSupplyPressure ?? 0.0;
}
