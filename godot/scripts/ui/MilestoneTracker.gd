extends PanelContainer
class_name MilestoneTracker

## Tracks production‑goal milestones and shows unlock notifications.
## Also tracks seed mastery XP across three domains.

const PANEL := LakskongenTheme.PANEL
const PANEL_ALT := LakskongenTheme.PANEL_ALT
const TEXT := LakskongenTheme.TEXT
const MUTED := LakskongenTheme.MUTED
const ACCENT := LakskongenTheme.ACCENT
const KELP := LakskongenTheme.KELP
const GOLD := LakskongenTheme.GOLD
const AURORA_PURPLE := LakskongenTheme.AURORA_PURPLE
const AURORA_GREEN := LakskongenTheme.AURORA_GREEN

enum MasteryDomain {
	BIOLOGY,
	BUSINESS,
	SUSTAINABILITY
}

var mastery_xp := {
	MasteryDomain.BIOLOGY: 0,
	MasteryDomain.BUSINESS: 0,
	MasteryDomain.SUSTAINABILITY: 0
}

var active_goals := []  # Array of ProductionGoal objects (C# instances)
var milestone_history := []  # Array of dictionaries {name: String, value: float, day: int}

@onready var goals_container := VBoxContainer.new()
@onready var milestones_container := VBoxContainer.new()
@onready var mastery_container := HBoxContainer.new()

func _ready():
	_setup_panel_style()
	
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_LG)
	add_child(vbox)
	
	# Header
	var header := LakskongenTheme.create_section_header("Milestone Tracker")
	vbox.add_child(header)
	
	# Mastery XP summary
	vbox.add_child(_create_mastery_summary())
	
	vbox.add_child(LakskongenTheme.create_divider())
	
	# Active goals section
	var goals_header := LakskongenTheme.create_label("Active Goals", LakskongenTheme.FONT_LG, ACCENT)
	vbox.add_child(goals_header)
	vbox.add_child(goals_container)
	
	# Milestone history section
	var milestones_header := LakskongenTheme.create_label("Recent Milestones", LakskongenTheme.FONT_LG, ACCENT)
	vbox.add_child(milestones_header)
	vbox.add_child(milestones_container)
	
	# Populate with dummy data (for testing)
	_add_test_data()

func _setup_panel_style():
	var style := StyleBoxFlat.new()
	style.bg_color = PANEL
	style.corner_radius_top_left = LakskongenTheme.RADIUS_MD
	style.corner_radius_top_right = LakskongenTheme.RADIUS_MD
	style.corner_radius_bottom_left = LakskongenTheme.RADIUS_MD
	style.corner_radius_bottom_right = LakskongenTheme.RADIUS_MD
	style.content_margin_left = LakskongenTheme.SPACE_MD
	style.content_margin_right = LakskongenTheme.SPACE_MD
	style.content_margin_top = LakskongenTheme.SPACE_MD
	style.content_margin_bottom = LakskongenTheme.SPACE_MD
	add_theme_stylebox_override("panel", style)

func _create_mastery_summary() -> HBoxContainer:
	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XL)
	hbox.alignment = HBoxContainer.ALIGNMENT_CENTER
	
	var domains := [
		["Biology", MasteryDomain.BIOLOGY, AURORA_GREEN],
		["Business", MasteryDomain.BUSINESS, GOLD],
		["Sustainability", MasteryDomain.SUSTAINABILITY, KELP]
	]
	
	for domain in domains:
		var vbox := VBoxContainer.new()
		vbox.alignment = BoxContainer.ALIGNMENT_CENTER
		vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
		
		var icon := ColorRect.new()
		icon.custom_minimum_size = Vector2(48, 48)
		icon.color = domain[2]
		
		var label := LakskongenTheme.create_label(domain[0], LakskongenTheme.FONT_SM, TEXT)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		
		var xp_label := LakskongenTheme.create_label(str(mastery_xp[domain[1]]), LakskongenTheme.FONT_MD, domain[2])
		xp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		
		vbox.add_child(icon)
		vbox.add_child(label)
		vbox.add_child(xp_label)
		hbox.add_child(vbox)
	
	return hbox

## Add a ProductionGoal to track (C# instance).
func add_goal(goal: ProductionGoal):
	if active_goals.has(goal):
		return
	active_goals.append(goal)
	goal.MilestoneReached.connect(_on_milestone_reached.bind(goal))
	goal.GoalCompleted.connect(_on_goal_completed.bind(goal))
	_update_goals_display()

## Remove a goal (when completed or cancelled).
func remove_goal(goal: ProductionGoal):
	active_goals.erase(goal)
	_update_goals_display()

## Add XP to a mastery domain.
func add_mastery_xp(domain: MasteryDomain, xp: int):
	mastery_xp[domain] += xp
	_update_mastery_display()
	_show_notification("+%d %s XP" % [xp, _domain_name(domain)])

## Called when a milestone is reached.
func _on_milestone_reached(milestone_name: String, milestone_value: float, goal: ProductionGoal):
	var entry := {
		"name": milestone_name,
		"value": milestone_value,
		"day": GameState.CurrentDay if GameState else 0,
		"goal": goal.Description
	}
	milestone_history.push_front(entry)
	if milestone_history.size() > 5:
		milestone_history.pop_back()
	
	_update_milestones_display()
	_show_notification("Milestone: %s – %s" % [goal.Description, milestone_name])
	
	# Award mastery XP based on milestone significance
	var xp := int(milestone_value / 1000.0) + 10  # Simple formula
	add_mastery_xp(MasteryDomain.BUSINESS, xp)

