# Sprint 13 File Ownership

## T83 — Decision Engine + Daily Pulse (Sonnet)
**OWNS:**
- `godot/scripts/simulation/DecisionManager.cs` (NEW)
- `godot/scripts/ui/DecisionPanel.gd` (NEW)
- `godot/scripts/ui/MorningBriefing.gd` (NEW)
- `godot/scripts/ui/NextActionWidget.gd` (NEW)
- `godot/scripts/ui/ComingSoonPanel.gd` (NEW)

**READS (do not modify):**
- `godot/scripts/simulation/GameState.cs`
- `godot/scripts/simulation/SimulationCore.cs`
- `godot/scripts/ui/LakskongenTheme.gd`
- `godot/scripts/ui/MasterUI.gd`

## T84 — Crisis Response System (Kimi)
**OWNS:**
- `godot/scripts/simulation/CrisisResponseManager.cs` (NEW)
- `godot/scripts/ui/CrisisPanel.gd` (NEW)
- `godot/scripts/ui/RecoveryDashboard.gd` (NEW)
- `godot/scripts/ui/FjordTensionMeter.gd` (NEW)

**READS (do not modify):**
- `godot/scripts/simulation/EpidemiologyEngine.cs`
- `godot/scripts/simulation/EnvironmentEngine.cs`
- `godot/scripts/simulation/GameState.cs`
- `godot/scripts/ui/LakskongenTheme.gd`

## T85 — Production Goals + Visual Feedback (DeepSeek)
**OWNS:**
- `godot/scripts/simulation/BottleneckAnalyzer.cs` (NEW)
- `godot/scripts/simulation/ProductionGoal.cs` (NEW)
- `godot/scripts/ui/ProductionChainPanel.gd` (NEW)
- `godot/scripts/ui/MilestoneTracker.gd` (NEW)

**READS (do not modify):**
- `godot/scripts/simulation/ProcessingFacility.cs`
- `godot/scripts/simulation/SupplyChain.cs`
- `godot/scripts/simulation/GameState.cs`
- `godot/scripts/ui/LakskongenTheme.gd`

## Shared Rules
- DO NOT modify files you don't own
- If you need info from another builder's domain, write to `shared/collab/questions/<your-task>.md`
- Use `LakskongenTheme` constants for ALL UI styling (NEW "Nordlys" palette)
- All C# classes must integrate with GameState signals
- All GDScript UI must use the theme helper functions
