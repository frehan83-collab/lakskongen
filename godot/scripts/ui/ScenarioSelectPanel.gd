## ScenarioSelectPanel.gd
## Campaign scenario selection — five Norwegian-themed cards with difficulty stars,
## descriptions, locked/unlocked state, and a Start button.
##
## Builds its layout procedurally (no external scene file required).
##
## Signals:
##   scenario_selected(scenario_id: String)  — emitted when the player presses Start
##   panel_close_requested                   — emitted when the ✕ button is pressed
##
## IDs match ScenarioSystem.cs: forste_sesong, lofoten_eventyr, laksebaronene,
##   barekraft_mester, krise_manager.

extends Control

# ── Signals ────────────────────────────────────────────────────────────────────

signal scenario_selected(scenario_id: String)
signal panel_close_requested

# ── Layout constants ────────────────────────────────────────────────────────────

const PANEL_W    := 1050
const PANEL_H    := 700
const CARD_W     := 310
const CARD_H     := 520
const CARD_GAP   := 18
const HEADER_H   := 56
const CARDS_Y    := 80    # vertical offset below title bar
## How far the backdrop scrim reaches past the panel on every side (covers
## the screen at any resolution the panel fits in).
const SCRIM_REACH := 4096.0

# ── Kyst & Stål styling ─────────────────────────────────────────────────────────
# The picker sits on the menu's Ink surface: cards are theme variations
# ("KpiCard<Tone>": raised Ink with the scenario's tone as a top stripe) and
# every label takes a role/tone variation, so no colour is hard-coded here.

## Scenario accent colour -> theme tone (the card stripe and title accent).
static func _tone_for(accent: Color) -> StringName:
	if accent.is_equal_approx(LakskongenTheme.CHART_CRIT):
		return &"Crit"
	if accent.is_equal_approx(LakskongenTheme.CHART_OK):
		return &"Ok"
	if accent.r > 0.9 and accent.g > 0.7 and accent.b < 0.4:
		return &"Warn"
	return &"Info"

# ── Scenario metadata ───────────────────────────────────────────────────────────

const SCENARIO_DATA: Array = [
	{
		"id":          "forste_sesong",
		"name":        "SCENARIO_FORSTE_SESONG_NAME",
		"subtitle":    "SCENARIO_FORSTE_SESONG_SUBTITLE",
		"difficulty":  1,
		"win":         "SCENARIO_FORSTE_SESONG_WIN",
		"description": "SCENARIO_FORSTE_SESONG_DESCRIPTION",
		"capital":     300_000,
		"capital_neg": false,
		"region":      "Vestland",
		"icon":        "🐟",
		"color":       LakskongenTheme.CHART_INFO,   # ocean blue
		"unlocked":    true,
		"special":     ["SCENARIO_FORSTE_SESONG_SPECIAL_1", "SCENARIO_FORSTE_SESONG_SPECIAL_2", "SCENARIO_FORSTE_SESONG_SPECIAL_3"],
	},
	{
		"id":          "lofoten_eventyr",
		"name":        "SCENARIO_LOFOTEN_EVENTYR_NAME",
		"subtitle":    "SCENARIO_LOFOTEN_EVENTYR_SUBTITLE",
		"difficulty":  3,
		"win":         "SCENARIO_LOFOTEN_EVENTYR_WIN",
		"description": "SCENARIO_LOFOTEN_EVENTYR_DESCRIPTION",
		"capital":     2_000_000,
		"capital_neg": false,
		"region":      "Nordland",
		"icon":        "❄️",
		"color":       LakskongenTheme.INK_INFO,   # arctic blue
		"unlocked":    true,
		"special":     ["SCENARIO_LOFOTEN_EVENTYR_SPECIAL_1", "SCENARIO_LOFOTEN_EVENTYR_SPECIAL_2", "SCENARIO_LOFOTEN_EVENTYR_SPECIAL_3"],
	},
	{
		"id":          "laksebaronene",
		"name":        "SCENARIO_LAKSEBARONENE_NAME",
		"subtitle":    "SCENARIO_LAKSEBARONENE_SUBTITLE",
		"difficulty":  4,
		"win":         "SCENARIO_LAKSEBARONENE_WIN",
		"description": "SCENARIO_LAKSEBARONENE_DESCRIPTION",
		"capital":     5_000_000,
		"capital_neg": false,
		"region":      "Trøndelag",
		"icon":        "👑",
		"color":       Color(1.00, 0.82, 0.20),   # gold
		"unlocked":    false,
		"special":     ["SCENARIO_LAKSEBARONENE_SPECIAL_1", "SCENARIO_LAKSEBARONENE_SPECIAL_2", "SCENARIO_LAKSEBARONENE_SPECIAL_3"],
	},
	{
		"id":          "krise_manager",
		"name":        "SCENARIO_KRISE_MANAGER_NAME",
		"subtitle":    "SCENARIO_KRISE_MANAGER_SUBTITLE",
		"difficulty":  5,
		"win":         "SCENARIO_KRISE_MANAGER_WIN",
		"description": "SCENARIO_KRISE_MANAGER_DESCRIPTION",
		"capital":     -500_000,
		"capital_neg": true,
		"region":      "Finnmark",
		"icon":        "🚨",
		"color":       LakskongenTheme.CHART_CRIT,   # crisis red
		"unlocked":    false,
		"special":     ["SCENARIO_KRISE_MANAGER_SPECIAL_1", "SCENARIO_KRISE_MANAGER_SPECIAL_2", "SCENARIO_KRISE_MANAGER_SPECIAL_3"],
	},
	{
		"id":          "barekraft_mester",
		"name":        "SCENARIO_BAREKRAFT_MESTER_NAME",
		"subtitle":    "SCENARIO_BAREKRAFT_MESTER_SUBTITLE",
		"difficulty":  1,
		"win":         "SCENARIO_BAREKRAFT_MESTER_WIN",
		"description": "SCENARIO_BAREKRAFT_MESTER_DESCRIPTION",
		"capital":     50_000_000,
		"capital_neg": false,
		"region":      "Vestland",
		"icon":        "👑",
		"color":       LakskongenTheme.CHART_OK,   # emerald
		"unlocked":    false,
		"special":     ["SCENARIO_BAREKRAFT_MESTER_SPECIAL_1", "SCENARIO_BAREKRAFT_MESTER_SPECIAL_2", "SCENARIO_BAREKRAFT_MESTER_SPECIAL_3"],
	},
]

