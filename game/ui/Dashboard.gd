extends Control
class_name Dashboard

signal time_speed_changed(new_speed)

# Node references (filled in _ready)
@onready var lbl_cash := $VBoxContainer/Row_Cash/Value
@onready var lbl_day := $VBoxContainer/Row_Day/Value
@onready var lbl_season := $VBoxContainer/Row_Season/Value
@onready var lbl_revenue := $VBoxContainer/Row_RevProfit/Revenue
@onready var lbl_profit := $VBoxContainer/Row_RevProfit/Profit
@onready var lbl_cost_per_kg := $VBoxContainer/Row_Cost/CostPerKg
@onready var lbl_ebit_per_kg := $VBoxContainer/Row_Cost/EBITPerKg
@onready var progress_biomass := $VBoxContainer/Row_Biomass/ProgressBar
@onready var lbl_biomass := $VBoxContainer/Row_Biomass/Value
@onready var lbl_fish_count := $VBoxContainer/Row_Fish/FishCount
@onready var lbl_mortality := $VBoxContainer/Row_Fish/Mortality
@onready var lbl_avg_weight := $VBoxContainer/Row_Fish/AvgWeight
@onready var lbl_temp := $VBoxContainer/Row_Env/Temp
@onready var lbl_oxygen := $VBoxContainer/Row_Env/Oxygen
@onready var lbl_lice := $VBoxContainer/Row_Lice/LiceValue
@onready var lbl_diseases := $VBoxContainer/Row_Diseases/DiseasesValue
@onready var lbl_spot_price := $VBoxContainer/Row_Market/SpotPrice
@onready var btn_1x := $VBoxContainer/Row_Time/Buttons/Btn1x
@onready var btn_7x := $VBoxContainer/Row_Time/Buttons/Btn7x
@onready var btn_30x := $VBoxContainer/Row_Time/Buttons/Btn30x
@onready var btn_pause := $VBoxContainer/Row_Time/Buttons/BtnPause
@onready var lbl_crew := $VBoxContainer/Row_Crew/CrewValue

# For smooth transitions we keep a displayed value map and lerp towards targets
var _display_values := {}
var _lerp_speed := 6.0 # higher = faster smoothing

# Panels for collapse/hover behaviour (optional, will check availability)
@onready var panel_rows := [$VBoxContainer/Row_Cash, $VBoxContainer/Row_RevProfit, $VBoxContainer/Row_Cost, $VBoxContainer/Row_Biomass, $VBoxContainer/Row_Fish, $VBoxContainer/Row_Env, $VBoxContainer/Row_Lice, $VBoxContainer/Row_Diseases, $VBoxContainer/Row_Market, $VBoxContainer/Row_Time]

func _ready():
    # Initialize display values from current labels
    _init_display_values()

    # Connect time buttons
    btn_1x.pressed = false
    btn_7x.pressed = false
    btn_30x.pressed = false
    btn_pause.pressed = false
    btn_1x.connect("pressed", Callable(self, "_on_time_button_pressed").bind(1))
    btn_7x.connect("pressed", Callable(self, "_on_time_button_pressed").bind(7))
    btn_30x.connect("pressed", Callable(self, "_on_time_button_pressed").bind(30))
    btn_pause.connect("pressed", Callable(self, "_on_time_button_pressed").bind(0))

    # Add hover and click (collapse) behaviour to rows if they exist
    for r in panel_rows:
        if r and r is Control:
            r.mouse_filter = Control.MOUSE_FILTER_STOP
            r.connect("mouse_entered", Callable(self, "_on_row_hover").bind(r))
            r.connect("mouse_exited", Callable(self, "_on_row_unhover").bind(r))
            r.connect("gui_input", Callable(self, "_on_row_gui_input").bind(r))

    set_process(true)

func _init_display_values() -> void:
    # Parse initial numeric values to populate _display_values map
    func _parse_number(s: String) -> float:
        var num = s.strip_edges().replace("NOK", "").replace("kg", "").replace("%", "").replace("°C", "").replace("mg/L", "").strip_edges()
        num = num.replace(",", ".")
        var f = 0.0
        var ok = true
        f = float(num) if num != "" else 0.0
        return f

    _display_values["cash"] = _parse_number(lbl_cash.text)
    _display_values["revenue"] = _parse_number(lbl_revenue.text)
    _display_values["profit"] = _parse_number(lbl_profit.text)
    _display_values["biomass"] = _parse_number(lbl_biomass.text)
    _display_values["spot_price"] = _parse_number(lbl_spot_price.text)
    _display_values["avg_weight_kg"] = _parse_number(lbl_avg_weight.text)
    _display_values["mortality_pct"] = _parse_number(lbl_mortality.text)

func _process(delta: float) -> void:
    # Smoothly interpolate numeric displays
    var changed = false
    for k in _display_values.keys():
        var target = _display_values[k]
        # We'll store temporary target in map if present (target_...)
        var key_t = "target_%s" % k
        if _display_values.has(key_t):
            target = _display_values[key_t]
        var current = _display_values[k]
        var next = lerp(current, target, 1.0 - pow(0.001, delta * _lerp_speed))
        if abs(next - current) > 0.001:
            _display_values[k] = next
            changed = true
    if changed:
        _apply_display_values()

