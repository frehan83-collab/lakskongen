## OffshorePanel v2.2 — Nordic Minimalist (scene-based).
## Offshore farm management UI
## Layout lives in scenes/ui/OffshorePanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Farm rows are data-driven and still populated in code.

extends PanelContainer
class_name OffshorePanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var offshore_header_label: Label = %OffshoreTitle
@onready var _farm_section_title: Label = %FarmHeader
@onready var farm_list_container: VBoxContainer = %FarmList
@onready var weather_waves_label: Label = %WeatherWavesLabel
@onready var crew_rotation_label: Label = %CrewRotationLabel
@onready var build_new_offshore_button: Button = %BtnBuildOffshore
@onready var contain_list: VBoxContainer = %ContainList
@onready var contain_title: Label = %ContainTitle
# Phase 5.3: Sludge & Transfers
@onready var sludge_list: VBoxContainer = %SludgeList
@onready var sludge_title: Label = %SludgeTitle
@onready var transfer_section: VBoxContainer = %TransferSection
@onready var transfer_title: Label = %TransferTitle
@onready var transfer_source_select: OptionButton = %TransferSourceSelect
@onready var transfer_dest_select: OptionButton = %TransferDestSelect
@onready var transfer_fish_count: SpinBox = %TransferFishCount
@onready var transfer_mean_weight: SpinBox = %TransferMeanWeight
@onready var transfer_reason_select: OptionButton = %TransferReasonSelect
@onready var transfer_plan_btn: Button = %TransferPlanBtn
@onready var transfer_execute_btn: Button = %TransferExecuteBtn
@onready var transfer_info_label: Label = %TransferInfoLabel

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _theme: Node = null
var _sim: Node = null
var _pending_transfer: Dictionary = {}

func _ready():
	_theme = get_node_or_null("/root/LakskongenTheme")
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("OffshorePanel: SimBridge not found — showing fallback data")

	_apply_localization()
	_apply_theme_fonts()
	build_new_offshore_button.pressed.connect(_on_build_new_offshore_pressed)
	transfer_plan_btn.pressed.connect(_on_transfer_plan)
	transfer_execute_btn.pressed.connect(_on_transfer_execute)
	transfer_source_select.item_selected.connect(_on_transfer_source_changed)

	refresh()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	offshore_header_label.text = tr("UI_OFFSHORE_TITLE")
	_farm_section_title.text = tr("UI_OFFSHORE_FARMS")
	build_new_offshore_button.text = tr("UI_OFFSHORE_BUILD")
	contain_title.text = tr("UI_CONTAINMENT_LADDER")
	sludge_title.text = tr("UI_SLUDGE_VALORIZATION")
	transfer_title.text = tr("UI_COHORT_TRANSFERS")
	transfer_plan_btn.text = tr("UI_TRANSFER_PLAN")
	transfer_execute_btn.text = tr("UI_TRANSFER_EXECUTE")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		offshore_header_label.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_farm_section_title.add_theme_font_override("font", _theme.font_heading_semibold)
		contain_title.add_theme_font_override("font", _theme.font_heading_semibold)
		sludge_title.add_theme_font_override("font", _theme.font_heading_semibold)
		transfer_title.add_theme_font_override("font", _theme.font_heading_semibold)

func refresh():
	# Refresh UI with live data from SimBridge (fallback to placeholders offline)
	_update_farm_list()
	_update_weather_waves()
	_update_crew_rotation()
	_update_containment_ladder()
	_update_sludge_status()
	_update_transfer_ui()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

func on_panel_shown() -> void:
	refresh()

func _on_state_updated() -> void:
	if visible:
		refresh()

func _get_farms() -> Array:
	if _sim != null and _sim.has_method("get_offshore_farms"):
		return _sim.get_offshore_farms()
	return [
		{"id": "offshore_001", "name": "Offshore Anlegg 1", "operational": true, "wave_height": 1.2, "crew_days_since_rotation": 12, "storm_lockdown": false},
		{"id": "offshore_002", "name": "Offshore Anlegg 2", "operational": true, "wave_height": 1.8, "crew_days_since_rotation": 8, "storm_lockdown": false},
	]

