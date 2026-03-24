extends PanelContainer

## ProductionChainPanel — Anno 1800-style production chain visualization.
## Shows the full production lifecycle: Broodstock → Eggs → Fry → Smolt → Sea Fish → Harvest → Processing → Export
## Each stage displays: current count/quantity, throughput rate, bottleneck indicators.
## Color-coded: green (flowing), yellow (slow), red (bottleneck), gray (not yet unlocked).

# ── Chain stage data ──────────────────────────────────────────────────────────

var _chain_stages: Array[Dictionary] = [
	{
		"id": "broodstock",
		"label": "Stamfisk",
		"stage": "Broodstock",
		"unit": "fish",
		"threshold_green": 100,
		"threshold_yellow": 50
	},
	{
		"id": "eggs",
		"label": "Rogn",
		"stage": "Egg",
		"unit": "fish",
		"threshold_green": 10000,
		"threshold_yellow": 5000
	},
	{
		"id": "fry",
		"label": "Yngel",
		"stage": "Fry",
		"unit": "fish",
		"threshold_green": 50000,
		"threshold_yellow": 20000
	},
	{
		"id": "smolt",
		"label": "Smolt",
		"stage": "Smolt",
		"unit": "fish",
		"threshold_green": 20000,
		"threshold_yellow": 10000
	},
	{
		"id": "sea_fish",
		"label": "Sjøfisk",
		"stage": "Adult",
		"unit": "kg",
		"threshold_green": 100000,
		"threshold_yellow": 50000
	},
	{
		"id": "harvest",
		"label": "Høsting",
		"stage": "Harvested",
		"unit": "kg",
		"threshold_green": 50000,
		"threshold_yellow": 20000
	},
	{
		"id": "processing",
		"label": "Prosessering",
		"stage": "",  # Special case: processing plant
		"unit": "kg",
		"threshold_green": 30000,
		"threshold_yellow": 10000
	},
	{
		"id": "export",
		"label": "Eksport",
		"stage": "",  # Special case: export markets
		"unit": "markets",
		"threshold_green": 3,
		"threshold_yellow": 1
	}
]

# ── UI references ─────────────────────────────────────────────────────────────

@onready var _chain_container: VBoxContainer = %ChainContainer

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	if not has_node("/root/SimBridge"):
		push_warning("ProductionChainPanel: SimBridge not found — panel inactive")
		return
	
	SimBridge.state_updated.connect(_on_state_updated)
	_build_ui()
	_refresh_display()

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── UI construction ───────────────────────────────────────────────────────────

func _build_ui() -> void:
	_chain_container.queue_free()
	
	var vbox := VBoxContainer.new()
	vbox.name = "ChainContainer"
	vbox.unique_name_in_owner = true
	add_child(vbox)
	
	# Header
	var header := Label.new()
	header.text = tr("UI_PRODUCTION_TITLE")
	header.tooltip_text = tr("UI_PRODUCTION_TITLE_TOOLTIP")
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color("#2E3440"))
	vbox.add_child(header)
	
	vbox.add_child(_create_separator())
	
	# Build each chain stage row
	for i in _chain_stages.size():
		var stage_data := _chain_stages[i]
		var row := _create_stage_row(stage_data)
		row.name = "Row_" + stage_data.id
		vbox.add_child(row)
		
		# Add arrow between stages (except after last)
		if i < _chain_stages.size() - 1:
			var arrow := _create_arrow()
			vbox.add_child(arrow)

func _create_stage_row(stage_data: Dictionary) -> HBoxContainer:
	var hbox := HBoxContainer.new()
	hbox.custom_minimum_size.y = 40
	
	# Stage label
	var label := Label.new()
	label.text = stage_data.label
	label.custom_minimum_size.x = 120
	label.add_theme_font_size_override("font_size", 14)
	label.tooltip_text = tr("UI_PRODUCTION_STAGE_TOOLTIP") % stage_data.label
	hbox.add_child(label)
	
	# Quantity display
	var quantity_label := Label.new()
	quantity_label.name = "Quantity"
	quantity_label.custom_minimum_size.x = 100
	quantity_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	quantity_label.add_theme_font_size_override("font_size", 14)
	quantity_label.tooltip_text = tr("UI_PRODUCTION_QUANTITY_TOOLTIP")
	hbox.add_child(quantity_label)
	
	# Status indicator (colored box)
	var status_indicator := ColorRect.new()
	status_indicator.name = "StatusIndicator"
	status_indicator.custom_minimum_size = Vector2(20, 20)
	status_indicator.color = Color.GRAY
	status_indicator.tooltip_text = tr("UI_PRODUCTION_STATUS_TOOLTIP")
	hbox.add_child(status_indicator)
	
	# Throughput/rate label
	var throughput_label := Label.new()
	throughput_label.name = "Throughput"
	throughput_label.custom_minimum_size.x = 150
	throughput_label.add_theme_font_size_override("font_size", 12)
	throughput_label.add_theme_color_override("font_color", Color("#4C566A"))
	throughput_label.tooltip_text = tr("UI_PRODUCTION_THROUGHPUT_TOOLTIP")
	hbox.add_child(throughput_label)
	
	return hbox

func _create_arrow() -> CenterContainer:
	var center := CenterContainer.new()
	center.custom_minimum_size.y = 20
	
	var arrow_label := Label.new()
	arrow_label.text = "▼"
	arrow_label.add_theme_font_size_override("font_size", 16)
	arrow_label.add_theme_color_override("font_color", Color("#7B8794"))
	center.add_child(arrow_label)
	
	return center

