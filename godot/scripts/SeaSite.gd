# SeaSite - manages pens/MABs, stocking, growth, and harvest hooks

extends Node
class_name SeaSite

signal stocked(pen_id, fish_count)
signal harvest_ready(pen_id, harvested_count, total_weight_kg)

var pens: Array = [] # each pen is a dictionary: {id:int, fish:Array of Fish}
var next_pen_id: int = 1

func create_pen() -> int:
    var pid = next_pen_id
    next_pen_id += 1
    pens.append({"id": pid, "fish": []})
    return pid

func list_pens() -> Array:
    return pens.duplicate(true)

func receive_stock(fish: Object) -> void:
    # default: put into first pen, create pen if none
    if pens.size() == 0:
        create_pen()
    pens[0]["fish"].append(fish)
    emit_signal("stocked", pens[0]["id"], 1)

func stock_multiple(fishes: Array, pen_id: int = -1) -> int:
    var target = null
    if pen_id == -1 and pens.size() == 0:
        create_pen()
    if pen_id == -1:
        target = pens[0]
    else:
        for p in pens:
            if p.id == pen_id or p["id"] == pen_id:
                target = p
                break
    if target == null:
        return 0
    for f in fishes:
        target["fish"].append(f)
    emit_signal("stocked", target["id"], fishes.size())
    return fishes.size()

func pen_count(pen_id: int) -> int:
    for p in pens:
        if p["id"] == pen_id:
            return p["fish"].size()
    return 0

func advance_days(days: int = 1) -> void:
    # advance all fish and perform simple mortality (fish.day_tick handles deterministic growth)
    for p in pens:
        var remove = []
        for f in p["fish"]:
            if f.has_method("day_tick"):
                f.day_tick(days)
            # simple stochastic-free mortality: use survival_probability deterministic check
            if f.has_method("survival_probability"):
                var surv = f.survival_probability(days)
                if surv <= 0.0:
                    f.alive = false
            if f.alive == false:
                remove.append(f)
        for r in remove:
            p["fish"].erase(r)

func compute_pen_biomass_kg(pen_id: int) -> float:
    for p in pens:
        if p["id"] == pen_id:
            var total_g = 0.0
            for f in p["fish"]:
                if f.alive:
                    total_g += float(f.weight_g)
            return total_g / 1000.0
    return 0.0

func harvest_pen(pen_id: int, min_weight_g: float = 2000.0) -> Dictionary:
    # harvest fish above min_weight_g, remove them and emit harvest_ready signal
    for p in pens:
        if p["id"] == pen_id:
            var to_harvest = []
            var total_w_g = 0.0
            for f in p["fish"]:
                if f.alive and float(f.weight_g) >= min_weight_g:
                    to_harvest.append(f)
                    total_w_g += float(f.weight_g)
            for h in to_harvest:
                p["fish"].erase(h)
            var count = to_harvest.size()
            var total_kg = total_w_g / 1000.0
            emit_signal("harvest_ready", pen_id, count, total_kg)
            return {"count": count, "total_kg": total_kg}
    return {"count":0, "total_kg":0.0}

func empty_pen(pen_id: int) -> int:
    for p in pens:
        if p["id"] == pen_id:
            var removed = p["fish"].size()
            p["fish"].clear()
            return removed
    return 0
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