func _update_farm_list():
	if farm_list_container == null:
		return
	# Clear existing list items
	for child in farm_list_container.get_children():
		child.queue_free()

	var farms: Array = _get_farms()
	if farms.is_empty():
		var empty_label = Label.new()
		empty_label.text = tr("UI_OFFSHORE_EMPTY")
		if _theme:
			empty_label.add_theme_font_size_override("font_size", _theme.FONT_SMALL)
			empty_label.add_theme_color_override("font_color", _theme.TEXT_SECONDARY)
		farm_list_container.add_child(empty_label)
		return

	for farm in farms:
		var farm_label = Label.new()
		var status := "Aktiv" if farm.get("operational", true) else "Inaktiv"
		var lockdown := " (stormsikret)" if farm.get("storm_lockdown", false) else ""
		farm_label.text = "%s (%s)%s" % [farm.get("name", "Offshore-anlegg"), status, lockdown]
		if _theme:
			farm_label.add_theme_font_size_override("font_size", _theme.FONT_SMALL)
			farm_label.add_theme_color_override("font_color", _theme.TEXT_SECONDARY)
		farm_list_container.add_child(farm_label)

func _update_weather_waves():
	if weather_waves_label == null:
		return
	var farms: Array = _get_farms()
	if farms.is_empty():
		weather_waves_label.text = "Vær & Bølger: Ingen anlegg"
		return
	# Live wave height from SimState (per-farm sensors arrive with ROC).
	var live_wave := -1.0
	if _sim and "wave_m" in _sim:
		live_wave = float(_sim.wave_m)
	var max_wave := 0.0
	for farm in farms:
		max_wave = max(max_wave, float(farm.get("wave_height", 0.0)))
	if live_wave >= 0.0:
		max_wave = max(max_wave, live_wave)
	weather_waves_label.text = "Vær & Bølger: %.1f m bølger" % max_wave

func _update_crew_rotation():
	if crew_rotation_label == null:
		return
	var farms: Array = _get_farms()
	if farms.is_empty():
		crew_rotation_label.text = "Mannskapsrotasjon: Ingen anlegg"
		return
	var max_days := 0
	for farm in farms:
		max_days = maxi(max_days, int(farm.get("crew_days_since_rotation", 0)))
	crew_rotation_label.text = "Mannskapsrotasjon: Lengste turnus %d dager" % max_days

func _on_build_new_offshore_pressed():
	if _sim != null and _sim.has_method("build_offshore_farm"):
		_sim.build_offshore_farm("Nytt offshore-anlegg", "semi_submersible")
	refresh()

## Containment ladder per sea site (Phase 4.1): current rung + next-rung button.
const CONTAIN_ORDER := ["OpenNet", "Skirt", "SemiClosed", "ClosedFloat", "Submersible", "OffshoreClosed"]

func _update_containment_ladder() -> void:
	if contain_list == null:
		return
	for child in contain_list.get_children():
		child.queue_free()
	if _sim == null or not _sim.has_method("get_all_pens"):
		return
	var pens: Array = _sim.get_all_pens()
	var levels: Array = _sim.get_containment_levels() if _sim.has_method("get_containment_levels") else []
	var by_id := {}
	for lv in levels:
		by_id[str(lv.get("id", ""))] = lv
	if pens.is_empty():
		UIPolish.show_empty_state(contain_list, "Ingen kystlokaliteter", 13, "", _theme)
		return
	for site in pens:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		contain_list.add_child(row)
		var cur := str(site.get("containment", "OpenNet"))
		var name_lbl := Label.new()
		name_lbl.text = "%s · %s" % [str(site.get("name", "?")), cur]
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 13)
		row.add_child(name_lbl)
		var idx := CONTAIN_ORDER.find(cur)
		if idx < 0 or idx >= CONTAIN_ORDER.size() - 1:
			var max_lbl := Label.new()
			max_lbl.text = "MAX"
			row.add_child(max_lbl)
		else:
			var next_id: String = CONTAIN_ORDER[idx + 1]
			var info: Dictionary = by_id.get(next_id, {})
			var btn := Button.new()
			var capex: float = float(info.get("capex_nok", 0.0))
			btn.text = "→ %s (%.1fM)" % [next_id, capex / 1_000_000.0]
			btn.tooltip_text = "Lus-blokkering %.0f%% · Storm ×%.1f · Rømning ×%.1f · Vekst ×%.2f · OPEX %.0fk/uke" % [
				float(info.get("lice_exclusion", 0.0)) * 100.0,
				float(info.get("storm_mult", 1.0)),
				float(info.get("escape_mult", 1.0)),
				float(info.get("growth_mult", 1.0)),
				float(info.get("weekly_opex_nok", 0.0)) / 1000.0]
			var sid := str(site.get("id", ""))
			btn.pressed.connect(_on_upgrade_containment.bind(sid, next_id))
			row.add_child(btn)
		_add_barrier_controls(row, site)

