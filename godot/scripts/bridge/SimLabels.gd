class_name SimLabels
extends RefCounted

## SimLabels — display names for the enum values and ids that cross the bridge
## (roadmap U1-03). The sim speaks in identifiers (WeatherState "Moderate",
## ZoneColor "Green", ContainmentLevel "SemiClosed", ledger category
## "storm_repairs", zone "po3_nord"); the player reads a translated name.
##
## Every value maps to the translation key "<GROUP>_<VALUE>" with the value in
## upper snake case: weather("PartlyCloudy") reads ENUM_WEATHER_PARTLY_CLOUDY.
## tests/test_text_correctness.py checks that every value of each C# enum
## listed in ENUM_SOURCES has its key in both languages. A value without a key
## (a mod, a newer sim) falls back to a readable form of the raw value, never
## the raw id.

const GROUP_WEATHER := "ENUM_WEATHER"
const GROUP_TRAFFIC_LIGHT := "ENUM_TRAFFIC_LIGHT"
const GROUP_CONTAINMENT := "ENUM_CONTAINMENT"
const GROUP_BARRIER := "ENUM_BARRIER"
const GROUP_DELOUSING := "ENUM_DELOUSING"
const GROUP_TRAIT := "ENUM_TRAIT"
const GROUP_TRANSFER_REASON := "ENUM_TRANSFER_REASON"
const GROUP_NIGHT_SHIFT := "ENUM_NIGHT_SHIFT"
const GROUP_WATCH_MODE := "ENUM_WATCH_MODE"
const GROUP_WELLBOAT_JOB := "ENUM_WELLBOAT_JOB"
const GROUP_BLOOM := "ENUM_BLOOM"
const GROUP_DISEASE := "ENUM_DISEASE"
const GROUP_SMOLT_TIER := "ENUM_SMOLT_TIER"
const GROUP_PEN_ACTION := "UI_PEN_ACTION"
const GROUP_CONTRACT_TYPE := "ENUM_CONTRACT_TYPE"
const GROUP_GILL_REASON := "GILL_REASON"
const GROUP_GE_STATUS := "ENUM_GE_STATUS"
const GROUP_GE_TRAIT := "GE_TRAIT"
const GROUP_RISK := "ENUM_RISK"
const GROUP_LEDGER := "LEDGER"
const GROUP_ZONE := "ZONE"

static var _camel: RegEx = null


static func weather(state: String) -> String:
	return label(GROUP_WEATHER, state)


static func traffic_light(color: String) -> String:
	return label(GROUP_TRAFFIC_LIGHT, color)


static func containment(level: String) -> String:
	return label(GROUP_CONTAINMENT, level)


static func barrier(kind: String) -> String:
	return label(GROUP_BARRIER, kind)


static func delousing(method: String) -> String:
	return label(GROUP_DELOUSING, method)


static func genetic_trait(trait_name: String) -> String:
	return label(GROUP_TRAIT, trait_name)


static func transfer_reason(reason: String) -> String:
	return label(GROUP_TRANSFER_REASON, reason)


static func night_shift(policy: String) -> String:
	return label(GROUP_NIGHT_SHIFT, policy)


static func watch_mode(mode: String) -> String:
	return label(GROUP_WATCH_MODE, mode)


static func wellboat_job(kind: String) -> String:
	return label(GROUP_WELLBOAT_JOB, kind)


static func bloom(kind: String) -> String:
	return label(GROUP_BLOOM, kind)


## A disease type (Data.DiseaseType, or the bridge's "SeaLice"/"Escape").
static func disease(disease_type: String) -> String:
	return label(GROUP_DISEASE, disease_type)


## A smolt quality tier (Farm.SmoltQualityTier: Standard, Large, SuperSmolt).
static func smolt_tier(tier: String) -> String:
	return label(GROUP_SMOLT_TIER, tier)


## A pen action id ("delouse", "harvest", "repair_nets" …) for failure toasts.
static func pen_action(action: String) -> String:
	return label(GROUP_PEN_ACTION, action)


## A contract type (Market.ContractType: Spot, Tolling, Exclusive, Forward).
static func contract_type(contract_type_name: String) -> String:
	return label(GROUP_CONTRACT_TYPE, contract_type_name)


## A gill-health warning reason (Health.GillHealthWarning, English in the sim).
static func gill_reason(reason: String) -> String:
	return label(GROUP_GILL_REASON, reason)


## A GM regulatory status (Biology.RegulatoryStatus).
static func ge_status(status: String) -> String:
	return label(GROUP_GE_STATUS, status)


## A GM trait by its id ("ge_sterility" …).
static func ge_trait(trait_id: String) -> String:
	return label(GROUP_GE_TRAIT, trait_id)


## A risk level ("Low", "Medium", "Medium-High", "High").
static func risk(level: String) -> String:
	return label(GROUP_RISK, level)


## A ledger booking category ("storm_repairs", "feed", "achievements" …).
static func ledger_category(category: String) -> String:
	return label(GROUP_LEDGER, category)


## A zone id ("po3_nord", "fjord_west") as a place name.
static func zone(zone_id: String) -> String:
	return label(GROUP_ZONE, zone_id)


## Why a command was refused, in the player's language: the translation of
## its error key (CMD_ERROR_<KEY>, keys from the engine's CommandResult), else
## the translation of `fallback_key`. The engine's own message is a developer
## text and is never shown.
static func command_error(result: Dictionary, fallback_key: String = "CMD_ERROR_GENERIC") -> String:
	var err := str(result.get("error", result.get("error_key", "")))
	if err != "":
		var key := "CMD_ERROR_" + upper_snake(err)
		var t := String(TranslationServer.translate(key))
		if t != key:
			return t
	return String(TranslationServer.translate(fallback_key))


## The translated name of `value` in `group`, or a readable fallback.
static func label(group: String, value: String) -> String:
	if value.strip_edges() == "":
		return "–"
	var key := group + "_" + upper_snake(value)
	var t := String(TranslationServer.translate(key))
	return t if t != key else humanize(value)


## "PartlyCloudy" → "PARTLY_CLOUDY"; "po3_nord" → "PO3_NORD"; "AI-ROC" → "AI_ROC".
static func upper_snake(value: String) -> String:
	if _camel == null:
		_camel = RegEx.new()
		_camel.compile("([a-z0-9])([A-Z])")
	var s := _camel.sub(value.strip_edges(), "$1_$2", true)
	var out := ""
	for ch in s:
		out += ch if (ch >= "A" and ch <= "Z") or (ch >= "a" and ch <= "z") or (ch >= "0" and ch <= "9") else "_"
	while out.contains("__"):
		out = out.replace("__", "_")
	return out.trim_prefix("_").trim_suffix("_").to_upper()


## "PartlyCloudy" → "Partly cloudy"; "po3_nord" → "Po3 nord": never a raw id.
static func humanize(value: String) -> String:
	var words := upper_snake(value).to_lower().split("_", false)
	var s := " ".join(words)
	return s.substr(0, 1).to_upper() + s.substr(1) if s != "" else value
