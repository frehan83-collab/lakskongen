class_name BuildSiteDialog
extends Control

## "Bygg nytt anlegg": the family-first build flow (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.1-6.2; U2-01 built its Sjø step).
##
## 1. Anleggstype: three family cards (Sjø, Land, Hav) with what each pays
##    for capacity with and what limits it, and how many variants are open.
## 2. Variant: the family's variants side by side on the catalogue's aligned
##    bars and pips (median and bad case), each locked card listing every
##    gate, money included, with a progress bar.
## 3. Tomt (Land, Hav): the site lens; choosing a plot ghosts the build onto
##    it (presentation only, the engine is untouched).
## 4. Forhåndsvisning: capex and payments with the p50/p90 overrun band, a
##    36-month cash runway, payback year, NOK/kg HOG, capacity cost with the
##    licence, first harvest; the permit timeline on land; the licence value
##    and the consortium at sea. Confirm files the permit cases (land), starts
##    construction (sea, or land once permitted) or builds the locality (Sjø).
## The diagnosis strip on top names the binding constraint from engine data.
## Every number is the engine's (SimBridge.get_farm_type_catalog,
## preview_farm_build, get_build_plots, get_build_diagnosis, plan_site_build).

signal built(site_id: String, zone_id: String)
signal view_site_requested(site_id: String)
signal tab_requested(tab_id: String)
signal closed

## Catalogue family id -> the card's node suffix (U2-01's names).
const CARD_IDS := {"sjo": "sea", "land": "land", "hav": "offshore"}
## The fjord variant this dialog builds (the closed pen is an upgrade on the pen card).
const SEA_BUILD := "open_pen"
## The entrance: a fade and a rise of this many px (JuiceEffects.panel_enter's).
const ENTER_SECONDS := 0.28
const ENTER_RISE_PX := 14.0

@onready var _dialog: PanelContainer = %Dialog
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _subtitle: Label = %SubtitleLabel
@onready var _close_btn: Button = %CloseBtn
@onready var _diagnosis_host: VBoxContainer = %DiagnosisHost
@onready var _family_header: Label = %FamilyHeader
@onready var _family_row: HBoxContainer = %FamilyRow
@onready var _scroll: ScrollContainer = %StepScroll
@onready var _variant_header: Label = %VariantHeader
@onready var _comparison_host: VBoxContainer = %ComparisonHost
@onready var _option_row: HBoxContainer = %OptionRow
@onready var _plot_header: Label = %PlotHeader
@onready var _plot_host: VBoxContainer = %PlotHost
@onready var _preview_header: Label = %PreviewHeader
@onready var _preview_row: HBoxContainer = %PreviewRow
@onready var _grid_slot: PanelContainer = %GridSlot
@onready var _ledger: GridContainer = %Ledger
@onready var _explain_callout: PanelContainer = %ExplainCallout
@onready var _explain: Label = %ExplainLabel
@onready var _facility_host: VBoxContainer = %FacilityHost
@onready var _verdict: Label = %VerdictLabel
@onready var _result_box: VBoxContainer = %ResultBox
@onready var _result_title: Label = %ResultTitle
@onready var _result_body: Label = %ResultBody
@onready var _hint: Label = %HintLabel
@onready var _cancel_btn: Button = %CancelBtn
@onready var _confirm_btn: Button = %ConfirmBtn

const FamilyCardScene := preload("res://scenes/ui/FarmFamilyCard.tscn")

var _sim: Node = null
var _zone_id := ""
var _plan: Dictionary = {}
var _built_site_id := ""
var _pen_grid: PenGridPreview = null
## Answers whether a tab may be opened (GameUI's tutorial gate); set by the host.
var _tab_probe: Callable = func(_tab: String) -> bool: return true

# ── The flow's state (choices only; every figure is re-read) ─────────────────
var _family := "sjo"
var _variant := SEA_BUILD
var _option := "hfs_pilot_lot"
var _plot := ""
var _contract := "managed"
var _consortium := false
var _mitigations: Array = []
## True while the family cards are the first step (opened without a zone).
var _choosing_family := false
## Land permits filed or a unit contracted by the last confirm ("" none).
var _done_kind := ""
## The entrance's tween (see _enter).
var _slide: Tween = null

var _catalog: Dictionary = {}
var _starts: Dictionary = {}
var _farm_preview: Dictionary = {}
var _diagnosis: DiagnosisStrip = null
var _comparison: ComparisonBars = null
var _plot_lens: PlotLens = null
var _facility: FacilityBuildPreview = null
var _research_names: Dictionary = {}
# Geo B: the Sjø step's places (the area's open-pen candidates) and 'why here'.
var _candidate := ""
var _sea_rows: Array = []
var _candidates: SeaCandidateList = null
var _why: CandidateWhyHere = null


func _ready() -> void:
	hide()
	_sim = get_node_or_null("/root/SimBridge")
	_overline.text = tr("EXPANSION_BUILD_OVERLINE")
	_family_header.text = tr("EXPANSION_FAMILY_HEADER")
	_variant_header.text = tr("BUILD_VARIANT_HEADER")
	_plot_header.text = tr("BUILD_PLOT_HEADER")
	_preview_header.text = tr("EXPANSION_PREVIEW_HEADER")
	_close_btn.tooltip_text = tr("UI_CLOSE")
	_close_btn.pressed.connect(close)
	_cancel_btn.pressed.connect(close)
	_confirm_btn.pressed.connect(_on_confirm)
	_pen_grid = PenGridPreview.new()
	_pen_grid.name = "PenGrid"
	_pen_grid.custom_minimum_size = Vector2(260, 180)
	_grid_slot.add_child(_pen_grid)
	_diagnosis = DiagnosisStrip.new()
	_diagnosis.name = "DiagnosisStrip"
	_diagnosis_host.add_child(_diagnosis)
	_comparison = ComparisonBars.new()
	_comparison.name = "Comparison"
	_comparison_host.add_child(_comparison)
	_plot_lens = PlotLens.new()
	_plot_lens.name = "PlotLens"
	_plot_host.add_child(_plot_lens)
	_plot_lens.plot_chosen.connect(choose_plot)
	var places := HBoxContainer.new()
	places.name = "CandidateRow"
	places.add_theme_constant_override("separation", 16)
	_plot_host.add_child(places)
	_candidates = SeaCandidateList.new()
	_candidates.name = "Candidates"
	places.add_child(_candidates)
	_why = CandidateWhyHere.new()
	_why.name = "WhyHere"
	places.add_child(_why)
	_candidates.candidate_chosen.connect(choose_candidate)
	_candidates.candidate_activated.connect(func(_id: String) -> void: _confirm_btn.grab_focus.call_deferred())
	_facility = FacilityBuildPreview.new()
	_facility.name = "FacilityPreview"
	_facility_host.add_child(_facility)
	_facility.contract_changed.connect(func(c: String) -> void:
		_contract = c
		_refresh())
	_facility.consortium_toggled.connect(func(on: bool) -> void:
		_consortium = on
		_refresh())
	_facility.mitigation_toggled.connect(_on_mitigation)
	get_viewport().size_changed.connect(_fit)
	resized.connect(_fit)


