## DataExportPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/DataExportPanel.tscn; script is wiring only:
## @onready % refs, signal hookups, export logic, localization.
## Dynamic content (the 4 export cards) is still built in code from EXPORT_TYPES.
##
## Signals:
##   export_completed(path: String, rows: int)
##   panel_close_requested

extends Control

# ── Signals ──────────────────────────────────────────────────────────────────

signal export_completed(path: String, rows: int)
signal panel_close_requested

# ── Colors ────────────────────────────────────────────────────────────────────

const COLOR_BG := Color("#ECEFF4")
const COLOR_HEADER := Color("#FFFFFF")
const COLOR_CARD := Color("#ECEFF4")
const COLOR_CARD_HOVER := Color("#FFFFFF")
const COLOR_TEXT_BRIGHT := Color("#2E3440")
const COLOR_TEXT_DIM := Color("#7B8794")
const COLOR_ACCENT := Color("#5E81AC")
const COLOR_SUCCESS := Color("#A3BE8C")
const COLOR_WARNING := Color("#EBCB8B")
const COLOR_ERROR := Color("#BF616A")
const COLOR_EXPORT_ALL := Color("#5E81AC")

# ── Export type definitions ───────────────────────────────────────────────────

const EXPORT_TYPES: Array[Dictionary] = [
	{
		"id": "daily_snapshots",
		"title": "Daglige øyeblikksbilder",
		"subtitle": "En rad per simuleringsdag",
		"icon": "📊",
		"description": "Biomasse, fiskeantall, markedspris, bærekraftsscore og mer.\nBra for trendanalyse og visualisering.",
		"filename": "daglig_oversikt",
		"color": Color("#5E81AC"),
		"method": "export_daily_snapshots"
	},
	{
		"id": "financial_report",
		"title": "Finansrapport",
		"subtitle": "Aggregert per 30-dagers periode",
		"icon": "💰",
		"description": "Saldoendringer, gjennomsnittlig markedspris og FCR per periode.\nBra for lønnsomhetsanalyse.",
		"filename": "finansrapport",
		"color": Color("#D4B06A"),
		"method": "export_financial_report"
	},
	{
		"id": "population_data",
		"title": "Populasjonsdata",
		"subtitle": "Biologiske nøkkeltall per dag",
		"icon": "🐟",
		"description": "Fiskeantall, gjennomsnittsvekt, lus per fisk, dødsrate.\nBra for biologisk analyse og velferdssporing.",
		"filename": "populasjon",
		"color": Color("#A3BE8C"),
		"method": "export_population_data"
	},
	{
		"id": "event_history",
		"title": "Hendelseslogg",
		"subtitle": "Alle hendelser med finansiell påvirkning",
		"icon": "📋",
		"description": "Dato, kategori, beskrivelse og saldoendring for hver hendelse.\nBra for revisjon og risikoevaluering.",
		"filename": "hendelseslogg",
		"color": Color("#5E81AC"),
		"method": "export_event_history"
	}
]

# ── Telemetry export definitions (SimBridge analytics) ────────────────────────

## Window (in simulated days) exported by the balance-telemetry action.
const TELEMETRY_DEFAULT_DAYS := 365

## Header row / per-day metric order for the balance-telemetry CSV.
const TELEMETRY_COLUMNS: Array[String] = [
	"day",
	"balance_nok",
	"total_biomass_kg",
	"market_price_nok",
	"fcr",
	"lice_per_fish",
	"temperature_c",
	"mortality_rate",
	"sustainability_score",
]

## SimBridge metric keys (same order as the value columns above, minus "day").
const TELEMETRY_METRICS: Array[String] = [
	"balance_nok",
	"total_biomass_kg",
	"market_price_nok",
	"fcr",
	"lice_per_fish",
	"temperature_c",
	"mortality_rate",
	"sustainability_score",
]

## Header row for the single-row composite depth snapshot CSV.
const DEPTH_COLUMNS: Array[String] = [
	"pen_price",
	"supply_pressure",
	"insurance_total_premium",
	"resource_rent_paid",
	"worst_gill_risk",
]

# ── Runtime state ─────────────────────────────────────────────────────────────

var _export_directory: String = ""

