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
const DISEASE_KEYS := ["sykdom", "disease", "outbreak", "lus", "sea lice", "sealice", "ila", "smitte"]
const GE_KEYS := ["aquadvantage", "genmodifisert", "genteknolog", "genredigering", "genome editing", "gmo", "gene edit", "ge_disease", "ge_growth", "ge_lice", "ge_sterility"]
const INSURANCE_KEYS := ["forsikring", "insurance"]
const TRAFFIC_PREFIX := "trafikklys"

var _sim: Node = null
var _game_bridge: Node = null
var _toasts: Node = null
var _audio: Node = null

var _dedup: Dictionary = {} # category key -> ticks_msec
var _seen_event_keys: Dictionary = {}
var _events_primed: bool = false

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
	if msg.contains("vær:") or msg.contains("storm"):
		return
	var lvl := level.to_upper()
	if _has_any(msg, ESCAPE_KEYS):
		_fire_critical("escape", "🚨 %s" % message)
	elif _has_any(msg, BLOOM_KEYS):
		_fire_warning("bloom", message)
	elif _has_any(msg, DISEASE_KEYS):
		_fire_warning("disease", message)
	elif lvl == "CRITICAL":
		_fire_critical("critical_alert", message)

func _on_achievement_unlocked(id: String, achievement_name: String, _description: String, _reward_nok: float, _tier: String) -> void:
	if _is_duplicate("achievement:%s" % id):
		return
	_toast("🏆 %s" % achievement_name, "SUCCESS", 6.0)
	_audio_call("play_achievement")
	_pulse_feedback()

func _on_pen_action_completed(action: String, pen_id: String, success: bool) -> void:
	if not success or action != "harvest":
		return
	if _is_duplicate("harvest:%s" % pen_id):
		return
	_toast("🐟 Høsting fullført — %s" % pen_id, "SUCCESS", 5.0)
	_audio_call("play_harvest_stinger")
	_pulse_feedback()

func _on_farm_action_completed(action: String, site_id: String, success: bool) -> void:
	if not success:
		return
	match action:
		"harvest":
			if _is_duplicate("harvest:%s" % site_id):
				return
			_toast("🐟 Høsting fullført — %s" % site_id, "SUCCESS", 5.0)
			_audio_call("play_harvest_stinger")
			_pulse_feedback()
		"mitigation", "countermeasure":
			if _is_duplicate("mitigation:%s" % site_id):
				return
			_toast("🛡️ Mottiltak installert — %s" % site_id, "SUCCESS", 4.0)
			_audio_call("play_mitigation_install")
			_pulse_feedback()
		"ge", "deploy_ge", "ge_deploy", "ge_deployment":
			if _is_duplicate("ge:%s" % site_id):
				return
			_toast("🧬 GE-trakt satt ut — %s" % site_id, "WARNING", 5.0)
			_audio_call("play_ge_deployment")
			_pulse_feedback()

## Red-zone / insurance / GE changes surface through the event log, so poll the
## SimBridge snapshot rather than inventing new C# signals. First snapshot only
## primes the seen-set (no retro stingers for events that predate this session).
func _on_state_updated() -> void:
	if _sim == null:
		return
	var events: Variant = _sim.get("recent_events")
	if events == null:
		return
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

func _dispatch_log_event(name: String, desc: String) -> void:
	var lname := name.to_lower()
	if _has_any(lname, INSURANCE_KEYS) or _has_any(desc.to_lower(), INSURANCE_KEYS):
		_toast("🛡️ Forsikringsutbetaling: %s" % desc, "SUCCESS", 6.0)
		_audio_call("play_insurance_payout")
		return
	if lname.begins_with(TRAFFIC_PREFIX):
		var is_red := lname.contains("red")
		_toast(("🔴 " if is_red else "🚦 ") + name, "WARNING" if is_red else "SUCCESS", 5.0)
		_audio_call("play_red_zone_alert", [is_red])
		return
	if _looks_like_ge(name, desc):
		_toast("🧬 %s" % (name if not name.is_empty() else "GE-utsetting"), "WARNING", 5.0)
		_audio_call("play_ge_deployment")

func _on_finance_transaction(type: String, _amount: float, description: String) -> void:
	if not _looks_like_ge(type, description):
		return
	if _is_duplicate("ge_finance:%s" % description):
		return
	_toast("🧬 %s" % (description if not description.is_empty() else "GE-utsetting"), "WARNING", 5.0)
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
		_fire_critical("storm", "⛈ Storm: %s" % description)
	# Mild weather is logged by NotificationCenter — no theater needed.

func _on_disease_outbreak(disease_type: String, site_id: String, severity: float, _treatment_cost: float) -> void:
	if disease_type == "Escape":
		_fire_critical("escape", "🚨 RØMMING @ %s — reparer nøtene nå" % site_id)
	elif severity >= 0.5:
		_fire_critical("disease", "🦠 Sykdom (%s @ %s)" % [disease_type, site_id])
	else:
		_fire_warning("disease", "⚠ Sykdom (%s @ %s)" % [disease_type, site_id])

func _on_game_ended(reason: String) -> void:
	if _is_duplicate("game_ended"):
		return
	_shake(20.0, 0.6)
	_toast("💥 Spillet er over: %s" % reason, "CRITICAL", 9.0)
	_audio_call("play_ui", ["notification"])

# ── Consequence primitives ─────────────────────────────────────────────────────

func _fire_critical(category: String, message: String) -> void:
	if _is_duplicate(category):
		return
	_shake(16.0, 0.4)
	_toast(message, "CRITICAL", 7.0)
	_audio_call("play_ui", ["notification"])

func _fire_warning(category: String, message: String) -> void:
	if _is_duplicate(category):
		return
	_flash_warning()
	_toast(message, "WARNING", 6.0)
	_audio_call("play_ui", ["notification"])

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

func _is_duplicate(key: String) -> bool:
	var now := Time.get_ticks_msec()
	for k in _dedup.keys():
		if now - int(_dedup[k]) > DEDUP_WINDOW_MS:
			_dedup.erase(k)
	if _dedup.has(key):
		return true
	_dedup[key] = now
	return false
