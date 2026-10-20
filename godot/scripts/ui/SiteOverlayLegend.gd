extends PanelContainer

## SiteOverlayLegend — compact data-overlay selector + legend for sea sites.
##
## Cycles Lice risk / Oxygen / Biomass / Gill risk / Containment, derives a
## per-site 0..1 intensity from live SimBridge data, and maps it onto the
## Nordic green→amber→red palette. Intended to be embedded by MapPanel.gd,
## which also tints its region markers by the average zone intensity.
##
## All SimBridge access is null-guarded: with no engine the legend simply
## shows an empty state instead of erroring.
##
## Public API
##   signal metric_changed(metric_id: String)
##   signal sites_updated()
##   set_metric(id) -> void            get_metric() -> String
##   cycle_metric() -> void            get_metric_label() -> String
##   refresh() -> void                 get_sites() -> Array
##   get_site_intensity(site_id) -> float
##   get_site_color(site_id) -> Color
##   get_zone_intensity(zone_id) -> float   (-1.0 when the zone has no sites)
##   get_area_intensity(area, zone_area) -> float   (per production area; the map's plates)
##   intensity_to_color(t) -> Color

signal metric_changed(metric_id: String)
signal sites_updated()

const SIM_BRIDGE_PATH := "/root/SimBridge"

const COL_GREEN := Color("#2E6B45")
const COL_AMBER := Color("#8A5A00")
const COL_RED   := Color("#B3172F")
const COL_TEXT  := Color("#1C1E20")
const COL_MUTED := Color("#5A6168")

const METRIC_ORDER := ["lice", "oxygen", "biomass", "gill", "containment"]

# Translation keys: "label" names the overlay, "lo"/"hi" are the legend
# end-captions, "note" explains the derivation.
const METRICS := {
	"lice": {"label": "UI_OVERLAY_LICE", "lo": "UI_OVERLAY_LICE_LO", "hi": "UI_OVERLAY_LICE_HI", "note": "UI_OVERLAY_LICE_NOTE"},
	"oxygen": {"label": "UI_OVERLAY_OXYGEN", "lo": "UI_OVERLAY_OXYGEN_LO", "hi": "UI_OVERLAY_OXYGEN_HI", "note": "UI_OVERLAY_OXYGEN_NOTE"},
	"biomass": {"label": "UI_OVERLAY_BIOMASS", "lo": "UI_OVERLAY_BIOMASS_LO", "hi": "UI_OVERLAY_BIOMASS_HI", "note": "UI_OVERLAY_BIOMASS_NOTE"},
	"gill": {"label": "UI_OVERLAY_GILL", "lo": "UI_OVERLAY_GILL_LO", "hi": "UI_OVERLAY_GILL_HI", "note": "UI_OVERLAY_GILL_NOTE"},
	"containment": {"label": "UI_OVERLAY_CONTAINMENT", "lo": "UI_OVERLAY_CONTAINMENT_LO", "hi": "UI_OVERLAY_CONTAINMENT_HI", "note": "UI_OVERLAY_CONTAINMENT_NOTE"},
}

# Exposure per ContainmentLevel.ToString() from the C# sim.
const CONTAINMENT_RISK := {
	"OpenNet": 1.00,
	"Skirt": 0.80,
	"SemiClosed": 0.55,
	"ClosedFloat": 0.35,
	"Submersible": 0.45,
	"OffshoreClosed": 0.30,
}

var _sim: Node = null
var _metric: String = "lice"

var _sites: Array = []            # raw site dicts from get_all_pens()
var _intensity: Dictionary = {}   # site_id -> float 0..1
var _values: Dictionary = {}      # site_id -> display string
var _no_data: Dictionary = {}     # site_id -> true when the metric is not measured

var _cycle_btn: Button = null
var _gradient: Control = null
var _lo_label: Label = null
var _hi_label: Label = null
var _note_label: Label = null
var _list: VBoxContainer = null
var _empty_label: Label = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null(SIM_BRIDGE_PATH)
	_build_ui()
	if _sim != null and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(_on_state_updated)
	refresh()

