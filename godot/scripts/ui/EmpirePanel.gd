## EmpirePanel v2.1 — Nordic Minimalist (scene-based).
## Empire Overview UI Panel — displays player's empire status, market position, competitors, brand reputation,
## stock market status, vertical integration chain, and active crises/opportunities.
## Layout lives in scenes/ui/EmpirePanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Leaderboard / brand / stock / integration / crisis rows are data-driven and still populated in code.
## Combines data from CompetitorAI, EmpireProgression, StockMarket, BrandReputation, CrisisCapitalism, and VerticalIntegration systems.

extends PanelContainer
class_name EmpirePanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _phase_section_title: Label = %PhaseHeader
@onready var _competitor_section_title: Label = %CompetitorHeader
@onready var _brand_section_title: Label = %BrandHeader
@onready var _stock_section_title: Label = %StockHeader
@onready var _integration_section_title: Label = %IntegrationHeader
@onready var _crisis_section_title: Label = %CrisisHeader

@onready var empire_phase_banner: VBoxContainer = %EmpirePhaseBanner
@onready var phase_name_label: Label = %PhaseNameLabel
@onready var phase_progress_bar: ProgressBar = %PhaseProgressBar
@onready var growth_strategy_label: Label = %GrowthStrategyLabel

@onready var competitor_leaderboard_container: VBoxContainer = %CompetitorList
@onready var brand_reputation_container: VBoxContainer = %BrandBox
@onready var stock_market_container: VBoxContainer = %StockBox
@onready var vertical_integration_container: VBoxContainer = %IntegrationBox
@onready var crisis_opportunities_container: VBoxContainer = %CrisisBox

# Color scheme (matching existing panels)
const COLOR_GREEN = Color("#2E6B45")
const COLOR_YELLOW = Color("#8A5A00")
const COLOR_RED = Color("#B3172F")
const COLOR_BLUE = Color("#1F5E73")
const COLOR_TEXT = "#1C1E20"
const COLOR_MUTED = "#5A6168"
const COLOR_HIGHLIGHT = "#1F5E73"

# Empire phase definitions
const EMPIRE_PHASES = {
	"SmallFarmer": {"label": "🌱 Liten Bonde", "min_revenue": 0, "color": Color.WHITE},
	"RegionalPlayer": {"label": "🏢 Regional Aktør", "min_revenue": 50_000_000, "color": COLOR_BLUE},
	"NationalLeader": {"label": "🏛️ Nasjonal Leder", "min_revenue": 500_000_000, "color": COLOR_YELLOW},
	"GlobalTitan": {"label": "🌍 Global Titan", "min_revenue": 2_000_000_000, "color": COLOR_GREEN},
	"IndustryMonopolist": {"label": "👑 Industrimonopolist", "min_revenue": 10_000_000_000, "color": COLOR_HIGHLIGHT}
}

# Vertical integration tiers
const INTEGRATION_TIERS = [
	{"id": "genetics", "label": "🧬 Genetikk", "icon": "🧬"},
	{"id": "hatchery", "label": "🥚 Klekkeri", "icon": "🥚"},
	{"id": "feed_mill", "label": "🌾 Fôrfabrikk", "icon": "🌾"},
	{"id": "sea_farm", "label": "🐟 Sjøanlegg", "icon": "🐟"},
	{"id": "processing", "label": "🏭 Prosessering", "icon": "🏭"},
	{"id": "cold_chain", "label": "❄️ Kjølekjede", "icon": "❄️"},
	{"id": "distribution", "label": "🚚 Distribusjon", "icon": "🚚"}
]

signal ipo_launched()
signal acquisition_purchased(opportunity_id: String)

var _theme: Node = null