func _on_upgrade_containment(site_id: String, level_id: String) -> void:
	if _sim and _sim.has_method("upgrade_containment"):
		_sim.upgrade_containment(site_id, level_id)
	refresh()

## P8: Smoltscreen permeable-barrier controls for one site row. Guarded — the
## controls only appear when GameBridge exposes the barrier API, and show the
## flow-dependent effectiveness reported by the engine.
func _add_barrier_controls(row: HBoxContainer, site: Dictionary) -> void:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge == null or not bridge.has_method("InstallBarrier"):
		return
	var sid := str(site.get("id", ""))
	if sid.is_empty():
		return
	var installed := bool(site.get("has_barrier", false))
	var lice := float(site.get("barrier_lice_blocking", 0.0))
	var jelly := float(site.get("barrier_jellyfish_blocking", 0.0))
	var days := int(site.get("barrier_days_since_cleaning", 0))
	var btn := Button.new()
	btn.text = "🔛 Smoltscreen (fjern)" if installed else "→ Smoltscreen (6.0M)"
	btn.tooltip_text = ("Permeabel returliner (150+200 µm) — lus −%.0f%%, manet −%.0f%% ved dagens strøm. "
		+ "Ikke kvalifisert for miljøfleksibilitet: regulatorer leser «tett» som impermeabelt.") % [
		lice * 100.0, jelly * 100.0]
	btn.pressed.connect(_on_install_barrier.bind(sid))
	row.add_child(btn)
	if installed:
		var clean := Button.new()
		clean.text = "🧽"
		clean.tooltip_text = "Rengjør barriere (nullstiller begroing). Sist rengjort for %d dager siden." % days
		clean.pressed.connect(_on_clean_barrier.bind(sid))
		row.add_child(clean)

func _on_install_barrier(site_id: String) -> void:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null and bridge.has_method("InstallBarrier"):
		bridge.InstallBarrier(site_id, "smoltscreen")
	refresh()

func _on_clean_barrier(site_id: String) -> void:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null and bridge.has_method("CleanBarrier"):
		bridge.CleanBarrier(site_id)
	refresh()

# ── Phase 5.3: Sludge Valorization UI ────────────────────────────────────────

