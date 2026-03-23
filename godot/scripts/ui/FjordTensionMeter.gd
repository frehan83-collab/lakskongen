extends HBoxContainer

# FjordTensionMeter - Zone Stress Indicator
# Layout: Horizontal bar per zone in status bar

@onready var north_bar: TextureProgressBar = $North
@onready var south_bar: TextureProgressBar = $South
@onready var deep_bar: TextureProgressBar = $Deep

var crisis_manager: Node = null

func _ready():
    crisis_manager = get_node("/root/CrisisResponseManager")
    crisis_manager.connect("fjord_tension_changed",Callable(self,"_update_tension"))
    
func _update_tension(zone: String, value: int):
    match zone:
        "North Zone":
            north_bar.value = value
            _update_bar_color(north_bar, value)
        "South Zone":
            south_bar.value = value
            _update_bar_color(south_bar, value)
        "Deep Fjord":
            deep_bar.value = value
            _update_bar_color(deep_bar, value)

func _update_bar_color(bar: TextureProgressBar, value: int):
    if value >= 75:
        bar.tint_progress = LakskongenTheme.DANGER
    elif value >= 50:
        bar.tint_progress = LakskongenTheme.WARNING
    else:
        bar.tint_progress = LakskongenTheme.TEAL

func _process(delta):
    # Add subtle pulsing animation for high tension
    for bar in [north_bar, south_bar, deep_bar]:
        if bar.value >= 80:
            var alpha = abs(sin(Time.get_ticks_msec() * 0.002))
            bar.modulate = Color(1, 1, 1, 0.8 + alpha * 0.2)
        else:
            bar.modulate = Color.WHITE