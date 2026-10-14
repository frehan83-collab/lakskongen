## PenManagement v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/PenManagement.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Pen list buttons are data-driven and still populated in code.

extends Control

signal pen_selected(pen_id: String)
signal action_requested(action: String, pen_id: String)

## FjordVistaBridge.GROUP: the 3D view's selection hook.
const FJORD_VISTA_GROUP := "fjord_vista"

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── State ─────────────────────────────────────────────────────────────────────
var _selected_pen_id: String = ""
var _pen_list_items: Dictionary = {}  # pen_id -> Button

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _left_title: Label = %LeftTitle
@onready var _pen_list_vbox: VBoxContainer = %PenList
@onready var _detail_scroll: ScrollContainer = %DetailScroll
@onready var _no_pen_label: Label = %NoPenLabel

@onready var _detail_name: Label = %DetailName
@onready var _detail_subtitle: Label = %DetailSubtitle
@onready var _detail_status_badge: Label = %DetailBadge

@onready var _det_count: Label = %DetCount
@onready var _det_weight: Label = %DetWeight
@onready var _det_biomass: Label = %DetBiomass
@onready var _det_fcr: Label = %DetFcr

@onready var _det_temp: Label = %DetTemp
@onready var _det_oxygen: Label = %DetOxygen
@onready var _det_current: Label = %DetCurrent
@onready var _det_visibility: Label = %DetVisibility
@onready var _det_algae: Label = %DetAlgae

@onready var _det_lice: Label = %DetLice
@onready var _det_disease: Label = %DetDisease
@onready var _det_mortality: Label = %DetMortality
@onready var _det_mortality_total: Label = %DetMortalityTotal

@onready var _det_gene_growth: Label = %DetGeneGrowth
@onready var _det_gene_lice: Label = %DetGeneLice
@onready var _det_gene_disease: Label = %DetGeneDisease
@onready var _det_gene_fillet: Label = %DetGeneFillet

@onready var _water_title: Label = %WaterTitle
@onready var _health_title: Label = %HealthTitle
@onready var _gene_title: Label = %GeneTitle

@onready var _btn_feed: Button = %BtnFeed
@onready var _btn_delouse: Button = %BtnDelouse
@onready var _btn_harvest: Button = %BtnHarvest
@onready var _btn_repair: Button = %BtnRepair
@onready var _strain_option: OptionButton = %StrainOption
@onready var _btn_stock: Button = %BtnStock
@onready var _btn_skirt: Button = %BtnSkirt
@onready var _btn_deterrent: Button = %BtnDeterrent
@onready var _delouse_option: OptionButton = %DelouseOption
@onready var _btn_rov: Button = %BtnROV

# ── Colors ────────────────────────────────────────────────────────────────────
const C_BG := Color("#F4F1EA")
const C_SURFACE := Color("#FFFFFF")
const C_ELEVATED := Color("#EDE9E0")
const C_TEXT := Color("#1C1E20")
const C_TEXT2 := Color("#5A6168")
const C_MUTED := Color("#5A6168")
const C_BORDER := Color("#E7E2D7")
const C_PRIMARY := Color("#1F5E73")
const C_SECONDARY := Color("#1F5E73")
const C_SUCCESS := Color("#2E6B45")
const C_SUCCESS_D := Color("#2E6B45")
const C_WARNING := Color("#8A5A00")
const C_WARNING_D := Color("#8A5A00")
const C_DANGER := Color("#B3172F")
const C_ACCENT := Color("#1F5E73")

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)
		if _sim.has_signal("pen_action_completed"):
			_sim.pen_action_completed.connect(_on_pen_action_completed)

	_apply_localization()
	_apply_theme_fonts()
	_btn_delouse.pressed.connect(_on_delouse)
	_btn_feed.pressed.connect(_on_feed)
	_btn_harvest.pressed.connect(_on_harvest)
	_btn_repair.pressed.connect(_on_repair)
	_btn_stock.pressed.connect(_on_stock)
	_btn_skirt.pressed.connect(_on_toggle_mitigation.bind("skirt"))
	_btn_deterrent.pressed.connect(_on_toggle_mitigation.bind("deterrent"))
	_btn_rov.pressed.connect(_on_rov)
	_populate_strains()
	_build_quality_selector()
	_populate_quality_tiers()
	_populate_treatments()
	_attach_button_juice()
	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_left_title.text = tr("🐟 Merder")
	_no_pen_label.text = _tr_or("UI_PEN_SELECT_HINT",
		"🐟 Velg en merd\nVelg en merd i listen til venstre for å se detaljer, fôre, avluse eller høste.")
	_no_pen_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_water_title.text = tr("🌊 Miljø")
	_health_title.text = tr("💀 Helse")
	_gene_title.text = tr("🧬 Genetikk")
	_btn_delouse.text = tr("🧪 Avlus")
	_btn_feed.text = tr("🐟 Fôr")
	_btn_harvest.text = tr("📦 Høst")
	_btn_repair.text = tr("🔧 Reparer nøter")
	_btn_stock.text = tr("🐣 Sett ut 10k")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

