class_name FeedPlanCard
extends PanelContainer

## The "Fôringsplan" card of the Pens tab (roadmap U2-04): how much of their
## appetite a site's fish are fed (a slider with three stops: restricted at
## the best conversion, moderate, full appetite), the recipe its barge
## carries and for how long, with the next 30 days of the plan projected by
## the engine (twin-lite: growth, FCR, feed cost per day and per kg of
## growth, against full appetite on the standard feed). "Bruk" sets the
## site's plan, "Fôr alle" every site's, "Tilbake til standard" clears it.
## Layout: scenes/ui/FeedPlanCard.tscn; this script is wiring and refresh.

## Plan windows the card offers (days; 0 = until changed).
const WINDOWS := [0, 14, 30, 60, 90]
## Recipe ids in the order the picker lists them (GameBridge.FeedPlans.cs).
const RECIPES := ["standard", "high_energy", "economy"]
## A change of less than this share against full appetite reads as "no change".
const SAME_SHARE := 0.005

@onready var _title: Label = %PlanTitle
@onready var _status: Label = %PlanStatus
@onready var _ration_caption: Label = %RationCaption
@onready var _ration_slider: HSlider = %RationSlider
@onready var _ration_value: Label = %RationValue
@onready var _stop_buttons: Array[Button] = [%StopRestricted, %StopModerate, %StopFull]
@onready var _recipe_caption: Label = %RecipeCaption
@onready var _recipe_option: OptionButton = %RecipeOption
@onready var _window_caption: Label = %WindowCaption
@onready var _window_option: OptionButton = %WindowOption
@onready var _forecast: GridContainer = %Forecast
@onready var _note: Label = %ForecastNote
@onready var _btn_apply: Button = %BtnApplyPlan
@onready var _btn_apply_all: Button = %BtnApplyAll
@onready var _btn_reset: Button = %BtnResetPlan

var _sim: Node = null
var _site_id := ""
var _site_name := ""
var _options: Dictionary = {}
## The plan the site runs now (GameBridge.GetFeedPlan).
var _plan: Dictionary = {}
## True once the player moved a control since the plan was shown: a refresh
## keeps their unsaved choice instead of snapping back to the running plan.
var _editing := false
## The last projection shown (tests read it): {plan, baseline}.
var last_preview: Dictionary = {}
## Labels of the forecast grid: row key -> [name, plan value, change].
var _rows: Dictionary = {}


func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_ration_slider.value_changed.connect(_on_ration_changed)
	for i in _stop_buttons.size():
		_stop_buttons[i].pressed.connect(_on_stop_pressed.bind(i))
	_recipe_option.item_selected.connect(func(_i: int) -> void: _on_edited())
	_window_option.item_selected.connect(func(_i: int) -> void: _on_edited())
	_btn_apply.pressed.connect(_on_apply)
	_btn_apply_all.pressed.connect(_on_apply_all)
	_btn_reset.pressed.connect(_on_reset)
	_load_options()
	_apply_texts()
	_build_forecast_rows()


## U2-02: "Bruk på …" (the first season's pointer moves to it once a plan is chosen).
func apply_button() -> Button:
	return _btn_apply


## Show `site_id`'s plan (a new site drops any unsaved edit).
func show_site(site_id: String, site_name: String) -> void:
	if site_id != _site_id:
		_editing = false
	_site_id = site_id
	_site_name = site_name
	refresh()


## Re-read the site's plan and the projection (every snapshot refresh).
func refresh() -> void:
	if _sim == null or _site_id.is_empty():
		return
	_plan = _sim.get_feed_plan(_site_id)
	if not _editing:
		_show_plan_controls()
	_update_status()
	_update_preview()


# ── Setup ──────────────────────────────────────────────────────────────────────

