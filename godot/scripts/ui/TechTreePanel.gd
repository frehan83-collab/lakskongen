## TechTreePanel.gd
## Visual tech tree (25 nodes, 5 branches in the shipped tree).
## Builds its layout procedurally - no external scene file required.
##
## Layout: one column per branch present in the data (any of the ten branch
## names), one band per tier. Nodes sharing a branch and tier stack inside the
## band, so none overlap. Nodes are PanelContainers sized NODE_W × NODE_H;
## connection lines are drawn on a canvas overlay behind the buttons.
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
const CELL_GAP   := 10    # gap between nodes stacked in one branch/tier cell
const START_X    := 20
const START_Y    := 90    # space for branch headers and title bar

# ── Branch metadata ─────────────────────────────────────────────────────────────

## Column order for every ResearchBranch value (review F1004: the panel knew
## five and silently dropped nodes on the other five). Only branches that
## have nodes get a column; an unknown name still gets one at the end.
const BRANCH_ORDER: Array = ["Biology", "Genetics", "Nutrition", "Health", "Technology",
	"Automation", "Sustainability", "Environment", "Processing", "Expansion"]

const BRANCH_META: Dictionary = {
	"Biology":       { "label": "Biologi",        "icon": "🧬", "color": Color("#2E6B45") },
	"Genetics":      { "label": "Genetikk",       "icon": "🧪", "color": Color("#2E6B45") },
	"Nutrition":     { "label": "Ernæring",       "icon": "🌾", "color": Color("#8A5A00") },
	"Health":        { "label": "Helse",          "icon": "🩺", "color": Color("#B3172F") },
	"Technology":    { "label": "Teknologi",       "icon": "⚙️", "color": Color("#1F5E73") },
	"Automation":    { "label": "Automasjon",     "icon": "🤖", "color": Color("#1F5E73") },
	"Sustainability":{ "label": "Bærekraft",       "icon": "🌿", "color": Color("#2E6B45") },
	"Environment":   { "label": "Miljø",          "icon": "🌍", "color": Color("#2E6B45") },
	"Processing":    { "label": "Foredling",       "icon": "🐟", "color": Color("#8A5A00") },
	"Expansion":     { "label": "Ekspansjon",      "icon": "🌊", "color": Color("#1F5E73") },
}
const DEFAULT_BRANCH_META: Dictionary = { "label": "", "icon": "🔬", "color": Color("#1F5E73") }

## Effect labels (ResearchEffectKeys.Live). Reductions show as −N %, the
## sustainability score as points (it used to print "+800%").
const EFFECT_LABELS: Dictionary = {
	"growth_rate_bonus": "Vekst",
	"fcr_reduction": "Fôrfaktor (FCR)",
	"feed_waste_reduction": "Fôrsvinn",
	"mortality_reduction": "Dødelighet",
	"disease_resistance_bonus": "Sykdomsresistens",
	"market_price_bonus": "Pris",
	"sustainability_score_bonus": "Bærekraftscore",
	"lice_reduction": "Lusepåslag",
	"carbon_reduction": "CO₂ fra energi",
}
const POINT_EFFECTS: Array = ["sustainability_score_bonus"]
const REDUCTION_EFFECTS: Array = ["fcr_reduction", "feed_waste_reduction", "mortality_reduction",
	"lice_reduction", "carbon_reduction"]


# ── Runtime state ───────────────────────────────────────────────────────────────

# Map: nodeId → Rect2 centre point (for drawing connection lines)
var _node_centres: Dictionary = {}
# Map: nodeId → node data dict (from GameBridge)
var _node_data: Dictionary    = {}
# Map: nodeId → the PanelContainer widget
var _node_panels: Dictionary  = {}
# Map: nodeId → top-left position from the data-driven layout
var _node_pos: Dictionary     = {}
# Branch columns currently laid out (headers are rebuilt when this changes)
var _branches: Array          = []
var _header_labels: Array     = []
# Node-state signature of the last build; unchanged data skips the rebuild (F672)
var _built_signature: String  = ""
var _close_btn: Button = null

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

