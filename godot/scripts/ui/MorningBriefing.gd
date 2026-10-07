extends CanvasLayer

## MorningBriefing v2.1 — Start-of-day summary (scene-based).
## Layout lives in scenes/ui/MorningBriefing.tscn; script is wiring only:
## @onready % refs, signal hookups, dynamic briefing sections, localization.
## Shows: weather forecast, overnight events, market changes, pending decisions
## Appears automatically at the start of each day (day advanced signal)

@onready var _dialog_panel: PanelContainer = %DialogPanel
@onready var _header_label: Label = %HeaderLabel
@onready var _day_label: Label = %DayLabel
@onready var _content_box: VBoxContainer = %ContentBox
@onready var _close_button: Button = %CloseButton

var _game_state: Node = null
var _sim_core: Node = null
var _decision_manager: Node = null
var _last_day_shown: int = 0

func _ready():
	visible = false

	_game_state = get_node_or_null("/root/GameState")
	_sim_core = get_node_or_null("/root/SimulationCore")
	_decision_manager = get_node_or_null("/root/DecisionManager")

	if _game_state and _game_state.has_signal("DayAdvanced"):
		_game_state.DayAdvanced.connect(_on_day_advanced)

	# Juice fix: the live autoload is SimBridge (day_advanced, lowercase).
	# Hook it too so the briefing actually appears each morning.
	# (Lambda adapts the 2-arg signal to the 1-arg handler; GameState
	# hookup above is untouched.)
	var sim := get_node_or_null("/root/SimBridge")
	if sim and sim.has_signal("day_advanced"):
		sim.day_advanced.connect(func(new_day: int, _season: String): _on_day_advanced(new_day))

	_apply_localization()
	_close_button.pressed.connect(_on_close_pressed)

func _apply_localization() -> void:
	_header_label.text = _tr_or("UI_BRIEFING_TITLE", "☀️ MORNING BRIEFING")
	_close_button.text = _tr_or("UI_BRIEFING_START_DAY", "Start the Day →")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _on_day_advanced(new_day: int):
	# Ignore non-days and day 1: the tutorial owns the first morning.
	# Record the day so a duplicate signal for the same day cannot re-show it.
	if new_day <= 1:
		_last_day_shown = max(_last_day_shown, new_day)
		return

	# Only show for forward progress (monotonic) — never re-show an old day.
	if new_day <= _last_day_shown:
		return

	_last_day_shown = new_day

	_update_briefing_content(new_day)
	visible = true
	_play_open_animation()

	# Play morning chime
	if has_node("/root/AudioController"):
		get_node("/root/AudioController").play_ui("notification")

## Juice: soft pop-in for the briefing dialog (Compatibility-safe tween).
func _play_open_animation() -> void:
	if not is_instance_valid(_dialog_panel):
		return
	_dialog_panel.pivot_offset = _dialog_panel.size / 2.0
	_dialog_panel.scale = Vector2(0.96, 0.96)
	_dialog_panel.modulate.a = 0.0
	var tween := create_tween()
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_BACK)
	tween.set_parallel(true)
	tween.tween_property(_dialog_panel, "scale", Vector2.ONE, 0.3)
	tween.tween_property(_dialog_panel, "modulate:a", 1.0, 0.22)

