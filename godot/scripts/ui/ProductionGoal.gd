class_name ProductionGoal
extends RefCounted
## Minimal milestone-goal contract consumed by MilestoneTracker UI.
## STUB: satisfies parsing and basic runtime wiring until the real
## (C#-planned) ProductionGoal lands — then DELETE this file to avoid
## a global-class name collision.

signal MilestoneReached(milestone_name: String, milestone_value: float)
signal GoalCompleted

var Description: String = ""
var GoalId: String = ""

func GetProgressPercent() -> float:
	return 0.0
