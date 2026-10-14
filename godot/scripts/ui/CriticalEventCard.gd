extends Control

## CriticalEventCard — the critical channel of the typed event bus (roadmap
## U2-06): a critical event stops the clock and opens one card with what
## happened, where, by when, and what to do about it.
##
##   KRITISK HENDELSE                                    1 av 2
##   Lusenivået er over grensen
##   Hele selskapet · Dag 12
##   Lus KRITISK: 0,74 / fisk (grense 0,5 — pålegg om tiltak)
##   [Planlegg avlusing]  [Vis lokaliteten]           [Fortsett]
##
## Exactly one card per critical event: events that arrive while a card is
## open wait their turn ("1 av 2"). The clock stops while a card is open
## when "Pause ved kritiske hendelser" is on (GameplayPrefs), and runs again
## after Fortsett on the last card, unless the player paused it meanwhile or
## acted on the event: then the game stays paused for the decision.
##
## Keyboard: Enter takes the focused action (the verb first), Esc continues;
## the game's own keys (Space, N, I) wait while a card is open.
## Reduced motion (MotionPrefs): the card fades in place, without the rise.

signal card_shown(event: Dictionary)
signal card_closed(event: Dictionary, action: String)
## The clock changed because of the card (Main.gd keeps the HUD in step).
signal pause_changed

const PRESENTER_GROUP := &"critical_event_presenter"
const FADE_IN_S := 0.2
const FADE_OUT_S := 0.14
const RISE_PX := 14.0
## Game keys that wait while a card is open.
const BLOCKED_ACTIONS := [&"toggle_pause", &"toggle_notifications", &"toggle_attention_inbox"]

@onready var _backdrop: ColorRect = %Backdrop
@onready var _card: PanelContainer = %Card
@onready var _severity_label: Label = %SeverityLabel
@onready var _queue_label: Label = %QueueLabel
@onready var _title: Label = %TitleLabel
@onready var _meta: Label = %MetaLabel
@onready var _message: Label = %MessageLabel
@onready var _deadline: Label = %DeadlineLabel
@onready var _hint: Label = %HintLabel
@onready var verb_button: Button = %VerbButton
@onready var site_button: Button = %SiteButton
@onready var continue_button: Button = %ContinueButton

## Cards shown since the scene started (QA and tests).
var shown_count: int = 0

var _sim: Node = null
var _queue: Array[Dictionary] = []
var _current: Dictionary = {}
var _paused_by_card: bool = false
var _fade: Tween = null
## The event bus's generation the open and waiting cards belong to.
var _generation: int = -1


func _ready() -> void:
	add_to_group(PRESENTER_GROUP)
	# A toast that arrives with the card never covers it.
	_card.add_to_group(&"toast_keep_clear")
	_sim = get_node_or_null("/root/SimBridge")
	_severity_label.text = tr("UI_EVENT_CARD_OVERLINE")
	# A solid alarm tag: Ink text on the crit fill (D13's "Ink on a tone"
	# pairing; the tinted pill read 4.4:1).
	_severity_label.add_theme_color_override("font_color", get_theme_color("on_salmon", "Palette"))
	site_button.text = tr("UI_EVENT_CARD_SHOW_SITE")
	continue_button.text = tr("UI_EVENT_CARD_CONTINUE")
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		_generation = int(bridge.GameEventGeneration)
		bridge.GameStarted.connect(_on_game_started)
	verb_button.pressed.connect(_on_verb_pressed)
	site_button.pressed.connect(_on_site_pressed)
	continue_button.pressed.connect(_on_continue_pressed)
	UIPolish.attach_press_feedback(_card)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	hide()


# ── Public API ─────────────────────────────────────────────────────────────────

## Show a critical event (queued behind an open card).
func present(event: Dictionary) -> void:
	_follow_generation(int(event.get("generation", _generation)))
	_queue.append(event)
	if _current.is_empty():
		_show_next()
	else:
		_update_queue_label()


func is_open() -> bool:
	return not _current.is_empty()


## The event on the open card ({} when closed).
func current_event() -> Dictionary:
	return _current


## Events waiting behind the open card.
func queued_count() -> int:
	return _queue.size()


# ── Flow ───────────────────────────────────────────────────────────────────────

func _show_next() -> void:
	_current = _queue.pop_front()
	_fill(_current)
	if not visible:
		_open()
	shown_count += 1
	card_shown.emit(_current)
	var first: Button = verb_button if verb_button.visible else continue_button
	first.grab_focus.call_deferred()


func _open() -> void:
	if _sim != null and GameplayPrefs.auto_pause_critical() and not _sim.is_paused():
		_sim.set_paused(true)
		_paused_by_card = true
		pause_changed.emit()
	_hint.text = tr("UI_EVENT_CARD_HINT_PAUSED") if _paused_by_card else tr("UI_EVENT_CARD_HINT_RUNNING")
	mouse_filter = Control.MOUSE_FILTER_STOP
	show()
	_animate(true)


