extends PanelContainer
class_name TelemetryPanel

## TelemetryPanel — "operations telemetry" that makes the deep simulation legible.
##
## Read-only view over SimBridge. Every value is guarded: when the bridge, a
## proxy or a single field is missing the row shows "–" instead of crashing.
##
## Sections: Water/O₂, Lice & resistance, Gill health, Feed & FCR, Smolt
## quality, Insurance, Market, Regulation/tax. Trends use the existing
## Sparkline / LineChart primitives.

const LICE_LIMIT := 0.5          ## Regulatory sea-lice limit (lice/fish).
const O2_CRITICAL := 5.0         ## mg/L — appetite/growth collapse.
const O2_LOW := 6.5              ## mg/L — appetite starts falling.
const DO_HISTORY_MAX := 60       ## Rolling samples kept for the DO sparkline.

@onready var _root: VBoxContainer = %RootVBox

var _sim: Node = null
var _theme: Node = null
var _do_history: Array[float] = []

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)
	refresh()

## GameUI calls this each time the tab becomes visible (panels are cached).
func on_panel_shown() -> void:
	refresh()

func _on_state_updated() -> void:
	refresh()

func _on_day_advanced(_day: int, _season: String) -> void:
	refresh()

# ── Build ─────────────────────────────────────────────────────────────────────

func refresh() -> void:
	if _root == null:
		return
	_clear(_root)
	_track_oxygen()
	if _sim == null:
		_add_unavailable("SimBridge ikke tilgjengelig")
		return

	_build_header()
	_build_water()
	_build_lice()
	_build_gill()
	_build_feed()
	_build_smolt()
	_build_insurance()
	_build_market()
	_build_regulation()

func _build_header() -> void:
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 8)
	_root.add_child(head)

	var title := Label.new()
	title.text = "📡 Driftstelemetri"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_style_label(title, true, LakskongenTheme.FONT_HEADER)
	head.add_child(title)

	var day := _intval("current_day")
	var day_lbl := Label.new()
	day_lbl.text = ("Dag %d" % day) if day >= 0 else "Dag –"
	_style_label(day_lbl, false, LakskongenTheme.FONT_SM)
	LakskongenTheme.set_text_color(day_lbl, LakskongenTheme.TEXT_SECONDARY)
	head.add_child(day_lbl)

	var btn := Button.new()
	btn.text = "↻"
	btn.tooltip_text = "Oppdater telemetri"
	btn.pressed.connect(refresh)
	head.add_child(btn)

# ── Water / O₂ ────────────────────────────────────────────────────────────────

func _build_water() -> void:
	var body := _add_section("🌊 Vann & Oksygen")

	var temp := _num("temperature_c")
	var temp_txt := "–"
	var temp_col := LakskongenTheme.TEXT_PRIMARY
	if not is_nan(temp):
		temp_txt = SimFormatters.fmt("%.1f °C", temp)
		if temp > 18.0 or temp < 2.0:
			temp_col = LakskongenTheme.DANGER
		elif temp > 16.0 or temp < 5.0:
			temp_col = LakskongenTheme.WARNING
	_add_row(body, "Temperatur", temp_txt, temp_col)

	var do_mgl := _num("oxygen_mgl")
	var o2_txt := "–"
	var o2_col := LakskongenTheme.TEXT_MUTED
	if not is_nan(do_mgl) and do_mgl > 0.0:
		o2_txt = SimFormatters.fmt("%.1f mg/L", do_mgl)
		if do_mgl < O2_CRITICAL:
			o2_col = LakskongenTheme.DANGER
		elif do_mgl < O2_LOW:
			o2_col = LakskongenTheme.WARNING
		else:
			o2_col = LakskongenTheme.SUCCESS
	_add_row(body, "Oppløst oksygen (DO)", o2_txt, o2_col)

	if not is_nan(do_mgl) and do_mgl > 0.0:
		# The sim's seawater solubility at the zone's own temperature; the panel
		# kept a copy of the old freshwater table and drifted from it (S1-01).
		var sat_mgl := _num("oxygen_saturation_mgl")
		if not is_nan(sat_mgl) and sat_mgl > 0.0:
			var sat_frac := clampf(do_mgl / sat_mgl, 0.0, 1.5)
			var sat_col := LakskongenTheme.SUCCESS
			if sat_frac < 0.60:
				sat_col = LakskongenTheme.DANGER
			elif sat_frac < 0.80:
				sat_col = LakskongenTheme.WARNING
			_add_row(body, "Oksygenmetning", SimFormatters.percent(sat_frac), sat_col)
		else:
			_add_row(body, "Oksygenmetning", "–", LakskongenTheme.TEXT_MUTED)
	else:
		_add_row(body, "Oksygenmetning", "–", LakskongenTheme.TEXT_MUTED)

	_add_row(body, "O₂-status", _o2_status(do_mgl), o2_col)

	if _do_history.size() >= 2:
		var sp := Sparkline.new()
		sp.baseline = O2_CRITICAL
		sp.unit_fmt = "%.1f"
		sp.line_color = LakskongenTheme.WATER_BLUE
		sp.fill_color = Color(LakskongenTheme.WATER_BLUE, 0.12)
		body.add_child(sp)
		sp.values = _do_history
		sp.custom_minimum_size = Vector2(0, 44)
		sp.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	else:
		_add_note(body, "DO-trend samles opp fra live-verdier.")

