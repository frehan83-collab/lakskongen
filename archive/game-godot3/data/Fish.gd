# data/Fish.gd
# Represents a single fish with simple genetics and growth model
class_name Fish

var id: int
var age_days: int = 0
var weight_kg: float = 0.01
var alive: bool = true
var genetics = {
	"growth": 50.0, # 1-100
	"disease_resistance": 50.0,
	"fillet_quality": 50.0,
}

func _init(_id:int, _genetics:Dictionary = {}):
	id = _id
	if _genetics.size() > 0:
		genetics = _genetics.duplicate(true)

# Simple growth: base growth scaled by genetics and temperature/feed
func grow(days:int=1, sea_temp:float=10.0, feed_quality:float=1.0):
	if not alive:
		return
	# Base daily growth (kg/day) for small fish scales with size
	var base = 0.002 * max(0.01, (1.0 - clamp(weight_kg/6.0, 0.0, 0.9)))
	# Genetics growth factor (normalized 0.7 - 1.3)
	var gfact = lerp(0.7, 1.3, genetics["growth"]/100.0)
	# Temp effect: optimal 8-14C
	var temp_factor = clamp(1.0 - abs(sea_temp - 11.0)/10.0, 0.5, 1.5)
	# Feed quality multiplier
	var feed_factor = clamp(feed_quality, 0.5, 1.5)
	# Growth applied
	var delta = base * gfact * temp_factor * feed_factor * days
	weight_kg += delta
	age_days += days

func apply_mortality(rate:float):
	# rate is daily mortality probability
	if not alive:
		return
	if randf() < rate:
		alive = false

func to_dict():
	return {
		"id": id,
		"alive": alive,
		"age_days": age_days,
		"weight_kg": weight_kg,
		"genetics": genetics.duplicate(true),
	}