## The host's tutorial gate for tab links.
func set_tab_probe(probe: Callable) -> void:
	_tab_probe = probe


## Open the dialog for a fjord zone: Sjø chosen, the zone's build plan below.
func open_for_zone(zone_id: String) -> void:
	_zone_id = zone_id
	_reset_choices("sjo")
	_choosing_family = false
	_open()


## Geo B: open the dialog on Sjø at an open-pen candidate (the map's site
## card). The place asked for is the place shown and built: with no zone, or a
## zone in another production area, the build uses the place's own build
## zone; a held or unknown place stays chosen and the verdict says why it
## cannot be built (captain's review of track B, finding 1).
func open_for_candidate(zone_id: String, candidate_id: String) -> void:
	if _sim != null:
		var at: Dictionary = _sim.plan_site_build_at(zone_id, candidate_id)
		var place: Dictionary = at.get("candidate", {})
		if not place.is_empty() and int(place.get("po", 0)) != int(at.get("production_area", 0)):
			zone_id = str(place.get("build_zone_id", zone_id))
	_zone_id = zone_id
	_reset_choices("sjo")
	_candidate = candidate_id
	_choosing_family = false
	_open()


## Geo B: the Sjø step's list of places, and the chosen one ("" none).
func candidate_list() -> Control:
	return _candidates


func selected_candidate() -> String:
	return _candidate


## The 'why here' beside the list.
func why_here() -> CandidateWhyHere:
	return _why


## Open the dialog on the family cards (RAS and Offshore tabs, "Nytt anlegg"):
## `family_id` ("land", "hav", "sjo") chosen, or "" to start at the cards.
func open_for_family(family_id: String, zone_id: String = "") -> void:
	_zone_id = zone_id
	_reset_choices(family_id if family_id != "" else "")
	_choosing_family = family_id == ""
	_open()


func _open() -> void:
	_built_site_id = ""
	_done_kind = ""
	show()
	_refresh()
	_enter()
	_focus_after_open.call_deferred()


func close() -> void:
	if not visible:
		return
	hide()
	_clear_ghost()
	closed.emit()


func zone_id() -> String:
	return _zone_id


## The fjord zone's build plan the dialog shows (the bridge's dictionary).
func plan() -> Dictionary:
	return _plan


## The chosen family ("sjo", "land", "hav"), variant and plot.
func family() -> String:
	return _family


func variant() -> String:
	return _variant


func plot_id() -> String:
	return _plot


## The farm build preview shown (SimBridge.preview_farm_build), {} on Sjø.
func farm_preview() -> Dictionary:
	return _farm_preview


## The family card of a catalogue family id, or null.
func family_card(family_id: String) -> FarmFamilyCard:
	return _family_row.get_node_or_null("Family_" + str(CARD_IDS.get(family_id, family_id))) as FarmFamilyCard


func comparison() -> ComparisonBars:
	return _comparison


func plot_lens() -> PlotLens:
	return _plot_lens


func facility_preview() -> FacilityBuildPreview:
	return _facility


func diagnosis_strip() -> DiagnosisStrip:
	return _diagnosis


## The select button of a variant's column, or null.
func variant_button(variant_id: String) -> Button:
	return _comparison.find_child("Choose_" + variant_id, true, false) as Button if _comparison != null else null


## The gate list of a variant's column, or null.
func gate_list(variant_id: String) -> GateList:
	return _comparison.find_child("Gates_" + variant_id, true, false) as GateList if _comparison != null else null


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		close()
		get_viewport().set_input_as_handled()


# ── Choices ─────────────────────────────────────────────────────────────────

func _reset_choices(family_id: String) -> void:
	_family = family_id
	_variant = SEA_BUILD if family_id == "sjo" else ""
	_option = "hfs_pilot_lot"
	_plot = ""
	_contract = "managed"
	_consortium = false
	_mitigations = []
	_farm_preview = {}
	_candidate = ""


## Geo B: choose an open-pen place (a row's click, an arrow key, Enter); the
## no-zone picker takes the place's build zone. A preview only.
func choose_candidate(candidate_id: String) -> void:
	var row := _candidates.candidate(candidate_id)
	if candidate_id == _candidate or row.is_empty() or _built_site_id != "":
		return
	_candidate = candidate_id
	if _zone_id == "":
		_zone_id = str(row.get("build_zone_id", ""))
	_refresh_sea()
	_fit.call_deferred()


## Choose a family (a card's click or its button).
func choose_family(family_id: String) -> void:
	if _built_site_id != "" or _done_kind != "":
		return
	_choosing_family = false
	if family_id == _family and _variant != "":
		_focus_variant.call_deferred()
		return
	_reset_choices(family_id)
	if family_id == "sjo":
		_variant = SEA_BUILD
	else:
		_variant = _first_startable(family_id)
	_clear_ghost()
	_refresh()
	_focus_variant.call_deferred()
	if family_id == "sjo":
		_show_places()   # as when the dialog opens on Sjø: the places in view