func _load_options() -> void:
	_options = _sim.get_feed_plan_options() if _sim else {}
	_ration_slider.min_value = float(_options.get("min_ration_pct", 50.0))
	_ration_slider.max_value = 100.0
	_ration_slider.step = 1.0
	_recipe_option.clear()
	for recipe in _options.get("recipes", []):
		var id := str(recipe.get("id", "standard"))
		_recipe_option.add_item(tr("FEEDPLAN_RECIPE_OPTION") % [tr("FEEDPLAN_RECIPE_" + id.to_upper()),
			SimFormatters.format_price_kg(float(recipe.get("price_per_kg", 0.0)))])
		_recipe_option.set_item_metadata(_recipe_option.item_count - 1, id)
		_recipe_option.set_item_tooltip(_recipe_option.item_count - 1, tr("FEEDPLAN_RECIPE_TIP_" + id.to_upper()))
	_window_option.clear()
	for days in WINDOWS:
		_window_option.add_item(tr("FEEDPLAN_WINDOW_UNTIL_CHANGED") if days == 0 else tr("FEEDPLAN_WINDOW_DAYS") % days)
		_window_option.set_item_metadata(_window_option.item_count - 1, days)


func _apply_texts() -> void:
	_title.text = tr("FEEDPLAN_TITLE")
	_ration_caption.text = tr("FEEDPLAN_RATION")
	_recipe_caption.text = tr("FEEDPLAN_RECIPE")
	_window_caption.text = tr("FEEDPLAN_WINDOW")
	var stops := _stops()
	var names := ["FEEDPLAN_STOP_RESTRICTED", "FEEDPLAN_STOP_MODERATE", "FEEDPLAN_STOP_FULL"]
	for i in _stop_buttons.size():
		_stop_buttons[i].text = tr(names[i]) % SimFormatters.num(stops[i], 0)
		_stop_buttons[i].tooltip_text = tr(names[i] + "_TIP")
	_ration_slider.tooltip_text = tr("FEEDPLAN_RATION_TIP")
	_window_option.tooltip_text = tr("FEEDPLAN_WINDOW_TIP")
	_btn_reset.text = tr("FEEDPLAN_RESET")
	_btn_reset.set_meta(&"enabled_tooltip", tr("FEEDPLAN_RESET_TIP"))
	_note.text = tr("FEEDPLAN_NOTE") % int(_options.get("projection_days", 30))


## The forecast grid: a header row, then one row per figure.
func _build_forecast_rows() -> void:
	for child in _forecast.get_children():
		child.queue_free()
	_add_cell("", &"CaptionMuted")
	_add_cell(tr("FEEDPLAN_COL_PLAN"), &"CaptionMuted", HORIZONTAL_ALIGNMENT_RIGHT)
	_add_cell(tr("FEEDPLAN_COL_CHANGE"), &"CaptionMuted", HORIZONTAL_ALIGNMENT_RIGHT)
	var days := int(_options.get("projection_days", 30))
	for key in ["growth", "fcr", "cost", "cost_per_kg", "weight"]:
		var name := tr("FEEDPLAN_WEIGHT") % days if key == "weight" else tr("FEEDPLAN_" + key.to_upper())
		_rows[key] = [_add_cell(name, &"BodySmall"), _add_cell("–", &"Figure", HORIZONTAL_ALIGNMENT_RIGHT),
			_add_cell("", &"SmallMuted", HORIZONTAL_ALIGNMENT_RIGHT)]


func _add_cell(text: String, variation: StringName, align := HORIZONTAL_ALIGNMENT_LEFT) -> Label:
	var label := Label.new()
	label.text = text
	label.theme_type_variation = variation
	label.horizontal_alignment = align
	if align == HORIZONTAL_ALIGNMENT_LEFT:
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	else:
		label.custom_minimum_size.x = 120.0
	_forecast.add_child(label)
	return label


# ── Controls ───────────────────────────────────────────────────────────────────

## The three stops of the slider (% of appetite).
func _stops() -> Array[float]:
	return [float(_options.get("restricted_pct", 70.0)), float(_options.get("moderate_pct", 85.0)),
		float(_options.get("full_pct", 100.0))]


func _show_plan_controls() -> void:
	_ration_slider.set_value_no_signal(float(_plan.get("ration_pct", 100.0)))
	_select_meta(_recipe_option, str(_plan.get("recipe", "standard")))
	var days_left := int(_plan.get("days_left", -1))
	_select_window(days_left)
	_update_ration_label()


func _select_meta(option: OptionButton, value: Variant) -> void:
	for i in option.item_count:
		if option.get_item_metadata(i) == value:
			option.select(i)
			return
	option.select(0)