## Juice-only: press feedback on the three detail action buttons.
func _attach_button_juice() -> void:
	for btn in [_btn_feed, _btn_delouse, _btn_harvest, _btn_repair, _btn_stock, _btn_skirt, _btn_deterrent, _btn_rov]:
		if btn is BaseButton:
			JuiceEffects.attach_press_feedback(btn)

## Juice-only toast helper; never touches simulation state.
func _toast(message: String, level: String = "INFO") -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, 4.0)

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	LakskongenTheme.set_strong(_detail_name)
	LakskongenTheme.set_strong(_det_count)
	LakskongenTheme.set_strong(_det_weight)
	LakskongenTheme.set_strong(_det_biomass)
	LakskongenTheme.set_strong(_det_fcr)
	LakskongenTheme.set_strong(_left_title)
	LakskongenTheme.set_strong(_water_title)
	LakskongenTheme.set_strong(_health_title)
	LakskongenTheme.set_strong(_gene_title)

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
		lbl.text = _tr_or("UI_PEN_LIST_EMPTY_GUIDE",
			"🐟 Ingen merder ennå\nKjøp din første merd, eller velg en produksjonssone på kartet for å komme i gang.")
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		LakskongenTheme.set_text_color(lbl, C_MUTED)
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

		# A selectable row (theme "ListItemButton"): the open pen shows pressed.
		btn.theme_type_variation = &"ListItemButton"
		btn.toggle_mode = true
		btn.set_pressed_no_signal(is_selected)
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
	var integrity: float = float(pen.get("net_integrity", 100.0))

	_detail_name.text = pen.get("name", _selected_pen_id)
	_detail_subtitle.text = "Sone: %s · Kapasitet: %.0f%% · Nøter: %.0f%%" % [
		pen.get("zone", "Ukjent"),
		(biomass / max_bio * 100.0) if max_bio > 0 else 0.0,
		integrity
	]

	_det_count.text = _fmt_num(fish)
	_det_weight.text = "%.1f kg" % avg_w
	_det_biomass.text = "%.0f t" % (biomass / 1000.0)
	_det_fcr.text = "%.2f" % float(_sim.fcr)

	# Water (live where simulated)
	_det_temp.text = "%.1f°C" % _sim.temperature_c
	var o2: float = float(pen.get("oxygen_mgl", _sim.oxygen_mgl))
	_det_oxygen.text = "%.1f mg/L" % o2 if o2 == o2 and o2 > 0.0 else "–"
	_det_current.text = "%d%%" % int(float(_sim.appetite) * 100.0)
	_det_visibility.text = tr("8.5 m")
	_det_algae.text = tr("Lav")
	LakskongenTheme.set_text_color(_det_algae, C_SUCCESS_D)

	# Health: this site's lice (its weekly count, as on the 3D collar), not
	# the company average shown for every site before.
	var lice: float = float(pen.get("lice_per_fish", 0.0))
	_det_lice.text = "%.2f lus/fisk" % lice
	LakskongenTheme.set_text_color(_det_lice, C_SUCCESS_D if lice < 0.2 else (C_WARNING_D if lice < 0.5 else C_DANGER))
	_det_disease.text = tr("Ingen")
	LakskongenTheme.set_text_color(_det_disease, C_SUCCESS_D)
	# Resistance: worst chemical track (Phase 2.2) — overuse bites back.
	var max_res := 0.0
	if _sim and _sim.has_method("get_resistance_levels"):
		var res_lvls: Dictionary = _sim.get_resistance_levels()
		for k in res_lvls:
			max_res = max(max_res, float(res_lvls[k]))
	_det_disease.text = "%.0f%%" % (max_res * 100.0)
	_det_disease.add_theme_color_override("font_color",
		C_SUCCESS_D if max_res < 0.2 else (C_WARNING_D if max_res < 0.5 else C_DANGER))
	var mort: float = float(_sim.mortality_rate)
	_det_mortality.text = "%.2f%%" % (mort * 100.0) if mort == mort else "–"
	_det_mortality_total.text = "1.2%"

	# Genetics (mock for now)
	_det_gene_growth.text = tr("A+")
	LakskongenTheme.set_text_color(_det_gene_growth, C_SUCCESS_D)
	_det_gene_lice.text = tr("B")
	LakskongenTheme.set_text_color(_det_gene_lice, C_WARNING_D)
	_det_gene_disease.text = tr("A")
	LakskongenTheme.set_text_color(_det_gene_disease, C_ACCENT)
	_det_gene_fillet.text = tr("A+")
	LakskongenTheme.set_text_color(_det_gene_fillet, C_SUCCESS_D)

	# Status badge
	if integrity < 40.0:
		_detail_status_badge.text = tr("🚨 Nøter kritisk — rømmingsfare")
		LakskongenTheme.set_text_color(_detail_status_badge, C_DANGER)
	elif lice >= 0.5:
		_detail_status_badge.text = tr("⚠ Forhøyet lusetall")
		LakskongenTheme.set_text_color(_detail_status_badge, C_WARNING_D)
	else:
		_detail_status_badge.text = tr("● Frisk")
		LakskongenTheme.set_text_color(_detail_status_badge, C_SUCCESS_D)

	# Button states
	_btn_feed.disabled = (fish == 0)
	_btn_delouse.disabled = (fish == 0 or lice < 0.2)
	_btn_harvest.disabled = (fish == 0 or avg_w < 3.5)
	_btn_repair.disabled = integrity >= 99.5
	# Mitigation toggles (Phase 1.5) reflect live site state.
	var has_skirt: bool = bool(pen.get("has_skirt", false))
	var has_det: bool = bool(pen.get("has_deterrent", false))
	_btn_skirt.text = ("🔛 " if has_skirt else "⚪ ") + tr("Luseskjørt")
	_btn_deterrent.text = ("🔛 " if has_det else "⚪ ") + tr("Seldeterrent")

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
	# The 3D view follows the list (highlight, indicator, "Fly to pen").
	if is_inside_tree():
		get_tree().call_group(FJORD_VISTA_GROUP, "select_pen_from_ui", pen_id)
	_refresh()