# Scene-declared nodes
@onready var _title_label: Label = %TitleLabel
@onready var _status_label: Label = %StatusLabel
@onready var _close_button: Button = %CloseButton
@onready var _folder_label: Label = %FolderLabel
@onready var _path_display: LineEdit = %PathDisplay
@onready var _browse_button: Button = %BrowseButton
@onready var _cards_grid: GridContainer = %CardsGrid
@onready var _export_all_button: Button = %ExportAllButton
@onready var _progress_bar: ProgressBar = %ProgressBar
@onready var _log_label: Label = %LogLabel
@onready var _export_log: RichTextLabel = %ExportLog

var _export_buttons: Dictionary = {}   # id → Button

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_export_directory = _default_export_dir()
	_apply_localization()
	_connect_signals()
	_path_display.text = _export_directory
	for export_type in EXPORT_TYPES:
		_cards_grid.add_child(_build_export_card(export_type))
	_add_telemetry_section()
	_log_info("Klar til å eksportere.")

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Wiring ────────────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_EXPORT_TITLE", "Dataeksport")
	_status_label.text = _tr_or("UI_EXPORT_STATUS_READY", "Klar")
	_folder_label.text = _tr_or("UI_EXPORT_FOLDER_LABEL", "Eksportmappe")
	_log_label.text = _tr_or("UI_EXPORT_LOG_LABEL", "Eksportlogg")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _connect_signals() -> void:
	_close_button.pressed.connect(_on_close_pressed)
	_browse_button.pressed.connect(_on_browse_pressed)
	_path_display.text_submitted.connect(_on_path_submitted)
	_export_all_button.pressed.connect(_on_export_all_pressed)

func _build_export_card(export_type: Dictionary) -> Control:
	var card := _make_panel(COLOR_CARD)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.custom_minimum_size.y = 130

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	card.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	margin.add_child(vbox)

	# Header row
	var header_hbox := HBoxContainer.new()
	header_hbox.add_theme_constant_override("separation", 6)
	vbox.add_child(header_hbox)

	var icon_lbl := Label.new()
	icon_lbl.text = export_type.get("icon", "📄")
	icon_lbl.add_theme_font_size_override("font_size", 22)
	header_hbox.add_child(icon_lbl)

	var title_vbox := VBoxContainer.new()
	title_vbox.add_theme_constant_override("separation", 1)
	title_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header_hbox.add_child(title_vbox)

	var title_lbl := Label.new()
	title_lbl.text = export_type.get("title", "")
	title_lbl.add_theme_color_override("font_color", COLOR_TEXT_BRIGHT)
	title_lbl.add_theme_font_size_override("font_size", 14)
	title_vbox.add_child(title_lbl)

	var sub_lbl := Label.new()
	sub_lbl.text = export_type.get("subtitle", "")
	sub_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	sub_lbl.add_theme_font_size_override("font_size", 11)
	title_vbox.add_child(sub_lbl)

	# Description
	var desc_lbl := Label.new()
	desc_lbl.text = export_type.get("description", "")
	desc_lbl.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	desc_lbl.add_theme_font_size_override("font_size", 11)
	desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_lbl.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(desc_lbl)

	# Export button
	var type_id: String = export_type.get("id", "")
	var btn_color: Color = export_type.get("color", COLOR_ACCENT)
	var btn := _make_small_button("Eksporter CSV", btn_color)
	btn.pressed.connect(_on_export_single_pressed.bind(type_id))
	vbox.add_child(btn)

	_export_buttons[type_id] = btn

	return card

# ── Export logic ──────────────────────────────────────────────────────────────

func _on_export_single_pressed(type_id: String) -> void:
	var export_type: Dictionary = {}
	for t in EXPORT_TYPES:
		if t.get("id") == type_id:
			export_type = t
			break
	if export_type.is_empty(): return

	var dir := _path_display.text.strip_edges()
	if dir.is_empty():
		_log_error("Velg en eksportmappe først.")
		return

	_set_button_state(type_id, false)
	_set_status("Eksporterer...")
	_progress_bar.visible = true
	_progress_bar.value = 30

	var day_str := "%04d" % _current_day()

	var filename: String = export_type.get("filename", type_id)
	var path := dir.path_join("%s_dag%s.csv" % [filename, day_str])

	var rows := _call_export_method(export_type.get("method", ""), path)

	_progress_bar.value = 100
	await get_tree().create_timer(0.3).timeout
	_progress_bar.visible = false

	if rows >= 0:
		_set_status("Ferdig")
		_log_ok("✓ %s — %d rader → %s" % [export_type.get("title", ""), rows, path])
		emit_signal("export_completed", path, rows)
	else:
		_set_status("Feil")
		_log_error("✗ Eksport mislyktes: " + export_type.get("title", ""))

	_set_button_state(type_id, true)

