## AIFeedingPanel v2.1 — Nordic Minimalist (scene-based).
## AI Feeding Dashboard UI
## Layout lives in scenes/ui/AIFeedingPanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.

extends PanelContainer
class_name AIFeedingPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var ai_feeding_header_label: Label = %AiTitle
@onready var calibration_status_label: Label = %CalibrationLabel
@onready var cameras_status_label: Label = %CamerasLabel
@onready var appetite_level_label: Label = %AppetiteLabel
@onready var fcr_savings_label: Label = %FcrLabel
@onready var install_ai_button: Button = %BtnInstallAi
@onready var protein_slider: HSlider = %ProteinSlider
@onready var replace_slider: HSlider = %ReplaceSlider
@onready var astax_slider: HSlider = %AstaxSlider
@onready var recipe_preview: Label = %RecipePreview
@onready var apply_recipe_button: Button = %BtnApplyRecipe

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _theme: Node = null
var _sim: Node = null

func _ready():
	_theme = get_node_or_null("/root/LakskongenTheme")
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("AIFeedingPanel: SimBridge not found — showing fallback data")

	_apply_localization()
	_apply_theme_fonts()
	install_ai_button.pressed.connect(_on_install_ai_pressed)
	protein_slider.value_changed.connect(_on_recipe_changed)
	replace_slider.value_changed.connect(_on_recipe_changed)
	astax_slider.value_changed.connect(_on_recipe_changed)
	apply_recipe_button.pressed.connect(_on_apply_recipe)

	refresh()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	ai_feeding_header_label.text = tr("UI_AIFEED_TITLE")
	install_ai_button.text = tr("UI_AIFEED_INSTALL")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	LakskongenTheme.set_strong(ai_feeding_header_label)

func refresh():
	# Refresh UI with live data from SimBridge (fallback to placeholders offline)
	_update_calibration_status()
	_update_cameras_status()
	_update_appetite_level()
	_update_fcr_savings()
	_sync_recipe_sliders()
	_update_recipe_preview()


## Feed recipe (Phase 4.4): sliders → preview → apply. Preview is computed
## locally with the same formula as the engine (cost 12-19, FCR ×0.90-1.0).
func _sync_recipe_sliders() -> void:
	if _sim == null or not _sim.has_method("get_feed_recipe"):
		return
	var r: Dictionary = _sim.get_feed_recipe()
	protein_slider.set_value_no_signal(float(r.get("protein", 42.0)))
	replace_slider.set_value_no_signal(float(r.get("replace", 20.0)))
	astax_slider.set_value_no_signal(float(r.get("astax", 50.0)))


func _on_recipe_changed(_value: float) -> void:
	_update_recipe_preview()


func _update_recipe_preview() -> void:
	var p := protein_slider.value
	var r := replace_slider.value
	var a := astax_slider.value
	var cost := 12.0 + (p - 38.0) * 0.18 + r * 0.05 + a * 0.025
	var fcr := 1.0 - (p - 38.0) * 0.003 - r * 0.0006 - minf(a, 60.0) * 0.0002
	var grade := "A" if a >= 50.0 and p >= 42.0 else ("B" if a >= 40.0 else "C")
	recipe_preview.text = tr("UI_AIFEED_RECIPE_PREVIEW") % [SimFormatters.num(cost, 1), SimFormatters.num(fcr, 3), grade,
		tr("UI_AIFEED_VAP_FULL") if grade != "C" else tr("UI_AIFEED_VAP_PALE")]
	recipe_preview.add_theme_color_override("font_color",
		Color("#2E6B45") if grade == "A" else (Color("#8A5A00") if grade == "B" else Color("#B3172F")))


func _on_apply_recipe() -> void:
	if _sim and _sim.has_method("set_feed_recipe"):
		_sim.set_feed_recipe(protein_slider.value, replace_slider.value, astax_slider.value)

func on_panel_shown() -> void:
	refresh()

func _on_state_updated() -> void:
	if visible:
		refresh()

func _get_status() -> Dictionary:
	if _sim != null and _sim.has_method("get_ai_feeding_status"):
		return _sim.get_ai_feeding_status()
	return {"installed": false, "calibration": 0.0, "camera_uptime": 0.0, "appetite_score": 60.0, "fcr_multiplier": 1.0, "daily_savings": 0.0}

func _is_installed(st: Dictionary) -> bool:
	# C# backend uses "is_installed"; legacy placeholders used "installed".
	return bool(st.get("is_installed", st.get("installed", false)))

func _pct(st: Dictionary, backend_key: String, legacy_key: String, legacy_default: float) -> float:
	# Backend reports fractions (0.0–1.0); legacy placeholders used percents.
	var v := float(st.get(backend_key, st.get(legacy_key, legacy_default)))
	return v * 100.0 if v <= 1.0 else v

func _update_calibration_status():
	if calibration_status_label == null:
		return
	var st := _get_status()
	if not _is_installed(st):
		calibration_status_label.text = tr("UI_AIFEED_CALIBRATION") % tr("UI_AIFEED_NOT_INSTALLED")
	else:
		calibration_status_label.text = tr("UI_AIFEED_CALIBRATION") % SimFormatters.percent(_pct(st, "calibration_level", "calibration", 0.0) / 100.0)

func _update_cameras_status():
	if cameras_status_label == null:
		return
	var st := _get_status()
	if not _is_installed(st):
		cameras_status_label.text = tr("UI_AIFEED_CAMERAS") % tr("UI_AIFEED_NOT_INSTALLED")
	else:
		cameras_status_label.text = tr("UI_AIFEED_CAMERAS") % (tr("UI_AIFEED_UPTIME") % SimFormatters.percent(_pct(st, "camera_uptime", "camera_uptime", 0.0) / 100.0, 1))

func _update_appetite_level():
	if appetite_level_label == null:
		return
	var st := _get_status()
	var score := _pct(st, "appetite_score", "appetite_score", 0.0)
	var level := tr("UI_LEVEL_LOW") if score < 40.0 else (tr("UI_LEVEL_MEDIUM") if score < 70.0 else tr("UI_LEVEL_HIGH"))
	appetite_level_label.text = tr("UI_AIFEED_APPETITE") % [level, SimFormatters.percent(score / 100.0)]

func _update_fcr_savings():
	if fcr_savings_label == null:
		return
	var st := _get_status()
	if not _is_installed(st):
		fcr_savings_label.text = tr("UI_AIFEED_FCR_SAVINGS") % tr("UI_AIFEED_INSTALL_FIRST")
	else:
		var saving := 1.0 - float(st.get("fcr_multiplier", 1.0))
		fcr_savings_label.text = tr("UI_AIFEED_FCR_SAVINGS") % SimFormatters.percent(saving, 1)

func _on_install_ai_pressed():
	if _sim != null and _sim.has_method("install_ai_feeding"):
		_sim.install_ai_feeding()
	refresh()

func _input(event: InputEvent) -> void:
	# Only while shown: as a cached, hidden tab this used to swallow every ESC.
	if is_visible_in_tree() and event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
