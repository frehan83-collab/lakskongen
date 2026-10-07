## TechTreePanel.gd
## Visual tech tree with 25 nodes across 5 branches.
## Builds its layout procedurally - no external scene file required.
##
## Layout: 5 branch columns, 5 tier rows. Nodes are PanelContainers
## sized at NODE_W × NODE_H. Connection lines are drawn on a canvas
## overlay behind the buttons.
##
## Node status colours:
##   LOCKED      - dark grey, no interaction
##   AVAILABLE   - ocean blue, clickable
##   RESEARCHING - warm amber, shows progress bar
##   UNLOCKED    - emerald green
##
## Signals this panel emits:
##   research_started(node_id: String)
##   panel_close_requested

extends Control

# ── Signals ────────────────────────────────────────────────────────────────────

signal research_started(node_id: String)
signal panel_close_requested

# ── Layout constants ────────────────────────────────────────────────────────────

const NODE_W     := 175
const NODE_H     := 90
const H_GAP      := 200   # horizontal spacing between branch columns
const V_GAP      := 115   # vertical spacing between tier rows
const START_X    := 20
const START_Y    := 90    # space for branch headers and title bar

# ── Branch metadata ─────────────────────────────────────────────────────────────

const BRANCH_ORDER: Array = ["Biology", "Technology", "Sustainability", "Processing", "Expansion"]

const BRANCH_META: Dictionary = {
	"Biology":       { "label": "Biologi",        "icon": "🧬", "color": Color("#A3BE8C") },
	"Technology":    { "label": "Teknologi",       "icon": "⚙️", "color": Color("#5E81AC") },
	"Sustainability":{ "label": "Bærekraft",       "icon": "🌿", "color": Color("#A3BE8C") },
	"Processing":    { "label": "Foredling",       "icon": "🐟", "color": Color("#EBCB8B") },
	"Expansion":     { "label": "Ekspansjon",      "icon": "🌊", "color": Color("#5E81AC") },
}

# Node state colours - Nordic Minimalist v2.0
const COLOR_LOCKED      := Color("#D8DEE9")   # Light gray (border)
const COLOR_AVAILABLE   := Color("#5E81AC")   # Accent blue
const COLOR_RESEARCHING := Color("#EBCB8B")   # Warning amber
const COLOR_UNLOCKED    := Color("#A3BE8C")   # Success green
const COLOR_TEXT_DIM    := Color("#7B8794")   # Muted
const COLOR_TEXT_BRIGHT := Color("#2E3440")   # Dark text

# ── Runtime state ───────────────────────────────────────────────────────────────

# Map: nodeId → Rect2 centre point (for drawing connection lines)
var _node_centres: Dictionary = {}
# Map: nodeId → node data dict (from GameBridge)
var _node_data: Dictionary    = {}
# Map: nodeId → the PanelContainer widget
var _node_panels: Dictionary  = {}

# Confirmation dialog target
var _pending_node_id: String = ""

# Refs
var _line_canvas: _LineCanvas = null
var _confirm_dialog: AcceptDialog = null
var _tooltip_popup: PopupPanel = null
var _tooltip_label: Label = null
var _sim: Node = null

# ── Lifecycle ───────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
		print("TechTreePanel: connected to SimBridge")
	else:
		push_warning("TechTreePanel: SimBridge not found")
	_build_skeleton()
	_refresh()

func on_panel_shown() -> void:
	_refresh()

func _on_state_updated() -> void:
	if visible:
		_refresh()

## Resolve the SimBridge facade (never touch GameBridge.cs directly).
func _bridge() -> Node:
	if _sim == null or not is_instance_valid(_sim):
		_sim = get_node_or_null("/root/SimBridge")
	return _sim

## Safe translated format: falls back to a local format string when the
## translation catalogue is unavailable (tr returns the bare key).
func _fmt(key: String, fallback: String, args: Array) -> String:
	var t := tr(key)
	if "%" not in t:
		t = fallback
	return t % args

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _process(_delta: float) -> void:
	# Light polling - refreshes every ~2 s (120 frames at 60 fps)
	if Engine.get_process_frames() % 120 == 0:
		_refresh()

