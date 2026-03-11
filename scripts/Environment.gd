# Environment.gd - zone-based environment simulation for Lakskongen
extends Node

class_name Environment

# Zone data structure
class Zone:
	var id: String
	var carrying_capacity_tonnes: float
	var temperature_c: float
	var oxygen_mg_l: float
	var sea_lice_risk: float
	var disease_risk: float

	func _init(_id, capacity=1000.0):
		id = _id
		carrying_capacity_tonnes = capacity
		temperature_c = 8.0
		oxygen_mg_l = 8.0
		sea_lice_risk = 0.01
		disease_risk = 0.001

# Public API
var zones: = {}
var rng: RandomNumberGenerator

signal environment_changed()
signal event_generated(event)

func _init():
	rng = RandomNumberGenerator.new()
	rng.seed = 123456789  # deterministic by default; GameManager can set seed

func add_zone(zone_id: String, capacity:=1000.0):
	var z = Zone.new(zone_id, capacity)
	zones[zone_id] = z
	emit_signal("environment_changed")
	return z

func get_zone(zone_id: String):
	return zones.get(zone_id, null)

func set_seed(s):
	rng.seed = int(s)

# Advance the environment by given days. Generates events probabilistically but deterministically if seed set.
func advance_days(days: int):
	# simple seasonal temperature cycle and oxygen dynamics per zone
	for zone_id in zones.keys():
		var z = zones[zone_id]
		# temperature: base + seasonal + random noise
		var seasonal = 4.0 * sin(TAU * (OS.get_system_time_secs() / 86400.0) / 365.0)
		z.temperature_c = 8.0 + seasonal + rng.randf_range(-0.5, 0.5)

		# oxygen inversely related to temperature and organic load approximation
		z.oxygen_mg_l = clamp(9.0 - 0.2 * (z.temperature_c - 8.0) - rng.randf_range(0.0, 0.5), 2.0, 12.0)

		# sea lice risk increases with temperature and local stocking pressure (approx from carrying capacity)
		var pressure = clamp(z.carrying_capacity_tonnes / 1000.0, 0.1, 3.0)
		z.sea_lice_risk = clamp(0.005 + 0.02 * max(0.0, z.temperature_c - 7.0) + 0.01 * (pressure - 1.0) + rng.randf_range(-0.002, 0.002), 0.0, 1.0)

		# disease risk depends on oxygen stress and lice risk
		var oxygen_stress = clamp((6.0 - z.oxygen_mg_l) / 6.0, 0.0, 1.0)
		z.disease_risk = clamp(0.0005 + 0.01 * oxygen_stress + 0.05 * z.sea_lice_risk + rng.randf_range(-0.001, 0.001), 0.0, 1.0)

	# Event generation: for each zone, possibly generate an environmental event
	for zone_id in zones.keys():
		var z = zones[zone_id]
		# flood/algae bloom event probability increases with temperature and low oxygen
		var bloom_prob = clamp(0.001 + 0.05 * max(0.0, z.temperature_c - 10.0) + 0.1 * max(0.0, (7.0 - z.oxygen_mg_l) / 7.0), 0.0, 1.0)
		if rng.randf() < bloom_prob * days:
			var ev = {"type":"algae_bloom","zone":zone_id,"severity":rng.randf_range(0.1,1.0)}
			emit_signal("event_generated", ev)

		# disease outbreak event: depends on disease_risk and lice
		var disease_prob = clamp(z.disease_risk * 0.1 + 0.05 * z.sea_lice_risk, 0.0, 1.0)
		if rng.randf() < disease_prob * days:
			var ev2 = {"type":"disease_outbreak","zone":zone_id,"probability":z.disease_risk}
			emit_signal("event_generated", ev2)

	emit_signal("environment_changed")

# Utility: deterministic event sampler for tests
func sample_events_for_zone(zone_id: String, trials: int) -> Array:
	var results = []
	var saved_seed = rng.seed
	for i in range(trials):
		# snapshot zone parameters
		var z = zones[zone_id]
		var bloom_prob = clamp(0.001 + 0.05 * max(0.0, z.temperature_c - 10.0) + 0.1 * max(0.0, (7.0 - z.oxygen_mg_l) / 7.0), 0.0, 1.0)
		var disease_prob = clamp(z.disease_risk * 0.1 + 0.05 * z.sea_lice_risk, 0.0, 1.0)
		var evs = []
		if rng.randf() < bloom_prob:
			evs.append("algae_bloom")
		if rng.randf() < disease_prob:
			evs.append("disease_outbreak")
		results.append(evs)

	rng.seed = saved_seed
	return results
