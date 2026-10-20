# Disease.gd
# Disease & health system for Lakskongen salmon farming simulator
# Godot 4 GDScript

extends Node
class_name DiseaseSystem

signal disease_outbreak(farm_id: int, disease_name: String)
signal mandatory_cull(farm_id: int, disease_name: String)

# Disease definitions
enum DiseaseType { ISA, PD, AGD, CMS, WINTER_ULCERS }

var diseases := {
	"ISA": {
		"type": DiseaseType.ISA,
		"mortality": 0.9, # up to 90%
		"mandatory_cull": true
	},
	"PD": {
		"type": DiseaseType.PD,
		"mortality": 0.15,
		"mandatory_cull": false
	},
	"AGD": {
		"type": DiseaseType.AGD,
		"mortality": 0.10,
		"mandatory_cull": false
	},
	"CMS": {
		"type": DiseaseType.CMS,
		"mortality": 0.12,
		"mandatory_cull": false
	},
	"WinterUlcers": {
		"type": DiseaseType.WINTER_ULCERS,
		"mortality": 0.10,
		"mandatory_cull": false
	}
}

# Farm state structure example: {
#   id: int,
#   zone_id: int,
#   biomass_density: float,
#   biosecurity: float (0.0-1.0),
#   vaccinated: {disease_name: true/false},
#   infections: {disease_name: prevalence (0.0-1.0)}
# }

var farms := {}

# Vaccination effectiveness (reduces infection probability by 70-90%)
var vaccination_effectiveness := {
	"default": 0.8,
	"ISA": 0.9,
	"PD": 0.8,
	"AGD": 0.75,
	"CMS": 0.8,
	"WinterUlcers": 0.7
}

# Treatment catalog (costs in arbitrary currency units)
var treatments := {
	"antibiotic": {"cost": 1000.0, "efficacy": 0.6, "side_effect_mortality": 0.02},
	"antiviral": {"cost": 5000.0, "efficacy": 0.7, "side_effect_mortality": 0.05},
	"amoebicide": {"cost": 1200.0, "efficacy": 0.65, "side_effect_mortality": 0.015},
	"supportive_care": {"cost": 300.0, "efficacy": 0.25, "side_effect_mortality": 0.0}
}

func register_farm(farm_id: int, zone_id: int, biomass_density: float, biosecurity: float) -> void:
	farms[farm_id] = {
		"id": farm_id,
		"zone_id": zone_id,
		"biomass_density": biomass_density,
		"biosecurity": clamp(biosecurity, 0.0, 1.0),
		"vaccinated": {},
		"infections": {}
	}

func vaccinate_farm(farm_id: int, disease_name: String) -> void:
	if not farms.has(farm_id):
		return
	farms[farm_id].vaccinated[disease_name] = true

func set_farm_biomass(farm_id: int, biomass_density: float) -> void:
	if farms.has(farm_id):
		farms[farm_id].biomass_density = biomass_density

func seed_infection(farm_id: int, disease_name: String, prevalence: float) -> void:
	if not farms.has(farm_id):
		return
	farms[farm_id].infections[disease_name] = clamp(prevalence, 0.0, 1.0)
	emit_signal("disease_outbreak", farm_id, disease_name)
	# ISA triggers mandatory cull if present (policy)
	if diseases.has(disease_name) and diseases[disease_name].mandatory_cull:
		emit_signal("mandatory_cull", farm_id, disease_name)

