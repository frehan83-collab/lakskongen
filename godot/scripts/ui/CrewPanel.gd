## CrewPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/CrewPanel.tscn; script is wiring only:
## @onready % refs, signal hookups, refresh, localization, theme fonts.
## Dynamic content (crew rows, skill tiles) is still built in code.

extends Control

signal worker_hired(role: String)
signal worker_fired(worker_id: String)

var _sim: Node = null
var _theme: Node = null

# UI refs (scene-declared)
@onready var _title_label: Label = %TitleLabel
@onready var _hire_button: Button = %HireButton
@onready var _kpi_count: Label = %KpiCountValue
@onready var _kpi_payroll: Label = %KpiPayrollValue
@onready var _kpi_morale: Label = %KpiMoraleValue
@onready var _kpi_efficiency: Label = %KpiEfficiencyValue
@onready var _crew_list: VBoxContainer = %CrewList
@onready var _skills_grid: GridContainer = %SkillsGrid

# Colors
const C_BG := Color("#ECEFF4")
const C_SURFACE := Color("#FFFFFF")
const C_ELEVATED := Color("#F8F9FB")
const C_TEXT := Color("#2E3440")
const C_TEXT2 := Color("#4C566A")
const C_MUTED := Color("#7B8794")
const C_BORDER := Color("#E5E9F0")
const C_PRIMARY := Color("#0A4D68")
const C_SECONDARY := Color("#88C0D0")
const C_SUCCESS := Color("#A3BE8C")
const C_SUCCESS_D := Color("#8FAD78")
const C_WARNING := Color("#EBCB8B")
const C_WARNING_D := Color("#D4B06A")
const C_DANGER := Color("#BF616A")
const C_ACCENT := Color("#5E81AC")

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	if _sim and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_refresh)
	_apply_localization()
	_apply_heading_fonts()
	_hire_button.pressed.connect(_on_hire_pressed)

	# UIPolish: keyboard navigation for crew list
	UIPolish.link_vertical_focus(_crew_list)

	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_CREW_TITLE", "👷 Mannskap")
	_hire_button.text = _tr_or("UI_CREW_HIRE", "+ Ansett ny")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _role_name(role: String) -> String:
	match role:
		"roekter": return "Røkter (storm/not vern)"
		"veterinaer": return "Veterinær (behandling)"
		"skipper": return "Skipper (brønnbåt)"
		_: return role.capitalize()

func _on_hire_pressed() -> void:
	if not _sim or not _sim.has_method("hire_worker"):
		return
	# Hire the first candidate role not yet on staff (one specialist each).
	var roster: Array = _sim.get_crew_list() if _sim.has_method("get_crew_list") else []
	var have := {}
	for w in roster:
		have[str(w.get("role", ""))] = true
	var order := ["roekter", "veterinaer", "skipper"]
	for role in order:
		if not have.get(role, false):
			if _sim.hire_worker(role):
				worker_hired.emit(role)
			_refresh()
			return
	_toast("Alle spesialistroller er besatt")

func _toast(message: String) -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, "INFO", 3.0)

func _apply_heading_fonts() -> void:
	if _theme and _theme.font_heading_bold:
		_title_label.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_count.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_payroll.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_morale.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_efficiency.add_theme_font_override("font", _theme.font_heading_bold)

func _refresh() -> void:
	_refresh_kpis()
	_refresh_crew_list()
	_refresh_skills()

func _refresh_kpis() -> void:
	if not _sim:
		return

	var crew: Array = []
	if _sim and _sim.has_method("get_crew_list"):
		crew = _sim.get_crew_list()

	_kpi_count.text = str(crew.size())

	var total_salary := 0.0
	var total_morale := 0.0
	for w in crew:
		total_salary += float(w.get("salary_nok", w.get("salary", 35000.0)))
		total_morale += float(w.get("morale", 75.0))

	_kpi_payroll.text = tr("%.0f kNOK") % (total_salary / 1000.0) if total_salary > 0 else "--"
	_kpi_morale.text = tr("%d%%") % int(total_morale / max(crew.size(), 1)) if not crew.is_empty() else "--"
	# Efficiency = specialist coverage (Phase 3.1): base 60 + 12 per role.
	var roles := {}
	for w in crew:
		roles[str(w.get("role", ""))] = true
	_kpi_efficiency.text = "%d%%" % mini(96, 60 + 12 * roles.size())

