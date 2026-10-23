extends "res://tests/UiFlowTests.gd"
## Review-only runner (not committed): runs the named checks files.

func _run() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	_clear_test_saves()
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	CampaignProgress.reset()
	SettingsStore.path_override = TEST_ROOT + "settings.json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	get_node("/root/SettingsApplier").apply_all(SettingsStore.load_all(), false)
	TranslationServer.set_locale("no")
	var which := OS.get_environment("RUN_ONE")
	for part in which.split(","):
		match part:
			"camera":
				await load("res://tests/CameraSeaChecks.gd").new().run(self)
			"first":
				await load("res://tests/FirstSightChecks.gd").new().run(self)
			"teaser":
				await load("res://tests/TeaserChecks.gd").new().run(self)
			"water":
				await _test_water_grid_bake_and_sea_state()
			"probe":
				await load("res://tests/tmp_review/Probe.gd").new().run(self)
			"probe2":
				await load("res://tests/tmp_review/Probe2.gd").new().run(self)
			"probe4":
				await load("res://tests/tmp_review/Probe4.gd").new().run(self)
			"probe5":
				await load("res://tests/tmp_review/Probe5.gd").new().run(self)
			"probe6":
				await load("res://tests/tmp_review/Probe6.gd").new().run(self)
			"probe7":
				await load("res://tests/tmp_review/Probe7.gd").new().run(self)
			"probe3":
				await load("res://tests/tmp_review/Probe3.gd").new().run(self)
	_finish()
