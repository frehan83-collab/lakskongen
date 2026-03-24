# TimeSystem - advances days and notifies listeners

extends Node
class_name TimeSystem

signal day_advanced(day_count)

var day: int = 0
var time_scale: float = 1.0 # 1.0 real-time mapping by external driver

func advance_days(days: int = 1) -> void:
    day += days
    emit_signal("day_advanced", days)

func set_time_scale(scale: float) -> void:
    time_scale = scale

func tick(delta_seconds: float) -> void:
    # optional integration with engine; accumulate seconds → days using time_scale
    pass
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
