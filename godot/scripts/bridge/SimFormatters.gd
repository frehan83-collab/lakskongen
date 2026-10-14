class_name SimFormatters
extends RefCounted

## SimFormatters — pure presentation helpers extracted from SimBridge.
## Stateless: every function is static. No signals (nothing to emit).
##
## Numbers and dates follow the active locale (TranslationServer): Norwegian
## Bokmål (nb, the default) writes a decimal comma and groups digits with a
## no-break space ("11,49 MNOK", "71 472", "12. jan. 2027"); English writes
## "11.49 MNOK", "71,472", "Jan 12, 2027". Every function takes an optional
## `locale` so tests and previews can pin one.

## Digit-group separator for nb: a no-break space, so "71 472" never wraps.
const NB_GROUP := "\u00a0"
## Sim day 0 is 1 January of this year until the sim calendar (roadmap G1-04)
## owns the date; then date_for_day() reads it from there.
const CALENDAR_START_YEAR := 2027

const _MONTHS_NB := ["jan.", "feb.", "mars", "apr.", "mai", "juni", "juli", "aug.", "sep.", "okt.", "nov.", "des."]
const _MONTHS_EN := ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
const _MONTH_DAYS := [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

static var _decimal_point: RegEx = null


## True when `locale` (or the active locale) is Norwegian.
static func is_norwegian(locale: String = "") -> bool:
	var loc := locale if locale != "" else TranslationServer.get_locale()
	return loc.begins_with("nb") or loc.begins_with("no") or loc.begins_with("nn") or loc == ""


## A number with `decimals` decimals, digit grouping and the locale's decimal
## mark: num(11.49, 2) → "11,49"; num(71472) → "71 472".
static func num(value: float, decimals: int = 0, locale: String = "") -> String:
	if not is_finite(value):
		return "–"
	var nb := is_norwegian(locale)
	var neg := value < 0.0
	var fixed := ("%." + str(maxi(decimals, 0)) + "f") % absf(value)
	var int_part := fixed
	var frac_part := ""
	var dot := fixed.find(".")
	if dot >= 0:
		int_part = fixed.substr(0, dot)
		frac_part = fixed.substr(dot + 1)
	if neg and (int_part.trim_prefix("0") != "" or frac_part.replace("0", "") != ""):
		int_part = "-" + _group(int_part, NB_GROUP if nb else ",")
	else:
		int_part = _group(int_part, NB_GROUP if nb else ",")
	if frac_part == "":
		return int_part
	return int_part + ("," if nb else ".") + frac_part


## printf-style formatting with the locale's decimal mark:
## fmt("%.1f °C", 3.0) → "3,0 °C" (nb) / "3.0 °C" (en). For short value
## strings; numbers that need digit grouping take num() instead.
static func fmt(format: String, args: Variant, locale: String = "") -> String:
	return localize_decimals(format % args, locale)


## "3.5 kg" → "3,5 kg" in Norwegian: a point between two digits becomes a
## comma. Other text passes through unchanged.
static func localize_decimals(text: String, locale: String = "") -> String:
	if not is_norwegian(locale):
		return text
	if _decimal_point == null:
		_decimal_point = RegEx.new()
		_decimal_point.compile("(?<=[0-9])\\.(?=[0-9])")
	return _decimal_point.sub(text, ",", true)


## An integer with digit grouping: 71472 → "71 472" (nb) / "71,472" (en).
static func integer(value: int, locale: String = "") -> String:
	return num(float(value), 0, locale)


## A percentage of a 0–1 fraction: percent(0.44) → "44 %" (nb) / "44%" (en).
static func percent(fraction: float, decimals: int = 0, locale: String = "") -> String:
	var body := num(fraction * 100.0, decimals, locale)
	return body + (NB_GROUP + "%" if is_norwegian(locale) else "%")


## Format a NOK amount with kNOK / MNOK suffix: 11 490 000 → "11,49 MNOK".
static func format_nok(amount: float, locale: String = "") -> String:
	var abs_amt: float = absf(amount)
	var sign: String = "-" if amount < 0.0 else ""
	if abs_amt >= 1_000_000_000.0:
		return sign + num(abs_amt / 1_000_000_000.0, 2, locale) + " GNOK"
	elif abs_amt >= 1_000_000.0:
		return sign + num(abs_amt / 1_000_000.0, 2, locale) + " MNOK"
	elif abs_amt >= 1_000.0:
		return sign + num(roundf(abs_amt / 1_000.0), 0, locale) + " kNOK"
	return sign + num(roundf(abs_amt), 0, locale) + " NOK"


## A price per kilo: 88.0 → "88,00 NOK/kg".
static func format_price_kg(nok_per_kg: float, locale: String = "") -> String:
	return num(nok_per_kg, 2, locale) + " NOK/kg"


## Format a biomass/weight value with t (tonne) suffix: 9300 → "9,3 t".
static func format_biomass(kg: float, locale: String = "") -> String:
	if kg >= 1_000_000.0:
		return num(kg / 1_000_000.0, 1, locale) + " kt"
	elif kg >= 1_000.0:
		return num(kg / 1_000.0, 1, locale) + " t"
	return num(kg, 0, locale) + " kg"


## Format a fish count compactly: 111 200 → "111,2k" (nb) / "111.2K" (en);
## 1 200 000 → "1,2 mill." / "1.2M".
static func format_fish(count: int, locale: String = "") -> String:
	var nb := is_norwegian(locale)
	if count >= 1_000_000:
		return num(count / 1_000_000.0, 1, locale) + (" mill." if nb else "M")
	elif count >= 1_000:
		return num(count / 1_000.0, 1, locale) + ("k" if nb else "K")
	return str(count)


## A calendar date: format_date(2027, 1, 12) → "12. jan. 2027" / "Jan 12, 2027".
static func format_date(year: int, month: int, day: int, locale: String = "") -> String:
	var m := clampi(month, 1, 12) - 1
	if is_norwegian(locale):
		return "%d. %s %d" % [day, _MONTHS_NB[m], year]
	return "%s %d, %d" % [_MONTHS_EN[m], day, year]


## The calendar date of a sim day ({year, month, day}); day 0 = 1 January of
## CALENDAR_START_YEAR, 365-day years (the sim has no leap days).
static func date_for_day(sim_day: int) -> Dictionary:
	var d := maxi(sim_day, 0)
	var year := CALENDAR_START_YEAR + d / 365
	var doy := d % 365
	var month := 0
	while doy >= _MONTH_DAYS[month]:
		doy -= _MONTH_DAYS[month]
		month += 1
	return {"year": year, "month": month + 1, "day": doy + 1}


## The formatted calendar date of a sim day: format_day(11) → "12. jan. 2027".
static func format_day(sim_day: int, locale: String = "") -> String:
	var d := date_for_day(sim_day)
	return format_date(int(d["year"]), int(d["month"]), int(d["day"]), locale)


## Return the season name for a given simulation day.
static func season_name(day: int) -> String:
	var day_of_year: int = day % 365
	if day_of_year < 60 or day_of_year >= 335:
		return "Vinter"
	elif day_of_year < 152:
		return "Vår"
	elif day_of_year < 244:
		return "Sommer"
	return "Høst"


## Return a color for a sustainability level string.
static func sustainability_color(level: String) -> Color:
	match level:
		"SustainabilityLeader": return Color(0.0, 0.8, 0.2)
		"OrganicCertified":     return Color(0.2, 0.7, 0.1)
		"ASCCertified":         return Color(0.4, 0.8, 0.3)
		"BasicCompliance":      return Color(0.9, 0.8, 0.1)
		_:                      return Color(0.9, 0.2, 0.1)


## Return a color for a sea lice level.
static func lice_color(lice_per_fish: float) -> Color:
	if lice_per_fish > 5.0:  return Color.RED
	elif lice_per_fish > 2.0: return Color.ORANGE
	elif lice_per_fish > 0.5: return Color.YELLOW
	return Color.GREEN


## Return a color for balance.
static func balance_color(amount: float) -> Color:
	if amount < 0.0:     return Color.RED
	elif amount < 500_000.0: return Color.YELLOW
	return Color.WHITE


## "71472" → "71 472": groups of three from the right.
static func _group(digits: String, sep: String) -> String:
	var out := ""
	var count := 0
	for i in range(digits.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			out = sep + out
		out = digits[i] + out
		count += 1
	return out
