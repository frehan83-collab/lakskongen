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
## F2-05: the site's standing orders, under its actions.
var _driftsplan_card: DriftsplanCard = null

# ── Scene refs ────────────────────────────────────────────────────────────────
## Nets at or above this integrity have nothing to repair.
const NETS_WHOLE_PCT := 99.5

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

@onready var _btn_delouse: Button = %BtnDelouse
@onready var _btn_harvest: Button = %BtnHarvest
## U2-05: the site's last harvest report (shown once the site has one).
@onready var _btn_harvest_report: Button = %BtnHarvestReport
@onready var _btn_repair: Button = %BtnRepair
@onready var _strain_option: OptionButton = %StrainOption
@onready var _btn_stock: Button = %BtnStock
@onready var _btn_skirt: Button = %BtnSkirt
@onready var _btn_deterrent: Button = %BtnDeterrent
@onready var _delouse_option: OptionButton = %DelouseOption
@onready var _btn_rov: Button = %BtnROV
# S2-01b: the site's pens and the stocking of one pen.
@onready var _stock_title: Label = %StockTitle
@onready var _pen_chips: HFlowContainer = %PenChips
@onready var _pen_option: OptionButton = %PenOption
@onready var _count_spin: SpinBox = %CountSpin
@onready var _stock_preview: Label = %StockPreview
# U2-04: the site's feeding plan (ration, recipe, window, 30-day projection).
@onready var _feed_plan: FeedPlanCard = %FeedPlanCard

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
	_btn_harvest.pressed.connect(_on_harvest)
	_btn_harvest_report.pressed.connect(_on_harvest_report)
	_btn_repair.pressed.connect(_on_repair)
	_btn_stock.pressed.connect(_on_stock)
	_btn_skirt.pressed.connect(_on_toggle_mitigation.bind("skirt"))
	_btn_deterrent.pressed.connect(_on_toggle_mitigation.bind("deterrent"))
	_btn_rov.pressed.connect(_on_rov)
	_pen_option.item_selected.connect(func(_i: int) -> void: _update_stock_preview())
	_count_spin.value_changed.connect(func(_v: float) -> void: _update_stock_preview())
	_strain_option.item_selected.connect(func(_i: int) -> void: _update_stock_preview())
	_populate_strains()
	_build_quality_selector()
	_populate_quality_tiers()
	_populate_treatments()
	_mount_driftsplan_card()
	_attach_button_juice()
	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_left_title.text = "🐟 " + tr("UI_PEN_LIST_TITLE")
	_no_pen_label.text = tr("UI_PEN_SELECT_HINT")
	_no_pen_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_water_title.text = "🌊 " + tr("UI_PEN_ENVIRONMENT")
	_health_title.text = "💀 " + tr("UI_PEN_HEALTH")
	_gene_title.text = "🧬 " + tr("UI_PEN_GENETICS")
	_btn_delouse.text = "🧪 " + tr("UI_PEN_DELOUSE")
	_btn_harvest.text = "📦 " + tr("UI_PEN_HARVEST")
	_btn_repair.text = "🔧 " + tr("UI_PEN_REPAIR")
	_btn_stock.text = "🐣 " + tr("UI_PEN_STOCK") % SimFormatters.integer(STOCK_BATCH)
	_btn_stock.tooltip_text = tr("UI_PEN_STOCK_TIP") % SimFormatters.integer(STOCK_BATCH)
	_stock_title.text = "🐟 " + tr("PENS_TITLE")
	_pen_option.tooltip_text = tr("PENS_PEN_PICK_TIP")
	_delouse_option.tooltip_text = tr("UI_PEN_DELOUSE_METHOD_TIP")
	_btn_skirt.tooltip_text = tr("UI_PEN_SKIRT_TIP")
	_btn_deterrent.tooltip_text = tr("UI_PEN_DETERRENT_TIP")
	_btn_rov.tooltip_text = tr("UI_PEN_ROV_TIP")

