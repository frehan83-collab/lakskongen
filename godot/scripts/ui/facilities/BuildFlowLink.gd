class_name BuildFlowLink
extends RefCounted

## The one way into the family-first build flow from a tab (roadmap F2-31):
## the RAS and Offshore tabs' "Planlegg …" buttons replaced their legacy
## build buttons (BuildRASFacility, BuildOffshoreFarm), which skipped the
## permits, the construction and every preview. This shows the map and opens
## its build dialog on the family. Returns false when no game UI hosts the
## caller (a tab shown on its own).


static func open(from: Node, family_id: String) -> bool:
	var ui := _game_ui(from)
	if ui == null:
		return false
	ui.call("show_tab", "map")
	var map := ui.find_child("Panel_map", true, false)
	if map == null or not map.has_method("open_build_family"):
		return false
	map.call("open_build_family", family_id)
	return true


static func _game_ui(from: Node) -> Node:
	var node := from
	while node != null:
		if node.has_method("show_tab") and node.has_method("get_active_tab"):
			return node
		node = node.get_parent()
	return null
