class_name GameEventText
extends RefCounted

## What the player reads for a typed game event (roadmap U2-06): its title,
## what it is about, its deadline and the label of the verb that answers it.
## Everything is read from the event's typed fields (type, severity, site,
## zone, day, counter_verb); the message text is only ever shown, never parsed.

## Severity -> Kyst & Stål tone (theme variations "<Role><Tone>", D13).
const TONES := {"critical": &"Crit", "warning": &"Warn", "info": &"Info"}

## Severity glyphs: text symbols drawn in the tone colour (no emoji, D14).
const GLYPHS := {"critical": "!!", "warning": "!", "info": "i"}

## Counter verbs the UI knows how to perform (GameEventCatalog.CounterVerbs
## minus "none"); a pytest keeps the two lists equal.
const VERBS := ["delouse", "freshwater_bath", "inspect_nets", "check_water", "review_feeding",
	"raise_capital", "open_sustainability", "inspect_site"]


static func tone(event: Dictionary) -> StringName:
	return TONES.get(str(event.get("severity", "info")), &"Info")


static func glyph(event: Dictionary) -> String:
	return GLYPHS.get(str(event.get("severity", "info")), "i")


## The headline: the event's own (a storyteller event's name) or its type's title.
static func title(event: Dictionary) -> String:
	var headline := str(event.get("headline", ""))
	if headline != "":
		return headline
	var key := "GAME_EVENT_" + str(event.get("type", "")).to_upper()
	var text := str(TranslationServer.translate(key))
	return text if text != key else SimLabels.humanize(str(event.get("type", "")))


## "PO3 Sør", "PO3 nord" (a zone) or "Hele selskapet".
static func subject(event: Dictionary, sim: Node) -> String:
	var site := str(event.get("site_id", ""))
	if site != "":
		return str(sim.site_name(site)) if sim != null and sim.has_method("site_name") else SimLabels.humanize(site)
	var zone := str(event.get("zone_id", ""))
	if zone != "":
		return SimLabels.zone(zone)
	return str(TranslationServer.translate("UI_EVENT_SUBJECT_COMPANY"))


## "PO3 Sør · Dag 12".
static func meta_line(event: Dictionary, sim: Node) -> String:
	return "%s · %s" % [subject(event, sim), str(TranslationServer.translate("UI_EVENT_DAY")) % int(event.get("day", 0))]


## "Frist: 18. oktober (om 8 dager)", "" without a deadline. The date is the
## sim calendar's (GameCalendar, U2-07), as the HUD shows it.
static func deadline_line(event: Dictionary, today: int) -> String:
	var due := int(event.get("deadline_day", -1))
	if due < 0:
		return ""
	var left := due - today
	if left <= 0:
		return str(TranslationServer.translate("UI_EVENT_DEADLINE_TODAY")) % GameCalendar.short_date(due)
	return str(TranslationServer.translate("UI_EVENT_DEADLINE")) % [GameCalendar.short_date(due), left]


## True when the event names a verb the UI can perform.
static func has_verb(event: Dictionary) -> bool:
	return VERBS.has(str(event.get("counter_verb", "none")))


## The button text of a counter verb ("Planlegg avlusing").
static func verb_label(verb: String) -> String:
	return str(TranslationServer.translate("UI_EVENT_VERB_" + verb.to_upper()))
