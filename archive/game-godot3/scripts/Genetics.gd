# scripts/Genetics.gd
# Simple genetics utilities for Phase 1

func breed(parent_a:Dictionary, parent_b:Dictionary, child_id:int)->Dictionary:
	# Parents are dictionaries with a 'genetics' dict
	var ga = parent_a.get("genetics", {})
	var gb = parent_b.get("genetics", {})
	var child_gen = {}
	for k in ga.keys():
		var a = ga[k]
		var b = gb.get(k, a)
		# simple averaging + small mutation
		var val = (a + b) * 0.5
		# mutation +/- up to 3 points
		val += randf_range(-3.0, 3.0)
		val = clamp(val, 1.0, 100.0)
		child_gen[k] = val
	return {
		"id": child_id,
		"genetics": child_gen,
		"age_days": 0,
		"weight_kg": 0.01,
		"alive": true,
	}

func create_random(id:int)->Dictionary:
	return {
		"id": id,
		"genetics": {
			"growth": randf_range(30.0, 70.0),
			"disease_resistance": randf_range(30.0, 70.0),
			"fillet_quality": randf_range(30.0, 70.0),
		},
		"age_days": 0,
		"weight_kg": 0.01,
		"alive": true,
	}
