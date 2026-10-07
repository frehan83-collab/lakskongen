extends PanelContainer

## ProductionChainPanel v2.0 — Anno 1800-style production chain visualization (scene-based).
## Layout lives in scenes/ui/ProductionChainPanel.tscn; this script is wiring only:
## SimBridge hookups, localized labels/tooltips, stage refresh.
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

# ── UI references (declared in ProductionChainPanel.tscn) ─────────────────────

@onready var _chain_container: VBoxContainer = %ChainContainer
@onready var _header_label: Label = %HeaderLabel

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	if not has_node("/root/SimBridge"):
		push_warning("ProductionChainPanel: SimBridge not found — panel inactive")
		return

	SimBridge.state_updated.connect(_on_state_updated)
	_apply_localization()
	_refresh_display()

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Localization (static texts live in the .tscn) ─────────────────────────────

func _apply_localization() -> void:
	_header_label.text = tr("UI_PRODUCTION_TITLE")
	_header_label.tooltip_text = tr("UI_PRODUCTION_TITLE_TOOLTIP")
	for stage_data in _chain_stages:
		var row: HBoxContainer = _chain_container.get_node_or_null("Row_" + str(stage_data.id))
		if row == null:
			continue
		var stage_label: Label = row.get_node("StageLabel")
		stage_label.text = stage_data.label
		stage_label.tooltip_text = tr("UI_PRODUCTION_STAGE_TOOLTIP") % stage_data.label
		row.get_node("Quantity").tooltip_text = tr("UI_PRODUCTION_QUANTITY_TOOLTIP")
		row.get_node("StatusIndicator").tooltip_text = tr("UI_PRODUCTION_STATUS_TOOLTIP")
		row.get_node("Throughput").tooltip_text = tr("UI_PRODUCTION_THROUGHPUT_TOOLTIP")

# ── Data refresh ──────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh_display()

func _refresh_display() -> void:
	if not has_node("/root/SimBridge"):
		return

	for stage_data in _chain_stages:
		_update_stage_row(stage_data)

func _update_stage_row(stage_data: Dictionary) -> void:
	var row_name: String = "Row_" + str(stage_data.id)
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
