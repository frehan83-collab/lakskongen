## FinancePanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/FinancePanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## The history list is data-driven and still populated in code.

extends Control

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _chart_title: Label = %ChartTitle
@onready var _pl_title: Label = %PlTitle
@onready var _loan_title: Label = %LoanTitle
@onready var _hist_title: Label = %HistTitle

@onready var _kpi_revenue: Label = %KpiRevenueValue
@onready var _kpi_expenses: Label = %KpiExpensesValue
@onready var _kpi_profit: Label = %KpiProfitValue
@onready var _kpi_balance: Label = %KpiBalanceValue

@onready var _loan_status: Label = %LoanStatus
@onready var _loan_amount: Label = %LoanAmount
@onready var _loan_remaining: Label = %LoanRemaining
@onready var _loan_take_btn: Button = %BtnTakeLoan
@onready var _loan_repay_btn: Button = %BtnRepayLoan

@onready var _history_container: VBoxContainer = %HistoryContainer

# Depth-system rows (P9): pen price + resource rent, built in code.
var _pen_price_label: Label = null
var _resource_rent_title: Label = null
var _resource_rent_label: Label = null

# Insurance section (P11): one row per catastrophe kind, built in code.
var _insurance_card: PanelContainer = null
var _insurance_total_label: Label = null
var _insurance_rows: Array[Dictionary] = []
var _insurance_updating: bool = false

# Causal P&L story (Phase 5.5): "why did I lose money" — top cost drivers and
# revenue sources, built in code and inserted near the top of the panel.
var _story_card: PanelContainer = null
var _story_list: VBoxContainer = null
var _story_verdict: Label = null

const _INSURANCE_KINDS := [
	{"key": "mortality", "label": "Dødelighet", "icon": "🐟"},
	{"key": "bloom", "label": "Oppblomstring", "icon": "🪼"},
	{"key": "storm", "label": "Storm/rømming", "icon": "⛈️"},
	{"key": "isa", "label": "ILA (ISA)", "icon": "🦠"},
]

# P&L rows are static scene labels; collected for future data binding.
var _pl_rows: Dictionary = {}

var _chart_bars_rev: Array[ColorRect] = []
var _chart_bars_cost: Array[ColorRect] = []

signal loan_taken(amount: float)
signal loan_repaid(amount: float)

# ── Previous values for count-up juice (display only) ───────────────────────
var _prev_revenue: float = 0.0
var _prev_expenses: float = 0.0
var _prev_profit: float = 0.0
var _prev_balance: float = 0.0
var _kpi_tweens: Array[Tween] = []

# Balance-trend sparkline (created in code; script loaded lazily).
var _sparkline_script: GDScript = null
var _balance_spark = null
# S2 time-series readouts (finance/market): market-price spark + trend rows.
var _price_spark = null
var _trend_rows: Array = []
var _trend_built: bool = false
const TREND_DEFS := [
	{"key": "balance_nok",   "title": "Kontanter",    "days": 90, "fmt": "short", "up_good": true},
	{"key": "lice_per_fish", "title": "Lus per fisk", "days": 90, "fmt": "ff",    "up_good": false},
	{"key": "fcr",           "title": "FCR",          "days": 90, "fmt": "ff",    "up_good": false},
]

# ── Colors (from design tokens) ──────────────────────────────────────────────
const C_BG := Color("#ECEFF4")
const C_SURFACE := Color("#FFFFFF")
const C_ELEVATED := Color("#F8F9FB")
const C_TEXT := Color("#2E3440")
const C_TEXT2 := Color("#4C566A")
const C_MUTED := Color("#7B8794")
const C_BORDER := Color("#E5E9F0")
const C_PRIMARY := Color("#0A4D68")
const C_SECONDARY := Color("#88C0D0")
const C_SUCCESS := Color("#A3BE8C")
const C_SUCCESS_D := Color("#8FAD78")
const C_WARNING := Color("#EBCB8B")
const C_DANGER := Color("#BF616A")
const C_ACCENT := Color("#5E81AC")

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)

	_collect_scene_refs()
	_build_deep_finance_row()
	_build_balance_sparkline()
	_build_insurance_card()
	_build_story_card()
	_apply_localization()
	_apply_theme_fonts()
	_loan_take_btn.pressed.connect(_on_take_loan)
	_loan_repay_btn.pressed.connect(_on_repay_loan)

	# UIPolish: keyboard navigation for loan buttons row
	var btn_row := get_node_or_null("%BtnRow")
	if btn_row:
		UIPolish.link_horizontal_focus(btn_row)

	_refresh()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _collect_scene_refs() -> void:
	_pl_rows.clear()
	for key in ["Feed", "Salary", "Maintenance", "Insurance", "Delousing", "Transport", "Other", "Total"]:
		_pl_rows[key.to_lower()] = get_node_or_null("%Pl" + key)
	_chart_bars_rev.clear()
	_chart_bars_cost.clear()
	for i in range(1, 7):
		var suffix := str(i)
		_chart_bars_rev.append(get_node_or_null("%ChartRev" + suffix) as ColorRect)
		_chart_bars_cost.append(get_node_or_null("%ChartCost" + suffix) as ColorRect)

