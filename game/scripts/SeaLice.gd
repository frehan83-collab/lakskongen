# SeaLice.gd
# Sea lice simulation for Lakskongen salmon farming simulator
# Godot 4 GDScript

extends Node
class_name SeaLiceSystem

signal lice_threshold_exceeded(farm_id: int, avg_adult_female_per_fish: float)
signal forced_treatment(farm_id: int, method: String)

# Track lice by farm: {farm_id: {"lice_per_fish": float, "adult_female_per_fish": float, "last_treatment": {method: day}}}
var farms := {}

# Regulatory limit (adult female lice per fish)
@export
var regulatory_limit: float = 0.5

# Lice reproduction parameters
# Reproduction increases with temperature, particularly >12C
@export
var base_reproduction_rate := 0.01 # per day baseline

# Treatment definitions
var treatments := {
	"thermal": {"cost": 2000.0, "mortality": 0.04, "efficacy": 0.65, "cooldown_days": 30},
	"mechanical": {"cost": 1500.0, "mortality": 0.12, "efficacy": 0.6, "cooldown_days": 20},
	"cleaner_fish": {"cost": 800.0, "mortality": 0.01, "efficacy": 0.4, "cooldown_days": 60},
	"laser": {"cost": 5000.0, "mortality": 0.02, "efficacy": 0.7, "cooldown_days": 45},
	"freshwater": {"cost": 2500.0, "mortality": 0.03, "efficacy": 0.75, "cooldown_days": 15},
	"lice_skirt": {"cost": 1200.0, "mortality": 0.0, "efficacy": 0.3, "cooldown_days": 0}
}

# Cross-infection factor for neighboring farms
@export
var cross_infection_rate := 0.02

func register_farm(farm_id: int, initial_lice_per_fish: float, adult_female_per_fish: float) -> void:
	farms[farm_id] = {
		"lice_per_fish": initial_lice_per_fish,
		"adult_female_per_fish": adult_female_per_fish,
		"last_treatment": {}
	}

func step_day(farm_id: int, water_temp: float, neighbors: Array = [], dt_days: float = 1.0) -> void:
	# Progress lice population for a single farm by dt_days
	if not farms.has(farm_id):
		return
	var state = farms[farm_id]
	# reproduction grows faster when temp > 12
	var temp_factor = 1.0
	if water_temp > 12.0:
		temp_factor += (water_temp - 12.0) * 0.1
	# daily increase
	var growth = base_reproduction_rate * temp_factor * dt_days
	# biomass/stocking density can be integrated externally; assuming proportionally
	state.lice_per_fish += growth * state.lice_per_fish
	# convert to adult female fraction (assume 20% of lice are mature adult females)
	state.adult_female_per_fish = state.lice_per_fish * 0.2
	# cross-infection from neighbors
	for n in neighbors:
		if farms.has(n):
			state.lice_per_fish += farms[n].lice_per_fish * cross_infection_rate * dt_days
	# enforce biological limits
	state.lice_per_fish = clamp(state.lice_per_fish, 0.0, 100.0)
	state.adult_female_per_fish = clamp(state.adult_female_per_fish, 0.0, 50.0)
	# Check regulatory threshold
	if state.adult_female_per_fish > regulatory_limit:
		emit_signal("lice_threshold_exceeded", farm_id, state.adult_female_per_fish)
		# caller/main game may enforce forced treatment; optionally auto-apply a cheap treatment
		# emit forced treatment signal but do not autonomously change state

func apply_treatment(farm_id: int, method: String, current_day: int) -> Dictionary:
	if not farms.has(farm_id):
		return {"success": false, "reason": "no_farm"}
	if not treatments.has(method):
		return {"success": false, "reason": "unknown_method"}
	var t = treatments[method]
	# check cooldown
	var last = farms[farm_id].last_treatment.get(method, -9999)
	if (current_day - last) < t.cooldown_days:
		return {"success": false, "reason": "cooldown"}
	# apply efficacy: reduce lice numbers
	var state = farms[farm_id]
	var before = state.lice_per_fish
	var reduction = before * t.efficacy
	state.lice_per_fish = clamp(before - reduction, 0.0, 100.0)
	state.adult_female_per_fish = state.lice_per_fish * 0.2
	# apply mortality to fish stock (proportional to mortality rate)
	var fish_mortality_fraction = t.mortality
	# record treatment day
	state.last_treatment[method] = current_day
	emit_signal("forced_treatment", farm_id, method)
	return {"success": true, "cost": t.cost, "reduction": reduction, "fish_mortality_fraction": fish_mortality_fraction}

func get_farm_lice_status(farm_id: int) -> Dictionary:
	if not farms.has(farm_id):
		return {}
	return farms[farm_id].duplicate(true)

# Convenience: enforce regulatory limit by selecting a treatment strategy
func enforce_limit(farm_id: int, current_day: int) -> Dictionary:
	# naive strategy: prefer low-mortality, effective methods if available
	if not farms.has(farm_id):
		return {"success": false}
	var state = farms[farm_id]
	if state.adult_female_per_fish <= regulatory_limit:
		return {"success": false, "reason": "within_limit"}
	# choose method order
	var preference = ["freshwater", "laser", "thermal", "mechanical", "cleaner_fish"]
	for method in preference:
		var res = apply_treatment(farm_id, method, current_day)
		if res.success:
			return res
	# fallback
	return {"success": false, "reason": "no_available_treatment"}

# Utilities
func set_lice_levels(farm_id: int, lice_per_fish: float) -> void:
	if farms.has(farm_id):
		farms[farm_id].lice_per_fish = lice_per_fish
		farms[farm_id].adult_female_per_fish = lice_per_fish * 0.2

func simulate_days(days: int, day_start: int, water_temp_func: Callable, neighbor_map: Dictionary) -> void:
	# water_temp_func: callable that takes (farm_id, day) -> temperature
	# neighbor_map: {farm_id: [neighbor_ids]}
	for d in range(days):
		var current_day = day_start + d
		for farm_id in farms.keys():
			var temp = water_temp_func.callf(farm_id, current_day)
			var neighbors = neighbor_map.get(farm_id, [])
			step_day(farm_id, temp, neighbors, 1.0)
			# optional enforcement
			if farms[farm_id].adult_female_per_fish > regulatory_limit:
				enforce_limit(farm_id, current_day)
