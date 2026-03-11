extends Node

class_name Feed

# Feed management system
# - Feed recipe composition: fishmeal, fish_oil, soy_protein, rapeseed_oil, insect_meal, astaxanthin
# - Each ingredient has: cost_per_kg, protein_content, omega3_content, color_contribution, sustainability_score
# - FCR (Feed Conversion Ratio): calculated from recipe quality and fish genetics, target ~1.15:1
# - Daily feed requirement based on fish biomass and temperature (fish eat more in warm water, almost nothing <4°C)
# - Feed waste model: overfeeding increases cost and environmental penalty
# - Feed supplier contracts: spot purchase vs locked contract pricing
# - Feeding automation levels: manual (higher waste), semi-auto, AI-driven (lowest waste, expensive upgrade)

signal feed_dispensed(kg, pen_id)

enum AutomationLevel {
    MANUAL,
    SEMI_AUTOMATIC,
    AI_DRIVEN
}

var ingredients: Dictionary = {
    "fishmeal": {"cost_per_kg": 1.2, "protein": 0.65, "omega3": 0.1, "color": 0.0, "sustainability": 0.5},
    "fish_oil": {"cost_per_kg": 2.0, "protein": 0.0, "omega3": 0.5, "color": 0.0, "sustainability": 0.4},
    "soy_protein": {"cost_per_kg": 0.8, "protein": 0.48, "omega3": 0.01, "color": 0.0, "sustainability": 0.7},
    "rapeseed_oil": {"cost_per_kg": 0.9, "protein": 0.0, "omega3": 0.02, "color": 0.0, "sustainability": 0.8},
    "insect_meal": {"cost_per_kg": 1.5, "protein": 0.6, "omega3": 0.03, "color": 0.0, "sustainability": 0.9},
    "astaxanthin": {"cost_per_kg": 50.0, "protein": 0.0, "omega3": 0.0, "color": 1.0, "sustainability": 0.6}
}

# Recipe is a map ingredient -> kg proportion (sum should be 1.0)
var recipe: Dictionary = {"fishmeal": 0.35, "fish_oil": 0.08, "soy_protein": 0.4, "rapeseed_oil": 0.12, "insect_meal": 0.04, "astaxanthin": 0.01}

# Supplier contracts
var suppliers: Dictionary = {
    "spot": {"type": "spot", "price_multiplier": 1.0},
    "contract": {"type": "contract", "price_multiplier": 0.92}
}

var selected_supplier: String = "spot"
var automation_level: AutomationLevel = AutomationLevel.MANUAL

# Feed conversion ratio baseline (ideal genetics)
var base_fcr: float = 1.15

# Waste multipliers by automation level
var waste_multipliers: Dictionary = {
    AutomationLevel.MANUAL: 0.20, # 20% extra waste
    AutomationLevel.SEMI_AUTOMATIC: 0.10,
    AutomationLevel.AI_DRIVEN: 0.03
}

# Calculate recipe quality metrics
func recipe_metrics() -> Dictionary:
    var total_cost = 0.0
    var protein = 0.0
    var omega3 = 0.0
    var color = 0.0
    var sustainability = 0.0
    for ingr in recipe.keys():
        var fraction = float(recipe[ingr])
        if not ingredients.has(ingr):
            continue
        var data = ingredients[ingr]
        total_cost += data["cost_per_kg"] * fraction
        protein += data["protein"] * fraction
        omega3 += data["omega3"] * fraction
        color += data["color"] * fraction
        sustainability += data["sustainability"] * fraction
    return {"cost_per_kg": total_cost, "protein": protein, "omega3": omega3, "color": color, "sustainability": sustainability}

# FCR influenced by recipe quality and genetics modifier (gene_factor <1 is better)
func compute_fcr(genetics_factor: float = 1.0) -> float:
    var metrics = recipe_metrics()
    var protein_score = clamp(metrics["protein"] / 0.5, 0.5, 1.5) # 0.5 reference
    var omega_score = clamp(metrics["omega3"] / 0.08, 0.5, 1.5)
    var sustainability_penalty = clamp(1.0 - metrics["sustainability"], 0.0, 0.7)
    var fcr = base_fcr * (1.0 / protein_score) * (1.0 / omega_score) * (1.0 + sustainability_penalty) * genetics_factor
    return max(0.8, fcr)

# Daily feed requirement (kg) based on biomass and temperature
func daily_feed_requirement(biomass_kg: float, temperature_c: float, genetics_factor: float = 1.0) -> float:
    # Feeding activity: near zero below 4C, increases up to optimal ~14-16C, then levels
    var activity = 0.0
    if temperature_c <= 4.0:
        activity = 0.05
    else:
        activity = clamp((temperature_c - 4.0) / 12.0, 0.05, 1.0)
    # base daily ration as percent of biomass: e.g., 1.5% to 2.5% depending on activity
    var percent = lerp(0.015, 0.025, activity)
    var feed_needed = biomass_kg * percent
    # Adjust by FCR to estimate actual feed input from growth potential and genetics
    var fcr = compute_fcr(genetics_factor)
    # The feed required to sustain is scaled by fcr (higher fcr -> more feed for same growth)
    var supplier_multiplier = suppliers.has(selected_supplier) ? suppliers[selected_supplier]["price_multiplier"] : 1.0
    return feed_needed * supplier_multiplier

# Feed waste model: overfeeding increases cost and environmental penalty
# feed_given_kg may be greater than required -> waste proportion determined by automation level
func dispense_feed(pen_id: String, feed_given_kg: float, biomass_kg: float, temperature_c: float, genetics_factor: float = 1.0) -> Dictionary:
    var required = daily_feed_requirement(biomass_kg, temperature_c, genetics_factor)
    var waste_multiplier = waste_multipliers[automation_level]
    var overfeed_ratio = 0.0
    if feed_given_kg > required:
        overfeed_ratio = (feed_given_kg - required) / required
    # waste increases non-linearly with overfeed
    var waste = feed_given_kg * (waste_multiplier + overfeed_ratio * 0.5)
    waste = min(waste, feed_given_kg)
    var consumed = feed_given_kg - waste
    var cost = feed_given_kg * recipe_metrics()["cost_per_kg"]
    emit_signal("feed_dispensed", feed_given_kg, pen_id)
    return {"given": feed_given_kg, "consumed": consumed, "waste": waste, "cost": cost, "required": required}

# Switch supplier contract
func set_supplier(supplier_key: String) -> void:
    if suppliers.has(supplier_key):
        selected_supplier = supplier_key

# Set automation level
func set_automation_level(level: int) -> void:
    automation_level = level

# Utility: compute expected daily FCR-adjusted growth (kg) from feed consumed
func estimated_growth_from_feed(feed_consumed_kg: float, genetics_factor: float = 1.0) -> float:
    var fcr = compute_fcr(genetics_factor)
    return feed_consumed_kg / fcr

# Helpers for editing recipe
func set_recipe_component(ingredient: String, fraction: float) -> void:
    recipe[ingredient] = fraction
    # Normalise so sum = 1.0 if desired externally

func normalize_recipe() -> void:
    var total = 0.0
    for v in recipe.values():
        total += float(v)
    if total <= 0.0:
        return
    for k in recipe.keys():
        recipe[k] = float(recipe[k]) / total

func get_recipe() -> Dictionary:
    return recipe.duplicate()

func report_metrics() -> Dictionary:
    var m = recipe_metrics()
    m["fcr"] = compute_fcr()
    return m
