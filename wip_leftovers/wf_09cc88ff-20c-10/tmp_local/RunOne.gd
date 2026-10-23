extends "res://tests/UiFlowTests.gd"

## Local runner for one checks file or one UiFlowTests method (not committed).
## ONE_CHECKS=<File> runs res://tests/<File>.gd; ONE_METHOD=<name> calls that method.
func _run() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	SettingsStore.path_override = TEST_ROOT + "settings_one.json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	get_node("/root/SettingsApplier").apply_all(SettingsStore.load_all(), false)
	TranslationServer.set_locale("no")
	var method := OS.get_environment("ONE_METHOD")
	if method != "":
		await call(method)
	else:
		var which := OS.get_environment("ONE_CHECKS")
		await load("res://tests/%s.gd" % which).new().run(self)
	_finish()
