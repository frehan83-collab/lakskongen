class_name RegionExpansionSection
extends VBoxContainer

## The region view's expansion section (roadmap U2-01): one production area's
## capacity and its zones, each with the engine's build and licence verdicts.
## Every refusal is rendered as text (EXPANSION_REASON_*), next to the button
## it disables. Built from SimBridge.get_expansion_overview(); nothing here
## decides a rule, it only shows the engine's verdicts and asks for the verbs.

signal build_requested(zone_id: String)
signal licence_requested(zone_id: String)

## The area's traffic light -> theme tone and label key.
const LIGHT_TONES := {"green": &"Ok", "yellow": &"Warn", "red": &"Crit"}
const LIGHT_KEYS := {"green": "EXPANSION_LIGHT_GREEN", "yellow": "EXPANSION_LIGHT_YELLOW", "red": "EXPANSION_LIGHT_RED"}

var _area: Dictionary = {}
var _overview: Dictionary = {}


func _ready() -> void:
	add_theme_constant_override("separation", LakskongenTheme.SPACE_SM + 2)
	size_flags_horizontal = Control.SIZE_EXPAND_FILL


## Show one area of the overview (a row of `overview.areas`).
func show_area(area: Dictionary, overview: Dictionary) -> void:
	_area = area
	_overview = overview
	_rebuild()


## The area shown (empty before the first show_area).
func area() -> Dictionary:
	return _area


## The zone card of a zone (tests and the tutorial highlight find it by name).
func zone_card(zone_id: String) -> Control:
	return find_child("Zone_" + zone_id, true, false) as Control


func _rebuild() -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	if _area.is_empty():
		return
	add_child(_header_row())
	add_child(_capacity_card())
	add_child(_overline("EXPANSION_ZONES_HEADER", "ZonesHeader"))
	for zone in _area.get("zones", []):
		add_child(_zone_card(zone as Dictionary))


# ── Header: traffic light and disease zone ─────────────────────────────────────

func _header_row() -> Control:
	var row := HFlowContainer.new()
	row.name = "AreaChips"
	row.add_theme_constant_override("h_separation", LakskongenTheme.SPACE_SM)
	row.add_theme_constant_override("v_separation", LakskongenTheme.SPACE_XS)
	var light := str(_area.get("traffic_light", "yellow"))
	var chip := _tag(tr(LIGHT_KEYS.get(light, "EXPANSION_LIGHT_YELLOW")), LIGHT_TONES.get(light, &"Warn"))
	chip.name = "TrafficLight"
	chip.tooltip_text = tr("EXPANSION_LIGHT_TIP")
	chip.mouse_filter = Control.MOUSE_FILTER_STOP
	row.add_child(chip)
	var pd := _tag(tr("EXPANSION_PD_ZONE" if bool(_area.get("in_pd_zone", false)) else "EXPANSION_SURVEILLANCE_ZONE"), &"Info")
	pd.name = "DiseaseZone"
	row.add_child(pd)
	return row


# ── The company's capacity in the area ─────────────────────────────────────────

func _capacity_card() -> Control:
	var card := PanelContainer.new()
	card.name = "CapacityCard"
	card.theme_type_variation = &"Tile"
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	card.add_child(col)
	col.add_child(_overline("EXPANSION_CAPACITY_HEADER", "CapacityHeader"))
	var licences := int(_area.get("licences", 0))
	var size_t := float(_area.get("licence_size_t", 780.0))
	if licences == 0:
		var none := _label(tr("EXPANSION_CAPACITY_NONE") % SimFormatters.num(size_t), &"BodySmall")
		none.name = "CapacityLine"
		col.add_child(none)
		return card
	var mtb := float(_area.get("licence_mtb_t", 0.0))
	var standing := float(_area.get("standing_t", 0.0))
	var line := _label(tr("EXPANSION_CAPACITY_LICENCES") % [licences, SimFormatters.num(mtb)], &"Figure")
	line.name = "CapacityLine"
	col.add_child(line)
	var bar := ProgressBar.new()
	bar.name = "CapacityBar"
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, 8)
	bar.max_value = maxf(mtb, 1.0)
	bar.value = clampf(standing, 0.0, bar.max_value)
	bar.tooltip_text = tr("EXPANSION_CAPACITY_BAR_TIP")
	bar.mouse_filter = Control.MOUSE_FILTER_STOP
	col.add_child(bar)
	var used := _label(tr("EXPANSION_CAPACITY_STANDING") % [SimFormatters.num(standing), SimFormatters.num(float(_area.get("headroom_t", 0.0)))], &"SmallMuted")
	used.name = "CapacityStanding"
	col.add_child(used)
	return card


# ── One zone with its verdicts ────────────────────────────────────────────────

