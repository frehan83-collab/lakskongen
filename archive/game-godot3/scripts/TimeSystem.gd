# scripts/TimeSystem.gd
# Simple time system for the prototype
extends Node

signal day_advanced(day:int)

var day:int = 0
var time_scale: int = 1 # days per tick
var running: bool = false

func start():
	running = true

func stop():
	running = false

func tick():
	if not running:
		return
	day += time_scale
	emit_signal("day_advanced", day)

func set_speed(days_per_tick:int):
	time_scale = days_per_tick

func get_day_of_year():
	return int(day % 365)