func _o2_status(v: float) -> String:
	if is_nan(v) or v <= 0.0:
		return "Ingen data"
	if v < O2_CRITICAL:
		return "🔴 Kritisk lavt — reduser fôring"
	if v < O2_LOW:
		return "🟡 Lavt — følg med"
	return "🟢 Innenfor toleranse"

## Sample the live DO value so the sparkline has history even though the
## analytics tracker does not record a dissolved-oxygen metric.
func _track_oxygen() -> void:
	if not _has("oxygen_mgl"):
		return
	var o := float(_sim.get("oxygen_mgl"))
	if not is_finite(o) or o <= 0.0:
		return
	if _do_history.is_empty() or absf(_do_history[_do_history.size() - 1] - o) > 0.0001:
		_do_history.append(o)
		while _do_history.size() > DO_HISTORY_MAX:
			_do_history.remove_at(0)

# ── Lice & resistance ─────────────────────────────────────────────────────────

func _build_lice() -> void:
	var body := _add_section("🦠 Lus & resistens")

	var lice := _num("sea_lice_per_fish")
	var lice_txt := "–"
	var lice_col := LakskongenTheme.TEXT_MUTED
	if not is_nan(lice):
		lice_txt = SimFormatters.fmt("%.2f / fisk (grense %.2f)", [lice, LICE_LIMIT])
		if lice >= LICE_LIMIT:
			lice_col = LakskongenTheme.DANGER
		elif lice >= 0.2:
			lice_col = LakskongenTheme.WARNING
		else:
			lice_col = LakskongenTheme.SUCCESS
	_add_row(body, "Lusetrykk", lice_txt, lice_col)

	var series := _analytics("lice_per_fish", 30)
	if _has_finite(series):
		var lc := LineChart.new()
		lc.title = "Lus per fisk (30 d)"
		lc.threshold = LICE_LIMIT
		lc.unit_fmt = "%.2f"
		lc.line_color = LakskongenTheme.WARNING_DARK
		body.add_child(lc)
		lc.set_series(series)
		lc.custom_minimum_size = Vector2(0, 140)
		lc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	else:
		_add_row(body, "Trend (30 d)", "–", LakskongenTheme.TEXT_MUTED)

	var res = _callp("get_resistance_levels", {})
	if res is Dictionary and not (res as Dictionary).is_empty():
		_add_subheader(body, "Resistens (0–1 · høyere = svakere behandling)")
		for key in ["pyrethroid", "organophosphate", "peroxide", "mechanical"]:
			if not (res as Dictionary).has(key):
				continue
			var v := float((res as Dictionary).get(key, 0.0))
			var col := LakskongenTheme.SUCCESS
			if v >= 0.66:
				col = LakskongenTheme.DANGER
			elif v >= 0.33:
				col = LakskongenTheme.WARNING
			_add_bar(body, _res_label(key), v, col, SimFormatters.percent(v))
	else:
		_add_row(body, "Resistens", "–", LakskongenTheme.TEXT_MUTED)

func _res_label(key: String) -> String:
	match key:
		"pyrethroid": return "Pyretroider"
		"organophosphate": return "Organofosfat"
		"peroxide": return "Peroksid"
		"mechanical": return "Mekanisk"
		_: return key

# ── Gill health ───────────────────────────────────────────────────────────────

