extends CanvasLayer

class_name DailySummary

# Theme accessed via LakskongenTheme class_name

var headlines = [
	"Kinesisk appetitt på norsk laks øker",
	"Miljøvernere krever strengere regler",
	"SalMar tester ny undervannsdrone",
	"Grieg Seafood investerer i lukket merd",
	"EU vurderer nye importkrav",
	"Havforskningsinstituttet med nye funn om lakselus",
	"Regjeringen varsler nye kvoteregler",
	"Innovasjon Norge støtter pilotprosjekter for lukkede anlegg",
	"Lakseprisen stabiliseres etter turbulente uker",
	"Ny rapport: Global sjømatetterspørsel i sterk vekst",
	"Akvakulturparker planlegges langs kysten",
	"Teknologigiganter satser på undervannsfôring",
	"Arbeidsmarkedet for fiskeoppdrettere strammes til",
	"Fokus på bærekraft endrer bransjen",
	"Smoltkvalitet avgjørende for produksjonsresultater",
	"Algerisiko under lupen: Forskere advarer",
	"Nye vaksiner gir håp i kampen mot sykdom",
	"Forbrukertrender: Sunn mat fra havet fortsatt populært",
	"Automatisering effektiviserer drift av oppdrettsanlegg",
	"Internasjonal konkurranse øker presset på norske eksportører"
]

var _overlay: Control
var _panel_container: PanelContainer

func _ready():
	layer = 50
	# Theme is auto-loaded via class_name

func create_background_overlay() -> Control:
	var bg = Control.new()
	bg.set_name("BackgroundOverlay")
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bg.set_mouse_filter(Control.MOUSE_FILTER_STOP)
	var color_rect = ColorRect.new()
	color_rect.color = Color(0, 0, 0, 0.7)
	color_rect.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bg.add_child(color_rect)
	return bg

func create_newspaper_panel() -> PanelContainer:
	var panel = LakskongenTheme.create_styled_panel()
	panel.set_name("NewspaperPanel")
	panel.set_custom_minimum_size(Vector2(800, 600))
	panel.add_theme_constant_override("panel_margin_left", LakskongenTheme.SPACE_LG)
	panel.add_theme_constant_override("panel_margin_top", LakskongenTheme.SPACE_LG)
	panel.add_theme_constant_override("panel_margin_right", LakskongenTheme.SPACE_LG)
	panel.add_theme_constant_override("panel_margin_bottom", LakskongenTheme.SPACE_LG)
	
	var vbox = VBoxContainer.new()
	vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
	panel.add_child(vbox)
	
	var center_container = CenterContainer.new()
	center_container.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	center_container.set_v_size_flags(Control.SIZE_SHRINK_CENTER)
	
	var header_vbox = VBoxContainer.new()
	header_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	
	var title_label = LakskongenTheme.create_label("HAVBRUKSPOSTEN", LakskongenTheme.FONT_TITLE, LakskongenTheme.ACCENT)
	title_label.set_horizontal_alignment(HORIZONTAL_ALIGNMENT_CENTER)
	header_vbox.add_child(title_label)
	
	var date_label = LakskongenTheme.create_label("Dag %s — År %s" % ["", ""], LakskongenTheme.FONT_MD, LakskongenTheme.MUTED)
	date_label.set_horizontal_alignment(HORIZONTAL_ALIGNMENT_CENTER)
	header_vbox.add_child(date_label)
	
	center_container.add_child(header_vbox)
	vbox.add_child(center_container)
	
	vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	return panel

func get_headline(day: int, price: float) -> String:
	if price > 90:
		return "LAKSEFEBER! Rekordpriser på spotmarkedet"
	if price < 60:
		return "PRISKOLLAPS: Oppdrettere blør millioner"
	if day % 180 == 0:
		return "TRAFIKKLYS-OPPDATERING: Fiskeridirektoratet revurderer soner"
	
	var random_headline = headlines[randi() % headlines.size()]
	return random_headline