func _create_separator() -> HSeparator:
	var sep := HSeparator.new()
	sep.custom_minimum_size.y = 4
	return sep

# ── Data refresh ──────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh_display()

func _refresh_display() -> void:
	if not has_node("/root/SimBridge"):
		return
	
	for stage_data in _chain_stages:
		_update_stage_row(stage_data)

func _update_stage_row(stage_data: Dictionary) -> void:
	var row_name := "Row_" + stage_data.id
	if not _chain_container.has_node(row_name):
		return
	
	var row: HBoxContainer = _chain_container.get_node(row_name)
	var quantity_label: Label = row.get_node("Quantity")
	var status_indicator: ColorRect = row.get_node("StatusIndicator")
	var throughput_label: Label = row.get_node("Throughput")
	
	var quantity: float = _get_stage_quantity(stage_data)
	var status_color: Color = _get_status_color(stage_data, quantity)
	var throughput_text: String = _get_throughput_text(stage_data)
	
	# Update quantity
	if stage_data.unit == "kg":
		quantity_label.text = SimBridge.format_biomass(quantity)
	elif stage_data.unit == "markets":
		quantity_label.text = tr("UI_PRODUCTION_MARKETS_FORMAT") % int(quantity)
	else:
		quantity_label.text = SimBridge.format_fish(int(quantity))
	
	# Update status color
	status_indicator.color = status_color
	
	# Update throughput
	throughput_label.text = throughput_text

# ── Data fetching ─────────────────────────────────────────────────────────────

func _get_stage_quantity(stage_data: Dictionary) -> float:
	match stage_data.id:
		"broodstock":
			return float(SimBridge.get_fish_count_by_stage("Broodstock"))
		"eggs":
			return float(SimBridge.get_fish_count_by_stage("Egg"))
		"fry":
			return float(SimBridge.get_fish_count_by_stage("Fry"))
		"smolt":
			return float(SimBridge.get_fish_count_by_stage("Smolt"))
		"sea_fish":
			return SimBridge.get_biomass_by_stage("Adult")
		"harvest":
			return SimBridge.get_biomass_by_stage("Harvested")
		"processing":
			return SimBridge.get_processing_throughput_kg()
		"export":
			return float(SimBridge.get_export_market_count())
	return 0.0

func _get_status_color(stage_data: Dictionary, quantity: float) -> Color:
	# Gray if not unlocked
	if not _is_stage_unlocked(stage_data):
		return Color("#D8DEE9")  # Gray
	
	# Green/yellow/red based on thresholds
	if quantity >= stage_data.threshold_green:
		return Color("#A3BE8C")  # Green (flowing)
	elif quantity >= stage_data.threshold_yellow:
		return Color("#EBCB8B")  # Yellow (slow)
	else:
		return Color("#BF616A")  # Red (bottleneck)

func _is_stage_unlocked(stage_data: Dictionary) -> bool:
	match stage_data.id:
		"broodstock", "eggs", "fry", "smolt", "sea_fish", "harvest":
			return true  # Always available in base game
		"processing":
			return SimBridge.owns_processing_plant()
		"export":
			return SimBridge.get_export_market_count() > 0
	return false

func _get_throughput_text(stage_data: Dictionary) -> String:
	match stage_data.id:
		"broodstock":
			var eggs := SimBridge.get_fish_count_by_stage("Egg")
			if eggs > 0:
				return tr("UI_PRODUCTION_STATUS_PRODUCING")
			return tr("UI_PRODUCTION_STATUS_IDLE")
		"eggs":
			var fry := SimBridge.get_fish_count_by_stage("Fry")
			if fry > 0:
				return tr("UI_PRODUCTION_STATUS_HATCHING")
			return tr("UI_PRODUCTION_STATUS_WAITING_HATCH")
		"fry":
			var smolt := SimBridge.get_fish_count_by_stage("Smolt")
			if smolt > 0:
				return tr("UI_PRODUCTION_STATUS_SMOLT_ACTIVE")
			return tr("UI_PRODUCTION_STATUS_SMOLT_WAIT")
		"smolt":
			var adult_biomass := SimBridge.get_biomass_by_stage("Adult")
			if adult_biomass > 10000:
				return tr("UI_PRODUCTION_STATUS_AT_SEA_GROWING")
			elif adult_biomass > 0:
				return tr("UI_PRODUCTION_STATUS_RECENTLY_RELEASED")
			return tr("UI_PRODUCTION_STATUS_WAITING_RELEASE")
		"sea_fish":
			var harvested := SimBridge.get_biomass_by_stage("Harvested")
			if harvested > 0:
				return tr("UI_PRODUCTION_STATUS_HARVESTING")
			return tr("UI_PRODUCTION_STATUS_GROWING_SEA")
		"harvest":
			var throughput := SimBridge.get_processing_throughput_kg()
			if throughput > 1000:
				return tr("UI_PRODUCTION_STATUS_PROCESSING_KG") % int(throughput)
			return tr("UI_PRODUCTION_STATUS_WAITING_PROCESSING")
		"processing":
			if SimBridge.owns_processing_plant():
				var markets := SimBridge.get_export_market_count()
				if markets > 0:
					return tr("UI_PRODUCTION_STATUS_SENDING_MARKETS") % markets
				return tr("UI_PRODUCTION_STATUS_READY_EXPORT")
			return tr("UI_PRODUCTION_STATUS_NOT_BUILT")
		"export":
			var markets := SimBridge.get_export_market_count()
			if markets > 0:
				return tr("UI_PRODUCTION_STATUS_ACTIVE_EXPORTS")
			return tr("UI_PRODUCTION_STATUS_NO_MARKETS")
	return ""
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
