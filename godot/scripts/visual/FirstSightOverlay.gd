class_name FirstSightOverlay
extends CanvasLayer
## The screen of a first-sight sequence (roadmap F2-23, FirstSightDirector)
## and of the teaser (TeaserDirector): cinematic letterbox bars, a dip from
## and to black, the skip hint, the end card (the unit's lockup, "Lagre
## bilde", "Fortsett"; the teaser's news card offers "Fortsett" only) and,
## with reduced motion, the still the sequence cross-fades from. It holds no
## state of the sequence: the director sets every alpha from its own clock,
## so a frame at a time t is the same however it was reached (seek()).
##
## Input: while it is up it takes Esc and a click anywhere but the card's
## buttons (skip_requested), before the game's UI sees them (the director
## adds it as the last child of the game scene: the last node takes input
## first), and the game's own keys wait as they do under a card (Space, the
## speeds, N, I: the hidden HUD would change the clock the sequence holds,
## the inbox would open over it).

signal skip_requested()
signal save_requested()
signal continue_requested()

## Draw order: over the HUD (2), the event cards (4) and the menus (5).
const LAYER := 6
## The game's keys that wait while it is up (CriticalEventCard's list).
const BLOCKED_ACTIONS := [&"toggle_pause", &"toggle_notifications", &"toggle_attention_inbox",
	&"next_event", &"speed_1", &"speed_2", &"speed_3", &"speed_4"]
## Letterbox bars, each this share of the frame's height.
const BAR_SHARE := 0.085
## The end card's lockup: left margin and lift over the bottom bar (px).
const CARD_MARGIN := Vector2(64.0, 36.0)
const INK := Color("#0B2A3A")

var bars: Array[ColorRect] = []
var black: ColorRect = null
var still: TextureRect = null
var hint: Label = null
var card: Control = null
var kicker: Label = null
var title: Label = null
var subtitle: Label = null
var save_button: Button = null
var continue_button: Button = null
var _root: Control = null


func _init() -> void:
	layer = LAYER
	name = "FirstSightOverlay"
	_root = Control.new()
	_root.name = "Root"
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)
	still = TextureRect.new()
	still.name = "Still"
	still.set_anchors_preset(Control.PRESET_FULL_RECT)
	still.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	still.stretch_mode = TextureRect.STRETCH_SCALE
	still.mouse_filter = Control.MOUSE_FILTER_IGNORE
	still.visible = false
	_root.add_child(still)
	for top: bool in [true, false]:
		var bar := ColorRect.new()
		bar.name = "BarTop" if top else "BarBottom"
		bar.color = Color.BLACK
		bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		bar.anchor_left = 0.0
		bar.anchor_right = 1.0
		bar.anchor_top = 0.0 if top else 1.0 - BAR_SHARE
		bar.anchor_bottom = BAR_SHARE if top else 1.0
		_root.add_child(bar)
		bars.append(bar)
	_build_card()
	hint = Label.new()
	hint.name = "SkipHint"
	hint.theme_type_variation = &"SmallMuted"
	hint.text = "FIRST_SIGHT_SKIP"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	hint.anchor_left = 0.6
	hint.anchor_right = 1.0
	hint.anchor_top = 1.0 - BAR_SHARE
	hint.anchor_bottom = 1.0
	hint.offset_right = -28.0
	hint.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	hint.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(hint)
	black = ColorRect.new()
	black.name = "Black"
	black.color = Color.BLACK
	black.set_anchors_preset(Control.PRESET_FULL_RECT)
	black.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(black)