func _update_sludge_status() -> void:
	if sludge_list == null:
		return
	for child in sludge_list.get_children():
		child.queue_free()

	if _sim == null or not _sim.has_method("get_sludge_status"):
		return

	var sludge_data: Array = _sim.get_sludge_status()
	if sludge_data.is_empty():
		var empty := Label.new()
		empty.text = tr("UI_SLUDGE_NO_DATA")
		if _theme:
			empty.add_theme_font_size_override("font_size", _theme.FONT_SMALL)
			empty.add_theme_color_override("font_color", _theme.TEXT_SECONDARY)
		sludge_list.add_child(empty)
		return

	for site_data in sludge_data:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		sludge_list.add_child(row)

		var name_lbl := Label.new()
		var acc := float(site_data.get("accumulated_kg_ds", 0.0))
		var val := float(site_data.get("valorized_kg_ds", 0.0))
		var hub := bool(site_data.get("has_hub_connection", false))
		var proj := float(site_data.get("projected_daily_kg_ds", 0.0))
		name_lbl.text = "%s: %.0f kg DS akkumulert (%.0f kg/dag proj.)" % [str(site_data.get("id", "?")), acc, proj]
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 13)
		row.add_child(name_lbl)

		if hub:
			var hub_lbl := Label.new()
			hub_lbl.text = "🔗 Hub"
			hub_lbl.add_theme_color_override("font_color", Color(0.3, 0.8, 0.3))
			hub_lbl.add_theme_font_size_override("font_size", 12)
			row.add_child(hub_lbl)
		else:
			var hub_btn := Button.new()
			hub_btn.text = "Koble til Hub"
			hub_btn.tooltip_text = "Bygg/koble til sentral slam-hub (40M NOK ny, 5M tilkobling)"
			hub_btn.pressed.connect(_on_toggle_sludge_hub.bind(str(site_data.get("id", ""))))
			row.add_child(hub_btn)

		if acc >= 100.0:
			var val_btn := Button.new()
			val_btn.text = "Valorisér nå (%.0f kg DS)" % acc
			val_btn.tooltip_text = "Manuell slam-valorisering: biogas + gjødsel - OPEX"
			val_btn.pressed.connect(_on_valorize_sludge.bind(str(site_data.get("id", ""))))
			row.add_child(val_btn)

# ── Phase 5.3: Cohort Transfers UI ───────────────────────────────────────────

func _update_transfer_ui() -> void:
	if transfer_source_select == null:
		return

	# Preserve current selection by site id (stored as item metadata).
	var current_source := ""
	if transfer_source_select.item_count > 0 and transfer_source_select.get_selected() >= 0:
		current_source = str(transfer_source_select.get_item_metadata(transfer_source_select.get_selected()))

	transfer_source_select.clear()
	transfer_dest_select.clear()

	if _sim == null or not _sim.has_method("get_all_pens"):
		return

	var pens: Array = _sim.get_all_pens()
	for site in pens:
		var biomass := float(site.get("biomass_kg", 0.0))
		if biomass > 0:
			var idx := transfer_source_select.item_count
			transfer_source_select.add_item("%s (%.0f t)" % [str(site.get("name", "?")), biomass / 1000.0])
			transfer_source_select.set_item_metadata(idx, str(site.get("id", "")))

	if current_source != "":
		for i in range(transfer_source_select.item_count):
			if str(transfer_source_select.get_item_metadata(i)) == current_source:
				transfer_source_select.select(i)
				break

	_on_transfer_source_changed(transfer_source_select.get_selected())

## OptionButton item metadata holds the site id (Godot item ids are ints only).
func _item_site_id(opt: OptionButton, idx: int) -> String:
	if opt == null or idx < 0 or idx >= opt.item_count:
		return ""
	return str(opt.get_item_metadata(idx))

func _on_transfer_source_changed(selected_idx: int) -> void:
	if transfer_dest_select == null or _sim == null or not _sim.has_method("get_transfer_destinations"):
		return

	var source_id := _item_site_id(transfer_source_select, selected_idx)
	if source_id == "":
		return

	# Get source biomass for destination filtering
	var source_biomass := 0.0
	var pens: Array = _sim.get_all_pens()
	for site in pens:
		if str(site.get("id", "")) == source_id:
			source_biomass = float(site.get("biomass_kg", 0.0))
			break

	transfer_dest_select.clear()
	var dests: Array = _sim.get_transfer_destinations(source_id, source_biomass)
	for dest in dests:
		var avail := float(dest.get("available_mab_kg", 0.0))
		var dest_idx := transfer_dest_select.item_count
		transfer_dest_select.add_item("%s (MAB ledig: %.0f t)" % [str(dest.get("name", "?")), avail / 1000.0])
		transfer_dest_select.set_item_metadata(dest_idx, str(dest.get("id", "")))

	# Populate reasons
	transfer_reason_select.clear()
	var reasons := ["Grading", "Fallowing", "MabManagement", "DiseaseIsolation", "OffshoreStocking", "ContainmentUpgrade"]
	for r in reasons:
		transfer_reason_select.add_item(r)

	# Reset plan
	_pending_transfer = {}
	transfer_info_label.text = ""
	transfer_execute_btn.disabled = true

