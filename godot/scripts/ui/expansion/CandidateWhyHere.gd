class_name CandidateWhyHere
extends PanelContainer

## 'Why here' for an open-pen candidate (geo stages 2-3, track B;
## docs/geo/STAGE23_PLAN.md §2 B). It leads with what decides the place, not
## with the score (the scores inside an area are close): the sentence saying
## what costs the place most, then one row per weighted score component with
## its weight from the published rules, its 0-100 bar and what was measured
## there (depth, openness, nearest farm, current, waves, oxygen), the river
## factor with the nearest salmon river, the zone whose licence and water the
## build uses, and the honest note: a siting model on open data that explains
## the choice and changes no farming (ruling OD3). Every figure is the
## bridge's row (SimBridge.get_sea_candidates); nothing here decides anything.

## Open-pen candidates per production area (candidates.json candidates.per_po).
const CANDIDATES_PER_AREA := 12
## What was measured for a score term: [the row's field, decimals, unit key or unit, from the ocean block].
const MEASURED := {
	"depth": ["depth_m", 0, " m", false],
	"exchange": ["openness", -1, "SEA_CANDIDATE_OPENNESS", false],
	"farm_distance": ["nearest_farm_km", 1, " km", false],
	"current": ["current_speed_ms", 2, " m/s", true],
	"waves": ["hs_m", 1, " m", true],
	"oxygen": ["o2_mmol_m3", 0, " mmol/m³", true],
	"river": ["nearest_river_km", 1, " km", false],
}

var _empty: Label = null
var _body: VBoxContainer = null
var _overline: Label = null
var _place: Label = null
var _rank: Label = null
var _score_head: Label = null
var _score: Label = null
var _binding: Label = null
var _grid: GridContainer = null
var _zone: Label = null
var _note: Label = null
## The candidate shown ({} none) and the zone its build uses.
var shown: Dictionary = {}
var _zone_id := ""


func _init() -> void:
	theme_type_variation = &"Tile"
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	custom_minimum_size = Vector2(400, 0)
	var stack := VBoxContainer.new()
	stack.name = "Stack"
	stack.alignment = BoxContainer.ALIGNMENT_CENTER   # the hint sits in the middle of an empty panel
	add_child(stack)
	_empty = _label("Empty", &"BodySmallMuted", true)
	_empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	stack.add_child(_empty)
	_body = VBoxContainer.new()
	_body.name = "Body"
	_body.add_theme_constant_override("separation", 6)
	stack.add_child(_body)
	var head := HBoxContainer.new()
	head.name = "Head"
	head.add_theme_constant_override("separation", 12)
	_body.add_child(head)
	var names := VBoxContainer.new()
	names.name = "Names"
	names.add_theme_constant_override("separation", 0)
	names.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(names)
	_overline = _label("Overline", &"OverlineMuted", false)
	_overline.uppercase = true
	names.add_child(_overline)
	_place = _label("Place", &"Subheading", true)
	names.add_child(_place)
	_rank = _label("Rank", &"SmallMuted", true)
	names.add_child(_rank)
	var score_box := VBoxContainer.new()
	score_box.name = "ScoreBox"
	score_box.add_theme_constant_override("separation", 0)
	head.add_child(score_box)
	_score_head = _label("ScoreHead", &"CaptionMuted", false)
	_score_head.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	score_box.add_child(_score_head)
	_score = _label("Score", &"Figure", false)
	_score.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	score_box.add_child(_score)
	var callout := PanelContainer.new()
	callout.name = "BindingCallout"
	callout.theme_type_variation = &"CalloutWarn"
	_body.add_child(callout)
	_binding = _label("Binding", &"BodySmall", true)
	callout.add_child(_binding)
	_grid = GridContainer.new()
	_grid.name = "Components"
	_grid.columns = 5
	_grid.add_theme_constant_override("h_separation", 8)
	_grid.add_theme_constant_override("v_separation", 3)
	_body.add_child(_grid)
	_zone = _label("Zone", &"SmallMuted", true)
	_body.add_child(_zone)
	_note = _label("Note", &"CaptionMuted", true)
	_body.add_child(_note)


func _ready() -> void:
	_overline.text = tr("SEA_CANDIDATE_WHY_TITLE")
	_score_head.text = tr("SEA_CANDIDATE_SCORE_LABEL")
	_empty.text = tr("SEA_CANDIDATE_WHY_EMPTY")
	_note.text = tr("SEA_CANDIDATE_HONEST_NOTE")
	show_candidate(shown, _zone_id)


## A score term's name ("current" → "Strøm").
static func term_name(term: String) -> String:
	return String(TranslationServer.translate("SEA_CANDIDATE_COMPONENT_" + term.to_upper()))


## The sentence saying what costs a place most ("none" included).
static func binding_text(term: String) -> String:
	return String(TranslationServer.translate("SEA_CANDIDATE_BINDING_" + (term if term != "" else "none").to_upper()))


