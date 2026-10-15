class_name LicenceAuctionCard
extends Control

## The growth-licence auction card (roadmap U2-01). Three states, all read
## from the engine through SimBridge:
##   * preview: what applying opens (the reserve and its price per tonne, the
##     licence's MTB, the auction's length) before the player commits; opening
##     is free.
##   * live: the leading bid and who holds it, the rivals' bids by name, the
##     minimum bid, a countdown in sim days, and the player's bid. The card
##     re-reads the auction every sim day; the clock keeps running (it slows
##     to 1× while the auction runs and stops the day before it closes unless
##     the player leads: SimulationEngine.Expansion.cs).
##   * result: won (with the way on to a locality) or lost.
## Every refusal is shown as text from its reason key (EXPANSION_REASON_*).

signal build_requested(zone_id: String)
signal closed

const MNOK := 1_000_000.0

@onready var _dialog: PanelContainer = %Dialog
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _subtitle: Label = %SubtitleLabel
@onready var _close_btn: Button = %CloseBtn
@onready var _preview_box: VBoxContainer = %PreviewBox
@onready var _reserve_overline: Label = %ReserveOverline
@onready var _reserve_amount: Label = %ReserveAmount
@onready var _reserve_per_tonne: Label = %ReservePerTonne
@onready var _length_overline: Label = %LengthOverline
@onready var _length_amount: Label = %LengthAmount
@onready var _length_note: Label = %LengthNote
@onready var _preview_note: Label = %PreviewNote
@onready var _live_box: VBoxContainer = %LiveBox
@onready var _lead_overline: Label = %LeadOverline
@onready var _lead_amount: Label = %LeadAmount
@onready var _lead_per_tonne: Label = %LeadPerTonne
@onready var _leader_label: Label = %LeaderLabel
@onready var _countdown_label: Label = %CountdownLabel
@onready var _countdown_bar: ProgressBar = %CountdownBar
@onready var _history_header: Label = %HistoryHeader
@onready var _history_list: VBoxContainer = %HistoryList
@onready var _bid_header: Label = %BidHeader
@onready var _bid_spin: SpinBox = %BidSpin
@onready var _min_bid_btn: Button = %MinBidBtn
@onready var _plus5_btn: Button = %Plus5Btn
@onready var _plus10_btn: Button = %Plus10Btn
@onready var _bid_info: Label = %BidInfo
@onready var _result_box: VBoxContainer = %ResultBox
@onready var _result_title: Label = %ResultTitle
@onready var _result_body: Label = %ResultBody
@onready var _notice: Label = %NoticeLabel
@onready var _reference: Label = %ReferenceLabel
@onready var _hint: Label = %HintLabel
@onready var _cancel_btn: Button = %CancelBtn
@onready var _primary_btn: Button = %PrimaryBtn

var _sim: Node = null
var _zone_id := ""
## "preview" | "live" | "result".
var _mode := ""
var _auction: Dictionary = {}
var _preview: Dictionary = {}
## The last refusal from a verb (a label key), shown until the next refresh.
var _last_error_key := ""


func _ready() -> void:
	hide()
	_sim = get_node_or_null("/root/SimBridge")
	_close_btn.tooltip_text = tr("UI_CLOSE")
	_close_btn.pressed.connect(close)
	_cancel_btn.text = tr("UI_CLOSE")
	_cancel_btn.pressed.connect(close)
	_primary_btn.pressed.connect(_on_primary)
	_min_bid_btn.pressed.connect(_set_bid_to_min)
	_plus5_btn.pressed.connect(_raise_bid.bind(1.05))
	_plus10_btn.pressed.connect(_raise_bid.bind(1.10))
	_bid_spin.value_changed.connect(func(_v: float) -> void: _update_bid_info())
	_overline.text = tr("EXPANSION_AUCTION_OVERLINE")
	_reserve_overline.text = tr("EXPANSION_AUCTION_RESERVE")
	_length_overline.text = tr("EXPANSION_AUCTION_LENGTH")
	_lead_overline.text = tr("EXPANSION_AUCTION_LEADING_BID")
	_history_header.text = tr("EXPANSION_AUCTION_BIDS")
	_bid_header.text = tr("EXPANSION_AUCTION_YOUR_BID")
	_min_bid_btn.text = tr("EXPANSION_AUCTION_MIN_BID_BUTTON")
	_plus5_btn.text = tr("EXPANSION_AUCTION_PLUS_5")
	_plus10_btn.text = tr("EXPANSION_AUCTION_PLUS_10")
	_bid_spin.suffix = "MNOK"
	_bid_spin.step = 1.0
	_bid_spin.tooltip_text = tr("EXPANSION_AUCTION_SPIN_TIP")
	if _sim != null:
		_sim.day_advanced.connect(func(_d: int, _s: String) -> void: _refresh_if_open())
		_sim.state_updated.connect(_refresh_if_open)


