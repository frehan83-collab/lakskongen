extends Node

## ConsequenceDirector — "consequence theater" autoload.
## Listens to SimBridge (and GameBridge for GameEnded) and turns meaningful
## simulation events into tangible, coordinated feedback: screen shake, toasts,
## flashes, pulses, and guarded audio. It only reads signals — it never calls
## simulation mutators.
##
## Register as an autoload (do NOT register before SimBridge / ToastNotification):
##   ConsequenceDirector="*res://scripts/ui/ConsequenceDirector.gd"

# Events sharing a category are coalesced so a single situation doesn't fire
# twice when it arrives via more than one bridge signal.
const DEDUP_WINDOW_MS := 2000

const ESCAPE_KEYS := ["rømming", "rømte", "escape"]
const BLOOM_KEYS  := ["oppblomstring", "bloom", "alge", "algae", "manet", "jellyfish", "hypoksi", "hypoxia"]
const DISEASE_KEYS := ["sykdom", "disease", "outbreak", "ila", "smitte"]
# Depth events now have their own severity + audio (task: lice / DO / gill).
const LICE_KEYS := ["lus", "sea lice", "sealice"]
const OXYGEN_KEYS := ["oksygen", "oxygen"]
const GILL_KEYS := ["gjelle", "gill"]
const GE_KEYS := ["aquadvantage", "genmodifisert", "genteknolog", "genredigering", "genome editing", "gmo", "gene edit", "ge_disease", "ge_growth", "ge_lice", "ge_sterility"]
const INSURANCE_KEYS := ["forsikring", "insurance"]
const TRAFFIC_PREFIX := "trafikklys"

## UI keys AudioController.play_ui understands (MusicLibrary.UI_SFX). Used to
## tell a genuine playable audio key from a semantic alert key we can't cue
## directly (alert SFX such as "lice_warning" are emitted by SimBridge).
const UI_SOUND_KEYS := ["button_click", "button_hover", "notification", "error", "menu_open", "menu_close", "panel_open", "panel_close"]

var _sim: Node = null
var _game_bridge: Node = null
var _toasts: Node = null
var _audio: Node = null

var _dedup: Dictionary = {} # category key -> ticks_msec
var _seen_event_keys: Dictionary = {}
var _events_primed: bool = false
var _day_dedup: Dictionary = {} # once-per-day key -> day number
var _last_depth_scan_day: int = -1

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_game_bridge = get_node_or_null("/root/GameBridge")
	_toasts = get_node_or_null("/root/ToastNotification")
	_audio = get_node_or_null("/root/AudioController")

	_connect_signal(_sim, "alert_raised", "_on_alert_raised")
	_connect_signal(_sim, "achievement_unlocked", "_on_achievement_unlocked")
	_connect_signal(_sim, "pen_action_completed", "_on_pen_action_completed")
	_connect_signal(_sim, "state_updated", "_on_state_updated")
	_connect_signal(_game_bridge, "WeatherEvent", "_on_weather_event")
	_connect_signal(_game_bridge, "DiseaseOutbreak", "_on_disease_outbreak")
	_connect_signal(_game_bridge, "GameEnded", "_on_game_ended")
	_connect_signal(_game_bridge, "FarmActionCompleted", "_on_farm_action_completed")
	_connect_signal(_game_bridge, "FinanceTransaction", "_on_finance_transaction")

func _connect_signal(source: Node, signal_name: String, method_name: String) -> void:
	if source == null or not source.has_signal(signal_name):
		return
	var cb := Callable(self, method_name)
	if source.is_connected(signal_name, cb):
		return
	source.connect(signal_name, cb)

# ── SimBridge handlers ─────────────────────────────────────────────────────────

func _on_alert_raised(level: String, message: String) -> void:
	var msg := message.to_lower()
	# Weather/storm consequences are owned by GameBridge.WeatherEvent; skipping
	# here avoids double-firing and misclassifying "stormfare" (risk) as a storm.
	if msg.contains("vær:") or msg.contains("weather:") or msg.contains("storm"):
		return
	var lvl := level.to_upper()
	if _has_any(msg, ESCAPE_KEYS):
		_fire_critical("escape", "🚨 %s" % message)
	elif _has_any(msg, LICE_KEYS):
		_on_lice_alert(lvl, message)
	elif _has_any(msg, OXYGEN_KEYS):
		_on_oxygen_alert(lvl, message)
	elif _has_any(msg, GILL_KEYS):
		_on_gill_alert(message, false)
	elif _has_any(msg, BLOOM_KEYS):
		_fire_warning("bloom", message)
	elif _has_any(msg, DISEASE_KEYS):
		_fire_warning("disease", message)
	elif lvl == "CRITICAL":
		_fire_critical("critical_alert", message)