## Balance-trend Sparkline near the KPI strip. Parented into the balance KPI's
## VBox (via its value label's parent) so a missing node never crashes.
func _build_balance_sparkline() -> void:
	_sparkline_script = load("res://scripts/ui/Sparkline.gd")
	if _sparkline_script == null or _kpi_balance == null:
		return
	var parent: Node = _kpi_balance.get_parent()
	if parent == null:
		return
	var spark = _sparkline_script.new()
	spark.name = "BalanceSpark"
	spark.line_color = C_ACCENT
	spark.unit_fmt = ""
	spark.custom_minimum_size = Vector2(80, 28)
	parent.add_child(spark)
	_balance_spark = spark

## P9 depth rows: binding monthly pen price and (when enabled) resource-rent tax
## paid. Built in code and inserted under the title so a missing node never crashes.
func _build_deep_finance_row() -> void:
	if _title_label == null:
		return
	var root: Node = _title_label.get_parent()
	if root == null:
		return
	var card := PanelContainer.new()
	card.name = "DeepFinanceRow"
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(10)
	card.add_theme_stylebox_override("panel", sb)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 24)
	card.add_child(row)

	var pp_title := Label.new()
	pp_title.text = tr("Pen price (binding)")
	pp_title.add_theme_color_override("font_color", C_TEXT2)
	pp_title.add_theme_font_size_override("font_size", 12)
	row.add_child(pp_title)
	_pen_price_label = Label.new()
	_pen_price_label.text = "--"
	_pen_price_label.add_theme_color_override("font_color", C_TEXT)
	_pen_price_label.add_theme_font_size_override("font_size", 12)
	row.add_child(_pen_price_label)

	# S2: market price as a colour-coded 30-day time series next to the value.
	if _sparkline_script == null:
		_sparkline_script = load("res://scripts/ui/Sparkline.gd")
	if _sparkline_script != null:
		_price_spark = _sparkline_script.new()
		_price_spark.name = "PriceSpark"
		_price_spark.custom_minimum_size = Vector2(120, 24)
		_price_spark.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		_price_spark.unit_fmt = ""
		row.add_child(_price_spark)

	_resource_rent_title = Label.new()
	_resource_rent_title.text = tr("Ressursrente")
	_resource_rent_title.add_theme_color_override("font_color", C_TEXT2)
	_resource_rent_title.add_theme_font_size_override("font_size", 12)
	_resource_rent_title.visible = false
	row.add_child(_resource_rent_title)
	_resource_rent_label = Label.new()
	_resource_rent_label.text = "--"
	_resource_rent_label.add_theme_color_override("font_color", C_DANGER)
	_resource_rent_label.add_theme_font_size_override("font_size", 12)
	_resource_rent_label.visible = false
	row.add_child(_resource_rent_label)

	root.add_child(card)
	root.move_child(card, 1)