func _refresh_crew_list() -> void:
	if not _crew_list:
		return
	for child in _crew_list.get_children():
		child.queue_free()

	var crew: Array = []
	if _sim and _sim.has_method("get_crew_list"):
		crew = _sim.get_crew_list()

	if crew.is_empty() and _sim == null:
		# Offline preview only (no bridge): mock data so the layout reads.
		crew = [
			{"name": "Erik Nordmann", "role": "Driftsleder", "salary_nok": 52000, "morale": 88, "xp": 4200, "status": "Aktiv"},
			{"name": "Kari Fjordsen", "role": "Fôrtekniker", "salary_nok": 42000, "morale": 92, "xp": 3100, "status": "Aktiv"},
			{"name": "Lars Berg", "role": "Røkter", "salary_nok": 38000, "morale": 75, "xp": 1800, "status": "Aktiv"},
		]

	if crew.is_empty():
		UIPolish.show_empty_state_localized(_crew_list, _tr_or("UI_CREW_EMPTY", "Ingen ansatte — bruk + Ansett ny (50k signering)"), 13, "", _theme)
		return

	for w in crew:
		_crew_list.add_child(_build_crew_row(w))

	# UIPolish: vertical keyboard navigation for crew rows (re-apply after rebuild)
	UIPolish.link_vertical_focus(_crew_list)

# Dynamic row builder (content-driven, kept in code by design)
func _build_crew_row(w: Dictionary) -> PanelContainer:
	var row := PanelContainer.new()
	row.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_ELEVATED
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(6)
	sb.set_content_margin_all(12)
	row.add_theme_stylebox_override("panel", sb)

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
	role_lbl.text = _role_name(str(w.get("role", "Generell")))
	role_lbl.add_theme_font_size_override("font_size", 12)
	role_lbl.add_theme_color_override("font_color", C_MUTED)
	info.add_child(role_lbl)

	# Tenure (real: hired day; mock rows carry xp instead)
	var xp_lbl := Label.new()
	if w.has("hired_day"):
		xp_lbl.text = "Dag %d" % int(w.get("hired_day", 0))
	else:
		xp_lbl.text = tr("%d XP") % int(w.get("xp", 0))
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
	sal_lbl.text = "%dk" % int(float(w.get("salary_nok", w.get("salary", 0))) / 1000.0)
	sal_lbl.add_theme_font_size_override("font_size", 12)
	sal_lbl.add_theme_color_override("font_color", C_TEXT2)
	if _theme and _theme.font_mono:
		sal_lbl.add_theme_font_override("font", _theme.font_mono)
	hbox.add_child(sal_lbl)

	# Status — or fire button for real hires (Phase 3.1).
	if w.has("id") and _sim and _sim.has_method("fire_worker"):
		var fire := Button.new()
		fire.text = "✕"
		fire.tooltip_text = "Si opp"
		var wid := str(w.get("id", ""))
		fire.pressed.connect(func(): _sim.fire_worker(wid))
		hbox.add_child(fire)
	else:
		var status: String = str(w.get("status", "Aktiv"))
		var status_lbl := Label.new()
		status_lbl.text = status
		status_lbl.add_theme_font_size_override("font_size", 11)
		match status:
			"Aktiv": status_lbl.add_theme_color_override("font_color", C_SUCCESS_D)
			"Permisjon": status_lbl.add_theme_color_override("font_color", C_WARNING_D)
			_: status_lbl.add_theme_color_override("font_color", C_MUTED)
		hbox.add_child(status_lbl)

	return row

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
