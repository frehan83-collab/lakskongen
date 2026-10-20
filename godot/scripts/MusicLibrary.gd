extends Node

## MusicLibrary — static catalogue of all audio resource paths.
## AudioController.gd looks up tracks here; never hard-codes paths elsewhere.
##
## Resource layout:
##   res://assets/audio/music/   — background music (.ogg)
##   res://assets/audio/ambient/ — ambient loops (.ogg)
##   res://assets/audio/sfx/     — alert one-shot SFX (.wav)
##   res://assets/audio/ui/      — UI one-shot SFX (.wav)
##
## To add new audio:
##   1. Drop the file into the correct subfolder.
##   2. Add a key→path entry in the relevant dictionary below.
##   3. Reference the key from AudioController or SimBridge.

# ── Background music (seasonal + situational) ─────────────────────────────────

## Seasonal BGM. Keys match AudioManager.GetSeasonalTrack() and SimBridge.audio_seasonal_track.
const MUSIC_TRACKS: Dictionary = {
	"spring_theme":  "res://assets/audio/music/spring.ogg",
	"summer_theme":  "res://assets/audio/music/summer.ogg",
	"autumn_theme":  "res://assets/audio/music/autumn.ogg",
	"winter_theme":  "res://assets/audio/music/winter.ogg",
	"main_theme":    "res://assets/audio/music/main_theme.ogg",
	# Menu / calm variation uses the calm-sea theme.
	"calm_sea":      "res://assets/audio/music/calm_sea.ogg",
}

# ── Ambient base layers (looping) ─────────────────────────────────────────────

## Ambient base-layer loops. Keys match SimBridge.audio_base_track / AmbienceProfile.BaseTrack.
## NOTE: there is no dedicated "processing_plant" asset; closest real loop is used.
const AMBIENT_BASE: Dictionary = {
	"calm_sea":          "res://assets/audio/ambient/ocean_waves.ogg",
	"storm":             "res://assets/audio/ambient/storm.ogg",
	"wind":              "res://assets/audio/ambient/wind_light.ogg",
	"rain":              "res://assets/audio/ambient/rain_light.ogg",
	"harbor":            "res://assets/audio/ambient/harbor_ambience.ogg",
	"processing_plant":  "res://assets/audio/ambient/harbor_ambience.ogg",
	# File-named keys (direct access / fallbacks).
	"ocean_waves":       "res://assets/audio/ambient/ocean_waves.ogg",
	"harbor_ambience":   "res://assets/audio/ambient/harbor_ambience.ogg",
	"ice_cracking":      "res://assets/audio/ambient/ice_cracking.ogg",
	"seagulls":          "res://assets/audio/ambient/seagulls.ogg",
	"underwater":        "res://assets/audio/ambient/underwater.ogg",
	"wind_light":        "res://assets/audio/ambient/wind_light.ogg",
	"wind_strong":       "res://assets/audio/ambient/wind_strong.ogg",
	"rain_light":        "res://assets/audio/ambient/rain_light.ogg",
	"rain_heavy":        "res://assets/audio/ambient/rain_heavy.ogg",
}

# ── Ambient weather overlays (additive, ducked on top of base) ────────────────

## Weather overlay loops. Keys match SimBridge.audio_weather_layer / AmbienceProfile.WeatherLayer.
const AMBIENT_WEATHER: Dictionary = {
	"wind":   "res://assets/audio/ambient/wind_strong.ogg",
	"rain":   "res://assets/audio/ambient/rain_heavy.ogg",
	"storm":  "res://assets/audio/ambient/storm.ogg",
}

# ── Alert / one-shot SFX ──────────────────────────────────────────────────────

## Alert SFX. Keys match SimBridge.audio_alert_triggered signal / AmbienceProfile.AlertLayer.
const ALERT_SFX: Dictionary = {
	"disease_outbreak": "res://assets/audio/sfx/disease_alert.wav",
	"market_crash":     "res://assets/audio/sfx/financial_crisis.wav",
	"achievement":      "res://assets/audio/sfx/achievement_unlock.wav",
	# U2-05: the harvest's payoff stinger (tools/gen_harvest_stinger.py), 1.9 s.
	"harvest":          "res://assets/audio/sfx/harvest_stinger.wav",
	"lice_warning":     "res://assets/audio/sfx/lice_alert.wav",
}

# ── UI sounds ─────────────────────────────────────────────────────────────────

const UI_SFX: Dictionary = {
	"button_click":    "res://assets/audio/ui/button_click.wav",
	"button_hover":    "res://assets/audio/ui/button_hover.wav",
	"notification":    "res://assets/audio/ui/notification.wav",
	"error":           "res://assets/audio/ui/error.wav",
	"menu_open":       "res://assets/audio/ui/panel_open.wav",
	"menu_close":      "res://assets/audio/ui/panel_close.wav",
	"panel_open":      "res://assets/audio/ui/panel_open.wav",
	"panel_close":     "res://assets/audio/ui/panel_close.wav",
}

# ── Lookup helpers ────────────────────────────────────────────────────────────

## Return the resource path for a named music track, or "" if not found.
static func music(key: String) -> String:
	return MUSIC_TRACKS.get(key, "")

## Return the resource path for an ambient base track, or "" if not found.
static func ambient_base(key: String) -> String:
	return AMBIENT_BASE.get(key, "")

## Return the resource path for a weather overlay, or "" if not found.
static func ambient_weather(key: String) -> String:
	return AMBIENT_WEATHER.get(key, "")

## Return the resource path for an alert SFX, or "" if not found.
static func alert_sfx(key: String) -> String:
	return ALERT_SFX.get(key, "")

## Return the resource path for a UI sound, or "" if not found.
static func ui_sfx(key: String) -> String:
	return UI_SFX.get(key, "")

## Verify every catalogue path exists on disk. Returns the list of missing
## resource paths and logs a warning when any are absent.
static func validate_catalog() -> Array[String]:
	var missing: Array[String] = []
	var catalogues: Array[Dictionary] = [
		MUSIC_TRACKS, AMBIENT_BASE, AMBIENT_WEATHER, ALERT_SFX, UI_SFX,
	]
	for catalogue in catalogues:
		for key in catalogue.keys():
			var path: String = catalogue[key]
			if not path.is_empty() and not ResourceLoader.exists(path):
				missing.append(path)
	if not missing.is_empty():
		push_warning("MusicLibrary: %d missing audio asset(s): %s" % [
			missing.size(), ", ".join(PackedStringArray(missing)),
		])
	return missing
