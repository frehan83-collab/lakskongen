extends Control

## CreditsScreen — auto-scrolling credits for Lakskongen.
## Builds the credits list procedurally from CREDITS_DATA.
## Scroll speed is constant; player can also drag/scroll manually.
## Close button or Escape returns to the previous screen.
##
## Expected node layout:
##   CreditsScreen (Control, full-screen)
##   ├─ BgRect     (ColorRect, full-screen dark ocean)
##   ├─ TopFade    (ColorRect, gradient fade at top, full-width, 120 px tall)
##   ├─ BottomFade (ColorRect, gradient fade at bottom)
##   ├─ ScrollContainer (full-screen, clip_contents = true)
##   │  └─ CreditsContainer (VBoxContainer, anchored top-centre)
##   └─ CloseButton (Button, anchored top-right) "✕"

signal close_requested

# ── Credits data ──────────────────────────────────────────────────────────────

const CREDITS_DATA: Array[Dictionary] = [
	{"type": "game_title",  "text": "🐟 LAKSKONGEN"},
	{"type": "subtitle",    "text": "Norsk Lakseoppdrett Simulator"},
	{"type": "spacer"},
	{"type": "section",     "text": "Produksjon"},
	{"type": "role",        "text": "Produsent"},
	{"type": "name",        "text": "Fredrik Hansen"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "Tech Lead"},
	{"type": "name",        "text": "Aksel Berg"},
	{"type": "spacer"},
	{"type": "section",     "text": "Design & Kunst"},
	{"type": "role",        "text": "Art Director"},
	{"type": "name",        "text": "Camilla Johansen"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "UI/UX Design"},
	{"type": "name",        "text": "Ingrid Lunde"},
	{"type": "spacer"},
	{"type": "section",     "text": "Programmering"},
	{"type": "role",        "text": "Simuleringsmotor"},
	{"type": "name",        "text": "Jonas Karlsen"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "Gameplay-programmering"},
	{"type": "name",        "text": "Kaja Nilsen"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "Frontend / UI"},
	{"type": "name",        "text": "Erik Dahl"},
	{"type": "spacer"},
	{"type": "section",     "text": "Lyd & Musikk"},
	{"type": "role",        "text": "Lyddesign & Komponist"},
	{"type": "name",        "text": "Mina Sørensen"},
	{"type": "spacer"},
	{"type": "section",     "text": "QA & Testing"},
	{"type": "role",        "text": "Spilltesting & QA"},
	{"type": "name",        "text": "Lars Pettersen"},
	{"type": "spacer"},
	{"type": "section",     "text": "Takk til"},
	{"type": "body",        "text": "Norsk lakseoppdrettsnæring\nog alle som støttet prosjektet."},
	{"type": "spacer"},
	{"type": "section",     "text": "Teknologi"},
	{"type": "role",        "text": "Spillmotor"},
	{"type": "name",        "text": "Godot Engine 4"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "AI / Simuleringsplattform"},
	{"type": "name",        "text": "OpenClaw"},
	{"type": "spacer_small"},
	{"type": "role",        "text": "Scripting"},
	{"type": "name",        "text": "GDScript + C#"},
	{"type": "spacer"},
	{"type": "divider"},
	{"type": "spacer"},
	{"type": "body",        "text": "Lakskongen er laget med\nkjærlighet til norsk natur\nog havbruksnæringen."},
	{"type": "spacer"},
	{"type": "body",        "text": "© 2026 Lakskongen-teamet\nAlle rettigheter forbeholdt."},
	{"type": "spacer"},
	{"type": "spacer"},
]

# ── Style constants ───────────────────────────────────────────────────────────

const COLOR_BG          := Color(0.03, 0.07, 0.16)
const COLOR_GAME_TITLE  := Color(0.90, 0.97, 1.00)
const COLOR_SUBTITLE    := Color(0.55, 0.80, 0.95)
const COLOR_SECTION     := Color(0.40, 0.80, 1.00)
const COLOR_ROLE        := Color(0.70, 0.80, 0.90)
const COLOR_NAME        := Color(1.00, 1.00, 1.00)
const COLOR_BODY        := Color(0.75, 0.85, 0.92)

const FONT_SIZE_GAME_TITLE := 52
const FONT_SIZE_SUBTITLE   := 24
const FONT_SIZE_SECTION    := 28
const FONT_SIZE_ROLE       := 18
const FONT_SIZE_NAME       := 22
const FONT_SIZE_BODY       := 18

const SCROLL_SPEED := 55.0      # pixels per second
const SPACER_HEIGHT       := 48
const SPACER_SMALL_HEIGHT := 20

# ── Node references ────────────────────────────────────────────────────────────

@onready var bg_rect:          ColorRect      = $BgRect
@onready var scroll_container: ScrollContainer = $ScrollContainer
@onready var credits_container: VBoxContainer = $ScrollContainer/CreditsContainer
@onready var close_button:     Button         = $CloseButton

