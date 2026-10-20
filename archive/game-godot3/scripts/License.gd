extends Node

class_name License

# Regulatory license system
# - License ownership tracking
# - MAB per license (780 tonnes standard)
# - Traffic light system: zone colors (green/yellow/red) affect allowed MAB changes
# - License purchase/sale marketplace
# - Compliance checking: biomass vs MAB, lice limits, fallow periods

signal license_transferred(new_owner)
signal license_listed(license_id, price)
signal compliance_violation(reason)

const STANDARD_MAB_KG: float = 780000.0 # 780 tonnes

enum ZoneColor { GREEN, YELLOW, RED }

var license_id: String = ""
var owner: String = ""
var mab_kg: float = STANDARD_MAB_KG
var zone_color: ZoneColor = ZoneColor.GREEN
var listed_for_sale: bool = false
var listed_price: float = 0.0

# Marketplace static listing array (in real game, this would be global manager)
static var marketplace: Array = []

# Compliance parameters
var lice_limit_per_fish: float = 0.2 # placeholder threshold
var requires_fallow_days: int = 30

func _init(_license_id: String = "lic_001", _owner: String = "OperatorA", _zone: int = ZoneColor.GREEN) -> void:
    license_id = _license_id
    owner = _owner
    zone_color = _zone
    mab_kg = STANDARD_MAB_KG

# Zone affects allowed MAB changes: in RED no increases allowed, in YELLOW limited, in GREEN flexible
func adjust_mab(new_mab_kg: float) -> bool:
    match zone_color:
        ZoneColor.RED:
            # cannot increase MAB in red; only decreases allowed
            if new_mab_kg > mab_kg:
                push_warning("MAB increase not allowed in RED zone")
                return false
        ZoneColor.YELLOW:
            # limited increases up to +10%
            if new_mab_kg > mab_kg * 1.10:
                push_warning("MAB increase exceeds zone limit")
                return false
        ZoneColor.GREEN:
            # free to adjust within reasonable game limits
            pass
    mab_kg = new_mab_kg
    return true

# List license for sale in marketplace
func list_for_sale(price: float) -> void:
    listed_for_sale = true
    listed_price = price
    marketplace.append({"license_id": license_id, "owner": owner, "price": price})
    emit_signal("license_listed", license_id, price)

func delist() -> void:
    listed_for_sale = false
    listed_price = 0.0
    for i in marketplace.size()-1:-1:0:
        if marketplace[i]["license_id"] == license_id:
            marketplace.remove_at(i)

# Purchase license from marketplace (buyer must match listing)
func purchase(buyer: String, offered_price: float) -> bool:
    if not listed_for_sale:
        push_warning("License not for sale")
        return false
    if offered_price < listed_price:
        push_warning("Offered price too low")
        return false
    # Transfer ownership
    owner = buyer
    delist()
    emit_signal("license_transferred", buyer)
    return true

# Compliance checks: biomass vs MAB, lice limits (avg lice per fish), fallow periods
func check_compliance(biomass_kg: float, avg_lice_per_fish: float, in_fallow: bool, fallow_end_date: String = "") -> Dictionary:
    var violations: Array = []
    if biomass_kg > mab_kg:
        violations.append("MAB_exceeded")
    if avg_lice_per_fish > lice_limit_per_fish:
        violations.append("Lice_limit_exceeded")
    if in_fallow:
        # if site is in fallow but not yet ended, this is fine; but if required fallow not observed, violation
        # here we assume external logic enforces fallow timing; placeholder
        pass
    if violations.size() > 0:
        emit_signal("compliance_violation", violations.join(","))
    return {"ok": violations.size() == 0, "violations": violations}

# Simple marketplace query
static func query_marketplace() -> Array:
    return marketplace.duplicate()

# Utility to set zone color from string
func set_zone_from_string(z: String) -> void:
    match z.to_lower():
        "green": zone_color = ZoneColor.GREEN
        "yellow": zone_color = ZoneColor.YELLOW
        "red": zone_color = ZoneColor.RED
        _: pass

# Simple serialization
func to_dict() -> Dictionary:
    return {"license_id": license_id, "owner": owner, "mab_kg": mab_kg, "zone_color": int(zone_color), "listed": listed_for_sale, "price": listed_price}