## Juice-only: press feedback on the three detail action buttons.
func _attach_button_juice() -> void:
	for btn in [_btn_delouse, _btn_harvest, _btn_harvest_report, _btn_repair, _btn_stock, _btn_skirt, _btn_deterrent, _btn_rov]:
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
		lbl.text = tr("UI_PEN_LIST_EMPTY_GUIDE")
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
		btn.text = "%s\n%s" % [str(pen.get("name", pen_id)),
			tr("UI_PEN_LIST_ROW") % [SimFormatters.integer(fish), SimFormatters.num(biomass / 1000.0, 1)]]
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
	_detail_subtitle.text = tr("UI_PEN_SUBTITLE") % [
		SimLabels.zone(str(pen.get("zone", ""))),
		SimFormatters.percent((biomass / max_bio) if max_bio > 0 else 0.0),
		SimFormatters.percent(integrity / 100.0)
	]

	_det_count.text = SimFormatters.integer(fish)
	_det_weight.text = SimFormatters.format_fish_weight(avg_w) if fish > 0 else "–"
	_det_biomass.text = SimFormatters.num(biomass / 1000.0, 0) + " t"
	_det_fcr.text = SimFormatters.num(float(_sim.fcr), 2)

	# Water (live where simulated)
	_det_temp.text = SimFormatters.num(_sim.temperature_c, 1) + " °C"
	var o2: float = float(pen.get("oxygen_mgl", _sim.oxygen_mgl))
	_det_oxygen.text = SimFormatters.num(o2, 1) + " mg/L" if o2 == o2 and o2 > 0.0 else "–"
	_det_current.text = SimFormatters.percent(float(_sim.appetite))
	# Visibility and algae growth are not simulated per site: say so instead
	# of showing invented values ("8.5 m", "Lav").
	_det_visibility.text = tr("UI_NOT_MEASURED")
	_det_algae.text = tr("UI_NOT_MEASURED")
	LakskongenTheme.set_text_color(_det_visibility, C_MUTED)
	LakskongenTheme.set_text_color(_det_algae, C_MUTED)

	# Health: this site's lice (its weekly count, as on the 3D collar), not
	# the company average shown for every site before.
	var lice: float = float(pen.get("lice_per_fish", 0.0))
	_det_lice.text = tr("UI_PEN_LICE_VALUE") % SimFormatters.num(lice, 2)
	LakskongenTheme.set_text_color(_det_lice, C_SUCCESS_D if lice < 0.2 else (C_WARNING_D if lice < 0.5 else C_DANGER))
	# Resistance: worst chemical track (Phase 2.2) — overuse bites back.
	var max_res := 0.0
	if _sim and _sim.has_method("get_resistance_levels"):
		var res_lvls: Dictionary = _sim.get_resistance_levels()
		for k in res_lvls:
			max_res = max(max_res, float(res_lvls[k]))
	_det_disease.text = SimFormatters.percent(max_res)
	_det_disease.add_theme_color_override("font_color",
		C_SUCCESS_D if max_res < 0.2 else (C_WARNING_D if max_res < 0.5 else C_DANGER))
	var mort: float = float(_sim.mortality_rate)
	_det_mortality.text = SimFormatters.percent(mort, 2) if mort == mort else "–"
	# Cumulative mortality and per-trait genetics are not tracked per site yet:
	# no invented grades ("A+", "1.2%").
	_det_mortality_total.text = tr("UI_NOT_MEASURED")
	LakskongenTheme.set_text_color(_det_mortality_total, C_MUTED)
	for gene_lbl in [_det_gene_growth, _det_gene_lice, _det_gene_disease, _det_gene_fillet]:
		gene_lbl.text = tr("UI_NOT_MEASURED")
		LakskongenTheme.set_text_color(gene_lbl, C_MUTED)

	# Status badge
	if integrity < 40.0:
		_detail_status_badge.text = "🚨 " + tr("UI_PEN_BADGE_NETS")
		LakskongenTheme.set_text_color(_detail_status_badge, C_DANGER)
	elif lice >= 0.5:
		_detail_status_badge.text = "⚠ " + tr("UI_PEN_BADGE_LICE")
		LakskongenTheme.set_text_color(_detail_status_badge, C_WARNING_D)
	else:
		_detail_status_badge.text = "● " + tr("UI_PEN_BADGE_HEALTHY")
		LakskongenTheme.set_text_color(_detail_status_badge, C_SUCCESS_D)

	# Button states
	UIPolish.set_available(_btn_delouse, _delouse_blocker(fish, lice))
	UIPolish.set_available(_btn_harvest, _harvest_blocker(fish))
	_update_harvest_button()
	UIPolish.set_available(_btn_repair,
		tr("UI_WHY_NETS_WHOLE") % SimFormatters.percent(integrity / 100.0) if integrity >= NETS_WHOLE_PCT else "")
	# Mitigation toggles (Phase 1.5) reflect live site state.
	var has_skirt: bool = bool(pen.get("has_skirt", false))
	var has_det: bool = bool(pen.get("has_deterrent", false))
	_btn_skirt.text = ("🔛 " if has_skirt else "⚪ ") + tr("UI_PEN_SKIRT")
	_btn_deterrent.text = ("🔛 " if has_det else "⚪ ") + tr("UI_PEN_DETERRENT")
	_refresh_pens()
	_feed_plan.show_site(_selected_pen_id, str(pen.get("name", _selected_pen_id)))

