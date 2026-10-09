## SustainabilityPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/SustainabilityPanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Category rows are data-driven and still populated in code.

extends PanelContainer
class_name SustainabilityPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _header_label: Label = %Header
@onready var score_value_label: Label = %ScoreValueLabel
@onready var score_unit_label: Label = %ScoreUnitLabel
@onready var score_caption_label: Label = %ScoreCaptionLabel
@onready var certification_label: Label = %CertificationBadgeLabel
@onready var warning_label: Label = %WarningLabel
@onready var price_multiplier_label: Label = %price_multiplier_label
@onready var _breakdown_header: Label = %BreakdownHeader
@onready var category_container: VBoxContainer = %category_container

# Legacy alias: older code/C# docs refer to score_label as the big score readout.
# It points at the dedicated value cell (numeric only, no format string).
var score_label: Label = null

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim_bridge: Node = null
var _theme: Node = null

# Color scheme from design tokens
const COLOR_BG = "#2E3440"
const COLOR_PANEL = "#3B4252"
const COLOR_TEXT = "#2E3440"
const COLOR_MUTED = "#7B8794"
const COLOR_ACCENT = "#88C0D0"
const COLOR_ACCENT_2 = "#0A4D68"
const COLOR_DANGER = "#BF616A"

# Certification level colors
const CERT_COLORS = {
	"NonCompliant": "#BF616A",        # Red
	"BasicCompliance": "#EBCB8B",     # Gold
	"ASCCertified": "#88C0D0",        # Cyan (accent)
	"OrganicCertified": "#A3BE8C",    # Green
	"SustainabilityLeader": "#A3BE8C" # Bright green
}

# Current data
var current_score: int = 0
var current_certification: String = "NonCompliant"
var category_scores: Dictionary = {}

signal score_updated(score: int, certification: String)

func _ready() -> void:
	_sim_bridge = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	score_label = score_value_label

	if _sim_bridge and _sim_bridge.has_signal("state_updated"):
		_sim_bridge.state_updated.connect(_on_state_updated)
	elif _sim_bridge == null:
		push_warning("SustainabilityPanel: SimBridge not found")

	_apply_localization()
	_apply_theme_fonts()
	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_header_label.text = tr("UI_SUSTAINABILITY_HEADER")
	_header_label.tooltip_text = tr("UI_SUSTAINABILITY_TOOLTIP")
	_breakdown_header.text = tr("UI_SUSTAINABILITY_BREAKDOWN")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		_header_label.add_theme_font_override("font", _theme.font_heading_bold)
		score_value_label.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		certification_label.add_theme_font_override("font", _theme.font_heading_semibold)
		_breakdown_header.add_theme_font_override("font", _theme.font_heading_semibold)

func update_display(score: int, certification: String, categories: Dictionary):
	"""Update all UI elements with new sustainability data"""
	current_score = score
	current_certification = certification
	category_scores = categories

	# Update score value cell (numeric only) + unit cell (static "/100").
	if score_value_label:
		score_value_label.text = str(score)
		score_value_label.add_theme_color_override("font_color", _get_score_color(score))
	score_label = score_value_label
	if score_unit_label:
		score_unit_label.text = "/100"

	# Update translated format caption on its own row (Norwegian key kept).
	# Falls back to the Norwegian string from translations.csv if the
	# translation database is unavailable (tr() then returns the key itself,
	# which has no placeholder and would otherwise display the raw key).
	if score_caption_label:
		score_caption_label.text = _tr_format("UI_SUSTAINABILITY_SCORE_FORMAT", "Poeng: %s") % score

	# Update certification badge on its own row (no warning icon here).
	if certification_label:
		var cert_display = _format_certification_badge(certification)
		certification_label.text = cert_display
		certification_label.add_theme_color_override("font_color", Color(CERT_COLORS.get(certification, COLOR_MUTED)))

	# Update warning on its own row (visible only when non-compliant).
	if warning_label:
		if certification == "NonCompliant":
			warning_label.visible = true
			warning_label.text = _format_warning(certification)
		else:
			warning_label.visible = false

	# Update price multiplier
	if price_multiplier_label:
		var multiplier = _get_price_multiplier(certification)
		var bonus_text = ""
		if multiplier > 1.0:
			bonus_text = "Price Bonus: +%.0f%%" % ((multiplier - 1.0) * 100)
		elif multiplier < 1.0:
			bonus_text = "Price Penalty: %.0f%%" % ((multiplier - 1.0) * 100)
		else:
			bonus_text = "Standard Pricing"
		price_multiplier_label.text = bonus_text

	# Update category breakdown (clears container first)
	_update_categories(categories)

	# Live traffic-light zone strip (Phase 1.3) on top of the bars.
	_update_traffic_strip()

	emit_signal("score_updated", score, certification)