## Insurance section (P11): per-kind enable/coverage/deductible rows. Built in
## code and inserted near the top; every access is guarded so a missing bridge or
## a scene without room degrades to a no-op.
func _build_insurance_card() -> void:
	if _title_label == null or _sim == null:
		return
	if not _sim.has_method("get_insurance_status") or not _sim.has_method("set_insurance_policy"):
		return
	var root: Node = _title_label.get_parent()
	if root == null:
		return

	_insurance_card = PanelContainer.new()
	_insurance_card.name = "InsuranceCard"
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(10)
	_insurance_card.add_theme_stylebox_override("panel", sb)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 8)
	_insurance_card.add_child(outer)

	var header := Label.new()
	header.text = tr("🛡️ Forsikring per katastrofe")
	header.add_theme_font_size_override("font_size", 15)
	header.add_theme_color_override("font_color", C_TEXT)
	outer.add_child(header)

	var hint := Label.new()
	hint.text = tr("Premie betales årlig; dekning og egenandel avgjør utbetaling ved tap.")
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	hint.add_theme_font_size_override("font_size", 11)
	hint.add_theme_color_override("font_color", C_MUTED)
	outer.add_child(hint)

	_insurance_rows.clear()
	for entry in _INSURANCE_KINDS:
		outer.add_child(_build_insurance_row(entry))

	outer.add_child(HSeparator.new())
	_insurance_total_label = Label.new()
	_insurance_total_label.text = tr("Total årspremie: --")
	_insurance_total_label.add_theme_font_size_override("font_size", 13)
	_insurance_total_label.add_theme_color_override("font_color", C_TEXT2)
	outer.add_child(_insurance_total_label)

	root.add_child(_insurance_card)
	var target: int = mini(2, root.get_child_count() - 1)
	root.move_child(_insurance_card, target)

func _build_insurance_row(entry: Dictionary) -> Control:
	var kind := str(entry.get("key", ""))
	var row := VBoxContainer.new()
	row.add_theme_constant_override("separation", 4)

	var top := HBoxContainer.new()
	top.add_theme_constant_override("separation", 10)
	row.add_child(top)

	var toggle := CheckButton.new()
	toggle.text = "%s %s" % [str(entry.get("icon", "")), tr(str(entry.get("label", kind)))]
	toggle.tooltip_text = tr("Slå forsikringen av/på. Premien belastes årlig uansett om det oppstår tap.")
	toggle.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	toggle.toggled.connect(func(_pressed: bool) -> void: _on_insurance_changed(kind))
	top.add_child(toggle)

	var premium := Label.new()
	premium.text = "--"
	premium.add_theme_font_size_override("font_size", 12)
	premium.add_theme_color_override("font_color", C_MUTED)
	premium.custom_minimum_size.x = 120
	premium.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	top.add_child(premium)

	var controls := HBoxContainer.new()
	controls.add_theme_constant_override("separation", 12)
	row.add_child(controls)

	var cov_lbl := Label.new()
	cov_lbl.text = tr("Dekning")
	cov_lbl.add_theme_font_size_override("font_size", 12)
	cov_lbl.add_theme_color_override("font_color", C_MUTED)
	controls.add_child(cov_lbl)

	var coverage := SpinBox.new()
	coverage.min_value = 0.0
	coverage.max_value = 100.0
	coverage.step = 5.0
	coverage.suffix = "%"
	coverage.custom_minimum_size.x = 90
	coverage.tooltip_text = tr("Andel av tap over egenandelen som dekkes. Høyere dekning gir høyere premie.")
	coverage.value_changed.connect(func(_v: float) -> void: _on_insurance_changed(kind))
	controls.add_child(coverage)

	var ded_lbl := Label.new()
	ded_lbl.text = tr("Egenandel")
	ded_lbl.add_theme_font_size_override("font_size", 12)
	ded_lbl.add_theme_color_override("font_color", C_MUTED)
	controls.add_child(ded_lbl)

	var deductible := SpinBox.new()
	deductible.min_value = 0.0
	deductible.max_value = 50_000_000.0
	deductible.step = 100_000.0
	deductible.suffix = " NOK"
	deductible.custom_minimum_size.x = 160
	deductible.tooltip_text = tr("Tap du dekker selv før forsikringen trer inn. Høyere egenandel gir lavere premie.")
	deductible.value_changed.connect(func(_v: float) -> void: _on_insurance_changed(kind))
	controls.add_child(deductible)

	_insurance_rows.append({
		"kind": kind,
		"toggle": toggle,
		"coverage": coverage,
		"deductible": deductible,
		"premium": premium,
	})
	return row

func _on_insurance_changed(kind: String) -> void:
	if _insurance_updating or _sim == null:
		return
	for r in _insurance_rows:
		if str(r.get("kind", "")) != kind:
			continue
		var enabled: bool = (r["toggle"] as CheckButton).button_pressed
		var coverage: float = float((r["coverage"] as SpinBox).value) / 100.0
		var deductible: float = float((r["deductible"] as SpinBox).value)
		if _sim.has_method("set_insurance_policy"):
			_sim.set_insurance_policy(kind, enabled, coverage, deductible)
		break
	_refresh_insurance()