# ── Pens and stocking (S2-01b) ────────────────────────────────────────────────

## The site's pens as chips (number, fish, density) and the empty pens in the
## stocking picker; the picked pen survives a refresh while it stays empty.
func _refresh_pens() -> void:
	var pens: Array = _sim.get_site_pens(_selected_pen_id) if _sim and _sim.has_method("get_site_pens") else []
	for child in _pen_chips.get_children():
		child.queue_free()
	var keep := _selected_stock_pen_id()
	_pen_option.clear()
	for pen in pens:
		var number := int(pen.get("number", 0))
		var chip := Label.new()
		chip.theme_type_variation = &"SmallMuted"
		if bool(pen.get("occupied", false)):
			chip.text = tr("PENS_CHIP_FISH") % [number, SimFormatters.integer(int(pen.get("fish_count", 0))),
				SimFormatters.num(float(pen.get("density_kg_m3", 0.0)), 1)]
		else:
			chip.text = tr("PENS_CHIP_EMPTY") % number
			_pen_option.add_item(tr("PENS_OPTION_FREE") % [number,
				SimFormatters.integer(int(round(float(pen.get("volume_m3", 0.0)))))])
			_pen_option.set_item_metadata(_pen_option.item_count - 1, str(pen.get("id", "")))
		_pen_chips.add_child(chip)
	for i in _pen_option.item_count:
		if str(_pen_option.get_item_metadata(i)) == keep:
			_pen_option.select(i)
	_update_stock_preview()

## Id of the pen picked for stocking ("" when no empty pen).
func _selected_stock_pen_id() -> String:
	if _pen_option.item_count == 0 or _pen_option.selected < 0:
		return ""
	return str(_pen_option.get_item_metadata(_pen_option.selected))