func _build_gill() -> void:
	var body := _add_section("🫁 Gjellehelse")
	if _sim == null or not _sim.has_method("get_all_pens") or not _sim.has_method("get_gill_risk"):
		_add_row(body, "Status", "– (ikke tilgjengelig)", LakskongenTheme.TEXT_MUTED)
		return

	var worst_risk := -1.0
	var worst_name := ""
	var worst_reason := ""
	var worst_crit := false
	var worst_warn := false
	var warnings := 0
	for pen in _sim.get_all_pens():
		if not (pen is Dictionary):
			continue
		var sid := str((pen as Dictionary).get("id", ""))
		if sid.is_empty():
			continue
		var g = _sim.get_gill_risk(sid)
		if not (g is Dictionary):
			continue
		var r := float((g as Dictionary).get("risk", 0.0))
		if bool((g as Dictionary).get("warning", false)):
			warnings += 1
		if r > worst_risk:
			worst_risk = r
			worst_name = str((pen as Dictionary).get("name", sid))
			worst_reason = str((g as Dictionary).get("reason", ""))
			worst_crit = bool((g as Dictionary).get("critical", false))
			worst_warn = bool((g as Dictionary).get("warning", false))

	if worst_risk < 0.0:
		_add_row(body, "Status", "Ingen lokaliteter", LakskongenTheme.TEXT_MUTED)
		return

	var col := LakskongenTheme.SUCCESS
	var tag := "🟢"
	if worst_crit:
		col = LakskongenTheme.DANGER
		tag = "🔴"
	elif worst_warn:
		col = LakskongenTheme.WARNING
		tag = "🟡"
	_add_row(body, "Verste lokalitet", "%s %s" % [tag, worst_name], col)
	_add_row(body, "Risiko", SimFormatters.percent(worst_risk), col)
	_add_bar(body, "Risikonivå", worst_risk, col)
	if not worst_reason.is_empty():
		_add_row(body, "Årsak", SimLabels.gill_reason(worst_reason), LakskongenTheme.TEXT_SECONDARY)
	_add_row(body, "Varsler", ("%d lokalitet" if warnings == 1 else "%d lokaliteter") % warnings,
		col if warnings > 0 else LakskongenTheme.TEXT_SECONDARY)

# ── Feed & FCR ────────────────────────────────────────────────────────────────

func _build_feed() -> void:
	var body := _add_section("🍽️ Fôr & FCR")

	# Target and ceiling come from CalibrationRegistry through the bridge (one
	# FCR target for every panel); fresh smolts are not judged (U1-08).
	var fcr := _num("fcr")
	var fcr_target := _num("fcr_target")
	var fcr_poor := _num("fcr_poor")
	var settled: bool = _sim != null and _sim.has_method("fcr_is_settled") and bool(_sim.fcr_is_settled())
	var fcr_txt := "–"
	var fcr_col := LakskongenTheme.TEXT_MUTED
	if not is_nan(fcr) and fcr > 0.0:
		fcr_txt = SimFormatters.fmt("%.2f (mål %.2f)", [fcr, fcr_target])
		if not settled:
			fcr_txt += " · smolten er i tilvenning"
		elif fcr > fcr_poor:
			fcr_col = LakskongenTheme.DANGER
		elif fcr > fcr_target:
			fcr_col = LakskongenTheme.WARNING
		else:
			fcr_col = LakskongenTheme.SUCCESS
	_add_row(body, "Fôrfaktor (FCR)", fcr_txt, fcr_col)

	var series := _analytics("fcr", 30)
	if _has_finite(series):
		var lc := LineChart.new()
		lc.title = "FCR (30 d)"
		lc.threshold = fcr_target
		lc.unit_fmt = "%.2f"
		lc.line_color = LakskongenTheme.ACCENT
		body.add_child(lc)
		lc.set_series(series)
		lc.custom_minimum_size = Vector2(0, 150)
		lc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	else:
		_add_row(body, "Trend (30 d)", "–", LakskongenTheme.TEXT_MUTED)

	_add_subheader(body, "Hva driver FCR nå?")
	# U2-04: the ration and recipe are each site's feeding plan.
	var plans = _callp("get_feed_plans", [])
	if plans is Array and not (plans as Array).is_empty():
		var custom := (plans as Array).filter(func(p: Dictionary) -> bool: return not bool(p.get("standard", true))).size()
		var plan_text := tr("UI_AIFEED_PLAN_ALL_STANDARD") % (plans as Array).size() if custom == 0 \
			else tr("UI_AIFEED_PLAN_SOME_CUSTOM") % [custom, (plans as Array).size()]
		_add_row(body, tr("FEEDPLAN_TITLE"), plan_text, LakskongenTheme.TEXT_SECONDARY)
	else:
		_add_row(body, tr("FEEDPLAN_TITLE"), "–", LakskongenTheme.TEXT_MUTED)

	var temp := _num("temperature_c")
	if not is_nan(temp):
		_add_row(body, "Temperatur", SimFormatters.fmt("%.1f °C — %s", [temp, _temp_phrase(temp)]), LakskongenTheme.TEXT_SECONDARY)
	else:
		_add_row(body, "Temperatur", "–", LakskongenTheme.TEXT_MUTED)

	var dens := _density_fraction()
	if dens >= 0.0:
		var dcol := LakskongenTheme.SUCCESS
		if dens > 0.90:
			dcol = LakskongenTheme.DANGER
		elif dens > 0.75:
			dcol = LakskongenTheme.WARNING
		_add_row(body, "Tetthet", "%s av MTB — %s" % [SimFormatters.percent(dens), _density_phrase(dens)], dcol)
	else:
		_add_row(body, "Tetthet", "–", LakskongenTheme.TEXT_MUTED)

	var mix := _derive_smolt_mix()
	if mix.is_empty():
		_add_row(body, "Smoltkvalitet", "– (kohortmiks ikke tilgjengelig)", LakskongenTheme.TEXT_MUTED)
	else:
		_add_row(body, "Smoltkvalitet", _mix_summary(mix), LakskongenTheme.TEXT_SECONDARY)