## Public selection API for 3D pen clicks (FjordVistaBridge).
func select_pen(pen_id: String) -> void:
	_select_pen(pen_id)


## Currently selected pen id (empty when none).
func get_selected_pen_id() -> String:
	return _selected_pen_id

func _on_feed() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("feed_pen"):
		_sim.feed_pen(_selected_pen_id, 100.0)
	action_requested.emit("feed", _selected_pen_id)

func _on_delouse() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	var idx := clampi(_delouse_option.selected, 0, _treatment_ids.size() - 1)
	var method_id := _treatment_ids[idx]
	# Wellboat methods (Thermolicer/Hydrolicer/…) take days via spot-hire;
	# the lice keep growing meanwhile — time it before the 0.5 limit.
	# FreshwaterBath gets its own RO boat (zero resistance, AGD included).
	if method_id == "FreshwaterBath":
		if _sim.has_method("request_wellboat"):
			_sim.request_wellboat(_selected_pen_id, "freshwater")
		action_requested.emit("wellboat", _selected_pen_id)
		return
	if bool(_treatment_wellboat.get(method_id, false)):
		if _sim.has_method("request_wellboat"):
			_sim.request_wellboat(_selected_pen_id, "delouse", method_id)
		action_requested.emit("wellboat", _selected_pen_id)
		return
	if _sim.has_method("delouse_pen"):
		_sim.delouse_pen(_selected_pen_id, method_id)
	action_requested.emit("delouse", _selected_pen_id)

