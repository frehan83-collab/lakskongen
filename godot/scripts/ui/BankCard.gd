class_name BankCard
extends PanelContainer

## BankCard — the bank's view of the company's cash (roadmap U2-03), at the
## top of the Finance tab, where the liquidity warning's and the bank call's
## "Skaff kapital" lead.
##
## The bank reads the running burn and the days left before the scenario's
## bankruptcy floor (SimBridge.get_liquidity_status): calm, a liquidity
## warning (60 days or less) or a bank call (28 days or less). At a bank call
## it may offer a rescue loan against the fish in the sea; the card shows its
## terms and its deadline and takes or declines it. The offer is the engine's
## (LiquidityRules): nothing here sizes or prices anything. It reads the bank
## only while it is on screen (and when it comes on screen): a hidden Finance
## tab costs no day of a fast clock anything.

const FIGURES := ["UI_BANK_FIG_BURN", "UI_BANK_FIG_RUNWAY", "UI_BANK_FIG_INTEREST", "UI_BANK_FIG_COLLATERAL"]

## A runway longer than this reads as calm (a year of cash at today's burn).
const CALM_RUNWAY_DAYS := 365.0

var _sim: Node = null
var _status: Dictionary = {}
var _tag: Label
var _text: Label
var _figures: GridContainer
var _figure_values: Dictionary = {}  # key -> Label
var _offer_box: VBoxContainer
var _offer_text: Label
var accept_button: Button
var decline_button: Button
var _result: Label


func _ready() -> void:
	name = "BankCard"
	theme_type_variation = &"CardPadded"
	_sim = get_node_or_null("/root/SimBridge")
	_build()
	if _sim != null:
		_sim.state_updated.connect(refresh)
		_sim.day_advanced.connect(func(_d: int, _s: String) -> void: refresh())
	visibility_changed.connect(refresh)
	refresh()


func status() -> Dictionary:
	return _status


func _build() -> void:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 8)
	add_child(box)
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 10)
	box.add_child(head)
	var title := Label.new()
	title.text = tr("UI_BANK_TITLE")
	title.theme_type_variation = &"Subheading"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	_tag = Label.new()
	_tag.name = "StageTag"
	_tag.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	head.add_child(_tag)
	_text = Label.new()
	_text.name = "StageText"
	_text.theme_type_variation = &"BodySmall"
	_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_text.custom_minimum_size = Vector2(320, 0)
	box.add_child(_text)
	_figures = GridContainer.new()
	_figures.name = "Figures"
	_figures.columns = 4
	_figures.add_theme_constant_override("h_separation", 18)
	_figures.add_theme_constant_override("v_separation", 4)
	box.add_child(_figures)
	for key in FIGURES:
		var cap := Label.new()
		cap.text = tr(key)
		cap.theme_type_variation = &"SmallMuted"
		_figures.add_child(cap)
		var val := Label.new()
		val.name = "Figure_" + str(key).trim_prefix("UI_BANK_FIG_").to_lower()
		val.theme_type_variation = &"FigureSmall"
		_figures.add_child(val)
		_figure_values[key] = val

	_offer_box = VBoxContainer.new()
	_offer_box.name = "Offer"
	_offer_box.add_theme_constant_override("separation", 8)
	box.add_child(_offer_box)
	var callout := PanelContainer.new()
	callout.theme_type_variation = &"CalloutCrit"
	_offer_box.add_child(callout)
	var inner := VBoxContainer.new()
	inner.add_theme_constant_override("separation", 6)
	callout.add_child(inner)
	var offer_title := Label.new()
	offer_title.text = tr("UI_BANK_OFFER_TITLE")
	offer_title.theme_type_variation = &"BodyStrong"
	inner.add_child(offer_title)
	_offer_text = Label.new()
	_offer_text.name = "OfferText"
	_offer_text.theme_type_variation = &"BodySmall"
	_offer_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_offer_text.custom_minimum_size = Vector2(320, 0)
	inner.add_child(_offer_text)
	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation", 10)
	inner.add_child(actions)
	accept_button = Button.new()
	accept_button.name = "AcceptButton"
	accept_button.text = tr("UI_BANK_ACCEPT")
	accept_button.tooltip_text = tr("UI_BANK_ACCEPT_TIP")
	accept_button.theme_type_variation = &"PrimaryButton"
	accept_button.pressed.connect(_on_accept)
	actions.add_child(accept_button)
	decline_button = Button.new()
	decline_button.name = "DeclineButton"
	decline_button.text = tr("UI_BANK_DECLINE")
	decline_button.tooltip_text = tr("UI_BANK_DECLINE_TIP")
	decline_button.theme_type_variation = &"GhostButton"
	decline_button.pressed.connect(_on_decline)
	actions.add_child(decline_button)
	_result = Label.new()
	_result.name = "Result"
	_result.theme_type_variation = &"BodySmallMuted"
	_result.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_result.custom_minimum_size = Vector2(320, 0)
	box.add_child(_result)