# ── Lifecycle ───────────────────────────────────────────────────────────────────

func _ready() -> void:
	custom_minimum_size = Vector2(PANEL_W, PANEL_H)
	anchor_left = 0.5
	anchor_right = 0.5
	anchor_top = 0.5
	anchor_bottom = 0.5
	offset_left = -PANEL_W / 2
	offset_right = PANEL_W / 2
	offset_top = -PANEL_H / 2
	offset_bottom = PANEL_H / 2
	_build_skeleton()

# ── Build ───────────────────────────────────────────────────────────────────────

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _build_skeleton() -> void:
	# An Ink scrim over the whole screen (first child, so it draws behind the
	# cards): the picker reads as a layer above the menu, whose buttons would
	# otherwise show between the cards.
	var scrim := ColorRect.new()
	scrim.name = "Scrim"
	scrim.color = Color(LakskongenTheme.INK, 0.96)
	scrim.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in [SIDE_LEFT, SIDE_TOP]:
		scrim.set_offset(side, -SCRIM_REACH)
	for side in [SIDE_RIGHT, SIDE_BOTTOM]:
		scrim.set_offset(side, SCRIM_REACH)
	add_child(scrim)

	# ── Title bar ──
	var title_lbl := Label.new()
	title_lbl.text = tr("UI_SCENARIO_TITLE")
	title_lbl.theme_type_variation = &"Title"
	title_lbl.position = Vector2(28, 14)
	title_lbl.tooltip_text = "Velg ett av fem forskjellige scenarier med unike utfordringer og vinnkriterier."
	add_child(title_lbl)

	var subtitle_lbl := Label.new()
	subtitle_lbl.text = tr("UI_SCENARIO_SUBTITLE")
	subtitle_lbl.theme_type_variation = &"BodySmallMuted"
	subtitle_lbl.position = Vector2(28, 50)
	add_child(subtitle_lbl)

	# ── Close button ──
	var close_btn := Button.new()
	close_btn.text = "×"
	close_btn.position = Vector2(PANEL_W - 52, 10)
	close_btn.size = Vector2(40, 40)
	close_btn.theme_type_variation = &"LargeGhostButton"
	close_btn.tooltip_text = "Lukk scenariovalg (Esc)"
	close_btn.pressed.connect(func(): emit_signal("panel_close_requested"))
	add_child(close_btn)

	# ── Separator line ──
	var sep := HSeparator.new()
	sep.position = Vector2(28, HEADER_H + 14)
	sep.size = Vector2(PANEL_W - 56, 1)
	add_child(sep)

	# ── Cards ──
	var total_cards_w: float = SCENARIO_DATA.size() * CARD_W + (SCENARIO_DATA.size() - 1) * CARD_GAP
	var start_x: float = (PANEL_W - total_cards_w) * 0.5

	for i in SCENARIO_DATA.size():
		var s: Dictionary = SCENARIO_DATA[i]
		var x: float = start_x + i * (CARD_W + CARD_GAP)
		_build_card(s, Vector2(x, CARDS_Y))

# ── Card builder ────────────────────────────────────────────────────────────────

