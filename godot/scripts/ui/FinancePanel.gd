## FinancePanel v2.0 — Nordic Minimalist
## Matches finance-prototype.html: P&L overview, balance, cost breakdown,
## investor info, insurance status, and financial chart.
## Fully programmatic UI, connected to SimBridge.

extends Control

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── UI Refs ───────────────────────────────────────────────────────────────────
# KPI row
var _kpi_revenue: Label
var _kpi_expenses: Label
var _kpi_profit: Label
var _kpi_balance: Label

# P&L table
var _pl_rows: Dictionary = {}

# Loan section
var _loan_status: Label
var _loan_amount: Label
var _loan_remaining: Label
var _loan_take_btn: Button
var _loan_repay_btn: Button

# History
var _history_container: VBoxContainer

# Chart
var _chart_bars_rev: Array[ColorRect] = []
var _chart_bars_cost: Array[ColorRect] = []

signal loan_taken(amount: float)
signal loan_repaid(amount: float)

# ── Colors (from design tokens) ──────────────────────────────────────────────
const C_BG         := Color("#ECEFF4")
const C_SURFACE    := Color("#FFFFFF")
const C_ELEVATED   := Color("#F8F9FB")
const C_TEXT       := Color("#2E3440")
const C_TEXT2      := Color("#4C566A")
const C_MUTED      := Color("#7B8794")
const C_BORDER     := Color("#E5E9F0")
const C_PRIMARY    := Color("#0A4D68")
const C_SECONDARY  := Color("#88C0D0")
const C_SUCCESS    := Color("#A3BE8C")
const C_SUCCESS_D  := Color("#8FAD78")
const C_WARNING    := Color("#EBCB8B")
const C_DANGER     := Color("#BF616A")
const C_ACCENT     := Color("#5E81AC")

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(func(_d, _s): _refresh())

	_build_ui()
	_refresh()
	print("FinancePanel v2.0: ready")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ══════════════════════════════════════════════════════════════════════════════
# UI CONSTRUCTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	# Background
	var bg := Panel.new()
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = C_BG
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	# Scroll
	var scroll := ScrollContainer.new()
	scroll.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	scroll.offset_left = 16; scroll.offset_top = 16
	scroll.offset_right = -16; scroll.offset_bottom = -16
	add_child(scroll)

	var root := VBoxContainer.new()
	root.size_flags_horizontal = SIZE_EXPAND_FILL
	root.add_theme_constant_override("separation", 20)
	scroll.add_child(root)

	# Title
	var title := Label.new()
	title.text = tr("💰 Økonomi")
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		title.add_theme_font_override("font", _theme.font_heading_bold)
	root.add_child(title)

	# ── KPI Strip ──
	var kpi_strip := HBoxContainer.new()
	kpi_strip.add_theme_constant_override("separation", 16)
	root.add_child(kpi_strip)

	var k1 := _make_kpi("INNTEKTER", "--", C_SUCCESS)
	_kpi_revenue = k1.value
	kpi_strip.add_child(k1.card)

	var k2 := _make_kpi("KOSTNADER", "--", C_DANGER)
	_kpi_expenses = k2.value
	kpi_strip.add_child(k2.card)

	var k3 := _make_kpi("RESULTAT", "--", C_PRIMARY)
	_kpi_profit = k3.value
	kpi_strip.add_child(k3.card)

	var k4 := _make_kpi("KONTANTER", "--", C_ACCENT)
	_kpi_balance = k4.value
	kpi_strip.add_child(k4.card)

	# ── Main grid: Chart + P&L ──
	var mid_row := HBoxContainer.new()
	mid_row.add_theme_constant_override("separation", 16)
	root.add_child(mid_row)

	# Revenue/Cost chart (left)
	var chart_card := _make_card("📊 Inntekter vs Kostnader (6 mnd)")
	chart_card.size_flags_horizontal = SIZE_EXPAND_FILL
	chart_card.size_flags_stretch_ratio = 1.5
	mid_row.add_child(chart_card)
	var chart_body := _card_body(chart_card)
	_build_chart(chart_body)

	# P&L breakdown (right)
	var pl_card := _make_card("📋 Resultatregnskap")
	pl_card.size_flags_horizontal = SIZE_EXPAND_FILL
	mid_row.add_child(pl_card)
	var pl_body := _card_body(pl_card)
	_build_pl_table(pl_body)

	# ── Bottom row: Loans + History ──
	var bot_row := HBoxContainer.new()
	bot_row.add_theme_constant_override("separation", 16)
	root.add_child(bot_row)

	# Loans
	var loan_card := _make_card("🏦 Lån & Finansiering")
	loan_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bot_row.add_child(loan_card)
	var loan_body := _card_body(loan_card)
	_build_loan_section(loan_body)

	# History
	var hist_card := _make_card("📈 Historikk (siste 10 dager)")
	hist_card.size_flags_horizontal = SIZE_EXPAND_FILL
	hist_card.size_flags_stretch_ratio = 1.5
	bot_row.add_child(hist_card)
	_history_container = _card_body(hist_card)

