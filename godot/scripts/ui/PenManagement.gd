## PenManagement v2.0 — Nordic Minimalist
## Pen list (left) + Detail panel (right) with stats, water quality,
## health, genetics, feed log, and action buttons.
## Fully programmatic UI matching pen-detail-prototype.html.

extends Control

signal pen_selected(pen_id: String)
signal action_requested(action: String, pen_id: String)

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── State ─────────────────────────────────────────────────────────────────────
var _selected_pen_id: String = ""
var _pen_list_items: Dictionary = {}  # pen_id -> Button

# ── UI Refs ───────────────────────────────────────────────────────────────────
var _pen_list_vbox: VBoxContainer
var _detail_scroll: ScrollContainer
var _no_pen_label: Label

# Detail labels
var _detail_name: Label
var _detail_subtitle: Label
var _detail_status_badge: Label

# KPIs
var _det_count: Label
var _det_weight: Label
var _det_biomass: Label
var _det_fcr: Label

# Water quality
var _det_temp: Label
var _det_oxygen: Label
var _det_current: Label
var _det_visibility: Label
var _det_algae: Label

# Health
var _det_lice: Label
var _det_disease: Label
var _det_mortality: Label
var _det_mortality_total: Label

# Genetics
var _det_gene_growth: Label
var _det_gene_lice: Label
var _det_gene_disease: Label
var _det_gene_fillet: Label

# Buttons
var _btn_feed: Button
var _btn_delouse: Button
var _btn_harvest: Button

# ── Colors ────────────────────────────────────────────────────────────────────
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

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)
		if _sim.has_signal("pen_action_completed"):
			_sim.pen_action_completed.connect(_on_pen_action_completed)

	_build_ui()
	_refresh()
	print("PenManagement v2.0: ready")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ══════════════════════════════════════════════════════════════════════════════
# UI BUILD
# ══════════════════════════════════════════════════════════════════════════════

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	# Background
	var bg := Panel.new()
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = C_BG
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	# Split layout: pen list (left) + detail (right)
	var hsplit := HBoxContainer.new()
	hsplit.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	hsplit.offset_left = 16; hsplit.offset_top = 16
	hsplit.offset_right = -16; hsplit.offset_bottom = -16
	hsplit.add_theme_constant_override("separation", 16)
	add_child(hsplit)

	# ── LEFT: Pen List ──
	var left_card := _make_card_container("🐟 Merder")
	left_card.custom_minimum_size.x = 250
	hsplit.add_child(left_card)

	var left_body := left_card.get_child(0).get_child(1) as VBoxContainer
	var list_scroll := ScrollContainer.new()
	list_scroll.size_flags_vertical = SIZE_EXPAND_FILL
	left_body.add_child(list_scroll)

	_pen_list_vbox = VBoxContainer.new()
	_pen_list_vbox.add_theme_constant_override("separation", 8)
	_pen_list_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
	list_scroll.add_child(_pen_list_vbox)

	# ── RIGHT: Detail Panel ──
	_no_pen_label = Label.new()
	_no_pen_label.text = "Velg en merd for detaljer"
	_no_pen_label.add_theme_font_size_override("font_size", 16)
	_no_pen_label.add_theme_color_override("font_color", C_MUTED)
	_no_pen_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_no_pen_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_no_pen_label.size_flags_horizontal = SIZE_EXPAND_FILL
	_no_pen_label.size_flags_vertical = SIZE_EXPAND_FILL

	_detail_scroll = ScrollContainer.new()
	_detail_scroll.size_flags_horizontal = SIZE_EXPAND_FILL
	_detail_scroll.size_flags_vertical = SIZE_EXPAND_FILL
	_detail_scroll.visible = false

	hsplit.add_child(_no_pen_label)
	hsplit.add_child(_detail_scroll)

	_build_detail_panel()