func _ready():
	_apply_localization()
	_apply_theme_fonts()

	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		if sim_bridge.has_signal("state_updated"):
			sim_bridge.state_updated.connect(_on_state_updated)
	else:
		push_warning("EmpirePanel: SimBridge not found")

	# Initial refresh
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
	_title_label.text = tr("👑 Empire Oversikt")
	_title_label.tooltip_text = "Oversikt over ditt lakseimperium — markedsposisjon, konkurrenter, merkevare, vertikal integrasjon, og strategiske muligheter."
	_phase_section_title.text = tr("📊 Empire Fase")
	_competitor_section_title.text = tr("🏆 Konkurrent Leaderboard")
	_brand_section_title.text = tr("⭐ Merkevare & Omdømme")
	_stock_section_title.text = tr("📈 Aksjemarked")
	_integration_section_title.text = tr("🔗 Vertikal Integrasjon")
	_crisis_section_title.text = tr("⚠️ Kriser & Muligheter")
	phase_name_label.tooltip_text = "Din nåværende empire-fase basert på inntekt, markedsandel, og eide eiendeler."
	phase_progress_bar.tooltip_text = "Fremgang til neste fase."
	growth_strategy_label.tooltip_text = "Din aktive vekststrategi."

func _apply_theme_fonts() -> void:
	_theme = get_node_or_null("/root/LakskongenTheme")
	if not _theme:
		return
	LakskongenTheme.set_strong(_title_label)
	LakskongenTheme.set_strong(_phase_section_title)
	LakskongenTheme.set_strong(_competitor_section_title)
	LakskongenTheme.set_strong(_brand_section_title)
	LakskongenTheme.set_strong(_stock_section_title)
	LakskongenTheme.set_strong(_integration_section_title)
	LakskongenTheme.set_strong(_crisis_section_title)

func _on_state_updated():
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return

	var sim_bridge = get_node("/root/SimBridge")

	# Get empire data (with fallback placeholders if methods don't exist yet)
	var empire_phase = _get_empire_phase_safe(sim_bridge)
	var revenue = _get_revenue_safe(sim_bridge)
	var competitors = _get_competitors_safe(sim_bridge)
	var brand_data = _get_brand_data_safe(sim_bridge)
	var stock_data = _get_stock_data_safe(sim_bridge)
	var integration_data = _get_integration_data_safe(sim_bridge)
	var crises = _get_crises_safe(sim_bridge)
	var opportunities = _get_opportunities_safe(sim_bridge)

	# Update UI sections
	_update_empire_phase_banner(empire_phase, revenue)
	_update_competitor_leaderboard(competitors, revenue)
	_update_brand_reputation(brand_data)
	_update_stock_market(stock_data)
	_update_vertical_integration(integration_data)
	_update_crisis_opportunities(crises, opportunities)

# ══════════════════════════════════════════════════════════════════════════════
# EMPIRE PHASE BANNER
# ══════════════════════════════════════════════════════════════════════════════

func _update_empire_phase_banner(phase_data: Dictionary, revenue: float):
	var current_phase_key = phase_data.get("phase", "SmallFarmer")
	var growth_strategy = phase_data.get("strategy", "Organisk vekst")

	# Determine phase and next phase
	var phase_info = EMPIRE_PHASES.get(current_phase_key, EMPIRE_PHASES["SmallFarmer"])
	var phase_label = phase_info["label"]
	var phase_color = phase_info["color"]

	phase_name_label.text = "Fase: %s" % phase_label
	LakskongenTheme.set_text_color(phase_name_label, phase_color)

	# Calculate progress to next phase
	var next_phase_key = _get_next_phase(current_phase_key)
	if next_phase_key:
		var next_phase_info = EMPIRE_PHASES[next_phase_key]
		var current_min = phase_info["min_revenue"]
		var next_min = next_phase_info["min_revenue"]
		var progress = clamp((revenue - current_min) / float(next_min - current_min), 0.0, 1.0) * 100.0

		phase_progress_bar.value = progress
		phase_progress_bar.tooltip_text = "Fremgang til %s: %.1f%% (%s / %s NOK)" % [
			next_phase_info["label"],
			progress,
			_format_currency(revenue),
			_format_currency(next_min)
		]
	else:
		# Max phase reached
		phase_progress_bar.value = 100.0
		phase_progress_bar.tooltip_text = "Maksimal fase oppnådd! 🎉"

	growth_strategy_label.text = "Vekststrategi: %s" % growth_strategy