# ── KPI Card ──────────────────────────────────────────────────────────────────

func _make_kpi(title: String, initial: String, stripe_color: Color) -> Dictionary:
	var card := PanelContainer.new()
	card.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = stripe_color
	sb.set_border_width_all(1)
	sb.border_width_top = 4
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(16)
	card.add_theme_stylebox_override("panel", sb)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	card.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", C_MUTED)
	vbox.add_child(lbl)

	var val := Label.new()
	val.text = initial
	val.add_theme_font_size_override("font_size", 24)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		val.add_theme_font_override("font", _theme.font_heading_bold)
	vbox.add_child(val)

	return {"card": card, "value": val}

# ── Chart ─────────────────────────────────────────────────────────────────────

func _build_chart(body: VBoxContainer) -> void:
	var chart := HBoxContainer.new()
	chart.custom_minimum_size.y = 120
	chart.add_theme_constant_override("separation", 8)
	chart.alignment = BoxContainer.ALIGNMENT_END
	body.add_child(chart)

	_chart_bars_rev.clear()
	_chart_bars_cost.clear()

	var months := ["Okt", "Nov", "Des", "Jan", "Feb", "Mar"]
	for i in range(6):
		var col := VBoxContainer.new()
		col.size_flags_horizontal = SIZE_EXPAND_FILL
		col.alignment = BoxContainer.ALIGNMENT_END
		chart.add_child(col)

		var bars := HBoxContainer.new()
		bars.add_theme_constant_override("separation", 2)
		bars.alignment = BoxContainer.ALIGNMENT_CENTER
		col.add_child(bars)

		var rev := ColorRect.new()
		rev.color = C_PRIMARY
		rev.custom_minimum_size = Vector2(14, 30 + i * 10)
		bars.add_child(rev)
		_chart_bars_rev.append(rev)

		var cost := ColorRect.new()
		cost.color = C_SECONDARY
		cost.custom_minimum_size = Vector2(14, 20 + i * 5)
		bars.add_child(cost)
		_chart_bars_cost.append(cost)

		var m_lbl := Label.new()
		m_lbl.text = months[i]
		m_lbl.add_theme_font_size_override("font_size", 10)
		m_lbl.add_theme_color_override("font_color", C_MUTED)
		m_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		col.add_child(m_lbl)

	# Legend
	var legend := HBoxContainer.new()
	legend.add_theme_constant_override("separation", 16)
	body.add_child(legend)
	legend.add_child(_legend_dot(C_PRIMARY, "Inntekt"))
	legend.add_child(_legend_dot(C_SECONDARY, "Kostnad"))

func _legend_dot(color: Color, text: String) -> HBoxContainer:
	var h := HBoxContainer.new()
	h.add_theme_constant_override("separation", 6)
	var d := ColorRect.new()
	d.custom_minimum_size = Vector2(10, 10)
	d.color = color
	h.add_child(d)
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 12)
	l.add_theme_color_override("font_color", C_TEXT2)
	h.add_child(l)
	return h

# ── P&L Table ─────────────────────────────────────────────────────────────────

func _build_pl_table(body: VBoxContainer) -> void:
	_pl_rows.clear()
	var items := [
		{"key": "feed", "label": "Fôrkostnader", "type": "expense"},
		{"key": "salary", "label": "Lønnskostnader", "type": "expense"},
		{"key": "maintenance", "label": "Vedlikehold", "type": "expense"},
		{"key": "insurance", "label": "Forsikring", "type": "expense"},
		{"key": "delousing", "label": "Avlusing", "type": "expense"},
		{"key": "transport", "label": "Transport", "type": "expense"},
		{"key": "other", "label": "Andre kostnader", "type": "expense"},
	]
	for item in items:
		var row := _make_pl_row(item.label, "--")
		body.add_child(row.container)
		_pl_rows[item.key] = row.value

	# Separator
	var sep := HSeparator.new()
	body.add_child(sep)

	# Totals
	var total_row := _make_pl_row("TOTALE KOSTNADER", "--")
	total_row.value.add_theme_color_override("font_color", C_DANGER)
	if _theme and _theme.font_heading_semibold:
		total_row.value.add_theme_font_override("font", _theme.font_heading_semibold)
	body.add_child(total_row.container)
	_pl_rows["total"] = total_row.value