func _update_briefing_content(current_day: int):
	var day_num := current_day
	var year_num := 1
	if _game_state:
		day_num = _game_state.CurrentDay
		year_num = _game_state.CurrentYear
	else:
		var sim_d := get_node_or_null("/root/SimBridge")
		if sim_d:
			day_num = int(sim_d.get("current_day") or current_day)
	_day_label.text = _tr_or("UI_BRIEFING_DAY_FORMAT", "Dag %d — år %d") % [day_num, year_num]

	# Clear old content
	for child in _content_box.get_children():
		child.queue_free()

	# 1. Weather Forecast
	_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_WEATHER", "🌤️ Værmelding"), _get_weather_forecast()))

	# 2. Market Update
	_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_MARKET", "💰 Marked"), _get_market_update()))

	# 3. Overnight Events
	var events = _get_overnight_events()
	if events != "":
		_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_EVENTS", "📰 Nattens hendelser"), events))
	else:
		_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_EVENTS", "📰 Nattens hendelser"),
			_tr_or("UI_BRIEFING_NO_EVENTS", "Rolig natt — ingen uhell å melde. 🎉")))

	# 3b. Natt-Driftssentral (Phase 5.2): night-shift digest
	var night_digest = _get_night_digest()
	if night_digest != "":
		_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_NIGHT_OPS", "🌙 Natt-Driftssentral"), night_digest))

	# 4. Pending Decisions
	var decisions = _get_pending_decisions()
	if decisions != "":
		_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_DECISIONS", "⚠️ Ventende beslutninger"), decisions))

	# 5. Company Status
	_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_STATUS", "🏢 Selskapsstatus"), _get_company_status()))

	# 6. Depth systems (P7/P9/P10/P11/P15): gill warning, insurance, pen price,
	#    supply pressure. Each proxy is guarded; unavailable lines are skipped.
	var depth := _get_depth_status()
	if depth != "":
		_content_box.add_child(_create_section(_tr_or("UI_BRIEFING_DEPTH", "🧭 Dybde"), depth))