## Push the bridge's policy state back into the controls without re-triggering
## set_insurance_policy (guarded by _insurance_updating).
func _refresh_insurance() -> void:
	if _insurance_card == null or _sim == null or not _sim.has_method("get_insurance_status"):
		return
	var status: Dictionary = _sim.get_insurance_status()
	if status.is_empty():
		return
	_insurance_updating = true
	for r in _insurance_rows:
		var kind := str(r.get("kind", ""))
		var s: Dictionary = status.get(kind, {})
		var enabled := bool(s.get("enabled", false))
		(r["toggle"] as CheckButton).button_pressed = enabled
		(r["coverage"] as SpinBox).value = clampf(float(s.get("coverage", 0.0)) * 100.0, 0.0, 100.0)
		(r["deductible"] as SpinBox).value = maxf(0.0, float(s.get("deductible", 0.0)))
		var premium_lbl := r["premium"] as Label
		premium_lbl.text = ("%s/år" % _fmt(float(s.get("premium", 0.0)))) if enabled else tr("Ingen")
		premium_lbl.add_theme_color_override("font_color", C_TEXT if enabled else C_MUTED)
	_insurance_updating = false
	if _insurance_total_label != null:
		_insurance_total_label.text = tr("Total årspremie: %s") % _fmt(float(status.get("total_premium", 0.0)))

## Causal P&L story (Phase 5.5): a "why did I lose money" card that lists the
## top cost drivers and revenue sources, each with a share bar. Built in code
## (like the insurance card) so a missing bridge or scene degrades to a no-op.
func _build_story_card() -> void:
	if _title_label == null or _sim == null:
		return
	if not _sim.has_method("get_profit_loss_story"):
		return
	var root: Node = _title_label.get_parent()
	if root == null:
		return

	_story_card = PanelContainer.new()
	_story_card.name = "ProfitLossStoryCard"
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(10)
	_story_card.add_theme_stylebox_override("panel", sb)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 6)
	_story_card.add_child(outer)

	var header := Label.new()
	header.text = tr("🧭 Hvorfor gikk det slik?")
	header.add_theme_font_size_override("font_size", 15)
	header.add_theme_color_override("font_color", C_TEXT)
	outer.add_child(header)

	_story_verdict = Label.new()
	_story_verdict.text = ""
	_story_verdict.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_story_verdict.add_theme_font_size_override("font_size", 12)
	_story_verdict.add_theme_color_override("font_color", C_TEXT2)
	outer.add_child(_story_verdict)

	_story_list = VBoxContainer.new()
	_story_list.add_theme_constant_override("separation", 4)
	outer.add_child(_story_list)

	root.add_child(_story_card)
	root.move_child(_story_card, mini(3, root.get_child_count() - 1))

## Map an internal cost/revenue category key to a short Norwegian label.
func _story_label(category: String) -> String:
	var labels := {
		"feed": "Fôr", "delousing": "Avlusing", "insurance": "Forsikring",
		"interest": "Renter", "loan_servicing": "Nedbetaling lån",
		"wages_maintenance": "Lønn & vedlikehold", "mitigation": "Skjørt/skremmer",
		"night_shift": "Nattskift", "operations": "Drift", "tax": "Skatt",
		"harvest": "Slakt", "sludge": "Slam", "other": "Annet",
	}
	var label: String = labels.get(category, "")
	if label != "":
		return label
	# Fall back to a readable version of the raw key.
	return category.replace("_", " ").capitalize()

func _refresh_story() -> void:
	if _story_card == null or _story_list == null or _sim == null:
		return
	if not _sim.has_method("get_profit_loss_story"):
		return
	var story: Dictionary = _sim.get_profit_loss_story(3)

	# Clear previous rows.
	for child in _story_list.get_children():
		child.queue_free()

	var net: float = float(story.get("net_income", 0.0))
	if _story_verdict != null:
		if net >= 0:
			_story_verdict.text = tr("Resultatet er positivt (%.1f MNOK). Største inntekt og kostnad under.") % (net / 1_000_000.0)
		else:
			_story_verdict.text = tr("Resultatet er negativt (%.1f MNOK). Største kostnadsdrivere under.") % (net / 1_000_000.0)

	var costs: Array = story.get("top_costs", [])
	if not costs.is_empty():
		_story_list.add_child(_story_heading(tr("💸 Største kostnader")))
		for item in costs:
			_story_list.add_child(_story_row(item, true))

	var revenues: Array = story.get("top_revenue", [])
	if not revenues.is_empty():
		_story_list.add_child(_story_heading(tr("💰 Største inntekter")))
		for item in revenues:
			_story_list.add_child(_story_row(item, false))