## Lice crossing the legal limit. Severity follows the alert level; deduped per
## day per severity so a persistent condition reports once (warning -> critical
## still escalates the same day). Audio key "lice_warning" is also cued by
## SimBridge.audio_alert_triggered.
func _on_lice_alert(level: String, message: String) -> void:
	var critical := level == "CRITICAL"
	var key := "lice_critical" if critical else "lice_warning"
	if _seen_today(key, _current_day()):
		return
	if critical:
		_fire_critical(key, "🐟 %s" % message, "lice_warning")
	else:
		_fire_warning(key, "🐟 %s" % message, "lice_warning")

## Dissolved-oxygen crash. Critical below 6 mg/L, warning for the pre-crash band.
func _on_oxygen_alert(level: String, message: String) -> void:
	var critical := level == "CRITICAL"
	var key := "oxygen_critical" if critical else "oxygen_warning"
	if _seen_today(key, _current_day()):
		return
	if critical:
		_fire_critical(key, "🫧 %s" % message, "error")
	else:
		_fire_warning(key, "🫧 %s" % message, "notification")

## Gill-health warning. Fires only on an explicit alert message; the polled
## proxy path calls this with is_critical = true for critical sites.
func _on_gill_alert(message: String, is_critical: bool, audio_key: String = "") -> void:
	var key := "gill_critical" if is_critical else "gill_warning"
	if _seen_today(key, _current_day()):
		return
	if is_critical:
		_fire_critical(key, "🫁 %s" % message, audio_key if audio_key != "" else "error")
	else:
		_fire_warning(key, "🫁 %s" % message, audio_key if audio_key != "" else "notification")

func _on_achievement_unlocked(id: String, achievement_name: String, _description: String, medal: String, _tier: String) -> void:
	if _is_duplicate("achievement:%s" % id):
		return
	# Achievements award a medal, never cash (D16): the toast names it.
	var medal_key := "UI_MEDAL_" + medal.to_upper()
	var medal_name := tr(medal_key)
	if medal_name == medal_key:
		medal_name = medal.capitalize()
	_toast("🏆 %s — %s" % [achievement_name, medal_name], "SUCCESS", 6.0)
	_audio_call("play_achievement")
	_pulse_feedback()

func _on_pen_action_completed(action: String, pen_id: String, success: bool) -> void:
	if not success or action != "harvest":
		return
	if _is_duplicate("harvest:%s" % pen_id):
		return
	_toast("🐟 " + tr("UI_TOAST_HARVEST_OK") % _site(pen_id), "SUCCESS", 5.0)
	_audio_call("play_harvest_stinger")
	_pulse_feedback()

func _on_farm_action_completed(action: String, site_id: String, success: bool) -> void:
	if not success:
		return
	match action:
		"harvest":
			if _is_duplicate("harvest:%s" % site_id):
				return
			_toast("🐟 " + tr("UI_TOAST_HARVEST_OK") % _site(site_id), "SUCCESS", 5.0)
			_audio_call("play_harvest_stinger")
			_pulse_feedback()
		"mitigation", "countermeasure":
			if _is_duplicate("mitigation:%s" % site_id):
				return
			_toast("🛡️ " + tr("UI_TOAST_COUNTERMEASURE_OK") % _site(site_id), "SUCCESS", 4.0)
			_audio_call("play_mitigation_install")
			_pulse_feedback()
		"ge", "deploy_ge", "ge_deploy", "ge_deployment":
			if _is_duplicate("ge:%s" % site_id):
				return
			_toast("🧬 " + tr("UI_TOAST_GE_DEPLOYED") % _site(site_id), "WARNING", 5.0)
			_audio_call("play_ge_deployment")
			_pulse_feedback()

