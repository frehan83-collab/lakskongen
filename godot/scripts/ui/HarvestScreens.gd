class_name HarvestScreens
extends RefCounted

## Where the harvest planner and the harvest report live (roadmap U2-05).
## Both are modal Chart-paper sheets over the running game: they go on the
## game scene's event layer (the critical card's CanvasLayer, above the game
## UI and below the pause menu), created on first use so the hot GameScene
## file is untouched, and freed with the scene. Without a game scene (a
## test, the menu) they get a layer of their own under the current scene.

const PLANNER_SCENE := "res://scenes/ui/HarvestPlanner.tscn"
const REPORT_SCENE := "res://scenes/ui/HarvestReport.tscn"
const PLANNER_GROUP := &"harvest_planner"
const REPORT_GROUP := &"harvest_report"
## The critical card's layer number (GameScene EventLayer).
const EVENT_LAYER := 4


## The planner sheet (created on first use).
static func planner(tree: SceneTree) -> Control:
	return _find_or_make(tree, PLANNER_GROUP, PLANNER_SCENE)


## The report sheet (created on first use).
static func report(tree: SceneTree) -> Control:
	return _find_or_make(tree, REPORT_GROUP, REPORT_SCENE)


static func _find_or_make(tree: SceneTree, group: StringName, path: String) -> Control:
	if tree == null:
		return null
	var existing := tree.get_first_node_in_group(group)
	if existing is Control and not existing.is_queued_for_deletion():
		return existing
	var scene := load(path) as PackedScene
	if scene == null:
		push_warning("HarvestScreens: cannot load %s" % path)
		return null
	var sheet := scene.instantiate() as Control
	_host(tree).add_child(sheet)
	return sheet


## The event layer of the running game scene, or a layer of our own.
static func _host(tree: SceneTree) -> Node:
	var card := tree.get_first_node_in_group(&"critical_event_presenter")
	if card != null and card.get_parent() is CanvasLayer and not card.is_queued_for_deletion():
		return card.get_parent()
	var root: Node = tree.current_scene if tree.current_scene != null else tree.root
	var layer := root.get_node_or_null(^"HarvestLayer") as CanvasLayer
	if layer == null:
		layer = CanvasLayer.new()
		layer.name = "HarvestLayer"
		layer.layer = EVENT_LAYER
		root.add_child(layer)
	return layer