## Called when a goal is fully completed.
func _on_goal_completed(goal: ProductionGoal):
	_show_notification("Goal Completed: %s" % goal.Description)
	# Award larger XP
	add_mastery_xp(MasteryDomain.BUSINESS, 50)
	# Optionally unlock research branches (handled elsewhere)
	# UnlockResearchBranch(goal)

func _update_goals_display():
	_clear_container(goals_container)
	
	for goal in active_goals:
		var hbox := HBoxContainer.new()
		hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
		
		var desc_label := LakskongenTheme.create_label(goal.Description, LakskongenTheme.FONT_SM, TEXT)
		desc_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hbox.add_child(desc_label)
		
		var progress_label := LakskongenTheme.create_label("%.0f%%" % goal.GetProgressPercent(), LakskongenTheme.FONT_SM, ACCENT)
		hbox.add_child(progress_label)
		
		goals_container.add_child(hbox)
	
	if active_goals.is_empty():
		var empty_label := LakskongenTheme.create_label("No active goals", LakskongenTheme.FONT_SM, MUTED)
		goals_container.add_child(empty_label)

func _update_milestones_display():
	_clear_container(milestones_container)
	
	for entry in milestone_history:
		var hbox := HBoxContainer.new()
		hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
		
		var day_str := "Day %d" % entry.get("day", 0)
		var day_label := LakskongenTheme.create_label(day_str, LakskongenTheme.FONT_XS, MUTED)
		day_label.custom_minimum_size.x = 60
		hbox.add_child(day_label)
		
		var text := "%s: %s" % [entry.get("goal", ""), entry.get("name", "")]
		var text_label := LakskongenTheme.create_label(text, LakskongenTheme.FONT_SM, TEXT)
		text_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		hbox.add_child(text_label)
		
		milestones_container.add_child(hbox)
	
	if milestone_history.is_empty():
		var empty_label := LakskongenTheme.create_label("No milestones yet", LakskongenTheme.FONT_SM, MUTED)
		milestones_container.add_child(empty_label)

func _update_mastery_display():
	var hbox := mastery_container
	if not hbox:
		return
	for child in hbox.get_children():
		child.queue_free()
	
	var domains := [
		["Biology", MasteryDomain.BIOLOGY, AURORA_GREEN],
		["Business", MasteryDomain.BUSINESS, GOLD],
		["Sustainability", MasteryDomain.SUSTAINABILITY, KELP]
	]
	for domain in domains:
		var vbox := VBoxContainer.new()
		vbox.alignment = BoxContainer.ALIGNMENT_CENTER
		vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
		
		var icon := ColorRect.new()
		icon.custom_minimum_size = Vector2(48, 48)
		icon.color = domain[2]
		
		var label := LakskongenTheme.create_label(domain[0], LakskongenTheme.FONT_SM, TEXT)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		
		var xp_label := LakskongenTheme.create_label(str(mastery_xp[domain[1]]), LakskongenTheme.FONT_MD, domain[2])
		xp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		
		vbox.add_child(icon)
		vbox.add_child(label)
		vbox.add_child(xp_label)
		hbox.add_child(vbox)

func _clear_container(container: Container):
	for child in container.get_children():
		child.queue_free()

func _show_notification(text: String):
	# Simple notification using a temporary label (could be replaced with a proper notification system)
	var notif := Label.new()
	notif.text = text
	notif.add_theme_font_size_override("font_size", LakskongenTheme.FONT_SM)
	notif.add_theme_color_override("font_color", ACCENT)
	notif.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	notif.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = PANEL_ALT
	style.corner_radius_top_left = LakskongenTheme.RADIUS_SM
	style.corner_radius_top_right = LakskongenTheme.RADIUS_SM
	style.corner_radius_bottom_left = LakskongenTheme.RADIUS_SM
	style.corner_radius_bottom_right = LakskongenTheme.RADIUS_SM
	style.content_margin_left = LakskongenTheme.SPACE_MD
	style.content_margin_right = LakskongenTheme.SPACE_MD
	style.content_margin_top = LakskongenTheme.SPACE_SM
	style.content_margin_bottom = LakskongenTheme.SPACE_SM
	panel.add_theme_stylebox_override("panel", style)
	panel.add_child(notif)
	
	add_child(panel)
	
	# Auto‑remove after 5 seconds
	var timer := get_tree().create_timer(5.0)
	timer.timeout.connect(panel.queue_free)

func _domain_name(domain: MasteryDomain) -> String:
	match domain:
		MasteryDomain.BIOLOGY: return "Biology"
		MasteryDomain.BUSINESS: return "Business"
		MasteryDomain.SUSTAINABILITY: return "Sustainability"
		_: return "Unknown"

func _connect_signals():
	GameState.connect("production_goal_created", Callable(self, "add_goal"))

# func _add_test_data():
	# Only for demo purposes – remove in production
	mastery_xp[MasteryDomain.BIOLOGY] = 125
	mastery_xp[MasteryDomain.BUSINESS] = 340
	mastery_xp[MasteryDomain.SUSTAINABILITY] = 80
	_update_mastery_display()
	
	milestone_history = [
		{"name": "First Harvest", "value": 1000.0, "day": 45, "goal": "Harvest 1 ton"},
		{"name": "25%", "value": 25000.0, "day": 120, "goal": "Harvest 100 tons"}
	]
	_update_milestones_display()