## The player answered the open card: show the next one, or close.
## keep_paused: the player acted on the event, so the game waits for them.
func _finish(action: String, keep_paused: bool) -> void:
	var event := _current
	_current = {}
	card_closed.emit(event, action)
	# Seen on its card: not unread in the inbox.
	var inbox := get_tree().get_first_node_in_group(&"event_inbox")
	if inbox != null and inbox.has_method("mark_read"):
		inbox.mark_read(str(event.get("id", "")))
	if keep_paused:
		_paused_by_card = false
	if not _queue.is_empty():
		_show_next()
		return
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_animate(false)
	if _paused_by_card:
		_paused_by_card = false
		if _sim != null and _sim.is_paused():
			_sim.set_paused(false)
			pause_changed.emit()


## A new or loaded game: the old game's cards go, without touching the clock
## (the new game sets its own).
func _on_game_started() -> void:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		_follow_generation(int(bridge.GameEventGeneration))

func _follow_generation(generation: int) -> void:
	if generation == _generation:
		return
	_generation = generation
	_queue.clear()
	_paused_by_card = false
	if not _current.is_empty():
		_current = {}
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		hide()


func _on_continue_pressed() -> void:
	_finish("continue", false)


func _on_verb_pressed() -> void:
	var event := _current
	_finish("verb", true)
	GameEventActions.perform(event, owner)


func _on_site_pressed() -> void:
	var event := _current.duplicate()
	event["counter_verb"] = "inspect_site"
	_finish("site", true)
	GameEventActions.perform(event, owner)


func _input(event: InputEvent) -> void:
	if _current.is_empty():
		return
	if event.is_action_pressed("ui_cancel"):
		_on_continue_pressed()
		get_viewport().set_input_as_handled()
		return
	for action in BLOCKED_ACTIONS:
		if event.is_action(action):
			get_viewport().set_input_as_handled()
			return


# ── Content ────────────────────────────────────────────────────────────────────

func _fill(event: Dictionary) -> void:
	_title.text = GameEventText.title(event)
	_meta.text = GameEventText.meta_line(event, _sim)
	var message := str(event.get("message", ""))
	_message.text = message
	_message.visible = message != ""
	var today: int = int(_sim.current_day) if _sim != null else int(event.get("day", 0))
	var deadline := GameEventText.deadline_line(event, today)
	_deadline.text = deadline
	_deadline.visible = deadline != ""
	var has_verb := GameEventText.has_verb(event) and str(event.get("counter_verb", "")) != "inspect_site"
	verb_button.visible = has_verb
	if has_verb:
		verb_button.text = GameEventText.verb_label(str(event.get("counter_verb", "")))
	# "Vis lokaliteten" flies to the site; the verb already does for inspect_site.
	site_button.visible = str(event.get("site_id", "")) != ""
	if str(event.get("counter_verb", "")) == "inspect_site" and not has_verb:
		site_button.visible = true
	continue_button.theme_type_variation = &"LargeGhostButton" if (has_verb or site_button.visible) else &"LargePrimaryButton"
	_update_queue_label()


func _update_queue_label() -> void:
	var total := _queue.size() + (0 if _current.is_empty() else 1)
	_queue_label.visible = total > 1
	_queue_label.text = tr("UI_EVENT_CARD_QUEUE") % [1, total]
	continue_button.text = tr("UI_EVENT_CARD_NEXT") if not _queue.is_empty() else tr("UI_EVENT_CARD_CONTINUE")


# ── Motion ─────────────────────────────────────────────────────────────────────

func _animate(opening: bool) -> void:
	if _fade != null and _fade.is_valid():
		_fade.kill()
	var still := MotionPrefs.reduced_motion()
	_fade = create_tween().set_parallel(true)
	if opening:
		modulate.a = 0.0
		_fade.tween_property(self, "modulate:a", 1.0, FADE_IN_S).set_ease(Tween.EASE_OUT)
		if not still:
			_rise_in()
	else:
		_fade.tween_property(self, "modulate:a", 0.0, FADE_OUT_S).set_ease(Tween.EASE_IN)
		_fade.chain().tween_callback(func() -> void:
			if _current.is_empty():
				hide())


## The card sits in a CenterContainer: offset it once the container has
## placed it, then let it settle.
func _rise_in() -> void:
	await get_tree().process_frame
	if not is_instance_valid(_card):
		return
	var rest := _card.position.y
	_card.position.y = rest + RISE_PX
	create_tween().tween_property(_card, "position:y", rest, FADE_IN_S) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
