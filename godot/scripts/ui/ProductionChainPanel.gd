extends Control

## ProductionChainPanel — Anno 1800-style production chain visualization.
## Shows: Broodstock → Eggs → Fry → Smolt → Sea Fish → Harvest → Processing → Export
## Each stage: count/quantity, color-coded status (green/yellow/red/gray).

# ── Stage definitions ─────────────────────────────────────────────────────────

const STAGES := [
	{ "id": "broodstock", "label": "Stamfisk", "stage": "Broodstock", "icon": "🐟", "unit": "stk" },
	{ "id": "eggs",       "label": "Egg",       "stage": "Egg",        "icon": "🥚", "unit": "stk" },
	{ "id": "fry",        "label": "Yngel",     "stage": "Fry",        "icon": "🐠", "unit": "stk" },
	{ "id": "smolt",      "label": "Smolt",     "stage": "Smolt",      "icon": "🐟", "unit": "stk" },
	{ "id": "sea_fish",   "label": "Sjøfisk",   "stage": "Adult",      "icon": "🐟", "unit": "kg" },
	{ "id": "harvest",    "label": "Høsting",    "stage": "Harvested",  "icon": "🎣", "unit": "kg" },
	{ "id": "processing", "label": "Foredling",  "stage": "",           "icon": "🏭", "unit": "kg" },
	{ "id": "export",     "label": "Eksport",    "stage": "",           "icon": "📦", "unit": "markeder" },
]

# ── Node refs ─────────────────────────────────────────────────────────────────

var _sim: Node = null
var _stage_labels: Dictionary = {}  # id -> { value_label, status_label }
var _container: VBoxContainer = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("ProductionChainPanel: SimBridge not found")
	
	_build_ui()
	_refresh()
	print("ProductionChainPanel: ready")

func _build_ui() -> void:
	# Find or create container
	if has_node("ScrollContainer/VBoxContainer"):
		_container = get_node("ScrollContainer/VBoxContainer")
	else:
		var scroll = ScrollContainer.new()
		scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
		add_child(scroll)
		_container = VBoxContainer.new()
		scroll.add_child(_container)
	
	# Title
	var title = Label.new()
	title.text = "🔗 Produksjonskjede"
	title.add_theme_font_size_override("font_size", 20)
	_container.add_child(title)
	
	var subtitle = Label.new()
	subtitle.text = "Fra egg til eksport"
	subtitle.add_theme_color_override("font_color", Color.GRAY)
	_container.add_child(subtitle)
	
	var sep = HSeparator.new()
	_container.add_child(sep)
	
	# Build each stage row
	for i in range(STAGES.size()):
		var stage = STAGES[i]
		
		# Arrow between stages (skip first)
		if i > 0:
			var arrow = Label.new()
			arrow.text = "    ↓"
			arrow.add_theme_color_override("font_color", Color(0.5, 0.5, 0.5))
			_container.add_child(arrow)
		
		var row = HBoxContainer.new()
		row.name = "Stage_%s" % stage["id"]
		_container.add_child(row)
		
		# Icon + label
		var name_lbl = Label.new()
		name_lbl.text = "%s %s" % [stage["icon"], stage["label"]]
		name_lbl.custom_minimum_size.x = 140
		row.add_child(name_lbl)
		
		# Value
		var value_lbl = Label.new()
		value_lbl.text = "--"
		value_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		value_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(value_lbl)
		
		# Status indicator
		var status_lbl = Label.new()
		status_lbl.text = "●"
		status_lbl.custom_minimum_size.x = 30
		status_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		row.add_child(status_lbl)
		
		_stage_labels[stage["id"]] = {
			"value": value_lbl,
			"status": status_lbl,
			"name": name_lbl
		}

# ── Refresh ───────────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh()

func _refresh() -> void:
	if not _sim:
		return
	
	for stage in STAGES:
		var sid: String = stage["id"]
		if not _stage_labels.has(sid):
			continue
		
		var labels: Dictionary = _stage_labels[sid]
		var value_lbl: Label = labels["value"]
		var status_lbl: Label = labels["status"]
		var count: int = 0
		var biomass: float = 0.0
		var display_text: String = "--"
		var status_color: Color = Color.GRAY
		
		match sid:
			"broodstock":
				count = _sim.get_fish_count_by_stage("Broodstock")
				display_text = "%s stk" % _format_number(count)
				status_color = Color.GREEN if count > 0 else Color.GRAY
			"eggs":
				count = _sim.get_fish_count_by_stage("Egg")
				display_text = "%s stk" % _format_number(count)
				status_color = _flow_color(count, 1000, 10000)
			"fry":
				count = _sim.get_fish_count_by_stage("Fry")
				display_text = "%s stk" % _format_number(count)
				status_color = _flow_color(count, 500, 5000)
			"smolt":
				count = _sim.get_fish_count_by_stage("Smolt")
				display_text = "%s stk" % _format_number(count)
				status_color = _flow_color(count, 100, 2000)
			"sea_fish":
				count = _sim.get_fish_count_by_stage("Adult")
				biomass = _sim.get_biomass_by_stage("Adult")
				display_text = "%s stk (%.1f t)" % [_format_number(count), biomass / 1000.0]
				status_color = _flow_color(count, 100, 5000)
			"harvest":
				count = _sim.get_fish_count_by_stage("Harvested")
				biomass = _sim.get_biomass_by_stage("Harvested")
				display_text = "%.1f tonn" % (biomass / 1000.0)
				status_color = _flow_color(int(biomass), 100, 5000)
			"processing":
				if _sim.owns_processing_plant():
					var throughput: float = _sim.get_processing_throughput_kg()
					display_text = "%.1f tonn" % (throughput / 1000.0)
					status_color = Color.GREEN if throughput > 0 else Color.YELLOW
				else:
					display_text = "Ikke eid"
					status_color = Color.GRAY
			"export":
				var markets: int = _sim.get_export_market_count()
				display_text = "%d markeder" % markets
				status_color = Color.GREEN if markets > 0 else Color.GRAY
		
		value_lbl.text = display_text
		status_lbl.add_theme_color_override("font_color", status_color)

# ── Helpers ───────────────────────────────────────────────────────────────────

## Color based on flow: gray (empty), red (bottleneck/low), yellow (moderate), green (flowing)
func _flow_color(value: int, low_threshold: int, good_threshold: int) -> Color:
	if value <= 0:
		return Color.GRAY
	elif value < low_threshold:
		return Color.RED
	elif value < good_threshold:
		return Color.YELLOW
	else:
		return Color.GREEN

## Format numbers with thousand separators
func _format_number(n: int) -> String:
	var s: String = str(n)
	if n < 1000:
		return s
	var result: String = ""
	var count: int = 0
	for i in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = " " + result
		result = s[i] + result
		count += 1
	return result