func _build_card(s: Dictionary, pos: Vector2) -> void:
	var locked: bool = not s["unlocked"]

	# ── Card panel ──
	var panel := PanelContainer.new()
	panel.position = pos
	panel.custom_minimum_size = Vector2(CARD_W, CARD_H)

	var tone := _tone_for(s["color"])
	panel.theme_type_variation = &"KpiCardMuted" if locked else StringName("KpiCard" + String(tone))
	add_child(panel)

	# No dim overlay: the gray card + darkened border + LÅST button already
	# signal "locked", and an overlay would wash out the body text.

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 8)
	panel.add_child(vbox)

	# ── Icon + title row ──
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	vbox.add_child(header)

	var icon_lbl := Label.new()
	icon_lbl.text = "🔒"
	icon_lbl.theme_type_variation = &"Title"
	icon_lbl.visible = locked  # the padlock only marks a locked scenario
	header.add_child(icon_lbl)

	var title_vbox := VBoxContainer.new()
	title_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(title_vbox)

	var name_lbl := Label.new()
	name_lbl.text = tr(s["name"])
	name_lbl.theme_type_variation = &"SubheadingMuted" if locked else &"Subheading"
	name_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title_vbox.add_child(name_lbl)

	var subtitle_lbl := Label.new()
	subtitle_lbl.text = tr(s["subtitle"])
	subtitle_lbl.theme_type_variation = &"CaptionMuted" if locked else StringName("Caption" + String(tone))
	title_vbox.add_child(subtitle_lbl)

	# ── Difficulty stars ──
	var stars_row := HBoxContainer.new()
	stars_row.add_theme_constant_override("separation", 2)
	vbox.add_child(stars_row)

	var diff_prefix := Label.new()
	diff_prefix.text = tr("UI_SCENARIO_DIFFICULTY")
	diff_prefix.theme_type_variation = &"SmallMuted"
	stars_row.add_child(diff_prefix)

	for si in 5:
		var star := Label.new()
		star.text = "★"
		var lit: bool = not locked and si < s["difficulty"]
		star.theme_type_variation = &"BodyStrongWarn" if lit else &"BodyMuted"
		if not lit:
			star.modulate.a = 0.45
		stars_row.add_child(star)

	# ── Separator ──
	var sep := HSeparator.new()
	vbox.add_child(sep)

	# ── Description ──
	var desc_lbl := Label.new()
	desc_lbl.text = tr(s["description"])
	desc_lbl.theme_type_variation = &"BodySmallMuted" if locked else &"BodySmall"
	desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_lbl.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(desc_lbl)

	# ── Special rules ──
	for rule: String in s["special"]:
		var r := Label.new()
		r.text = "· " + tr(rule)
		r.theme_type_variation = &"SmallMuted" if locked else &"Small"
		vbox.add_child(r)

	# ── Meta: region + capital ──
	var meta := VBoxContainer.new()
	meta.add_theme_constant_override("separation", 3)
	vbox.add_child(meta)

	var region_lbl := Label.new()
	region_lbl.text = tr("UI_SCENARIO_REGION_ICON") + " " + s["region"]
	region_lbl.theme_type_variation = &"SmallMuted"
	meta.add_child(region_lbl)

	var cap_lbl := Label.new()
	cap_lbl.text = tr("UI_SCENARIO_CAPITAL_ICON") + " " + SimFormatters.integer(int(s["capital"])) + " NOK"
	if not locked:
		cap_lbl.theme_type_variation = &"FigureSmallCrit" if s["capital_neg"] else &"FigureSmallOk"
	else:
		cap_lbl.theme_type_variation = &"FigureSmall"
	meta.add_child(cap_lbl)

	var win_lbl := Label.new()
	win_lbl.text = tr("UI_SCENARIO_WIN_ICON") + " " + tr(s["win"])
	win_lbl.theme_type_variation = &"SmallMuted" if locked else StringName("Small" + String(tone))
	win_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	meta.add_child(win_lbl)

	# ── Start / Locked button ──
	var btn := Button.new()
	btn.custom_minimum_size = Vector2(0, 42)

	if locked:
		btn.text = "🔒  " + tr("UI_SCENARIO_LOCKED")
		btn.tooltip_text = tr("UI_SCENARIO_LOCKED_TOOLTIP")
		btn.disabled = true
	else:
		btn.text = "▶  " + tr("UI_SCENARIO_START")
		btn.tooltip_text = tr("UI_SCENARIO_START_TOOLTIP")
		btn.theme_type_variation = &"PrimaryButton"
		btn.pressed.connect(_on_start_pressed.bind(s["id"]))

	vbox.add_child(btn)

# ── Signal handlers ─────────────────────────────────────────────────────────────

func _on_start_pressed(scenario_id: String) -> void:
	print("[ScenarioSelectPanel] Starting scenario: ", scenario_id)
	emit_signal("scenario_selected", scenario_id)

	# Fade-out then free self
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.35)
	tw.finished.connect(queue_free)