func _temp_phrase(t: float) -> String:
	if t < 4.0:
		return "kaldt vann — treg fordøyelse, høyere FCR"
	if t < 8.0:
		return "kjølig — redusert appetitt"
	if t <= 16.0:
		return "gunstig temperatursone"
	if t <= 18.0:
		return "varmt — økt metabolisme"
	return "for varmt — stress øker FCR"

func _density_fraction() -> float:
	if _sim == null or not _sim.has_method("get_all_pens"):
		return -1.0
	var biomass := 0.0
	var max_biomass := 0.0
	for pen in _sim.get_all_pens():
		if not (pen is Dictionary):
			continue
		biomass += float((pen as Dictionary).get("biomass_kg", 0.0))
		max_biomass += float((pen as Dictionary).get("max_biomass", 0.0))
	if max_biomass <= 0.0:
		return -1.0
	return clampf(biomass / max_biomass, 0.0, 2.0)

func _density_phrase(d: float) -> String:
	if d > 0.90:
		return "høy biomasseutnyttelse"
	if d > 0.75:
		return "nær kapasitet"
	return "god margin"

# ── Smolt quality mix ─────────────────────────────────────────────────────────

func _build_smolt() -> void:
	var body := _add_section("🐣 Smoltkvalitet")

	var mix := _derive_smolt_mix()
	if not mix.is_empty():
		_add_row(body, "Kohorter etter kvalitet", _mix_summary(mix), LakskongenTheme.TEXT_PRIMARY)
	else:
		_add_note(body, "Kohortmiks er ikke eksponert — viser kvalitetskatalogen.")

	_add_subheader(body, "Kvalitetskatalog")
	var tiers = _callp("get_smolt_quality_tiers", [])
	if tiers is Array and not (tiers as Array).is_empty():
		for t in tiers:
			if not (t is Dictionary):
				continue
			var td := t as Dictionary
			var name := SimLabels.smolt_tier(str(td.get("id", "")))
			var mort := float(td.get("mortality_mult", 1.0))
			var growth := float(td.get("growth_mult", 1.0))
			var quality := float(td.get("quality", 0.0))
			_add_row(body, name,
				SimFormatters.fmt("dødelighet ×%.2f · vekst ×%.2f · kvalitet %s", [mort, growth, SimFormatters.percent(quality)]),
				LakskongenTheme.TEXT_SECONDARY)
	else:
		_add_row(body, "Katalog", "–", LakskongenTheme.TEXT_MUTED)

## Best-effort cohort-tier count from get_all_pens(). The current bridge does not
## expose per-cohort quality, so this usually returns {} (catalogue shown instead).
func _derive_smolt_mix() -> Dictionary:
	var out: Dictionary = {}
	if _sim == null or not _sim.has_method("get_all_pens"):
		return out
	for pen in _sim.get_all_pens():
		if not (pen is Dictionary):
			continue
		var d := pen as Dictionary
		for key in ["quality_tier", "smolt_tier", "quality", "tier"]:
			if d.has(key):
				var tier := _tier_name(d.get(key))
				if not tier.is_empty():
					out[tier] = int(out.get(tier, 0)) + 1
				break
	return out