func _get_next_phase(current_phase: String) -> String:
	var phase_order = ["SmallFarmer", "RegionalPlayer", "NationalLeader", "GlobalTitan", "IndustryMonopolist"]
	var idx = phase_order.find(current_phase)
	if idx >= 0 and idx < phase_order.size() - 1:
		return phase_order[idx + 1]
	return ""

# ══════════════════════════════════════════════════════════════════════════════
# COMPETITOR LEADERBOARD
# ══════════════════════════════════════════════════════════════════════════════

func _update_competitor_leaderboard(competitors: Array[Dictionary], player_revenue: float):
	_clear_container(competitor_leaderboard_container)

	if competitors.is_empty():
		var label = Label.new()
		label.text = tr("Ingen konkurrentdata tilgjengelig")
		LakskongenTheme.set_text_color(label, Color(COLOR_MUTED))
		competitor_leaderboard_container.add_child(label)
		return

	# Sort by revenue (descending)
	var sorted_competitors = competitors.duplicate()
	sorted_competitors.sort_custom(func(a, b): return a.get("revenue", 0.0) > b.get("revenue", 0.0))

	# Insert player into list
	var player_data = {
		"name": "DIN BEDRIFT",
		"revenue": player_revenue,
		"market_share": 0.0,
		"strategy": "Spillerstyrt",
		"is_player": true
	}
	sorted_competitors.append(player_data)
	sorted_competitors.sort_custom(func(a, b): return a.get("revenue", 0.0) > b.get("revenue", 0.0))

	# Calculate market shares
	var total_market = 0.0
	for comp in sorted_competitors:
		total_market += comp.get("revenue", 0.0)

	for i in range(sorted_competitors.size()):
		var comp = sorted_competitors[i]
		var market_share = (comp.get("revenue", 0.0) / total_market * 100.0) if total_market > 0 else 0.0
		comp["market_share"] = market_share

	# Display leaderboard (top 10)
	var display_count = min(10, sorted_competitors.size())
	for i in range(display_count):
		var comp = sorted_competitors[i]
		var is_player = comp.get("is_player", false)
		var is_leader = (i == 0)

		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)

		# Rank
		var rank_label = Label.new()
		rank_label.text = "#%d" % (i + 1)
		rank_label.custom_minimum_size = Vector2(40, 0)
		LakskongenTheme.set_size(rank_label, 14)
		LakskongenTheme.set_text_color(rank_label, COLOR_YELLOW if is_leader else Color(COLOR_MUTED))
		row.add_child(rank_label)

		# Leader badge
		if is_leader:
			var badge = Label.new()
			badge.text = tr("👑")
			badge.tooltip_text = "Markedsleder"
			row.add_child(badge)

		# Name
		var name_label = Label.new()
		name_label.text = comp.get("name", "Ukjent")
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(name_label, 14)
		LakskongenTheme.set_text_color(name_label, COLOR_HIGHLIGHT if is_player else Color(COLOR_TEXT))
		row.add_child(name_label)

		# Revenue
		var revenue_label = Label.new()
		revenue_label.text = _format_currency(comp.get("revenue", 0.0))
		LakskongenTheme.set_size(revenue_label, 13)
		LakskongenTheme.set_text_color(revenue_label, Color(COLOR_MUTED))
		row.add_child(revenue_label)

		# Market Share
		var share_label = Label.new()
		share_label.text = "%.1f%%" % comp.get("market_share", 0.0)
		LakskongenTheme.set_size(share_label, 13)
		LakskongenTheme.set_text_color(share_label, COLOR_GREEN if is_player else Color(COLOR_MUTED))
		row.add_child(share_label)

		# Strategy
		var strategy_label = Label.new()
		strategy_label.text = comp.get("strategy", "Ukjent")
		LakskongenTheme.set_size(strategy_label, 12)
		LakskongenTheme.set_text_color(strategy_label, Color(COLOR_MUTED))
		strategy_label.tooltip_text = "Konkurrentens hovedstrategi"
		row.add_child(strategy_label)

		competitor_leaderboard_container.add_child(row)

# ══════════════════════════════════════════════════════════════════════════════
# BRAND & REPUTATION
# ══════════════════════════════════════════════════════════════════════════════

