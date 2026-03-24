## CrewPanel v2.0 — Nordic Minimalist
## Crew roster: KPIs, employee cards with XP/moral/role,
## skill overview, payroll summary.
## Matches crew-prototype.html.

extends Control

signal worker_hired(role: String)
signal worker_fired(worker_id: String)

var _sim: Node = null
var _theme: Node = null

# UI refs
var _kpi_count: Label
var _kpi_payroll: Label
var _kpi_morale: Label
var _kpi_efficiency: Label
var _crew_list: VBoxContainer
var _skills_grid: GridContainer

# Colors
const C_BG       := Color("#ECEFF4")
const C_SURFACE  := Color("#FFFFFF")
const C_ELEVATED := Color("#F8F9FB")
const C_TEXT     := Color("#2E3440")
const C_TEXT2    := Color("#4C566A")
const C_MUTED    := Color("#7B8794")
const C_BORDER   := Color("#E5E9F0")
const C_PRIMARY  := Color("#0A4D68")
const C_SECONDARY := Color("#88C0D0")
const C_SUCCESS  := Color("#A3BE8C")
const C_SUCCESS_D := Color("#8FAD78")
const C_WARNING  := Color("#EBCB8B")
const C_WARNING_D := Color("#D4B06A")
const C_DANGER   := Color("#BF616A")
const C_ACCENT   := Color("#5E81AC")

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	if _sim and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_refresh)
	_build_ui()
	_refresh()
	print("CrewPanel v2.0: ready")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	var bg := Panel.new()
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = C_BG
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	var scroll := ScrollContainer.new()
	scroll.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	scroll.offset_left = 16; scroll.offset_top = 16
	scroll.offset_right = -16; scroll.offset_bottom = -16
	add_child(scroll)

	var root := VBoxContainer.new()
	root.size_flags_horizontal = SIZE_EXPAND_FILL
	root.add_theme_constant_override("separation", 20)
	scroll.add_child(root)

	# Title + hire button
	var header := HBoxContainer.new()
	root.add_child(header)
	var title := Label.new()
	title.text = tr("👷 Mannskap")
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		title.add_theme_font_override("font", _theme.font_heading_bold)
	title.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(title)

	var hire_btn := _make_btn("+ Ansett ny", C_PRIMARY)
	hire_btn.pressed.connect(func(): worker_hired.emit("general"))
	header.add_child(hire_btn)

	# KPI strip
	var kpi := HBoxContainer.new()
	kpi.add_theme_constant_override("separation", 16)
	root.add_child(kpi)

	var k1 := _kpi_card("ANSATTE", "--", C_PRIMARY)
	_kpi_count = k1.value; kpi.add_child(k1.card)
	var k2 := _kpi_card("LØNNSKOSTNAD/MND", "--", C_DANGER)
	_kpi_payroll = k2.value; kpi.add_child(k2.card)
	var k3 := _kpi_card("SNITT MORAL", "--", C_SUCCESS)
	_kpi_morale = k3.value; kpi.add_child(k3.card)
	var k4 := _kpi_card("EFFEKTIVITET", "--", C_ACCENT)
	_kpi_efficiency = k4.value; kpi.add_child(k4.card)

	# Main: Crew list + Skills
	var mid := HBoxContainer.new()
	mid.add_theme_constant_override("separation", 16)
	root.add_child(mid)

	var list_card := _make_card("📋 Ansattliste")
	list_card.size_flags_horizontal = SIZE_EXPAND_FILL
	list_card.size_flags_stretch_ratio = 2.0
	mid.add_child(list_card)
	_crew_list = _card_body(list_card)

	var skills_card := _make_card("🎯 Ferdigheter")
	skills_card.size_flags_horizontal = SIZE_EXPAND_FILL
	mid.add_child(skills_card)
	var skills_body := _card_body(skills_card)
	_skills_grid = GridContainer.new()
	_skills_grid.columns = 2
	_skills_grid.add_theme_constant_override("h_separation", 12)
	_skills_grid.add_theme_constant_override("v_separation", 12)
	skills_body.add_child(_skills_grid)

func _refresh() -> void:
	_refresh_kpis()
	_refresh_crew_list()
	_refresh_skills()

func _refresh_kpis() -> void:
	if not _sim:
		return

	var crew: Array = []
	if _sim.has_method("get_crew_roster"):
		crew = _sim.get_crew_roster()

	_kpi_count.text = str(crew.size())

	var total_salary := 0.0
	var total_morale := 0.0
	for w in crew:
		total_salary += w.get("salary", 35000.0)
		total_morale += w.get("morale", 75.0)

	_kpi_payroll.text = tr("%.0f kNOK") % (total_salary / 1000.0) if total_salary > 0 else "--"
	_kpi_morale.text = tr("%d%%") % int(total_morale / max(crew.size(), 1))
	_kpi_efficiency.text = "87%"  # Mock

