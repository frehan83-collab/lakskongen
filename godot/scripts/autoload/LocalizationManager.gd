extends Node

## LocalizationManager — the game's language (roadmap U2-08).
##
## The game ships two languages, Norwegian Bokmål ("nb", the default) and
## English ("en"): the two columns of translations/translations.csv
## (project.godot loads translations.nb.translation and
## translations.en.translation; Godot reads the column "no" as nb). The
## choice is the player's "Språk" setting (general.language in
## SettingsStore); a language.cfg from older versions is read once and moved
## into the settings file. Screens that build their text in code listen to
## language_changed (or NOTIFICATION_TRANSLATION_CHANGED) and redraw;
## scene text that holds a translation key follows by itself.

signal language_changed(new_locale: String)

const SUPPORTED_LOCALES := ["nb", "en"]
## Each language's own name (shown as written, in every language).
const LOCALE_NAMES := {
	"nb": "Norsk (bokmål)",
	"en": "English",
}

const DEFAULT_LOCALE := "nb"
## The pre-U2-08 file (one line, the locale); read once and moved.
const LEGACY_PATH := "user://language.cfg"

var current_locale: String = DEFAULT_LOCALE

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	apply_language(_saved_language())


# ── Public API ─────────────────────────────────────────────────────────────────

## The supported locale a code means ("no", "nn", "nb_NO" -> "nb"; "en_GB" ->
## "en"); "" when it is none of them.
static func normalize(locale: String) -> String:
	var code := locale.strip_edges().to_lower().replace("-", "_").split("_")[0]
	if code in ["nb", "no", "nn"]:
		return "nb"
	return code if code in SUPPORTED_LOCALES else ""


## Set the language and keep it (the player's choice).
func set_language(locale: String) -> void:
	var chosen := apply_language(locale)
	SettingsStore.set_value("general", "language", chosen)


## Switch the language now without writing the settings (the applier, at
## launch). Returns the locale in force.
func apply_language(locale: String) -> String:
	var chosen := normalize(locale)
	if chosen.is_empty():
		push_warning("LocalizationManager: unsupported locale '%s', using %s" % [locale, DEFAULT_LOCALE])
		chosen = DEFAULT_LOCALE
	var changed := chosen != current_locale or normalize(TranslationServer.get_locale()) != chosen
	current_locale = chosen
	TranslationServer.set_locale(chosen)
	if changed:
		language_changed.emit(chosen)
	return chosen


## Get the current language code.
func get_language() -> String:
	return current_locale


## Get list of all supported locale codes.
func get_supported_locales() -> Array:
	return SUPPORTED_LOCALES.duplicate()


## A language's own name ("Norsk (bokmål)", "English").
func get_locale_name(locale: String) -> String:
	return str(LOCALE_NAMES.get(normalize(locale), locale.to_upper()))


## Get all locale codes with their names.
func get_locale_options() -> Dictionary:
	var options: Dictionary = {}
	for locale in SUPPORTED_LOCALES:
		options[locale] = get_locale_name(locale)
	return options


# ── Persistence ────────────────────────────────────────────────────────────────

## The saved language: the settings file's, else an old language.cfg's
## (moved into the settings file), else Norwegian.
func _saved_language() -> String:
	var general: Variant = SettingsStore.load_raw().get("general", {})
	if general is Dictionary and (general as Dictionary).has("language"):
		return str((general as Dictionary)["language"])
	if FileAccess.file_exists(LEGACY_PATH):
		var file := FileAccess.open(LEGACY_PATH, FileAccess.READ)
		if file != null:
			var legacy := normalize(file.get_line())
			file.close()
			if not legacy.is_empty():
				SettingsStore.set_value("general", "language", legacy)
				return legacy
	return DEFAULT_LOCALE
