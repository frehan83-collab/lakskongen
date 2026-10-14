extends Control

## NextActionWidget — HUD element showing "Next Action" suggestion
## Anchored to top-right, always visible, updates every day
## Pulls suggestion from DecisionManager

var _decision_manager: Node = null
var _game_state: Node = null

func _ready():
	_decision_manager = get_node("/root/DecisionManager")
	_game_state = get_node("/root/GameState")
	
	if _game_state:
		_game_state.DayAdvanced.connect(_on_day_advanced)
	
	_build_ui()
	_update_suggestion()

func _build_ui():
	# Anchor to top-right
	anchor_left = 1.0
	anchor_right = 1.0
	anchor_top = 0.0
	anchor_bottom = 0.0
	offset_left = -420
	offset_right = -20
	offset_top = 80
	offset_bottom = 180
	
	# Panel background
	var panel := LakskongenTheme.create_styled_panel()
	panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(panel)
	
	# Content VBox
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	panel.add_child(vbox)
	
	# Header
	var header := LakskongenTheme.create_label("NEXT ACTION", LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
	vbox.add_child(header)
	
	# Suggestion text
	var suggestion := LakskongenTheme.create_label("Loading...", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	suggestion.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	suggestion.name = "Suggestion"
	vbox.add_child(suggestion)

func _on_day_advanced(_new_day: int):
	_update_suggestion()

func _update_suggestion():
	if not _decision_manager:
		return
	
	var suggestion_label = $PanelContainer/VBoxContainer/Suggestion as Label
	var text = _decision_manager.GetNextActionSuggestion()
	
	suggestion_label.text = text

	# No colour read off the suggestion's text (U2-06: nothing classifies
	# alert text by substring). Urgency comes back as a typed field when the
	# widget is revived with the choice events (S4-08, roadmap D6).
	LakskongenTheme.set_text_color(suggestion_label, LakskongenTheme.TEXT)
