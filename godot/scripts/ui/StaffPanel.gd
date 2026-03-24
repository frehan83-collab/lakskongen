extends Control

signal hire_requested()
signal fire_requested(staff_name: String)
signal training_requested()

var staff_list_container: VBoxContainer
var total_salary_label: Label
var employee_count_label: Label

func _ready():
	# Main VBoxContainer for the panel
	var main_vbox = VBoxContainer.new()
	add_child(main_vbox)
	main_vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	main_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)

	# Title
	var title_label = LakskongenTheme.create_label("PERSONALE", LakskongenTheme.FONT_XL, LakskongenTheme.ACCENT)
	main_vbox.add_child(title_label)

	# Overview Section
	var overview_hbox = HBoxContainer.new()
	main_vbox.add_child(overview_hbox)
	employee_count_label = LakskongenTheme.create_label("Ansatte: 0", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	overview_hbox.add_child(employee_count_label)
	overview_hbox.add_child(Control.new()) # Spacer
	total_salary_label = LakskongenTheme.create_label("Lønnskostnad: 0 NOK/mnd", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	overview_hbox.add_child(total_salary_label)

	# Staff List (ScrollContainer)
	var scroll_container = ScrollContainer.new()
	scroll_container.set_v_size_flags(Control.SIZE_EXPAND_FILL)
	main_vbox.add_child(scroll_container)

	staff_list_container = VBoxContainer.new()
	staff_list_container.add_theme_constant_override("separation", LakskongenTheme.SPACING_XS)
	scroll_container.add_child(staff_list_container)
	staff_list_container.set_h_size_flags(Control.SIZE_EXPAND_FILL)

	# Bottom Section
	var bottom_hbox = HBoxContainer.new()
	main_vbox.add_child(bottom_hbox)

	var hire_button = LakskongenTheme.create_button("ANSETT NY", LakskongenTheme.FONT_MD, LakskongenTheme.ACCENT)
	hire_button.pressed.connect(func(): hire_requested.emit())
	bottom_hbox.add_child(hire_button)

	var training_button = LakskongenTheme.create_button("OPPLÆRING", LakskongenTheme.FONT_MD, LakskongenTheme.ACCENT)
	training_button.pressed.connect(func(): training_requested.emit())
	bottom_hbox.add_child(training_button)

func update_staff(staff_list: Array):
	# Clear existing staff rows
	for child in staff_list_container.get_children():
		child.queue_free()

	var total_salary = 0
	for staff in staff_list:
		var staff_row = _create_staff_row(staff)
		staff_list_container.add_child(staff_row)
		total_salary += staff.salary_monthly

	employee_count_label.text = "Ansatte: %d" % staff_list.size()
	total_salary_label.text = "Total lønnskostnad: %s NOK/mnd" % LakskongenTheme.format_currency(total_salary)

func _create_staff_row(staff: Dictionary) -> HBoxContainer:
	var hbox = HBoxContainer.new()
	hbox.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	hbox.add_theme_constant_override("separation", LakskongenTheme.SPACING_SM)

	# Name
	var name_label = LakskongenTheme.create_label(staff.name, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	name_label.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	hbox.add_child(name_label)

	# Role
	var role_label = LakskongenTheme.create_label(staff.role, LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
	role_label.set_h_size_flags(Control.SIZE_EXPAND)
	hbox.add_child(role_label)

	# Skill Level
	var skill_level_text = ""
	for i in range(staff.skill_level):
		skill_level_text += "⭐"
	var skill_label = LakskongenTheme.create_label(skill_level_text, LakskongenTheme.FONT_SM, LakskongenTheme.TEXT)
	skill_label.set_h_size_flags(Control.SIZE_EXPAND)
	hbox.add_child(skill_label)

	# Morale (as a bar, using a custom drawing control)
	var morale_bar = Control.new()
	morale_bar.custom_minimum_size = Vector2(80, LakskongenTheme.FONT_SM.get_height())
	morale_bar.set_h_size_flags(Control.SIZE_EXPAND)
	morale_bar.set_v_size_flags(Control.SIZE_SHRINK_CENTER) # Center vertically
	hbox.add_child(morale_bar)

	morale_bar.add_child(Label.new()) # Dummy label to make it visible
	morale_bar.get_child(0).text = "Moral: %d" % staff.morale
	morale_bar.get_child(0).add_theme_font_size_override("font_size", LakskongenTheme.FONT_SM.get_height())
	morale_bar.get_child(0).add_theme_color_override("font_color", LakskongenTheme.TEXT)

	morale_bar.set_draw_callback(func():
		var rect = morale_bar.get_rect()
		var morale_color = LakskongenTheme.COLOR_GREEN
		if staff.morale < 40:
			morale_color = LakskongenTheme.COLOR_RED
		elif staff.morale < 70:
			morale_color = LakskongenTheme.COLOR_YELLOW

		var fill_width = rect.size.x * (float(staff.morale) / 100.0)
		morale_bar.draw_rect(Rect2(0, 0, rect.size.x, rect.size.y), LakskongenTheme.COLOR_BACKGROUND_DARK, false) # Background
		morale_bar.draw_rect(Rect2(0, 0, fill_width, rect.size.y), morale_color) # Fill
		morale_bar.draw_string(LakskongenTheme.FONT_SM, Vector2(5, LakskongenTheme.FONT_SM.get_ascent()), "Moral: %d" % staff.morale, HORIZONTAL_ALIGNMENT_LEFT, -1, LakskongenTheme.FONT_SM.get_height(), LakskongenTheme.TEXT)
	)

	# Salary
	var salary_label = LakskongenTheme.create_label(LakskongenTheme.format_currency(staff.salary_monthly) + " NOK", LakskongenTheme.FONT_SM, LakskongenTheme.TEXT)
	salary_label.set_h_size_flags(Control.SIZE_SHRINK_CENTER)
	hbox.add_child(salary_label)

	# Fire Button
	var fire_button = LakskongenTheme.create_button("SPARKE", LakskongenTheme.FONT_SM, LakskongenTheme.DANGER)
	fire_button.pressed.connect(func(): fire_requested.emit(staff.name))
	hbox.add_child(fire_button)

	return hbox

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
