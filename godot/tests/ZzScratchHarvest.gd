extends "res://tests/UiFlowTests.gd"

## Scratch runner (not committed): only the U2-05 harvest flow.

func _run() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	TranslationServer.set_locale("no")
	await _test_harvest_planner_orders_and_reports()
	_finish()