func _build_card() -> void:
	card = VBoxContainer.new()
	card.name = "EndCard"
	card.anchor_left = 0.0
	card.anchor_right = 0.6
	card.anchor_top = 1.0 - BAR_SHARE
	card.anchor_bottom = 1.0 - BAR_SHARE
	card.offset_left = CARD_MARGIN.x
	card.offset_top = -220.0
	card.offset_bottom = -CARD_MARGIN.y
	card.grow_vertical = Control.GROW_DIRECTION_BEGIN
	card.alignment = BoxContainer.ALIGNMENT_END
	card.add_theme_constant_override("separation", 6)
	card.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(card)
	var rule := ColorRect.new()
	rule.name = "AccentRule"
	rule.color = Color("#F2876A")
	rule.custom_minimum_size = Vector2(56.0, 3.0)
	rule.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	rule.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card.add_child(rule)
	kicker = _label("Kicker", &"OverlineAccent")
	title = _label("Title", &"Display")
	subtitle = _label("Subtitle", &"BodyStrong")
	var row := HBoxContainer.new()
	row.name = "Buttons"
	row.add_theme_constant_override("separation", 12)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card.add_child(row)
	save_button = Button.new()
	save_button.name = "SaveButton"
	save_button.theme_type_variation = &"LargeGhostButton"
	save_button.text = "FIRST_SIGHT_SAVE_PICTURE"
	save_button.pressed.connect(func() -> void: save_requested.emit())
	row.add_child(save_button)
	continue_button = Button.new()
	continue_button.name = "ContinueButton"
	continue_button.theme_type_variation = &"LargePrimaryButton"
	continue_button.text = "FIRST_SIGHT_CONTINUE"
	continue_button.pressed.connect(func() -> void: continue_requested.emit())
	row.add_child(continue_button)


func _label(node_name: String, variation: StringName) -> Label:
	var l := Label.new()
	l.name = node_name
	l.theme_type_variation = variation
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# The lockup reads over any sky: a soft Ink shadow under the letters.
	l.add_theme_color_override("font_shadow_color", Color(INK.r, INK.g, INK.b, 0.85))
	l.add_theme_constant_override("shadow_offset_x", 0)
	l.add_theme_constant_override("shadow_offset_y", 2)
	l.add_theme_constant_override("shadow_outline_size", 8)
	card.add_child(l)
	return l


## The card's text: the kicker, the title (variant and place), the line under it.
func set_card(kicker_text: String, title_text: String, subtitle_text: String) -> void:
	kicker.text = kicker_text
	title.text = title_text
	subtitle.text = subtitle_text


## Whether the card offers "Lagre bilde" (the first sight's photo-mode
## prompt); the teaser's news card offers "Fortsett" only.
func offer_save(on: bool) -> void:
	save_button.visible = on


## The card's buttons the player can see (tests: what a card offers).
func offered_buttons() -> Array[Button]:
	var out: Array[Button] = []
	for b: Button in [save_button, continue_button]:
		if b.visible:
			out.append(b)
	return out


## Every alpha the director composes for a time: the bars (0 gone .. 1 in),
## the black (0 clear .. 1 black), the end card and the still.
func show_state(bars_in: float, black_alpha: float, card_alpha: float, still_alpha: float, hint_alpha: float) -> void:
	for bar in bars:
		bar.modulate.a = clampf(bars_in, 0.0, 1.0)
	black.modulate.a = clampf(black_alpha, 0.0, 1.0)
	card.modulate.a = clampf(card_alpha, 0.0, 1.0)
	card.visible = card_alpha > 0.001
	# The buttons answer once the card can be read.
	save_button.disabled = card_alpha < 0.5
	continue_button.disabled = card_alpha < 0.5
	still.visible = still_alpha > 0.001 and still.texture != null
	still.modulate.a = clampf(still_alpha, 0.0, 1.0)
	hint.modulate.a = clampf(hint_alpha, 0.0, 1.0)


## The frame the reduced-motion still cross-fades from.
func set_still(texture: Texture2D) -> void:
	still.texture = texture


func _input(event: InputEvent) -> void:
	if not visible:
		return
	if event.is_action_pressed("ui_cancel"):
		skip_requested.emit()
		get_viewport().set_input_as_handled()
		return
	for action: StringName in BLOCKED_ACTIONS:
		if InputMap.has_action(action) and event.is_action(action):
			get_viewport().set_input_as_handled()
			return
	var mb := event as InputEventMouseButton
	if mb != null and mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		for b: Button in offered_buttons():
			if card.visible and not b.disabled and b.get_global_rect().has_point(mb.position):
				return  # the button takes it
		skip_requested.emit()
		get_viewport().set_input_as_handled()