## What stocking the chosen count in the chosen pen would do, from the engine's
## own check (the 200,000-fish and 25 kg/m³ limits, MTB): the plan's peak and
## the room, or the reason it is refused, which also disables the button.
func _update_stock_preview() -> void:
	if not _sim or _selected_pen_id.is_empty():
		return
	var count := int(_count_spin.value)
	_btn_stock.text = "🐣 " + tr("UI_PEN_STOCK") % SimFormatters.integer(count)
	# UIPolish.set_available shows this tooltip whenever the button is enabled.
	_btn_stock.set_meta(&"enabled_tooltip", tr("UI_PEN_STOCK_TIP") % SimFormatters.integer(count))
	var pen_id := _selected_stock_pen_id()
	if pen_id.is_empty():
		_stock_preview.text = tr("PENS_NONE_FREE")
		UIPolish.set_available(_btn_stock, tr("PENS_NONE_FREE"))
		return
	var strain_id := _strain_ids[clampi(_strain_option.selected, 0, _strain_ids.size() - 1)]
	var tier := maxi(0, _quality_option.selected) if _quality_option != null else 0
	var p: Dictionary = _sim.preview_pen_stocking(_selected_pen_id, pen_id, count, strain_id, tier)
	_count_spin.tooltip_text = tr("PENS_COUNT_TIP") % [SimFormatters.integer(int(p.get("max_fish_per_pen", 0))),
		SimFormatters.num(float(p.get("density_limit_kg_m3", 0.0)), 0)]
	var reason := ""
	if not bool(p.get("allowed", false)):
		var key := str(p.get("label_key", ""))
		reason = tr(key) if not key.is_empty() else str(p.get("message", ""))
	_stock_preview.text = tr("PENS_PREVIEW") % [
		SimFormatters.num(float(p.get("projected_peak_density_kg_m3", 0.0)), 1),
		SimFormatters.num(float(p.get("density_limit_kg_m3", 0.0)), 0),
		SimFormatters.num(float(p.get("projected_peak_weight_kg", 0.0)), 1),
		SimFormatters.integer(int(p.get("max_count", 0)))] + ("" if reason.is_empty() else " · " + reason)
	UIPolish.set_available(_btn_stock, reason)

## Why the pen cannot be deloused now ("" when it can).
func _delouse_blocker(fish: int, lice: float) -> String:
	if fish == 0:
		return tr("UI_WHY_NO_FISH")
	if lice < SimState.LICE_TREATMENT_LIMIT:
		return tr("UI_WHY_LICE_BELOW") % [SimFormatters.num(lice, 2), SimFormatters.num(SimState.LICE_TREATMENT_LIMIT, 2)]
	return ""

## Why the site's harvest planner has nothing to plan ("" when it has fish).
## Small fish open it too: the planner prices them and says they are small
## (an emergency harvest is a real decision, U2-05).
func _harvest_blocker(fish: int) -> String:
	return tr("UI_WHY_NO_FISH") if fish == 0 else ""


## "Høst" opens the harvest planner (U2-05); with an order placed it reads
## "Slakteplan · bestilt" and opens the planner on the order.
func _update_harvest_button() -> void:
	var ordered := false
	if _sim and _sim.has_method("get_harvest_orders"):
		for o in _sim.get_harvest_orders():
			ordered = ordered or str(o.get("holding_id", "")) == _selected_pen_id
	_btn_harvest.text = "📦 " + (tr("HARVESTPLAN_BUTTON_ORDERED") if ordered else tr("UI_PEN_HARVEST"))
	_btn_harvest.set_meta(&"enabled_tooltip", tr("HARVESTPLAN_BUTTON_TIP"))
	_btn_harvest_report.text = tr("HARVESTPLAN_LAST_REPORT")
	_btn_harvest_report.visible = not HarvestReport.latest_key_for(_sim, _selected_pen_id).is_empty()

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
	if _driftsplan_card != null:
		_driftsplan_card.set_unit(pen_id, "fjord_site")
	# The 3D view follows the list (highlight, indicator, "Fly to pen").
	if is_inside_tree():
		get_tree().call_group(FJORD_VISTA_GROUP, "select_pen_from_ui", pen_id)
	_refresh()


## F2-05: the Driftsplan card goes right under the site's actions: the orders
## that do those actions by themselves.
func _mount_driftsplan_card() -> void:
	var actions: Control = _btn_delouse.get_parent()
	var detail_root := actions.get_parent()
	_driftsplan_card = DriftsplanCard.new()
	detail_root.add_child(_driftsplan_card)
	detail_root.move_child(_driftsplan_card, actions.get_index() + 1)


## The Driftsplan card (UI flow tests).
func driftsplan_card() -> DriftsplanCard:
	return _driftsplan_card


