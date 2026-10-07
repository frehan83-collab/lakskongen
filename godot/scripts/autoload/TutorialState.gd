extends Node

## TutorialState — progressive-disclosure autoload.
##
## Single read-only view of onboarding progress for gating advanced UI panels.
## The C# TutorialSystem (exposed through /root/SimBridge) is the source of
## truth; this node only mirrors it and maps step numbers to panel unlocks.
##
## Register in project.godot [autoload] AFTER SimBridge:
##   TutorialState="*res://scripts/autoload/TutorialState.gd"
##
## Everything is null-guarded: if SimBridge (or its properties) is missing the
## node simply reports phase 0 / not complete and never crashes. Non-gated
## panels always report unlocked, so a missing autoload cannot soft-lock the UI.

## Emitted when the mirrored tutorial phase (step number) changes.
signal phase_changed(phase: int)

## Emitted once per advanced panel when it first becomes available.
signal panel_unlocked(panel_id: String)

## Advanced panels are locked until the tutorial reaches the mapped step number.
## All other panel ids are always unlocked.
const ADVANCED_PANELS: Dictionary = {
	"analytics": 8,
	"genetics": 11,
	"research": 12,
	"insurance": 13,
	"mitigation": 14,
	"carbon": 15,
	"sustainability": 15,
	"ras": 16,
	"offshore": 17,
}

## Current 1-based tutorial step number (0 when no simulation/bridge).
var phase: int = 0

## True once the C# tutorial reports completion — unlocks everything.
var is_complete: bool = false

var _sim: Node = null
var _has_data: bool = false
var _phase_seen: bool = false
var _emitted_unlocks: Dictionary = {}

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_refresh)
	_refresh()

func _process(_delta: float) -> void:
	# Cheap poll — keeps us correct even if state_updated is never emitted
	# (stub mode / menu state).
	_refresh()

# ── Public API ────────────────────────────────────────────────────────────────

## True when the panel is available to the player.
## Non-gated panels are always unlocked; gated panels unlock at their step.
func is_panel_unlocked(panel_id: String) -> bool:
	if not ADVANCED_PANELS.has(panel_id):
		return true
	if is_complete:
		return true
	if not _has_data:
		return true
	return phase >= int(ADVANCED_PANELS[panel_id])

# ── Internal ──────────────────────────────────────────────────────────────────

func _refresh() -> void:
	_has_data = _sim != null and ("tutorial_step_number" in _sim or _sim.has_method("GetTutorialStepNumber"))
	var new_phase: int = int(_read_sim_value("tutorial_step_number", ["GetTutorialStepNumber"], 0))
	var new_complete: bool = bool(_read_sim_value("tutorial_is_complete", ["GetTutorialIsComplete"], false))

	var changed: bool = (not _phase_seen) or new_phase != phase or new_complete != is_complete
	phase = new_phase
	is_complete = new_complete
	_phase_seen = true

	if changed:
		phase_changed.emit(phase)

	_emit_new_unlocks()

func _emit_new_unlocks() -> void:
	for panel_id: String in ADVANCED_PANELS.keys():
		var unlocked: bool = is_panel_unlocked(panel_id)
		if unlocked and not bool(_emitted_unlocks.get(panel_id, false)):
			_emitted_unlocks[panel_id] = true
			panel_unlocked.emit(panel_id)

## Read a value from SimBridge, tolerating a missing node/property/method.
func _read_sim_value(property_name: String, method_names: Array, fallback: Variant) -> Variant:
	if _sim == null:
		return fallback
	# Property check: Object.get() returns null when the property is absent.
	var value: Variant = _sim.get(property_name)
	if value != null:
		return value
	# Method fallback (for bridges that expose getters instead of properties).
	for method_name: String in method_names:
		if _sim.has_method(method_name):
			return _sim.call(method_name)
	return fallback
