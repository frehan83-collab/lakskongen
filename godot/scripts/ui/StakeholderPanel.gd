## StakeholderPanel.gd
## Stakeholder & Political Relations UI Panel — displays faction relationships, demands,
## reputation impact, and political action options.
## Covers: Regulators, Environmental NGOs, Local Communities, Workers' Unions, Industry Associations, Media.

extends PanelContainer
class_name StakeholderPanel

# UI References
@onready var faction_overview_container: VBoxContainer
@onready var active_demands_container: VBoxContainer
@onready var reputation_impact_container: VBoxContainer
@onready var political_actions_container: VBoxContainer

# Color scheme (matching existing panels)
const COLOR_GREEN = Color("#A3BE8C")
const COLOR_YELLOW = Color("#EBCB8B")
const COLOR_RED = Color("#BF616A")
const COLOR_BLUE = Color("#5E81AC")
const COLOR_TEXT = "#2E3440"
const COLOR_MUTED = "#7B8794"
const COLOR_HIGHLIGHT = "#0A4D68"

# Stakeholder factions
const STAKEHOLDER_FACTIONS = [
	{"id": "regulators", "name": "Reguleringsmyndigheter", "icon": "⚖️"},
	{"id": "environmental_ngos", "name": "Miljøorganisasjoner", "icon": "🌿"},
	{"id": "local_communities", "name": "Lokalsamfunn", "icon": "🏘️"},
	{"id": "workers_unions", "name": "Arbeidsunioner", "icon": "👷"},
	{"id": "industry_associations", "name": "Bransjeorganisasjoner", "icon": "🏢"},
	{"id": "media", "name": "Media", "icon": "📰"}
]

signal lobby_action_pressed(faction_id: String)
signal donate_action_pressed(faction_id: String)
signal negotiate_action_pressed(demand_id: String)
signal ignore_demand_pressed(demand_id: String)

func _ready():
	if not faction_overview_container:
		_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		sim_bridge.state_updated.connect(_on_state_updated)
		print("StakeholderPanel: connected to SimBridge")
	else:
		push_warning("StakeholderPanel: SimBridge not found")
	
	# Initial refresh
	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _create_ui():
	"""Dynamically create UI elements"""
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var scroll = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	margin.add_child(scroll)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 20)
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(vbox)
	
	# ══════════════════════════════════════════════════════════════════════════
	# HEADER
	# ══════════════════════════════════════════════════════════════════════════
	var header = Label.new()
	header.text = tr("🤝 Interessenter & Politikk")
	header.add_theme_font_size_override("font_size", 22)
	header.add_theme_color_override("font_color", Color(COLOR_HIGHLIGHT))
	header.tooltip_text = "Oversikt over dine relasjoner med interessenter, aktive krav, og politisk innflytelse."
	vbox.add_child(header)
	
	var separator0 = HSeparator.new()
	vbox.add_child(separator0)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 1. FACTION OVERVIEW
	# ══════════════════════════════════════════════════════════════════════════
	var faction_header = Label.new()
	faction_header.text = tr("👥 Interessenter Oversikt")
	faction_header.add_theme_font_size_override("font_size", 18)
	faction_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(faction_header)
	
	faction_overview_container = VBoxContainer.new()
	faction_overview_container.add_theme_constant_override("separation", 8)
	vbox.add_child(faction_overview_container)
	
	var separator1 = HSeparator.new()
	vbox.add_child(separator1)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 2. ACTIVE DEMANDS
	# ══════════════════════════════════════════════════════════════════════════
	var demands_header = Label.new()
	demands_header.text = tr("📋 Aktive Krav")
	demands_header.add_theme_font_size_override("font_size", 18)
	demands_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(demands_header)
	
	active_demands_container = VBoxContainer.new()
	active_demands_container.add_theme_constant_override("separation", 8)
	vbox.add_child(active_demands_container)
	
	var separator2 = HSeparator.new()
	vbox.add_child(separator2)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 3. REPUTATION IMPACT
	# ══════════════════════════════════════════════════════════════════════════
	var reputation_header = Label.new()
	reputation_header.text = tr("📊 Omdømmeeffekter")
	reputation_header.add_theme_font_size_override("font_size", 18)
	reputation_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(reputation_header)
	
	reputation_impact_container = VBoxContainer.new()
	reputation_impact_container.add_theme_constant_override("separation", 6)
	vbox.add_child(reputation_impact_container)
	
	var separator3 = HSeparator.new()
	vbox.add_child(separator3)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 4. POLITICAL ACTIONS
	# ══════════════════════════════════════════════════════════════════════════
	var actions_header = Label.new()
	actions_header.text = tr("🎯 Politiske Handlinger")
	actions_header.add_theme_font_size_override("font_size", 18)
	actions_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(actions_header)
	
	political_actions_container = VBoxContainer.new()
	political_actions_container.add_theme_constant_override("separation", 8)
	vbox.add_child(political_actions_container)