## Open the card for a zone: the live auction when one runs, the result of
## the last one, else the preview of applying.
func open_for_zone(zone_id: String) -> void:
	_zone_id = zone_id
	_last_error_key = ""
	show()
	_refresh()
	JuiceEffects.panel_enter(_dialog)
	if _primary_btn.disabled:
		_cancel_btn.grab_focus.call_deferred()
	else:
		_primary_btn.grab_focus.call_deferred()


func close() -> void:
	if not visible:
		return
	hide()
	closed.emit()


## The zone shown, and the card's state ("preview", "live", "result").
func zone_id() -> String:
	return _zone_id


func mode() -> String:
	return _mode


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		close()
		get_viewport().set_input_as_handled()


func _refresh_if_open() -> void:
	if visible and _zone_id != "":
		_refresh()


func _refresh() -> void:
	if _sim == null:
		return
	_auction = _sim.get_licence_auction(_zone_id)
	_preview = _sim.preview_licence_application(_zone_id)
	var status := str(_auction.get("status", ""))
	if bool(_auction.get("success", false)) and status == "open":
		_mode = "live"
	elif bool(_auction.get("success", false)) and bool(_auction.get("player_entered", false)) \
			and not (status == "won_player" and bool(_auction.get("consumed", false))):
		_mode = "result"
	else:
		_mode = "preview"
	_preview_box.visible = _mode == "preview"
	_live_box.visible = _mode == "live"
	_result_box.visible = _mode == "result"
	_show_header()
	match _mode:
		"preview": _show_preview()
		"live": _show_live()
		_: _show_result()
	_show_reference()


func _show_header() -> void:
	var area := int(_preview.get("production_area", 0))
	_title.text = tr("EXPANSION_AUCTION_TITLE") % [SimLabels.zone(_zone_id), area]
	_subtitle.text = tr("EXPANSION_AUCTION_SUBTITLE") % [SimFormatters.num(float(_preview.get("licence_mtb_t", 0.0))), area]


# ── Preview: what applying opens ──────────────────────────────────────────────

func _show_preview() -> void:
	var reserve := float(_preview.get("reserve_nok", 0.0))
	var balance := float(_preview.get("balance_nok", 0.0))
	_reserve_amount.text = SimFormatters.format_nok(reserve)
	_reserve_per_tonne.text = tr("EXPANSION_PER_TONNE") % SimFormatters.num(float(_preview.get("reserve_per_tonne_nok", 0.0)))
	_length_amount.text = tr("EXPANSION_AUCTION_DAYS") % int(_preview.get("auction_length_days", 7))
	_length_note.text = tr("EXPANSION_AUCTION_LENGTH_NOTE")
	_preview_note.text = tr("EXPANSION_AUCTION_PREVIEW_NOTE")
	_hint.text = tr("EXPANSION_AUCTION_FREE_HINT")
	var allowed := bool(_preview.get("allowed", false))
	var refusal := "" if allowed else tr(str(_preview.get("label_key", "")))
	if refusal != "":
		_show_notice(refusal)
	elif balance < reserve:
		_show_notice(tr("EXPANSION_AUCTION_CASH_SHORT") % [SimFormatters.format_nok(balance), SimFormatters.format_nok(reserve)])
	else:
		_show_notice(_error_text())
	_primary_btn.text = tr("EXPANSION_AUCTION_OPEN_BUTTON")
	_primary_btn.theme_type_variation = &"PrimaryButton"
	UIPolish.set_available(_primary_btn, refusal)


# ── Live: the running auction ─────────────────────────────────────────────────

