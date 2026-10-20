extends RefCounted

## OverlapLint — the screen-region check behind roadmap U1-04.
##
## The game screen is split into regions that must never cover each other:
## the HUD readouts, the goal chip and the HUD controls (speed, pause, inbox)
## in the top bar; the tab rail under it; the notification drawer on the right
## under the rail; the toast stack in the bottom-right corner (beside the
## drawer while it is open); the tutorial card bottom-left; the fjord camera
## hint at the bottom centre. Run by UiFlowTests at several UI scales.
##
##  * regions(scene): the visible regions as {name: Rect2} in screen space
##    (every CanvasLayer here has an identity transform).
##  * overlaps(regions): every pair that intersects or touches, plus every
##    region that leaves the screen.

## Visible regions of a running GameScene: {name: Rect2}.
static func regions(scene: Node) -> Dictionary:
	var out := {}
	var hud := scene.get_node_or_null("UILayer/HUD") as Control
	if hud != null and hud.is_visible_in_tree():
		var readouts := _union_of_children(hud.get_node_or_null("MainHBox/Left") as Control)
		if readouts.has_area():
			out["hud_readouts"] = readouts
		_add(out, "goal_chip", hud.get_node_or_null("MainHBox/GoalChip") as Control)
		_add(out, "hud_controls", hud.get_node_or_null("MainHBox/Right") as Control)
	var ui := scene.get_node_or_null("UILayer/GameUI") as Control
	if ui != null:
		_add(out, "tab_rail", ui.find_child("RailPanel", true, false) as Control)
	var center := scene.get_node_or_null("UILayer/NotificationCenter")
	if center != null:
		_add(out, "notification_panel", center.get_node_or_null("Panel") as Control)
	var tutorial := scene.get_node_or_null("UILayer/TutorialUI") as Control
	_add(out, "tutorial_card", tutorial)
	var hint := scene.get_node_or_null("UILayer/CameraHint")
	if hint != null:
		_add(out, "camera_hint", hint.find_child("HintChip", true, false) as Control)
		_add(out, "fly_to_farm", hint.find_child("FlyToFarmButton", true, false) as Control)
	var toasts := scene.get_node_or_null("/root/ToastNotification")
	if toasts != null and toasts.has_method("get_toast_rects"):
		var i := 0
		for r: Rect2 in toasts.get_toast_rects():
			out["toast_%d" % i] = r
			i += 1
	return out

## ["a × b", "x off screen", …] for every overlap or touch and every region
## outside the visible screen.
static func overlaps(found: Dictionary, screen: Rect2) -> PackedStringArray:
	var out := PackedStringArray()
	var names: Array = found.keys()
	for i in names.size():
		var a: Rect2 = found[names[i]]
		if not screen.encloses(a):
			out.append("%s off screen %s" % [names[i], str(a)])
		for j in range(i + 1, names.size()):
			var b: Rect2 = found[names[j]]
			if a.intersects(b, true):
				out.append("%s × %s" % [names[i], names[j]])
	return out

static func _add(out: Dictionary, region: String, control: Control) -> void:
	if control != null and control.is_visible_in_tree() and control.get_global_rect().has_area():
		out[region] = control.get_global_rect()

## The rect the visible children really cover (an expanding box's own rect
## includes the empty space it was stretched over).
static func _union_of_children(box: Control) -> Rect2:
	var r := Rect2()
	if box == null or not box.is_visible_in_tree():
		return r
	for child in box.get_children():
		var c := child as Control
		if c == null or not c.is_visible_in_tree() or not c.get_global_rect().has_area():
			continue
		r = c.get_global_rect() if not r.has_area() else r.merge(c.get_global_rect())
	return r
