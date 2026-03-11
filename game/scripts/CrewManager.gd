extends Node

# CrewManager: manages workers, hiring/firing, daily payroll and XP/morale progression

signal payroll_processed(total_cost)

var workers := [] # Array of Worker references
var next_id: int = 1

# Cost model parameters
var recruitment_fee = 5000.0
var training_cost_per_xp = 0.1

func _ready():
    # create a small initial crew
    _create_initial_crew()

func _create_initial_crew() -> void:
    # Seed deterministic example crew (no randomness to aid tests)
    hire_worker("Ole", 1200.0)
    hire_worker("Ingrid", 1100.0)
    hire_worker("Kari", 900.0)

func hire_worker(name: String, salary_per_day: float = 1000.0) -> Worker:
    var WorkerClass = preload("res://game/scripts/Worker.gd")
    var w = WorkerClass.new(next_id, name, salary_per_day)
    next_id += 1
    workers.append(w)
    # recruitment fee charged by caller (finance subsystem should account for recruitment_fee)
    return w

func fire_worker(worker_id: int) -> bool:
    for i in range(workers.size()):
        if workers[i].id == worker_id:
            workers.remove_at(i)
            return true
    return false

# Daily update called by GameManager
# Returns total daily payroll cost
func daily_update(day: int) -> float:
    var total = 0.0
    # Simple deterministic workload model: workload_factor depends on number of workers
    var workload_factor = _compute_workload_factor()
    for w in workers:
        w.daily_tick(workload_factor)
        total += float(w.salary_per_day)
    emit_signal("payroll_processed", total)
    return total

func _compute_workload_factor() -> float:
    # If few workers, workload increases: base 0.8, scales inversely with staff count
    var count = max(1, workers.size())
    var factor = 0.8 + 0.2 * (2.0 / float(count))
    return clamp(factor, 0.5, 2.0)

# Report for UI / GameManager
func report() -> Dictionary:
    var total_salary = 0.0
    var total_morale = 0.0
    var list := []
    for w in workers:
        total_salary += float(w.salary_per_day)
        total_morale += float(w.morale)
        list.append(w.to_dict())
    var avg_morale = 0.0
    if workers.size() > 0:
        avg_morale = total_morale / float(workers.size())
    return {
        "staff_count": workers.size(),
        "avg_morale": avg_morale,
        "total_daily_salary": total_salary,
        "workers": list
    }

# Auto-hire simple policy: hire if staff below threshold
func auto_hire_if_needed(min_staff: int = 3) -> Array:
    var hired := []
    while workers.size() < min_staff:
        var n = "Auto%d".sprintf([next_id])
        var w = hire_worker(n, 1000.0)
        hired.append(w.to_dict())
    return hired

# Deterministic test helpers
func clear_workers() -> void:
    workers.clear()
    next_id = 1

func get_worker_by_id(id_val: int) -> Worker:
    for w in workers:
        if w.id == id_val:
            return w
    return null
