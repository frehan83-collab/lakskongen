# Hatchery - handles eggs -> smolt progression and stocking

extends Node
class_name Hatchery

var eggs: Array = [] # array of dictionaries {id, genetics, days_to_hatch}
var juveniles: Array = [] # Fish instances
var next_id: int = 1

func incubate_eggs(days: int = 1) -> void:
    for i in range(eggs.size() - 1, -1, -1):
        var e = eggs[i]
        e.days_to_hatch -= days
        if e.days_to_hatch <= 0:
            _hatch(e)
            eggs.remove_at(i)

func spawn_egg(genetics: Dictionary, incubation_days: int = 30) -> int:
    var eid = next_id
    next_id += 1
    eggs.append({"id": eid, "genetics": genetics.duplicate(true), "days_to_hatch": incubation_days})
    return eid

func _hatch(egg: Dictionary) -> void:
    # create a Fish as juvenile (smolt)
    var Fish = preload("res://godot/scripts/data/Fish.gd").Fish
    var f = Fish.new(next_id, egg.genetics, 0.2, 0)
    next_id += 1
    juveniles.append(f)

func stock_to_farm(count: int, farm_node: Node) -> Array:
    var moved = []
    for i in range(min(count, juveniles.size())):
        var f = juveniles.pop_front()
        if farm_node and farm_node.has_method("receive_stock"):
            farm_node.receive_stock(f)
        moved.append(f)
    return moved
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
