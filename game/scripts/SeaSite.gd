extends Node

class_name SeaSite

# Sea farm site management
# - Net pen management: capacity (200,000 fish), stocking density limit (25 kg/m^3)
# - Multiple pens per site
# - Biomass tracking (total weight of all fish)
# - MAB (Maximum Allowable Biomass) limit per license: 780 tonnes
# - Fallow period tracking (mandatory between cycles)
# - Site properties: zone, coordinates, current_speed, depth
# - Harvest system: select pens, harvest by size threshold (target 4-6kg HOG)
# - Mortality tracking and reporting

signal biomass_changed(new_biomass_kg)
signal mortality_reported(pen_id, dead_kg)
signal harvest_executed(pen_ids, harvested_kg)

const PEN_CAPACITY_FISH: int = 200000
const STOCKING_DENSITY_LIMIT_KG_M3: float = 25.0
const DEFAULT_MAB_KG: float = 780000.0 # 780 tonnes in kg

# Represent a pen with a Dictionary for flexibility
# pen: {
#   id: String,
#   capacity_fish: int,
#   current_fish_count: int,
#   average_weight_kg: float,
#   volume_m3: float,
#   biomass_kg: float,
#   last_stocked: DateTime (string),
#   fallow_until: DateTime (string/null)
# }

var site_id: String
var zone: String = "unknown"
var coordinates: Vector2 = Vector2.ZERO
var current_speed: float = 0.0 # m/s
var depth: float = 30.0 # m

var license_ref: String = "" # link to license id

var pens: Array = []

var mab_limit_kg: float = DEFAULT_MAB_KG

var total_biomass_kg: float = 0.0 setget _set_total_biomass

var mortality_log: Array = [] # array of {pen_id, dead_count, dead_kg, timestamp}

# Fallow tracking
var is_in_fallow: bool = false
var fallow_end_date: String = "" # ISO date string

func _init(_site_id: String = "site_01", _zone: String = "green", _coords: Vector2 = Vector2.ZERO, _depth: float = 30.0, _current_speed: float = 0.0) -> void:
    site_id = _site_id
    zone = _zone
    coordinates = _coords
    depth = _depth
    current_speed = _current_speed
    pens = []

# Pen management
func add_pen(pen_id: String, volume_m3: float, capacity_fish: int = PEN_CAPACITY_FISH) -> Dictionary:
    var pen: Dictionary = {
        "id": pen_id,
        "capacity_fish": capacity_fish,
        "current_fish_count": 0,
        "average_weight_kg": 0.0,
        "volume_m3": volume_m3,
        "biomass_kg": 0.0,
        "last_stocked": "",
        "fallow_until": ""
    }
    pens.append(pen)
    return pen

func remove_pen(pen_id: String) -> bool:
    for i in pens.size()-1:-1:0:
        if pens[i]["id"] == pen_id:
            pens.remove_at(i)
            _recalculate_biomass()
            return true
    return false

func find_pen(pen_id: String) -> Dictionary:
    for p in pens:
        if p["id"] == pen_id:
            return p
    return {}

# Stock fish into a pen (number of fish, avg weight kg)
func stock_fish(pen_id: String, number: int, avg_weight_kg: float, timestamp: String = "") -> bool:
    var pen = find_pen(pen_id)
    if pen.empty():
        return false
    if number + pen["current_fish_count"] > pen["capacity_fish"]:
        push_warning("Exceeds pen capacity")
        return false
    var new_biomass = pen["biomass_kg"] + number * avg_weight_kg
    var density = new_biomass / pen["volume_m3"]
    if density > STOCKING_DENSITY_LIMIT_KG_M3:
        push_warning("Exceeds stocking density limit")
        return false
    pen["current_fish_count"] += number
    # update weighted average
    if pen["current_fish_count"] > 0:
        pen["average_weight_kg"] = (pen["biomass_kg"] + number * avg_weight_kg) / pen["current_fish_count"]
    else:
        pen["average_weight_kg"] = 0.0
    pen["biomass_kg"] = new_biomass
    pen["last_stocked"] = timestamp
    _recalculate_biomass()
    return true

