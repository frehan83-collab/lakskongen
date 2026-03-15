## CrewPanel.gd
## Crew roster management panel showing hired workers, their roles, salaries, and skill levels.
## Wired to GameBridge crew APIs for live data display.

extends PanelContainer
class_name CrewPanel

# UI References
@onready var crew_list_container: VBoxContainer
@onready var total_payroll_label: Label
@onready var crew_count_label: Label

# Hire section
@onready var hire_button_container: HBoxContainer

# Color scheme
const COLOR_TEXT = "#E6EEF3"
const COLOR_MUTED = "#9AA7B2"
const COLOR_ACCENT = "#2AB7B5"
const COLOR_DANGER = "#E06666"

signal worker_hired(role: String)
signal worker_fired(worker_id: String)

func _ready():
	# Create UI elements if not assigned
	if not crew_list_container:
		_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		sim_bridge.state_updated.connect(_on_state_updated)
		print("CrewPanel: connected to SimBridge")
	else:
		push_warning("CrewPanel: SimBridge not found")
	
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
	"""Dynamically create UI elements if not set in editor"""
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	margin.add_child(vbox)
	
	# Header
	var header = Label.new()
	header.text = "👷 Crew Management"
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	header.tooltip_text = "Administrer ansatte: roller, lønn, ferdigheter og ansettelse/oppsigelse."
	vbox.add_child(header)
	
	# Summary section
	var summary_container = HBoxContainer.new()
	summary_container.add_theme_constant_override("separation", 24)
	vbox.add_child(summary_container)
	
	# Crew count
	var count_container = HBoxContainer.new()
	count_container.add_theme_constant_override("separation", 8)
	summary_container.add_child(count_container)
	
	var count_label = Label.new()
	count_label.text = "Total Workers:"
	count_label.add_theme_font_size_override("font_size", 14)
	count_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	count_container.add_child(count_label)
	
	crew_count_label = Label.new()
	crew_count_label.text = "0"
	crew_count_label.add_theme_font_size_override("font_size", 14)
	crew_count_label.add_theme_color_override("font_color", Color(COLOR_ACCENT))
	count_container.add_child(crew_count_label)
	
	# Total payroll
	var payroll_container = HBoxContainer.new()
	payroll_container.add_theme_constant_override("separation", 8)
	summary_container.add_child(payroll_container)
	
	var payroll_label = Label.new()
	payroll_label.text = "Total Payroll:"
	payroll_label.add_theme_font_size_override("font_size", 14)
	payroll_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	payroll_container.add_child(payroll_label)
	
	total_payroll_label = Label.new()
	total_payroll_label.text = "0 NOK"
	total_payroll_label.add_theme_font_size_override("font_size", 14)
	total_payroll_label.add_theme_color_override("font_color", Color(COLOR_DANGER))
	payroll_container.add_child(total_payroll_label)
	
	# Separator
	var separator1 = HSeparator.new()
	vbox.add_child(separator1)
	
	# Hire section
	var hire_header = Label.new()
	hire_header.text = "Hire Workers"
	hire_header.add_theme_font_size_override("font_size", 16)
	hire_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(hire_header)
	
	hire_button_container = HBoxContainer.new()
	hire_button_container.add_theme_constant_override("separation", 12)
	vbox.add_child(hire_button_container)
	
	# Create hire buttons for each role
	var roles = ["manager", "technician", "veterinarian", "diver", "feeder"]
	var role_labels = {
		"manager": "Manager",
		"technician": "Technician",
		"veterinarian": "Veterinarian",
		"diver": "Diver",
		"feeder": "Feeder"
	}
	
	for role in roles:
		var btn = Button.new()
		btn.text = "Hire %s" % role_labels[role]
		btn.pressed.connect(_on_hire_pressed.bind(role))
		hire_button_container.add_child(btn)
	
	# Separator
	var separator2 = HSeparator.new()
	vbox.add_child(separator2)
	
	# Crew list header
	var crew_header = Label.new()
	crew_header.text = "Current Crew Roster"
	crew_header.add_theme_font_size_override("font_size", 16)
	crew_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(crew_header)
	
	# Crew list container
	crew_list_container = VBoxContainer.new()
	crew_list_container.add_theme_constant_override("separation", 8)
	vbox.add_child(crew_list_container)

func _on_state_updated():
	"""Called when SimBridge state updates"""
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Get crew list
	var crew_list = sim_bridge.get_crew_list()
	
	# Update summary
	if crew_count_label:
		crew_count_label.text = str(crew_list.size())
	
	# Calculate total payroll
	var total_payroll = 0.0
	for worker in crew_list:
		total_payroll += worker.get("salary_nok", 0.0)
	
	if total_payroll_label:
		total_payroll_label.text = _format_nok(total_payroll) + " / day"
	
	# Update crew list display
	_update_crew_list_display(crew_list)