func _update_brand_reputation(brand_data: Dictionary):
	_clear_container(brand_reputation_container)

	var brand_score = brand_data.get("score", 50)
	var quality = brand_data.get("quality", 50)
	var sustainability = brand_data.get("sustainability", 50)
	var reliability = brand_data.get("reliability", 50)
	var innovation = brand_data.get("innovation", 50)
	var certifications = brand_data.get("certifications", [])
	var pricing_power = brand_data.get("pricing_power", 1.0)

	# Overall brand score
	var score_row = HBoxContainer.new()
	score_row.add_theme_constant_override("separation", 8)

	var score_label = Label.new()
	score_label.text = tr("Merkevare Score:")
	score_label.custom_minimum_size = Vector2(150, 0)
	LakskongenTheme.set_text_color(score_label, Color(COLOR_MUTED))
	score_row.add_child(score_label)

	var score_value = Label.new()
	score_value.text = "%d / 100" % brand_score
	LakskongenTheme.set_size(score_value, 16)
	LakskongenTheme.set_text_color(score_value, _brand_color(brand_score))
	score_row.add_child(score_value)

	brand_reputation_container.add_child(score_row)

	# Breakdown bars
	_create_brand_breakdown_bar(brand_reputation_container, "Kvalitet", quality)
	_create_brand_breakdown_bar(brand_reputation_container, "Bærekraft", sustainability)
	_create_brand_breakdown_bar(brand_reputation_container, "Pålitelighet", reliability)
	_create_brand_breakdown_bar(brand_reputation_container, "Innovasjon", innovation)

	# Certifications
	if certifications.size() > 0:
		var cert_label = Label.new()
		cert_label.text = tr("Aktive sertifiseringer:")
		LakskongenTheme.set_size(cert_label, 14)
		LakskongenTheme.set_text_color(cert_label, Color(COLOR_TEXT))
		brand_reputation_container.add_child(cert_label)

		for cert in certifications:
			var cert_row = Label.new()
			cert_row.text = "  ✓ %s" % cert
			LakskongenTheme.set_size(cert_row, 12)
			LakskongenTheme.set_text_color(cert_row, COLOR_GREEN)
			brand_reputation_container.add_child(cert_row)
	else:
		var no_cert = Label.new()
		no_cert.text = tr("Ingen sertifiseringer ennå")
		LakskongenTheme.set_size(no_cert, 12)
		LakskongenTheme.set_text_color(no_cert, Color(COLOR_MUTED))
		brand_reputation_container.add_child(no_cert)

	# Pricing power
	var pricing_row = HBoxContainer.new()
	pricing_row.add_theme_constant_override("separation", 8)

	var pricing_label = Label.new()
	pricing_label.text = tr("Prismakt:")
	pricing_label.custom_minimum_size = Vector2(150, 0)
	LakskongenTheme.set_text_color(pricing_label, Color(COLOR_MUTED))
	pricing_row.add_child(pricing_label)

	var pricing_value = Label.new()
	pricing_value.text = "x%.2f (%.0f%% premie)" % [pricing_power, (pricing_power - 1.0) * 100]
	LakskongenTheme.set_size(pricing_value, 14)
	LakskongenTheme.set_text_color(pricing_value, COLOR_GREEN if pricing_power > 1.0 else Color(COLOR_TEXT))
	pricing_value.tooltip_text = "Din merkevare lar deg ta høyere priser enn markedsgjennomsnittet."
	pricing_row.add_child(pricing_value)

	brand_reputation_container.add_child(pricing_row)

func _create_brand_breakdown_bar(parent: VBoxContainer, label_text: String, value: float):
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)

	var label = Label.new()
	label.text = label_text + ":"
	label.custom_minimum_size = Vector2(120, 0)
	LakskongenTheme.set_size(label, 13)
	LakskongenTheme.set_text_color(label, Color(COLOR_MUTED))
	row.add_child(label)

	var bar = ProgressBar.new()
	bar.custom_minimum_size = Vector2(200, 18)
	bar.max_value = 100
	bar.value = value
	bar.show_percentage = false
	row.add_child(bar)

	var value_label = Label.new()
	value_label.text = "%d" % int(value)
	LakskongenTheme.set_size(value_label, 12)
	LakskongenTheme.set_text_color(value_label, _brand_color(value))
	row.add_child(value_label)

	parent.add_child(row)