## Red-zone / insurance / GE changes surface through the event log, so poll the
## SimBridge snapshot rather than inventing new C# signals. First snapshot only
## primes the seen-set (no retro stingers for events that predate this session).
func _on_state_updated() -> void:
	if _sim == null:
		return
	var events: Variant = _sim.get("recent_events")
	if events != null:
		for ev in events:
			if not (ev is Dictionary):
				continue
			var name := str(ev.get("name", ""))
			var desc := str(ev.get("description", ""))
			var day := int(ev.get("day", 0))
			var key := "%d|%s|%s" % [day, name, desc]
			if _seen_event_keys.has(key):
				continue
			_seen_event_keys[key] = true
			if _events_primed:
				_dispatch_log_event(name, desc)
		_events_primed = true
		_prune_seen_events()
	_scan_depth_once_per_day()

## Depth events (lice/DO/gill) mostly surface as alerts, but gill-health risk
## is only exposed through the get_gill_risk proxy. Scan it once per simulated
## day so a persistent warning reports exactly once, not on every snapshot.
func _scan_depth_once_per_day() -> void:
	if _sim == null:
		return
	var day := _current_day()
	if day < 0 or day == _last_depth_scan_day:
		return
	_last_depth_scan_day = day
	if not _sim.has_method("get_all_pens") or not _sim.has_method("get_gill_risk"):
		return
	for pen in _sim.get_all_pens():
		if not (pen is Dictionary):
			continue
		var sid := str(pen.get("id", ""))
		if sid.is_empty():
			continue
		var g = _sim.get_gill_risk(sid)
		if not (g is Dictionary):
			continue
		if bool(g.get("critical", false)):
			_on_gill_alert("Gjellehelsen er kritisk på %s" % str(_sim.site_name(sid)), true, "error")
		elif bool(g.get("warning", false)):
			_on_gill_alert("Varsel om gjellehelse på %s" % str(_sim.site_name(sid)), false, "notification")

func _dispatch_log_event(name: String, desc: String) -> void:
	var lname := name.to_lower()
	if _has_any(lname, INSURANCE_KEYS) or _has_any(desc.to_lower(), INSURANCE_KEYS):
		_toast("🛡️ " + tr("UI_TOAST_INSURANCE_PAYOUT") % desc, "SUCCESS", 6.0)
		_audio_call("play_insurance_payout")
		return
	if lname.begins_with(TRAFFIC_PREFIX):
		var is_red := lname.contains("rød") or lname.contains("red")
		_toast(("🔴 " if is_red else "🚦 ") + name, "WARNING" if is_red else "SUCCESS", 5.0)
		_audio_call("play_red_zone_alert", [is_red])
		return
	if _looks_like_ge(name, desc):
		_toast("🧬 %s" % (name if not name.is_empty() else tr("UI_TOAST_GE_DEPLOYED_SHORT")), "WARNING", 5.0)
		_audio_call("play_ge_deployment")

func _on_finance_transaction(type: String, _amount: float, description: String) -> void:
	if not _looks_like_ge(type, description):
		return
	if _is_duplicate("ge_finance:%s" % description):
		return
	_toast("🧬 %s" % (description if not description.is_empty() else tr("UI_TOAST_GE_DEPLOYED_SHORT")), "WARNING", 5.0)
	_audio_call("play_ge_deployment")

func _looks_like_ge(text_a: String, text_b: String) -> bool:
	var haystack := (text_a + " " + text_b).to_lower()
	return _has_any(haystack, GE_KEYS)

func _prune_seen_events() -> void:
	if _seen_event_keys.size() <= 256:
		return
	var keys := _seen_event_keys.keys()
	for i in range(mini(64, keys.size())):
		_seen_event_keys.erase(keys[i])

# ── GameBridge handlers ────────────────────────────────────────────────────────

func _on_weather_event(event_type: String, severity: float, description: String) -> void:
	if severity >= 0.8 or event_type.to_lower().contains("storm"):
		_fire_critical("storm", "⛈ %s" % description)
	# Mild weather is logged by NotificationCenter — no theater needed.