# ── State ──────────────────────────────────────────────────────────────────────

var _scrolling: bool     = true
var _total_height: float = 0.0

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	bg_rect.color = COLOR_BG

	_style_close_button()
	close_button.pressed.connect(_on_close_pressed)

	_build_credits()

	# Start scroll from bottom of viewport (credits enter from the bottom)
	await get_tree().process_frame
	_total_height = credits_container.size.y
	scroll_container.scroll_vertical = int(_total_height + get_viewport_rect().size.y)

	# Fade in
	modulate = Color(1, 1, 1, 0)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 1.0, 0.6)


func _process(delta: float) -> void:
	if not _scrolling:
		return
	scroll_container.scroll_vertical -= int(SCROLL_SPEED * delta)
	# Stop once credits have fully scrolled off the top
	if scroll_container.scroll_vertical <= -int(get_viewport_rect().size.y * 0.25):
		_scrolling = false


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		_on_close_pressed()
		get_viewport().set_input_as_handled()
	# Allow manual scroll to pause auto-scroll temporarily
	if event is InputEventMouseButton or event is InputEventScreenDrag:
		_scrolling = false
		# Resume after 2 seconds of no input
		get_tree().create_timer(2.0).timeout.connect(func(): _scrolling = true)

# ── Credits builder ───────────────────────────────────────────────────────────

func _build_credits() -> void:
	for entry in CREDITS_DATA:
		var node := _make_entry(entry)
		if node:
			credits_container.add_child(node)


func _make_entry(entry: Dictionary) -> Control:
	var kind: String = entry.get("type", "")

	match kind:
		"spacer":
			return _make_spacer(SPACER_HEIGHT)

		"spacer_small":
			return _make_spacer(SPACER_SMALL_HEIGHT)

		"divider":
			var line := HSeparator.new()
			line.custom_minimum_size = Vector2(400, 2)
			line.add_theme_color_override("color", COLOR_SECTION)
			return line

		"game_title":
			return _make_label(entry.get("text", ""), FONT_SIZE_GAME_TITLE, COLOR_GAME_TITLE, true)

		"subtitle":
			return _make_label(entry.get("text", ""), FONT_SIZE_SUBTITLE, COLOR_SUBTITLE, false)

		"section":
			var vbox := VBoxContainer.new()
			var lbl := _make_label(entry.get("text", "").to_upper(), FONT_SIZE_SECTION, COLOR_SECTION, false)
			lbl.add_theme_color_override("font_color", COLOR_SECTION)
			var sep := HSeparator.new()
			sep.custom_minimum_size = Vector2(300, 1)
			sep.add_theme_color_override("color", COLOR_SECTION * Color(1, 1, 1, 0.4))
			vbox.add_child(lbl)
			vbox.add_child(sep)
			return vbox

		"role":
			return _make_label(entry.get("text", ""), FONT_SIZE_ROLE, COLOR_ROLE, false)

		"name":
			return _make_label(entry.get("text", ""), FONT_SIZE_NAME, COLOR_NAME, false)

		"body":
			return _make_label(entry.get("text", ""), FONT_SIZE_BODY, COLOR_BODY, false)

		_:
			return null


func _make_label(text: String, font_size: int, color: Color, bold: bool) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lbl.custom_minimum_size = Vector2(600, 0)
	lbl.add_theme_font_size_override("font_size", font_size)
	lbl.add_theme_color_override("font_color", color)
	if bold:
		# Bold via outline to avoid needing a separate font resource
		lbl.add_theme_constant_override("outline_size", 2)
		lbl.add_theme_color_override("font_outline_color", color * Color(0.5, 0.5, 0.5, 0.8))
	return lbl


func _make_spacer(height: int) -> Control:
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, height)
	return spacer

# ── Visual helpers ─────────────────────────────────────────────────────────────

func _style_close_button() -> void:
	var style := StyleBoxFlat.new()
	style.bg_color                     = Color(0.15, 0.28, 0.48, 0.90)
	style.corner_radius_top_left       = 6
	style.corner_radius_top_right      = 6
	style.corner_radius_bottom_left    = 6
	style.corner_radius_bottom_right   = 6
	style.content_margin_left          = 14
	style.content_margin_right         = 14
	style.content_margin_top           = 8
	style.content_margin_bottom        = 8
	close_button.add_theme_stylebox_override("normal", style)
	var hover_style := style.duplicate() as StyleBoxFlat
	hover_style.bg_color = Color(0.22, 0.42, 0.68, 0.95)
	close_button.add_theme_stylebox_override("hover", hover_style)
	close_button.add_theme_color_override("font_color", Color(0.95, 0.98, 1.0))
	close_button.add_theme_font_size_override("font_size", 18)

# ── Close ─────────────────────────────────────────────────────────────────────

func _on_close_pressed() -> void:
	_scrolling = false
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.35)
	tween.tween_callback(func():
		hide()
		close_requested.emit()
	)
