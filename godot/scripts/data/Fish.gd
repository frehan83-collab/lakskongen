# Fish data model - GDScript
# Implements empirical growth models (von Bertalanffy and Gompertz)

class_name Fish
extends RefCounted

var id: int
var age_days: int = 0
var weight_g: float = 0.0
var alive: bool = true
var genetics = {} # dictionary of loci -> allele tuples

# growth model selection and parameters
enum GrowthModel { VONBERTALANFFY, GOMPERTZ }
var growth_model: int = GrowthModel.VONBERTALANFFY

# Common configurable parameters (defaults reasonable for salmon-like fish)
var k: float = 0.0025 # growth coefficient (per day) for VB
var Linf_g: float = 4000.0 # asymptotic weight in grams for VB
var t0_days: float = -30.0 # theoretical age at zero size for VB

# Gompertz params: weight(t) = A * exp(-exp(-k*(t - t_inf)))
var A_g: float = 4000.0
var k_g: float = 0.0025
var t_inf_days: float = 365.0

# environmental modifiers
var temp_c: float = 10.0 # ambient temperature (C)
var feed_quality: float = 1.0 # multiplier [0..2]

# mortality
var mortality_rate_daily: float = 0.0005

func _init(_id: int = 0, _genetics: Dictionary = {}, _weight_g: float = 0.1, _age_days: int = 0) -> void:
    id = _id
    genetics = _genetics.duplicate(true)
    weight_g = _weight_g
    age_days = _age_days

# Deterministic growth curve functions
func von_bertalanffy_weight(age_days_in: float) -> float:
    # VB usually defined for length; here we apply on weight by treating Linf as asymptotic weight
    # W(t) = Linf * (1 - exp(-k*(t - t0)))^3  -- cubic relationship between length and weight
    var t = max(age_days_in, 0.0)
    var inner = 1.0 - exp(-k * (t - t0_days))
    return Linf_g * pow(inner, 3)

func gompertz_weight(age_days_in: float) -> float:
    var t = max(age_days_in, 0.0)
    return A_g * exp(-exp(-k_g * (t - t_inf_days)))

func growth_environment_modifier() -> float:
    # Simple model: Q10-like temperature effect and linear feed quality
    # Q10 rule: metabolic rate roughly doubles every 10 C -> factor = Q10^((T-Ref)/10)
    var Q10 = 2.0
    var ref_T = 10.0
    var temp_factor = pow(Q10, (temp_c - ref_T) / 10.0)
    var feed_factor = clamp(feed_quality, 0.0, 2.0)
    return temp_factor * feed_factor

func expected_weight_at_age(age_days_in: float) -> float:
    var base: float
    match growth_model:
        GrowthModel.VONBERTALANFFY:
            base = von_bertalanffy_weight(age_days_in)
        GrowthModel.GOMPERTZ:
            base = gompertz_weight(age_days_in)
        _:
            base = weight_g
    return base * growth_environment_modifier()

func day_tick(days: int = 1) -> void:
    if not alive:
        return
    age_days += days
    # deterministic update to expected weight (tests rely on deterministic behaviour)
    weight_g = expected_weight_at_age(age_days)

func survival_probability(days: int = 1) -> float:
    # probability to survive `days` assuming constant hazard
    return pow(1.0 - mortality_rate_daily, days)

func serialize() -> Dictionary:
    return {
        "id": id,
        "age_days": age_days,
        "weight_g": weight_g,
        "alive": alive,
        "genetics": genetics,
    }

func clone(new_id: int = 0) -> Fish:
    var f = Fish.new(new_id, genetics, weight_g, age_days)
    f.growth_model = growth_model
    f.k = k
    f.Linf_g = Linf_g
    f.t0_days = t0_days
    f.A_g = A_g
    f.k_g = k_g
    f.t_inf_days = t_inf_days
    f.temp_c = temp_c
    f.feed_quality = feed_quality
    f.mortality_rate_daily = mortality_rate_daily
    f.alive = alive
    return f