# ── Skeleton build (runs once) ──────────────────────────────────────────────────

func _build_skeleton() -> void:
	var total_w := START_X * 2 + BRANCH_ORDER.size() * H_GAP
	var total_h := START_Y + 5 * V_GAP + NODE_H + 50
	custom_minimum_size = Vector2(total_w, total_h)

	# ── Background panel ──
	var bg := ColorRect.new()
	bg.color = Color(0.92549, 0.937255, 0.956863, 0.82)  # Nordic Minimalist light bg, lets 3D vista show through
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# ── Title bar ──
	var title := Label.new()
	title.text = tr("UI_TECH_TITLE")
	title.add_theme_font_size_override("font_size", 16)
	title.modulate = Color("#2E3440")  # Dark text
	title.position = Vector2(START_X, 10)
	title.tooltip_text = tr("UI_TECH_TITLE_TOOLTIP")
	add_child(title)

	# ── Close button ──
	var close_btn := Button.new()
	close_btn.text = tr("✕")
	close_btn.position = Vector2(total_w - 40, 8)
	close_btn.size = Vector2(28, 28)
	close_btn.tooltip_text = tr("UI_TECH_CLOSE_TOOLTIP")
	close_btn.pressed.connect(func(): emit_signal("panel_close_requested"))
	add_child(close_btn)

	# ── Research status bar ──
	var status_lbl := Label.new()
	status_lbl.name = "StatusLabel"
	status_lbl.text = tr("UI_TECH_NO_RESEARCH")
	status_lbl.add_theme_font_size_override("font_size", 11)
	status_lbl.modulate = Color("#4C566A")  # Secondary text
	status_lbl.position = Vector2(START_X, 36)
	status_lbl.tooltip_text = tr("UI_TECH_STATUS_TOOLTIP")
	add_child(status_lbl)

	# ── Branch headers ──
	for bi in BRANCH_ORDER.size():
		var branch: String = BRANCH_ORDER[bi]
		var meta: Dictionary = BRANCH_META[branch]
		var x := START_X + bi * H_GAP
		var hdr := Label.new()
		hdr.text = _fmt("UI_TECH_TIER_NODE", "%s %s", [meta["icon"], meta["label"]])
		hdr.add_theme_font_size_override("font_size", 12)
		hdr.modulate = meta["color"]
		hdr.position = Vector2(x, 58)
		hdr.custom_minimum_size = Vector2(NODE_W, 24)
		add_child(hdr)

	# ── Line canvas (drawn BEHIND node panels) ──
	_line_canvas = _LineCanvas.new()
	_line_canvas.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_line_canvas.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_line_canvas.panel_ref = self
	add_child(_line_canvas)

	# ── Confirmation dialog ──
	_confirm_dialog = AcceptDialog.new()
	_confirm_dialog.title = "Start forskning?"
	_confirm_dialog.confirmed.connect(_on_research_confirmed)
	add_child(_confirm_dialog)

	# ── Tooltip popup ──
	_tooltip_popup = PopupPanel.new()
	_tooltip_popup.size = Vector2(280, 120)
	_tooltip_label = Label.new()
	_tooltip_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_tooltip_label.add_theme_font_size_override("font_size", 10)
	_tooltip_popup.add_child(_tooltip_label)
	add_child(_tooltip_popup)

# ── Data refresh ────────────────────────────────────────────────────────────────

func _refresh() -> void:
	var gb := _bridge()
	if gb == null:
		_update_status_label("SimBridge ikke tilgjengelig")
		return

	var nodes_raw: Array = Array(gb.get_research_nodes())

	# Rebuild lookup maps
	_node_data.clear()
	for nd in nodes_raw:
		_node_data[nd["id"]] = nd

	# Build or update node panels
	_build_node_panels()

	# Update status bar
	var active_id: String = gb.get_active_research_id()
	if active_id != "":
		var remaining: int = gb.get_research_days_remaining()
		var nd: Dictionary = _node_data.get(active_id, {})
		_update_status_label("Forsker: %s - %d dager gjenstår" % [nd.get("name", active_id), remaining])
	else:
		_update_status_label("Ingen aktiv forskning  |  Velg en node for å starte")

	# Redraw connection lines
	_line_canvas.queue_redraw()