# Mortality tracking
func report_mortality(pen_id: String, dead_count: int, avg_dead_weight_kg: float, timestamp: String = "") -> bool:
    var pen = find_pen(pen_id)
    if pen.empty():
        return false
    var dead_kg = dead_count * avg_dead_weight_kg
    pen["current_fish_count"] = max(0, pen["current_fish_count"] - dead_count)
    pen["biomass_kg"] = max(0.0, pen["biomass_kg"] - dead_kg)
    mortality_log.append({"pen_id": pen_id, "dead_count": dead_count, "dead_kg": dead_kg, "timestamp": timestamp})
    emit_signal("mortality_reported", pen_id, dead_kg)
    _recalculate_biomass()
    return true

# Harvest by size threshold (e.g., harvest fish >= min_size_kg and <= max_size_kg)
# Returns harvested_kg
func harvest_by_size(min_size_kg: float = 4.0, max_size_kg: float = 6.0, pen_ids: Array = []) -> float:
    var harvested_total_kg: float = 0.0
    var harvested_pens: Array = []
    var target_pens = pen_ids.size() > 0 ? pen_ids : [p["id"] for p in pens]
    for pid in target_pens:
        var pen = find_pen(pid)
        if pen.empty():
            continue
        # Assume portion of fish within size range proportional to distribution around avg weight
        var avg = pen["average_weight_kg"]
        if avg >= min_size_kg and avg <= max_size_kg:
            harvested_total_kg += pen["biomass_kg"]
            harvested_pens.append(pid)
            # empty the pen
            pen["current_fish_count"] = 0
            pen["biomass_kg"] = 0.0
            pen["average_weight_kg"] = 0.0
            pen["last_stocked"] = ""
    if harvested_total_kg > 0.0:
        emit_signal("harvest_executed", harvested_pens, harvested_total_kg)
        _recalculate_biomass()
    return harvested_total_kg

# Recalculate total site biomass
func _recalculate_biomass() -> void:
    var sum: float = 0.0
    for p in pens:
        sum += float(p["biomass_kg"])
    total_biomass_kg = sum

func _set_total_biomass(value: float) -> void:
    total_biomass_kg = value
    emit_signal("biomass_changed", total_biomass_kg)

# Compliance checks against license MAB
func check_mab_compliance() -> bool:
    return total_biomass_kg <= mab_limit_kg

# Start a fallow period for the site (duration days)
func start_fallow(duration_days: int, start_date_iso: String = "") -> void:
    is_in_fallow = true
    # naive date handling: store as string for game simulation
    fallow_end_date = start_date_iso + " +%d days".sprintf(duration_days) if start_date_iso != "" else "in_%d_days".sprintf(duration_days)

func end_fallow() -> void:
    is_in_fallow = false
    fallow_end_date = ""

# Daily update hook (call each game day): could include growth, mortality events, environment effects
func daily_update(temperature_c: float, growth_rate_modifier: float = 1.0) -> void:
    # simplistic growth model: fish grow proportional to temperature and modifier
    var grown_kg: float = 0.0
    for p in pens:
        if p["current_fish_count"] <= 0:
            continue
        var temp_factor = 0.0
        if temperature_c > 4.0:
            temp_factor = clamp((temperature_c - 4.0) / 16.0, 0.0, 1.0) # warms -> more growth
        var daily_growth_per_fish = 0.003 * temp_factor * growth_rate_modifier # kg per fish per day (placeholder)
        var growth = daily_growth_per_fish * p["current_fish_count"]
        p["biomass_kg"] += growth
        p["average_weight_kg"] = p["biomass_kg"] / max(1, p["current_fish_count"])
        grown_kg += growth
    if grown_kg > 0:
        _recalculate_biomass()

# Utilities
func get_pen_summary() -> Array:
    var out: Array = []
    for p in pens:
        out.append({"id": p["id"], "fish": p["current_fish_count"], "biomass_kg": p["biomass_kg"], "avg_wt": p["average_weight_kg"]})
    return out

func get_mortality_report() -> Array:
    return mortality_log.duplicate()

# Simple reporting helper
func report_status() -> Dictionary:
    return {
        "site_id": site_id,
        "zone": zone,
        "coords": coordinates,
        "depth": depth,
        "current_speed": current_speed,
        "total_biomass_kg": total_biomass_kg,
        "mab_limit_kg": mab_limit_kg,
        "pens": get_pen_summary(),
        "in_fallow": is_in_fallow,
        "fallow_end": fallow_end_date
    }
