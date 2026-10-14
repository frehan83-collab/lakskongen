## StakeholderPanel v2.1 — Nordic Minimalist (scene-based).
## Stakeholder & Political Relations UI Panel — displays faction relationships, demands,
## reputation impact, and political action options.
## Layout lives in scenes/ui/StakeholderPanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Faction rows, demand cards and reputation rows are data-driven and still populated in code.
## Covers: Regulators, Environmental NGOs, Local Communities, Workers' Unions, Industry Associations, Media.

extends PanelContainer
class_name StakeholderPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _faction_section_title: Label = %FactionHeader
@onready var _demands_section_title: Label = %DemandsHeader
@onready var _reputation_section_title: Label = %ReputationHeader
@onready var _actions_section_title: Label = %ActionsHeader

@onready var faction_overview_container: VBoxContainer = %FactionList
@onready var active_demands_container: VBoxContainer = %DemandsList
@onready var reputation_impact_container: VBoxContainer = %ReputationList

@onready var _actions_info: Label = %ActionsInfo
@onready var _lobby_header: Label = %LobbyHeader
@onready var _lobby_desc: Label = %LobbyDesc
@onready var _donate_header: Label = %DonateHeader
@onready var _donate_desc: Label = %DonateDesc
@onready var _media_header: Label = %MediaHeader
@onready var _media_desc: Label = %MediaDesc

@onready var _btn_lobby_regulators: Button = %BtnLobbyRegulators
@onready var _btn_lobby_industry: Button = %BtnLobbyIndustry
@onready var _btn_donate_env: Button = %BtnDonateEnv
@onready var _btn_donate_local: Button = %BtnDonateLocal
@onready var _btn_media_campaign: Button = %BtnMediaCampaign

# Color scheme (matching existing panels)
const COLOR_GREEN = Color("#2E6B45")
const COLOR_YELLOW = Color("#8A5A00")
const COLOR_RED = Color("#B3172F")
const COLOR_BLUE = Color("#1F5E73")
const COLOR_TEXT = "#1C1E20"
const COLOR_MUTED = "#5A6168"
const COLOR_HIGHLIGHT = "#1F5E73"

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

var _theme: Node = null

func _ready():
	_apply_localization()
	_apply_theme_fonts()

	_btn_lobby_regulators.pressed.connect(_on_lobby_pressed.bind("regulators"))
	_btn_lobby_industry.pressed.connect(_on_lobby_pressed.bind("industry_associations"))
	_btn_donate_env.pressed.connect(_on_donate_pressed.bind("environmental_ngos"))
	_btn_donate_local.pressed.connect(_on_donate_pressed.bind("local_communities"))
	_btn_media_campaign.pressed.connect(_on_lobby_pressed.bind("media"))

	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		if sim_bridge.has_signal("state_updated"):
			sim_bridge.state_updated.connect(_on_state_updated)
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

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_title_label.text = tr("🤝 Interessenter & Politikk")
	_title_label.tooltip_text = "Oversikt over dine relasjoner med interessenter, aktive krav, og politisk innflytelse."
	_faction_section_title.text = tr("👥 Interessenter Oversikt")
	_demands_section_title.text = tr("📋 Aktive Krav")
	_reputation_section_title.text = tr("📊 Omdømmeeffekter")
	_actions_section_title.text = tr("🎯 Politiske Handlinger")
	_actions_info.text = tr("Bruk politiske handlinger for å påvirke interessenter og forbedre relasjoner.")
	_lobby_header.text = tr("🎤 Lobbying")
	_lobby_desc.text = tr("Påvirk politikere og reguleringsmyndigheter til fordel for din bedrift.")
	_donate_header.text = tr("💰 Donasjoner & Sponsing")
	_donate_desc.text = tr("Doner til lokalsamfunn eller miljøorganisasjoner for å bygge goodwill.")
	_media_header.text = tr("📣 Media Kampanjer")
	_media_desc.text = tr("Kjør PR-kampanjer for å forbedre din offentlige image.")
	_btn_media_campaign.text = tr("📰 Start Media Kampanje")
	_btn_media_campaign.tooltip_text = "Forbedre din relasjon med media og offentligheten"
	for faction in STAKEHOLDER_FACTIONS:
		match faction["id"]:
			"regulators":
				_btn_lobby_regulators.text = "%s %s" % [faction["icon"], faction["name"]]
				_btn_lobby_regulators.tooltip_text = "Lobby hos %s" % faction["name"]
			"industry_associations":
				_btn_lobby_industry.text = "%s %s" % [faction["icon"], faction["name"]]
				_btn_lobby_industry.tooltip_text = "Lobby hos %s" % faction["name"]
			"environmental_ngos":
				_btn_donate_env.text = "%s %s" % [faction["icon"], faction["name"]]
				_btn_donate_env.tooltip_text = "Doner til %s" % faction["name"]
			"local_communities":
				_btn_donate_local.text = "%s %s" % [faction["icon"], faction["name"]]
				_btn_donate_local.tooltip_text = "Doner til %s" % faction["name"]