func compute_infection_probability(farm_id: int, disease_name: String, environment_temp: float, nearby_infection_pressure: float) -> float:
	# Base probability depends on disease
	var base = 0.01
	if diseases.has(disease_name):
		# more virulent diseases have higher base
		match disease_name:
			"ISA": base = 0.05
			"PD": base = 0.03
			"AGD": base = 0.02
			"CMS": base = 0.02
			"WinterUlcers": base = 0.015
	# Temperature effects (some diseases like AGD prefer warmer; winter ulcers colder)
	var temp_factor = 1.0
	if disease_name == "AGD":
		temp_factor += clamp((environment_temp - 8.0) * 0.05, -0.5, 1.0)
	elif disease_name == "WinterUlcers":
		temp_factor += clamp((6.0 - environment_temp) * 0.06, -0.5, 1.0)
	elif disease_name == "ISA":
		temp_factor += clamp((environment_temp - 6.0) * 0.03, -0.5, 1.0)

	# Density increases infection risk
	var density = farms[farm_id].biomass_density
	var density_factor = 1.0 + clamp(density * 0.02, 0.0, 3.0)

	# Biosecurity reduces risk
	var bio = farms[farm_id].biosecurity
	var bio_factor = lerp(1.5, 0.5, bio) # low bio -> 1.5x, high bio -> 0.5x

	# Nearby infection pressure scales risk
	var nearby_factor = 1.0 + clamp(nearby_infection_pressure, 0.0, 5.0)

	var prob = base * temp_factor * density_factor * bio_factor * nearby_factor

	# Vaccination reduces probability
	if farms[farm_id].vaccinated.has(disease_name) and farms[farm_id].vaccinated[disease_name]:
		var v_eff = vaccination_effectiveness.get(disease_name, vaccination_effectiveness["default"])
		prob *= (1.0 - v_eff)

	# Clamp
	return clamp(prob, 0.0, 1.0)

func step_infections(farm_id: int, environment_temp: float, nearby_infection_pressure: float, dt_days: float = 1.0) -> void:
	# Progress each disease on a farm by one time step (dt_days)
	if not farms.has(farm_id):
		return
	for disease_name in diseases.keys():
		# chance to seed new infection
		var p = compute_infection_probability(farm_id, disease_name, environment_temp, nearby_infection_pressure)
		if randf() < p * dt_days:
			# start low prevalence
			farms[farm_id].infections[disease_name] = min(0.05, farms[farm_id].infections.get(disease_name, 0.0) + 0.05)
			emitanalytics(disease_name) # lightweight hook
		# existing infection growth or decay
		if farms[farm_id].infections.has(disease_name):
			# simple progression: prevalence grows with low biosecurity and favorable conditions
			var growth = 0.0
			var favor = 1.0
			if disease_name == "AGD":
				favor = 1.0 + clamp((environment_temp - 8.0) * 0.05, -0.9, 1.0)
			if farms[farm_id].biosecurity < 0.5:
				growth += 0.02
			# density-driven growth
			growth += clamp(farms[farm_id].biomass_density * 0.001, 0.0, 0.05)
			farms[farm_id].infections[disease_name] = clamp(farms[farm_id].infections[disease_name] + growth * favor * dt_days, 0.0, 1.0)
			# high mortality diseases may cause culling
			if diseases[disease_name].mandatory_cull and farms[farm_id].infections[disease_name] > 0.05:
				emit_signal("mandatory_cull", farm_id, disease_name)

func apply_treatment(farm_id: int, disease_name: String, treatment_name: String) -> Dictionary:
	# Apply treatment, return result summary
	if not farms.has(farm_id):
		return {"success": false, "reason": "no_farm"}
	if not treatments.has(treatment_name):
		return {"success": false, "reason": "unknown_treatment"}
	var t = treatments[treatment_name]
	var current_prev = farms[farm_id].infections.get(disease_name, 0.0)
	var reduction = t.efficacy * current_prev
	farms[farm_id].infections[disease_name] = clamp(current_prev - reduction, 0.0, 1.0)
	# side-effect mortality reduces biomass
	var mortality = t.side_effect_mortality * (farms[farm_id].biomass_density)
	farms[farm_id].biomass_density = max(0.0, farms[farm_id].biomass_density - mortality)
	return {"success": true, "cost": t.cost, "reduction": reduction, "mortality": mortality}

# Lightweight analytics hook (for UI or logging)
func emitanalytics(disease_name: String) -> void:
	# placeholder for analytics/event logging
	pass

# Utility to compute nearby infection pressure from neighboring farms
func compute_nearby_pressure(farm_id: int, neighbors: Array) -> float:
	# neighbors: array of farm IDs with distance weighting
	var pressure = 0.0
	for n in neighbors:
		if not farms.has(n):
			continue
		for disease_name in farms[n].infections.keys():
			pressure += farms[n].infections[disease_name] * 0.5 # base transmissibility
	# normalize
	return clamp(pressure, 0.0, 5.0)

# Get farm summary
func get_farm_status(farm_id: int) -> Dictionary:
	if not farms.has(farm_id):
		return {}
	return farms[farm_id].duplicate(true)