## Re-read the bank's view (on every state change and day while on screen).
func refresh() -> void:
	if _sim == null or not is_inside_tree() or not is_visible_in_tree():
		return
	_status = _sim.get_liquidity_status()
	if not bool(_status.get("success", false)):
		_tag.text = ""
		_text.text = tr("UI_BANK_TEXT_CALM")
		_offer_box.visible = false
		_result.visible = false
		return
	var stage := str(_status.get("stage", "none"))
	var runway := float(_status.get("runway_days", -1.0))
	var floor_text := SimFormatters.format_nok(float(_status.get("floor", 0.0)))
	match stage:
		"bank_call":
			_tag.text = tr("UI_BANK_STAGE_CALL")
			_tag.theme_type_variation = &"TagCrit"
			_text.text = tr("UI_BANK_TEXT_CALL") % [int(_status.get("bank_call_day", 0)), floor_text, _days(runway)]
		"warning":
			_tag.text = tr("UI_BANK_STAGE_WARNING")
			_tag.theme_type_variation = &"TagWarn"
			_text.text = tr("UI_BANK_TEXT_WARNING") % [int(_status.get("warning_day", 0)), floor_text, _days(runway)]
		_:
			_tag.text = tr("UI_BANK_STAGE_CALM")
			_tag.theme_type_variation = &"TagOk"
			_text.text = tr("UI_BANK_TEXT_FALLING") % [SimFormatters.format_nok(float(_status.get("burn_per_day", 0.0))),
				floor_text, _days(runway)] if runway >= 0.0 and runway < CALM_RUNWAY_DAYS else tr("UI_BANK_TEXT_CALM")
	_fill_figures(runway)
	_fill_offer()


func _days(runway: float) -> String:
	return SimFormatters.integer(int(ceil(runway))) if runway >= 0.0 else "–"


func _fill_figures(runway: float) -> void:
	var rate := SimFormatters.percent(float(_status.get("overdraft_rate", 0.0)), 2)
	(_figure_values["UI_BANK_FIG_BURN"] as Label).text = SimFormatters.format_nok(float(_status.get("burn_per_day", 0.0)))
	(_figure_values["UI_BANK_FIG_RUNWAY"] as Label).text = _days(runway)
	(_figure_values["UI_BANK_FIG_INTEREST"] as Label).text = tr("UI_BANK_FIG_INTEREST_VALUE") % [
		SimFormatters.format_nok(float(_status.get("overdraft_interest_paid", 0.0))), rate]
	(_figure_values["UI_BANK_FIG_COLLATERAL"] as Label).text = SimFormatters.format_nok(float(_status.get("collateral_value", 0.0)))


func _fill_offer() -> void:
	var offer: Dictionary = _status.get("offer", {})
	var open := str(offer.get("status", "")) == "open"
	_offer_box.visible = open
	_result.visible = not open and not offer.is_empty()
	if open:
		_offer_text.text = tr("UI_BANK_OFFER_TEXT") % [SimFormatters.format_nok(float(offer.get("amount", 0.0))),
			SimFormatters.percent(float(offer.get("interest_rate", 0.0)), 1), SimFormatters.format_nok(float(offer.get("fee", 0.0))),
			int(offer.get("term_months", 0)), int(offer.get("expires_day", 0)), int(offer.get("days_left", 0))]
	elif not offer.is_empty():
		_result.text = tr("UI_BANK_OFFER_" + str(offer.get("status", "")).to_upper()) % [
			SimFormatters.format_nok(float(offer.get("amount", 0.0))), int(offer.get("offered_day", 0))]


func _on_accept() -> void:
	var r: Dictionary = _sim.accept_rescue_loan()
	if not bool(r.get("success", false)):
		_result.text = tr("UI_BANK_ERROR_" + str(r.get("error_key", "engine_not_ready")).to_upper())
		_result.visible = true
	refresh()


func _on_decline() -> void:
	var r: Dictionary = _sim.decline_rescue_loan()
	if not bool(r.get("success", false)):
		_result.text = tr("UI_BANK_ERROR_" + str(r.get("error_key", "engine_not_ready")).to_upper())
		_result.visible = true
	refresh()
