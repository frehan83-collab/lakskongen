extends Node
class_name UIPolish

## UIPolish — shared, additive UI polish helpers.
##
## Keeps interaction behaviours (focus, press feedback, tab order) in ONE
## place so panels don't each reinvent them. All methods are static and
## side-effect-light: calling them never changes gameplay state.

## Recursively attach press feedback to every Button under `root`.
## Safe to call repeatedly — JuiceEffects guards against double-attach.
## True while a text field has keyboard focus: letter and Space hotkeys must
## leave the key to the field.
static func is_typing(node: Node) -> bool:
	if node == null or not node.is_inside_tree():
		return false
	var focus := node.get_viewport().gui_get_focus_owner()
	return focus is LineEdit or focus is TextEdit

static func attach_press_feedback(root: Node) -> void:
	if root == null:
		return
	for child in root.get_children():
		if child is BaseButton:
			JuiceEffects.attach_press_feedback(child)
		if child.get_child_count() > 0:
			attach_press_feedback(child)


## Give focus to the first keyboard-focusable Control under `root`.
## Returns true if something was focused. Leaves focus alone if nothing is
## focusable, so callers can branch on the result if needed.
static func focus_first(root: Node) -> bool:
	if root == null:
		return false
	for child in root.get_children():
		if child is Control:
			var c := child as Control
			if c.visible and c.focus_mode != Control.FOCUS_NONE:
				c.grab_focus()
				return true
		if child.get_child_count() > 0 and focus_first(child):
			return true
	return false


## Make a horizontal row of Controls navigable left/right with arrow keys
## by chaining their focus_neighbor_left/right in child order.
## Skips non-focusable controls. Idempotent.
static func link_horizontal_focus(row: Control) -> void:
	if row == null:
		return
	var focusables: Array[Control] = []
	for child in row.get_children():
		if child is Control and (child as Control).focus_mode != Control.FOCUS_NONE:
			focusables.append(child)
	for i in focusables.size():
		var c := focusables[i]
		c.focus_neighbor_left = c.get_path_to(focusables[max(0, i - 1)])
		c.focus_neighbor_right = c.get_path_to(focusables[min(focusables.size() - 1, i + 1)])


## Make a vertical stack of Controls navigable up/down with arrow keys.
static func link_vertical_focus(column: Control) -> void:
	if column == null:
		return
	var focusables: Array[Control] = []
	for child in column.get_children():
		if child is Control and (child as Control).focus_mode != Control.FOCUS_NONE:
			focusables.append(child)
	for i in focusables.size():
		var c := focusables[i]
		c.focus_neighbor_top = c.get_path_to(focusables[max(0, i - 1)])
		c.focus_neighbor_bottom = c.get_path_to(focusables[min(focusables.size() - 1, i + 1)])


## Soften a panel's entrance (fade + rise) with a guard for validity.
static func panel_enter(panel: Control) -> void:
	if panel == null or not is_instance_valid(panel):
		return
	JuiceEffects.panel_enter(panel)


## Show a standardized empty state message in a container.
## Clears existing children and adds a muted Label with the given text.
## Uses LakskongenTheme if provided for consistent font/color, otherwise
## falls back to Nordic Minimalist defaults.
##
## Args:
##   container: VBoxContainer or similar to receive the empty state label
##   text: Message to display (pre-localized by caller)
##   font_size: Optional font size override (default: 12)
##   icon: Optional emoji prefix (e.g. "🔔 ", "📈 ")
##   theme: Optional LakskongenTheme node for themed label creation
static func show_empty_state(container: Control, text: String, font_size: int = 12, icon: String = "", theme: Node = null) -> void:
	if container == null:
		return
	for child in container.get_children():
		child.queue_free()
	var label := Label.new()
	label.text = "%s%s" % [icon, text] if not icon.is_empty() else text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if theme != null and theme.has_method("create_label"):
		# Use theme helper for consistency
		var themed_label: Label = theme.create_label(label.text, font_size, theme.MUTED)
		container.add_child(themed_label)
	else:
		LakskongenTheme.set_size(label, font_size)
		LakskongenTheme.set_tone(label, &"Muted")
		container.add_child(label)


## Show an empty state with a localized key and fallback.
## Convenience wrapper for panels using _tr_or pattern.
## Caller must handle localization (using tr() or _tr_or) and pass the result.
static func show_empty_state_localized(container: Control, text: String, font_size: int = 12, icon: String = "", theme: Node = null) -> void:
	show_empty_state(container, text, font_size, icon, theme)