# ── Node panel builder ──────────────────────────────────────────────────────────

func _build_node_panels() -> void:
	# Calculate tier-index per branch
	var branch_tier_count: Dictionary = {}
	for nd_dict in _node_data.values():
		var branch: String = nd_dict["branch"]
		if branch not in branch_tier_count:
			branch_tier_count[branch] = {}
		var tier: int = nd_dict["tier"]
		if tier not in branch_tier_count[branch]:
			branch_tier_count[branch][tier] = []
		branch_tier_count[branch][tier].append(nd_dict["id"])

	# Remove old panels
	for old_panel in _node_panels.values():
		old_panel.queue_free()
	_node_panels.clear()
	_node_centres.clear()

	for nd in _node_data.values():
		_build_node_panel(nd)

func _build_node_panel(nd: Dictionary) -> void:
	var branch: String = nd["branch"]
	var tier: int      = nd["tier"]
	var bi: int        = BRANCH_ORDER.find(branch)
	if bi < 0:
		return

	var x := START_X + bi * H_GAP
	var y := START_Y + (tier - 1) * V_GAP

	# Store centre for line drawing
	_node_centres[nd["id"]] = Vector2(x + NODE_W * 0.5, y + NODE_H * 0.5)

	# Determine status
	var is_unlocked:    bool = nd["unlocked"]
	var is_active:      bool = nd["is_active"]
	var can_research:   bool = nd["can_research"]
	var progress_days:  int  = nd["progress_days"]
	var research_days:  int  = nd["research_days"]

	# Panel container
	var panel := PanelContainer.new()
	panel.position = Vector2(x, y)
	panel.size = Vector2(NODE_W, NODE_H)

	# Style
	var style := StyleBoxFlat.new()
	style.border_width_top    = 2
	style.border_width_bottom = 2
	style.border_width_left   = 2
	style.border_width_right  = 2
	style.corner_radius_top_left     = 5
	style.corner_radius_top_right    = 5
	style.corner_radius_bottom_left  = 5
	style.corner_radius_bottom_right = 5

	var branch_color: Color = BRANCH_META[branch]["color"]
	if is_unlocked:
		style.bg_color     = COLOR_UNLOCKED
		style.border_color = branch_color
	elif is_active:
		style.bg_color     = COLOR_RESEARCHING
		style.border_color = Color(1.0, 0.75, 0.20)
	elif can_research:
		style.bg_color     = COLOR_AVAILABLE
		style.border_color = branch_color.lightened(0.3)
	else:
		style.bg_color     = COLOR_LOCKED
		style.border_color = Color("#D8DEE9")  # Border

	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	_node_panels[nd["id"]] = panel

	# Inner layout
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 1)
	panel.add_child(vbox)

	# Title row
	var title_row := HBoxContainer.new()
	vbox.add_child(title_row)

	var status_icon := Label.new()
	status_icon.add_theme_font_size_override("font_size", 11)
	if is_unlocked:
		status_icon.text = tr("✅")
	elif is_active:
		status_icon.text = tr("⏳")
	elif can_research:
		status_icon.text = tr("🔬")
	else:
		status_icon.text = tr("🔒")
	title_row.add_child(status_icon)

	var name_lbl := Label.new()
	name_lbl.text = nd["name"]
	name_lbl.add_theme_font_size_override("font_size", 10)
	name_lbl.modulate = COLOR_TEXT_BRIGHT if (is_unlocked or is_active or can_research) else COLOR_TEXT_DIM
	name_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(name_lbl)

	# Tier badge
	var tier_lbl := Label.new()
	tier_lbl.text = _fmt("UI_TECH_TIER_LABEL", "Nivå %s", [tier])
	tier_lbl.add_theme_font_size_override("font_size", 9)
	tier_lbl.modulate = branch_color
	vbox.add_child(tier_lbl)

	# Cost / progress row
	if is_unlocked:
		var done_lbl := Label.new()
		done_lbl.text = tr("UI_TECH_COMPLETED")
		done_lbl.add_theme_font_size_override("font_size", 9)
		done_lbl.modulate = Color("#A3BE8C")  # Success
		vbox.add_child(done_lbl)
	elif is_active:
		# Progress bar
		var prog := ProgressBar.new()
		prog.value = float(progress_days) / float(max(research_days, 1)) * 100.0
		prog.custom_minimum_size = Vector2(NODE_W - 16, 10)
		prog.show_percentage = false
		vbox.add_child(prog)
		var prog_lbl := Label.new()
		prog_lbl.text = _fmt("UI_TECH_PROGRESS_DAYS", "%d av %d dager", [progress_days, research_days])
		prog_lbl.add_theme_font_size_override("font_size", 9)
		prog_lbl.modulate = Color(1.0, 0.80, 0.30)
		vbox.add_child(prog_lbl)
	else:
		var cost_str: String = _format_nok(nd["cost_nok"])
		var cost_lbl := Label.new()
		cost_lbl.text = "%s  •  %dd" % [cost_str, research_days]
		cost_lbl.add_theme_font_size_override("font_size", 9)
		cost_lbl.modulate = COLOR_TEXT_DIM
		vbox.add_child(cost_lbl)

	# Clickable hitbox (only for available / active nodes)
	if can_research or is_active:
		var btn := Button.new()
		btn.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		btn.flat = true
		btn.focus_mode = Control.FOCUS_NONE
		if can_research:
			btn.pressed.connect(_on_node_clicked.bind(nd["id"]))
			btn.mouse_entered.connect(_on_node_hover.bind(nd["id"]))
			btn.mouse_exited.connect(_on_node_hover_exit)
		elif is_active:
			btn.pressed.connect(_on_active_node_clicked.bind(nd["id"]))
		panel.add_child(btn)
	elif not is_unlocked:
		# Tooltip on locked nodes via hover
		var hover_btn := Button.new()
		hover_btn.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		hover_btn.flat = true
		hover_btn.focus_mode = Control.FOCUS_NONE
		hover_btn.mouse_entered.connect(_on_node_hover.bind(nd["id"]))
		hover_btn.mouse_exited.connect(_on_node_hover_exit)
		panel.add_child(hover_btn)