## Choose a variant (its column's button).
func choose_variant(variant_id: String) -> void:
	_variant = variant_id
	_plot = ""
	_farm_preview = {}
	_clear_ghost()
	_refresh()
	_focus_plot.call_deferred()


## Choose a plot: the ghost stands on it and the preview is read.
func choose_plot(plot_id: String) -> void:
	_plot = plot_id
	_refresh()
	_place_ghost()
	_confirm_btn.grab_focus.call_deferred()


func _on_mitigation(m: String, on: bool) -> void:
	if on and not _mitigations.has(m):
		_mitigations.append(m)
	elif not on:
		_mitigations.erase(m)
	_refresh()


func _first_startable(family_id: String) -> String:
	for f in _catalog.get("families", []):
		if str(f.get("id", "")) != family_id:
			continue
		for v in f.get("variants", []):
			if bool((_starts.get(str(v.get("id", "")), {}) as Dictionary).get("startable", false)):
				return str(v.get("id", ""))
	return ""


# ── Refresh ─────────────────────────────────────────────────────────────────

func _refresh() -> void:
	if _sim == null:
		return
	_catalog = _sim.get_farm_type_catalog()
	_starts = _sim.get_farm_type_starts()
	_research_names = {}
	for node in _sim.get_research_nodes():
		# The sim names nodes in English; the player reads the tree's translation.
		var key := "RESEARCH_" + SimLabels.upper_snake(str(node.get("id", ""))) + "_NAME"
		_research_names[str(node.get("id", ""))] = tr(key) if tr(key) != key else str(node.get("name", ""))
	_diagnosis.show_diagnosis(_sim.get_build_diagnosis())
	var done := _built_site_id != "" or _done_kind != ""
	_result_box.visible = done
	# The binding constraint is advice for choosing; on the result it read as
	# a red alarm about land and offshore capital over a sea site just built
	# (SHIP_PLAN cycle 1 playtest, the first season's step 16).
	_diagnosis_host.visible = not done
	_family_header.visible = not done
	_family_row.visible = not done
	_scroll.visible = not done
	_build_families()
	_build_comparison()
	_build_options()
	if _family == "sjo":
		_refresh_sea()
	else:
		_refresh_farm()
	_fit_after_layout()


func _family_dict(family_id: String) -> Dictionary:
	for f in _catalog.get("families", []):
		if str(f.get("id", "")) == family_id:
			return f
	return {}


func _variant_dict(variant_id: String) -> Dictionary:
	for f in _catalog.get("families", []):
		for v in f.get("variants", []):
			if str(v.get("id", "")) == variant_id:
				return v
	return {}


func _all_variants() -> Array:
	var out: Array = []
	for f in _catalog.get("families", []):
		out.append_array(f.get("variants", []))
	return out


# ── Step 1: the family cards ────────────────────────────────────────────────

func _build_families() -> void:
	for child in _family_row.get_children():
		_family_row.remove_child(child)
		child.queue_free()
	for f in _catalog.get("families", []):
		var id := str(f.get("id", ""))
		var card := FamilyCardScene.instantiate() as FarmFamilyCard
		card.name = "Family_" + str(CARD_IDS.get(id, id))
		_family_row.add_child(card)
		card.configure(f, _starts, id == _family and not _choosing_family, not _choosing_family)
		card.chosen.connect(choose_family)
	var buttons: Array = []
	for card in _family_row.get_children():
		buttons.append((card as FarmFamilyCard).choose_button())
	for i in buttons.size():
		var b := buttons[i] as Button
		b.focus_neighbor_left = b.get_path_to(buttons[maxi(0, i - 1)])
		b.focus_neighbor_right = b.get_path_to(buttons[mini(buttons.size() - 1, i + 1)])


# ── Step 2: the variants on the aligned bars ────────────────────────────────

func _build_comparison() -> void:
	var fam := _family_dict(_family)
	var show := not _choosing_family and not fam.is_empty()
	_variant_header.visible = show
	_comparison_host.visible = show
	if not show:
		return
	var variants: Array = (fam.get("variants", []) as Array).duplicate()
	var reference := ""
	if _family != "sjo":
		variants.push_front(_variant_dict(SEA_BUILD))
		reference = SEA_BUILD
	_comparison.setup(_catalog.get("axes", []), variants, _all_variants(), reference,
		_variant_head, _variant_gates)
	var buttons: Array = []
	for v in variants:
		var b := variant_button(str(v.get("id", "")))
		if b != null:
			buttons.append(b)
	for i in buttons.size():
		var b := buttons[i] as Button
		b.focus_neighbor_left = b.get_path_to(buttons[maxi(0, i - 1)])
		b.focus_neighbor_right = b.get_path_to(buttons[mini(buttons.size() - 1, i + 1)])
	var note := _comparison_host.get_node_or_null("SourceNote") as Label
	if note == null:
		note = Label.new()
		note.name = "SourceNote"
		note.theme_type_variation = &"SmallMuted"
		note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		note.custom_minimum_size = Vector2(400, 0)
		_comparison_host.add_child(note)
	note.text = tr("BUILD_COMPARE_NOTE")


