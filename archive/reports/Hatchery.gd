extends Node

var eggs = []

func incubate_eggs(days: int = 1) -> void:
    for i in range(eggs.size() - 1, -1, -1):
        var e = eggs[i]
        e.days_to_hatch -= days
        if e.days_to_hatch <= 0:
            _hatch(e)
            eggs.remove_at(i)

func _hatch(egg):
    # Logic to hatch an egg
    pass