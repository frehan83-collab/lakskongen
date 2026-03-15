extends CanvasLayer

## AchievementPopup — celebratory gold banner shown when an achievement unlocks.
## Auto-dismisses after 5 seconds with a fade-out.
##
## Usage:
##   1. Add this scene (or script) as a child of your HUD or as an autoload.
##   2. Connect SimBridge.achievement_unlocked → show_achievement().
##   3. The popup appears at top-centre, holds for 5 s, then fades out.

# ── Layout constants ──────────────────────────────────────────────────────────

const DISPLAY_DURATION  := 5.0   # seconds before auto-dismiss
const FADE_DURATION     := 0.4   # seconds for fade-in / fade-out
const SLIDE_DISTANCE    := 60.0  # px — banner slides down on entry

# ── Node references (built procedurally so no .tscn dependency needed) ────────

var _panel: Panel
var _trophy_label: Label
var _name_label: Label
var _desc_label: Label
var _reward_label: Label
var _tier_label: Label
var _dismiss_btn: Button

var _tween: Tween = null
var _timer: float = 0.0
var _visible_flag: bool = false

# ── Gold colour palette ───────────────────────────────────────────────────────

const COL_GOLD_DARK   := Color(0.72, 0.53, 0.04)
const COL_GOLD_MID    := Color(0.95, 0.75, 0.10)
const COL_GOLD_LIGHT  := Color(1.00, 0.93, 0.50)
const COL_TEXT_DARK   := Color(0.15, 0.10, 0.02)
const COL_TEXT_WHITE  := Color(1.0,  1.0,  1.0)

# ── Tier colour map ───────────────────────────────────────────────────────────

const TIER_COLORS := {
	"Beginner":     Color(0.55, 0.85, 0.55),
	"Novice":       Color(0.40, 0.70, 1.00),
	"Intermediate": Color(0.70, 0.40, 1.00),
	"Advanced":     Color(1.00, 0.55, 0.10),
	"Master":       Color(1.00, 0.20, 0.20),
	"Hidden":       Color(0.60, 0.60, 0.60),
}

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_build_ui()
	_panel.modulate.a = 0.0
	_panel.visible = false

	# Auto-connect to SimBridge if available
	if has_node("/root/SimBridge"):
		get_node("/root/SimBridge").achievement_unlocked.connect(show_achievement)
		print("AchievementPopup: connected to SimBridge")

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _process(delta: float) -> void:
	if not _visible_flag:
		return
	_timer -= delta
	if _timer <= 0.0:
		_dismiss()

# ── Public API ────────────────────────────────────────────────────────────────

## Show a popup for the given achievement data.
## Called automatically via SimBridge.achievement_unlocked signal.
func show_achievement(achievement_name: String, description: String, reward_nok: float, tier: String) -> void:
	# Cancel any running dismiss tween so back-to-back unlocks work cleanly
	if _tween and _tween.is_valid():
		_tween.kill()

	_name_label.text   = achievement_name
	_desc_label.text   = description
	_tier_label.text   = tier.to_upper()
	_tier_label.modulate = TIER_COLORS.get(tier, COL_GOLD_LIGHT)

	if reward_nok > 0.0:
		_reward_label.text    = tr("ui.achievement.reward_format") % _format_nok(reward_nok)
		_reward_label.visible = true
	else:
		_reward_label.visible = false

	_panel.visible = true
	_timer = DISPLAY_DURATION
	_visible_flag = true

	# Slide-down + fade-in
	_panel.position.y = -SLIDE_DISTANCE
	_tween = create_tween().set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_tween.tween_property(_panel, "modulate:a", 1.0, FADE_DURATION)
	_tween.parallel().tween_property(_panel, "position:y", 0.0, FADE_DURATION)

# ── Internal ──────────────────────────────────────────────────────────────────

func _dismiss() -> void:
	_visible_flag = false
	_timer = 0.0

	if _tween and _tween.is_valid():
		_tween.kill()

	_tween = create_tween().set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
	_tween.tween_property(_panel, "modulate:a", 0.0, FADE_DURATION)
	_tween.tween_callback(func(): _panel.visible = false)