func _update_crew_list_display(crew_list: Array):
	"""Update crew roster display"""
	if not crew_list_container:
		return
	
	# Clear existing entries
	for child in crew_list_container.get_children():
		child.queue_free()
	
	if crew_list.is_empty():
		var empty_label = Label.new()
		empty_label.text = "No workers hired yet"
		empty_label.add_theme_font_size_override("font_size", 12)
		empty_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		crew_list_container.add_child(empty_label)
		return
	
	# Create crew entry for each worker
	for worker in crew_list:
		_create_crew_entry(worker)

func _create_crew_entry(worker: Dictionary):
	"""Create a single crew member entry"""
	var panel = PanelContainer.new()
	crew_list_container.add_child(panel)
	
	var hbox = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 16)
	panel.add_child(hbox)
	
	# Name and role
	var info_vbox = VBoxContainer.new()
	info_vbox.add_theme_constant_override("separation", 4)
	info_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)
	
	var name_label = Label.new()
	name_label.text = worker.get("name", "Unknown")
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
	info_vbox.add_child(name_label)
	
	var role_label = Label.new()
	var role = worker.get("role", "Worker")
	role_label.text = _format_role(role)
	role_label.add_theme_font_size_override("font_size", 12)
	role_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	info_vbox.add_child(role_label)
	
	# Skill level
	var skill_container = VBoxContainer.new()
	skill_container.add_theme_constant_override("separation", 4)
	hbox.add_child(skill_container)
	
	var skill_label = Label.new()
	skill_label.text = "Skill"
	skill_label.add_theme_font_size_override("font_size", 11)
	skill_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	skill_container.add_child(skill_label)
	
	var skill_value = Label.new()
	var skill_level = worker.get("skill_level", 0)
	skill_value.text = str(skill_level)
	skill_value.add_theme_font_size_override("font_size", 14)
	skill_value.add_theme_color_override("font_color", _get_skill_color(skill_level))
	skill_container.add_child(skill_value)
	
	# Salary
	var salary_container = VBoxContainer.new()
	salary_container.add_theme_constant_override("separation", 4)
	hbox.add_child(salary_container)
	
	var salary_label = Label.new()
	salary_label.text = "Salary"
	salary_label.add_theme_font_size_override("font_size", 11)
	salary_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	salary_container.add_child(salary_label)
	
	var salary_value = Label.new()
	var salary = worker.get("salary_nok", 0.0)
	salary_value.text = _format_nok_short(salary) + "/day"
	salary_value.add_theme_font_size_override("font_size", 14)
	salary_value.add_theme_color_override("font_color", Color(COLOR_DANGER))
	salary_container.add_child(salary_value)
	
	# Hire date
	var date_container = VBoxContainer.new()
	date_container.add_theme_constant_override("separation", 4)
	hbox.add_child(date_container)
	
	var date_label = Label.new()
	date_label.text = "Hired"
	date_label.add_theme_font_size_override("font_size", 11)
	date_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	date_container.add_child(date_label)
	
	var date_value = Label.new()
	var hire_date = worker.get("hire_date", 0)
	date_value.text = "Day %d" % hire_date
	date_value.add_theme_font_size_override("font_size", 12)
	date_value.add_theme_color_override("font_color", Color(COLOR_MUTED))
	date_container.add_child(date_value)
	
	# Fire button
	var fire_btn = Button.new()
	fire_btn.text = "Fire"
	fire_btn.custom_minimum_size = Vector2(80, 0)
	fire_btn.pressed.connect(_on_fire_pressed.bind(worker.get("id", "")))
	hbox.add_child(fire_btn)

func _on_hire_pressed(role: String):
	"""Handle hire button press"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	var success = sim_bridge.hire_worker(role)
	
	if success:
		print("CrewPanel: Hired %s" % role)
		emit_signal("worker_hired", role)
	else:
		print("CrewPanel: Failed to hire %s (insufficient funds)" % role)

func _on_fire_pressed(worker_id: String):
	"""Handle fire button press"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	var success = sim_bridge.fire_worker(worker_id)
	
	if success:
		print("CrewPanel: Fired worker %s" % worker_id)
		emit_signal("worker_fired", worker_id)
	else:
		print("CrewPanel: Failed to fire worker %s" % worker_id)

func _format_role(role: String) -> String:
	"""Format role name for display"""
	return role.capitalize()

func _get_skill_color(skill: int) -> Color:
	"""Get color based on skill level"""
	if skill >= 8:
		return Color(0.3, 0.9, 0.3)  # Excellent
	elif skill >= 5:
		return Color(0.9, 0.8, 0.2)  # Good
	else:
		return Color(0.9, 0.5, 0.3)  # Average

func _format_nok(amount: float) -> String:
	"""Format NOK amount with k suffix"""
	if amount >= 1_000.0:
		return "%.1f kNOK" % (amount / 1_000.0)
	return "%.0f NOK" % amount

func _format_nok_short(amount: float) -> String:
	"""Format NOK amount (short version)"""
	if amount >= 1_000.0:
		return "%.1fk" % (amount / 1_000.0)
	return "%.0f" % amount
