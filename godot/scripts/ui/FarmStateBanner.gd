extends PanelContainer
class_name FarmStateBanner

## FarmStateBanner — one-line "state of the farm" summary for the Dashboard.
##
## Additive UI only: reads SimBridge snapshot properties and never mutates
## simulation state. The static evaluate() is the single source of truth for
## the healthy / strained / critical decision, shared by Dashboard.gd and
## MorningBriefing.gd so the two surfaces can never disagree.
##
## Deliberately self-contained (no LakskongenTheme autoload reference) so the
## class can be parse-checked in isolation: styling is by theme type variation
## name only ("Callout<Tone>" banner, "<Role><Tone>" labels), resolved on
## whichever surface hosts the banner.
##
## Thresholds mirror SimState._check_alerts where an equivalent exists:
##   lice  : warning @ 0.20, legal limit @ 0.50 per fish
##   O2    : warning @ 6.5, critical @ 6.0 mg/L   (task threshold: under 6)
##   FCR   : strained @ 1.15, over target @ 1.30
##   saldo : low @ 1 000 000, critical when negative

const STATE_HEALTHY := "healthy"
const STATE_STRAINED := "strained"
const STATE_CRITICAL := "critical"

const LICE_STRAINED := 0.2
const LICE_CRITICAL := 0.5
const OXYGEN_STRAINED := 6.5
const OXYGEN_CRITICAL := 6.0
const FCR_STRAINED := 1.15
const FCR_CRITICAL := 1.3
const BALANCE_LOW := 1_000_000.0
const BALANCE_CRITICAL := 0.0

const EMPTY_SUMMARY := "Alt i rute — ingen kritiske avvik"
const EMPTY_DETAIL := "Lus, oksygen, fôr og likviditet innenfor mål"

# State -> theme tone; Chart-surface colours for callers that need a Color.
const TONE_HEALTHY := &"Ok"
const TONE_STRAINED := &"Warn"
const TONE_CRITICAL := &"Crit"
const COLOR_HEALTHY := Color("#2E6B45")
const COLOR_STRAINED := Color("#8A5A00")
const COLOR_CRITICAL := Color("#B3172F")

var _icon: Label = null
var _title: Label = null
var _detail: Label = null
var _built: bool = false
var _state: String = ""

func _ready() -> void:
	_build()

func _build() -> void:
	if _built:
		return
	_built = true
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 10)
	add_child(row)

	_icon = _make_label("●", &"SubheadingOk")
	_icon.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	row.add_child(_icon)

	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 0)
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(col)

	_title = _make_label("", &"BodyStrong")
	col.add_child(_title)

	_detail = _make_label("", &"SmallMuted")
	_detail.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	col.add_child(_detail)

func _make_label(text: String, variation: StringName) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.theme_type_variation = variation
	return lbl

## Update the banner. Text is set every call (cheap) but the pulse only fires
## when the state actually changes, so frequent state_updated signals cannot
## make it spam.
func set_state(state: String, title: String, detail: String = "") -> void:
	if not _built:
		_build()
	var changed := state != _state
	_state = state
	var tone := tone_for(state)
	if _icon:
		_icon.theme_type_variation = StringName("Subheading" + String(tone))
	if _title:
		_title.text = title
	if _detail:
		_detail.text = detail
		_detail.visible = not detail.is_empty()
	theme_type_variation = StringName("Callout" + String(tone))
	if changed and is_inside_tree():
		JuiceEffects.pulse(self, 1.01, 0.25)

static func tone_for(state: String) -> StringName:
	match state:
		STATE_CRITICAL: return TONE_CRITICAL
		STATE_STRAINED: return TONE_STRAINED
		_: return TONE_HEALTHY

static func tint_for(state: String) -> Color:
	match state:
		STATE_CRITICAL: return COLOR_CRITICAL
		STATE_STRAINED: return COLOR_STRAINED
		_: return COLOR_HEALTHY

static func icon_for(state: String) -> String:
	match state:
		STATE_CRITICAL: return "🔴"
		STATE_STRAINED: return "🟡"
		_: return "🟢"

## Evaluate the overall farm state from a SimBridge node.
## Returns:
##   state      : "healthy" | "strained" | "critical"
##   severity   : 0 | 1 | 2
##   factors    : {"lice","oxygen","fcr","balance"} each 0 | 1 | 2
##   worst_key  : the factor driving the severity ("" when healthy)
##   summary_no : Norwegian one-liner (feed to tr() at the call site)
##   detail_no  : Norwegian contributing factors
static func evaluate(sim: Node) -> Dictionary:
	var result := {
		"state": STATE_HEALTHY,
		"severity": 0,
		"factors": {"lice": 0, "oxygen": 0, "fcr": 0, "balance": 0},
		"worst_key": "",
		"summary_no": EMPTY_SUMMARY,
		"detail_no": EMPTY_DETAIL,
	}
	if sim == null:
		return result

	var factors: Dictionary = result["factors"]
	var notes: Array[String] = []

	var lice := _read_float(sim, "sea_lice_per_fish", -1.0)
	if lice >= 0.0:
		if lice >= LICE_CRITICAL:
			factors["lice"] = 2
			notes.append("lus %.2f (grense 0,50)" % lice)
		elif lice >= LICE_STRAINED:
			factors["lice"] = 1
			notes.append("lus %.2f" % lice)

	var oxygen := _read_float(sim, "oxygen_mgl", -1.0)
	if oxygen > 0.0:
		if oxygen < OXYGEN_CRITICAL:
			factors["oxygen"] = 2
			notes.append("O2 %.1f mg/L" % oxygen)
		elif oxygen < OXYGEN_STRAINED:
			factors["oxygen"] = 1
			notes.append("O2 %.1f mg/L" % oxygen)

	var fcr := _read_float(sim, "fcr", -1.0)
	if fcr > 0.0:
		if fcr > FCR_CRITICAL:
			factors["fcr"] = 2
			notes.append("FCR %.2f (mål 1,30)" % fcr)
		elif fcr > FCR_STRAINED:
			factors["fcr"] = 1
			notes.append("FCR %.2f" % fcr)

	var balance := _read_float(sim, "balance", NAN)
	if is_finite(balance):
		if balance < BALANCE_CRITICAL:
			factors["balance"] = 2
			notes.append("saldo negativ")
		elif balance > 0.0 and balance < BALANCE_LOW:
			factors["balance"] = 1
			notes.append("lav likviditet")

	var worst_key := ""
	var worst_sev := 0
	for key in ["lice", "oxygen", "fcr", "balance"]:
		var sev := int(factors.get(key, 0))
		if sev > worst_sev:
			worst_sev = sev
			worst_key = key

	result["factors"] = factors
	result["severity"] = worst_sev
	result["worst_key"] = worst_key
	if worst_sev >= 2:
		result["state"] = STATE_CRITICAL
		result["summary_no"] = "Kritisk tilstand — handling kreves nå"
	elif worst_sev == 1:
		result["state"] = STATE_STRAINED
		result["summary_no"] = "Driften under press — følg med"

	if not notes.is_empty():
		var detail := ""
		for i in range(notes.size()):
			if i > 0:
				detail += " · "
			detail += notes[i]
		result["detail_no"] = detail
	return result

static func _read_float(sim: Node, prop: String, fallback: float) -> float:
	if sim == null:
		return fallback
	var v = sim.get(prop)
	if v == null:
		return fallback
	var f := float(v)
	return f if is_finite(f) else fallback