func _variant_head(v: Dictionary) -> Control:
	var id := str(v.get("id", ""))
	var selected := id == _variant
	var start: Dictionary = _starts.get(id, {})
	var startable := bool(start.get("startable", false))
	var panel := PanelContainer.new()
	panel.name = "Head_" + id
	panel.theme_type_variation = &"KpiCardAccent" if selected else &"Tile"
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 4)
	panel.add_child(col)
	var name_label := Label.new()
	name_label.text = tr(str(v.get("name_key", "")))
	name_label.theme_type_variation = &"BodyStrong" if selected else &"BodySmallStrong"
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.custom_minimum_size = Vector2(112, 0)
	col.add_child(name_label)
	var tag := Label.new()
	tag.name = "State"
	if not startable:
		tag.text = tr("BUILD_VARIANT_LOCKED")
		tag.theme_type_variation = &"TagMuted"
	elif int(v.get("stage", 1)) > 1:
		tag.text = tr("BUILD_VARIANT_STAGE3")
		tag.theme_type_variation = &"TagInfo"
	elif not bool(v.get("affordable", false)):
		tag.text = tr("BUILD_VARIANT_NEEDS_FUNDS")
		tag.theme_type_variation = &"TagWarn"
	else:
		tag.text = tr("BUILD_VARIANT_OPEN")
		tag.theme_type_variation = &"TagOk"
	tag.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	col.add_child(tag)
	var b := Button.new()
	b.name = "Choose_" + id
	b.toggle_mode = true
	b.set_pressed_no_signal(selected)
	b.text = tr("BUILD_VARIANT_CHOSEN") if selected else tr("BUILD_VARIANT_CHOOSE")
	b.theme_type_variation = &"SmallButton" if selected else &"SmallGhostButton"
	b.custom_minimum_size = Vector2(0, 32)
	var reason := ""
	if id == "closed_pen":
		reason = tr("BUILD_VARIANT_UPGRADE_ON_PEN")
	elif not startable:
		reason = tr("BUILD_VARIANT_LOCKED_TIP")
	UIPolish.set_available(b, reason)
	b.pressed.connect(func() -> void: choose_variant(id))
	col.add_child(b)
	return panel


func _variant_gates(v: Dictionary) -> Control:
	var id := str(v.get("id", ""))
	var list := GateList.new()
	list.name = "Gates_" + id
	list.research_names = _research_names
	var names := {}
	for other in _all_variants():
		names[str(other.get("id", ""))] = str(other.get("name_key", ""))
	list.variant_names = names
	var start: Dictionary = _starts.get(id, {})
	list.show_gates(v, start.get("waived", []), str(v.get("family", "")))
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return list


## The HFS size (pilot lot or full lot): the land lane's two templates.
func _build_options() -> void:
	for child in _option_row.get_children():
		_option_row.remove_child(child)
		child.queue_free()
	_option_row.visible = _variant == "hfs" and not _choosing_family
	if not _option_row.visible:
		return
	var label := Label.new()
	label.text = tr("BUILD_OPTION_SIZE")
	label.theme_type_variation = &"SmallMuted"
	label.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_option_row.add_child(label)
	var group := ButtonGroup.new()
	for opt in ["hfs_pilot_lot", "hfs_lot"]:
		var b := Button.new()
		b.name = "Option_" + opt
		b.toggle_mode = true
		b.button_group = group
		b.set_pressed_no_signal(opt == _option)
		b.text = tr("BUILD_OPTION_" + opt.to_upper())
		b.theme_type_variation = &"SmallButton" if opt == _option else &"SmallGhostButton"
		b.pressed.connect(func() -> void:
			_option = opt
			_refresh()
			_place_ghost())
		_option_row.add_child(b)


# ── Sjø: the fjord zone's locality (U2-01) ──────────────────────────────────

func _refresh_sea() -> void:
	if _zone_id == "" and _candidate == "":
		_zone_id = _zone_when_every_place_is_held()
	_facility_host.visible = false
	var has_zone := _zone_id != ""
	_preview_header.visible = has_zone and _variant == SEA_BUILD
	_preview_row.visible = has_zone and _variant == SEA_BUILD
	_explain_callout.visible = has_zone and _variant == SEA_BUILD
	_plan = _sim.plan_site_build(_zone_id) if has_zone else {}
	var place := _refresh_places()
	var area := int(_plan.get("production_area", 0))
	if has_zone:
		_title.text = tr("EXPANSION_BUILD_TITLE") % SimLabels.zone(_zone_id) if place == "" else tr("SEA_CANDIDATE_BUILD_TITLE") % place
		var where := tr("EXPANSION_BUILD_FIRST_SITE")
		if bool(_plan.get("new_zone", false)):
			where = tr("EXPANSION_BUILD_NEW_ZONE")
		elif int(_plan.get("player_sites_in_zone", 0)) > 0:
			where = tr("EXPANSION_BUILD_YOUR_ZONE")
		_subtitle.text = tr("EXPANSION_BUILD_SUBTITLE") % [area, where] + ("" if place == "" else " · " + tr("SEA_CANDIDATE_PLACE") % place)
		_build_ledger()
		_pen_grid.pens = int(_plan.get("pens", 0))
		_pen_grid.queue_redraw()
		_explain.text = tr("EXPANSION_BUILD_EXPLAIN") % [SimFormatters.num(float(_plan.get("area_licence_mtb_t", 0.0))), area]
	else:
		_title.text = tr("BUILD_TITLE")
		# The picker's list frames the step; without one the map is the way in.
		_subtitle.text = tr("BUILD_SUBTITLE_SEA_PICK_ZONE") if _sea_rows.is_empty() \
			else tr("SEA_CANDIDATE_SUBTITLE_PICK") % int(_sea_rows[0].get("po", 0))
	var built_now := _built_site_id != ""
	var allowed := has_zone and bool(_plan.get("allowed", false))
	var reason := ""
	if not has_zone:
		reason = tr("SEA_CANDIDATE_PICK") if _candidates.first_free_row() != null else tr("BUILD_SEA_PICK_ZONE")
		if _candidate != "":
			# An unknown place asked for with no zone: the candidate check's own text.
			reason = tr(str(_sim.plan_site_build_at("", _candidate).get("candidate_label_key", "")))
	elif not allowed:
		reason = tr(str(_plan.get("label_key", "")))
		if str(_plan.get("reason_key", "")) == "insufficient_funds":
			# "Ikke nok penger: dette koster 6,00 MNOK, og du har 4,00 MNOK."
			reason = UIPolish.funds_blocker(float(_plan.get("cost_nok", 0.0)), float(_plan.get("balance_nok", 0.0)))
	_verdict.text = reason
	_verdict.visible = reason != "" and not built_now and not _choosing_family
	if built_now:
		_confirm_btn.text = tr("EXPANSION_BUILD_VIEW_BUTTON")
		_cancel_btn.text = tr("UI_CLOSE")
		UIPolish.set_available(_confirm_btn, "")
		_hint.text = ""
		return
	_cancel_btn.text = tr("EXPANSION_CANCEL")
	_confirm_btn.text = tr("EXPANSION_BUILD_CONFIRM") % SimFormatters.format_nok(float(_plan.get("cost_nok", 0.0))) if has_zone else tr("BUILD_CONFIRM_GENERIC")
	UIPolish.set_available(_confirm_btn, reason)
	_hint.text = tr("EXPANSION_BUILD_HINT")