func _refresh_crew_list() -> void:
	if not _crew_list:
		return
	for child in _crew_list.get_children():
		child.queue_free()

	var crew: Array = []
	if _sim and _sim.has_method("get_crew_roster"):
		crew = _sim.get_crew_roster()

	if crew.is_empty():
		# Show mock data
		crew = [
			{"name": "Erik Nordmann", "role": "Driftsleder", "salary": 52000, "morale": 88, "xp": 4200, "status": "Aktiv"},
			{"name": "Kari Fjordsen", "role": "Fôrtekniker", "salary": 42000, "morale": 92, "xp": 3100, "status": "Aktiv"},
			{"name": "Lars Berg", "role": "Røkter", "salary": 38000, "morale": 75, "xp": 1800, "status": "Aktiv"},
			{"name": "Mona Haugen", "role": "Veterinær", "salary": 58000, "morale": 81, "xp": 5600, "status": "Aktiv"},
			{"name": "Olav Strand", "role": "Skipper", "salary": 48000, "morale": 70, "xp": 2900, "status": "Permisjon"},
		]

	for w in crew:
		var row := PanelContainer.new()
		row.size_flags_horizontal = SIZE_EXPAND_FILL
		var sb := StyleBoxFlat.new()
		sb.bg_color = C_ELEVATED
		sb.border_color = C_BORDER
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(6)
		sb.set_content_margin_all(12)
		row.add_theme_stylebox_override("panel", sb)
		_crew_list.add_child(row)

		var hbox := HBoxContainer.new()
		hbox.add_theme_constant_override("separation", 12)
		row.add_child(hbox)

		# Avatar placeholder
		var avatar := Label.new()
		avatar.text = w.name.substr(0, 2).to_upper() if w.has("name") else "??"
		avatar.add_theme_font_size_override("font_size", 14)
		avatar.add_theme_color_override("font_color", Color.WHITE)
		avatar.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		avatar.custom_minimum_size = Vector2(36, 36)

		var avatar_bg := PanelContainer.new()
		var av_sb := StyleBoxFlat.new()
		av_sb.bg_color = C_PRIMARY
		av_sb.set_corner_radius_all(18)
		av_sb.set_content_margin_all(0)
		avatar_bg.add_theme_stylebox_override("panel", av_sb)
		avatar_bg.custom_minimum_size = Vector2(36, 36)
		avatar_bg.add_child(avatar)
		hbox.add_child(avatar_bg)

		# Name + role
		var info := VBoxContainer.new()
		info.size_flags_horizontal = SIZE_EXPAND_FILL
		hbox.add_child(info)

		var name_lbl := Label.new()
		name_lbl.text = w.get("name", "Ukjent")
		name_lbl.add_theme_font_size_override("font_size", 14)
		name_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_heading_semibold:
			name_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
		info.add_child(name_lbl)

		var role_lbl := Label.new()
		role_lbl.text = w.get("role", "Generell")
		role_lbl.add_theme_font_size_override("font_size", 12)
		role_lbl.add_theme_color_override("font_color", C_MUTED)
		info.add_child(role_lbl)

		# XP
		var xp_lbl := Label.new()
		xp_lbl.text = tr("%d XP") % w.get("xp", 0)
		xp_lbl.add_theme_font_size_override("font_size", 12)
		xp_lbl.add_theme_color_override("font_color", C_ACCENT)
		if _theme and _theme.font_mono:
			xp_lbl.add_theme_font_override("font", _theme.font_mono)
		hbox.add_child(xp_lbl)

		# Morale
		var morale: int = w.get("morale", 75)
		var morale_lbl := Label.new()
		morale_lbl.text = tr("%d%%") % morale
		morale_lbl.add_theme_font_size_override("font_size", 12)
		morale_lbl.add_theme_color_override("font_color", C_SUCCESS_D if morale >= 80 else (C_WARNING_D if morale >= 60 else C_DANGER))
		if _theme and _theme.font_mono:
			morale_lbl.add_theme_font_override("font", _theme.font_mono)
		hbox.add_child(morale_lbl)

		# Salary
		var sal_lbl := Label.new()
		sal_lbl.text = "%dk" % int(w.get("salary", 0) / 1000.0)
		sal_lbl.add_theme_font_size_override("font_size", 12)
		sal_lbl.add_theme_color_override("font_color", C_TEXT2)
		if _theme and _theme.font_mono:
			sal_lbl.add_theme_font_override("font", _theme.font_mono)
		hbox.add_child(sal_lbl)

		# Status
		var status: String = w.get("status", "Aktiv")
		var status_lbl := Label.new()
		status_lbl.text = status
		status_lbl.add_theme_font_size_override("font_size", 11)
		match status:
			"Aktiv": status_lbl.add_theme_color_override("font_color", C_SUCCESS_D)
			"Permisjon": status_lbl.add_theme_color_override("font_color", C_WARNING_D)
			_: status_lbl.add_theme_color_override("font_color", C_MUTED)
		hbox.add_child(status_lbl)

