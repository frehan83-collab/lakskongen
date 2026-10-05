class_name ProductionGoal
extends RefCounted
## Goal contract consumed by MilestoneTracker.add_goal().
## Visual milestones come from the sim via GameBridge
## (GetAchievedMilestones / GetNextVisualMilestone); this class is for
## explicitly added goals (scenarios, tutorial chains).

signal MilestoneReached(milestone_name: String, milestone_value: float)
signal GoalCompleted

var Description: String = ""
var GoalId: String = ""

func GetProgressPercent() -> float:
	return 0.0