func _on_export_all_pressed() -> void:
	var dir := _path_display.text.strip_edges()
	if dir.is_empty():
		_log_error("Velg en eksportmappe først.")
		return

	for t in EXPORT_TYPES:
		_set_button_state(t.get("id", ""), false)

	_set_status("Eksporterer alle...")
	_progress_bar.visible = true

	var total := float(EXPORT_TYPES.size())
	var total_rows := 0

	for i in range(EXPORT_TYPES.size()):
		var export_type: Dictionary = EXPORT_TYPES[i]
		_progress_bar.value = int((float(i) / total) * 90.0)

		var day_str := "%04d" % _current_day()

		var path := dir.path_join("%s_dag%s.csv" % [export_type.get("filename", "data"), day_str])
		var rows := _call_export_method(export_type.get("method", ""), path)

		if rows >= 0:
			total_rows += rows
			_log_ok("✓ %s — %d rader" % [export_type.get("title", ""), rows])
		else:
			_log_error("✗ %s mislyktes" % export_type.get("title", ""))

		await get_tree().create_timer(0.1).timeout

	_progress_bar.value = 100
	await get_tree().create_timer(0.4).timeout
	_progress_bar.visible = false

	_set_status("Alle eksportert — %d rader totalt" % total_rows)
	_log_ok("Alle eksporter fullført. %d rader totalt → %s" % [total_rows, dir])

	for t in EXPORT_TYPES:
		_set_button_state(t.get("id", ""), true)

## The SimBridge facade (an autoload node: Engine.has_singleton was always
## false, so every export failed with "GameBridge ikke tilgjengelig").
func _sim_bridge() -> Node:
	return get_node_or_null("/root/SimBridge")

func _current_day() -> int:
	var sim := _sim_bridge()
	return int(sim.current_day) if sim != null else 0

func _call_export_method(method_name: String, path: String) -> int:
	var sim := _sim_bridge()
	if sim == null:
		_log_error("SimBridge ikke tilgjengelig.")
		return -1
	if not sim.has_method(method_name):
		_log_error("Metode ikke funnet: " + method_name)
		return -1

	var result = sim.call(method_name, path)
	if result is int:
		return result
	return -1

# ── Telemetry export (SimBridge analytics) ────────────────────────────────────

func _add_telemetry_section() -> void:
	var body := _cards_grid.get_parent()
	if body == null:
		return
	var section := _build_telemetry_section()
	body.add_child(section)
	body.move_child(section, _cards_grid.get_index() + 1)

func _build_telemetry_section() -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)

	var telemetry_btn := _make_small_button("Export balance telemetry (CSV)", COLOR_ACCENT)
	telemetry_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	telemetry_btn.tooltip_text = "Én rad per simuleringsdag for de siste %d dagene." % TELEMETRY_DEFAULT_DAYS
	telemetry_btn.pressed.connect(_on_export_telemetry_pressed)
	row.add_child(telemetry_btn)

	var depth_btn := _make_small_button("Export depth snapshot (CSV)", COLOR_ACCENT)
	depth_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	depth_btn.tooltip_text = "Pen-pris, tilbudstrykk, forsikringspremie, ressursrente og verste gjellerisiko."
	depth_btn.pressed.connect(_on_export_depth_pressed)
	row.add_child(depth_btn)

	return row

func _on_export_telemetry_pressed() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null:
		_set_status("Feil")
		_log_error("✗ SimBridge ikke tilgjengelig.")
		_toast("Telemetrieksport mislyktes: SimBridge mangler", "CRITICAL", 5.0)
		return

	var dir := _resolve_export_dir()
	if dir.is_empty():
		_log_error("Velg en eksportmappe først.")
		return

	_set_status("Eksporterer telemetri...")
	_progress_bar.visible = true
	_progress_bar.value = 25

	var contents := _build_telemetry_csv(sim, TELEMETRY_DEFAULT_DAYS)
	var path := dir.path_join("balansetelemetri_dag%s.csv" % _day_string(sim))
	var rows := _write_csv(path, contents)

	_progress_bar.value = 100
	await get_tree().create_timer(0.3).timeout
	_progress_bar.visible = false

	if rows >= 0:
		_set_status("Ferdig")
		_log_ok("✓ Balansetelemetri — %d rader → %s" % [rows, path])
		_toast("✓ Telemetri eksportert (%d rader)" % rows, "SUCCESS", 4.0)
		emit_signal("export_completed", path, rows)
	else:
		_set_status("Feil")
		_log_error("✗ Telemetrieksport mislyktes: " + path)
		_toast("Telemetrieksport mislyktes", "CRITICAL", 5.0)