func _story_heading(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 12)
	lbl.add_theme_color_override("font_color", C_MUTED)
	return lbl

## One story row: label, amount, and a share bar. Costs are red, revenue green.
func _story_row(item: Dictionary, is_cost: bool) -> Control:
	var category := str(item.get("category", "other"))
	var amount: float = float(item.get("amount", 0.0))
	var share: float = clampf(float(item.get("share", 0.0)), 0.0, 1.0)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)

	var name_lbl := Label.new()
	name_lbl.text = _story_label(category)
	name_lbl.custom_minimum_size.x = 140
	name_lbl.add_theme_font_size_override("font_size", 12)
	name_lbl.add_theme_color_override("font_color", C_TEXT)
	row.add_child(name_lbl)

	var bar_bg := PanelContainer.new()
	bar_bg.custom_minimum_size = Vector2(120, 8)
	bar_bg.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var bg_style := StyleBoxFlat.new()
	bg_style.bg_color = C_ELEVATED
	bg_style.set_corner_radius_all(4)
	bar_bg.add_theme_stylebox_override("panel", bg_style)
	bar_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(bar_bg)

	var fill := ColorRect.new()
	var fill_color: Color = C_DANGER if is_cost else C_SUCCESS_D
	fill.color = fill_color
	fill.custom_minimum_size = Vector2(120.0 * share, 8)
	fill.size_flags_vertical = Control.SIZE_EXPAND_FILL
	fill.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bar_bg.add_child(fill)

	var amount_lbl := Label.new()
	amount_lbl.text = _fmt_short(amount if is_cost else amount)
	amount_lbl.add_theme_font_size_override("font_size", 12)
	amount_lbl.add_theme_color_override("font_color", fill_color)
	if _theme and _theme.font_mono:
		amount_lbl.add_theme_font_override("font", _theme.font_mono)
	row.add_child(amount_lbl)

	return row

func _apply_localization() -> void:
	_title_label.text = tr("💰 Økonomi")
	_chart_title.text = tr("📊 Inntekter vs Kostnader (6 mnd)")
	_pl_title.text = tr("📋 Resultatregnskap")
	_loan_title.text = tr("🏦 Lån & Finansiering")
	_hist_title.text = tr("📈 Trender (90 d)")
	_loan_take_btn.text = tr("Ta lån (5M NOK)")
	_loan_repay_btn.text = tr("Betal tilbake")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		_title_label.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_revenue.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_expenses.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_profit.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_balance.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_chart_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_pl_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_loan_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_hist_title.add_theme_font_override("font", _theme.font_heading_semibold)
		if _pl_rows.get("total"):
			(_pl_rows["total"] as Label).add_theme_font_override("font", _theme.font_heading_semibold)
	if _theme.font_mono:
		for key in _pl_rows:
			if _pl_rows[key]:
				(_pl_rows[key] as Label).add_theme_font_override("font", _theme.font_mono)

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _exit_tree() -> void:
	# Tab panels are re-instanced on every switch; without this the old
	# lambda/method connections fire on freed panels ("Lambda capture freed").
	if _sim:
		if _sim.has_signal("state_updated") and _sim.state_updated.is_connected(_refresh):
			_sim.state_updated.disconnect(_refresh)
		if _sim.has_signal("day_advanced") and _sim.day_advanced.is_connected(_on_day_advanced):
			_sim.day_advanced.disconnect(_on_day_advanced)


func _on_day_advanced(_d, _s) -> void:
	_refresh()