# ── Interaction handlers ────────────────────────────────────────────────────────

func _on_node_clicked(node_id: String) -> void:
	var nd: Dictionary = _node_data.get(node_id, {})
	if nd.is_empty():
		return

	_pending_node_id = node_id
	var cost_str: String = _format_nok(nd["cost_nok"])
	_confirm_dialog.dialog_text = "%s\n\nKostnad: %s\nForskningsvarighet: %d dager\n\n%s" % [
		nd["name"],
		cost_str,
		nd["research_days"],
		nd["description"]
	]
	_confirm_dialog.popup_centered()

func _on_active_node_clicked(_node_id: String) -> void:
	pass  # Could show a cancel dialog in future

func _on_research_confirmed() -> void:
	if _pending_node_id == "":
		return
	var gb := _bridge()
	if gb == null:
		return
	var success: bool = gb.start_research(_pending_node_id)
	if success:
		emit_signal("research_started", _pending_node_id)
		_toast_research(_pending_node_id)
		_refresh()
	else:
		# Show brief error - insufficient funds or state changed
		var err := AcceptDialog.new()
		err.title = "Kan ikke starte forskning"
		err.dialog_text = "Ikke nok midler eller et krav er ikke oppfylt."
		add_child(err)
		err.popup_centered()
		err.confirmed.connect(func(): err.queue_free())
	_pending_node_id = ""