func _show_live() -> void:
	var a := _auction
	var current := float(a.get("current_bid_nok", 0.0))
	var leader_kind := str(a.get("leader_kind", "none"))
	if current > 0.0:
		_lead_amount.text = SimFormatters.format_nok(current)
		_lead_per_tonne.text = tr("EXPANSION_PER_TONNE") % SimFormatters.num(float(a.get("current_per_tonne_nok", 0.0)))
	else:
		_lead_amount.text = tr("EXPANSION_AUCTION_NO_BIDS")
		_lead_per_tonne.text = tr("EXPANSION_AUCTION_RESERVE_IS") % SimFormatters.format_nok(float(a.get("reserve_nok", 0.0)))
	if leader_kind == "player":
		_leader_label.text = tr("EXPANSION_AUCTION_YOU_LEAD")
		_leader_label.theme_type_variation = &"BodyStrongOk"
	elif leader_kind == "rival":
		_leader_label.text = tr("EXPANSION_AUCTION_RIVAL_LEADS") % str(a.get("leader_name", ""))
		_leader_label.theme_type_variation = &"BodyStrongWarn"
	else:
		_leader_label.text = tr("EXPANSION_AUCTION_NOBODY_LEADS")
		_leader_label.theme_type_variation = &"BodyStrong"
	_show_countdown(a)
	_show_history(a.get("bids", []) as Array)

	var min_bid := float(a.get("min_bid_nok", 0.0))
	var balance := float(a.get("balance_nok", 0.0))
	var min_m := ceilf(min_bid / MNOK)
	_bid_spin.min_value = min_m
	_bid_spin.max_value = maxf(min_m, floorf(maxf(balance, min_bid) / MNOK) + 1000.0)
	if _bid_spin.value < min_m:
		_bid_spin.set_value_no_signal(min_m)
	_update_bid_info()
	_hint.text = tr("EXPANSION_AUCTION_CLOCK_HINT")
	_primary_btn.text = tr("EXPANSION_AUCTION_BID_BUTTON")
	_primary_btn.theme_type_variation = &"PrimaryButton"
	var cash_short := balance < min_bid
	UIPolish.set_available(_primary_btn, tr("EXPANSION_REASON_INSUFFICIENT_FUNDS") if cash_short else "")
	if cash_short:
		_show_notice(tr("EXPANSION_AUCTION_CASH_SHORT") % [SimFormatters.format_nok(balance), SimFormatters.format_nok(min_bid)])
	else:
		_show_notice(_error_text())


func _show_countdown(a: Dictionary) -> void:
	var opens := int(a.get("opens_day", 0))
	var closes := int(a.get("closes_day", 0))
	var today := int(a.get("today", opens))
	var left := int(a.get("days_left", 0))
	if left > 1:
		_countdown_label.text = tr("EXPANSION_AUCTION_CLOSES_IN") % [left, GameCalendar.short_date(closes)]
	elif left == 1:
		_countdown_label.text = tr("EXPANSION_AUCTION_CLOSES_TOMORROW") % GameCalendar.short_date(closes)
	else:
		_countdown_label.text = tr("EXPANSION_AUCTION_LAST_ROUND")
	_countdown_bar.max_value = maxf(1.0, float(closes - opens))
	_countdown_bar.value = clampf(float(today - opens), 0.0, _countdown_bar.max_value)
	_countdown_bar.tooltip_text = _countdown_label.text


func _show_history(bids: Array) -> void:
	for child in _history_list.get_children():
		_history_list.remove_child(child)
		child.queue_free()
	if bids.is_empty():
		var empty := Label.new()
		empty.text = tr("EXPANSION_AUCTION_NO_BIDS_YET")
		empty.theme_type_variation = &"BodySmallMuted"
		_history_list.add_child(empty)
		return
	for i in bids.size():
		var b: Dictionary = bids[i]
		var row := HBoxContainer.new()
		row.name = "Bid_%d" % i
		row.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
		var mine := str(b.get("bidder_kind", "")) == "player"
		var tone := &"Accent" if mine else &""
		row.add_child(_cell(GameCalendar.short_date(int(b.get("day", 0))), &"SmallMuted", 84.0))
		var who := _cell(tr("EXPANSION_AUCTION_YOU") if mine else str(b.get("bidder_name", "")), StringName("BodySmallStrong" + String(tone)) if mine else &"BodySmall", 0.0)
		who.name = "Bidder"
		who.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(who)
		var amount := _cell(SimFormatters.format_nok(float(b.get("amount_nok", 0.0))), StringName("Figure" + String(tone)) if mine else &"Figure", 0.0)
		amount.name = "Amount"
		row.add_child(amount)
		row.add_child(_cell(tr("EXPANSION_PER_TONNE") % SimFormatters.num(float(b.get("per_tonne_nok", 0.0))), &"SmallMuted", 132.0))
		_history_list.add_child(row)


func _cell(text: String, variation: StringName, min_width: float) -> Label:
	var l := Label.new()
	l.text = text
	l.theme_type_variation = variation
	l.custom_minimum_size = Vector2(min_width, 0)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT if min_width >= 120.0 else HORIZONTAL_ALIGNMENT_LEFT
	return l


