class_name GameCalendar
extends RefCounted

## GameCalendar — the calendar as the player reads it (roadmap U2-07): the
## HUD's "Uke 14 · april · År 1", the briefing's "Fredag 1. januar 2027" and
## the long date in tooltips. Pure static functions of the sim day.
##
## The rules mirror the engine's one calendar (SimCalendar, roadmap S2-02):
## sim day 0 is Friday 1 January SimFormatters.CALENDAR_START_YEAR, years have
## 365 days, months their real lengths, the weekday moves one day a year, and
## weeks are ISO-8601 weeks (Monday first; week 1 holds the year's first
## Thursday), which is what Norwegian farmers and the lice rules count in.
## Sim year 1 is day for day the real 2027, so its first three days are in
## week 53 (of 2026), as on a Norwegian wall calendar.
## tests/test_game_calendar.py checks the rule against Python's calendar.

## ISO weekday of sim day 0 (1 January 2027): a Friday (Monday = 1).
const DAY0_ISO_WEEKDAY := 5
const DAYS_PER_WEEK := 7
## Days in every sim year (no leap days), as SimCalendar.DaysPerYear.
const DAYS_PER_YEAR := 365
const THURSDAY := 4

const _MONTHS_LONG_NB := ["januar", "februar", "mars", "april", "mai", "juni",
	"juli", "august", "september", "oktober", "november", "desember"]
const _MONTHS_LONG_EN := ["January", "February", "March", "April", "May", "June",
	"July", "August", "September", "October", "November", "December"]
const _WEEKDAYS_NB := ["mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag", "søndag"]
const _WEEKDAYS_EN := ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]


## Zero-based year index (days 0-364 are year 0; earlier days count back).
static func year_index(day: int) -> int:
	return floori(float(day) / float(DAYS_PER_YEAR))


## Day of the year, 0 (1 January) to 364.
static func day_of_year(day: int) -> int:
	return posmod(day, DAYS_PER_YEAR)


## First sim day of the year that holds `day`.
static func year_start(day: int) -> int:
	return year_index(day) * DAYS_PER_YEAR


## The player's year number: "År 1" for the first 365 days.
static func game_year(day: int) -> int:
	return year_index(day) + 1


## Month number, 1 (January) to 12.
static func month(day: int) -> int:
	return int(SimFormatters.date_for_day(day_of_year(day))["month"])


## Day of the month, 1 to 31.
static func day_of_month(day: int) -> int:
	return int(SimFormatters.date_for_day(day_of_year(day))["day"])


## ISO weekday, 1 (Monday) to 7 (Sunday).
static func iso_weekday(day: int) -> int:
	return posmod(DAY0_ISO_WEEKDAY - 1 + day, DAYS_PER_WEEK) + 1


## ISO weeks in the sim year holding `day`: 53 when 1 January is a Thursday
## (a 365-day year starts and ends on the same weekday).
static func iso_weeks_in_year(day: int) -> int:
	return 53 if iso_weekday(year_start(day)) == THURSDAY else 52


## ISO-8601 week number, 1 to 53.
static func iso_week(day: int) -> int:
	var week := (day_of_year(day) + 1 - iso_weekday(day) + 10) / DAYS_PER_WEEK
	if week < 1:
		return iso_weeks_in_year(year_start(day) - 1)
	if week > iso_weeks_in_year(day):
		return 1
	return week


## A month's full name: "april" (nb, lower case as Norwegian writes it) / "April".
static func month_name(month_number: int, locale: String = "") -> String:
	var m := clampi(month_number, 1, 12) - 1
	return _MONTHS_LONG_NB[m] if SimFormatters.is_norwegian(locale) else _MONTHS_LONG_EN[m]


## A weekday's name (1 = Monday): "fredag" / "Friday".
static func weekday_name(iso_wd: int, locale: String = "") -> String:
	var i := clampi(iso_wd, 1, 7) - 1
	return _WEEKDAYS_NB[i] if SimFormatters.is_norwegian(locale) else _WEEKDAYS_EN[i]


## The HUD's calendar: "Uke 14 · april · År 1" / "Week 14 · April · Year 1".
static func hud_label(day: int) -> String:
	return str(TranslationServer.translate("UI_HUD_CALENDAR")) % [iso_week(day), month_name(month(day)), game_year(day)]


## The calendar year of a sim day (2027 for year 1).
static func calendar_year(day: int) -> int:
	return SimFormatters.CALENDAR_START_YEAR + year_index(day)


## "fredag 1. januar 2027" / "Friday, January 1, 2027".
static func long_date(day: int, locale: String = "") -> String:
	var wd := weekday_name(iso_weekday(day), locale)
	if SimFormatters.is_norwegian(locale):
		return "%s %d. %s %d" % [wd, day_of_month(day), month_name(month(day), locale), calendar_year(day)]
	return "%s, %s %d, %d" % [wd, month_name(month(day), locale), day_of_month(day), calendar_year(day)]


## "5. april" / "April 5": a date within the year (deadlines, the next event).
static func short_date(day: int, locale: String = "") -> String:
	if SimFormatters.is_norwegian(locale):
		return "%d. %s" % [day_of_month(day), month_name(month(day), locale)]
	return "%s %d" % [month_name(month(day), locale), day_of_month(day)]