func _on_state_updated() -> void:
	refresh()

# ── UI construction (all code; no scene edits needed by embedders) ─────────────

func _build_ui() -> void:
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 6)
	add_child(v)

	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	v.add_child(header)

	var title := Label.new()
	title.text = tr("UI_OVERLAY_TITLE")
	LakskongenTheme.set_size(title, 11)
	LakskongenTheme.set_text_color(title, COL_MUTED)
	title.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	header.add_child(title)

	_cycle_btn = Button.new()
	_cycle_btn.focus_mode = Control.FOCUS_NONE
	_cycle_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_cycle_btn.alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_cycle_btn.tooltip_text = tr("UI_OVERLAY_CYCLE_TIP")
	_cycle_btn.pressed.connect(cycle_metric)
	header.add_child(_cycle_btn)

	_gradient = _GradientBar.new()
	v.add_child(_gradient)

	var caps := HBoxContainer.new()
	v.add_child(caps)
	_lo_label = Label.new()
	LakskongenTheme.set_size(_lo_label, 11)
	LakskongenTheme.set_text_color(_lo_label, COL_MUTED)
	_lo_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	caps.add_child(_lo_label)
	_hi_label = Label.new()
	LakskongenTheme.set_size(_hi_label, 11)
	LakskongenTheme.set_text_color(_hi_label, COL_MUTED)
	_hi_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	caps.add_child(_hi_label)

	_note_label = Label.new()
	LakskongenTheme.set_size(_note_label, 11)
	LakskongenTheme.set_text_color(_note_label, COL_MUTED)
	_note_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(_note_label)

	var sep := HSeparator.new()
	v.add_child(sep)

	_empty_label = Label.new()
	_empty_label.text = tr("UI_OVERLAY_EMPTY")
	LakskongenTheme.set_size(_empty_label, 11)
	LakskongenTheme.set_text_color(_empty_label, COL_MUTED)
	_empty_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(_empty_label)

	_list = VBoxContainer.new()
	_list.add_theme_constant_override("separation", 3)
	v.add_child(_list)

# ── Public API ────────────────────────────────────────────────────────────────

func set_metric(metric_id: String) -> void:
	if not METRICS.has(metric_id):
		return
	if metric_id == _metric:
		return
	_metric = metric_id
	metric_changed.emit(_metric)
	refresh()

func get_metric() -> String:
	return _metric

func get_metric_label() -> String:
	return tr(str((METRICS.get(_metric, {}) as Dictionary).get("label", _metric)))

func cycle_metric() -> void:
	var i := METRIC_ORDER.find(_metric)
	_metric = METRIC_ORDER[(i + 1) % METRIC_ORDER.size()]
	metric_changed.emit(_metric)
	refresh()

func get_sites() -> Array:
	return _sites

func get_site_intensity(site_id: String) -> float:
	return float(_intensity.get(site_id, 0.0))

func get_site_color(site_id: String) -> Color:
	if _no_data.has(site_id):
		return COL_MUTED
	return intensity_to_color(float(_intensity.get(site_id, 0.0)))

## Average overlay intensity for every site in a zone, or -1.0 if none.
func get_zone_intensity(zone_id: String) -> float:
	if zone_id.is_empty():
		return -1.0
	var total := 0.0
	var n := 0
	for site in _sites:
		if str((site as Dictionary).get("zone", "")) != zone_id:
			continue
		var sid := str((site as Dictionary).get("id", ""))
		if _intensity.has(sid):
			total += float(_intensity[sid])
			n += 1
	return total / float(n) if n > 0 else -1.0

