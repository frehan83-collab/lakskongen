# scripts/Hatchery.gd
# Simulates freshwater/hatchery phase with eggs -> smolt progression
extends Node

const SMOLT_SIZE_KG = 0.05

var eggs: Array = [] # list of dictionaries representing eggs/fry
var next_id: int = 1

func spawn_eggs(count:int, parent_a:Dictionary, parent_b:Dictionary):
	for i in range(count):
		var child = preload("res://scripts/Genetics.gd").breed(parent_a, parent_b, next_id)
		next_id += 1
		eggs.append(child)

func progress_days(days:int, temperature:float=8.0):
	# simple development: after X days become smolt
	var hatched = []
	for e in eggs:
		# increase age
		e['age_days'] += days
		# growth influenced by temperature; use simple linear growth
		var growth_factor = clamp(lerp(0.5, 1.2, (temperature-4.0)/10.0), 0.4, 1.5)
		e['weight_kg'] += 0.0005 * days * growth_factor
		# hatch threshold
		if e['age_days'] >= 180 or e['weight_kg'] >= SMOLT_SIZE_KG:
			hatched.append(e)
	# remove hatched
	for h in hatched:
		eggs.erase(h)
	return hatched # return smolts ready to transfer

func get_count():
	return eggs.size()

func to_dict():
	return {"eggs": eggs.duplicate(true)}