func _refresh() -> void:
	if not _sim:
		return

	var revenue: float = float(_sim.get_revenue())
	var expenses: float = float(_sim.get_expenses())
	var profit: float = float(_sim.get_profit())
	var balance: float = float(_sim.balance)

	# Juice: count-up tweens on KPI numbers (display only, no sim changes).
	_count_up(_kpi_revenue, _prev_revenue, revenue, Callable(self, "_fmt"))
	_prev_revenue = revenue
	_count_up(_kpi_expenses, _prev_expenses, expenses, Callable(self, "_fmt"))
	_prev_expenses = expenses
	_count_up(_kpi_profit, _prev_profit, profit, Callable(self, "_fmt"))
	_prev_profit = profit
	_count_up(_kpi_balance, _prev_balance, balance, Callable(self, "_fmt"))
	_prev_balance = balance
	_kpi_revenue.add_theme_color_override("font_color", C_SUCCESS_D)

	_kpi_expenses.add_theme_color_override("font_color", C_DANGER)

	_kpi_profit.add_theme_color_override("font_color", C_SUCCESS_D if profit >= 0 else C_DANGER)

	_kpi_balance.add_theme_color_override("font_color", C_TEXT if balance >= 0 else C_DANGER)

	# Loan (F943: the card read a method that did not exist and never updated)
	if _sim.has_method("get_loan_info"):
		var loan: Dictionary = _sim.get_loan_info()
		var has_loan: bool = loan.get("has_loan", false)
		_loan_status.text = (tr("Aktiv") + " (%d)" % int(loan.get("count", 1))) if has_loan else tr("Ingen lån")
		_loan_status.add_theme_color_override("font_color", C_WARNING if has_loan else C_SUCCESS_D)
		_loan_amount.text = _fmt(loan.get("amount", 0.0)) if has_loan else "--"
		_loan_remaining.text = _fmt(loan.get("remaining", 0.0)) if has_loan else "--"
		_loan_take_btn.disabled = float(loan.get("credit_available", LOAN_AMOUNT)) < LOAN_AMOUNT
		_loan_repay_btn.disabled = not has_loan

	# S2: trend block replaces the text history wall (color-coded time series).
	_refresh_trends()

	# Balance-trend sparkline (display only, no sim mutation).
	if _balance_spark != null and is_instance_valid(_balance_spark) and _sim.has_method("get_analytics_data"):
		var series: Array[float] = _sim.get_analytics_data("balance_nok", 30)
		if _balance_spark.has_method("set_values"):
			_balance_spark.call("set_values", series)
		else:
			_balance_spark.set("values", series)

	_refresh_deep_finance()
	_refresh_insurance()
	_refresh_story()

## P9: pen price always shown; resource-rent row only when the scenario enables it.
func _refresh_deep_finance() -> void:
	if _pen_price_label == null or not _sim:
		return
	if _sim.has_method("get_pen_price"):
		_pen_price_label.text = "%.2f NOK/kg" % float(_sim.get_pen_price())
	# S2: price trend (colour = up-good trend; series = DailySnapshots).
	if _price_spark != null and is_instance_valid(_price_spark) and _sim.has_method("get_analytics_data"):
		var ps: Array[float] = _sim.get_analytics_data("market_price_nok", 30)
		_price_spark.call("set_values", ps)
		_price_spark.line_color = _trend_color("market_price_nok", true)
		_price_spark.fill_color = Color(_price_spark.line_color, 0.10)
	if _sim.has_method("get_resource_rent"):
		var rr: Dictionary = _sim.get_resource_rent()
		var enabled: bool = bool(rr.get("enabled", false))
		_resource_rent_title.visible = enabled
		_resource_rent_label.visible = enabled
		if enabled:
			_resource_rent_label.text = _fmt(float(rr.get("paid", 0.0)))

# S2: colour-coded time series (Kontanter / Lus per fisk / FCR, 90 d).
# Replaces the former last-10-days text wall. Data: State.DailySnapshots via
# SimBridge.get_analytics_data — no new sim state.

func _ensure_trend_rows() -> void:
	if _trend_built or _history_container == null:
		return
	_trend_built = true
	_trend_rows.clear()
	if _sparkline_script == null:
		_sparkline_script = load("res://scripts/ui/Sparkline.gd")
	for def in TREND_DEFS:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_history_container.add_child(row)

		var t_lbl := Label.new()
		t_lbl.text = tr(str(def["title"]))
		t_lbl.custom_minimum_size.x = 110
		t_lbl.add_theme_font_size_override("font_size", 12)
		t_lbl.add_theme_color_override("font_color", C_TEXT2)
		row.add_child(t_lbl)

		var spark = null
		if _sparkline_script != null:
			spark = _sparkline_script.new()
			spark.custom_minimum_size = Vector2(140, 26)
			spark.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			spark.unit_fmt = ""
			row.add_child(spark)

		var v_lbl := Label.new()
		v_lbl.text = "--"
		v_lbl.custom_minimum_size.x = 86
		v_lbl.add_theme_font_size_override("font_size", 12)
		v_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_mono:
			v_lbl.add_theme_font_override("font", _theme.font_mono)
		v_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(v_lbl)

		_trend_rows.append({"def": def, "spark": spark, "value": v_lbl})