func _create_section(title: String, content: String) -> VBoxContainer:
	var section := VBoxContainer.new()
	section.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)

	# Title
	var title_label := LakskongenTheme.create_label(title, LakskongenTheme.FONT_LG, LakskongenTheme.ACCENT)
	section.add_child(title_label)

	# Content
	var content_label := LakskongenTheme.create_label(content, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	content_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	section.add_child(content_label)

	# Divider
	section.add_child(LakskongenTheme.create_divider())

	return section

func _get_weather_forecast() -> String:
	if _sim_core and _sim_core.Environment:
		var env = _sim_core.Environment
		var temp = env.CurrentWaterTemp
		var weather = _weather_event_to_string(env.CurrentWeather)
		return _tr_or("UI_BRIEFING_WEATHER_FORMAT", "Sjøtemperatur: %.1f°C\nNå: %s\nOksygen: %d%%") % [temp, weather, env.OxygenLevel]
	# Fallback: live SimBridge snapshot (display only).
	var sim := get_node_or_null("/root/SimBridge")
	if sim:
		var t: float = float(sim.get("temperature_c") or 8.0)
		var w: String = str(sim.call("get_current_weather_state")) if sim.has_method("get_current_weather_state") else _tr_or("UI_WEATHER_CALM", "Klart")
		return _tr_or("UI_BRIEFING_WEATHER_FORMAT", "Sjøtemperatur: %.1f°C\nNå: %s\nOksygen: %d%%") % [t, w, 92]
	return _tr_or("UI_BRIEFING_NO_DATA", "Ingen data tilgjengelig.")

func _get_market_update() -> String:
	if _sim_core and _sim_core.Market:
		var market = _sim_core.Market
		var price = market.CurrentSpotPriceNoK
		var change = market.PriceChangePercent * 100.0
		return _format_market(price, change)
	# Fallback: live SimBridge snapshot (display only).
	var sim := get_node_or_null("/root/SimBridge")
	if sim:
		var p: float = float(sim.get("market_price_nok") or 0.0)
		var week_avg: float = float(sim.get("weekly_avg_price_nok") or p)
		var ch: float = ((p - week_avg) / max(week_avg, 0.01) * 100.0) if week_avg > 0.0 else 0.0
		return _format_market(p, ch)
	return _tr_or("UI_BRIEFING_NO_DATA", "Ingen data tilgjengelig.")

func _format_market(price: float, change: float) -> String:
	var trend = "→"
	var color_text = ""
	if change > 0.5:
		trend = "↗"
		color_text = _tr_or("UI_BRIEFING_TREND_UP", " (stigende)")
	elif change < -0.5:
		trend = "↘"
		color_text = _tr_or("UI_BRIEFING_TREND_DOWN", " (fallende)")
	return _tr_or("UI_BRIEFING_MARKET_FORMAT", "Spotpris: %.2f NOK/kg %s\nEndring: %+.1f%%%s") % [price, trend, change, color_text]

func _get_overnight_events() -> String:
	# Pull the latest SimBridge events so the briefing has real content.
	var sim := get_node_or_null("/root/SimBridge")
	if sim:
		var evts: Array = sim.get("recent_events") or []
		if not evts.is_empty():
			var lines: PackedStringArray = []
			for i in range(min(3, evts.size())):
				var ev: Dictionary = evts[i] if evts[i] is Dictionary else {}
				var day := int(ev.get("day", 0))
				var label := str(ev.get("name", ev.get("description", "Hendelse")))
				lines.append("• %s (dag %d)" % [label, day])
			return "\n".join(lines)
	return ""

func _get_pending_decisions() -> String:
	if not _decision_manager:
		return ""

	var decisions = _decision_manager.GetActiveDecisions()
	if decisions.size() == 0:
		return ""

	var text = ""
	for decision in decisions:
		text += _tr_or("UI_BRIEFING_DECISION_FORMAT", "• %s (%d dager igjen)\n") % [decision.Title, decision.DaysRemaining]

	return text.strip_edges()

func _get_company_status() -> String:
	if _game_state:
		var balance = _game_state.CashBalance
		var balance_text = "%.0f NOK" % balance
		var status = _tr_or("UI_BRIEFING_STATUS_HEALTHY", "Solid")
		if balance < 0:
			status = _tr_or("UI_BRIEFING_STATUS_DEBT", "⚠️ I GJELD")
		elif balance < 1_000_000:
			status = _tr_or("UI_BRIEFING_STATUS_LOW", "⚠️ Lav likviditet")
		return _tr_or("UI_BRIEFING_STATUS_FORMAT", "Kontanter: %s\nStatus: %s\nMerder: %d\nOmdømme: %d/100") % [
			balance_text, status, _game_state.OwnedPens, _game_state.ReputationScore
		]
	# Fallback: live SimBridge snapshot (display only).
	var sim := get_node_or_null("/root/SimBridge")
	if sim:
		var bal: float = float(sim.get("balance") or 0.0)
		var status_fb := _tr_or("UI_BRIEFING_STATUS_HEALTHY", "Solid")
		if bal < 0:
			status_fb = _tr_or("UI_BRIEFING_STATUS_DEBT", "⚠️ I GJELD")
		elif bal < 1_000_000:
			status_fb = _tr_or("UI_BRIEFING_STATUS_LOW", "⚠️ Lav likviditet")
		var nok: String = sim.call("format_nok", bal) if sim.has_method("format_nok") else ("%.0f NOK" % bal)
		var pens := int(sim.get("site_count") or 0)
		return _tr_or("UI_BRIEFING_STATUS_FORMAT", "Kontanter: %s\nStatus: %s\nMerder: %d\nOmdømme: %d/100") % [
			nok, status_fb, pens, 70
		]
	return ""

## Depth line (Wave 7): gill-health warning, active insurance premium, binding
## pen price, and player-supply pressure. Every proxy is guarded; a line is
## only added when its proxy is available (unavailable -> skipped).
func _get_depth_status() -> String:
	var sim := get_node_or_null("/root/SimBridge")
	if not sim:
		return ""
	var parts: PackedStringArray = []

	# P10: gill-health early warning — scan sites, report the worst risk.
	if sim.has_method("get_all_pens") and sim.has_method("get_gill_risk"):
		var pens: Array = sim.get_all_pens()
		var warnings := 0
		var criticals := 0
		var worst_site := ""
		var worst_risk := 0.0
		for pen in pens:
			if pen is Dictionary:
				var sid := str(pen.get("id", ""))
				if sid == "":
					continue
				var g: Dictionary = sim.get_gill_risk(sid)
				if bool(g.get("warning", false)):
					warnings += 1
				if bool(g.get("critical", false)):
					criticals += 1
				var r := float(g.get("risk", 0.0))
				if r > worst_risk:
					worst_risk = r
					worst_site = sid
		if criticals > 0:
			parts.append(_tr_or("UI_BRIEFING_DEPTH_GILL_CRIT", "⚠️ Gjellehelse: %d kritisk, %d varslet") % [criticals, warnings])
		elif warnings > 0:
			parts.append(_tr_or("UI_BRIEFING_DEPTH_GILL_WARN", "⚠️ Gjellehelse: %d merd(er) varslet (maks %.0f%% @ %s)") % [warnings, worst_risk * 100.0, worst_site])
		else:
			parts.append(_tr_or("UI_BRIEFING_DEPTH_GILL_OK", "🫁 Gjellehelse: ingen varsler"))

	# P11: active catastrophe-insurance premium.
	if sim.has_method("get_insurance_status"):
		var ins: Dictionary = sim.get_insurance_status()
		if ins.has("total_premium"):
			var prem := float(ins.get("total_premium", 0.0))
			var prem_nok: String = sim.call("format_nok", prem) if sim.has_method("format_nok") else ("%.0f NOK" % prem)
			parts.append(_tr_or("UI_BRIEFING_DEPTH_INSURANCE", "🛡️ Forsikring: %s/år i premie") % prem_nok)

	# P9: binding monthly pen price from the Price Council.
	if sim.has_method("get_pen_price"):
		var pen_price := float(sim.get_pen_price())
		if pen_price > 0.0:
			parts.append(_tr_or("UI_BRIEFING_DEPTH_PEN_PRICE", "📋 Prisråd (pen-pris): %.2f NOK/kg") % pen_price)

	# P15: player-supply pressure on the spot price (-1.0 = unavailable).
	if sim.has_method("get_player_supply_pressure"):
		var pressure := float(sim.get_player_supply_pressure())
		if pressure >= 0.0:
			var label := _tr_or("UI_BRIEFING_DEPTH_SUPPLY_LOW", "lav")
			if pressure >= 0.66:
				label = _tr_or("UI_BRIEFING_DEPTH_SUPPLY_HIGH", "høy")
			elif pressure >= 0.33:
				label = _tr_or("UI_BRIEFING_DEPTH_SUPPLY_MED", "moderat")
			parts.append(_tr_or("UI_BRIEFING_DEPTH_SUPPLY", "📉 Eget tilbudstrykk: %.0f%% (%s)") % [pressure * 100.0, label])

	return "\n".join(parts)

func _weather_event_to_string(event) -> String:
	match event:
		0: return "Calm"
		1: return "⛈️ Storm"
		2: return "🌊 HURRICANE"
		3: return "☠️ Algal Bloom"
		4: return "🪼 Jellyfish Swarm"
		_: return "Unknown"


## Night-shift digest (Phase 5.2): policy + overnight actions + misfires.
func _get_night_digest() -> String:
	var sim := get_node_or_null("/root/SimBridge")
	if not sim or not sim.has_method("get_night_shift_status"):
		return ""
	var status: Dictionary = sim.get_night_shift_status()
	var policy: String = str(status.get("policy", "Standard"))
	var used: int = int(status.get("used", 0)) if status.has("used") else 0
	var budget: int = int(status.get("budget", 100)) if status.has("budget") else 100
	var detect: float = float(status.get("detection_chance", 0.7)) if status.has("detection_chance") else 0.7
	var fatigue: int = int(status.get("fatigue_per_night", 0)) if status.has("fatigue_per_night") else 0
	var misfire: float = float(status.get("misfire_chance", 0.0)) if status.has("misfire_chance") else 0.0
	var premium: float = float(status.get("premium_monthly", 0.0)) if status.has("premium_monthly") else 0.0

	var icon := {"Standard": "👁️", "ScanOnly": "🔍", "AutomatedROC": "🤖"}.get(policy, "👁️")
	var text := "%s %s-modus (oppmerksomhet %d/%d, deteksjon %d%%, utmattelse +%d%%)" % [
		icon, policy, 100 - (policy == "AutomatedROC" ? 85 : 0), 100, int(detect * 100.0), int(fatigue * 100.0)]

	if policy == "AutomatedROC":
		var auto_cost: int = int(premium / 30.0)
		text += "\n🤖 FULL-AUTO: natt-O2, blomstring, lys — %d NOK/natt (%d NOK/mnd)" % [auto_cost, int(premium)]
		if misfire > 0.0:
			text += "\n⚠️ Misfire-risiko: %.0f%% (feil tiltak / feil merd)" % (misfire * 100.0)
	elif policy == "ScanOnly":
		text += "\n🔍 SKANN: oppdaget i natt, håndteres kl 06:00 — forsinkelse 12t"
	else:
		text += "\n👁️ VAKT: standard vakt — alarm på epost/SMS, manuell respons"

	return text

func _on_close_pressed():
	visible = false

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