## Geo B: the area's open-pen places beside the chosen one's 'why here' (no
## zone: the home area's, nothing chosen). A zone's build preselects the first
## free place, so tutorial step 16 stays BuildBtn then ConfirmBtn; with none
## free the build is U2-01's without a place. A place already chosen (by the
## player or the map) is never swapped for the suggestion, even held or
## unknown: the plan's verdict says why it cannot be built. Returns the chosen
## place's label.
func _refresh_places() -> String:
	var listed := _variant == SEA_BUILD and (_zone_id == "" or bool(_plan.get("success", false)))
	_sea_rows = _sim.get_sea_candidates(int(_plan.get("production_area", 0))) if listed else []
	var chosen: Dictionary = {}
	for c in _sea_rows:
		if str(c.get("id", "")) == _candidate or (_candidate == "" and _zone_id != "" and bool(c.get("suggested", false))):
			chosen = c
			break
	if _candidate == "":
		_candidate = str(chosen.get("id", ""))
	_plot_header.text = tr("SEA_CANDIDATE_HEADER")
	_plot_header.visible = not _sea_rows.is_empty()
	_plot_host.visible = not _sea_rows.is_empty()
	_plot_lens.visible = false
	_candidates.get_parent().visible = true
	_candidates.show_candidates(_sea_rows, _candidate)
	if chosen.is_empty() and not _sea_rows.is_empty() and _candidates.first_free_row() == null:
		_why.show_all_held(_zone_id)
	else:
		_why.show_candidate(chosen, _zone_id)
	var row := _candidates.row_button(_candidate)
	var reachable := row != null and row.focus_mode != Control.FOCUS_NONE
	_confirm_btn.focus_neighbor_top = _confirm_btn.get_path_to(row) if reachable else NodePath()
	if _candidate != "" and _zone_id != "":
		_plan = _sim.plan_site_build_at(_zone_id, _candidate)
	return str(chosen.get("label", ""))


## Geo B: the zone a Sjø build with no zone takes when every place in the
## home area already holds one of the company's sites ("" while one is free):
## the build is then the zone's, without a place, instead of a list with
## nothing to choose (captain's review of track B, finding 3).
func _zone_when_every_place_is_held() -> String:
	var rows: Array = _sim.get_sea_candidates(0)
	for c in rows:
		if str(c.get("taken_by_site_id", "")) == "":
			return ""
	return str(rows[0].get("build_zone_id", "")) if not rows.is_empty() else ""


func _build_ledger() -> void:
	for child in _ledger.get_children():
		_ledger.remove_child(child)
		child.queue_free()
	var cost := float(_plan.get("cost_nok", 0.0))
	var balance := float(_plan.get("balance_nok", 0.0))
	var after := balance - cost
	var area := int(_plan.get("production_area", 0))
	var area_mtb := float(_plan.get("area_licence_mtb_t", 0.0))
	_row("Cost", "EXPANSION_LEDGER_COST", SimFormatters.format_nok(cost), &"Figure")
	_row("Cash", "EXPANSION_LEDGER_CASH", "%s  →  %s" % [SimFormatters.format_nok(balance), SimFormatters.format_nok(after)],
		&"FigureCrit" if after < 0.0 else &"Figure")
	_row("Running", "EXPANSION_LEDGER_RUNNING", tr("EXPANSION_LEDGER_RUNNING_VALUE") % [
		SimFormatters.format_nok(float(_plan.get("running_cost_per_month_nok", 0.0))), int(round(float(_plan.get("workers", 0.0))))], &"Figure")
	_row("Locality", "EXPANSION_LEDGER_LOCALITY_MTB", tr("EXPANSION_TONNES") % SimFormatters.num(float(_plan.get("locality_mtb_t", 0.0))), &"Figure")
	_row("Licence", "EXPANSION_LEDGER_LICENCE_MTB", tr("EXPANSION_LEDGER_LICENCE_VALUE") % [
		SimFormatters.num(area_mtb), SimFormatters.num(area_mtb + float(_plan.get("licence_mtb_added_t", 0.0))),
		SimFormatters.num(float(_plan.get("licence_mtb_added_t", 0.0)))], &"Figure", area)
	_row("Pens", "EXPANSION_LEDGER_PENS", tr("EXPANSION_LEDGER_PENS_VALUE") % [
		int(_plan.get("pens", 0)), SimFormatters.num(float(_plan.get("pen_circumference_m", 0.0))),
		SimFormatters.num(float(_plan.get("pen_net_depth_m", 0.0))),
		SimFormatters.num(float(_plan.get("pen_volume_m3", 0.0)))], &"Figure")


func _row(id: String, key: String, value: String, variation: StringName, area: int = -1) -> void:
	var k := Label.new()
	k.name = "Key" + id
	k.text = tr(key) % area if area >= 0 else tr(key)
	k.theme_type_variation = &"OverlineMuted"
	k.uppercase = true
	k.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_ledger.add_child(k)
	var v := Label.new()
	v.name = "Value" + id
	v.text = value
	v.theme_type_variation = variation
	v.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	# A floor width: an autowrapping label measured before the grid has a width
	# wraps word-per-line and reports a huge minimum height. The VBox above had
	# already sized PreviewRow's slot from the old minimum, and Godot clamps
	# the child to the new one, so without this the row froze 2750 px tall over
	# its siblings on the CI runner (PR #11, run 06:26; the rects in the lint).
	v.custom_minimum_size.x = 200.0
	_ledger.add_child(v)


# ── Land and Hav: the site lens and the preview ─────────────────────────────