## A running plan with days left shows the nearest window at or above them.
func _select_window(days_left: int) -> void:
	if days_left < 0:
		_window_option.select(0)
		return
	for i in _window_option.item_count:
		var days := int(_window_option.get_item_metadata(i))
		if days > 0 and days >= days_left:
			_window_option.select(i)
			return
	_window_option.select(_window_option.item_count - 1)


func _on_ration_changed(_value: float) -> void:
	_on_edited()


func _on_stop_pressed(index: int) -> void:
	_ration_slider.value = _stops()[index]


func _on_edited() -> void:
	_editing = true
	_update_ration_label()
	_update_preview()


func _update_ration_label() -> void:
	var pct := _ration_slider.value
	_ration_value.text = tr("FEEDPLAN_RATION_VALUE") % SimFormatters.num(pct, 0)
	var stops := _stops()
	for i in _stop_buttons.size():
		_stop_buttons[i].set_pressed_no_signal(absf(pct - stops[i]) < 0.5)


## The plan the controls describe: {ration_pct, recipe, window}.
func chosen_plan() -> Dictionary:
	return {
		"ration_pct": _ration_slider.value,
		"recipe": str(_recipe_option.get_item_metadata(maxi(_recipe_option.selected, 0))),
		"window": int(_window_option.get_item_metadata(maxi(_window_option.selected, 0))),
	}


func _update_status() -> void:
	var standard := bool(_plan.get("standard", true))
	var days_left := int(_plan.get("days_left", -1))
	if standard:
		_status.text = tr("FEEDPLAN_STATUS_STANDARD")
		_status.theme_type_variation = &"TagMuted"
	elif days_left >= 0:
		_status.text = tr("FEEDPLAN_STATUS_DAYS_LEFT") % days_left
		_status.theme_type_variation = &"TagInfo"
	else:
		_status.text = tr("FEEDPLAN_STATUS_CUSTOM")
		_status.theme_type_variation = &"TagInfo"
	_btn_apply.text = tr("FEEDPLAN_APPLY") % _site_name
	_btn_apply.set_meta(&"enabled_tooltip", tr("FEEDPLAN_APPLY_TIP"))
	UIPolish.set_available(_btn_apply, "" if _differs_from_running(chosen_plan()) else tr("FEEDPLAN_WHY_IN_USE"))
	var sites: int = (_sim.sea_sites as Array).size() if _sim and "sea_sites" in _sim else 0
	_btn_apply_all.text = tr("FEEDPLAN_APPLY_ALL") % sites
	_btn_apply_all.set_meta(&"enabled_tooltip", tr("FEEDPLAN_APPLY_ALL_TIP"))
	UIPolish.set_available(_btn_apply_all, "" if sites > 0 else tr("FEEDPLAN_WHY_NO_SITES"))
	UIPolish.set_available(_btn_reset, tr("FEEDPLAN_WHY_STANDARD") if standard else "")


## True when applying the controls would change the site's feeding: another
## ration or recipe, or a window (which starts counting from today), or
## "until changed" for a plan that is running out.
func _differs_from_running(chosen: Dictionary) -> bool:
	if absf(float(chosen["ration_pct"]) - float(_plan.get("ration_pct", 100.0))) >= 0.5:
		return true
	if str(chosen["recipe"]) != str(_plan.get("recipe", "standard")):
		return true
	var window := int(chosen["window"])
	var days_left := int(_plan.get("days_left", -1))
	# A window counts from today: the same plan with as many days left is
	# the plan already running.
	return window != days_left if window > 0 else days_left >= 0


# ── Projection ─────────────────────────────────────────────────────────────────

