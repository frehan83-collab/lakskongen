extends CanvasLayer

## AchievementPopup — celebratory gold banner shown when an achievement unlocks.
## Scene-based (scenes/ui/AchievementPopup.tscn declares layout).
## Script is wiring only: @onready refs, signal hookups, tweens, localization.
##
## Usage:
##   1. Instance scenes/ui/AchievementPopup.tscn as a child of your HUD.
##   2. Connect SimBridge.achievement_unlocked → show_achievement().
##   3. The popup appears at top-centre, holds for 5 s, then fades out.

# ── Layout constants ──────────────────────────────────────────────────────────

const DISPLAY_DURATION := 5.0   # seconds before auto-dismiss
const FADE_DURATION := 0.4   # seconds for fade-in / fade-out
const SLIDE_DISTANCE := 60.0  # px — banner slides down on entry

# ── Node references (scene-declared) ──────────────────────────────────────────

@onready var _panel: PanelContainer = %AchievementBanner
@onready var _trophy_label: Label = %TrophyLabel
@onready var _header_label: Label = %HeaderLabel
@onready var _name_label: Label = %NameLabel
@onready var _desc_label: Label = %DescLabel
@onready var _reward_label: Label = %RewardLabel
@onready var _tier_label: Label = %TierLabel
@onready var _dismiss_btn: Button = %DismissButton

var _tween: Tween = null
var _timer: float = 0.0
var _visible_flag: bool = false

# ── Gold colour palette ───────────────────────────────────────────────────────

const COL_GOLD_DARK := Color("#8A5A00")
const COL_GOLD_MID := Color("#8A5A00")
const COL_GOLD_LIGHT := Color("#F2B705")  # Ink warn (tier fallback)
const COL_TEXT_DARK := Color("#E8EEF0")  # Ink text
const COL_TEXT_WHITE := Color(1.0, 1.0, 1.0)

# ── Tier colour map ───────────────────────────────────────────────────────────

const TIER_COLORS := {
	"Beginner": Color("#2E6B45"),
	"Novice": Color("#1F5E73"),
	"Intermediate": Color("#1F5E73"),
	"Advanced": Color(1.00, 0.55, 0.10),
	"Master": Color(1.00, 0.20, 0.20),
	"Hidden": Color("#5A6168"),
}

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_apply_localization()
	_apply_theme_fonts()
	_panel.modulate.a = 0.0
	_panel.visible = false
	_dismiss_btn.pressed.connect(_dismiss)

	# Auto-connect to SimBridge if available
	if has_node("/root/SimBridge"):
		get_node("/root/SimBridge").achievement_unlocked.connect(show_achievement)

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

	_name_label.text = achievement_name
	_desc_label.text = description
	_tier_label.text = SimLabels.achievement_tier(tier).to_upper()
	_tier_label.modulate = TIER_COLORS.get(tier, COL_GOLD_LIGHT)

	if reward_nok > 0.0:
		_reward_label.text = tr("UI_ACHIEVEMENT_REWARD_FORMAT") % _format_nok(reward_nok)
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

func _apply_localization() -> void:
	_header_label.text = _tr_or("UI_ACHIEVEMENT_UNLOCKED", "PRESTASJON LÅST OPP")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _apply_theme_fonts() -> void:
	var theme_node = get_node_or_null("/root/LakskongenTheme")
	LakskongenTheme.set_strong(_name_label)

# ── Format helper ─────────────────────────────────────────────────────────────

func _format_nok(amount: float) -> String:
	return SimFormatters.format_nok(amount)