func _build_detail_panel() -> void:
	var root := VBoxContainer.new()
	root.size_flags_horizontal = SIZE_EXPAND_FILL
	root.add_theme_constant_override("separation", 16)
	_detail_scroll.add_child(root)

	# ── Header ──
	var header := HBoxContainer.new()
	root.add_child(header)

	var header_left := VBoxContainer.new()
	header_left.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(header_left)

	_detail_name = Label.new()
	_detail_name.text = "Merd 1"
	_detail_name.add_theme_font_size_override("font_size", 24)
	_detail_name.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		_detail_name.add_theme_font_override("font", _theme.font_heading_bold)
	header_left.add_child(_detail_name)

	_detail_subtitle = Label.new()
	_detail_subtitle.text = "--"
	_detail_subtitle.add_theme_font_size_override("font_size", 13)
	_detail_subtitle.add_theme_color_override("font_color", C_MUTED)
	header_left.add_child(_detail_subtitle)

	# Status badge + buttons
	var header_right := HBoxContainer.new()
	header_right.add_theme_constant_override("separation", 10)
	header.add_child(header_right)

	_detail_status_badge = Label.new()
	_detail_status_badge.text = "● Frisk"
	_detail_status_badge.add_theme_font_size_override("font_size", 12)
	_detail_status_badge.add_theme_color_override("font_color", C_SUCCESS_D)
	header_right.add_child(_detail_status_badge)

	_btn_delouse = _make_action_btn("🧪 Avlus", C_WARNING)
	header_right.add_child(_btn_delouse)
	_btn_delouse.pressed.connect(_on_delouse)

	_btn_feed = _make_action_btn("🐟 Fôr", C_PRIMARY)
	header_right.add_child(_btn_feed)
	_btn_feed.pressed.connect(_on_feed)

	_btn_harvest = _make_action_btn("📦 Høst", C_SUCCESS)
	header_right.add_child(_btn_harvest)
	_btn_harvest.pressed.connect(_on_harvest)

	# ── KPI row ──
	var kpi_row := HBoxContainer.new()
	kpi_row.add_theme_constant_override("separation", 12)
	root.add_child(kpi_row)

	_det_count = _add_stat_card(kpi_row, "ANTALL FISK", "--")
	_det_weight = _add_stat_card(kpi_row, "SNITT VEKT", "--")
	_det_biomass = _add_stat_card(kpi_row, "BIOMASSE", "--")
	_det_fcr = _add_stat_card(kpi_row, "FCR (AKTUELL)", "--")

	# ── Bottom grid: Water + Health + Genetics ──
	var bottom := HBoxContainer.new()
	bottom.add_theme_constant_override("separation", 16)
	root.add_child(bottom)

	# Water quality card
	var water_card := _make_card_container("🌊 Miljø")
	water_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bottom.add_child(water_card)
	var water_body := water_card.get_child(0).get_child(1) as VBoxContainer

	_det_temp = _add_detail_row(water_body, "Temperatur", "--")
	_det_oxygen = _add_detail_row(water_body, "Oksygen", "--")
	_det_current = _add_detail_row(water_body, "Strøm", "--")
	_det_visibility = _add_detail_row(water_body, "Sikt", "--")
	_det_algae = _add_detail_row(water_body, "Algevekst", "--")

	# Health card
	var health_card := _make_card_container("💀 Helse")
	health_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bottom.add_child(health_card)
	var health_body := health_card.get_child(0).get_child(1) as VBoxContainer

	_det_lice = _add_detail_row(health_body, "Lusetall", "--")
	_det_disease = _add_detail_row(health_body, "Sykdom", "--")
	_det_mortality = _add_detail_row(health_body, "Dødelighet (uke)", "--")
	_det_mortality_total = _add_detail_row(health_body, "Dødelighet (total)", "--")

	# Genetics card
	var gene_card := _make_card_container("🧬 Genetikk")
	gene_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bottom.add_child(gene_card)
	var gene_body := gene_card.get_child(0).get_child(1) as VBoxContainer

	_det_gene_growth = _add_detail_row(gene_body, "Veksthastighet", "--")
	_det_gene_lice = _add_detail_row(gene_body, "Luseresistens", "--")
	_det_gene_disease = _add_detail_row(gene_body, "Sykdomsresistens", "--")
	_det_gene_fillet = _add_detail_row(gene_body, "Filétkvalitet", "--")

# ── Widget builders ───────────────────────────────────────────────────────────

func _make_card_container(title: String) -> PanelContainer:
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
	h_sb.set_content_margin(SIDE_LEFT, 16)
	h_sb.set_content_margin(SIDE_RIGHT, 16)
	h_sb.set_content_margin(SIDE_TOP, 12)
	h_sb.set_content_margin(SIDE_BOTTOM, 12)
	header.add_theme_stylebox_override("panel", h_sb)
	outer.add_child(header)

	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 14)
	t.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_semibold:
		t.add_theme_font_override("font", _theme.font_heading_semibold)
	header.add_child(t)

	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 8)
	outer.add_child(body)

	return card