func _tier_name(v) -> String:
	if v is int or v is float:
		match int(v):
			1: return "Large"
			2: return "SuperSmolt"
			_: return "Standard"
	return str(v)

func _mix_summary(mix: Dictionary) -> String:
	var parts := PackedStringArray()
	for tier in ["Standard", "Large", "SuperSmolt"]:
		if mix.has(tier):
			parts.append("%s: %d" % [SimLabels.smolt_tier(tier), int(mix[tier])])
	for tier in mix.keys():
		if tier not in ["Standard", "Large", "SuperSmolt"]:
			parts.append("%s: %d" % [SimLabels.smolt_tier(str(tier)), int(mix[tier])])
	if parts.is_empty():
		return "–"
	return ", ".join(parts)

# ── Insurance ─────────────────────────────────────────────────────────────────

func _build_insurance() -> void:
	var body := _add_section("🛡️ Forsikring")
	var status = _callp("get_insurance_status", {})
	if not (status is Dictionary) or (status as Dictionary).is_empty():
		_add_row(body, "Status", "– (ikke tilgjengelig)", LakskongenTheme.TEXT_MUTED)
		return

	var st := status as Dictionary
	_add_row(body, "Total årspremie", _format_nok(float(st.get("total_premium", 0.0))), LakskongenTheme.TEXT_PRIMARY)

	var kinds := [
		["mortality", "Dødelighet"],
		["bloom", "Algeblomst"],
		["storm", "Storm"],
		["isa", "ILA"],
	]
	for entry in kinds:
		var key: String = entry[0]
		var label: String = entry[1]
		if not st.has(key):
			_add_row(body, label, "–", LakskongenTheme.TEXT_MUTED)
			continue
		var info = st[key]
		if not (info is Dictionary):
			_add_row(body, label, "–", LakskongenTheme.TEXT_MUTED)
			continue
		var id := info as Dictionary
		if bool(id.get("enabled", false)):
			var prem := float(id.get("premium", 0.0))
			var cov := float(id.get("coverage", 0.0))
			_add_row(body, label,
				"✅ Aktiv · dekning %s · %s" % [SimFormatters.percent(cov), _format_nok(prem)],
				LakskongenTheme.SUCCESS)
		else:
			_add_row(body, label, "— Ikke tegnet", LakskongenTheme.TEXT_MUTED)

# ── Market ────────────────────────────────────────────────────────────────────

func _build_market() -> void:
	var body := _add_section("💹 Marked")

	var pen_price := float(_callp("get_pen_price", 0.0))
	if pen_price > 0.0:
		_add_row(body, "Bindende pen-pris", SimFormatters.fmt("%.2f NOK/kg", pen_price), LakskongenTheme.TEXT_PRIMARY)
	else:
		_add_row(body, "Bindende pen-pris", "–", LakskongenTheme.TEXT_MUTED)

	if _has("market_price_nok"):
		var mp := _num("market_price_nok")
		if not is_nan(mp):
			_add_row(body, "Spotmarkedspris", SimFormatters.fmt("%.2f NOK/kg", mp), LakskongenTheme.TEXT_SECONDARY)

	var pressure := float(_callp("get_player_supply_pressure", -1.0))
	if pressure >= 0.0:
		var col := LakskongenTheme.SUCCESS
		if pressure > 0.66:
			col = LakskongenTheme.DANGER
		elif pressure > 0.33:
			col = LakskongenTheme.WARNING
		_add_bar(body, "Spillertilbud-press", pressure, col, SimFormatters.percent(pressure))
		_add_row(body, "Prispåvirkning", _pressure_phrase(pressure), col)
	else:
		_add_row(body, "Spillertilbud-press", "– (ikke tilgjengelig)", LakskongenTheme.TEXT_MUTED)

func _pressure_phrase(p: float) -> String:
	if p > 0.66:
		return "høyt tilbud — presser spotprisen ned"
	if p > 0.33:
		return "økende tilbud — følg med"
	return "nøytralt/lavt press"

# ── Regulation / tax ──────────────────────────────────────────────────────────