func _refresh_farm() -> void:
	_preview_header.visible = false
	_preview_row.visible = false
	_explain_callout.visible = false
	var fam_name := tr(str(_family_dict(_family).get("name_key", "")))
	_title.text = tr("BUILD_TITLE_FAMILY") % fam_name if not _choosing_family else tr("BUILD_TITLE")
	_subtitle.text = tr("BUILD_SUBTITLE_" + _family.to_upper()) if not _choosing_family else tr("BUILD_SUBTITLE_PICK_FAMILY")
	var startable := _variant != "" and bool((_starts.get(_variant, {}) as Dictionary).get("startable", false))
	var show_plots := not _choosing_family and startable
	_plot_header.visible = show_plots
	_plot_host.visible = show_plots
	_plot_header.text = tr("BUILD_PLOT_HEADER")
	_plot_lens.visible = true
	_candidates.get_parent().visible = false
	_confirm_btn.focus_neighbor_top = NodePath()
	if show_plots:
		_plot_lens.show_plots(_sim.get_build_plots(_family), _plot, _variant, _option)
	_farm_preview = {}
	if show_plots and _plot != "":
		_farm_preview = _sim.preview_farm_build(_variant, _plot, _option, _contract, 1.0, _mitigations)
		if _family == "hav" and _consortium:
			# The consortium's share is the sea lane's (the preview's own figure).
			var share := float((_farm_preview.get("offshore", {}) as Dictionary).get("consortium_equity_share", 1.0))
			_farm_preview = _sim.preview_farm_build(_variant, _plot, _option, _contract, share, _mitigations)
	_facility_host.visible = bool(_farm_preview.get("ok", false))
	if _facility_host.visible:
		_facility.show_preview(_farm_preview, _contract, _consortium, _mitigations)
	_update_farm_footer(startable)


func _update_farm_footer(startable: bool) -> void:
	_cancel_btn.text = tr("EXPANSION_CANCEL") if _done_kind == "" else tr("UI_CLOSE")
	if _done_kind != "":
		_confirm_btn.text = tr("BUILD_DONE_VIEW_" + _family.to_upper())
		UIPolish.set_available(_confirm_btn, "")
		_verdict.visible = false
		_hint.text = ""
		return
	var reason := ""
	if _choosing_family:
		reason = tr("BUILD_PICK_FAMILY")
	elif _variant == "" or not startable:
		reason = tr("BUILD_PICK_VARIANT")
	elif _plot == "":
		reason = tr("BUILD_PICK_PLOT")
	elif not bool(_farm_preview.get("ok", false)):
		reason = tr("LAND_REASON_ENGINE_NOT_READY")
	var action := _farm_action()
	if reason == "":
		reason = str(action.get("blocker", ""))
	_confirm_btn.text = str(action.get("text", tr("BUILD_CONFIRM_GENERIC")))
	UIPolish.set_available(_confirm_btn, reason)
	_verdict.text = reason
	_verdict.visible = reason != "" and _plot != ""
	_hint.text = tr("BUILD_HINT_" + _family.to_upper())


## What the confirm does now: {"verb": "apply_permits" | "build_land" |
## "build_offshore" | "apply_licence", "text", "blocker" (why it cannot, ""
## when it can)}.
func _farm_action() -> Dictionary:
	if _family == "land":
		var land: Dictionary = _farm_preview.get("land", {})
		var permits: Array = land.get("permits", [])
		var pending := permits.filter(func(p: Dictionary) -> bool: return str(p.get("status", "")) == "pending").size()
		var needed := permits.filter(func(p: Dictionary) -> bool: return str(p.get("status", "")) in ["needed", "refused"]).size()
		if needed > 0 and pending == 0:
			return {"verb": "apply_permits", "text": tr("BUILD_CONFIRM_APPLY") % needed, "blocker": ""}
		if pending > 0:
			return {"verb": "apply_permits", "text": tr("BUILD_CONFIRM_APPLY") % maxi(needed, 1), "blocker": tr("LAND_REASON_ALREADY_PENDING")}
		var verdict := str(land.get("reason_key", ""))
		var blocker := "" if verdict == "" else tr(str(land.get("label_key", "")))
		return {"verb": "build_land", "text": tr("BUILD_CONFIRM_BUILD_LAND") % SimFormatters.format_nok(float(land.get("budget_nok", 0.0))), "blocker": blocker}
	var sea: Dictionary = _farm_preview.get("offshore", {})
	var refusal := str(sea.get("refusal_key", ""))
	if refusal == "licence_missing":
		return _licence_action()
	var blocker := ""
	if refusal == "insufficient_funds":
		blocker = UIPolish.funds_blocker(float(sea.get("first_payment_nok", 0.0)), float(_sim.balance))
	elif refusal != "":
		blocker = tr("BUILD_SEA_REASON_" + refusal.to_upper())
	return {"verb": "build_offshore", "text": tr("BUILD_CONFIRM_BUILD_SEA") % SimFormatters.format_nok(float(sea.get("first_payment_nok", 0.0))), "blocker": blocker}


## F2-09 (merged on wave4/int): an offshore unit is built on a
## development-licence award for its plot. Without one the confirm files the
## application while a round is open, as the Land path files its permits; a
## pending application or a closed round says why it cannot.
func _licence_action() -> Dictionary:
	var text := tr("BUILD_CONFIRM_APPLY_LICENCE")
	var dev: Dictionary = _sim.get_development_round()
	for a in dev.get("applications", []):
		if str(a.get("plot_id", "")) == _plot and str(a.get("status", "")) in ["Submitted", "Appealed"]:
			return {"verb": "apply_licence", "text": text,
				"blocker": tr("BUILD_SEA_REASON_LICENCE_PENDING") % SimFormatters.format_day(int(a.get("decision_day", 0)))}
	if not bool(dev.get("open", false)):
		return {"verb": "apply_licence", "text": text, "blocker": tr("BUILD_SEA_REASON_LICENCE_MISSING")}
	return {"verb": "apply_licence", "text": text, "blocker": ""}


func _place_ghost() -> void:
	var ghost := FarmGhost.ensure(get_tree())
	if ghost == null:
		return
	if _plot == "":
		ghost.clear_ghost()
		return
	ghost.show_ghost(_plot_lens.plot_by_id(_plot), _variant, _option)