func _add_stat_card(parent: HBoxContainer, title: String, value: String) -> Label:
	var card := PanelContainer.new()
	card.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(14)
	card.add_theme_stylebox_override("panel", sb)
	parent.add_child(card)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	card.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", C_MUTED)
	vbox.add_child(lbl)

	var val := Label.new()
	val.text = value
	val.add_theme_font_size_override("font_size", 22)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		val.add_theme_font_override("font", _theme.font_heading_bold)
	vbox.add_child(val)

	return val

func _add_detail_row(parent: VBoxContainer, label_text: String, value_text: String) -> Label:
	var row := HBoxContainer.new()
	parent.add_child(row)

	var lbl := Label.new()
	lbl.text = label_text
	lbl.add_theme_font_size_override("font_size", 13)
	lbl.add_theme_color_override("font_color", C_TEXT2)
	lbl.size_flags_horizontal = SIZE_EXPAND_FILL
	row.add_child(lbl)

	var val := Label.new()
	val.text = value_text
	val.add_theme_font_size_override("font_size", 13)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_mono:
		val.add_theme_font_override("font", _theme.font_mono)
	val.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(val)

	return val

func _make_action_btn(text: String, color: Color) -> Button:
	var btn := Button.new()
	btn.text = text
	var sb := StyleBoxFlat.new()
	sb.bg_color = color
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(8)
	btn.add_theme_stylebox_override("normal", sb)
	var hover_sb := sb.duplicate() as StyleBoxFlat
	hover_sb.bg_color = color.lightened(0.1)
	btn.add_theme_stylebox_override("hover", hover_sb)
	btn.add_theme_color_override("font_color", Color.WHITE)
	btn.add_theme_color_override("font_hover_color", Color.WHITE)
	btn.add_theme_font_size_override("font_size", 13)
	return btn

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	_refresh_pen_list()
	if _selected_pen_id:
		_refresh_detail()

func _refresh_pen_list() -> void:
	for child in _pen_list_vbox.get_children():
		child.queue_free()
	_pen_list_items.clear()

	if not _sim:
		return

	var pens: Array = []
	if _sim.has_method("get_all_pens"):
		pens = _sim.get_all_pens()

	if pens.is_empty():
		var lbl := Label.new()
		lbl.text = "Ingen merder tilgjengelig"
		lbl.add_theme_color_override("font_color", C_MUTED)
		_pen_list_vbox.add_child(lbl)
		_show_no_pen()
		return

	for pen in pens:
		var pen_id: String = pen.get("id", "")
		var fish: int = pen.get("fish_count", 0)
		var biomass: float = pen.get("biomass_kg", 0.0)
		var is_selected := pen_id == _selected_pen_id

		var btn := Button.new()
		btn.text = "%s\n%s fisk · %.1f t" % [pen_id, _fmt_num(fish), biomass / 1000.0]
		btn.size_flags_horizontal = SIZE_EXPAND_FILL
		btn.custom_minimum_size.y = 50

		var sb := StyleBoxFlat.new()
		sb.bg_color = C_PRIMARY if is_selected else C_ELEVATED
		sb.border_color = C_PRIMARY if is_selected else C_BORDER
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(6)
		sb.set_content_margin_all(10)
		btn.add_theme_stylebox_override("normal", sb)
		btn.add_theme_color_override("font_color", Color.WHITE if is_selected else C_TEXT)
		btn.add_theme_font_size_override("font_size", 13)
		btn.pressed.connect(_select_pen.bind(pen_id))

		_pen_list_vbox.add_child(btn)
		_pen_list_items[pen_id] = btn

	# Auto-select first if none selected
	if _selected_pen_id.is_empty() and not pens.is_empty():
		_select_pen(pens[0].get("id", ""))

