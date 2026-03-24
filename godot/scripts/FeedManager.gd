# FeedManager - temperature- and quality-dependent FCR model, dynamic pricing, and cohort cost API

extends Node
class_name FeedManager

# feed quality in [0,1]
var feed_quality: float = 0.8
var base_fcr: float = 1.2 # feed conversion ratio at optimal conditions (quality=1, optimal temp)
var feed_base_price_per_kg: float = 1.5

# temperature model parameters
var optimal_temp_c: float = 14.0
var temp_tolerance_c: float = 4.0 # within +/- this range performance is near-optimal
var temp_sensitivity: float = 0.03 # fractional FCR increase per degree beyond tolerance

# quality model parameters
var quality_sensitivity: float = 0.4 # fractional FCR increase at quality=0 relative to quality=1

func set_feed_quality(q: float) -> void:
    feed_quality = clamp(q, 0.0, 1.0)

func set_temperature_model(opt_temp: float, tol: float, sensitivity: float) -> void:
    optimal_temp_c = opt_temp
    temp_tolerance_c = tol
    temp_sensitivity = sensitivity

func compute_fcr_for_conditions(water_temp_c: float) -> float:
    # Temperature effect: when within tolerance, no penalty. Beyond that, penalty grows linearly with degrees out of tolerance.
    var temp_diff = abs(water_temp_c - optimal_temp_c)
    var temp_penalty_fraction = 0.0
    if temp_diff > temp_tolerance_c:
        temp_penalty_fraction = (temp_diff - temp_tolerance_c) * temp_sensitivity

    # Quality effect: lower quality increases FCR up to quality_sensitivity fraction when quality=0.
    var quality_penalty_fraction = (1.0 - feed_quality) * quality_sensitivity

    var fcr = base_fcr * (1.0 + temp_penalty_fraction + quality_penalty_fraction)
    return max(fcr, 0.1)

func feed_needed_for_growth(gain_g: float, water_temp_c: float) -> float:
    # returns kg of feed needed for gain_g grams under given water temperature
    var fcr = compute_fcr_for_conditions(water_temp_c)
    var feed_g = gain_g * fcr
    return feed_g / 1000.0

func dynamic_price_per_kg(demand_index: float=1.0) -> float:
    # demand_index ~1.0 is normal, >1 higher demand. Price increases with demand.
    # Higher-quality feed commands a premium; lower-quality discount applied.
    var quality_premium = feed_quality * 0.15 # up to +15% for top quality
    var quality_discount = (1.0 - feed_quality) * 0.10 # up to -10% for lowest quality
    var price = feed_base_price_per_kg * (1.0 + (demand_index - 1.0)*0.2 + quality_premium - quality_discount)
    return max(price, 0.01)

func cost_for_feed_kg(kg: float, demand_index: float=1.0) -> float:
    return kg * dynamic_price_per_kg(demand_index)

func daily_feed_cost_for_cohort(cohort_count: int, avg_daily_growth_g: float, water_temp_c: float, demand_index: float=1.0) -> float:
    # Computes total daily feed cost for a cohort of fish
    if cohort_count <= 0 or avg_daily_growth_g <= 0.0:
        return 0.0
    var feed_per_fish_kg = feed_needed_for_growth(avg_daily_growth_g, water_temp_c)
    var total_feed_kg = feed_per_fish_kg * float(cohort_count)
    return cost_for_feed_kg(total_feed_kg, demand_index)

# convenience API for external calls
func compute_daily_feed_and_cost(cohort_count: int, avg_daily_growth_g: float, water_temp_c: float, demand_index: float=1.0) -> Dictionary:
    var feed_per_fish_kg = feed_needed_for_growth(avg_daily_growth_g, water_temp_c)
    var total_feed_kg = feed_per_fish_kg * float(max(cohort_count, 0))
    var total_cost = cost_for_feed_kg(total_feed_kg, demand_index)
    return {"feed_per_fish_kg": feed_per_fish_kg, "total_feed_kg": total_feed_kg, "total_cost": total_cost}
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
