extends SceneTree
## Headless terrain QA: generates FjordTerrain twice and prints heightfield
## stats (determinism hash, channel depth, above-water cells in channel).
## Run: Godot --headless --path godot -s res://tools/terrain_stats.gd

func _init() -> void:
	var script := load("res://scripts/environment/FjordTerrain.gd") as GDScript
	var stats_a := _generate_stats(script)
	var stats_b := _generate_stats(script)
	print("TERRAIN_QA first_hash=", stats_a["hash"], " second_hash=", stats_b["hash"])
	print("TERRAIN_QA deterministic=", stats_a["hash"] == stats_b["hash"])
	print("TERRAIN_QA min=", stats_a["min"], " max=", stats_a["max"],
		" center_depth=", stats_a["center"],
		" above_water_in_channel=", stats_a["channel_above"],
		" channel_cells=", stats_a["channel_total"])
	quit()

func _generate_stats(script: GDScript) -> Dictionary:
	var node := Node3D.new()
	root.add_child(node)
	node.set_script(script)
	node.terrain_resolution = 128
	node.terrain_seed = 42
	node.generate_terrain()
	var hm: PackedFloat32Array = node.get("_heightmap")
	var size: int = node.terrain_resolution
	var min_h := 999.0
	var max_h := -999.0
	var h := 0.0
	var channel_above := 0
	var channel_total := 0
	for z in range(size):
		for x in range(size):
			var v: float = hm[z * size + x]
			min_h = minf(min_h, v)
			max_h = maxf(max_h, v)
			h = h * 1.000001 + v
			var nx := (float(x) / float(size) - 0.5) * 2.0
			var nz := (float(z) / float(size) - 0.5) * 2.0
			var fjord_center := sin(nz * 2.0) * 0.1
			if absf(nx - fjord_center) < 0.15 * 0.3:
				channel_total += 1
				if v > 0.0:
					channel_above += 1
	node.queue_free()
	return {"hash": snappedf(h, 0.000001), "min": min_h, "max": max_h,
		"center": hm[size / 2 * size + size / 2],
		"channel_above": channel_above, "channel_total": channel_total}
