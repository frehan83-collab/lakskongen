extends CanvasLayer

## AttentionInbox — Unified "what needs my attention" inbox.
## Data source: the C# aggregator (GameBridge.GetAttentionItems via
## SimBridge.get_attention_items) plus a market-drop item computed here.
## Opened by GameBridge.AttentionInboxRequested (emitted from
## OpenAttentionInbox), the HUD bell button, or `toggle_attention_inbox` (I).
## Layout: scenes/ui/AttentionInbox.tscn

@onready var _overlay: ColorRect = %Overlay
@onready var _dialog_panel: PanelContainer = %DialogPanel
@onready var _header_label: Label = %HeaderLabel
@onready var _scroll: ScrollContainer = %Scroll
@onready var _list_container: VBoxContainer = %ListContainer
@onready var _empty_label: Label = %EmptyLabel
@onready var _close_button: Button = %CloseButton
@onready var _refresh_button: Button = %RefreshButton
@onready var _auto_refresh_toggle: CheckButton = %AutoRefreshToggle

var _sim_bridge: Node = null
var _game_bridge: Node = null
var _attention_items: Array[Dictionary] = []
var _auto_refresh_enabled: bool = true

func _ready() -> void:
	visible = false
	_sim_bridge = get_node_or_null("/root/SimBridge")
	_game_bridge = get_node_or_null("/root/GameBridge")
	_setup_signal_hooks()
	_apply_localization()
	_close_button.pressed.connect(_on_close_pressed)
	_refresh_button.pressed.connect(_on_refresh_pressed)
	_auto_refresh_toggle.toggled.connect(_on_auto_refresh_toggled)
	_overlay.gui_input.connect(_on_overlay_gui_input)
	_refresh_attention_items(true)

func _setup_signal_hooks() -> void:
	# SimBridge signals (these all exist — see SimBridge.gd signal block).
	if _sim_bridge:
		if _sim_bridge.has_signal("day_advanced"):
			_sim_bridge.day_advanced.connect(_on_day_advanced)
		if _sim_bridge.has_signal("state_updated"):
			_sim_bridge.state_updated.connect(_on_state_updated)
		if _sim_bridge.has_signal("alert_raised"):
			_sim_bridge.alert_raised.connect(_on_alert_raised)
	# Open requests from GameBridge.OpenAttentionInbox() (signal is declared in
	# GameBridge.Farm.cs — same direct-connect pattern SimBridge uses).
	if _game_bridge:
		_game_bridge.AttentionInboxRequested.connect(open_inbox)

func _apply_localization() -> void:
	_header_label.text = _tr_or("UI_ATTENTION_INBOX_TITLE", "🔔 Attention Inbox")
	_empty_label.text = _tr_or("UI_ATTENTION_INBOX_EMPTY", "No items needing attention right now. ✨")
	_refresh_button.text = _tr_or("UI_ATTENTION_INBOX_REFRESH", "🔄 Refresh")
	_auto_refresh_toggle.text = _tr_or("UI_ATTENTION_INBOX_AUTO_REFRESH", "🔄 Auto-refresh")
	_close_button.text = _tr_or("UI_ATTENTION_INBOX_CLOSE", "✕")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

# ── Signal handlers (event-driven refresh while visible) ──

func _on_day_advanced(_day: int, _season: String) -> void:
	_refresh_attention_items()

func _on_state_updated() -> void:
	_refresh_attention_items()

func _on_alert_raised(_level: String, _message: String) -> void:
	_refresh_attention_items()

func _on_auto_refresh_toggled(enabled: bool) -> void:
	_auto_refresh_enabled = enabled
	if enabled and visible:
		_refresh_attention_items(true)

func _on_refresh_pressed() -> void:
	_refresh_attention_items(true)

func _on_overlay_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		visible = false

# ── Core logic ──

## Refreshes the inbox. Event-driven calls are skipped while hidden or when
## auto-refresh is off; opening the inbox and the Refresh button always force.
func _refresh_attention_items(force: bool = false) -> void:
	if not force:
		if not visible or not _auto_refresh_enabled:
			return
	_attention_items = _collect_attention_items()
	_build_ui()