func _brand_color(score: float) -> Color:
	if score >= 80:
		return COLOR_GREEN
	elif score >= 60:
		return COLOR_YELLOW
	elif score >= 40:
		return Color.ORANGE
	else:
		return COLOR_RED

# ══════════════════════════════════════════════════════════════════════════════
# STOCK MARKET
# ══════════════════════════════════════════════════════════════════════════════

func _update_stock_market(stock_data: Dictionary):
	_clear_container(stock_market_container)

	var is_public = stock_data.get("is_public", false)

	if not is_public:
		var status_label = Label.new()
		status_label.text = tr("🏢 Privat selskap")
		LakskongenTheme.set_size(status_label, 14)
		LakskongenTheme.set_text_color(status_label, Color(COLOR_MUTED))
		stock_market_container.add_child(status_label)

		var eligible = stock_data.get("ipo_eligible", false)

		if eligible:
			var eligible_label = Label.new()
			eligible_label.text = tr("✅ Klar for børsnotering!")
			LakskongenTheme.set_text_color(eligible_label, COLOR_GREEN)
			stock_market_container.add_child(eligible_label)

			var ipo_button = Button.new()
			ipo_button.text = tr("🚀 Start børsintroduksjon (IPO)")
			ipo_button.tooltip_text = "Gå på børs og selg aksjer til offentligheten for å skaffe kapital."
			ipo_button.pressed.connect(_on_ipo_button_pressed)
			stock_market_container.add_child(ipo_button)
		else:
			var not_eligible_label = Label.new()
			not_eligible_label.text = tr("❌ Ikke kvalifisert for IPO ennå")
			LakskongenTheme.set_text_color(not_eligible_label, COLOR_RED)
			not_eligible_label.tooltip_text = "Trenger høyere inntekt, fortjeneste, og flere anlegg for å gå på børs."
			stock_market_container.add_child(not_eligible_label)

		return

	# Company is public — show stock data
	var share_price = stock_data.get("share_price", 0.0)
	var daily_change_pct = stock_data.get("daily_change_pct", 0.0)
	var market_cap = stock_data.get("market_cap", 0.0)
	var ownership_pct = stock_data.get("ownership_pct", 100.0)

	var public_label = Label.new()
	public_label.text = tr("📈 Børsnotert selskap (OSE)")
	LakskongenTheme.set_size(public_label, 14)
	LakskongenTheme.set_text_color(public_label, COLOR_GREEN)
	stock_market_container.add_child(public_label)

	# Share price
	var price_row = HBoxContainer.new()
	price_row.add_theme_constant_override("separation", 8)

	var price_label = Label.new()
	price_label.text = tr("Aksjekurs:")
	price_label.custom_minimum_size = Vector2(120, 0)
	LakskongenTheme.set_text_color(price_label, Color(COLOR_MUTED))
	price_row.add_child(price_label)

	var price_value = Label.new()
	var change_color = COLOR_GREEN if daily_change_pct >= 0 else COLOR_RED
	var change_arrow = "▲" if daily_change_pct >= 0 else "▼"
	price_value.text = "%.2f NOK %s %+.2f%%" % [share_price, change_arrow, daily_change_pct]
	LakskongenTheme.set_size(price_value, 14)
	LakskongenTheme.set_text_color(price_value, change_color)
	price_row.add_child(price_value)

	stock_market_container.add_child(price_row)

	# Market cap
	var cap_row = HBoxContainer.new()
	cap_row.add_theme_constant_override("separation", 8)

	var cap_label = Label.new()
	cap_label.text = tr("Markedsverdi:")
	cap_label.custom_minimum_size = Vector2(120, 0)
	LakskongenTheme.set_text_color(cap_label, Color(COLOR_MUTED))
	cap_row.add_child(cap_label)

	var cap_value = Label.new()
	cap_value.text = _format_currency(market_cap)
	LakskongenTheme.set_size(cap_value, 14)
	LakskongenTheme.set_text_color(cap_value, Color(COLOR_TEXT))
	cap_row.add_child(cap_value)

	stock_market_container.add_child(cap_row)

	# Ownership badge
	var ownership_row = HBoxContainer.new()
	ownership_row.add_theme_constant_override("separation", 8)

	var ownership_label = Label.new()
	ownership_label.text = tr("Din eierandel:")
	ownership_label.custom_minimum_size = Vector2(120, 0)
	LakskongenTheme.set_text_color(ownership_label, Color(COLOR_MUTED))
	ownership_row.add_child(ownership_label)

	var ownership_value = Label.new()
	ownership_value.text = "%.1f%%" % ownership_pct
	LakskongenTheme.set_size(ownership_value, 14)
	var ownership_color = COLOR_GREEN if ownership_pct > 50 else COLOR_YELLOW if ownership_pct > 25 else COLOR_RED
	LakskongenTheme.set_text_color(ownership_value, ownership_color)
	ownership_value.tooltip_text = "Din andel av selskapet. Under 50% kan føre til kontrollrisiko."
	ownership_row.add_child(ownership_value)

	stock_market_container.add_child(ownership_row)