## Treatment cards (Phase 2.2): id + efficacy/mortality per profile.
var _treatment_ids: Array[String] = ["Hydrolicer"]
var _treatment_wellboat: Dictionary = {}

func _populate_treatments() -> void:
	_treatment_ids.clear()
	_treatment_wellboat.clear()
	_delouse_option.clear()
	if _sim and _sim.has_method("get_treatment_profiles"):
		for p in _sim.get_treatment_profiles():
			var id := str(p.get("id", "?"))
			_treatment_ids.append(id)
			_treatment_wellboat[id] = bool(p.get("requires_wellboat", false))
			var tag := "🚢" if bool(p.get("requires_wellboat", false)) else ""
			_delouse_option.add_item("%s%s %.0f%%/%.1f%%" % [
				tag, id, float(p.get("efficacy", 0.0)) * 100.0, float(p.get("mortality", 0.0)) * 100.0])
	if _treatment_ids.is_empty():
		_treatment_ids.append("Hydrolicer")
		_delouse_option.add_item("Hydrolicer")

func _on_harvest() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("harvest_pen"):
		_sim.harvest_pen(_selected_pen_id)
	action_requested.emit("harvest", _selected_pen_id)

func _on_repair() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("repair_nets"):
		_sim.repair_nets(_selected_pen_id)
	action_requested.emit("repair_nets", _selected_pen_id)

## Strain picker (Phase 1.4): fixed 10k batch per click, strain from dropdown.
var _strain_ids: Array[String] = ["standard"]

# P5 smolt-quality picker: tier index equals the StockSmolts quality value
# (0=Standard, 1=Large, 2=SuperSmolt), matching get_smolt_quality_tiers() order.
var _quality_option: OptionButton = null

## Build the quality OptionButton in code and place it just before the stock button.
func _build_quality_selector() -> void:
	if _btn_stock == null:
		return
	var parent: Node = _btn_stock.get_parent()
	if parent == null:
		return
	_quality_option = OptionButton.new()
	_quality_option.name = "SmoltQualityOption"
	LakskongenTheme.set_size(_quality_option, 12)
	_quality_option.tooltip_text = tr("Smoltkvalitet — forventet bæreevne i sjøfasen")
	parent.add_child(_quality_option)
	parent.move_child(_quality_option, _btn_stock.get_index())

## Populate quality tiers + tooltips describing expected carryover.
func _populate_quality_tiers() -> void:
	if _quality_option == null:
		return
	_quality_option.clear()
	var tiers: Array = []
	if _sim and _sim.has_method("get_smolt_quality_tiers"):
		tiers = _sim.get_smolt_quality_tiers()
	if tiers.is_empty():
		_quality_option.add_item(tr("Standard S1"))
		_quality_option.tooltip_text = tr("Smoltkvalitet utilgjengelig")
		return
	for q in tiers:
		_quality_option.add_item(tr(str(q.get("name", "?"))))
		var idx := _quality_option.item_count - 1
		_quality_option.set_item_tooltip(idx, "%s — dødelighet ×%.2f, vekst ×%.2f, dager til slakt ×%.2f" % [
			tr(str(q.get("name", "?"))),
			float(q.get("mortality_mult", 1.0)),
			float(q.get("growth_mult", 1.0)),
			float(q.get("days_mult", 1.0))])

