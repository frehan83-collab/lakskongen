# Environment.gd
# Environmental simulation for Lakskongen salmon farming simulator
# Godot 4 GDScript

extends Node
class_name Environment

# Zones: integer IDs representing latitudinal zones (1 = southmost, increasing northwards)
# Temperature model: seasonal sinusoidal curve per zone
# Oxygen: inverse to temperature, affected by current speed and biomass density
# Current speed: random variation around zone baseline
# Daylight hours: latitude-based seasonal curve
# Random events: algal blooms (summer), storms (autumn), jellyfish

signal algal_bloom(zone_id)
signal storm(zone_id)
signal jellyfish_bloom(zone_id)

@export_range(1, 13)
var zones := 13

# Tunable zone parameters (index 1..zones). Use dictionaries keyed by zone id
var zone_params := {}

# State (per-zone)
var zone_state := {}

# Day tracking (1..365)
@export
var day_of_year: int = 1 setget set_day

# Time step in days used by advance_day
@export
var timestep_days: float = 1.0

func _ready():
	_init_zone_parameters()
	_init_zone_state()

func set_day(value: int) -> void:
	day_of_year = ((value - 1) % 365) + 1

func _init_zone_parameters() -> void:
	# Define mean and amplitude so ranges match spec
	# Zones 1-2 (south): 4-16°C -> mean 10, amp 6
	# Zones 6-7 (mid): 2-14°C -> mean 8, amp 6
	# Zones 11-13 (north): 0-10°C -> mean 5, amp 5
	# Other zones interpolate linearly
	for i in range(1, zones + 1):
		var p = {
			"zone_id": i,
			"latitude": lerp(58.0, 72.0, float(i - 1) / float(max(1, zones - 1))), # example latitudes (south ~58N, north ~72N)
			"temp_mean": 10.0,
			"temp_amp": 6.0,
			"temp_phase": 170.0, # day of peak temperature (approx mid-summer)
			"current_baseline": 0.2 + 0.05 * ((i - 1) % 4), # m/s baseline by zone
			"biomass_density": 10.0, # tonnes/ha or arbitrary units
			"bio_region_factor": 1.0,
		}
		# adjust for latitudinal gradient
		if i <= 2:
			p.temp_mean = 10.0
			p.temp_amp = 6.0
		elif i >= 11:
			p.temp_mean = 5.0
			p.temp_amp = 5.0
		elif i >= 6 and i <= 7:
			p.temp_mean = 8.0
			p.temp_amp = 6.0
		else:
			# linear interpolation between south/mid/north
			var south_mean = 10.0
			var north_mean = 5.0
			p.temp_mean = lerp(south_mean, north_mean, float(i - 1) / float(max(1, zones - 1)))
			p.temp_amp = lerp(6.0, 5.0, float(i - 1) / float(max(1, zones - 1)))
		zone_params[i] = p

func _init_zone_state() -> void:
	for i in range(1, zones + 1):
		zone_state[i] = {
			"temperature": _compute_zone_temperature(i, day_of_year),
			"oxygen": 8.0,
			"current_speed": zone_params[i].current_baseline,
			"daylight_hours": _compute_daylight_hours(zone_params[i].latitude, day_of_year),
			"last_event_day": -999
		}

func advance_day(days: float = 1.0) -> void:
	# Advance environment by given days
	var new_day = int(day_of_year + days)
	set_day(new_day)
	for i in range(1, zones + 1):
		# update temperature
		var temp = _compute_zone_temperature(i, day_of_year)
		zone_state[i].temperature = temp
		# current speed random variation
		var baseline = zone_params[i].current_baseline
		var variation = randf_range(-0.2 * baseline, 0.2 * baseline)
		var cur = max(0.0, baseline + variation)
		zone_state[i].current_speed = cur
		# daylight hours
		zone_state[i].daylight_hours = _compute_daylight_hours(zone_params[i].latitude, day_of_year)
		# oxygen: inverse to temp, improved by current, reduced by biomass
		zone_state[i].oxygen = _compute_oxygen(temp, cur, zone_params[i].biomass_density)
		# random events
		_check_random_events(i)