func _zone_card(zone: Dictionary) -> Control:
	var zone_id := str(zone.get("zone_id", ""))
	var card := PanelContainer.new()
	card.name = "Zone_" + zone_id
	card.theme_type_variation = &"CardFlat"
	var col := VBoxContainer.new()
	col.name = "Body"
	col.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS + 2)
	card.add_child(col)

	var title := _label(SimLabels.zone(zone_id), &"Subheading")
	title.name = "ZoneTitle"
	col.add_child(title)

	var chips := HFlowContainer.new()
	chips.add_theme_constant_override("h_separation", LakskongenTheme.SPACE_XS + 2)
	chips.add_theme_constant_override("v_separation", LakskongenTheme.SPACE_XS)
	var sites := int(zone.get("player_sites", 0))
	if sites > 0:
		var key := "EXPANSION_ZONE_YOURS_ONE" if sites == 1 else "EXPANSION_ZONE_YOURS_MANY"
		chips.add_child(_tag(tr(key) % sites, &"Info"))
	else:
		chips.add_child(_tag(tr("EXPANSION_ZONE_NEW"), &""))
	if bool(zone.get("holds_licence", false)):
		chips.add_child(_tag(tr("EXPANSION_ZONE_LICENCE_WON"), &"Ok"))
	if str(zone.get("open_auction_id", "")) != "":
		chips.add_child(_tag(tr("EXPANSION_ZONE_AUCTION_RUNNING"), &"Warn"))
	col.add_child(chips)

	col.add_child(_verdict_block(zone, "build"))
	col.add_child(_verdict_block(zone, "apply"))
	return card


## The build or licence verdict: an overline, the verdict as text (the reason
## when refused) and the button that opens the dialog, disabled with the same
## reason as its tooltip.
func _verdict_block(zone: Dictionary, which: String) -> Control:
	var zone_id := str(zone.get("zone_id", ""))
	var block := VBoxContainer.new()
	block.name = "Build" if which == "build" else "Licence"
	block.add_theme_constant_override("separation", 2)
	block.add_child(_overline("EXPANSION_BUILD_HEADER" if which == "build" else "EXPANSION_LICENCE_HEADER"))

	var allowed := bool(zone.get("can_build" if which == "build" else "can_apply", false))
	var label_key := str(zone.get("build_label_key" if which == "build" else "apply_label_key", ""))
	var auction_running := str(zone.get("open_auction_id", "")) != ""
	var text := ""
	var tone := &""
	if which == "build":
		var cost := float(_overview.get("build_cost_nok", 0.0))
		text = tr("EXPANSION_BUILD_OK") % SimFormatters.format_nok(cost) if allowed else tr(label_key)
		if str(zone.get("build_reason_key", "")) == "insufficient_funds":
			# "Ikke nok penger: dette koster 6,00 MNOK, og du har 4,00 MNOK."
			text = UIPolish.funds_blocker(cost, float(_overview.get("balance_nok", 0.0)))
		tone = &"Ok" if allowed else &"Warn"
	elif auction_running:
		text = tr("EXPANSION_LICENCE_RUNNING")
		tone = &"Info"
	else:
		text = tr("EXPANSION_LICENCE_OK") % [int(_overview.get("auction_length_days", 7)), SimFormatters.format_nok(float(_overview.get("licence_reserve_nok", 0.0)))] if allowed else tr(label_key)
		tone = &"Ok" if allowed else &"Muted"
	var verdict := _label(text, StringName("BodySmall" + String(tone)))
	verdict.name = "Reason"
	verdict.set_meta(&"label_key", "" if allowed else label_key)
	block.add_child(verdict)

	var btn := Button.new()
	btn.name = "BuildBtn" if which == "build" else "LicenceBtn"
	btn.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	btn.custom_minimum_size = Vector2(0, 36)
	if which == "build":
		btn.text = tr("EXPANSION_BUILD_BUTTON")
		btn.theme_type_variation = &"SmallPrimaryButton" if allowed else &"SmallSecondaryButton"
		# The dialog previews what a refused build would need; only the confirm waits.
		btn.pressed.connect(func() -> void: build_requested.emit(zone_id))
		btn.tooltip_text = tr("EXPANSION_BUILD_BUTTON_TIP")
	else:
		btn.text = tr("EXPANSION_AUCTION_BUTTON" if auction_running else "EXPANSION_LICENCE_BUTTON")
		btn.theme_type_variation = &"SmallSecondaryButton"
		btn.pressed.connect(func() -> void: licence_requested.emit(zone_id))
		var open := allowed or auction_running or str(zone.get("apply_reason_key", "")) == "licence_held"
		UIPolish.set_available(btn, "" if open else tr(label_key))
	block.add_child(btn)
	return block


# ── Small builders ─────────────────────────────────────────────────────────────

func _label(text: String, variation: StringName) -> Label:
	var l := Label.new()
	l.text = text
	l.theme_type_variation = variation
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return l


func _overline(key: String, node_name: String = "") -> Label:
	var l := Label.new()
	l.text = tr(key)
	l.theme_type_variation = &"OverlineMuted"
	l.uppercase = true
	if node_name != "":
		l.name = node_name
	return l


func _tag(text: String, tone: StringName) -> Label:
	var l := Label.new()
	l.text = text
	l.theme_type_variation = StringName("Tag" + String(tone))
	return l