func _on_state_updated():
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Get stakeholder data (with fallback placeholders if methods don't exist yet)
	var faction_relationships = _get_faction_relationships_safe(sim_bridge)
	var active_demands = _get_active_demands_safe(sim_bridge)
	var reputation_impacts = _get_reputation_impacts_safe(sim_bridge)
	var political_actions_available = _get_political_actions_safe(sim_bridge)
	
	# Update UI sections
	_update_faction_overview(faction_relationships)
	_update_active_demands(active_demands)
	_update_reputation_impact(reputation_impacts)
	_update_political_actions(political_actions_available)

# ══════════════════════════════════════════════════════════════════════════════
# FACTION OVERVIEW
# ══════════════════════════════════════════════════════════════════════════════

func _update_faction_overview(faction_relationships: Dictionary):
	_clear_container(faction_overview_container)
	
	for faction in STAKEHOLDER_FACTIONS:
		var faction_id = faction["id"]
		var faction_name = faction["name"]
		var faction_icon = faction["icon"]
		
		var relationship_score = faction_relationships.get(faction_id, 50)
		
		# Create faction row
		var faction_row = VBoxContainer.new()
		faction_row.add_theme_constant_override("separation", 4)
		
		# Faction header (icon + name)
		var header_row = HBoxContainer.new()
		header_row.add_theme_constant_override("separation", 8)
		
		var icon_label = Label.new()
		icon_label.text = faction_icon
		icon_label.add_theme_font_size_override("font_size", 20)
		header_row.add_child(icon_label)
		
		var name_label = Label.new()
		name_label.text = faction_name
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_label.add_theme_font_size_override("font_size", 15)
		name_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		header_row.add_child(name_label)
		
		var score_label = Label.new()
		score_label.text = "%d / 100" % int(relationship_score)
		score_label.add_theme_font_size_override("font_size", 14)
		score_label.add_theme_color_override("font_color", _relationship_color(relationship_score))
		header_row.add_child(score_label)
		
		faction_row.add_child(header_row)
		
		# Relationship bar
		var bar_container = HBoxContainer.new()
		bar_container.add_theme_constant_override("separation", 8)
		
		var bar = ProgressBar.new()
		bar.custom_minimum_size = Vector2(350, 20)
		bar.max_value = 100
		bar.value = relationship_score
		bar.show_percentage = false
		bar.tooltip_text = "Relasjonsscore med %s" % faction_name
		bar_container.add_child(bar)
		
		# Status label
		var status_label = Label.new()
		status_label.text = _relationship_status_text(relationship_score)
		status_label.add_theme_font_size_override("font_size", 12)
		status_label.add_theme_color_override("font_color", _relationship_color(relationship_score))
		bar_container.add_child(status_label)
		
		faction_row.add_child(bar_container)
		faction_overview_container.add_child(faction_row)

func _relationship_color(score: float) -> Color:
	if score < 30:
		return COLOR_RED
	elif score < 60:
		return COLOR_YELLOW
	else:
		return COLOR_GREEN

func _relationship_status_text(score: float) -> String:
	if score >= 80:
		return "Utmerket"
	elif score >= 60:
		return "God"
	elif score >= 40:
		return "Nøytral"
	elif score >= 20:
		return "Dårlig"
	else:
		return "Kritisk"

# ══════════════════════════════════════════════════════════════════════════════
# ACTIVE DEMANDS
# ══════════════════════════════════════════════════════════════════════════════

func _update_active_demands(demands: Array[Dictionary]):
	_clear_container(active_demands_container)
	
	if demands.is_empty():
		var no_demands = Label.new()
		no_demands.text = tr("✅ Ingen aktive krav for øyeblikket")
		no_demands.add_theme_font_size_override("font_size", 13)
		no_demands.add_theme_color_override("font_color", COLOR_GREEN)
		active_demands_container.add_child(no_demands)
		return
	
	for demand in demands:
		var demand_id = demand.get("id", "")
		var faction_id = demand.get("faction_id", "")
		var title = demand.get("title", "Ukjent krav")
		var description = demand.get("description", "")
		var deadline_days = demand.get("deadline_days", 30)
		var consequence = demand.get("consequence", "Ukjente konsekvenser")
		
		# Find faction icon
		var faction_icon = "❓"
		for faction in STAKEHOLDER_FACTIONS:
			if faction["id"] == faction_id:
				faction_icon = faction["icon"]
				break
		
		# Demand card
		var demand_card = PanelContainer.new()
		var demand_vbox = VBoxContainer.new()
		demand_vbox.add_theme_constant_override("separation", 6)
		
		# Title row
		var title_row = HBoxContainer.new()
		title_row.add_theme_constant_override("separation", 8)
		
		var icon = Label.new()
		icon.text = faction_icon
		icon.add_theme_font_size_override("font_size", 16)
		title_row.add_child(icon)
		
		var title_label = Label.new()
		title_label.text = title
		title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		title_label.add_theme_font_size_override("font_size", 14)
		title_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		title_row.add_child(title_label)
		
		var deadline_label = Label.new()
		var deadline_color = COLOR_RED if deadline_days < 7 else COLOR_YELLOW if deadline_days < 30 else COLOR_GREEN
		deadline_label.text = "⏳ %d dager" % deadline_days
		deadline_label.add_theme_font_size_override("font_size", 12)
		deadline_label.add_theme_color_override("font_color", deadline_color)
		deadline_label.tooltip_text = "Frist før konsekvenser aktiveres"
		title_row.add_child(deadline_label)
		
		demand_vbox.add_child(title_row)
		
		# Description
		if description != "":
			var desc_label = Label.new()
			desc_label.text = description
			desc_label.add_theme_font_size_override("font_size", 12)
			desc_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
			desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			demand_vbox.add_child(desc_label)
		
		# Consequence
		var consequence_label = Label.new()
		consequence_label.text = "⚠️ Konsekvens: %s" % consequence
		consequence_label.add_theme_font_size_override("font_size", 12)
		consequence_label.add_theme_color_override("font_color", COLOR_RED)
		demand_vbox.add_child(consequence_label)
		
		# Action buttons
		var action_row = HBoxContainer.new()
		action_row.add_theme_constant_override("separation", 8)
		
		var negotiate_btn = Button.new()
		negotiate_btn.text = tr("🤝 Forhandle")
		negotiate_btn.tooltip_text = "Prøv å forhandle ned kravet"
		negotiate_btn.pressed.connect(_on_negotiate_pressed.bind(demand_id))
		action_row.add_child(negotiate_btn)
		
		var ignore_btn = Button.new()
		ignore_btn.text = tr("🚫 Ignorer")
		ignore_btn.tooltip_text = "Ignorer kravet og aksepter konsekvensene"
		ignore_btn.pressed.connect(_on_ignore_demand_pressed.bind(demand_id))
		action_row.add_child(ignore_btn)
		
		demand_vbox.add_child(action_row)
		demand_card.add_child(demand_vbox)
		active_demands_container.add_child(demand_card)

func _on_negotiate_pressed(demand_id: String):
	print("StakeholderPanel: Negotiate demand: %s" % demand_id)
	emit_signal("negotiate_action_pressed", demand_id)

func _on_ignore_demand_pressed(demand_id: String):
	print("StakeholderPanel: Ignore demand: %s" % demand_id)
	emit_signal("ignore_demand_pressed", demand_id)

# ══════════════════════════════════════════════════════════════════════════════
# REPUTATION IMPACT
# ══════════════════════════════════════════════════════════════════════════════

func _update_reputation_impact(impacts: Dictionary):
	_clear_container(reputation_impact_container)
	
	# Show how each faction's stance affects operations
	for faction in STAKEHOLDER_FACTIONS:
		var faction_id = faction["id"]
		var faction_name = faction["name"]
		var faction_icon = faction["icon"]
		
		var impact_data = impacts.get(faction_id, {})
		var impact_text = impact_data.get("effect", "Nøytral påvirkning")
		var impact_severity = impact_data.get("severity", "neutral")  # positive, neutral, negative
		
		var impact_row = HBoxContainer.new()
		impact_row.add_theme_constant_override("separation", 8)
		
		var icon_label = Label.new()
		icon_label.text = faction_icon
		icon_label.add_theme_font_size_override("font_size", 16)
		impact_row.add_child(icon_label)
		
		var name_label = Label.new()
		name_label.text = faction_name + ":"
		name_label.custom_minimum_size = Vector2(180, 0)
		name_label.add_theme_font_size_override("font_size", 13)
		name_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		impact_row.add_child(name_label)
		
		var effect_label = Label.new()
		effect_label.text = impact_text
		effect_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		effect_label.add_theme_font_size_override("font_size", 13)
		var effect_color = COLOR_GREEN if impact_severity == "positive" else COLOR_RED if impact_severity == "negative" else Color(COLOR_TEXT)
		effect_label.add_theme_color_override("font_color", effect_color)
		impact_row.add_child(effect_label)
		
		reputation_impact_container.add_child(impact_row)

# ══════════════════════════════════════════════════════════════════════════════
# POLITICAL ACTIONS
# ══════════════════════════════════════════════════════════════════════════════

func _update_political_actions(actions_available: Dictionary):
	_clear_container(political_actions_container)
	
	var info_label = Label.new()
	info_label.text = tr("Bruk politiske handlinger for å påvirke interessenter og forbedre relasjoner.")
	info_label.add_theme_font_size_override("font_size", 12)
	info_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	political_actions_container.add_child(info_label)
	
	# Lobby action
	var lobby_section = VBoxContainer.new()
	lobby_section.add_theme_constant_override("separation", 4)
	
	var lobby_header = Label.new()
	lobby_header.text = tr("🎤 Lobbying")
	lobby_header.add_theme_font_size_override("font_size", 14)
	lobby_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	lobby_section.add_child(lobby_header)
	
	var lobby_desc = Label.new()
	lobby_desc.text = tr("Påvirk politikere og reguleringsmyndigheter til fordel for din bedrift.")
	lobby_desc.add_theme_font_size_override("font_size", 12)
	lobby_desc.add_theme_color_override("font_color", Color(COLOR_MUTED))
	lobby_section.add_child(lobby_desc)
	
	var lobby_buttons = HBoxContainer.new()
	lobby_buttons.add_theme_constant_override("separation", 8)
	
	for faction in STAKEHOLDER_FACTIONS:
		if faction["id"] in ["regulators", "industry_associations"]:
			var lobby_btn = Button.new()
			lobby_btn.text = "%s %s" % [faction["icon"], faction["name"]]
			lobby_btn.tooltip_text = "Lobby hos %s" % faction["name"]
			lobby_btn.pressed.connect(_on_lobby_pressed.bind(faction["id"]))
			lobby_buttons.add_child(lobby_btn)
	
	lobby_section.add_child(lobby_buttons)
	political_actions_container.add_child(lobby_section)
	
	var separator1 = HSeparator.new()
	political_actions_container.add_child(separator1)
	
	# Donate action
	var donate_section = VBoxContainer.new()
	donate_section.add_theme_constant_override("separation", 4)
	
	var donate_header = Label.new()
	donate_header.text = tr("💰 Donasjoner & Sponsing")
	donate_header.add_theme_font_size_override("font_size", 14)
	donate_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	donate_section.add_child(donate_header)
	
	var donate_desc = Label.new()
	donate_desc.text = tr("Doner til lokalsamfunn eller miljøorganisasjoner for å bygge goodwill.")
	donate_desc.add_theme_font_size_override("font_size", 12)
	donate_desc.add_theme_color_override("font_color", Color(COLOR_MUTED))
	donate_section.add_child(donate_desc)
	
	var donate_buttons = HBoxContainer.new()
	donate_buttons.add_theme_constant_override("separation", 8)
	
	for faction in STAKEHOLDER_FACTIONS:
		if faction["id"] in ["environmental_ngos", "local_communities"]:
			var donate_btn = Button.new()
			donate_btn.text = "%s %s" % [faction["icon"], faction["name"]]
			donate_btn.tooltip_text = "Doner til %s" % faction["name"]
			donate_btn.pressed.connect(_on_donate_pressed.bind(faction["id"]))
			donate_buttons.add_child(donate_btn)
	
	donate_section.add_child(donate_buttons)
	political_actions_container.add_child(donate_section)
	
	var separator2 = HSeparator.new()
	political_actions_container.add_child(separator2)
	
	# Media campaigns
	var media_section = VBoxContainer.new()
	media_section.add_theme_constant_override("separation", 4)
	
	var media_header = Label.new()
	media_header.text = tr("📣 Media Kampanjer")
	media_header.add_theme_font_size_override("font_size", 14)
	media_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	media_section.add_child(media_header)
	
	var media_desc = Label.new()
	media_desc.text = tr("Kjør PR-kampanjer for å forbedre din offentlige image.")
	media_desc.add_theme_font_size_override("font_size", 12)
	media_desc.add_theme_color_override("font_color", Color(COLOR_MUTED))
	media_section.add_child(media_desc)
	
	var media_btn = Button.new()
	media_btn.text = tr("📰 Start Media Kampanje")
	media_btn.tooltip_text = "Forbedre din relasjon med media og offentligheten"
	media_btn.pressed.connect(_on_lobby_pressed.bind("media"))
	media_section.add_child(media_btn)
	
	political_actions_container.add_child(media_section)

func _on_lobby_pressed(faction_id: String):
	print("StakeholderPanel: Lobby action for faction: %s" % faction_id)
	emit_signal("lobby_action_pressed", faction_id)

func _on_donate_pressed(faction_id: String):
	print("StakeholderPanel: Donate action for faction: %s" % faction_id)
	emit_signal("donate_action_pressed", faction_id)

# ══════════════════════════════════════════════════════════════════════════════
# SAFE DATA GETTERS (with fallback placeholders)
# ══════════════════════════════════════════════════════════════════════════════

func _get_faction_relationships_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_stakeholder_relationships"):
		return sim_bridge.get_stakeholder_relationships()
	else:
		# Placeholder relationship scores
		return {
			"regulators": randf_range(40.0, 70.0),
			"environmental_ngos": randf_range(30.0, 60.0),
			"local_communities": randf_range(50.0, 80.0),
			"workers_unions": randf_range(60.0, 85.0),
			"industry_associations": randf_range(55.0, 75.0),
			"media": randf_range(45.0, 65.0)
		}

func _get_active_demands_safe(sim_bridge: Node) -> Array[Dictionary]:
	if sim_bridge.has_method("get_stakeholder_demands"):
		var raw = sim_bridge.get_stakeholder_demands()
		var result: Array[Dictionary] = []
		for entry in raw:
			result.append(entry)
		return result
	else:
		# Placeholder demands
		var demands: Array[Dictionary] = []
		
		# Random chance of active demands
		if randf() > 0.6:
			demands.append({
				"id": "demand_001",
				"faction_id": "environmental_ngos",
				"title": "Reduser lakselus til < 0.5 per fisk",
				"description": "Miljøorganisasjoner krever strengere luskekontroll.",
				"deadline_days": 14,
				"consequence": "Omdømme -15, Media oppmerksomhet"
			})
		
		if randf() > 0.7:
			demands.append({
				"id": "demand_002",
				"faction_id": "workers_unions",
				"title": "Forbedre lønnsvilkår for anleggsarbeidere",
				"description": "Fagforening krever 8% lønnsøkning.",
				"deadline_days": 30,
				"consequence": "Streik, produksjonsstans i 7 dager"
			})
		
		if randf() > 0.8:
			demands.append({
				"id": "demand_003",
				"faction_id": "regulators",
				"title": "Oppgrader til nye renseanlegg",
				"description": "Nye regulatoriske krav for vannkvalitet.",
				"deadline_days": 60,
				"consequence": "Bøter 2.5M NOK, risiko for tilbaketrekning av lisenser"
			})
		
		return demands

func _get_reputation_impacts_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_stakeholder_impacts"):
		return sim_bridge.get_stakeholder_impacts()
	else:
		# Placeholder impacts based on relationship scores
		var relationships = _get_faction_relationships_safe(sim_bridge)
		
		var impacts = {}
		
		var regulators_score = relationships.get("regulators", 50)
		if regulators_score >= 70:
			impacts["regulators"] = {"effect": "Raskere lisensgodkjenning (-30% saksbehandlingstid)", "severity": "positive"}
		elif regulators_score < 40:
			impacts["regulators"] = {"effect": "Økt inspeksjonsfrekvens (+50% kontroller)", "severity": "negative"}
		else:
			impacts["regulators"] = {"effect": "Normal lisensprosess", "severity": "neutral"}
		
		var env_ngos_score = relationships.get("environmental_ngos", 50)
		if env_ngos_score >= 70:
			impacts["environmental_ngos"] = {"effect": "Positiv mediedekning, premiumprising +5%", "severity": "positive"}
		elif env_ngos_score < 40:
			impacts["environmental_ngos"] = {"effect": "Negativ mediedekning, boikott risiko", "severity": "negative"}
		else:
			impacts["environmental_ngos"] = {"effect": "Nøytral miljøprofil", "severity": "neutral"}
		
		var local_score = relationships.get("local_communities", 50)
		if local_score >= 70:
			impacts["local_communities"] = {"effect": "Sterk lokal støtte, lavere arbeidskonflikt", "severity": "positive"}
		elif local_score < 40:
			impacts["local_communities"] = {"effect": "Lokal motstand, forsinkede utvidelser", "severity": "negative"}
		else:
			impacts["local_communities"] = {"effect": "Nøytral relasjon", "severity": "neutral"}
		
		var unions_score = relationships.get("workers_unions", 50)
		if unions_score >= 70:
			impacts["workers_unions"] = {"effect": "Høy arbeidsmoral, +10% produktivitet", "severity": "positive"}
		elif unions_score < 40:
			impacts["workers_unions"] = {"effect": "Risiko for streik, lav produktivitet", "severity": "negative"}
		else:
			impacts["workers_unions"] = {"effect": "Standard arbeidsforhold", "severity": "neutral"}
		
		var industry_score = relationships.get("industry_associations", 50)
		if industry_score >= 70:
			impacts["industry_associations"] = {"effect": "Tilgang til bransjedata, samarbeidsrabatter", "severity": "positive"}
		elif industry_score < 40:
			impacts["industry_associations"] = {"effect": "Isolert fra bransjenettverk", "severity": "negative"}
		else:
			impacts["industry_associations"] = {"effect": "Normal bransjerelasjon", "severity": "neutral"}
		
		var media_score = relationships.get("media", 50)
		if media_score >= 70:
			impacts["media"] = {"effect": "Positiv PR, økt merkevare score +8%", "severity": "positive"}
		elif media_score < 40:
			impacts["media"] = {"effect": "Negativ PR, merkevare skade -12%", "severity": "negative"}
		else:
			impacts["media"] = {"effect": "Balansert mediedekning", "severity": "neutral"}
		
		return impacts

func _get_political_actions_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_available_political_actions"):
		return sim_bridge.get_available_political_actions()
	else:
		# Placeholder: all actions available
		return {
			"lobby_available": true,
			"donate_available": true,
			"media_campaign_available": true
		}

# ══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ══════════════════════════════════════════════════════════════════════════════

func _clear_container(container: VBoxContainer):
	for child in container.get_children():
		child.queue_free()
