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

# ── Deep-ocean colour palette ───────────────────────────────────────────────────

const C_BG         := Color(0.06, 0.08, 0.12)   # near-black fjord
const C_CARD_BASE  := Color(0.11, 0.15, 0.22)   # dark slate
const C_LOCKED_BG  := Color(0.09, 0.10, 0.13)   # muted locked card
const C_TITLE      := Color(0.88, 0.92, 1.00)
const C_TEXT       := Color(0.78, 0.82, 0.90)
const C_DIM        := Color(0.45, 0.48, 0.55)
const C_LOCKED_TXT := Color(0.35, 0.38, 0.42)
const C_STAR_ON    := Color(1.00, 0.85, 0.15)
const C_STAR_OFF   := Color(0.30, 0.32, 0.38)
const C_POSITIVE   := Color(0.35, 0.90, 0.50)
const C_NEGATIVE   := Color(0.95, 0.35, 0.35)

# ── Scenario metadata ───────────────────────────────────────────────────────────

const SCENARIO_DATA: Array = [
	{
		"id":          "forste_sesong",
		"name":        "Første Sesong",
		"subtitle":    "Opplæring",
		"difficulty":  1,
		"win":         "Fullfør opplæringen",
		"description": "Du har arvet en liten oppdrettslisens i Hardanger. "
		             + "Lær grunnleggende drift med ett anlegg, rolig vær og stabile "
		             + "priser. Perfekt for nybegynnere.",
		"capital":     "500 000 NOK",
		"capital_neg": false,
		"region":      "Vestland",
		"icon":        "🐟",
		"color":       Color(0.30, 0.65, 1.00),   # ocean blue
		"unlocked":    true,
		"special":     ["☀️ Rolig vær", "📈 Stabile priser", "1 anlegg"],
	},
	{
		"id":          "lofoten_eventyr",
		"name":        "Lofotens Eventyr",
		"subtitle":    "Storm & Premiumkvalitet",
		"difficulty":  3,
		"win":         "Nå 5 000 000 NOK i overskudd",
		"description": "De dramatiske øyene i Nordland byr på de reneste havstrømmene "
		             + "og de råeste stormene. Premium-priser i Asia og Europa — "
		             + "men uvær truer anleggene.",
		"capital":     "2 000 000 NOK",
		"capital_neg": false,
		"region":      "Nordland",
		"icon":        "❄️",
		"color":       Color(0.55, 0.82, 1.00),   # arctic blue
		"unlocked":    true,
		"special":     ["⛈️ Harde stormer", "💴 Premiumpriser", "2 anlegg"],
	},
	{
		"id":          "laksebaronene",
		"name":        "Laksebaronene",
		"subtitle":    "Konkurranse",
		"difficulty":  4,
		"win":         "Nå 10 000 000 NOK før konkurrentene",
		"description": "Tre aggressive konkurrenter kjemper om å bli Trøndelags største "
		             + "lakseaktør. Vær den første til å nå 10M NOK i fortjeneste — "
		             + "eller bli utkonkurrert.",
		"capital":     "5 000 000 NOK",
		"capital_neg": false,
		"region":      "Trøndelag",
		"icon":        "👑",
		"color":       Color(1.00, 0.82, 0.20),   # gold
		"unlocked":    false,
		"special":     ["🤖 3 AI-rivaler", "📊 Ukentlig leaderboard", "Dynamisk marked"],
	},
	{
		"id":          "barekraft_mester",
		"name":        "Bærekraftsmester",
		"subtitle":    "Miljøsertifisering",
		"difficulty":  3,
		"win":         "Sustainability Leader-sertifisering (≥ 95 poeng)",
		"description": "Forbrukere og politikere krever ansvarlig havbruk. Bygg et "
		             + "selskap med minst mulig miljøavtrykk og oppnå toppsertifisering. "
		             + "Strengere reguleringer gjør veien krevende.",
		"capital":     "3 000 000 NOK",
		"capital_neg": false,
		"region":      "Vestland",
		"icon":        "🌿",
		"color":       Color(0.25, 0.88, 0.52),   # emerald
		"unlocked":    false,
		"special":     ["🔬 ASC-fokus", "⚖️ Reguleringstrykk", "Grønne obligasjoner"],
	},
	{
		"id":          "krise_manager",
		"name":        "Krise Manager",
		"subtitle":    "Overlevelsesprøve",
		"difficulty":  5,
		"win":         "Overlev 730 dager med positiv saldo",
		"description": "Du tar over et konkursrammet anlegg i Finnmark: ILA-utbrudd, "
		             + "500 000 NOK i gjeld og Mattilsynet-varsel. Bare de sterkeste "
		             + "overlever.",
		"capital":     "−500 000 NOK",
		"capital_neg": true,
		"region":      "Finnmark",
		"icon":        "🚨",
		"color":       Color(0.95, 0.32, 0.32),   # crisis red
		"unlocked":    false,
		"special":     ["🦠 ILA-utbrudd", "💸 Startgjeld", "🏛️ Myndighetstilsyn"],
	},
]

