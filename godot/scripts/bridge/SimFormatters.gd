class_name SimFormatters
extends RefCounted

## SimFormatters — pure presentation helpers extracted from SimBridge.
## Stateless: every function is static. No signals (nothing to emit).


## Format a NOK amount with kNOK / MNOK suffix.
static func format_nok(amount: float) -> String:
	var abs_amt: float = absf(amount)
	var sign: String = "-" if amount < 0.0 else ""
	if abs_amt >= 1_000_000_000.0:
		return sign + "%.2f GNOK" % (abs_amt / 1_000_000_000.0)
	elif abs_amt >= 1_000_000.0:
		return sign + "%.2f MNOK" % (abs_amt / 1_000_000.0)
	elif abs_amt >= 1_000.0:
		return sign + "%.0f kNOK" % (abs_amt / 1_000.0)
	return sign + "%.0f NOK" % abs_amt


## Format a biomass/weight value with t (tonne) suffix.
static func format_biomass(kg: float) -> String:
	if kg >= 1_000_000.0:
		return "%.1f kt" % (kg / 1_000_000.0)
	elif kg >= 1_000.0:
		return "%.1f t" % (kg / 1_000.0)
	return "%.0f kg" % kg


## Format a fish count with K suffix.
static func format_fish(count: int) -> String:
	if count >= 1_000_000:
		return "%.1fM" % (count / 1_000_000.0)
	elif count >= 1_000:
		return "%.1fK" % (count / 1_000.0)
	return str(count)


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
