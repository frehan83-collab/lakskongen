extends Node

## LocalizationManager — handles runtime language switching for Lakskongen.
## Supports: Norwegian (no), English (en), German (de), French (fr), Spanish (es), Japanese (ja)
## Default language: Norwegian (no)

signal language_changed(new_locale: String)

const SUPPORTED_LOCALES := ["no", "en", "de", "fr", "es", "ja"]
const LOCALE_NAMES := {
	"no": "ui.lang.norwegian",
	"en": "ui.lang.english",
	"de": "ui.lang.german",
	"fr": "ui.lang.french",
	"es": "ui.lang.spanish",
	"ja": "ui.lang.japanese"
}

const DEFAULT_LOCALE := "no"
const SETTINGS_PATH := "user://language.cfg"

var current_locale: String = DEFAULT_LOCALE

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_load_saved_language()
	print("LocalizationManager: ready (locale=%s)" % current_locale)


# ── Public API ─────────────────────────────────────────────────────────────────

## Set the current language and persist the choice.
func set_language(locale: String) -> void:
	if not locale in SUPPORTED_LOCALES:
		push_warning("LocalizationManager: unsupported locale '%s', using default" % locale)
		locale = DEFAULT_LOCALE
	
	current_locale = locale
	TranslationServer.set_locale(locale)
	_save_language(locale)
	language_changed.emit(locale)
	print("LocalizationManager: language set to %s" % locale)


## Get the current language code.
func get_language() -> String:
	return current_locale


## Get list of all supported locale codes.
func get_supported_locales() -> Array:
	return SUPPORTED_LOCALES.duplicate()


## Get the localized name of a language by its code.
func get_locale_name(locale: String) -> String:
	var key: String = LOCALE_NAMES.get(locale, "")
	if key.is_empty():
		return locale.to_upper()
	return tr(key)


## Get all locale codes with their localized names.
func get_locale_options() -> Dictionary:
	var options: Dictionary = {}
	for locale in SUPPORTED_LOCALES:
		options[locale] = get_locale_name(locale)
	return options


# ── Persistence ────────────────────────────────────────────────────────────────

func _load_saved_language() -> void:
	if not FileAccess.file_exists(SETTINGS_PATH):
		set_language(DEFAULT_LOCALE)
		return
	
	var file: FileAccess = FileAccess.open(SETTINGS_PATH, FileAccess.READ)
	if file == null:
		push_warning("LocalizationManager: could not open %s" % SETTINGS_PATH)
		set_language(DEFAULT_LOCALE)
		return
	
	var saved_locale: String = file.get_line().strip_edges()
	file.close()
	
	if saved_locale in SUPPORTED_LOCALES:
		set_language(saved_locale)
	else:
		push_warning("LocalizationManager: invalid saved locale '%s', using default" % saved_locale)
		set_language(DEFAULT_LOCALE)


func _save_language(locale: String) -> void:
	var file: FileAccess = FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if file == null:
		push_warning("LocalizationManager: could not write %s" % SETTINGS_PATH)
		return
	file.store_line(locale)
	file.close()
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