func _trend_color(metric: String, up_good: bool) -> Color:
	var trend := "stable"
	if _sim != null and _sim.has_method("get_analytics_trend"):
		trend = str(_sim.get_analytics_trend(metric, 14))
	if trend == "stable":
		return C_ACCENT
	var good := (trend == "up") == up_good
	return C_SUCCESS_D if good else C_DANGER


func _refresh_trends() -> void:
	if not _sim:
		return
	_ensure_trend_rows()
	for entry in _trend_rows:
		var def: Dictionary = entry["def"]
		var series: Array[float] = _sim.get_analytics_data(str(def["key"]), int(def["days"]))
		if series.is_empty():
			continue
		var spark = entry["spark"]
		if spark != null and is_instance_valid(spark):
			spark.call("set_values", series)
			spark.line_color = _trend_color(str(def["key"]), bool(def["up_good"]))
			spark.fill_color = Color(spark.line_color, 0.12)
		var last: float = series[series.size() - 1]
		var v_lbl: Label = entry["value"]
		v_lbl.text = _fmt_short(last) if str(def["fmt"]) == "short" else ("%.2f" % last)

# ── Loan Actions ──────────────────────────────────────────────────────────────

## Loan button amounts and the term the bank offers (the bank sets the rate).
const LOAN_AMOUNT := 5_000_000.0
const LOAN_TERM_MONTHS := 36
const REPAY_AMOUNT := 1_000_000.0

func _on_take_loan() -> void:
	if not _sim or not _sim.has_method("take_loan"):
		return
	# All three arguments (F943: the one-argument call failed), and the result is
	# a Dictionary — bool(dict) was true even for a refused loan.
	var result: Dictionary = _sim.take_loan(LOAN_AMOUNT, LOAN_TERM_MONTHS, 0.0)
	if result.get("success", false):
		loan_taken.emit(LOAN_AMOUNT)
		_toast(_tr_or("UI_TOAST_LOAN_TAKEN", "🏦 Lån tatt opp: 5,0 MNOK"), "SUCCESS")
	else:
		_toast("🏦 " + str(result.get("message", "Lånet ble avslått")), "WARNING")
	_refresh()

func _on_repay_loan() -> void:
	if not _sim or not _sim.has_method("repay_loan"):
		return
	var result: Dictionary = _sim.repay_loan(REPAY_AMOUNT)
	if result.get("success", false):
		loan_repaid.emit(float(result.get("repaid", REPAY_AMOUNT)))
		_toast(_tr_or("UI_TOAST_LOAN_REPAID", "🏦 Nedbetaling fullført: 1,0 MNOK"), "SUCCESS")
	else:
		_toast("🏦 " + str(result.get("message", "Nedbetaling avslått")), "WARNING")
	_refresh()

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _toast(message: String, level: String = "INFO") -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, 4.0)

# ── Count-up juice (display only) ───────────────────────────────────────────

func _count_up(lbl: Label, from_val: float, to_val: float, fmt: Callable) -> void:
	if lbl == null:
		return
	if is_equal_approx(from_val, to_val):
		lbl.text = fmt.call(to_val)
		return
	var tween := lbl.create_tween()
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tween.tween_method(func(v: float): lbl.text = fmt.call(v), from_val, to_val, 0.6)
	_kpi_tweens.append(tween)

# ── Formatting ────────────────────────────────────────────────────────────────

func _fmt(amount: float) -> String:
	var a := absf(amount)
	var s := "-" if amount < 0 else ""
	if a >= 1_000_000.0:
		return "%s%.2f MNOK" % [s, a / 1_000_000.0]
	if a >= 1_000.0:
		return "%s%.0f kNOK" % [s, a / 1_000.0]
	return "%s%.0f NOK" % [s, a]

func _fmt_short(amount: float) -> String:
	var a := absf(amount)
	var s := "-" if amount < 0 else ""
	if a >= 1_000_000.0:
		return "%s%.1fM" % [s, a / 1_000_000.0]
	if a >= 1_000.0:
		return "%s%.0fk" % [s, a / 1_000.0]
	return "%s%.0f" % [s, a]