## Average overlay intensity of every site in a production area, or -1.0 if
## none. zone_area maps a site's zone id to its area (the expansion
## overview's zones[].production_area): the map's plates are per area, while
## sites carry zone ids such as "fjord_west" or "po04_solund".
func get_area_intensity(area: int, zone_area: Dictionary) -> float:
	if area <= 0:
		return -1.0
	var total := 0.0
	var n := 0
	for site in _sites:
		var d := site as Dictionary
		if int(zone_area.get(str(d.get("zone", "")), 0)) != area:
			continue
		var sid := str(d.get("id", ""))
		if _intensity.has(sid) and not _no_data.has(sid):
			total += float(_intensity[sid])
			n += 1
	return total / float(n) if n > 0 else -1.0

func intensity_to_color(t: float) -> Color:
	t = clampf(t, 0.0, 1.0)
	if t < 0.5:
		return COL_GREEN.lerp(COL_AMBER, t * 2.0)
	return COL_AMBER.lerp(COL_RED, (t - 0.5) * 2.0)

func refresh() -> void:
	_sites = _read_sites()
	_intensity.clear()
	_values.clear()
	_no_data.clear()
	for site in _sites:
		var sid := str((site as Dictionary).get("id", ""))
		if sid.is_empty():
			continue
		if _metric == "oxygen" and not _oxygen_measured():
			# No oxygen reading: say so instead of "0,0 mg/L" in alarm red.
			_no_data[sid] = true
			_intensity[sid] = 0.0
			_values[sid] = "–"
			continue
		_intensity[sid] = _compute_intensity(site)
		_values[sid] = _format_value(site)
	_rebuild_list()
	_update_header()
	sites_updated.emit()

# ── Data derivation ───────────────────────────────────────────────────────────

func _read_sites() -> Array:
	if _sim == null or not _sim.has_method("get_all_pens"):
		return []
	var raw = _sim.get_all_pens()
	if raw == null:
		return []
	return raw

func _sim_float(prop: String, fallback: float) -> float:
	if _sim == null:
		return fallback
	var v = _sim.get(prop)
	return float(v) if v != null else fallback

func _compute_intensity(site: Dictionary) -> float:
	match _metric:
		"lice": return _lice_intensity(site)
		"oxygen": return _oxygen_intensity(site)
		"biomass": return _biomass_intensity(site)
		"gill": return _gill_intensity(site)
		"containment": return _containment_intensity(site)
	return 0.0

func _lice_intensity(site: Dictionary) -> float:
	var lice := _sim_float("sea_lice_per_fish", 0.0)
	var t := clampf(lice / 0.5, 0.0, 1.0)
	var r := _gill_risk(str(site.get("id", "")))
	if not r.is_empty() and bool(r.get("warning", false)):
		t = maxf(t, clampf(float(r.get("risk", t)), 0.0, 1.0))
	return t

func _oxygen_measured() -> bool:
	var o2 := _sim_float("oxygen_mgl", NAN)
	return is_finite(o2) and o2 > 0.0

func _oxygen_intensity(site: Dictionary) -> float:
	var o2 := _sim_float("oxygen_mgl", 7.0)
	# 7.0 mg/L healthy -> 0.0 ; 4.0 mg/L critical -> 1.0
	var t := clampf((7.0 - o2) / 3.0, 0.0, 1.0)
	if bool(site.get("has_bubble", false)) or bool(site.get("has_diffuser", false)):
		t *= 0.55
	return t

func _biomass_intensity(site: Dictionary) -> float:
	var bio := float(site.get("biomass_kg", 0.0))
	var max_bio := float(site.get("max_biomass", 0.0))
	if max_bio <= 0.0:
		return 0.0
	return clampf(bio / max_bio, 0.0, 1.0)

func _gill_intensity(site: Dictionary) -> float:
	var r := _gill_risk(str(site.get("id", "")))
	if not r.is_empty():
		return clampf(float(r.get("risk", 0.0)), 0.0, 1.0)
	# Fallback: gill_score is 0..100, higher = healthier.
	var score := float(site.get("gill_score", 100.0))
	return clampf(1.0 - score / 100.0, 0.0, 1.0)