func _on_ipo_button_pressed():
	emit_signal("ipo_launched")
	# TODO: Trigger IPO flow via SimBridge when method is implemented

# ══════════════════════════════════════════════════════════════════════════════
# VERTICAL INTEGRATION TRACKER
# ══════════════════════════════════════════════════════════════════════════════

func _update_vertical_integration(integration_data: Dictionary):
	_clear_container(vertical_integration_container)

	var owned_tiers = integration_data.get("owned_tiers", [])
	var integration_score = integration_data.get("integration_score", 0.0)

	# Integration score
	var score_row = HBoxContainer.new()
	score_row.add_theme_constant_override("separation", 8)

	var score_label = Label.new()
	score_label.text = tr("Integrasjonsscore:")
	score_label.custom_minimum_size = Vector2(150, 0)
	LakskongenTheme.set_text_color(score_label, Color(COLOR_MUTED))
	score_row.add_child(score_label)

	var score_bar = ProgressBar.new()
	score_bar.custom_minimum_size = Vector2(200, 20)
	score_bar.max_value = 100
	score_bar.value = integration_score
	score_bar.show_percentage = true
	score_bar.tooltip_text = "Andel av verdikjeden du kontrollerer."
	score_row.add_child(score_bar)

	vertical_integration_container.add_child(score_row)

	# Visual chain
	var chain_container = HBoxContainer.new()
	chain_container.add_theme_constant_override("separation", 4)
	vertical_integration_container.add_child(chain_container)

	for tier in INTEGRATION_TIERS:
		var tier_id = tier["id"]
		var tier_icon = tier["icon"]
		var tier_label_text = tier["label"]
		var is_owned = tier_id in owned_tiers

		var tier_box = VBoxContainer.new()
		tier_box.add_theme_constant_override("separation", 2)

		var icon_label = Label.new()
		icon_label.text = tier_icon
		LakskongenTheme.set_size(icon_label, 20)
		icon_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		icon_label.tooltip_text = tier_label_text
		tier_box.add_child(icon_label)

		var status_label = Label.new()
		status_label.text = tr("✓")
		LakskongenTheme.set_size(status_label, 14)
		LakskongenTheme.set_text_color(status_label, COLOR_GREEN if is_owned else COLOR_RED)
		status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		tier_box.add_child(status_label)

		chain_container.add_child(tier_box)

		# Add arrow between tiers (except last)
		if tier != INTEGRATION_TIERS[-1]:
			var arrow = Label.new()
			arrow.text = tr("→")
			LakskongenTheme.set_size(arrow, 16)
			LakskongenTheme.set_text_color(arrow, Color(COLOR_MUTED))
			chain_container.add_child(arrow)

	# Tier details list
	var details_label = Label.new()
	details_label.text = tr("Eide enheter:")
	LakskongenTheme.set_size(details_label, 14)
	LakskongenTheme.set_text_color(details_label, Color(COLOR_TEXT))
	vertical_integration_container.add_child(details_label)

	if owned_tiers.size() > 0:
		for tier_id in owned_tiers:
			var tier = INTEGRATION_TIERS.filter(func(t): return t["id"] == tier_id)
			if tier.size() > 0:
				var tier_label = Label.new()
				tier_label.text = "  %s %s" % [tier[0]["icon"], tier[0]["label"]]
				LakskongenTheme.set_size(tier_label, 12)
				LakskongenTheme.set_text_color(tier_label, COLOR_GREEN)
				vertical_integration_container.add_child(tier_label)
	else:
		var no_integration = Label.new()
		no_integration.text = tr("  Ingen vertikal integrasjon ennå")
		LakskongenTheme.set_size(no_integration, 12)
		LakskongenTheme.set_text_color(no_integration, Color(COLOR_MUTED))
		vertical_integration_container.add_child(no_integration)