func _collect_attention_items() -> Array[Dictionary]:
	var items: Array[Dictionary] = []

	# 1. C# aggregator: interventions, gill health, finance, lice, weather, contracts.
	if _sim_bridge and _sim_bridge.has_method("get_attention_items"):
		var source: Array = _sim_bridge.get_attention_items()
		for entry in source:
			if entry is Dictionary:
				items.append(entry)

	# 2. Market drop (computed here — the C# aggregator has no market items).
	if _sim_bridge and _sim_bridge.has_method("get_market_report"):
		var report: Dictionary = _sim_bridge.get_market_report()
		var current := float(report.get("current_price", -1.0))
		var weekly := float(report.get("weekly_avg", -1.0))
		if current > 0.0 and weekly > 0.0:
			var change_pct := (current - weekly) / weekly * 100.0
			if change_pct <= -5.0:
				var desc_template := _tr_or("UI_ATTENTION_MARKET_DROP_DESC", "Spot price {pct}% below the weekly average — review hedging and harvest timing")
				items.append({
					"type": "market_drop",
					"priority": "high",
					"category": "market",
					"title": _tr_or("UI_ATTENTION_MARKET_DROP", "Market Price Drop"),
					"description": desc_template.format({"pct": "%.1f" % change_pct}),
					"callback": "open_market_panel"
				})

	# Sort by priority: critical > high > medium > low.
	var priority_order: Dictionary = {"critical": 0, "high": 1, "medium": 2, "low": 3}
	items.sort_custom(_compare_priority.bind(priority_order))

	# Limit to top 10 (rebuilt into a typed array — slice() returns untyped).
	var limited: Array[Dictionary] = []
	for i in range(mini(10, items.size())):
		limited.append(items[i])
	return limited

func _compare_priority(a: Dictionary, b: Dictionary, priority_order: Dictionary) -> int:
	var pa: int = priority_order.get(str(a.get("priority", "low")), 3)
	var pb: int = priority_order.get(str(b.get("priority", "low")), 3)
	if pa != pb:
		return pa - pb
	# Secondary sort by category (first byte is stable and allocation-free).
	var cat_a: String = str(a.get("category", ""))
	var cat_b: String = str(b.get("category", ""))
	if cat_a.length() > 0 and cat_b.length() > 0:
		return int(cat_a[0]) - int(cat_b[0])
	return 0

# ── UI building ──

func _build_ui() -> void:
	# Clear existing items.
	for child in _list_container.get_children():
		child.queue_free()

	if _attention_items.is_empty():
		_empty_label.visible = true
		_scroll.visible = false
		return
	_empty_label.visible = false
	_scroll.visible = true

	# Group by category.
	var grouped: Dictionary = {}
	for item in _attention_items:
		var cat := str(item.get("category", "other"))
		if not grouped.has(cat):
			grouped[cat] = []
		grouped[cat].append(item)

	var category_order: Array = ["health", "finance", "intervention", "decision", "weather", "market", "contract", "other"]

	for cat in category_order:
		if not grouped.has(cat):
			continue
		var cat_items: Array = grouped[cat]
		_list_container.add_child(_create_category_header(cat, cat_items.size()))
		for item in cat_items:
			_list_container.add_child(_create_attention_item(item))

func _create_category_header(category: String, count: int) -> HBoxContainer:
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)

	var icons: Dictionary = {
		"health": "🏥", "finance": "💰", "intervention": "🎯", "decision": "⚠️",
		"weather": "🌤️", "market": "📈", "contract": "📋", "other": "📌"
	}
	var labels: Dictionary = {
		"health": _tr_or("UI_ATTENTION_CAT_HEALTH", "Fish Health"),
		"finance": _tr_or("UI_ATTENTION_CAT_FINANCE", "Finance"),
		"intervention": _tr_or("UI_ATTENTION_CAT_INTERVENTION", "Optimization"),
		"decision": _tr_or("UI_ATTENTION_CAT_DECISION", "Decisions"),
		"weather": _tr_or("UI_ATTENTION_CAT_WEATHER", "Weather"),
		"market": _tr_or("UI_ATTENTION_CAT_MARKET", "Market"),
		"contract": _tr_or("UI_ATTENTION_CAT_CONTRACT", "Contracts"),
		"other": _tr_or("UI_ATTENTION_CAT_OTHER", "Other")
	}

	var icon: String = icons.get(category, "📌")
	var label_text: String = labels.get(category, str(category).to_upper())

	var icon_label := LakskongenTheme.create_label("%s %s (%d)" % [icon, label_text, count], LakskongenTheme.FONT_SM, LakskongenTheme.ACCENT)
	header.add_child(icon_label)

	# Category color indicator strip.
	var color_strip := PanelContainer.new()
	var strip_style := StyleBoxFlat.new()
	strip_style.bg_color = _category_color(category)
	strip_style.set_corner_radius_all(4)
	color_strip.custom_minimum_size = Vector2(4, 20)
	color_strip.add_theme_stylebox_override("panel", strip_style)
	color_strip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(color_strip)

	return header

