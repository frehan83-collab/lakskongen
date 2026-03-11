extends Control
class_name MarketPanel

@onready var lbl_spot := $VBoxContainer/Row_Spot/SpotLabel
@onready var lbl_trend := $VBoxContainer/Row_Spot/TrendLabel
@onready var lbl_history := $VBoxContainer/PriceHistory
@onready var contracts_box := $VBoxContainer/Contracts/VBoxContainer

var price_history: Array = [] # floats, last entries

# small smoothing for spot label
var _display_spot := 0.0
var _target_spot := 0.0
var _spot_lerp_speed := 8.0

func _ready():
    # placeholder
    lbl_spot.text = "NOK 0.00"
    lbl_trend.text = "-"
    lbl_history.text = ""
    set_process(true)

func _process(delta: float) -> void:
    # Smooth spot price change
    var next = lerp(_display_spot, _target_spot, 1.0 - pow(0.001, delta * _spot_lerp_speed))
    if abs(next - _display_spot) > 0.0001:
        _display_spot = next
        lbl_spot.text = str("NOK ", String.format("%.2f", _display_spot))

func update_market(spot_price: float, history: Array, contracts: Array) -> void:
    _target_spot = float(spot_price)
    price_history = history.duplicate()
    # Trend arrow: compare last two
    var trend = "-"
    if price_history.size() >= 2:
        var last = float(price_history[price_history.size() - 1])
        var prev = float(price_history[price_history.size() - 2])
        if last > prev:
            trend = "▲"
            lbl_trend.add_theme_color_override("font_color", Color(0.4, 1, 0.4))
        elif last < prev:
            trend = "▼"
            lbl_trend.add_theme_color_override("font_color", Color(1, 0.4, 0.4))
        else:
            trend = "–"
            lbl_trend.add_theme_color_override("font_color", Color(1, 1, 1))
    lbl_trend.text = trend

    # Price history simple textual list (last 52 weeks expected)
    var lines := []
    var max_weeks = min(52, price_history.size())
    for i in range(price_history.size() - max_weeks, price_history.size()):
        if i >= 0:
            lines.append(String.format("%.2f", float(price_history[i])))
    lbl_history.text = "\n".join(lines)

    # Active contracts
    contracts_box.clear()
    for c in contracts:
        var h = HBoxContainer.new()
        var l = Label.new()
        l.text = str(c)
        h.add_child(l)
        # placeholder buttons for accept/cancel
        var btn_accept = Button.new()
        btn_accept.text = "Accept"
        btn_accept.pressed = false
        btn_accept.connect("pressed", Callable(self, "_on_accept_contract").bind(c))
        var btn_cancel = Button.new()
        btn_cancel.text = "Cancel"
        btn_cancel.pressed = false
        btn_cancel.connect("pressed", Callable(self, "_on_cancel_contract").bind(c))
        h.add_child(btn_accept)
        h.add_child(btn_cancel)
        contracts_box.add_child(h)

func _on_accept_contract(contract_data) -> void:
    # Placeholder: integrate with Market module later
    print("Accept contract:", contract_data)

func _on_cancel_contract(contract_data) -> void:
    print("Cancel contract:", contract_data)

func export_market_distribution(path: String = "user://market_distribution.csv") -> bool:
    # Exports price history distribution to CSV (price,count)
    var counts := {}
    for p in price_history:
        var key = String.format("%.2f", float(p))
        counts[key] = counts.get(key, 0) + 1
    var file = FileAccess.open(path, FileAccess.WRITE)
    if not file:
        push_error("MarketPanel: Failed to open file: %s" % path)
        return false
    file.store_line("price,count")
    for k in counts.keys():
        file.store_line(k + "," + str(counts[k]))
    file.close()
    return true