func _compute_zone_temperature(zone_id: int, day: int) -> float:
	var p = zone_params[zone_id]
	# sinusoidal: mean + amp * sin(2π (day - phase) / 365)
	var radians = (TAU * float(day - p.temp_phase)) / 365.0
	var t = p.temp_mean + p.temp_amp * sin(radians)
	# clamp to realistic bounds
	if zone_id >= 11:
		t = clamp(t, 0.0, 10.0)
	elif zone_id <= 2:
		t = clamp(t, 4.0, 16.0)
	return t

func _compute_daylight_hours(latitude_deg: float, day: int) -> float:
	# Approximate daylight hours using solar declination
	# latitude in degrees, day 1..365
	var decl = 23.44 * sin(TAU * float(day - 81) / 365.0) # tilt declination
	var lat_rad = deg2rad(latitude_deg)
	var decl_rad = deg2rad(decl)
	# hour angle at sunrise
	var cos_ha = -tan(lat_rad) * tan(decl_rad)
	cos_ha = clamp(cos_ha, -1.0, 1.0)
	var ha = acos(cos_ha)
	var daylight = (2.0 * ha) / TAU * 24.0
	return daylight

func _compute_oxygen(temperature: float, current_speed: float, biomass_density: float) -> float:
	# Base saturation decreases with temperature
	var base_o2 = 12.0 - 0.25 * temperature # arbitrary scaling (mg/L)
	# current increases oxygen exchange
	base_o2 += clamp(current_speed * 4.0, 0.0, 4.0)
	# biomass reduces oxygen
	base_o2 -= clamp(biomass_density * 0.05, 0.0, 6.0)
	# clamp to realistic range
	return clamp(base_o2, 0.0, 14.0)

func _check_random_events(zone_id: int) -> void:
	# Algal blooms: more likely in warm months and when oxygen low
	var temp = zone_state[zone_id].temperature
	var doy = day_of_year
	# Bloom window roughly day 140-260 (summer)
	if doy >= 140 and doy <= 260:
		var bloom_chance = 0.0
		if temp >= 12.0:
			bloom_chance += (temp - 11.0) * 0.02
		# low oxygen increases bloom chance
		if zone_state[zone_id].oxygen <= 6.0:
			bloom_chance += 0.05
		# biomass also contributes
		bloom_chance += clamp(zone_params[zone_id].biomass_density * 0.001, 0.0, 0.05)
		if randf() < bloom_chance:
			emit_signal("algal_bloom", zone_id)

	# Storms: autumn months 240-320
	if doy >= 240 and doy <= 320:
		var storm_chance = 0.01 + (zone_state[zone_id].current_speed * 0.02)
		if randf() < storm_chance:
			emit_signal("storm", zone_id)

	# Jellyfish: increase with warm water and low current
	if temp >= 10.0 and zone_state[zone_id].current_speed <= (zone_params[zone_id].current_baseline * 0.5):
		if randf() < 0.02:
			emit_signal("jellyfish_bloom", zone_id)

# API: getters for external systems
func get_temperature(zone_id: int) -> float:
	return zone_state.get(zone_id, {}).get("temperature", 0.0)

func get_oxygen(zone_id: int) -> float:
	return zone_state.get(zone_id, {}).get("oxygen", 0.0)

func get_current_speed(zone_id: int) -> float:
	return zone_state.get(zone_id, {}).get("current_speed", 0.0)

func get_daylight_hours(zone_id: int) -> float:
	return zone_state.get(zone_id, {}).get("daylight_hours", 0.0)

func set_biomass_density(zone_id: int, density: float) -> void:
	if zone_params.has(zone_id):
		zone_params[zone_id].biomass_density = density

func add_biomass(zone_id: int, delta: float) -> void:
	if zone_params.has(zone_id):
		zone_params[zone_id].biomass_density += delta

func set_current_baseline(zone_id: int, baseline: float) -> void:
	if zone_params.has(zone_id):
		zone_params[zone_id].current_baseline = baseline

func simulate_days(days: int) -> void:
	for i in range(days):
		advance_day(timestep_days)