func _category_color(category: String) -> Color:
	match category:
		"health": return LakskongenTheme.DANGER
		"finance": return LakskongenTheme.WARNING
		"intervention": return LakskongenTheme.ACCENT
		"decision": return LakskongenTheme.DANGER
		"weather": return Color(0.6, 0.4, 1.0)
		"market": return LakskongenTheme.ACCENT2
		"contract": return LakskongenTheme.ACCENT
		_: return LakskongenTheme.MUTED

func _create_priority_badge(priority: String) -> Label:
	var texts: Dictionary = {"critical": "🔴 CRITICAL", "high": "🟠 HIGH", "medium": "🟡 MEDIUM", "low": "🔵 LOW"}
	var accents: Dictionary = {
		"critical": LakskongenTheme.DANGER, "high": LakskongenTheme.WARNING,
		"medium": LakskongenTheme.ACCENT2, "low": LakskongenTheme.MUTED
	}
	var text_colors: Dictionary = {
		"critical": LakskongenTheme.DANGER_DARK, "high": Color("#9A7B1F"),
		"medium": Color("#4A6A8A"), "low": LakskongenTheme.TEXT_MUTED
	}
	var accent: Color = accents.get(priority, LakskongenTheme.MUTED)

	var badge := Label.new()
	badge.text = texts.get(priority, str(priority).to_upper())
	badge.add_theme_color_override("font_color", text_colors.get(priority, LakskongenTheme.TEXT_MUTED))
	badge.add_theme_font_size_override("font_size", LakskongenTheme.FONT_XS)
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var badge_style := StyleBoxFlat.new()
	badge_style.bg_color = accent.lightened(0.4)
	badge_style.border_color = accent
	badge_style.set_border_width_all(1)
	badge_style.set_corner_radius_all(4)
	badge_style.content_margin_left = 6
	badge_style.content_margin_right = 6
	badge_style.content_margin_top = 1
	badge_style.content_margin_bottom = 1
	badge.add_theme_stylebox_override("normal", badge_style)

	return badge

func _create_attention_item(item: Dictionary) -> PanelContainer:
	var priority := str(item.get("priority", "low"))
	var title := str(item.get("title", ""))
	var description := str(item.get("description", ""))
	var site_id := str(item.get("site_id", ""))
	var callback := str(item.get("callback", ""))
	var delta_nok := float(item.get("delta_nok", 0.0))
	var days_out := int(item.get("days_out", 0))

	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = LakskongenTheme.SURFACE
	style.border_color = _priority_border_color(priority)
	style.border_width_left = 4
	style.border_width_right = 1
	style.border_width_top = 1
	style.border_width_bottom = 1
	style.set_corner_radius_all(LakskongenTheme.RADIUS_SM)
	style.content_margin_left = LakskongenTheme.SPACE_MD
	style.content_margin_right = LakskongenTheme.SPACE_MD
	style.content_margin_top = LakskongenTheme.SPACE_SM
	style.content_margin_bottom = LakskongenTheme.SPACE_SM
	panel.add_theme_stylebox_override("panel", style)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE

	# Title row with priority badge (+ optional site badge).
	var title_row := HBoxContainer.new()
	title_row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)

	var title_label := LakskongenTheme.create_label(title, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT_PRIMARY)
	title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(title_label)
	title_row.add_child(_create_priority_badge(priority))

	if site_id != "":
		var site_badge := Label.new()
		site_badge.text = "📍 %s" % site_id
		site_badge.add_theme_color_override("font_color", LakskongenTheme.TEXT_MUTED)
		site_badge.add_theme_font_size_override("font_size", LakskongenTheme.FONT_XS)
		site_badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		title_row.add_child(site_badge)

	vbox.add_child(title_row)

	# Description.
	var desc_label := LakskongenTheme.create_label(description, LakskongenTheme.FONT_SM, LakskongenTheme.TEXT_SECONDARY)
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(desc_label)

	# Meta row: Δ NOK, days out, category tag.
	var meta_row := HBoxContainer.new()
	meta_row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)

	if delta_nok != 0.0:
		var delta_label := LakskongenTheme.create_label(
			"Δ %s" % _format_nok(delta_nok), LakskongenTheme.FONT_XS,
			LakskongenTheme.SUCCESS_DARK if delta_nok > 0 else LakskongenTheme.DANGER_DARK)
		meta_row.add_child(delta_label)

	if days_out > 0:
		var days_label := LakskongenTheme.create_label(
			"📅 %d %s" % [days_out, _tr_or("UI_ATTENTION_DAYS", "days")],
			LakskongenTheme.FONT_XS, LakskongenTheme.TEXT_MUTED)
		meta_row.add_child(days_label)

	var cat_label := LakskongenTheme.create_label(
		str(item.get("category", "")).to_upper(), LakskongenTheme.FONT_XS,
		_category_color(str(item.get("category", "other"))))
	meta_row.add_child(cat_label)

	vbox.add_child(meta_row)

	# Action button.
	if callback != "":
		var action_btn := Button.new()
		action_btn.text = _tr_or("UI_ATTENTION_ACTION_VIEW", "View Details →")
		action_btn.custom_minimum_size = Vector2(140, 28)
		var btn_style := StyleBoxFlat.new()
		btn_style.bg_color = LakskongenTheme.PRIMARY
		btn_style.set_corner_radius_all(4)
		btn_style.content_margin_left = 10
		btn_style.content_margin_right = 10
		btn_style.content_margin_top = 3
		btn_style.content_margin_bottom = 3
		action_btn.add_theme_stylebox_override("normal", btn_style)
		var btn_hover := btn_style.duplicate()
		btn_hover.bg_color = LakskongenTheme.PRIMARY_LIGHT
		action_btn.add_theme_stylebox_override("hover", btn_hover)
		action_btn.add_theme_color_override("font_color", LakskongenTheme.TEXT_INVERSE)
		action_btn.add_theme_color_override("font_hover_color", Color.WHITE)
		action_btn.pressed.connect(_on_item_action.bind(callback, str(item.get("action_id", "")), site_id))
		vbox.add_child(action_btn)

	panel.add_child(vbox)

	# Hover effect (light card → elevated).
	panel.mouse_entered.connect(func():
		var hover_style := style.duplicate()
		hover_style.bg_color = LakskongenTheme.SURFACE_ELEVATED
		hover_style.border_color = LakskongenTheme.ACCENT
		panel.add_theme_stylebox_override("panel", hover_style)
	)
	panel.mouse_exited.connect(func():
		panel.add_theme_stylebox_override("panel", style)
	)

	return panel