func _containment_intensity(site: Dictionary) -> float:
	var c := str(site.get("containment", ""))
	return float(CONTAINMENT_RISK.get(c, 0.5))

func _gill_risk(site_id: String) -> Dictionary:
	if _sim == null or site_id.is_empty() or not _sim.has_method("get_gill_risk"):
		return {}
	var r = _sim.get_gill_risk(site_id)
	return r if r is Dictionary else {}

func _format_value(site: Dictionary) -> String:
	match _metric:
		"lice":
			return SimFormatters.num(_sim_float("sea_lice_per_fish", 0.0), 2)
		"oxygen":
			return SimFormatters.num(_sim_float("oxygen_mgl", 0.0), 1) + " mg/L"
		"biomass":
			var bio := float(site.get("biomass_kg", 0.0)) / 1000.0
			var mx := float(site.get("max_biomass", 0.0)) / 1000.0
			if mx > 0.0:
				return "%s / %s t" % [SimFormatters.num(bio, 0), SimFormatters.num(mx, 0)]
			return SimFormatters.num(bio, 0) + " t"
		"gill":
			var r := _gill_risk(str(site.get("id", "")))
			if not r.is_empty():
				return SimFormatters.num(float(r.get("risk", 0.0)), 2)
			return SimFormatters.num(float(site.get("gill_score", 0.0)), 0)
		"containment":
			return SimLabels.containment(str(site.get("containment", "")))
	return ""

# ── UI refresh ────────────────────────────────────────────────────────────────

func _update_header() -> void:
	var cfg: Dictionary = METRICS.get(_metric, {})
	if _cycle_btn != null:
		_cycle_btn.text = "%s  ▸" % tr(str(cfg.get("label", _metric)))
	if _lo_label != null:
		_lo_label.text = tr(str(cfg.get("lo", "")))
	if _hi_label != null:
		_hi_label.text = tr(str(cfg.get("hi", "")))
	if _note_label != null:
		_note_label.text = tr(str(cfg.get("note", "")))
	if _gradient != null:
		_gradient.queue_redraw()

func _rebuild_list() -> void:
	if _list == null:
		return
	for child in _list.get_children():
		child.queue_free()
	if _empty_label != null:
		_empty_label.visible = _sites.is_empty()
	if _sites.is_empty():
		return
	for site in _sites:
		var d: Dictionary = site
		var sid := str(d.get("id", ""))
		if sid.is_empty() or not _intensity.has(sid):
			continue
		var t := float(_intensity[sid])
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_list.add_child(row)

		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(12, 12)
		swatch.color = COL_MUTED if _no_data.has(sid) else intensity_to_color(t)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		row.add_child(swatch)

		var name_lbl := Label.new()
		name_lbl.text = str(d.get("name", sid))
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(name_lbl, 12)
		row.add_child(name_lbl)

		var val_lbl := Label.new()
		val_lbl.text = str(_values.get(sid, ""))
		LakskongenTheme.set_size(val_lbl, 12)
		LakskongenTheme.set_text_color(val_lbl, COL_MUTED)
		row.add_child(val_lbl)

# ── Inner widgets ─────────────────────────────────────────────────────────────

## Three-stop gradient bar: green → amber → red.
class _GradientBar extends Control:
	var c1 := Color("#2E6B45")
	var c2 := Color("#8A5A00")
	var c3 := Color("#B3172F")

	func _ready() -> void:
		custom_minimum_size = Vector2(0, 10)
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	func _draw() -> void:
		var w := size.x
		var h := size.y
		if w <= 0.0 or h <= 0.0:
			return
		var steps := 32
		for i in range(steps):
			var t := float(i) / float(steps - 1)
			draw_rect(Rect2(t * w, 0.0, w / float(steps) + 1.0, h), _sample(t), true)

	func _sample(t: float) -> Color:
		if t < 0.5:
			return c1.lerp(c2, t * 2.0)
		return c2.lerp(c3, (t - 0.5) * 2.0)
