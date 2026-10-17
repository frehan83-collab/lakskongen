extends Node

## ConsequenceDirector — "consequence theater" autoload and the router of the
## typed event bus (roadmap U2-06). It only reads signals and never calls
## simulation mutators.
##
## One channel per severity, decided by the event's typed severity alone
## (nothing here reads alert text):
##   critical  pause + one modal card with actions (CriticalEventCard), a short
##             camera shake (none under reduced motion) and the alarm cue;
##   warning   one toast (the inbox entry and the HUD badge come from the
##             NotificationCenter, which hears every event) and a soft cue;
##   info      the log only (the NotificationCenter).
## The bus has already dropped repeats (same category and subject within three
## days) and marked escalations, so every event that reaches here is news.
## An event from days skipped at once that is three or more days old is
## history: logged, not announced (U1-05).
##
## The player's own actions get their payoff here too (a harvest, a medal, a
## countermeasure): success toasts, separate from the event channels.
##
## Register as an autoload (after SimBridge / ToastNotification):
##   ConsequenceDirector="*res://scripts/ui/ConsequenceDirector.gd"

## The channel an event was routed to ("modal" | "toast" | "log").
signal event_routed(event: Dictionary, channel: String)
## A harvest's one payoff was announced (U2-05): its holding and the key of
## its report (the order id, or the batch's report id).
signal harvest_announced(holding_id: String, report_key: String)

const CHANNEL_MODAL := "modal"
const CHANNEL_TOAST := "toast"
const CHANNEL_LOG := "log"

## An event this many days old (or older) when it arrives is history.
const STALE_EVENT_DAYS := 3
## Coalesces a player-action payoff that arrives through two signals.
const DEDUP_WINDOW_MS := 2000
## Camera trauma (0..1) of a critical event and of the end of a game.
const CRITICAL_SHAKE_TRAUMA := 0.55
const GAME_OVER_SHAKE_TRAUMA := 0.8
const WARNING_TOAST_SECONDS := 6.0

## UI keys AudioController.play_ui understands (MusicLibrary.UI_SFX).
const UI_SOUND_KEYS := ["button_click", "button_hover", "notification", "error", "menu_open", "menu_close", "panel_open", "panel_close"]

var _sim: Node = null
var _game_bridge: Node = null
var _toasts: Node = null
var _audio: Node = null

var _dedup: Dictionary = {} # payoff key -> ticks_msec

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_game_bridge = get_node_or_null("/root/GameBridge")
	_toasts = get_node_or_null("/root/ToastNotification")
	_audio = get_node_or_null("/root/AudioController")

	_connect_signal(_sim, "game_event_raised", "_on_game_event")
	_connect_signal(_sim, "achievement_unlocked", "_on_achievement_unlocked")
	_connect_signal(_sim, "pen_action_completed", "_on_pen_action_completed")
	_connect_signal(_game_bridge, "GameEnded", "_on_game_ended")
	_connect_signal(_game_bridge, "FarmActionCompleted", "_on_farm_action_completed")

func _connect_signal(source: Node, signal_name: String, method_name: String) -> void:
	if source == null or not source.has_signal(signal_name):
		return
	var cb := Callable(self, method_name)
	if source.is_connected(signal_name, cb):
		return
	source.connect(signal_name, cb)

# ── The event channels ─────────────────────────────────────────────────────────

## The channel for an event on `today` (pure: the routing rule, testable).
static func route_for(event: Dictionary, today: int) -> String:
	if today >= 0 and today - int(event.get("day", today)) >= STALE_EVENT_DAYS:
		return CHANNEL_LOG
	match str(event.get("severity", "info")):
		"critical":
			return CHANNEL_MODAL
		"warning":
			return CHANNEL_TOAST
	return CHANNEL_LOG

func _on_game_event(event: Dictionary) -> void:
	var channel := route_for(event, _today())
	match channel:
		CHANNEL_MODAL:
			_present_critical(event)
		CHANNEL_TOAST:
			_present_warning(event)
	event_routed.emit(event, channel)

## Critical: the card (with the pause) where a game screen is running; a
## critical toast stands in for it anywhere else.
func _present_critical(event: Dictionary) -> void:
	var card := get_tree().get_first_node_in_group(&"critical_event_presenter")
	if card != null and card.has_method("present"):
		card.present(event)
	else:
		_toast(_toast_text(event), "CRITICAL", 8.0)
	CameraShake.shake(get_viewport(), CRITICAL_SHAKE_TRAUMA)
	_play_cue("error")

## Warning: exactly one toast. Clicking it opens the notification drawer.
func _present_warning(event: Dictionary) -> void:
	_toast(_toast_text(event), "WARNING", WARNING_TOAST_SECONDS, _open_inbox)
	_play_cue("notification")

