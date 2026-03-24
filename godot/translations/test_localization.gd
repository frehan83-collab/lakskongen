extends Node

## Quick localization test script.
## Attach to any scene and run to verify translation system works.
## Expected output: Prints the same UI string in all 6 languages.

func _ready() -> void:
	print("\n=== Lakskongen Localization Test ===\n")
	
	var loc_mgr = get_node_or_null("/root/LocalizationManager")
	if not loc_mgr:
		push_error("❌ LocalizationManager not found!")
		return
	
	var test_key := "ui.main_menu.new_game"
	var original_locale := loc_mgr.get_language()
	
	print("Testing translation key: '%s'" % test_key)
	print("Original locale: %s\n" % original_locale)
	
	var locales := ["no", "en", "de", "fr", "es", "ja"]
	var expected := {
		"no": "Nytt Spill",
		"en": "New Game",
		"de": "Neues Spiel",
		"fr": "Nouvelle Partie",
		"es": "Nueva Partida",
		"ja": "新しいゲーム"
	}
	
	var all_pass := true
	
	for locale in locales:
		loc_mgr.set_language(locale)
		var translation := tr(test_key)
		var expected_text := expected[locale]
		var match_emoji := "✅" if translation == expected_text else "❌"
		
		print("%s %s: '%s' (expected: '%s')" % [
			match_emoji,
			locale.to_upper(),
			translation,
			expected_text
		])
		
		if translation != expected_text:
			all_pass = false
	
	# Restore original locale
	loc_mgr.set_language(original_locale)
	
	print("\n=== Test Complete ===")
	if all_pass:
		print("✅ All translations passed!")
	else:
		print("❌ Some translations failed. Check strings.csv")
	
	# Test language persistence
	print("\n=== Testing Persistence ===")
	loc_mgr.set_language("en")
	print("Set language to EN and saved to disk")
	print("Restart the game — language should remain EN")
	
	print("\n=== Supported Locales ===")
	var supported := loc_mgr.get_supported_locales()
	print("Count: %d" % supported.size())
	for locale in supported:
		print("  • %s — %s" % [locale, loc_mgr.get_locale_name(locale)])
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