func _on_transfer_plan() -> void:
	if _sim == null or not _sim.has_method("plan_cohort_transfer"):
		return

	var source_id := _item_site_id(transfer_source_select, transfer_source_select.get_selected())
	var dest_id := _item_site_id(transfer_dest_select, transfer_dest_select.get_selected())
	var fish_count := int(transfer_fish_count.value)
	var mean_weight := transfer_mean_weight.value
	var reason := transfer_reason_select.get_item_text(transfer_reason_select.get_selected())

	if source_id == "" or dest_id == "" or fish_count <= 0 or mean_weight <= 0:
		transfer_info_label.text = tr("UI_TRANSFER_INVALID_INPUT")
		transfer_info_label.add_theme_color_override("font_color", Color(1, 0.3, 0.3))
		return

	var plan: Dictionary = _sim.plan_cohort_transfer(source_id, dest_id, fish_count, mean_weight, reason)
	if plan.has("error"):
		transfer_info_label.text = "Feil: %s" % plan.get("error", "Ukjent")
		transfer_info_label.add_theme_color_override("font_color", Color(1, 0.3, 0.3))
		transfer_execute_btn.disabled = true
		return

	_pending_transfer = plan
	var cost := float(plan.get("estimated_cost_nok", 0.0))
	var mort := float(plan.get("estimated_mortality_pct", 0.0))
	var eta := int(plan.get("eta_days", 3))
	transfer_info_label.text = "Plan: %d fisk · %.1f kg · %s · %.1f%% tap · %d dager · %.1fM NOK" % [
		fish_count, mean_weight, reason, mort, eta, cost / 1_000_000.0]
	transfer_info_label.add_theme_color_override("font_color", _theme.TEXT_PRIMARY if _theme else Color(1,1,1))
	transfer_execute_btn.disabled = false

func _on_transfer_execute() -> void:
	if _sim == null or not _sim.has_method("execute_cohort_transfer") or _pending_transfer.is_empty():
		return

	var source_id := _item_site_id(transfer_source_select, transfer_source_select.get_selected())
	var dest_id := _item_site_id(transfer_dest_select, transfer_dest_select.get_selected())
	var fish_count := int(transfer_fish_count.value)
	var mean_weight := transfer_mean_weight.value
	var reason := transfer_reason_select.get_item_text(transfer_reason_select.get_selected())

	var result: Dictionary = _sim.execute_cohort_transfer(source_id, dest_id, fish_count, mean_weight, reason)
	var success := bool(result.get("success", false))
	var moved := int(result.get("fish_transferred", 0))
	var lost := int(result.get("fish_lost", 0))
	var msg := str(result.get("message", ""))

	if success:
		transfer_info_label.text = "✅ %s (%d overført, %d tap)" % [msg, moved, lost]
		transfer_info_label.add_theme_color_override("font_color", Color(0.3, 0.8, 0.3))
	else:
		transfer_info_label.text = "❌ %s" % msg
		transfer_info_label.add_theme_color_override("font_color", Color(1, 0.3, 0.3))

	transfer_execute_btn.disabled = true
	_pending_transfer = {}
	refresh()

func _on_toggle_sludge_hub(site_id: String) -> void:
	if _sim and _sim.has_method("toggle_sludge_hub"):
		_sim.toggle_sludge_hub(site_id)
	refresh()

func _on_valorize_sludge(site_id: String) -> void:
	if _sim and _sim.has_method("valorize_sludge_now"):
		var rev: float = float(_sim.valorize_sludge_now(site_id))
		var msg := ("Valorisert: %.0f NOK" % rev) if rev != 0.0 else "Ingen slam å valorisere"
		transfer_info_label.text = msg
		transfer_info_label.add_theme_color_override("font_color", Color(0.3, 0.8, 0.3) if rev > 0 else Color(1, 0.6, 0.2))
	refresh()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