func _make_pl_row(label_text: String, value_text: String) -> Dictionary:
	var h := HBoxContainer.new()
	var l := Label.new()
	l.text = label_text
	l.add_theme_font_size_override("font_size", 13)
	l.add_theme_color_override("font_color", C_TEXT2)
	l.size_flags_horizontal = SIZE_EXPAND_FILL
	h.add_child(l)

	var v := Label.new()
	v.text = value_text
	v.add_theme_font_size_override("font_size", 13)
	v.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_mono:
		v.add_theme_font_override("font", _theme.font_mono)
	v.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	h.add_child(v)

	return {"container": h, "value": v}

# ── Loan Section ──────────────────────────────────────────────────────────────

func _build_loan_section(body: VBoxContainer) -> void:
	var status_row := _make_pl_row("Status:", "Ingen lån")
	body.add_child(status_row.container)
	_loan_status = status_row.value

	var amt_row := _make_pl_row("Lånebeløp:", "--")
	body.add_child(amt_row.container)
	_loan_amount = amt_row.value

	var rem_row := _make_pl_row("Gjenstående:", "--")
	body.add_child(rem_row.container)
	_loan_remaining = rem_row.value

	var btn_row := HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 10)
	body.add_child(btn_row)

	_loan_take_btn = Button.new()
	_loan_take_btn.text = tr("Ta lån (5M NOK)")
	_loan_take_btn.size_flags_horizontal = SIZE_EXPAND_FILL
	var take_sb := StyleBoxFlat.new()
	take_sb.bg_color = C_PRIMARY
	take_sb.set_corner_radius_all(4)
	take_sb.set_content_margin_all(10)
	_loan_take_btn.add_theme_stylebox_override("normal", take_sb)
	_loan_take_btn.add_theme_color_override("font_color", Color.WHITE)
	_loan_take_btn.add_theme_font_size_override("font_size", 13)
	_loan_take_btn.pressed.connect(_on_take_loan)
	btn_row.add_child(_loan_take_btn)

	_loan_repay_btn = Button.new()
	_loan_repay_btn.text = tr("Betal tilbake")
	_loan_repay_btn.size_flags_horizontal = SIZE_EXPAND_FILL
	_loan_repay_btn.disabled = true
	var repay_sb := StyleBoxFlat.new()
	repay_sb.bg_color = C_ELEVATED
	repay_sb.border_color = C_BORDER
	repay_sb.set_border_width_all(1)
	repay_sb.set_corner_radius_all(4)
	repay_sb.set_content_margin_all(10)
	_loan_repay_btn.add_theme_stylebox_override("normal", repay_sb)
	_loan_repay_btn.add_theme_color_override("font_color", C_TEXT)
	_loan_repay_btn.add_theme_font_size_override("font_size", 13)
	_loan_repay_btn.pressed.connect(_on_repay_loan)
	btn_row.add_child(_loan_repay_btn)

# ══════════════════════════════════════════════════════════════════════════════
# CARD HELPER
# ══════════════════════════════════════════════════════════════════════════════

func _make_card(title: String) -> PanelContainer:
	var card := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", sb)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	card.add_child(outer)

	# Header
	var header := PanelContainer.new()
	var h_sb := StyleBoxFlat.new()
	h_sb.bg_color = C_SURFACE
	h_sb.border_color = C_BORDER
	h_sb.border_width_bottom = 1
	h_sb.set_content_margin(SIDE_LEFT, 20)
	h_sb.set_content_margin(SIDE_RIGHT, 20)
	h_sb.set_content_margin(SIDE_TOP, 14)
	h_sb.set_content_margin(SIDE_BOTTOM, 14)
	header.add_theme_stylebox_override("panel", h_sb)
	outer.add_child(header)

	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 15)
	t.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_semibold:
		t.add_theme_font_override("font", _theme.font_heading_semibold)
	header.add_child(t)

	# Body
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 10)
	outer.add_child(body)

	return card