func _clear_ghost() -> void:
	var ghost := get_tree().get_first_node_in_group(FarmGhost.GROUP) as FarmGhost if is_inside_tree() else null
	if ghost != null:
		ghost.clear_ghost()


# ── Focus (the keyboard path) and fit ────────────────────────────────────────

func _focus_after_open() -> void:
	if _choosing_family or _family == "":
		var first := _family_row.get_child(0) as FarmFamilyCard if _family_row.get_child_count() > 0 else null
		if first != null:
			first.choose_button().grab_focus()
		return
	if _family == "sjo":
		_show_places()
		if _zone_id == "" and _plot_host.visible and _candidates.first_free_row() != null:
			_candidates.first_free_row().grab_focus()
		elif _confirm_btn.disabled:
			_cancel_btn.grab_focus()
		else:
			_confirm_btn.grab_focus()
		return
	var card := family_card(_family)
	if card != null:
		card.choose_button().grab_focus()


## The plot step in view once laid out, the variants a scroll above: a Sjø
## build opens on its places (Geo B), the map's hav_1 card on hav_1's lens
## (GEO-VIS). `family` "" means whichever family is chosen by then.
func show_plot_step(family: String = "") -> void:
	await get_tree().process_frame
	_fit()   # again, once the strip and the header wrap at their real width
	for i in 2:
		await get_tree().process_frame
	if visible and (family == "" or _family == family) and _plot_host.visible:
		_scroll.scroll_vertical = int(_plot_header.position.y)


func _show_places() -> void:
	await show_plot_step("sjo")


func _focus_variant() -> void:
	if _family == "sjo" and _zone_id == "" and _plot_host.visible and _candidates.first_free_row() != null:
		_candidates.first_free_row().grab_focus()   # Geo B: the no-zone picker's places come first
		return
	if _family == "sjo" and _plot_host.visible and _candidates.first_free_row() == null and not _confirm_btn.disabled:
		_confirm_btn.grab_focus()   # Geo B: every place held, the zone's build is what is left to do
		return
	var b := variant_button(_variant) if _variant != "" else null
	if b == null or b.disabled:
		for v in _family_dict(_family).get("variants", []):
			var other := variant_button(str(v.get("id", "")))
			if other != null and not other.disabled:
				b = other
				break
	if b != null:
		b.grab_focus()


func _focus_plot() -> void:
	if not _plot_host.visible:
		_focus_variant()
		return
	for p in _plot_lens.plots:
		var b := _plot_lens.plot_button(str(p.get("id", "")))
		if b != null and not b.disabled:
			b.grab_focus()
			return
	_confirm_btn.grab_focus()


## The entrance: a fade, and a rise into the place the centre gives the panel
## at its size now. A fit grows the panel while it slides in (Sjø's places),
## and a rise towards where the panel stood at the start held it, and its
## confirm, below the window for the slide's 0.28 s (GEO-VIS close: U2-01's
## auction test clicked past it; the merge of track B saw it at 1280x720).
## The centre sorts the panel again at the end.
func _enter() -> void:
	if _slide != null and _slide.is_valid():
		_slide.kill()
	_dialog.modulate.a = 0.0
	_slide = _dialog.create_tween()
	_slide.set_ease(Tween.EASE_OUT)
	_slide.set_trans(Tween.TRANS_CUBIC)
	_slide.set_parallel(true)
	_slide.tween_property(_dialog, "modulate:a", 1.0, ENTER_SECONDS)
	_slide.tween_method(_rise_into_place, ENTER_RISE_PX, 0.0, ENTER_SECONDS)
	var centre := _dialog.get_parent() as Container
	if centre != null:
		_slide.finished.connect(centre.queue_sort)


## The panel `offset` px below the place its centre gives it now.
func _rise_into_place(offset: float) -> void:
	var centre := _dialog.get_parent() as Control
	if centre != null:
		_dialog.position.y = floorf((centre.size.y - _dialog.get_combined_minimum_size().y) * 0.5) + offset


## Fit at the end of this frame and once more on the next, when rebuilt steps
## (the family cards, the places) and the wrapped header and strip have their
## real size. Measured only at the end of the refresh, «Nytt anlegg» → Sjø kept
## the steps' scroll at its 120 px floor with the places out of sight (captain's
## merge of track B; track B's follow-up for Land and Hav too).
func _fit_after_layout() -> void:
	_fit.call_deferred()
	# One more pass per frame however many refreshes ran; a freed dialog drops it.
	if is_inside_tree() and not get_tree().process_frame.is_connected(_fit):
		get_tree().process_frame.connect(_fit, CONNECT_ONE_SHOT)


## Keep the dialog inside the window at any size and UI scale: the steps
## scroll, the header, the family cards and the footer stay.
func _fit() -> void:
	if not visible or _scroll == null:
		return
	# The dialog covers its host (the map tab), not the whole window.
	var view := size if size.y > 0.0 else get_viewport_rect().size
	var body := _scroll.get_child(0) as Control
	var content := body.get_combined_minimum_size().y if body != null else 0.0
	_scroll.custom_minimum_size.y = 0.0
	var chrome := _dialog.get_combined_minimum_size().y - _scroll.get_combined_minimum_size().y
	var room := view.y - chrome - 32.0
	_scroll.custom_minimum_size.y = clampf(content, 0.0, maxf(120.0, room))
	# Re-place the steps with the minimums as they are NOW: a child whose
	# minimum changed after the last sort (the rebuilt ledger's wrapping
	# labels) otherwise keeps a slot computed from the stale value, and when
	# nothing else dirties the layout — slower runners — that never heals.
	if body is Container:
		(body as Container).queue_sort()
	_scroll.queue_sort()


# ── Confirm ─────────────────────────────────────────────────────────────────

func _on_confirm() -> void:
	if _sim == null:
		return
	if _built_site_id != "":
		var site := _built_site_id
		close()
		view_site_requested.emit(site)
		return
	if _done_kind != "":
		close()
		tab_requested.emit("ras" if _family == "land" else "offshore")
		return
	if _family == "sjo":
		_confirm_sea()
	else:
		_confirm_farm()


