extends Node

## TutorialManager — DEPRECATED (retired during onboarding unification).
##
## The C# `TutorialSystem` is now the single source of truth for onboarding.
## It is surfaced to the UI by `scripts/ui/TutorialUI.gd` through `SimBridge`
## (`tutorial_step_number`, `tutorial_is_complete`, `tutorial_step_completed`).
##
## This autoload remains registered in project.godot only for backwards
## compatibility with any straggling callers: it logs once and then does
## nothing. Do NOT add progression logic here — extend the C# step track
## (or godot/data/tutorial_steps.json, which the C# system loads) instead.

# Kept so legacy signal connections (e.g. TutorialOverlay) do not error.
signal step_changed(step_id: int)
signal step_completed(step_id: int)
signal tutorial_started()
signal tutorial_completed()
signal tutorial_skipped()
signal tutorial_paused()
signal tutorial_resumed()

# Kept as inert public fields for compatibility.
var current_state: int = 0
var current_step: int = 0
var completed_steps: Array[int] = []
var step_definitions: Array = []
var tutorial_enabled: bool = true
var can_skip: bool = true

var _logged_once: bool = false

func _ready() -> void:
	_log_deprecated()

func _log_deprecated() -> void:
	if _logged_once:
		return
	_logged_once = true
	print("TutorialManager: DEPRECATED — onboarding is driven by the C# TutorialSystem via SimBridge/TutorialUI. This autoload is a no-op.")

# ── Public API (no-ops) ───────────────────────────────────────────────────────

func start_tutorial() -> void: _log_deprecated()
func resume_tutorial() -> void: _log_deprecated()
func pause_tutorial() -> void: _log_deprecated()
func skip_tutorial() -> void: _log_deprecated()
func next_step() -> void: _log_deprecated()
func check_completion(_trigger_type: String, _data: Variant = null) -> bool: return false
func get_current_step() -> Dictionary: return {}
func is_active() -> bool: return false
func is_completed() -> bool: return false
func is_skipped() -> bool: return false
func get_progress_percent() -> float: return 0.0
func on_continue_clicked() -> void: _log_deprecated()
func on_skip_clicked() -> void: _log_deprecated()
func on_pen_clicked(_pen_id: int) -> void: _log_deprecated()
func on_action_completed(_action_type: String, _data: Variant = null) -> void: _log_deprecated()