func _on_disease_outbreak(disease_type: String, site_id: String, severity: float, _treatment_cost: float) -> void:
	var where := tr("UI_ALL_SITES") if site_id == "all_sites" else _site(site_id)
	if disease_type == "Escape":
		_fire_critical("escape", "🚨 " + tr("UI_TOAST_ESCAPE") % where)
	elif severity >= 0.5:
		_fire_critical("disease", "🦠 " + tr("UI_TOAST_DISEASE") % [SimLabels.disease(disease_type), where])
	else:
		_fire_warning("disease", "⚠ " + tr("UI_TOAST_DISEASE") % [SimLabels.disease(disease_type), where])

func _on_game_ended(reason: String) -> void:
	if _is_duplicate("game_ended"):
		return
	_shake(20.0, 0.6)
	# The reason is a translation key when the engine ended the game
	# (GAME_OVER_BANKRUPTCY, S1-03); tr() leaves other text as it is.
	_toast("💥 " + tr("UI_TOAST_GAME_OVER") % tr(reason), "CRITICAL", 9.0)
	_audio_call("play_ui", ["notification"])

# ── Consequence primitives ─────────────────────────────────────────────────────

func _fire_critical(category: String, message: String, audio_key: String = "") -> void:
	if _is_duplicate(category):
		return
	_shake(16.0, 0.4)
	_toast(message, "CRITICAL", 7.0)
	_play_cue(audio_key, "error")

func _fire_warning(category: String, message: String, audio_key: String = "") -> void:
	if _is_duplicate(category):
		return
	_flash_warning()
	_toast(message, "WARNING", 6.0)
	_play_cue(audio_key, "notification")

## Play the preferred audio key when AudioController can address it, otherwise
## fall back to the severity default. Alert SFX keys (e.g. "lice_warning") are
## not play_ui-addressable and are already emitted by SimBridge.
func _play_cue(preferred_key: String, fallback_key: String) -> void:
	if _audio == null:
		return
	var key := preferred_key if _is_ui_sound_key(preferred_key) else fallback_key
	_audio_call("play_ui", [key])

func _is_ui_sound_key(key: String) -> bool:
	return UI_SOUND_KEYS.has(key)

func _shake(intensity: float, duration: float) -> void:
	var vp := get_viewport()
	if vp:
		JuiceEffects.screen_shake(vp, intensity, duration)

func _pulse_feedback() -> void:
	var target := _feedback_node()
	if target:
		JuiceEffects.pulse(target, 1.05, 0.35)

func _flash_warning() -> void:
	var target := _feedback_node()
	if target:
		JuiceEffects.flash_warning(target)

func _feedback_node() -> Control:
	var hud := get_node_or_null("/root/HUD")
	if hud is Control:
		return hud as Control
	var ui := get_node_or_null("/root/GameUI")
	if ui is Control:
		return ui as Control
	return null

func _toast(message: String, level: String, duration: float) -> void:
	if _toasts == null or not _toasts.has_method("show_toast"):
		return
	_toasts.show_toast(message, level, duration)

func _audio_call(method_name: String, args: Array = []) -> void:
	if _audio == null or not _audio.has_method(method_name):
		return
	_audio.callv(method_name, args)

# ── Utilities ──────────────────────────────────────────────────────────────────

func _has_any(haystack: String, keys: Array) -> bool:
	for key in keys:
		if haystack.contains(key):
			return true
	return false

func _current_day() -> int:
	if _sim == null:
		return -1
	var d = _sim.get("current_day")
	return int(d) if d != null else -1

## Returns true when `key` was already fired for `day` (so the caller skips).
## A day of -1 (no sim clock) makes this a no-op, leaving _is_duplicate to
## coalesce bursts within the 2s window.
func _seen_today(key: String, day: int) -> bool:
	if day < 0:
		return false
	if int(_day_dedup.get(key, -1)) == day:
		return true
	_day_dedup[key] = day
	if _day_dedup.size() > 256:
		var keys := _day_dedup.keys()
		for i in range(mini(64, keys.size())):
			_day_dedup.erase(keys[i])
	return false

func _is_duplicate(key: String) -> bool:
	var now := Time.get_ticks_msec()
	for k in _dedup.keys():
		if now - int(_dedup[k]) > DEDUP_WINDOW_MS:
			_dedup.erase(k)
	if _dedup.has(key):
		return true
	_dedup[key] = now
	return false


## The name the player reads for a site id.
func _site(site_id: String) -> String:
	return str(_sim.site_name(site_id)) if _sim and _sim.has_method("site_name") else site_id