func _build_ui() -> void:
	layer = 10  # Above HUD, below modal dialogs

	_panel = Panel.new()
	_panel.name = "AchievementBanner"
	add_child(_panel)

	# Anchor the panel at top-centre of the viewport
	var vp_w := float(ProjectSettings.get_setting("display/window/size/viewport_width", 1920))
	var panel_w := 560.0
	var panel_h := 140.0
	_panel.set_anchors_preset(Control.PRESET_TOP_CENTER)
	_panel.set_size(Vector2(panel_w, panel_h))
	_panel.position = Vector2(-panel_w / 2.0, 16.0)

	# Gold gradient StyleBox
	var style := StyleBoxFlat.new()
	style.bg_color        = COL_GOLD_MID
	style.border_color    = COL_GOLD_DARK
	style.set_border_width_all(3)
	style.set_corner_radius_all(10)
	style.shadow_color    = Color(0, 0, 0, 0.45)
	style.shadow_size     = 8
	_panel.add_theme_stylebox_override("panel", style)

	# Trophy icon
	_trophy_label = Label.new()
	_trophy_label.text = "🏆"
	_trophy_label.add_theme_font_size_override("font_size", 42)
	_trophy_label.position = Vector2(12, 10)
	_trophy_label.set_size(Vector2(56, 56))
	_panel.add_child(_trophy_label)

	# "ACHIEVEMENT UNLOCKED" header
	var header := Label.new()
	header.text = tr("ui.achievement.unlocked")
	header.add_theme_font_size_override("font_size", 11)
	header.add_theme_color_override("font_color", COL_TEXT_DARK)
	header.position = Vector2(76, 12)
	_panel.add_child(header)

	# Achievement name (large)
	_name_label = Label.new()
	_name_label.add_theme_font_size_override("font_size", 22)
	_name_label.add_theme_color_override("font_color", COL_TEXT_DARK)
	_name_label.position = Vector2(76, 28)
	_name_label.set_size(Vector2(440, 32))
	_panel.add_child(_name_label)

	# Description
	_desc_label = Label.new()
	_desc_label.add_theme_font_size_override("font_size", 12)
	_desc_label.add_theme_color_override("font_color", COL_TEXT_DARK)
	_desc_label.position = Vector2(76, 60)
	_desc_label.set_size(Vector2(390, 24))
	_desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_panel.add_child(_desc_label)

	# Reward label
	_reward_label = Label.new()
	_reward_label.add_theme_font_size_override("font_size", 13)
	_reward_label.add_theme_color_override("font_color", COL_TEXT_DARK)
	_reward_label.position = Vector2(76, 86)
	_panel.add_child(_reward_label)

	# Tier badge (top-right)
	_tier_label = Label.new()
	_tier_label.add_theme_font_size_override("font_size", 10)
	_tier_label.position = Vector2(panel_w - 96, 12)
	_tier_label.set_size(Vector2(80, 18))
	_tier_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_panel.add_child(_tier_label)

	# Dismiss button (×)
	_dismiss_btn = Button.new()
	_dismiss_btn.text = "×"
	_dismiss_btn.flat = true
	_dismiss_btn.add_theme_font_size_override("font_size", 20)
	_dismiss_btn.add_theme_color_override("font_color", COL_TEXT_DARK)
	_dismiss_btn.position = Vector2(panel_w - 32, 4)
	_dismiss_btn.set_size(Vector2(28, 28))
	_dismiss_btn.pressed.connect(_dismiss)
	_panel.add_child(_dismiss_btn)

# ── Format helper ─────────────────────────────────────────────────────────────

func _format_nok(amount: float) -> String:
	if amount >= 1_000_000.0:
		return "%.2f M" % (amount / 1_000_000.0)
	elif amount >= 1_000.0:
		return "%.0f k" % (amount / 1_000.0)
	return "%.0f" % amount