func _on_export_depth_pressed() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null:
		_set_status("Feil")
		_log_error("✗ SimBridge ikke tilgjengelig.")
		_toast("Dybdeeksport mislyktes: SimBridge mangler", "CRITICAL", 5.0)
		return

	var dir := _resolve_export_dir()
	if dir.is_empty():
		_log_error("Velg en eksportmappe først.")
		return

	_set_status("Eksporterer dybde...")
	_progress_bar.visible = true
	_progress_bar.value = 25

	var contents := _build_depth_csv(sim)
	var path := dir.path_join("dybde_oyeblikksbilde_dag%s.csv" % _day_string(sim))
	var rows := _write_csv(path, contents)

	_progress_bar.value = 100
	await get_tree().create_timer(0.3).timeout
	_progress_bar.visible = false

	if rows >= 0:
		_set_status("Ferdig")
		_log_ok("✓ Dybde-øyeblikksbilde — %d rader → %s" % [rows, path])
		_toast("✓ Dybde-øyeblikksbilde eksportert", "SUCCESS", 4.0)
		emit_signal("export_completed", path, rows)
	else:
		_set_status("Feil")
		_log_error("✗ Dybdeeksport mislyktes: " + path)
		_toast("Dybdeeksport mislyktes", "CRITICAL", 5.0)

func _resolve_export_dir() -> String:
	var dir := _path_display.text.strip_edges()
	if dir.is_empty():
		return "user://exports"
	return dir

func _day_string(sim: Node) -> String:
	if sim != null and sim.has_method("get_current_day"):
		return "%04d" % int(sim.get_current_day())
	return "0000"

func _build_telemetry_csv(sim: Node, days: int) -> String:
	var series: Dictionary = {}
	var longest := 0
	for metric in TELEMETRY_METRICS:
		var values: Array = series.get(metric, [])
		if sim.has_method("get_analytics_data"):
			var raw = sim.get_analytics_data(metric, days)
			if raw is Array:
				values = raw
		series[metric] = values
		longest = maxi(longest, values.size())

	var row_count: int = clampi(longest, 0, days)
	var start_day := _telemetry_start_day(sim, row_count)

	var lines := PackedStringArray()
	lines.append(",".join(TELEMETRY_COLUMNS))
	for i in range(row_count):
		var cells := PackedStringArray()
		cells.append(str(start_day + i))
		for metric in TELEMETRY_METRICS:
			cells.append(_cell(series[metric], i))
		lines.append(",".join(cells))
	return "\n".join(lines) + "\n"

func _telemetry_start_day(sim: Node, row_count: int) -> int:
	var current := 0
	if sim != null and sim.has_method("get_current_day"):
		current = int(sim.get_current_day())
	if current < 1:
		current = row_count
	return maxi(1, current - row_count + 1)

func _build_depth_csv(sim: Node) -> String:
	var pen_price := ""
	if sim.has_method("get_pen_price"):
		var pp := float(sim.get_pen_price())
		if pp > 0.0:
			pen_price = "%.4f" % pp

	var supply_pressure := ""
	if sim.has_method("get_player_supply_pressure"):
		var sp := float(sim.get_player_supply_pressure())
		if sp >= 0.0:
			supply_pressure = "%.4f" % sp

	var insurance_total_premium := ""
	if sim.has_method("get_insurance_status"):
		var ins = sim.get_insurance_status()
		if ins is Dictionary and ins.has("total_premium"):
			insurance_total_premium = "%.4f" % float(ins.get("total_premium", 0.0))

	var resource_rent_paid := ""
	if sim.has_method("get_resource_rent"):
		var rr = sim.get_resource_rent()
		if rr is Dictionary and rr.has("paid"):
			resource_rent_paid = "%.4f" % float(rr.get("paid", 0.0))

	var worst_gill_risk := ""
	if sim.has_method("get_all_pens") and sim.has_method("get_gill_risk"):
		var worst := -1.0
		var pens = sim.get_all_pens()
		if pens is Array:
			for pen in pens:
				if pen is Dictionary:
					var sid := str(pen.get("id", ""))
					if sid.is_empty():
						continue
					var g = sim.get_gill_risk(sid)
					if g is Dictionary:
						worst = maxf(worst, float(g.get("risk", 0.0)))
		if worst >= 0.0:
			worst_gill_risk = "%.4f" % worst

	var lines := PackedStringArray()
	lines.append(",".join(DEPTH_COLUMNS))
	lines.append(",".join([pen_price, supply_pressure, insurance_total_premium, resource_rent_paid, worst_gill_risk]))
	return "\n".join(lines) + "\n"