## Show a candidate (the bridge's row; {} for the hint) built through `zone_id`
## ("" for its own build zone).
func show_candidate(c: Dictionary, zone_id: String) -> void:
	shown = c
	_zone_id = zone_id
	_empty.visible = c.is_empty()
	_body.visible = not c.is_empty()
	if c.is_empty() or not is_inside_tree():
		return
	var binding := str(c.get("binding", "none"))
	_place.text = str(c.get("label", ""))
	_rank.text = tr("SEA_CANDIDATE_RANK") % [int(c.get("rank", 0)), CANDIDATES_PER_AREA, int(c.get("po", 0))]
	_score.text = tr("SEA_CANDIDATE_SCORE") % int(round(float(c.get("score", 0.0)) * 100.0))
	_binding.text = binding_text(binding)
	_zone.text = tr("SEA_CANDIDATE_ZONE") % SimLabels.zone(zone_id if zone_id != "" else str(c.get("build_zone_id", "")))
	_fill_grid(c, binding)


## The score's label ("94 av 100").
func score_label() -> Label:
	return _score


## The sentence saying what costs the place most.
func binding_label() -> Label:
	return _binding


## The zone line ("Lisens og vann: sonen …").
func zone_label() -> Label:
	return _zone


## The honest note.
func note_label() -> Label:
	return _note


## The bar of a score term ("depth" … "river"), or null.
func component_bar(term: String) -> ProgressBar:
	return _grid.get_node_or_null("Bar_" + term) as ProgressBar


## A score term's 0-100 points (the river factor "×1,00"), or null.
func component_value(term: String) -> Label:
	return _grid.get_node_or_null("Value_" + term) as Label


## A score term's weight ("21 %"; "" for the river factor), or null.
func component_weight(term: String) -> Label:
	return _grid.get_node_or_null("Weight_" + term) as Label


## What was measured for a score term ("82 m", "0,03 m/s"), or null.
func component_measured(term: String) -> Label:
	return _grid.get_node_or_null("Measured_" + term) as Label


## A score term's name label, or null.
func component_name(term: String) -> Label:
	return _grid.get_node_or_null("Name_" + term) as Label


## What the row says was measured for `term` ("" when the term has no own measurement).
static func measured_text(c: Dictionary, term: String) -> String:
	if not MEASURED.has(term):
		return ""
	var m: Array = MEASURED[term]
	var source: Dictionary = c.get("ocean", {}) if bool(m[3]) else c
	if not source.has(str(m[0])):
		return ""
	var v := float(source.get(str(m[0]), 0.0))
	if int(m[1]) < 0:
		return String(TranslationServer.translate(str(m[2]))) % SimFormatters.percent(v)
	return SimFormatters.num(v, int(m[1])) + str(m[2])


func _fill_grid(c: Dictionary, binding: String) -> void:
	for child in _grid.get_children():
		_grid.remove_child(child)
		child.queue_free()
	for head in ["", "SEA_CANDIDATE_COL_WEIGHT", "", "SEA_CANDIDATE_COL_POINTS", "SEA_CANDIDATE_COL_MEASURED"]:
		var h := _label("Head_%d" % _grid.get_child_count(), &"CaptionMuted", false)
		h.text = tr(head) if head != "" else ""
		h.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT if head != "" else HORIZONTAL_ALIGNMENT_LEFT
		_grid.add_child(h)
	var weights: Dictionary = c.get("weights", {})
	var components: Dictionary = c.get("components", {})
	var total := 0.0
	for w in weights.values():
		total += float(w)
	for term in (c.get("component_costs", {}) as Dictionary).keys():
		var key := str(term)
		var river := key == "river"
		var strong := key == binding
		var value := float(c.get("river_factor", 1.0)) if river else float(components.get(key, 0.0))
		_cell("Name_" + key, tr("SEA_CANDIDATE_RIVER_FACTOR") if river else term_name(key),
			&"BodySmallStrong" if strong else &"BodySmall", HORIZONTAL_ALIGNMENT_LEFT)
		_cell("Weight_" + key, "" if river else SimFormatters.percent(float(weights.get(key, 0.0)) / maxf(total, 1e-9)),
			&"SmallMuted", HORIZONTAL_ALIGNMENT_RIGHT)
		var bar := ProgressBar.new()
		bar.name = "Bar_" + key
		bar.theme_type_variation = &"ProgressBarWarn" if strong else &"ProgressBar"
		bar.show_percentage = false
		bar.min_value = 0.0
		bar.max_value = 100.0
		bar.value = value * 100.0
		bar.custom_minimum_size = Vector2(56, 8)
		bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		bar.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		_grid.add_child(bar)
		_cell("Value_" + key, ("×" + SimFormatters.num(value, 2)) if river else str(int(round(value * 100.0))),
			&"FigureSmall", HORIZONTAL_ALIGNMENT_RIGHT)
		_cell("Measured_" + key, measured_text(c, key), &"SmallMuted", HORIZONTAL_ALIGNMENT_RIGHT)


func _cell(cell_name: String, text: String, variation: StringName, align: HorizontalAlignment) -> void:
	var l := _label(cell_name, variation, false)
	l.text = text
	l.horizontal_alignment = align
	l.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_grid.add_child(l)


func _label(label_name: String, variation: StringName, wrap: bool) -> Label:
	var l := Label.new()
	l.name = label_name
	l.theme_type_variation = variation
	if wrap:
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.custom_minimum_size = Vector2(220, 0)
		l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return l