func _update_traffic_strip() -> void:
	"""Live zone traffic-light summary (Phase 1.3). First row of the breakdown."""
	if not category_container:
		return
	var zones: Array = []
	if _sim_bridge and "traffic_zones" in _sim_bridge:
		zones = _sim_bridge.traffic_zones
	if zones.is_empty():
		return
	var greens := 0
	var yellows := 0
	var reds := 0
	for z in zones:
		match str(z.get("color", "Yellow")):
			"Green": greens += 1
			"Red": reds += 1
			_: yellows += 1
	var row := HBoxContainer.new()
	row.name = "TrafficStrip"
	row.add_theme_constant_override("separation", 8)
	category_container.add_child(row)
	category_container.move_child(row, 0)
	var lbl := Label.new()
	lbl.text = "🚦 Soner: 🟢 %d  🟡 %d  🔴 %d%s" % [greens, yellows, reds,
		" — RØDT strammer MAB" if reds > 0 else ""]
	lbl.add_theme_font_size_override("font_size", 13)
	lbl.add_theme_color_override("font_color", Color(COLOR_TEXT))
	row.add_child(lbl)

func _update_categories(categories: Dictionary) -> void:
	"""Update category breakdown bars"""
	if not category_container:
		return

	# Clear existing children
	for child in category_container.get_children():
		child.queue_free()

	# Category display order and max points
	var category_order = [
		{"name": "SeaLiceManagement", "display": "Sea Lice Management", "max": 25},
		{"name": "EscapePrevention", "display": "Escape Prevention", "max": 20},
		{"name": "FeedSustainability", "display": "Feed Sustainability", "max": 20},
		{"name": "CarbonFootprint", "display": "Carbon Footprint", "max": 15},
		{"name": "WelfareStandards", "display": "Welfare Standards", "max": 10},
		{"name": "EcosystemImpact", "display": "Ecosystem Impact", "max": 10}
	]

	for cat in category_order:
		var score = categories.get(cat.name, 0)
		var max_score = cat.max
		_create_category_bar(cat.display, score, max_score)

func _create_category_bar(label_text: String, score: int, max_score: int):
	"""Create a single category progress bar"""
	var container = HBoxContainer.new()
	container.add_theme_constant_override("separation", 8)
	category_container.add_child(container)

	# Label
	var label = Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(200, 0)
	label.add_theme_font_size_override("font_size", 12)
	label.add_theme_color_override("font_color", Color(COLOR_TEXT))
	container.add_child(label)

	# Progress bar
	var progress = ProgressBar.new()
	progress.max_value = max_score
	progress.value = score
	progress.custom_minimum_size = Vector2(150, 20)
	progress.show_percentage = false
	container.add_child(progress)

	# Score text
	var score_text = Label.new()
	score_text.text = tr("UI_SUSTAINABILITY_CATEGORY_SCORE") % [score, max_score]
	score_text.custom_minimum_size = Vector2(50, 0)
	score_text.add_theme_font_size_override("font_size", 12)
	score_text.add_theme_color_override("font_color", Color(COLOR_MUTED))
	container.add_child(score_text)