func show_daily_summary(day: int, data: Dictionary):
	if _overlay:
		_overlay.queue_free()
	
	_overlay = create_background_overlay()
	add_child(_overlay)
	
	_panel_container = create_newspaper_panel()
	_overlay.add_child(_panel_container)
	_panel_container.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	
	var main_vbox = _panel_container.get_child(0)
	var header_vbox = main_vbox.get_child(0).get_child(0) # CenterContainer -> VBoxContainer
	var date_label = header_vbox.get_child(1)
	date_label.text = "Dag %s — År %s" % [day, data.get("year", "")]
	
	# Section 1: Headline
	var headline_data = data.get("headline_data", {})
	var price = headline_data.get("spot_price", 0.0)
	var current_headline = get_headline(day, price)
	
	main_vbox.add_child(LakskongenTheme.create_label(current_headline, LakskongenTheme.FONT_LG, LakskongenTheme.TEXT))
	main_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	# Section 2: Economic Summary
	main_vbox.add_child(LakskongenTheme.create_stat("Inntekt i dag:", str(data.get("revenue", 0)), LakskongenTheme.SUCCESS))
	main_vbox.add_child(LakskongenTheme.create_stat("Utgifter i dag:", str(data.get("expenses", 0)), LakskongenTheme.DANGER))
	main_vbox.add_child(LakskongenTheme.create_stat("Netto:", str(data.get("net", 0)), LakskongenTheme.ACCENT))
	main_vbox.add_child(LakskongenTheme.create_stat("Saldo:", str(data.get("balance", 0)), LakskongenTheme.TEXT))
	main_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	var spot_price = data.get("spot_price", 0.0)
	var price_change = data.get("price_change", 0.0)
	var price_change_color = LakskongenTheme.TEXT
	if price_change > 0:
		price_change_color = LakskongenTheme.SUCCESS
	elif price_change < 0:
		price_change_color = LakskongenTheme.DANGER
	
	main_vbox.add_child(LakskongenTheme.create_stat("Spotpris:", "%.2f NOK/kg" % spot_price, LakskongenTheme.TEXT))
	main_vbox.add_child(LakskongenTheme.create_stat("Prisendring:", "%.2f%%" % price_change, price_change_color))
	main_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	# Section 3: Events
	var events = data.get("events", [])
	if not events.is_empty():
		for i in range(min(3, events.size())):
			main_vbox.add_child(LakskongenTheme.create_label("- " + str(events[i]), LakskongenTheme.FONT_SM, LakskongenTheme.TEXT))
		main_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	# Bottom: Continue Button
	var continue_button = LakskongenTheme.create_button("FORTSETT ▶", true, false)
	continue_button.pressed.connect(hide_summary)
	var button_center = CenterContainer.new()
	button_center.add_child(continue_button)
	main_vbox.add_child(button_center)

func show_weekly_summary(week: int, data: Dictionary):
	if _overlay:
		_overlay.queue_free()
	
	_overlay = create_background_overlay()
	add_child(_overlay)
	
	_panel_container = create_newspaper_panel()
	_overlay.add_child(_panel_container)
	_panel_container.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	
	var main_vbox = _panel_container.get_child(0)
	var header_vbox = main_vbox.get_child(0).get_child(0) # CenterContainer -> VBoxContainer
	var title_label = header_vbox.get_child(0)
	title_label.text = tr("UKENS OPPSUMMERING")
	title_label.set_modulate(LakskongenTheme.ACCENT2) # Use ACCENT2 for weekly
	
	var date_label = header_vbox.get_child(1)
	date_label.text = "Uke %s — År %s" % [week, data.get("year", "")]
	
	# Weekly Profit/Loss Visualization
	main_vbox.add_child(LakskongenTheme.create_label("Ukentlig resultat:", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT))
	var weekly_graph_hbox = HBoxContainer.new()
	weekly_graph_hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	weekly_graph_hbox.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	
	var weekly_profits = data.get("weekly_profits", [])
	for i in range(7):
		var day_profit = weekly_profits[i] if i < weekly_profits.size() else 0.0
		var rect = ColorRect.new()
		rect.set_custom_minimum_size(Vector2(20, abs(day_profit / 100))) # Scale height based on profit
		if day_profit >= 0:
			rect.color = LakskongenTheme.SUCCESS
		else:
			rect.color = LakskongenTheme.DANGER
		weekly_graph_hbox.add_child(rect)
	
	main_vbox.add_child(weekly_graph_hbox)
	main_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
	
	# Bottom: Continue Button
	var continue_button = LakskongenTheme.create_button("FORTSETT ▶", true, false)
	continue_button.pressed.connect(hide_summary)
	var button_center = CenterContainer.new()
	button_center.add_child(continue_button)
	main_vbox.add_child(button_center)

func hide_summary():
	if _overlay:
		_overlay.queue_free()
		_overlay = null
		_panel_container = null

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()


tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
