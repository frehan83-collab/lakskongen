
extends Control

## ProcessingFacilityPanel v3.0 — VAP fillet line (Phase 3.5, rewritten).
## Before: placeholder with fake tariffs and a smuggle RNG touching no sim.
## Now: buy/upgrade a real plant tier; owned plant adds +12% fillet margin
## on all harvest revenue at 5k NOK/day opex. All UI built in code.

var _sim: Node = null

var _status_label: Label
var _tier_option: OptionButton
var _buy_button: Button
var _info_label: Label

const TIERS := ["Small", "Medium", "Large"]
const COSTS := {"Small": 1_000_000.0, "Medium": 5_000_000.0, "Large": 15_000_000.0}

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var main_vbox := VBoxContainer.new()
	add_child(main_vbox)
	main_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	main_vbox.add_theme_constant_override("separation", 10)

	var title := Label.new()
	title.text = "🏭 Foredling (VAP)"
	title.add_theme_font_size_override("font_size", 20)
	main_vbox.add_child(title)

	_status_label = Label.new()
	_status_label.add_theme_font_size_override("font_size", 14)
	main_vbox.add_child(_status_label)

	_info_label = Label.new()
	_info_label.text = "Filetlinje gir +12% på all høsteinntekt. Drift: 5k NOK/dag.\nSmall 1M · Medium 5M · Large 15M (oppgradering = mellomlegg)."
	_info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_info_label.add_theme_font_size_override("font_size", 12)
	main_vbox.add_child(_info_label)

	_tier_option = OptionButton.new()
	for t in TIERS:
		_tier_option.add_item("%s (%.0f MNOK)" % [t, COSTS[t] / 1_000_000.0])
	main_vbox.add_child(_tier_option)

	_buy_button = Button.new()
	_buy_button.text = "Kjøp / oppgrader"
	_buy_button.pressed.connect(_on_buy_pressed)
	main_vbox.add_child(_buy_button)

	if _sim and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_refresh)
	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _refresh() -> void:
	if not _sim:
		_status_label.text = "Ingen sim-tilkobling"
		return
	var owned: bool = _sim.owns_processing_plant() if _sim.has_method("owns_processing_plant") else false
	var tier: String = _sim.processing_tier() if _sim.has_method("processing_tier") else ""
	if owned:
		_status_label.text = "✅ Filetlinje i drift (%s) — +12% høstpremie" % tier
	else:
		_status_label.text = "⚪ Ingen foredling — høst selges rundt"

func _on_buy_pressed() -> void:
	if not _sim:
		return
	var tier: String = TIERS[_tier_option.selected]
	if _sim.has_method("buy_processing_plant"):
		if _sim.buy_processing_plant(tier):
			_toast("🏭 Filetlinje kjøpt: " + tier)
		else:
			_toast("⚠️ Ikke nok midler eller ugyldig nivå")
	_refresh()

func _toast(message: String) -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, "INFO", 3.5)
