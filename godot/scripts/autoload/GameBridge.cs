using Godot;

/// <summary>
/// Godot autoload anchor for the split <see cref="GameBridge"/> partial class.
///
/// <para>Godot resolves a C# script by the case-sensitive file basename matching
/// the class name, so the registered autoload path in <c>project.godot</c> must
/// stay <c>res://scripts/autoload/GameBridge.cs</c>. The implementation itself
/// lives in the cohesive partials — <c>GameBridge.Core.cs</c>,
/// <c>GameBridge.Farm.cs</c>, <c>GameBridge.Market.cs</c>,
/// <c>GameBridge.Depth.cs</c>, <c>GameBridge.Economy.cs</c> and
/// <c>GameBridge.Licence.cs</c> — plus <c>GameBridge.Content.cs</c>. This file
/// intentionally declares the class only; all members are contributed by the
/// partials.</para>
/// </summary>
public partial class GameBridge : Node
{
}