func _priority_border_color(priority: String) -> Color:
	match priority:
		"critical": return LakskongenTheme.DANGER
		"high": return LakskongenTheme.WARNING
		"medium": return LakskongenTheme.ACCENT2
		_: return LakskongenTheme.MUTED

# ── Item actions ──

func _on_item_action(callback: String, _action_id: String, _site_id: String) -> void:
	match callback:
		"open_intervention_detail":
			_show_tab("pens")
		"open_decision_panel":
			_show_tab("dashboard")
		"open_site_health", "open_health_panel":
			_show_tab("pens")
		"open_finance_panel", "open_market_panel":
			_show_tab("finance")
		"open_weather_panel":
			_show_tab("map")
		"open_contracts_panel":
			_show_tab("contracts")
		_:
			push_warning("Unknown attention callback: %s" % callback)
	visible = false

## Switches the main GameUI to a tab. GameUI lives under the scene root
## (GameScene/UILayer/GameUI in the real game, Main/GameUI in Main.tscn), so
## resolve it by walking up instead of hardcoding a path.
func _show_tab(tab_id: String) -> void:
	var game_ui := _find_game_ui()
	if game_ui != null and game_ui.has_method("show_tab"):
		game_ui.show_tab(tab_id)
	else:
		push_warning("AttentionInbox: GameUI not found — cannot show tab '%s'" % tab_id)

func _find_game_ui() -> Node:
	var node: Node = self
	while node != null:
		var ui := node.find_child("GameUI", true, false)
		if ui != null:
			return ui
		node = node.get_parent()
	return null

func _format_nok(val: float) -> String:
	if val >= 1_000_000_000:
		return "%.1fB NOK" % (val / 1_000_000_000.0)
	elif val >= 1_000_000:
		return "%.1fM NOK" % (val / 1_000_000.0)
	elif val >= 1_000:
		return "%.1fK NOK" % (val / 1_000.0)
	else:
		return "%.0f NOK" % val

# ── Open / close / input ──

## Public entry point (also connected to GameBridge.AttentionInboxRequested).
func open_inbox() -> void:
	visible = true
	_refresh_attention_items(true)
	_close_button.grab_focus()

func _on_close_pressed() -> void:
	visible = false

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_attention_inbox"):
		# Never hijack the shortcut while the user is typing in a text field.
		var focused := get_viewport().gui_get_focus_owner()
		if focused is LineEdit or focused is TextEdit:
			return
		if visible:
			visible = false
		else:
			open_inbox()
		get_viewport().set_input_as_handled()
		return
	# Never swallow input while hidden (a hidden CanvasLayer still gets _input).
	if not visible:
		return
	if event.is_action_pressed("ui_cancel"):
		visible = false
		get_viewport().set_input_as_handled()
