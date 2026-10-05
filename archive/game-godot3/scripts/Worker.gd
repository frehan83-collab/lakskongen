extends Reference

class_name Worker

# Simple worker / crew member model
# Skills: Dictionary mapping skill name -> level (0.0..1.0)
# XP: Dictionary mapping skill name -> xp (float)

var id: int
var name: String = ""
var salary_per_day: float = 1000.0
var morale: float = 75.0 # 0..100
var hired: bool = true
var skills := {} # skill -> level (0.0..1.0)
var xp := {} # skill -> xp float
var role: String = "general"

func _init(p_id: int = 0, p_name: String = "Worker", p_salary: float = 1000.0) -> void:
    id = p_id
    name = p_name
    salary_per_day = p_salary
    morale = 75.0
    hired = true
    skills = {
        "feeding": 0.4,
        "harvesting": 0.3,
        "maintenance": 0.3,
        "veterinary": 0.2,
        "processing": 0.3,
        "logistics": 0.4
    }
    xp = {}
    for k in skills.keys():
        xp[k] = 0.0

func to_dict() -> Dictionary:
    return {
        "id": id,
        "name": name,
        "salary_per_day": salary_per_day,
        "morale": morale,
        "hired": hired,
        "role": role,
        "skills": skills.duplicate(true)
    }

# Gain XP in a particular skill; if enough XP, level up (simple linear progression)
func gain_xp(skill: String, amount: float) -> void:
    if not xp.has(skill):
        xp[skill] = 0.0
    xp[skill] += amount
    # Level up threshold: every 100 XP -> +0.05 skill level (capped 1.0)
    var gained_levels = int(xp[skill] / 100.0)
    if gained_levels > 0:
        var inc = gained_levels * 0.05
        var old = skills.get(skill, 0.0)
        skills[skill] = clamp(old + inc, 0.0, 1.0)
        xp[skill] = xp[skill] - gained_levels * 100.0

# Daily tick: passive XP gain based on role and morale
func daily_tick(workload_factor: float = 1.0) -> void:
    # Workload_factor: 0..1 scale of intensity; higher => more XP but more morale drain
    for skill_name in skills.keys():
        var base = 0.2 # base XP/day for on-the-job learning
        var morale_bonus = morale / 100.0
        var skill_focus = 0.5 + (skills[skill_name] - 0.5) # specialists learn slightly faster in their stronger skills
        var amount = base * workload_factor * morale_bonus * skill_focus
        gain_xp(skill_name, amount)
    # morale adjustment: small drift depending on workload
    morale = clamp(morale - 0.1 * workload_factor + 0.01 * (100.0 - morale) * 0.0, 0.0, 100.0)

# Productivity estimate for a given task (0..1)
func productivity_for(task: String) -> float:
    var s = skills.get(task, 0.2)
    # morale scales productivity linearly between 0.5x to 1.2x
    var morale_factor = 0.5 + (morale / 100.0) * 0.7
    return clamp(s * morale_factor, 0.0, 1.0)