func _confirm_sea() -> void:
	var place := str(_candidates.candidate(_candidate).get("label", ""))
	var result: Dictionary = _sim.build_expansion_site_at(_zone_id, _candidate) if _candidate != "" else _sim.build_expansion_site(_zone_id)
	if not bool(result.get("success", false)):
		_refresh()
		_verdict.text = tr(str(result.get("label_key", "EXPANSION_REASON_ENGINE_NOT_READY")))
		_verdict.visible = true
		return
	_built_site_id = str(result.get("site_id", ""))
	_result_title.text = tr("EXPANSION_BUILD_DONE_TITLE") % str(result.get("site_label", "")) if place == "" \
		else tr("SEA_CANDIDATE_DONE_TITLE") % [str(result.get("site_label", "")), place]
	_result_body.text = tr("EXPANSION_BUILD_DONE_BODY") % int(_plan.get("pens", 0))
	_refresh()
	_enter()
	built.emit(_built_site_id, _zone_id)


func _confirm_farm() -> void:
	var action := _farm_action()
	var template := str(_farm_preview.get("template", ""))
	var result: Dictionary = {}
	match str(action.get("verb", "")):
		"apply_permits":
			result = _sim.apply_land_permits(_plot, template, 1, _mitigations)
		"build_land":
			result = _sim.build_land_site(_plot, template, _contract, "")
		"build_offshore":
			var equity := float((_farm_preview.get("economics", {}) as Dictionary).get("equity_share", 1.0))
			result = _sim.build_offshore_unit(_plot, str(_farm_preview.get("offshore_type", "")), _contract, equity)
		"apply_licence":
			result = _sim.apply_development_licence(_variant, _plot)
	if not bool(result.get("success", false)):
		_refresh()
		var key := str(result.get("label_key", ""))
		if key == "":
			var err := str(result.get("error", ""))
			key = "BUILD_SEA_REASON_" + err.to_upper() if err != "" else "LAND_REASON_ENGINE_NOT_READY"
		_verdict.text = tr(key)
		_verdict.visible = true
		return
	_done_kind = str(action.get("verb", ""))
	var plot_name := str(_plot_lens.plot_by_id(_plot).get("name", _plot))
	match _done_kind:
		"apply_permits":
			var cases: Array = _sim.get_land_permit_cases(_plot)
			var last := 0
			for c in cases:
				last = maxi(last, int(c.get("decision_day", 0)))
			_result_title.text = tr("BUILD_DONE_PERMITS_TITLE") % plot_name
			_result_body.text = tr("BUILD_DONE_PERMITS_BODY") % [int(result.get("value2", cases.size())),
				SimFormatters.percent(float(result.get("value", 0.0)), 1), SimFormatters.format_day(last)]
		"build_land":
			_result_title.text = tr("BUILD_DONE_LAND_TITLE") % plot_name
			_result_body.text = tr("BUILD_DONE_LAND_BODY") % SimFormatters.format_day(int(result.get("value2", 0)))
		"apply_licence":
			_result_title.text = tr("BUILD_DONE_LICENCE_TITLE") % plot_name
			_result_body.text = tr("BUILD_DONE_LICENCE_BODY") % ["%.2f" % float(result.get("value", 0.0)),
				"%.2f" % float((_sim.get_development_round() as Dictionary).get("award_bar", 0.0)),
				SimFormatters.format_day(int(result.get("value2", 0)))]
		"build_offshore":
			_result_title.text = tr("BUILD_DONE_SEA_TITLE") % plot_name
			_result_body.text = tr("BUILD_DONE_SEA_BODY") % SimFormatters.format_day(int(
				(_farm_preview.get("offshore", {}) as Dictionary).get("first_stocking_day", 0)))
	_refresh()
	_enter()
	built.emit(str(result.get("id", "")), _plot)


## The standard mooring frame seen from above: the pens in two rows inside the
## grid of mooring lines, with the feed barge at the end, in the chart's
## colours. Purely illustrative of the pen count the engine builds.
class PenGridPreview extends Control:
	var pens := 8

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		resized.connect(queue_redraw)

	func _draw() -> void:
		if pens <= 0 or size.x < 40.0 or size.y < 40.0:
			return
		var water := LakskongenTheme.palette(self, &"info")
		var line := LakskongenTheme.palette(self, &"stroke")
		var collar := LakskongenTheme.palette(self, &"text")
		var buoy := Color("#E8B92A")
		var rows := 2 if pens > 3 else 1
		var cols := int(ceil(float(pens) / rows))
		var barge_w := 26.0
		var pad := 14.0
		var cell := minf((size.x - pad * 2.0 - barge_w - 10.0) / cols, (size.y - pad * 2.0) / rows)
		var origin := Vector2((size.x - cell * cols - barge_w - 10.0) * 0.5, (size.y - cell * rows) * 0.5)
		draw_rect(Rect2(origin, Vector2(cell * cols, cell * rows)), Color(water, 0.10), true)
		for r in rows + 1:
			draw_line(origin + Vector2(0, r * cell), origin + Vector2(cols * cell, r * cell), Color(line, 0.7), 1.0)
		for c in cols + 1:
			draw_line(origin + Vector2(c * cell, 0), origin + Vector2(c * cell, rows * cell), Color(line, 0.7), 1.0)
		for r in rows + 1:
			for c in cols + 1:
				draw_circle(origin + Vector2(c * cell, r * cell), 2.5, buoy)
		var n := 0
		for r in rows:
			for c in cols:
				if n >= pens:
					break
				var center := origin + Vector2((c + 0.5) * cell, (r + 0.5) * cell)
				var radius := cell * 0.36
				draw_circle(center, radius, Color(water, 0.28))
				draw_arc(center, radius, 0.0, TAU, 40, collar, 2.5, true)
				n += 1
		var barge := Rect2(origin + Vector2(cols * cell + 10.0, rows * cell * 0.5 - 18.0), Vector2(barge_w, 36.0))
		draw_rect(barge, collar, true)
		draw_rect(barge.grow(-5.0), Color.WHITE, true)
