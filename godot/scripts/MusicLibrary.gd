extends Node

## MusicLibrary — static catalogue of all audio resource paths.
## AudioController.gd looks up tracks here; never hard-codes paths elsewhere.
##
## Resource layout:
##   res://audio/music/   — background music (.ogg)
##   res://audio/ambient/ — ambient loops (.ogg)
##   res://audio/sfx/     — one-shot SFX (.ogg)
##
## To add new audio:
##   1. Drop the .ogg into the correct subfolder.
##   2. Add a key→path entry in the relevant dictionary below.
##   3. Reference the key from AudioController or SimBridge.

# ── Background music (seasonal + situational) ─────────────────────────────────

## Seasonal BGM. Keys match AudioManager.GetSeasonalTrack() and SimBridge.audio_seasonal_track.
const MUSIC_TRACKS: Dictionary = {
	"spring_theme":  "res://audio/music/spring_theme.ogg",
	"summer_theme":  "res://audio/music/summer_theme.ogg",
	"autumn_theme":  "res://audio/music/autumn_theme.ogg",
	"winter_theme":  "res://audio/music/winter_theme.ogg",
	"main_theme":    "res://audio/music/main_theme.ogg",
	# Situational variations
	"storm_music":   "res://audio/music/storm_music.ogg",
	"harbor_theme":  "res://audio/music/harbor_theme.ogg",
	"menu_theme":    "res://audio/music/menu_theme.ogg",
}

# ── Ambient base layers (looping) ─────────────────────────────────────────────

## Ambient base-layer loops. Keys match SimBridge.audio_base_track / AmbienceProfile.BaseTrack.
const AMBIENT_BASE: Dictionary = {
	"calm_sea":          "res://audio/ambient/calm_sea.ogg",
	"storm":             "res://audio/ambient/storm.ogg",
	"wind":              "res://audio/ambient/wind.ogg",
	"rain":              "res://audio/ambient/rain.ogg",
	"harbor":            "res://audio/ambient/harbor.ogg",
	"processing_plant":  "res://audio/ambient/processing_plant.ogg",
}

# ── Ambient weather overlays (additive, ducked on top of base) ────────────────

## Weather overlay loops. Keys match SimBridge.audio_weather_layer / AmbienceProfile.WeatherLayer.
const AMBIENT_WEATHER: Dictionary = {
	"wind":   "res://audio/ambient/overlay_wind.ogg",
	"rain":   "res://audio/ambient/overlay_rain.ogg",
	"storm":  "res://audio/ambient/overlay_storm.ogg",
}

# ── Alert / one-shot SFX ──────────────────────────────────────────────────────

## Alert SFX. Keys match SimBridge.audio_alert_triggered signal / AmbienceProfile.AlertLayer.
const ALERT_SFX: Dictionary = {
	"disease_outbreak": "res://audio/sfx/alert_disease.ogg",
	"market_crash":     "res://audio/sfx/alert_market_crash.ogg",
	"achievement":      "res://audio/sfx/achievement.ogg",
	"harvest":          "res://audio/sfx/harvest_complete.ogg",
	"lice_warning":     "res://audio/sfx/alert_lice.ogg",
}

# ── UI sounds ─────────────────────────────────────────────────────────────────

const UI_SFX: Dictionary = {
	"button_click":    "res://audio/ui/button_click.ogg",
	"button_hover":    "res://audio/ui/button_hover.ogg",
	"notification":    "res://audio/ui/notification.ogg",
	"day_advance":     "res://audio/ui/day_advance.ogg",
	"menu_open":       "res://audio/ui/menu_open.ogg",
	"menu_close":      "res://audio/ui/menu_close.ogg",
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