## The alert texts already say what they are about ("Lus forhøyet: 0,32 /
## fisk …"), so a toast shows the message; an event with its own headline (a
## storyteller event) leads with it, and one without a message shows its title.
func _toast_text(event: Dictionary) -> String:
	var message := str(event.get("message", ""))
	var headline := str(event.get("headline", ""))
	if message == "":
		return GameEventText.title(event)
	return message if headline == "" else "%s — %s" % [headline, message]

func _open_inbox() -> void:
	var inbox := get_tree().get_first_node_in_group(&"event_inbox")
	if inbox != null and inbox.has_method("show_panel"):
		inbox.show_panel()

## The engine's day: events arrive before the snapshot refreshes, when
## SimBridge.current_day may still hold yesterday.
func _today() -> int:
	if _game_bridge != null:
		return int(_game_bridge.CurrentDay)
	var d: Variant = _sim.get("current_day") if _sim != null else null
	return int(d) if d != null else -1

# ── Player-action payoffs (success toasts) ─────────────────────────────────────

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

func _on_pen_action_completed(action: String, pen_id: String, success: bool) -> void:
	if not success or action != "harvest":
		return
	_harvest_payoff(pen_id)


## A harvest's payoff (U2-05): exactly one toast per harvest, with its net
## and a click to its report, and the harvest stinger. A planned order is one
## harvest however many slaughter days it takes (its last batch announces
## it); the same harvest heard through a second signal is not announced again.
func _harvest_payoff(holding_id: String) -> void:
	var key := HarvestReport.latest_key_for(_sim, holding_id)
	if _is_duplicate("harvest:%s" % (key if not key.is_empty() else holding_id)):
		return
	var text := tr("UI_TOAST_HARVEST_OK") % _site(holding_id)
	if not key.is_empty():
		var batches: Array = _sim.get_harvest_reports().filter(func(r: Dictionary) -> bool:
			return str(r.get("order_id", "")) == key or str(r.get("id", "")) == key)
		var sum := HarvestReport.sum_batches(batches)
		text = tr("UI_TOAST_HARVEST_DONE") % [_site(holding_id), SimFormatters.format_biomass(float(sum.get("hog_kg", 0.0))),
			SimFormatters.format_nok(float(sum.get("net_nok", 0.0)))]
	_toast(text, "SUCCESS", 7.0, _open_harvest_report.bind(key) if not key.is_empty() else Callable())
	_audio_call("play_harvest_stinger")
	harvest_announced.emit(holding_id, key)


func _open_harvest_report(key: String) -> void:
	var sheet := HarvestScreens.report(get_tree())
	if sheet != null:
		sheet.show_harvest(key)

func _on_farm_action_completed(action: String, site_id: String, success: bool) -> void:
	if not success:
		return
	match action:
		"harvest":
			_harvest_payoff(site_id)
		"mitigation", "countermeasure":
			if _is_duplicate("mitigation:%s" % site_id):
				return
			_toast("🛡️ " + tr("UI_TOAST_COUNTERMEASURE_OK") % _site(site_id), "SUCCESS", 4.0)
			_audio_call("play_mitigation_install")
		"ge", "deploy_ge", "ge_deploy", "ge_deployment":
			if _is_duplicate("ge:%s" % site_id):
				return
			_toast("🧬 " + tr("UI_TOAST_GE_DEPLOYED") % _site(site_id), "WARNING", 5.0)
			_audio_call("play_ge_deployment")

func _on_game_ended(reason: String) -> void:
	if _is_duplicate("game_ended"):
		return
	CameraShake.shake(get_viewport(), GAME_OVER_SHAKE_TRAUMA)
	# The reason is a translation key when the engine ended the game
	# (GAME_OVER_BANKRUPTCY, S1-03); tr() leaves other text as it is.
	_toast("💥 " + tr("UI_TOAST_GAME_OVER") % tr(reason), "CRITICAL", 9.0)
	_audio_call("play_ui", ["notification"])

# ── Primitives ─────────────────────────────────────────────────────────────────

func _play_cue(key: String) -> void:
	if _audio != null and UI_SOUND_KEYS.has(key):
		_audio_call("play_ui", [key])

func _toast(message: String, level: String, duration: float, action: Callable = Callable()) -> void:
	if _toasts == null or not _toasts.has_method("show_toast"):
		return
	_toasts.show_toast(message, level, duration, action)

func _audio_call(method_name: String, args: Array = []) -> void:
	if _audio == null or not _audio.has_method(method_name):
		return
	_audio.callv(method_name, args)

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