func _on_node_hover(node_id: String) -> void:
	var nd: Dictionary = _node_data.get(node_id, {})
	if nd.is_empty():
		return

	var prereq_names: Array = []
	for pid in nd["prerequisites"]:
		var prereq: Dictionary = _node_data.get(pid, {})
		prereq_names.append(prereq.get("name", pid))

	var lines: PackedStringArray = [nd["description"]]
	if prereq_names.size() > 0:
		lines.append("Krever: " + ", ".join(prereq_names))

	var effects: Dictionary = nd["effects"]
	if not effects.is_empty():
		var fx_parts: PackedStringArray = []
		for k in effects:
			fx_parts.append("%s: +%.0f%%" % [k.replace("_", " ").capitalize(), effects[k] * 100])
		lines.append("Effekter: " + ", ".join(fx_parts))

	_tooltip_label.text = "\n".join(lines)
	_tooltip_label.custom_minimum_size = Vector2(260, 0)
	_tooltip_popup.popup(Rect2(get_global_mouse_position() + Vector2(12, 8), Vector2(280, 1)))

func _on_node_hover_exit() -> void:
	_tooltip_popup.hide()

# ── Helpers ────────────────────────────────────────────────────────────────────

## Juice-only toast on research start (signal + sim call above untouched).
func _toast_research(node_id: String) -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		var nd: Dictionary = _node_data.get(node_id, {})
		var label := str(nd.get("name", node_id))
		var t := tr("UI_TOAST_RESEARCH")
		var msg := (t % label) if t != "UI_TOAST_RESEARCH" else ("🔬 Forskning startet: %s" % label)
		mgr.show_toast(msg, "SUCCESS", 4.0)

func _update_status_label(text: String) -> void:
	var lbl: Label = get_node_or_null("StatusLabel")
	if lbl:
		lbl.text = text

func _format_nok(value: float) -> String:
	if value >= 1_000_000.0:
		return "%.1f M NOK" % (value / 1_000_000.0)
	if value >= 1_000.0:
		return "%.0f k NOK" % (value / 1_000.0)
	return "%.0f NOK" % value

# ── Inner class: connection line canvas ────────────────────────────────────────

class _LineCanvas extends Control:
	## Draws prerequisite → dependent connection lines behind all node panels.

	var panel_ref: Control = null

	func _draw() -> void:
		if panel_ref == null:
			return

		var centres: Dictionary = panel_ref._node_centres
		var node_data: Dictionary = panel_ref._node_data

		for nd in node_data.values():
			var target_id: String = nd["id"]
			if not centres.has(target_id):
				continue

			var target_pt: Vector2 = centres[target_id]
			var is_unlocked: bool = nd["unlocked"]

			for prereq_id in nd["prerequisites"]:
				if not centres.has(prereq_id):
					continue

				var src_pt: Vector2 = centres[prereq_id]
				var prereq_nd: Dictionary = node_data.get(prereq_id, {})
				var prereq_unlocked: bool = prereq_nd.get("unlocked", false)

				var line_color: Color
				if is_unlocked and prereq_unlocked:
					line_color = Color(0.64, 0.74, 0.55, 0.75)  # success green - fully connected
				elif prereq_unlocked:
					line_color = Color(0.37, 0.51, 0.67, 0.60)  # accent blue - prereq done
				else:
					line_color = Color(0.85, 0.87, 0.91, 0.40)  # border gray - locked chain

				# Draw the connector with an elbow: src → mid-x → target
				var mid_y := (src_pt.y + target_pt.y) * 0.5
				var mid1 := Vector2(src_pt.x, mid_y)
				var mid2 := Vector2(target_pt.x, mid_y)
				draw_line(src_pt,  mid1, line_color, 1.5)
				draw_line(mid1,    mid2, line_color, 1.5)
				draw_line(mid2, target_pt, line_color, 1.5)

				# Arrow head at target
				var dir := (target_pt - mid2).normalized()
				var perp := Vector2(-dir.y, dir.x) * 4.0
				var arrow_tip := target_pt - dir * 8.0
				draw_line(arrow_tip, target_pt, line_color, 1.5)
				draw_line(arrow_tip + perp, target_pt, line_color, 1.0)
				draw_line(arrow_tip - perp, target_pt, line_color, 1.0)