# ── Skeleton build (runs once) ──────────────────────────────────────────────────

## Branch accent colour -> theme tone (labels resolve per surface).
static func _tone(accent: Color) -> StringName:
	var tone: StringName = LakskongenTheme.tone_for_color(accent)
	return tone if tone != &"?" else &"Info"

func _build_skeleton() -> void:
	var total_w := START_X * 2 + 5 * H_GAP
	var total_h := START_Y + 5 * V_GAP + NODE_H + 50
	custom_minimum_size = Vector2(total_w, total_h)

	# ── Background panel ──
	var bg := ColorRect.new()
	bg.color = Color(LakskongenTheme.CHART, 0.97)  # Chart paper (document surface)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# ── Title bar ──
	var title := Label.new()
	title.text = tr("UI_TECH_TITLE")
	title.theme_type_variation = &"Subheading"
	title.position = Vector2(START_X, 10)
	title.tooltip_text = tr("UI_TECH_TITLE_TOOLTIP")
	add_child(title)

	# ── Close button ──
	_close_btn = Button.new()
	_close_btn.text = "×"
	_close_btn.theme_type_variation = &"LargeGhostButton"
	_close_btn.position = Vector2(total_w - 40, 8)
	_close_btn.size = Vector2(28, 28)
	_close_btn.tooltip_text = tr("UI_TECH_CLOSE_TOOLTIP")
	_close_btn.pressed.connect(func(): emit_signal("panel_close_requested"))
	add_child(_close_btn)

	# ── Research status bar ──
	var status_lbl := Label.new()
	status_lbl.name = "StatusLabel"
	status_lbl.text = tr("UI_TECH_NO_RESEARCH")
	status_lbl.theme_type_variation = &"SmallMuted"
	status_lbl.position = Vector2(START_X, 36)
	status_lbl.tooltip_text = tr("UI_TECH_STATUS_TOOLTIP")
	add_child(status_lbl)

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
	LakskongenTheme.set_size(_tooltip_label, 10)
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

	# Rebuild panels only when a node's state changed (F672: the tree used to
	# be destroyed and rebuilt every state update and every ~2 s while hidden).
	var signature := _state_signature()
	if signature != _built_signature:
		_layout_nodes()
		_build_node_panels()
		_built_signature = signature

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

## Everything a node panel shows, so an unchanged tree is not rebuilt.
func _state_signature() -> String:
	var ids: Array = _node_data.keys()
	ids.sort()
	var parts: PackedStringArray = []
	for id in ids:
		var nd: Dictionary = _node_data[id]
		parts.append("%s|%s|%s|%s|%s|%d" % [id, nd["branch"], nd["unlocked"], nd["is_active"],
			nd["can_research"], int(nd["progress_days"])])
	return ",".join(parts)

## One column per branch present (BRANCH_ORDER first), one band per tier;
## nodes sharing a branch and tier stack inside the band.
func _layout_nodes() -> void:
	var cells: Dictionary = {}   # branch -> tier -> [ids]
	for nd in _node_data.values():
		var branch: String = str(nd["branch"])
		var tier: int = int(nd["tier"])
		if not cells.has(branch):
			cells[branch] = {}
		if not cells[branch].has(tier):
			cells[branch][tier] = []
		cells[branch][tier].append(str(nd["id"]))

	var branches: Array = []
	for b in BRANCH_ORDER:
		if cells.has(b):
			branches.append(b)
	for b in cells.keys():
		if b not in branches:
			branches.append(b)

	var tiers: Array = []
	for b in branches:
		for t in cells[b].keys():
			if t not in tiers:
				tiers.append(t)
	tiers.sort()

	var band_y: Dictionary = {}
	var y := START_Y
	for t in tiers:
		band_y[t] = y
		var stacked := 1
		for b in branches:
			if cells[b].has(t):
				stacked = maxi(stacked, cells[b][t].size())
		y += stacked * (NODE_H + CELL_GAP) - CELL_GAP + (V_GAP - NODE_H)

	_node_pos.clear()
	for bi in branches.size():
		var x := START_X + bi * H_GAP
		for t in cells[branches[bi]].keys():
			var ids: Array = cells[branches[bi]][t]
			ids.sort()
			for i in ids.size():
				_node_pos[ids[i]] = Vector2(x, band_y[t] + i * (NODE_H + CELL_GAP))

	var total_w: int = START_X * 2 + maxi(branches.size(), 1) * H_GAP
	custom_minimum_size = Vector2(total_w, y + 50)
	if _close_btn != null:
		_close_btn.position = Vector2(total_w - 40, 8)
	if branches != _branches:
		_branches = branches
		_rebuild_headers()