func _get_score_color(score: int) -> Color:
	"""Get color based on score (red -> yellow -> green gradient)"""
	if score >= 85:
		return Color("#A3BE8C")  # Bright green
	elif score >= 70:
		return Color("#A3BE8C")  # Green
	elif score >= 50:
		return Color("#EBCB8B")  # Gold
	else:
		return Color("#BF616A")  # Red/danger

func _format_certification_badge(cert: String) -> String:
	"""Certification badge text (own row, no warning prefix)."""
	match cert:
		"NonCompliant":
			return "Non-Compliant"
		"BasicCompliance":
			return "✓ Basic Compliance"
		"ASCCertified":
			return "⭐ ASC Certified"
		"OrganicCertified":
			return "🌿 Organic Certified"
		"SustainabilityLeader":
			return "🏆 Sustainability Leader"
		_:
			return cert

func _format_warning(cert: String) -> String:
	"""Warning row text (own row, only shown when relevant)."""
	match cert:
		"NonCompliant":
			return "⚠️ Non-Compliant"
		_:
			return ""

func _tr_format(key: String, fallback: String) -> String:
	"""Translate key, falling back to bundled Norwegian if DB is unavailable."""
	var s = tr(key)
	if s == key or "%" not in s:
		return fallback
	return s

func _format_certification(cert: String) -> String:
	"""Format certification level for display (legacy: badge + warning combined)."""
	if cert == "NonCompliant":
		return "⚠️ Non-Compliant"
	return _format_certification_badge(cert)

func _get_price_multiplier(cert: String) -> float:
	"""Get price multiplier from certification level (from bridge if available, otherwise fallback)"""
	if _sim_bridge and _sim_bridge.has_method("get_price_multiplier"):
		return _sim_bridge.get_price_multiplier()

	# Fallback to local calculation
	match cert:
		"NonCompliant":
			return 0.85
		"BasicCompliance":
			return 1.00
		"ASCCertified":
			return 1.08
		"OrganicCertified":
			return 1.15
		"SustainabilityLeader":
			return 1.15
		_:
			return 1.00

# === EXTERNAL API ===
# Called from C# via signals or direct method calls

func on_score_changed(score: int, certification: String):
	"""Handler for C# SustainabilityManager.OnScoreChanged event"""
	# Fetch latest category breakdown from game state
	# For now, just update with current data
	update_display(score, certification, category_scores)

func set_category_scores(categories: Dictionary):
	"""Set category breakdown (called from game manager)"""
	category_scores = categories
	update_display(current_score, current_certification, categories)

# === BRIDGE DATA REFRESH ===

func _on_state_updated():
	"""Called when SimBridge state updates"""
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if _sim_bridge == null:
		return

	# Get sustainability score and certification level
	var score = _sim_bridge.sustainability_score
	var cert = _sim_bridge.get_certification_level()

	# Get detailed breakdown (C# returns earned points per category already:
	# SeaLice 0-25, Escape 0-20, Feed 0-20, Carbon 0-15, Welfare 0-10, Eco 0-10).
	var breakdown = _sim_bridge.get_sustainability_breakdown()

	# Pass through raw points (no renormalization — matches C# weights).
	var categories = {
		"SeaLiceManagement": int(breakdown.get("sea_lice", 0.0)),
		"EscapePrevention": int(breakdown.get("escape", 0.0)),
		"FeedSustainability": int(breakdown.get("feed", 0.0)),
		"CarbonFootprint": int(breakdown.get("carbon", 0.0)),
		"WelfareStandards": int(breakdown.get("welfare", 0.0)),
		"EcosystemImpact": int(breakdown.get("ecosystem", 0.0))
	}

	# Update display with live data
	update_display(score, cert, categories)