func _card_body(card: PanelContainer) -> VBoxContainer:
	return card.get_child(0).get_child(1) as VBoxContainer

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	if not _sim:
		return

	var revenue: float = float(_sim.get_revenue())
	var expenses: float = float(_sim.get_expenses())
	var profit: float = float(_sim.get_profit())
	var balance: float = float(_sim.balance)

	_kpi_revenue.text = _fmt(revenue)
	_kpi_revenue.add_theme_color_override("font_color", C_SUCCESS_D)

	_kpi_expenses.text = _fmt(expenses)
	_kpi_expenses.add_theme_color_override("font_color", C_DANGER)

	_kpi_profit.text = _fmt(profit)
	_kpi_profit.add_theme_color_override("font_color", C_SUCCESS_D if profit >= 0 else C_DANGER)

	_kpi_balance.text = _fmt(balance)
	_kpi_balance.add_theme_color_override("font_color", C_TEXT if balance >= 0 else C_DANGER)

	# Loan
	if _sim.has_method("get_loan_info"):
		var loan := _sim.get_loan_info()
		var has_loan: bool = loan.get("has_loan", false)
		_loan_status.text = tr("Aktiv")
		_loan_status.add_theme_color_override("font_color", C_WARNING if has_loan else C_SUCCESS_D)
		_loan_amount.text = _fmt(loan.get("amount", 0.0)) if has_loan else "--"
		_loan_remaining.text = _fmt(loan.get("remaining", 0.0)) if has_loan else "--"
		_loan_take_btn.disabled = has_loan
		_loan_repay_btn.disabled = not has_loan

	# History
	if _sim.has_method("get_financial_history"):
		_refresh_history(_sim.get_financial_history(10))

func _refresh_history(history: Array) -> void:
	if not _history_container:
		return
	for child in _history_container.get_children():
		child.queue_free()

	for entry in history:
		var day: int = entry.get("day", 0)
		var rev: float = entry.get("revenue", 0.0)
		var exp: float = entry.get("expenses", 0.0)
		var bal: float = entry.get("balance", 0.0)

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		_history_container.add_child(row)

		var d_lbl := Label.new()
		d_lbl.text = tr("Dag %d") % day
		d_lbl.custom_minimum_size.x = 60
		d_lbl.add_theme_font_size_override("font_size", 12)
		d_lbl.add_theme_color_override("font_color", C_MUTED)
		row.add_child(d_lbl)

		var r_lbl := Label.new()
		r_lbl.text = "+%s" % _fmt_short(rev)
		r_lbl.add_theme_font_size_override("font_size", 12)
		r_lbl.add_theme_color_override("font_color", C_SUCCESS_D)
		if _theme and _theme.font_mono:
			r_lbl.add_theme_font_override("font", _theme.font_mono)
		row.add_child(r_lbl)

		var e_lbl := Label.new()
		e_lbl.text = "-%s" % _fmt_short(exp)
		e_lbl.add_theme_font_size_override("font_size", 12)
		e_lbl.add_theme_color_override("font_color", C_DANGER)
		if _theme and _theme.font_mono:
			e_lbl.add_theme_font_override("font", _theme.font_mono)
		row.add_child(e_lbl)

		var b_lbl := Label.new()
		b_lbl.text = _fmt_short(bal)
		b_lbl.add_theme_font_size_override("font_size", 12)
		b_lbl.add_theme_color_override("font_color", C_TEXT if bal >= 0 else C_DANGER)
		if _theme and _theme.font_mono:
			b_lbl.add_theme_font_override("font", _theme.font_mono)
		b_lbl.size_flags_horizontal = SIZE_EXPAND_FILL
		b_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(b_lbl)

# ── Loan Actions ──────────────────────────────────────────────────────────────

func _on_take_loan() -> void:
	if not _sim or not _sim.has_method("take_loan"):
		return
	var success := _sim.take_loan(5_000_000.0)
	if success:
		loan_taken.emit(5_000_000.0)
		_refresh()

func _on_repay_loan() -> void:
	if not _sim or not _sim.has_method("repay_loan"):
		return
	var success := _sim.repay_loan(1_000_000.0)
	if success:
		loan_repaid.emit(1_000_000.0)
		_refresh()

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