# ══════════════════════════════════════════════════════════════════════════════
# CRISIS & OPPORTUNITIES
# ══════════════════════════════════════════════════════════════════════════════

func _update_crisis_opportunities(crises: Array[Dictionary], opportunities: Array[Dictionary]):
	_clear_container(crisis_opportunities_container)

	# Active crises
	if crises.size() > 0:
		var crisis_header = Label.new()
		crisis_header.text = tr("🚨 Aktive kriser:")
		LakskongenTheme.set_size(crisis_header, 14)
		LakskongenTheme.set_text_color(crisis_header, COLOR_RED)
		crisis_opportunities_container.add_child(crisis_header)

		for crisis in crises:
			var crisis_row = Label.new()
			crisis_row.text = "  ⚠️ %s" % crisis.get("title", "Ukjent krise")
			LakskongenTheme.set_size(crisis_row, 12)
			LakskongenTheme.set_text_color(crisis_row, COLOR_RED)
			crisis_row.tooltip_text = crisis.get("description", "")
			crisis_opportunities_container.add_child(crisis_row)
	else:
		var no_crisis = Label.new()
		no_crisis.text = tr("✅ Ingen aktive kriser")
		LakskongenTheme.set_size(no_crisis, 12)
		LakskongenTheme.set_text_color(no_crisis, COLOR_GREEN)
		crisis_opportunities_container.add_child(no_crisis)

	# Spacer
	var spacer = Control.new()
	spacer.custom_minimum_size = Vector2(0, 10)
	crisis_opportunities_container.add_child(spacer)

	# Acquisition opportunities
	if opportunities.size() > 0:
		var opp_header = Label.new()
		opp_header.text = tr("💼 Oppkjøpsmuligheter:")
		LakskongenTheme.set_size(opp_header, 14)
		LakskongenTheme.set_text_color(opp_header, COLOR_BLUE)
		crisis_opportunities_container.add_child(opp_header)

		for opp in opportunities:
			var opp_row = HBoxContainer.new()
			opp_row.add_theme_constant_override("separation", 8)

			var opp_label = Label.new()
			opp_label.text = opp.get("title", "Ukjent mulighet")
			opp_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			LakskongenTheme.set_size(opp_label, 12)
			LakskongenTheme.set_text_color(opp_label, Color(COLOR_TEXT))
			opp_label.tooltip_text = opp.get("description", "")
			opp_row.add_child(opp_label)

			var price_label = Label.new()
			price_label.text = _format_currency(opp.get("price", 0.0))
			LakskongenTheme.set_size(price_label, 12)
			LakskongenTheme.set_text_color(price_label, Color(COLOR_MUTED))
			opp_row.add_child(price_label)

			var buy_button = Button.new()
			buy_button.text = tr("Kjøp")
			buy_button.tooltip_text = "Kjøp denne eiendelen og utvid imperiet ditt."
			buy_button.custom_minimum_size = Vector2(60, 0)
			buy_button.pressed.connect(_on_buy_opportunity.bind(opp.get("id", "")))
			opp_row.add_child(buy_button)

			crisis_opportunities_container.add_child(opp_row)
	else:
		var no_opp = Label.new()
		no_opp.text = tr("Ingen oppkjøpsmuligheter akkurat nå")
		LakskongenTheme.set_size(no_opp, 12)
		LakskongenTheme.set_text_color(no_opp, Color(COLOR_MUTED))
		crisis_opportunities_container.add_child(no_opp)