func _apply_display_values() -> void:
    # Write back to labels formatting appropriately
    lbl_cash.text = "NOK %s" % String.format("%.0f", _display_values.get("cash", 0.0))
    lbl_revenue.text = "NOK %s" % String.format("%.0f", _display_values.get("revenue", 0.0))
    lbl_profit.text = "NOK %s" % String.format("%.0f", _display_values.get("profit", 0.0))
    var biomass = _display_values.get("biomass", 0.0)
    lbl_biomass.text = "%s kg" % String.format("%.1f", biomass)
    lbl_spot_price.text = "NOK %s" % String.format("%.2f", _display_values.get("spot_price", 0.0))
    lbl_avg_weight.text = "%s kg" % String.format("%.2f", _display_values.get("avg_weight_kg", 0.0))
    lbl_mortality.text = String.format("%.2f", _display_values.get("mortality_pct", 0.0)) + "%"

func _on_time_button_pressed(speed: int) -> void:
    # Visual feedback
    btn_1x.pressed = (speed == 1)
    btn_7x.pressed = (speed == 7)
    btn_30x.pressed = (speed == 30)
    btn_pause.pressed = (speed == 0)
    emit_signal("time_speed_changed", speed)

# Update the dashboard from the full game state dictionary
func update_from_state(state: Dictionary) -> void:
    # Safe lookups with defaults
    # Instead of snapping, write targets to the target_ keys so smoothing applies
    _display_values["target_cash"] = float(state.get("cash", 0))
    _display_values["target_revenue"] = float(state.get("revenue", 0))
    _display_values["target_profit"] = float(state.get("profit", 0))
    _display_values["target_biomass"] = float(state.get("total_biomass", 0.0))
    _display_values["target_avg_weight_kg"] = float(state.get("avg_weight_kg", 0.0))
    _display_values["target_mortality_pct"] = float(state.get("mortality_pct", 0.0))
    _display_values["target_spot_price"] = float(state.get("spot_price_nok_per_kg", 0.0))

    lbl_day.text = str("Day ", state.get("day", 0), " / Week ", state.get("week", 0), " / Year ", state.get("year", 0))
    lbl_season.text = str(state.get("season", "Unknown"))

    lbl_cost_per_kg.text = str("NOK ", state.get("production_cost_per_kg", 0.0))
    lbl_ebit_per_kg.text = str("NOK ", state.get("ebit_per_kg", 0.0))

    var mab := float(state.get("mab_limit", 1.0))
    var biomass := float(state.get("total_biomass", 0.0))
    var pct := 0.0
    if mab > 0:
        pct = clamp(biomass / mab * 100.0, 0.0, 100.0)
    progress_biomass.value = pct
    # progress bar instant update is fine

    lbl_fish_count.text = str(state.get("fish_alive", 0))

    lbl_temp.text = str(String.format("%.1f", float(state.get("sea_temp_c", 0.0))), " °C")
    lbl_oxygen.text = str(String.format("%.2f", float(state.get("oxygen_mg_l", 0.0))), " mg/L")

    var lice := float(state.get("lice_per_fish", 0.0))
    lbl_lice.text = String.format("%.2f", lice)
    if lice > 0.5:
        lbl_lice.add_theme_color_override("font_color", Color.red)
    else:
        lbl_lice.add_theme_color_override("font_color", Color.white)

    var diseases := state.get("active_diseases", [])
    if diseases.size() == 0:
        lbl_diseases.text = "None"
    else:
        lbl_diseases.text = ", ".join(diseases)

    # Optionally update button states if state carries time_speed
    if state.has("time_speed"):
        var ts = int(state.get("time_speed"))
        btn_1x.pressed = (ts == 1)
        btn_7x.pressed = (ts == 7)
        btn_30x.pressed = (ts == 30)
        btn_pause.pressed = (ts == 0)

    # Crew info (optional)
    if state.has("crew"):
        var c = state.get("crew")
        var staff = str(c.get("staff_count", 0))
        var morale = String.format("%.1f", float(c.get("avg_morale", 0.0)))
        var payroll = String.format("%.0f", float(c.get("total_daily_salary", 0.0)))
        lbl_crew.text = str(staff, " staff / morale ", morale, "% / NOK ", payroll)
    else:
        lbl_crew.text = "0 staff / morale 0% / NOK 0"

func clear_theme_overrides() -> void:
    # Remove simple overrides (helpful when theme changes)
    lbl_lice.add_theme_color_override("font_color", null)

# Hover and collapse helpers
func _on_row_hover(row: Control) -> void:
    if row:
        row.add_theme_color_override("font_color", Color(0.9,0.9,1))

func _on_row_unhover(row: Control) -> void:
    if row:
        row.add_theme_color_override("font_color", null)

func _on_row_gui_input(row: Control, ev) -> void:
    if ev is InputEventMouseButton and ev.pressed and ev.button_index == MOUSE_BUTTON_LEFT:
        # Toggle visibility of the row's first child content (collapse)
        if row.get_child_count() > 1:
            var c = row.get_child(1)
            c.visible = not c.visible