func _update_bid_info() -> void:
	if _mode != "live":
		return
	var amount := _bid_spin.value * MNOK
	var mtb := float(_auction.get("licence_mtb_t", 780.0))
	_bid_info.text = tr("EXPANSION_AUCTION_BID_INFO") % [
		SimFormatters.num(roundf(amount / maxf(mtb, 1.0))),
		SimFormatters.format_nok(float(_auction.get("min_bid_nok", 0.0))),
		SimFormatters.format_nok(float(_auction.get("balance_nok", 0.0)))]


func _set_bid_to_min() -> void:
	_bid_spin.value = ceilf(float(_auction.get("min_bid_nok", 0.0)) / MNOK)


func _raise_bid(factor: float) -> void:
	var base := maxf(float(_auction.get("current_bid_nok", 0.0)), float(_auction.get("reserve_nok", 0.0)))
	_bid_spin.value = maxf(ceilf(base * factor / MNOK), _bid_spin.min_value)


# ── Result: won or lost ───────────────────────────────────────────────────────

func _show_result() -> void:
	var a := _auction
	var status := str(a.get("status", ""))
	var mtb := SimFormatters.num(float(a.get("licence_mtb_t", 0.0)))
	var area := int(a.get("production_area", 0))
	_hint.text = ""
	_show_notice(_error_text())
	match status:
		"won_player":
			_result_title.text = tr("EXPANSION_AUCTION_WON_TITLE")
			_result_title.theme_type_variation = &"HeadingOk"
			_result_body.text = tr("EXPANSION_AUCTION_WON_BODY") % [mtb, area, SimFormatters.format_nok(float(a.get("current_bid_nok", 0.0))), SimLabels.zone(_zone_id)]
			_primary_btn.text = tr("EXPANSION_BUILD_BUTTON")
			UIPolish.set_available(_primary_btn, "")
		"won_rival":
			_result_title.text = tr("EXPANSION_AUCTION_LOST_TITLE")
			_result_title.theme_type_variation = &"Heading"
			_result_body.text = tr("EXPANSION_AUCTION_LOST_BODY") % [str(a.get("leader_name", "")), SimFormatters.format_nok(float(a.get("current_bid_nok", 0.0)))]
			_primary_btn.text = tr("EXPANSION_AUCTION_AGAIN_BUTTON")
			UIPolish.set_available(_primary_btn, "" if bool(_preview.get("allowed", false)) else tr(str(_preview.get("label_key", ""))))
		_:
			_result_title.text = tr("EXPANSION_AUCTION_ENDED_TITLE")
			_result_title.theme_type_variation = &"Heading"
			_result_body.text = tr("EXPANSION_AUCTION_ENDED_BODY")
			_primary_btn.text = tr("EXPANSION_AUCTION_AGAIN_BUTTON")
			UIPolish.set_available(_primary_btn, "" if bool(_preview.get("allowed", false)) else tr(str(_preview.get("label_key", ""))))


# ── The reference band and the notice ─────────────────────────────────────────

func _show_reference() -> void:
	var source := _auction if bool(_auction.get("success", false)) else _preview
	_reference.text = tr("EXPANSION_AUCTION_REFERENCE") % [
		SimFormatters.num(float(source.get("reference_2022_per_tonne_nok", 0.0))),
		SimFormatters.num(float(source.get("reference_2024_per_tonne_nok", 0.0)))]


func _show_notice(text: String) -> void:
	_notice.text = text
	_notice.visible = text != ""


func _error_text() -> String:
	return tr(_last_error_key) if _last_error_key != "" else ""


# ── Verbs ─────────────────────────────────────────────────────────────────────

func _on_primary() -> void:
	if _sim == null:
		return
	match _mode:
		"preview":
			var opened: Dictionary = _sim.open_licence_auction(_zone_id)
			_last_error_key = "" if bool(opened.get("success", false)) else str(opened.get("label_key", ""))
			_refresh()
			if bool(opened.get("success", false)):
				_set_bid_to_min()
		"live":
			var result: Dictionary = _sim.bid_on_licence_auction(_zone_id, _bid_spin.value * MNOK)
			_last_error_key = "" if bool(result.get("success", false)) else str(result.get("label_key", ""))
			_refresh()
		_:
			if str(_auction.get("status", "")) == "won_player":
				var zone := _zone_id
				close()
				build_requested.emit(zone)
			else:
				_mode = "preview"
				_auction = {}
				_preview_box.visible = true
				_result_box.visible = false
				_show_preview()