func _on_buy_opportunity(opportunity_id: String):
	emit_signal("acquisition_purchased", opportunity_id)
	# TODO: Trigger acquisition flow via SimBridge when method is implemented

# ══════════════════════════════════════════════════════════════════════════════
# SAFE DATA GETTERS (with fallback placeholders)
# ══════════════════════════════════════════════════════════════════════════════

func _get_empire_phase_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_empire_phase"):
		var raw = sim_bridge.get_empire_phase()
		if raw is Dictionary:
			return raw
		# Backend reports the phase key as String; normalize to panel shape.
		return {"phase": str(raw), "strategy": "Organisk vekst"}
	else:
		# Placeholder
		return {
			"phase": "SmallFarmer",
			"strategy": "Organisk vekst"
		}

func _get_revenue_safe(sim_bridge: Node) -> float:
	if sim_bridge.has_method("get_revenue"):
		return sim_bridge.get_revenue()
	elif "balance" in sim_bridge:
		return sim_bridge.balance * 0.1  # Rough estimate
	else:
		return 10_000_000.0  # 10M NOK placeholder

func _get_competitors_safe(sim_bridge: Node) -> Array[Dictionary]:
	if sim_bridge.has_method("get_competitors"):
		return sim_bridge.get_competitors()
	else:
		# Placeholder competitors
		return [
			{"name": "Nordmyr Seafood AS", "revenue": 150_000_000.0, "market_share": 0.0, "strategy": "Volumfokusert"},
			{"name": "Fjordhavs Laks", "revenue": 120_000_000.0, "market_share": 0.0, "strategy": "Premiummerke"},
			{"name": "Atlantica Aqua", "revenue": 90_000_000.0, "market_share": 0.0, "strategy": "Kostnadsoptimalisering"}
		]

func _get_brand_data_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_brand_reputation"):
		return sim_bridge.get_brand_reputation()
	else:
		# Placeholder
		return {
			"score": 55,
			"quality": 60,
			"sustainability": 50,
			"reliability": 65,
			"innovation": 45,
			"certifications": ["ASC", "GlobalGAP"],
			"pricing_power": 1.05
		}

func _get_stock_data_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_stock_market_status"):
		return sim_bridge.get_stock_market_status()
	else:
		# Placeholder
		return {
			"is_public": false,
			"ipo_eligible": false,
			"share_price": 0.0,
			"daily_change_pct": 0.0,
			"market_cap": 0.0,
			"ownership_pct": 100.0
		}

func _get_integration_data_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_vertical_integration"):
		return sim_bridge.get_vertical_integration()
	else:
		# Placeholder
		return {
			"owned_tiers": ["sea_farm"],
			"integration_score": 14.3  # 1 out of 7 tiers
		}

func _get_crises_safe(sim_bridge: Node) -> Array[Dictionary]:
	if sim_bridge.has_method("get_active_crises"):
		return sim_bridge.get_active_crises()
	else:
		# Placeholder
		return []

func _get_opportunities_safe(sim_bridge: Node) -> Array[Dictionary]:
	if sim_bridge.has_method("get_acquisition_opportunities"):
		return sim_bridge.get_acquisition_opportunities()
	else:
		# Placeholder
		return [
			{"id": "opp_1", "title": "Solvik Klekkeri", "description": "Lokalt klekkeri i nærheten av dine anlegg.", "price": 25_000_000.0},
			{"id": "opp_2", "title": "Bergens Fôrfabrikk", "description": "Fôrproduksjon med stor kapasitet.", "price": 80_000_000.0}
		]

# ══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ══════════════════════════════════════════════════════════════════════════════

func _clear_container(container: VBoxContainer):
	if container == null:
		return
	for child in container.get_children():
		child.queue_free()

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2f mrd NOK" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1f mill NOK" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0f k NOK" % (value / 1_000.0)
	else:
		return "%.0f NOK" % value