func _refresh_skills() -> void:
	if not _skills_grid:
		return
	for child in _skills_grid.get_children():
		child.queue_free()

	var skills := [
		{"name": "Fôring", "level": 85, "color": C_SUCCESS},
		{"name": "Avlusing", "level": 72, "color": C_SECONDARY},
		{"name": "Vedlikehold", "level": 68, "color": C_ACCENT},
		{"name": "Navigasjon", "level": 78, "color": C_PRIMARY},
		{"name": "Veterinær", "level": 90, "color": C_SUCCESS},
		{"name": "Ledelse", "level": 65, "color": C_WARNING},
	]

	for s in skills:
		var panel := PanelContainer.new()
		panel.size_flags_horizontal = SIZE_EXPAND_FILL
		var sb := StyleBoxFlat.new()
		sb.bg_color = C_ELEVATED
		sb.set_corner_radius_all(6)
		sb.set_content_margin_all(12)
		panel.add_theme_stylebox_override("panel", sb)
		_skills_grid.add_child(panel)

		var vbox := VBoxContainer.new()
		vbox.add_theme_constant_override("separation", 6)
		panel.add_child(vbox)

		var name_lbl := Label.new()
		name_lbl.text = s.name
		name_lbl.add_theme_font_size_override("font_size", 12)
		name_lbl.add_theme_color_override("font_color", C_TEXT2)
		vbox.add_child(name_lbl)

		var val := Label.new()
		val.text = "%d%%" % s.level
		val.add_theme_font_size_override("font_size", 18)
		val.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_mono:
			val.add_theme_font_override("font", _theme.font_mono)
		vbox.add_child(val)

		# Progress bar
		var bar_bg := ColorRect.new()
		bar_bg.color = C_BORDER
		bar_bg.custom_minimum_size = Vector2(0, 4)
		vbox.add_child(bar_bg)
		# Note: actual fill requires more complex approach in Godot
		# For now the percentage label conveys the info

# ── Helpers ───────────────────────────────────────────────────────────────────

func _kpi_card(title: String, initial: String, stripe: Color) -> Dictionary:
	var card := PanelContainer.new()
	card.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = stripe
	sb.set_border_width_all(1)
	sb.border_width_top = 4
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(16)
	card.add_theme_stylebox_override("panel", sb)
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	card.add_child(vbox)
	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", C_MUTED)
	vbox.add_child(lbl)
	var val := Label.new()
	val.text = initial
	val.add_theme_font_size_override("font_size", 22)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		val.add_theme_font_override("font", _theme.font_heading_bold)
	vbox.add_child(val)
	return {"card": card, "value": val}

func _make_card(title: String) -> PanelContainer:
	var card := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", sb)
	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	card.add_child(outer)
	var header := PanelContainer.new()
	var h_sb := StyleBoxFlat.new()
	h_sb.bg_color = C_SURFACE
	h_sb.border_color = C_BORDER
	h_sb.border_width_bottom = 1
	h_sb.set_content_margin(SIDE_LEFT, 20)
	h_sb.set_content_margin(SIDE_RIGHT, 20)
	h_sb.set_content_margin(SIDE_TOP, 14)
	h_sb.set_content_margin(SIDE_BOTTOM, 14)
	header.add_theme_stylebox_override("panel", h_sb)
	outer.add_child(header)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 15)
	t.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_semibold:
		t.add_theme_font_override("font", _theme.font_heading_semibold)
	header.add_child(t)
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 10)
	outer.add_child(body)
	return card

func _card_body(card: PanelContainer) -> VBoxContainer:
	return card.get_child(0).get_child(1) as VBoxContainer

func _make_btn(text: String, color: Color) -> Button:
	var btn := Button.new()
	btn.text = text
	var sb := StyleBoxFlat.new()
	sb.bg_color = color
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(10)
	btn.add_theme_stylebox_override("normal", sb)
	btn.add_theme_color_override("font_color", Color.WHITE)
	btn.add_theme_font_size_override("font_size", 13)
	return btn
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