func _apply_theme_fonts() -> void:
	_theme = get_node_or_null("/root/LakskongenTheme")
	if not _theme:
		return
	LakskongenTheme.set_strong(_title_label)
	LakskongenTheme.set_strong(_faction_section_title)
	LakskongenTheme.set_strong(_demands_section_title)
	LakskongenTheme.set_strong(_reputation_section_title)
	LakskongenTheme.set_strong(_actions_section_title)

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

	# Update UI sections
	_update_faction_overview(faction_relationships)
	_update_active_demands(active_demands)
	_update_reputation_impact(reputation_impacts)

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
		LakskongenTheme.set_size(icon_label, 20)
		header_row.add_child(icon_label)

		var name_label = Label.new()
		name_label.text = faction_name
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(name_label, 15)
		LakskongenTheme.set_text_color(name_label, Color(COLOR_TEXT))
		header_row.add_child(name_label)

		var score_label = Label.new()
		score_label.text = "%d / 100" % int(relationship_score)
		LakskongenTheme.set_size(score_label, 14)
		LakskongenTheme.set_text_color(score_label, _relationship_color(relationship_score))
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
		LakskongenTheme.set_size(status_label, 12)
		LakskongenTheme.set_text_color(status_label, _relationship_color(relationship_score))
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
		LakskongenTheme.set_size(no_demands, 13)
		LakskongenTheme.set_text_color(no_demands, COLOR_GREEN)
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
		LakskongenTheme.set_size(icon, 16)
		title_row.add_child(icon)

		var title_label = Label.new()
		title_label.text = title
		title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(title_label, 14)
		LakskongenTheme.set_text_color(title_label, Color(COLOR_TEXT))
		title_row.add_child(title_label)

		var deadline_label = Label.new()
		var deadline_color = COLOR_RED if deadline_days < 7 else COLOR_YELLOW if deadline_days < 30 else COLOR_GREEN
		deadline_label.text = "⏳ %d dager" % deadline_days
		LakskongenTheme.set_size(deadline_label, 12)
		LakskongenTheme.set_text_color(deadline_label, deadline_color)
		deadline_label.tooltip_text = "Frist før konsekvenser aktiveres"
		title_row.add_child(deadline_label)

		demand_vbox.add_child(title_row)

		# Description
		if description != "":
			var desc_label = Label.new()
			desc_label.text = description
			LakskongenTheme.set_size(desc_label, 12)
			LakskongenTheme.set_text_color(desc_label, Color(COLOR_MUTED))
			desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			demand_vbox.add_child(desc_label)

		# Consequence
		var consequence_label = Label.new()
		consequence_label.text = "⚠️ Konsekvens: %s" % consequence
		LakskongenTheme.set_size(consequence_label, 12)
		LakskongenTheme.set_text_color(consequence_label, COLOR_RED)
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
	emit_signal("negotiate_action_pressed", demand_id)

func _on_ignore_demand_pressed(demand_id: String):
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
		LakskongenTheme.set_size(icon_label, 16)
		impact_row.add_child(icon_label)

		var name_label = Label.new()
		name_label.text = faction_name + ":"
		name_label.custom_minimum_size = Vector2(180, 0)
		LakskongenTheme.set_size(name_label, 13)
		LakskongenTheme.set_text_color(name_label, Color(COLOR_MUTED))
		impact_row.add_child(name_label)

		var effect_label = Label.new()
		effect_label.text = impact_text
		effect_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(effect_label, 13)
		var effect_color = COLOR_GREEN if impact_severity == "positive" else COLOR_RED if impact_severity == "negative" else Color(COLOR_TEXT)
		LakskongenTheme.set_text_color(effect_label, effect_color)
		impact_row.add_child(effect_label)

		reputation_impact_container.add_child(impact_row)

# ══════════════════════════════════════════════════════════════════════════════
# POLITICAL ACTIONS (static scene buttons; handlers only)
# ══════════════════════════════════════════════════════════════════════════════

func _on_lobby_pressed(faction_id: String):
	emit_signal("lobby_action_pressed", faction_id)

func _on_donate_pressed(faction_id: String):
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

# ══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ══════════════════════════════════════════════════════════════════════════════

func _clear_container(container: VBoxContainer):
	if container == null:
		return
	for child in container.get_children():
		child.queue_free()