func _cell(values: Array, index: int) -> String:
	if index < 0 or index >= values.size():
		return ""
	var value = values[index]
	if value == null:
		return ""
	if value is float or value is int:
		var f := float(value)
		if is_nan(f) or is_inf(f):
			return ""
		return "%.4f" % f
	return str(value)

func _write_csv(path: String, contents: String) -> int:
	var dir_path := path.get_base_dir()
	if not dir_path.is_empty() and not DirAccess.dir_exists_absolute(dir_path):
		DirAccess.make_dir_recursive_absolute(dir_path)
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		_log_error("Kunne ikke åpne fil for skriving: %s" % path)
		return -1
	file.store_string(contents)
	file.close()
	return maxi(0, contents.split("\n", false).size() - 1)

func _toast(message: String, level: String, duration: float) -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, duration)

# ── File browser ──────────────────────────────────────────────────────────────

func _on_browse_pressed() -> void:
	# Native file dialog (Godot 4.x)
	var dialog := FileDialog.new()
	dialog.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	dialog.access = FileDialog.ACCESS_FILESYSTEM
	dialog.current_dir = _path_display.text if not _path_display.text.is_empty() else OS.get_system_dir(OS.SYSTEM_DIR_DOCUMENTS)
	dialog.title = "Velg eksportmappe"
	add_child(dialog)
	dialog.popup_centered(Vector2i(700, 500))
	dialog.dir_selected.connect(func(dir: String):
		_path_display.text = dir
		_export_directory = dir
		dialog.queue_free()
	)
	dialog.canceled.connect(func(): dialog.queue_free())

func _on_path_submitted(new_text: String) -> void:
	_export_directory = new_text.strip_edges()

# ── Status helpers ────────────────────────────────────────────────────────────

func _set_status(text: String) -> void:
	if _status_label:
		_status_label.text = text

func _set_button_state(type_id: String, enabled: bool) -> void:
	if type_id in _export_buttons:
		_export_buttons[type_id].disabled = not enabled

func _log_ok(message: String) -> void:
	if _export_log:
		_export_log.append_text("[color=#A3BE8C]" + message + "[/color]\n")

func _log_error(message: String) -> void:
	if _export_log:
		_export_log.append_text("[color=#BF616A]" + message + "[/color]\n")

func _log_info(message: String) -> void:
	if _export_log:
		_export_log.append_text("[color=#7B8794]" + message + "[/color]\n")

# ── Misc ──────────────────────────────────────────────────────────────────────

func _on_close_pressed() -> void:
	emit_signal("panel_close_requested")

func _default_export_dir() -> String:
	var docs := OS.get_system_dir(OS.SYSTEM_DIR_DOCUMENTS)
	return docs.rstrip("/") + "/LakskongenSim/Eksport"

func _make_panel(bg_color: Color) -> PanelContainer:
	var pc := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = bg_color
	pc.add_theme_stylebox_override("panel", style)
	return pc

func _make_small_button(label_text: String, color: Color) -> Button:
	var btn := Button.new()
	btn.text = label_text
	btn.add_theme_color_override("font_color", COLOR_TEXT_BRIGHT)
	btn.add_theme_font_size_override("font_size", 12)
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.set_corner_radius_all(3)
	style.set_content_margin_all(5)
	btn.add_theme_stylebox_override("normal", style)
	var hover_style := style.duplicate() as StyleBoxFlat
	hover_style.bg_color = color.lightened(0.15)
	btn.add_theme_stylebox_override("hover", hover_style)
	var disabled_style := style.duplicate() as StyleBoxFlat
	disabled_style.bg_color = color.darkened(0.45)
	btn.add_theme_stylebox_override("disabled", disabled_style)
	return btn