func _refresh_detail() -> void:
	if not _sim or _selected_pen_id.is_empty():
		_show_no_pen()
		return

	var pens: Array = []
	if _sim.has_method("get_all_pens"):
		pens = _sim.get_all_pens()

	var pen := {}
	for p in pens:
		if p.get("id") == _selected_pen_id:
			pen = p
			break

	if pen.is_empty():
		_show_no_pen()
		return

	_show_detail()

	var fish: int = pen.get("fish_count", 0)
	var biomass: float = pen.get("biomass_kg", 0.0)
	var avg_w: float = biomass / float(fish) if fish > 0 else 0.0
	var max_bio: float = pen.get("max_biomass", 780000.0)

	_detail_name.text = pen.get("name", _selected_pen_id)
	_detail_subtitle.text = "Sone: %s · Kapasitet: %.0f%%" % [
		pen.get("zone", "Ukjent"),
		(biomass / max_bio * 100.0) if max_bio > 0 else 0.0
	]

	_det_count.text = _fmt_num(fish)
	_det_weight.text = "%.1f kg" % avg_w
	_det_biomass.text = "%.0f t" % (biomass / 1000.0)
	_det_fcr.text = "1.15"  # Mock

	# Water
	_det_temp.text = "%.1f°C" % _sim.temperature_c
	_det_oxygen.text = "91%"
	_det_current.text = "0.3 m/s"
	_det_visibility.text = "8.5 m"
	_det_algae.text = "Lav"
	_det_algae.add_theme_color_override("font_color", C_SUCCESS_D)

	# Health
	var lice: float = _sim.sea_lice_per_fish if _sim.get("sea_lice_per_fish") != null else 0.1
	_det_lice.text = "%.2f lus/fisk" % lice
	_det_lice.add_theme_color_override("font_color", C_SUCCESS_D if lice < 0.2 else (C_WARNING_D if lice < 0.5 else C_DANGER))
	_det_disease.text = "Ingen"
	_det_disease.add_theme_color_override("font_color", C_SUCCESS_D)
	_det_mortality.text = "0.05%"
	_det_mortality_total.text = "1.2%"

	# Genetics (mock for now)
	_det_gene_growth.text = "A+"
	_det_gene_growth.add_theme_color_override("font_color", C_SUCCESS_D)
	_det_gene_lice.text = "B"
	_det_gene_lice.add_theme_color_override("font_color", C_WARNING_D)
	_det_gene_disease.text = "A"
	_det_gene_disease.add_theme_color_override("font_color", C_ACCENT)
	_det_gene_fillet.text = "A+"
	_det_gene_fillet.add_theme_color_override("font_color", C_SUCCESS_D)

	# Status badge
	if lice >= 0.5:
		_detail_status_badge.text = "⚠ Forhøyet lusetall"
		_detail_status_badge.add_theme_color_override("font_color", C_WARNING_D)
	else:
		_detail_status_badge.text = "● Frisk"
		_detail_status_badge.add_theme_color_override("font_color", C_SUCCESS_D)

	# Button states
	_btn_feed.disabled = (fish == 0)
	_btn_delouse.disabled = (fish == 0 or lice < 0.2)
	_btn_harvest.disabled = (fish == 0 or avg_w < 3.5)

func _show_no_pen() -> void:
	_no_pen_label.visible = true
	_detail_scroll.visible = false

func _show_detail() -> void:
	_no_pen_label.visible = false
	_detail_scroll.visible = true

# ── Actions ───────────────────────────────────────────────────────────────────

func _select_pen(pen_id: String) -> void:
	_selected_pen_id = pen_id
	pen_selected.emit(pen_id)
	_refresh()

func _on_feed() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("feed_pen"):
		_sim.feed_pen(_selected_pen_id, 100.0)
	action_requested.emit("feed", _selected_pen_id)

func _on_delouse() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("delouse_pen"):
		_sim.delouse_pen(_selected_pen_id, "mechanical")
	action_requested.emit("delouse", _selected_pen_id)

func _on_harvest() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("harvest_pen"):
		_sim.harvest_pen(_selected_pen_id)
	action_requested.emit("harvest", _selected_pen_id)

func _on_pen_action_completed(action: String, pen_id: String, _success: bool) -> void:
	_refresh()

# ── Helpers ───────────────────────────────────────────────────────────────────

func _fmt_num(n: int) -> String:
	var s := str(abs(n))
	var result := ""
	var count := 0
	for i in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = " " + result
		result = s[i] + result
		count += 1
	return ("-" + result) if n < 0 else result