func _build_regulation() -> void:
	var body := _add_section("🚦 Regulering & avgift")

	var zones = _get_zone_lights()
	if zones is Array and not (zones as Array).is_empty():
		var green := 0
		var yellow := 0
		var red := 0
		for z in zones:
			if not (z is Dictionary):
				continue
			var c := str((z as Dictionary).get("color", "")).to_lower()
			if c == "red":
				red += 1
			elif c == "yellow":
				yellow += 1
			else:
				green += 1
		_add_row(body, "Trafikklys-soner", "🟢 %d · 🟡 %d · 🔴 %d" % [green, yellow, red], LakskongenTheme.TEXT_PRIMARY)
	else:
		_add_row(body, "Trafikklys-soner", "–", LakskongenTheme.TEXT_MUTED)

	var rent = _callp("get_resource_rent", {})
	if rent is Dictionary and not (rent as Dictionary).is_empty():
		var r := rent as Dictionary
		if bool(r.get("enabled", false)):
			_add_row(body, "Ressursrenteskatt",
				"Aktiv · betalt %s" % _format_nok(float(r.get("paid", 0.0))),
				LakskongenTheme.WARNING_DARK)
		else:
			_add_row(body, "Ressursrenteskatt", "Ikke aktiv", LakskongenTheme.TEXT_MUTED)
	else:
		_add_row(body, "Ressursrenteskatt", "–", LakskongenTheme.TEXT_MUTED)

	var sus := _num("sustainability_score")
	if not is_nan(sus):
		var scol := LakskongenTheme.SUCCESS
		if sus < 50.0:
			scol = LakskongenTheme.DANGER
		elif sus < 70.0:
			scol = LakskongenTheme.WARNING
		_add_row(body, "Bærekraftscore", "%d / 100" % int(sus), scol)
	else:
		_add_row(body, "Bærekraftscore", "–", LakskongenTheme.TEXT_MUTED)

	var budget = _callp("get_attention_budget", {})
	if budget is Dictionary and not (budget as Dictionary).is_empty():
		var b := budget as Dictionary
		var cap := float(b.get("budget", 0.0))
		var used := float(b.get("used", 0.0))
		if cap > 0.0:
			var col := LakskongenTheme.SUCCESS if used <= cap else LakskongenTheme.DANGER
			_add_bar(body, "ROC-oppmerksomhet", clampf(used / cap, 0.0, 1.0), col,
				"%d/%d" % [int(used), int(cap)])

## Prefer the dedicated proxy; fall back to the mirrored traffic_zones property.
func _get_zone_lights() -> Array:
	if _sim != null and _sim.has_method("get_zone_traffic_lights"):
		var a = _sim.get_zone_traffic_lights()
		if a is Array and not a.is_empty():
			return a
	if _has("traffic_zones"):
		var t = _sim.get("traffic_zones")
		if t is Array:
			return t
	return []

# ── Bridge accessors (all guarded) ────────────────────────────────────────────

func _has(prop: String) -> bool:
	return _sim != null and (prop in _sim)

func _num(prop: String) -> float:
	if _has(prop):
		var v = _sim.get(prop)
		if v is float or v is int:
			return float(v)
	return NAN

func _intval(prop: String) -> int:
	if _has(prop):
		var v = _sim.get(prop)
		if v is int or v is float:
			return int(v)
	return -1

func _callp(method: String, fallback):
	if _sim != null and _sim.has_method(method):
		return _sim.call(method)
	return fallback

func _analytics(metric: String, days: int) -> Array[float]:
	var out: Array[float] = []
	if _sim == null or not _sim.has_method("get_analytics_data"):
		return out
	var raw = _sim.get_analytics_data(metric, days)
	if raw is Array:
		out.assign(raw)
	return out

func _has_finite(arr: Array) -> bool:
	for v in arr:
		if (v is float or v is int) and is_finite(float(v)):
			return true
	return false

func _format_nok(v: float) -> String:
	if is_nan(v):
		return "–"
	if _sim != null and _sim.has_method("format_nok"):
		return str(_sim.format_nok(v))
	return "%.0f NOK" % v

# ── Small UI builders ─────────────────────────────────────────────────────────

func _clear(node: Node) -> void:
	for c in node.get_children():
		node.remove_child(c)
		c.queue_free()