func _rebuild_headers() -> void:
	for hdr in _header_labels:
		hdr.queue_free()
	_header_labels.clear()
	for bi in _branches.size():
		var branch: String = _branches[bi]
		var meta: Dictionary = BRANCH_META.get(branch, DEFAULT_BRANCH_META)
		var hdr := Label.new()
		var label: String = meta["label"] if meta["label"] != "" else branch
		hdr.text = _fmt("UI_TECH_TIER_NODE", "%s %s", [meta["icon"], label])
		hdr.theme_type_variation = StringName("Caption" + String(_tone(meta["color"])))
		hdr.position = Vector2(START_X + bi * H_GAP, 58)
		hdr.custom_minimum_size = Vector2(NODE_W, 24)
		add_child(hdr)
		_header_labels.append(hdr)

## Tooltip text for one effect: fractions as ±N %, score effects as points.
static func format_effect(key: String, value: float) -> String:
	var label: String = EFFECT_LABELS.get(key, key.replace("_", " ").capitalize())
	if key in POINT_EFFECTS:
		return "%s +%d" % [label, int(round(value))]
	var prefix: String = "−" if key in REDUCTION_EFFECTS else "+"
	return "%s %s%d %%" % [label, prefix, int(round(value * 100.0))]

func _build_node_panels() -> void:
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
	var pos: Vector2   = _node_pos.get(nd["id"], Vector2(START_X, START_Y + (tier - 1) * V_GAP))
	var x := pos.x
	var y := pos.y

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

	# Card by research state (theme variations, resolved on the Chart surface).
	var branch_color: Color = BRANCH_META.get(branch, DEFAULT_BRANCH_META)["color"]
	if is_unlocked:
		panel.theme_type_variation = &"CalloutOk"
	elif is_active:
		panel.theme_type_variation = &"CalloutWarn"
	elif can_research:
		panel.theme_type_variation = &"CalloutInfo"
	else:
		panel.theme_type_variation = &"Tile"
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
	status_icon.theme_type_variation = &"Small"
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
	name_lbl.theme_type_variation = &"Caption" if (is_unlocked or is_active or can_research) else &"CaptionMuted"
	name_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(name_lbl)

	# Tier badge
	var tier_lbl := Label.new()
	tier_lbl.text = _fmt("UI_TECH_TIER_LABEL", "Nivå %s", [tier])
	tier_lbl.theme_type_variation = StringName("Small" + String(_tone(branch_color)))
	vbox.add_child(tier_lbl)

	# Cost / progress row
	if is_unlocked:
		var done_lbl := Label.new()
		done_lbl.text = tr("UI_TECH_COMPLETED")
		done_lbl.theme_type_variation = &"SmallOk"
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
		prog_lbl.theme_type_variation = &"SmallWarn"
		vbox.add_child(prog_lbl)
	else:
		var cost_str: String = _format_nok(nd["cost_nok"])
		var cost_lbl := Label.new()
		cost_lbl.text = "%s  •  %dd" % [cost_str, research_days]
		cost_lbl.theme_type_variation = &"SmallMuted"
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
			fx_parts.append(format_effect(k, float(effects[k])))
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
					line_color = Color(get_theme_color(&"ok", &"Palette"), 0.75)  # fully connected
				elif prereq_unlocked:
					line_color = Color(get_theme_color(&"info", &"Palette"), 0.6)  # prerequisite done
				else:
					line_color = get_theme_color(&"stroke", &"Palette")  # locked chain

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