func _populate_strains() -> void:
	_strain_ids.clear()
	_strain_option.clear()
	if _sim and _sim.has_method("get_smolt_strains"):
		for s in _sim.get_smolt_strains():
			_strain_ids.append(str(s.get("id", "standard")))
			_strain_option.add_item("%s (%.0fg, ×%.1f)" % [
				str(s.get("name", "?")),
				float(s.get("weight_g", 100.0)),
				float(s.get("cost_multiplier", 1.0))])
	if _strain_ids.is_empty():
		_strain_ids.append("standard")
		_strain_option.add_item("Standard S1")

func _on_stock() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	var strain_id := _strain_ids[clampi(_strain_option.selected, 0, _strain_ids.size() - 1)]
	var quality_tier := maxi(0, _quality_option.selected) if _quality_option != null else 0
	if _sim.has_method("stock_smolts"):
		_sim.stock_smolts(_selected_pen_id, 10000, strain_id, quality_tier)
	action_requested.emit("stock_smolts", _selected_pen_id)

func _on_toggle_mitigation(kind: String) -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("toggle_mitigation"):
		_sim.toggle_mitigation(_selected_pen_id, kind)
	action_requested.emit("mitigation", _selected_pen_id)

func _on_rov() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("request_wellboat"):
		_sim.request_wellboat(_selected_pen_id, "rov")
	action_requested.emit("wellboat", _selected_pen_id)

func _on_pen_action_completed(action: String, pen_id: String, _success: bool) -> void:
	_refresh()
	# Juice-only feedback: toast per action, celebration on harvest.
	# Simulation numbers are untouched; this only reads the completion signal.
	if _success:
		match action:
			"feed":
				_toast(_tr_or("UI_TOAST_FEED_OK", "🐟 Merd %s fôret") % pen_id, "SUCCESS")
			"delouse", "delousing":
				_toast(_tr_or("UI_TOAST_DELOUSE_OK", "🧪 Avlusing bestilt for merd %s") % pen_id, "SUCCESS")
			"harvest":
				_toast(_tr_or("UI_TOAST_HARVEST_OK", "📦 Høsting fullført i merd %s! 🎉") % pen_id, "SUCCESS")
				_play_harvest_celebration()
			"repair_nets":
				_toast(_tr_or("UI_TOAST_REPAIR_OK", "🔧 Nøter reparert i %s (100%)") % pen_id, "SUCCESS")
			"stock_smolts":
				_toast(_tr_or("UI_TOAST_STOCK_OK", "🐣 10 000 smolt satt ut i %s") % pen_id, "SUCCESS")
			"mitigation":
				_toast(_tr_or("UI_TOAST_MITIGATION_OK", "🛡️ Tiltak oppdatert i %s") % pen_id, "SUCCESS")
			"wellboat":
				_toast(_tr_or("UI_TOAST_WELLBOAT_OK", "🚢 Brønnbåt bestilt til %s") % pen_id, "SUCCESS")
			"place_pen", "buy":
				_toast(_tr_or("UI_TOAST_BUY_PEN", "🏗️ Ny merd kjøpt — lykke til!"), "SUCCESS")
			"sell":
				_toast(_tr_or("UI_TOAST_SELL_PEN", "💰 Merd %s solgt") % pen_id, "INFO")
	else:
		_toast(_tr_or("UI_TOAST_ACTION_FAILED", "⚠️ %s feilet for merd %s") % [action.capitalize(), pen_id], "WARNING")

## Harvest celebration reusing the AchievementPopup/JuiceEffects pattern:
## double-pulse + income flash on the detail panel, no new systems.
func _play_harvest_celebration() -> void:
	var target: Control = _detail_scroll if is_instance_valid(_detail_scroll) else self
	JuiceEffects.double_pulse(target, 1.02, 0.5)
	JuiceEffects.flash_income(target)

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