## Public selection API for 3D pen clicks (FjordVistaBridge).
func select_pen(pen_id: String) -> void:
	_select_pen(pen_id)


## Currently selected pen id (empty when none).
func get_selected_pen_id() -> String:
	return _selected_pen_id

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
			var tag := "🚢 " if bool(p.get("requires_wellboat", false)) else ""
			_delouse_option.add_item(tr("UI_PEN_TREATMENT_ITEM") % [tag + SimLabels.delousing(id),
				SimFormatters.percent(float(p.get("efficacy", 0.0))), SimFormatters.percent(float(p.get("mortality", 0.0)), 1)])
	if _treatment_ids.is_empty():
		_treatment_ids.append("Hydrolicer")
		_delouse_option.add_item(SimLabels.delousing("Hydrolicer"))

## "Høst" (U2-05): the harvest planner, whose default is the planned order
## (fast, wellboat slot, slaughterhouse capacity) and whose secondary verb is
## the express harvest today.
func _on_harvest() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	var planner := HarvestScreens.planner(get_tree())
	if planner != null:
		var site := str(_sim.site_name(_selected_pen_id)) if _sim.has_method("site_name") else _selected_pen_id
		planner.open_for(_selected_pen_id, site)
		if not planner.planner_closed.is_connected(_on_planner_closed):
			planner.planner_closed.connect(_on_planner_closed)
	action_requested.emit("harvest_plan", _selected_pen_id)


func _on_planner_closed(_action: String) -> void:
	_refresh()


## The site's last harvest report (U2-05), also after its pens are emptied.
func _on_harvest_report() -> void:
	if _selected_pen_id.is_empty():
		return
	var sheet := HarvestScreens.report(get_tree())
	if sheet != null:
		sheet.show_latest_for(_selected_pen_id)

func _on_repair() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	if _sim.has_method("repair_nets"):
		_sim.repair_nets(_selected_pen_id)
	action_requested.emit("repair_nets", _selected_pen_id)

## Strain picker (Phase 1.4): fixed batch per click, strain from dropdown.
const STOCK_BATCH := 10_000
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
	_quality_option.tooltip_text = tr("UI_PEN_QUALITY_TIP")
	parent.add_child(_quality_option)
	parent.move_child(_quality_option, _btn_stock.get_index())
	_quality_option.item_selected.connect(func(_i: int) -> void: _update_stock_preview())

## Populate quality tiers + tooltips describing expected carryover.
func _populate_quality_tiers() -> void:
	if _quality_option == null:
		return
	_quality_option.clear()
	var tiers: Array = []
	if _sim and _sim.has_method("get_smolt_quality_tiers"):
		tiers = _sim.get_smolt_quality_tiers()
	if tiers.is_empty():
		_quality_option.add_item(SimLabels.smolt_tier("Standard"))
		_quality_option.tooltip_text = tr("UI_PEN_QUALITY_NONE")
		return
	for q in tiers:
		var tier_name := SimLabels.smolt_tier(str(q.get("id", "")))
		_quality_option.add_item(tier_name)
		var idx := _quality_option.item_count - 1
		_quality_option.set_item_tooltip(idx, tr("UI_PEN_QUALITY_ITEM_TIP") % [tier_name,
			SimFormatters.num(float(q.get("mortality_mult", 1.0)), 2),
			SimFormatters.num(float(q.get("growth_mult", 1.0)), 2),
			SimFormatters.num(float(q.get("days_mult", 1.0)), 2)])

func _populate_strains() -> void:
	_strain_ids.clear()
	_strain_option.clear()
	if _sim and _sim.has_method("get_smolt_strains"):
		for s in _sim.get_smolt_strains():
			_strain_ids.append(str(s.get("id", "standard")))
			_strain_option.add_item(tr("UI_PEN_STRAIN_ITEM") % [
				str(s.get("name", "?")),
				SimFormatters.num(float(s.get("weight_g", 100.0)), 0),
				SimFormatters.num(float(s.get("cost_multiplier", 1.0)), 1)])
	if _strain_ids.is_empty():
		_strain_ids.append("standard")
		_strain_option.add_item(SimLabels.smolt_tier("Standard"))