# ── Lifecycle ───────────────────────────────────────────────────────────────────

func _ready() -> void:
	custom_minimum_size = Vector2(PANEL_W, PANEL_H)
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
	# ── Background ──
	var bg := ColorRect.new()
	bg.color = C_BG
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	# ── Title bar ──
	var title_lbl := Label.new()
	title_lbl.text = "Velg Scenario"
	title_lbl.add_theme_font_size_override("font_size", 22)
	title_lbl.modulate = C_TITLE
	title_lbl.position = Vector2(28, 14)
	title_lbl.tooltip_text = "Velg ett av fem forskjellige scenarier med unike utfordringer og vinnkriterier."
	add_child(title_lbl)

	var subtitle_lbl := Label.new()
	subtitle_lbl.text = "Fem norske eventyr venter — velg din utfordring"
	subtitle_lbl.add_theme_font_size_override("font_size", 13)
	subtitle_lbl.modulate = C_DIM
	subtitle_lbl.position = Vector2(28, 38)
	add_child(subtitle_lbl)

	# ── Close button ──
	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.position = Vector2(PANEL_W - 44, 10)
	close_btn.size = Vector2(32, 32)
	close_btn.add_theme_font_size_override("font_size", 16)
	close_btn.tooltip_text = "Lukk scenariovalg (Esc)"
	close_btn.pressed.connect(func(): emit_signal("panel_close_requested"))
	add_child(close_btn)

	# ── Separator line ──
	var sep := ColorRect.new()
	sep.color = Color(0.20, 0.28, 0.42)
	sep.position = Vector2(0, HEADER_H - 2)
	sep.size = Vector2(PANEL_W, 1)
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

	var style := StyleBoxFlat.new()
	style.bg_color      = C_LOCKED_BG if locked else C_CARD_BASE
	style.border_color  = s["color"].darkened(0.3) if locked else s["color"]
	style.set_border_width_all(2)
	style.set_corner_radius_all(10)
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)

	# Dim overlay for locked cards
	if locked:
		var dim := ColorRect.new()
		dim.color = Color(0, 0, 0, 0.42)
		dim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		panel.add_child(dim)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 8)
	panel.add_child(vbox)

	# ── Icon + title row ──
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	vbox.add_child(header)

	var icon_lbl := Label.new()
	icon_lbl.text = "🔒" if locked else s["icon"]
	icon_lbl.add_theme_font_size_override("font_size", 36)
	header.add_child(icon_lbl)

	var title_vbox := VBoxContainer.new()
	title_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(title_vbox)

	var name_lbl := Label.new()
	name_lbl.text = s["name"]
	name_lbl.add_theme_font_size_override("font_size", 16)
	name_lbl.add_theme_color_override("font_color", C_LOCKED_TXT if locked else s["color"])
	name_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title_vbox.add_child(name_lbl)

	var subtitle_lbl := Label.new()
	subtitle_lbl.text = s["subtitle"]
	subtitle_lbl.add_theme_font_size_override("font_size", 11)
	subtitle_lbl.add_theme_color_override("font_color", C_DIM)
	title_vbox.add_child(subtitle_lbl)

	# ── Difficulty stars ──
	var stars_row := HBoxContainer.new()
	stars_row.add_theme_constant_override("separation", 2)
	vbox.add_child(stars_row)

	var diff_prefix := Label.new()
	diff_prefix.text = "Vanskelighet  "
	diff_prefix.add_theme_font_size_override("font_size", 12)
	diff_prefix.add_theme_color_override("font_color", C_DIM)
	stars_row.add_child(diff_prefix)

	for si in 5:
		var star := Label.new()
		star.text = "★"
		star.add_theme_font_size_override("font_size", 16)
		var star_color: Color = C_STAR_OFF
		if not locked and si < s["difficulty"]:
			star_color = C_STAR_ON
		star.add_theme_color_override("font_color", star_color)
		stars_row.add_child(star)

	# ── Separator ──
	var sep := ColorRect.new()
	sep.custom_minimum_size = Vector2(0, 1)
	sep.color = Color(0.22, 0.28, 0.38) if not locked else Color(0.15, 0.18, 0.22)
	vbox.add_child(sep)

	# ── Description ──
	var desc_lbl := Label.new()
	desc_lbl.text = s["description"]
	desc_lbl.add_theme_font_size_override("font_size", 12)
	desc_lbl.add_theme_color_override("font_color", C_LOCKED_TXT if locked else C_TEXT)
	desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_lbl.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(desc_lbl)

	# ── Special rules ──
	for rule: String in s["special"]:
		var r := Label.new()
		r.text = rule
		r.add_theme_font_size_override("font_size", 11)
		r.add_theme_color_override("font_color", C_DIM if locked else C_TEXT.darkened(0.15))
		vbox.add_child(r)

	# ── Meta: region + capital ──
	var meta := VBoxContainer.new()
	meta.add_theme_constant_override("separation", 3)
	vbox.add_child(meta)

	var region_lbl := Label.new()
	region_lbl.text = "📍 " + s["region"]
	region_lbl.add_theme_font_size_override("font_size", 12)
	region_lbl.add_theme_color_override("font_color", C_DIM)
	meta.add_child(region_lbl)

	var cap_lbl := Label.new()
	cap_lbl.text = "💰 " + s["capital"]
	cap_lbl.add_theme_font_size_override("font_size", 12)
	if not locked:
		cap_lbl.add_theme_color_override("font_color", C_NEGATIVE if s["capital_neg"] else C_POSITIVE)
	else:
		cap_lbl.add_theme_color_override("font_color", C_LOCKED_TXT)
	meta.add_child(cap_lbl)

	var win_lbl := Label.new()
	win_lbl.text = "🎯 " + s["win"]
	win_lbl.add_theme_font_size_override("font_size", 11)
	win_lbl.add_theme_color_override("font_color", C_DIM if locked else s["color"].lightened(0.2))
	win_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	meta.add_child(win_lbl)

	# ── Start / Locked button ──
	var btn := Button.new()
	btn.custom_minimum_size = Vector2(0, 42)
	btn.add_theme_font_size_override("font_size", 14)

	if locked:
		btn.text = "🔒  LÅST"
		btn.tooltip_text = "Dette scenariet er låst. Fullfør andre scenarier for å låse opp."
		btn.disabled = true
		var lock_style := StyleBoxFlat.new()
		lock_style.bg_color = Color(0.15, 0.17, 0.20)
		lock_style.set_corner_radius_all(7)
		btn.add_theme_stylebox_override("normal",   lock_style)
		btn.add_theme_stylebox_override("disabled", lock_style)
		btn.add_theme_color_override("font_disabled_color", C_LOCKED_TXT)
	else:
		btn.text = "▶  START SCENARIO"
		btn.tooltip_text = "Start dette scenariet (Enter)"
		var n := StyleBoxFlat.new()
		n.bg_color = s["color"].darkened(0.25)
		n.set_corner_radius_all(7)
		var h := n.duplicate() as StyleBoxFlat
		h.bg_color = s["color"].darkened(0.10)
		var p := n.duplicate() as StyleBoxFlat
		p.bg_color = s["color"].darkened(0.40)
		btn.add_theme_stylebox_override("normal",  n)
		btn.add_theme_stylebox_override("hover",   h)
		btn.add_theme_stylebox_override("pressed", p)
		btn.add_theme_color_override("font_color", Color.WHITE)
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