func _update_preview() -> void:
	if _sim == null or _site_id.is_empty():
		return
	var chosen := chosen_plan()
	var plan: Dictionary = _sim.preview_feed_plan(_site_id, float(chosen["ration_pct"]), str(chosen["recipe"]), 0)
	var base: Dictionary = _sim.preview_feed_plan(_site_id, 100.0, "standard", 0)
	last_preview = {"plan": plan, "baseline": base}
	var fish := int(plan.get("fish", 0))
	if not bool(plan.get("ok", false)) or fish <= 0:
		for key in _rows:
			_rows[key][1].text = "–"
			_rows[key][2].text = ""
		_note.text = tr("FEEDPLAN_NO_FISH")
		_update_status()
		return
	_note.text = tr("FEEDPLAN_NOTE") % int(plan.get("days", 30))
	_set_row("growth", tr("FEEDPLAN_GROWTH_VALUE") % SimFormatters.num(float(plan["growth_kg_per_day"]), 0),
		_share_change(plan["growth_kg_per_day"], base["growth_kg_per_day"]), true)
	_set_row("fcr", SimFormatters.num(float(plan["fcr"]), 2),
		_abs_change(plan["fcr"], base["fcr"], 2), false)
	_set_row("cost", tr("FEEDPLAN_COST_VALUE") % (SimFormatters.num(float(plan["cost_nok_per_day"]), 0) + " NOK"),
		_share_change(plan["cost_nok_per_day"], base["cost_nok_per_day"]), false)
	_set_row("cost_per_kg", SimFormatters.format_price_kg(float(plan["cost_per_kg_growth"])),
		_share_change(plan["cost_per_kg_growth"], base["cost_per_kg_growth"]), false)
	_set_row("weight", SimFormatters.format_fish_weight(float(plan["end_mean_weight_kg"])),
		_weight_change(float(plan["end_mean_weight_kg"]), float(base["end_mean_weight_kg"])), true)
	_update_status()


## A figure and its change; `up_is_good` picks the change's tone.
func _set_row(key: String, value: String, change: Array, up_is_good: bool) -> void:
	_rows[key][1].text = value
	var label: Label = _rows[key][2]
	label.text = change[0]
	var sign: int = change[1]
	if sign == 0:
		label.theme_type_variation = &"SmallMuted"
	else:
		label.theme_type_variation = &"SmallOk" if (sign > 0) == up_is_good else &"SmallWarn"


## [text, sign] of a relative change ("−18 %"; "" and 0 when about the same).
func _share_change(value: Variant, base: Variant) -> Array:
	var b := float(base)
	if b <= 0.0:
		return ["", 0]
	var share := float(value) / b - 1.0
	if absf(share) < SAME_SHARE:
		return [tr("FEEDPLAN_SAME"), 0]
	return [("+" if share > 0.0 else "−") + SimFormatters.percent(absf(share)), 1 if share > 0.0 else -1]


## [text, sign] of a change of mean weight, in the unit the weight reads in
## (grams under a kilo, as SimFormatters.format_fish_weight).
func _weight_change(value: float, base: float) -> Array:
	var delta := value - base
	if absf(delta) < 0.0005:
		return [tr("FEEDPLAN_SAME"), 0]
	var sign := "+" if delta > 0.0 else "−"
	var text := SimFormatters.num(absf(delta) * 1000.0, 0) + " g" if absf(delta) < 1.0 \
		else SimFormatters.num(absf(delta), 2) + " kg"
	return [sign + text, 1 if delta > 0.0 else -1]


## [text, sign] of an absolute change ("−0,03").
func _abs_change(value: Variant, base: Variant, decimals: int, unit := "") -> Array:
	var delta := float(value) - float(base)
	if absf(delta) < pow(10.0, -decimals) * 0.5:
		return [tr("FEEDPLAN_SAME"), 0]
	return [("+" if delta > 0.0 else "−") + SimFormatters.num(absf(delta), decimals) + unit, 1 if delta > 0.0 else -1]


# ── Actions ────────────────────────────────────────────────────────────────────

func _on_apply() -> void:
	var c := chosen_plan()
	var result: Dictionary = _sim.set_feed_plan(_site_id, float(c["ration_pct"]), str(c["recipe"]), int(c["window"]))
	_after_apply(result)


func _on_apply_all() -> void:
	var c := chosen_plan()
	var result: Dictionary = _sim.set_feed_plan_all(float(c["ration_pct"]), str(c["recipe"]), int(c["window"]))
	_after_apply(result)


func _on_reset() -> void:
	_after_apply(_sim.set_feed_plan(_site_id, 100.0, "standard", 0))


func _after_apply(result: Dictionary) -> void:
	if bool(result.get("success", false)):
		_editing = false
	else:
		var key := "FEEDPLAN_ERROR_" + str(result.get("error_key", "")).to_upper()
		var mgr := get_node_or_null("/root/ToastNotification")
		if mgr:
			mgr.show_toast(tr(key) if tr(key) != key else str(result.get("message", "")), "WARNING", 5.0)
	refresh()