## S2-01b: stock the chosen count of the chosen strain and quality in the
## chosen empty pen of the open site.
func _on_stock() -> void:
	if _selected_pen_id.is_empty() or not _sim:
		return
	var pen_id := _selected_stock_pen_id()
	if pen_id.is_empty():
		return
	var strain_id := _strain_ids[clampi(_strain_option.selected, 0, _strain_ids.size() - 1)]
	var quality_tier := maxi(0, _quality_option.selected) if _quality_option != null else 0
	_last_stock_count = int(_count_spin.value)
	# The verb's completion signal (and its toast) fires before it returns, so
	# the reason comes from the same check run first as a preview.
	var check: Dictionary = _sim.preview_pen_stocking(_selected_pen_id, pen_id, _last_stock_count, strain_id, quality_tier)
	_last_stock_reason = str(check.get("label_key", ""))
	_sim.stock_smolts_in_pen(_selected_pen_id, pen_id, _last_stock_count, strain_id, quality_tier)
	action_requested.emit("stock_smolts", _selected_pen_id)

## The count and refusal of the last stocking, for its toast.
var _last_stock_count := STOCK_BATCH
var _last_stock_reason := ""

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
	# Juice-only feedback: toast per action. A harvest's one toast (and its
	# report) belongs to ConsequenceDirector (U2-05: exactly one per harvest);
	# the panel only pulses. Simulation numbers are untouched.
	var site := str(_sim.site_name(pen_id)) if _sim and _sim.has_method("site_name") else pen_id
	if _success:
		match action:
			"delouse", "delousing":
				_toast("🧪 " + tr("UI_TOAST_DELOUSE_OK") % site, "SUCCESS")
			"harvest":
				_play_harvest_celebration()
			"repair_nets":
				_toast("🔧 " + tr("UI_TOAST_REPAIR_OK") % site, "SUCCESS")
			"stock_smolts":
				_toast("🐣 " + tr("UI_TOAST_STOCK_OK") % [SimFormatters.integer(_last_stock_count), site], "SUCCESS")
			"mitigation":
				_toast("🛡️ " + tr("UI_TOAST_MITIGATION_OK") % site, "SUCCESS")
			"wellboat":
				_toast("🚢 " + tr("UI_TOAST_WELLBOAT_OK") % site, "SUCCESS")
			"place_pen", "buy":
				_toast("🏗️ " + tr("UI_TOAST_BUY_PEN"), "SUCCESS")
			"sell":
				_toast("💰 " + tr("UI_TOAST_SELL_PEN") % site, "INFO")
			"feed_plan":
				if pen_id == "all_sites":
					_toast(tr("UI_TOAST_FEED_PLAN_ALL_OK"), "SUCCESS")
				else:
					_toast(tr("UI_TOAST_FEED_PLAN_OK") % site, "SUCCESS")
	elif action == "feed_plan":
		pass  # the card says why (FeedPlanCard._after_apply)
	elif action == "stock_smolts" and not _last_stock_reason.is_empty() and tr(_last_stock_reason) != _last_stock_reason:
		# S2-01b: say why the pen refused (200,000 fish, 25 kg/m³, MTB, money).
		_toast("⚠️ " + tr(_last_stock_reason), "WARNING")
	else:
		_toast("⚠️ " + tr("UI_TOAST_ACTION_FAILED") % [SimLabels.pen_action(action), site], "WARNING")

## Harvest celebration reusing the AchievementPopup/JuiceEffects pattern:
## double-pulse + income flash on the detail panel, no new systems.
func _play_harvest_celebration() -> void:
	var target: Control = _detail_scroll if is_instance_valid(_detail_scroll) else self
	JuiceEffects.double_pulse(target, 1.02, 0.5)
	JuiceEffects.flash_income(target)