func _add_unavailable(msg: String) -> void:
	var lbl := Label.new()
	lbl.text = msg
	_style_label(lbl, false, LakskongenTheme.FONT_BODY)
	LakskongenTheme.set_text_color(lbl, LakskongenTheme.TEXT_MUTED)
	_root.add_child(lbl)

func _add_section(title: String) -> VBoxContainer:
	var card := PanelContainer.new()
	var card_sb := StyleBoxFlat.new()
	card_sb.bg_color = LakskongenTheme.SURFACE
	card_sb.border_color = LakskongenTheme.BORDER
	card_sb.set_border_width_all(1)
	card_sb.set_corner_radius_all(LakskongenTheme.RADIUS_MD)
	card.add_theme_stylebox_override("panel", card_sb)
	_root.add_child(card)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	card.add_child(outer)

	var header := PanelContainer.new()
	var head_sb := StyleBoxFlat.new()
	head_sb.bg_color = LakskongenTheme.SURFACE_ELEVATED
	head_sb.border_color = LakskongenTheme.BORDER
	head_sb.border_width_bottom = 1
	head_sb.set_corner_radius_all(LakskongenTheme.RADIUS_MD)
	head_sb.content_margin_left = 14
	head_sb.content_margin_right = 14
	head_sb.content_margin_top = 8
	head_sb.content_margin_bottom = 8
	header.add_theme_stylebox_override("panel", head_sb)
	outer.add_child(header)

	var title_lbl := Label.new()
	title_lbl.text = title
	_style_label(title_lbl, true, LakskongenTheme.FONT_BODY)
	header.add_child(title_lbl)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 14)
	margin.add_theme_constant_override("margin_right", 14)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 12)
	outer.add_child(margin)

	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 6)
	margin.add_child(body)
	return body

func _add_subheader(body: VBoxContainer, text: String) -> void:
	var lbl := Label.new()
	lbl.text = text
	_style_label(lbl, true, LakskongenTheme.FONT_XS)
	LakskongenTheme.set_text_color(lbl, LakskongenTheme.ACCENT)
	body.add_child(lbl)

func _add_note(body: VBoxContainer, text: String) -> void:
	var lbl := Label.new()
	lbl.text = text
	lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_style_label(lbl, false, LakskongenTheme.FONT_XS)
	LakskongenTheme.set_text_color(lbl, LakskongenTheme.TEXT_MUTED)
	body.add_child(lbl)

func _add_row(body: VBoxContainer, label: String, value: String,
		color: Color = Color(0.180392, 0.203922, 0.25098)) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	body.add_child(row)

	var l := Label.new()
	l.text = label
	l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_style_label(l, false, LakskongenTheme.FONT_SM)
	LakskongenTheme.set_text_color(l, LakskongenTheme.TEXT_SECONDARY)
	row.add_child(l)

	var v := Label.new()
	v.text = value
	v.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_style_label(v, true, LakskongenTheme.FONT_SM)
	LakskongenTheme.set_text_color(v, color)
	row.add_child(v)
	return row

func _add_bar(body: VBoxContainer, label: String, frac: float, color: Color,
		value_text: String = "") -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	body.add_child(row)

	var l := Label.new()
	l.text = label
	l.custom_minimum_size = Vector2(150, 0)
	_style_label(l, false, LakskongenTheme.FONT_XS)
	LakskongenTheme.set_text_color(l, LakskongenTheme.TEXT_SECONDARY)
	row.add_child(l)

	var pb := ProgressBar.new()
	pb.min_value = 0.0
	pb.max_value = 1.0
	pb.value = clampf(frac, 0.0, 1.0)
	pb.show_percentage = false
	pb.custom_minimum_size = Vector2(0, 12)
	pb.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(LakskongenTheme.BORDER, 0.6)
	bg.set_corner_radius_all(4)
	var fill := StyleBoxFlat.new()
	fill.bg_color = color
	fill.set_corner_radius_all(4)
	pb.add_theme_stylebox_override("background", bg)
	pb.add_theme_stylebox_override("fill", fill)
	row.add_child(pb)

	if not value_text.is_empty():
		var v := Label.new()
		v.text = value_text
		v.custom_minimum_size = Vector2(56, 0)
		v.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		_style_label(v, false, LakskongenTheme.FONT_XS)
		LakskongenTheme.set_text_color(v, color)
		row.add_child(v)

func _style_label(lbl: Label, heading: bool, size_px: int) -> void:
	if heading:
		LakskongenTheme.set_strong(lbl)
	LakskongenTheme.set_size(lbl, size_px)
