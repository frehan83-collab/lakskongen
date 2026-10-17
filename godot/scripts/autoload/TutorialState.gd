extends Node

## TutorialState — progressive disclosure for the first season (roadmap U2-02).
##
## A read-only mirror of the C# TutorialSystem (through /root/SimBridge) that
## the tab rail asks which tabs are open. Only Første sesong runs the tutorial
## (owner decision D19): anywhere else, and once the season is done or
## skipped, every tab is open. In Første sesong a tab named in a step's
## "unlocks" (godot/data/tutorial_steps.json) opens when that step is reached,
## so the step that teaches a tab is the one that opens it.
##
## Everything is null-guarded: without SimBridge every tab reports open, so a
## missing autoload can never soft-lock the UI.

## Emitted when the mirrored tutorial step changes (or the tutorial starts or ends).
signal phase_changed(phase: int)

## Emitted once per gated tab when it opens.
signal panel_unlocked(panel_id: String)

## Current 1-based tutorial step number (0 when no tutorial runs).
var phase: int = 0

## True once the tutorial is finished or skipped, or when none runs.
var is_complete: bool = true

## True while the tutorial runs (Første sesong, neither finished nor skipped).
var is_active: bool = false

var _sim: Node = null
var _unlock_step: Dictionary = {}   # tab id -> step number that opens it
var _steps_total: int = -1
var _emitted_unlocks: Dictionary = {}

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_refresh)
	_refresh()

func _process(_delta: float) -> void:
	# Cheap poll of SimState's mirror (no C# call): keeps the rail right even
	# when no state_updated is emitted (menu state, a loaded game).
	_refresh()

# ── Public API ────────────────────────────────────────────────────────────────

## True when the tab may be opened.
func is_panel_unlocked(panel_id: String) -> bool:
	if not is_active:
		return true
	if not _unlock_step.has(panel_id):
		return true
	return phase >= int(_unlock_step[panel_id])

## The step that opens a gated tab (0 when the tab is never gated).
func unlock_step(panel_id: String) -> int:
	return int(_unlock_step.get(panel_id, 0))

# ── Internal ──────────────────────────────────────────────────────────────────

func _refresh() -> void:
	var active := bool(_read("tutorial_active", false))
	var new_phase := int(_read("tutorial_step_number", 0)) if active else 0
	var total := int(_read("tutorial_total_steps", 0))
	if total != _steps_total:
		_steps_total = total
		_load_unlocks()
	var changed := active != is_active or new_phase != phase
	is_active = active
	is_complete = not active
	phase = new_phase
	if changed:
		phase_changed.emit(phase)
	_emit_new_unlocks()

## The tabs each step opens, from the bridge's step list.
func _load_unlocks() -> void:
	_unlock_step.clear()
	if _sim == null or not _sim.has_method("get_tutorial_steps"):
		return
	for step in _sim.get_tutorial_steps():
		for tab in step.get("unlocks", []):
			_unlock_step[str(tab)] = int(step.get("number", 0))

func _emit_new_unlocks() -> void:
	for panel_id: String in _unlock_step.keys():
		var unlocked: bool = is_panel_unlocked(panel_id)
		if unlocked and not bool(_emitted_unlocks.get(panel_id, false)):
			_emitted_unlocks[panel_id] = true
			panel_unlocked.emit(panel_id)
		elif not unlocked:
			_emitted_unlocks.erase(panel_id)

## A SimBridge property, tolerating a missing node or property.
func _read(property_name: String, fallback: Variant) -> Variant:
	if _sim == null:
		return fallback
	var value: Variant = _sim.get(property_name)
	return value if value != null else fallback
