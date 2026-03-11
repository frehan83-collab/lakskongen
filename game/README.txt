Lakskongen Phase 1 prototype (Godot 4)

Structure:
- scenes/
- scripts/
- data/
- ui/

Open this folder as a Godot project (create a new project in Godot and point it to this folder). The scripts provide a minimal core loop: Hatchery -> Sea Phase -> Harvest -> Sell.

Notes:
- Placeholder logic and simple numeric models implemented in GDScript files under scripts/ and data/.
- No assets included; use Godot editor to create scenes and attach scripts if desired.

Files created by the subagent:
- scenes/Main.tscn (placeholder scene)
- scripts/GameManager.gd
- data/Fish.gd
- scripts/Genetics.gd
- scripts/Hatchery.gd
- scripts/SeaSite.gd
- scripts/Market.gd
- scripts/TimeSystem.gd
- scripts/FeedManager.gd
- scripts/Finance.gd
- ui/Dashboard.tscn
- ui/Dashboard.gd

How to run a simple test in Godot:
1. Create a new Godot project or open an existing one and add this folder as the project root.
2. Create a Node2D scene, attach scripts/GameManager.gd to it as the root node script, or load GameManager.gd from the editor and call start().
3. Use the Godot debugger output to see printed KPIs and cycle progression.

This is Phase 1 skeleton: core loop runs with simple models for growth, feed, temperature, market price, and finance. Further tuning and expansion needed for full gameplay.
