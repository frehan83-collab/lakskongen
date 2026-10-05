extends Node

# GameManager.gd
# Integrates subsystems and runs the main simulation loop

signal state_updated(state: Dictionary)

var environment
var license
var sea_site
var feed
var disease
var sea_lice
var finance
var market
var hatchery
var time_system

var farm_id: int = 1
var zone_id: int = 7 # Trøndelag / mid zone

func _ready():
    # Instantiate subsystems as child nodes so Godot manages their lifecycle
    environment = preload("res://game/scripts/Environment.gd").new()
    add_child(environment)

    license = preload("res://game/scripts/License.gd").new("lic_001", "OperatorA")
    add_child(license)

    sea_site = preload("res://game/scripts/SeaSite.gd").new("site_01", str(zone_id), Vector2.ZERO, 40.0, 0.3)
    add_child(sea_site)
    # create 3 pens of 10_000 m3 each
    for i in range(1, 4):
        sea_site.add_pen("pen_%d".sprintf(i), 10000.0)

    feed = preload("res://game/scripts/Feed.gd").new()
    add_child(feed)

    disease = preload("res://game/scripts/Disease.gd").new()
    add_child(disease)

    sea_lice = preload("res://game/scripts/SeaLice.gd").new()
    add_child(sea_lice)

    finance = preload("res://game/scripts/Finance.gd").new()
    add_child(finance)
    # set starting cash to 50M NOK
    finance.reset_financials(50_000_000.0)

    market = preload("res://game/scripts/Market.gd").new()
    add_child(market)

    hatchery = preload("res://game/scripts/Hatchery.gd").new()
    add_child(hatchery)

    time_system = preload("res://game/scripts/TimeSystem.gd").new()
    add_child(time_system)
    # start time system
    time_system.start()

    # connect time tick
    time_system.connect("day_advanced", Callable(self, "_on_day_advanced"))

    # connect environment events
    environment.connect("algal_bloom", Callable(self, "_on_algal_bloom"))
    environment.connect("storm", Callable(self, "_on_storm"))
    environment.connect("jellyfish_bloom", Callable(self, "_on_jellyfish"))

    # Crew / workforce subsystem
    crew_manager = preload("res://game/scripts/CrewManager.gd").new()
    add_child(crew_manager)
    # listen to payroll events so finance can be charged by GameManager when state updated
    crew_manager.connect("payroll_processed", Callable(self, "_on_payroll_processed"))

    # register farm in disease and sea lice
    # biomass density initial guess (tonnes per area-like unit); use small value
    disease.register_farm(farm_id, zone_id, 1.0, 0.8)
    sea_lice.register_farm(farm_id, 0.1, 0.02)

    # spawn initial broodstock and eggs
    var Genetics = preload("res://game/scripts/Genetics.gd")
    var parent_a = Genetics.create_random(0)
    var parent_b = Genetics.create_random(1)
    # spawn some eggs (e.g., 2000)
    hatchery.spawn_eggs(2000, parent_a, parent_b)

    # fast-forward hatchery until smolts ready (simulate 180 days)
    var temp = environment.get_temperature(zone_id)
    var smolts = hatchery.progress_days(180, temp)
    if smolts.size() > 0:
        _stock_smolts(smolts)

    # emit initial state
    emit_signal("state_updated", to_dict())


func _on_day_advanced(day: int) -> void:
    # Run daily update loop
    # 1) advance environment
    environment.advance_day(1.0)

    # get current temperature for farm zone
    var temp = environment.get_temperature(zone_id)

    # 2) progress hatchery by one day and stock any ready smolts
    var new_smolts = hatchery.progress_days(1, temp)
    if new_smolts.size() > 0:
        _stock_smolts(new_smolts)

    # 3) Feeding: compute site feed requirement and dispense
    var biomass = sea_site.total_biomass_kg
    var genetics_factor = 1.0
    var feed_req = feed.daily_feed_requirement(biomass, temp, genetics_factor)
    # Distribute feed across pens proportionally to biomass
    var pens = sea_site.pens
    var total_biomass = 0.0
    for p in pens:
        total_biomass += float(p["biomass_kg"])
    # if no biomass, skip
    if total_biomass > 0.0 and feed_req > 0.0:
        var total_cost = 0.0
        for p in pens:
            var pen_b = float(p["biomass_kg"])
            var portion = pen_b / total_biomass if total_biomass > 0 else 0
            var feed_given = feed_req * portion
            var res = feed.dispense_feed(p["id"], feed_given, pen_b, temp, genetics_factor)
            total_cost += float(res["cost"])
        # record feed expense
        var week = int(floor(day / 7)) + 1
        finance.record_expense(total_cost, "feed_purchase", "feed", week)

    # 4) Grow fish in sea site
    # growth modifier estimate from feed: assume consumed/feed * estimated growth
    sea_site.daily_update(temp, 1.0)

    # 5) Disease and sea lice progression
    var nearby_pressure = disease.compute_nearby_pressure(farm_id, [])
    disease.set_farm_biomass(farm_id, sea_site.total_biomass_kg / 1000.0) # adjust internal density metric
    disease.step_infections(farm_id, temp, nearby_pressure, 1.0)

    sea_lice.step_day(farm_id, temp, [], 1.0)

    # 6) Crew daily update: progression and payroll
    if crew_manager:
        var payroll = crew_manager.daily_update(day)
        # record payroll as expense
        finance.record_expense(payroll, "payroll", "labor", int(floor(day/7))+1)

    # 7) Weekly tasks
    if day % 7 == 0:
        market.advance_week()
        var week = int(day / 7)
        finance.process_weekly_loans(week)

    # 8) Auto-harvest if average weight >= 4.5 kg
    var total_fish = 0
    for p in pens:
        total_fish += int(p["current_fish_count"])
    var avg_weight = 0.0
    if total_fish > 0:
        avg_weight = sea_site.total_biomass_kg / float(total_fish)
    if avg_weight >= 4.5 and sea_site.total_biomass_kg > 0.0:
        var harvested_kg = sea_site.harvest_by_size(4.5, 999.0, [])
        if harvested_kg > 0.0:
            # sell harvested fish via market
            var weeknum = int(floor(day / 7)) + 1
            var price = market.price_for_sale(weeknum, avg_weight, "ORDINARY", "EU", "HOG")
            finance.record_sale(harvested_kg, price, "EU", "HOG", "ORDINARY", weeknum)
            finance.record_production_costs(harvested_kg, 60.0, weeknum)

    # 8) Compliance checks
    var lice_status = sea_lice.get_farm_lice_status(farm_id)
    var avg_lice = lice_status.get("adult_female_per_fish", 0.0) if lice_status else 0.0
    var compliance = license.check_compliance(sea_site.total_biomass_kg, avg_lice, sea_site.is_in_fallow, sea_site.fallow_end_date)
    # if lice exceed licensed limit, enforce sea lice treatments
    if avg_lice > license.lice_limit_per_fish:
        var res = sea_lice.enforce_limit(farm_id, time_system.get_day_of_year())
        if res.has("cost"):
            finance.record_expense(res["cost"], "lice_treatment", "lice_treatment", int(floor(day/7))+1)
        # apply mortality fraction to site biomass
        if res.has("fish_mortality_fraction"):
            var frac = float(res["fish_mortality_fraction"])
            # approximate dead kg and report
            for p in pens:
                var dead_count = int(float(p["current_fish_count"]) * frac)
                if dead_count > 0:
                    sea_site.report_mortality(p["id"], dead_count, float(p["average_weight_kg"]))

    # Emit updated state
    emit_signal("state_updated", to_dict())


func _stock_smolts(smolts: Array) -> void:
    if smolts.size() == 0:
        return
    # compute average weight
    var sum_w = 0.0
    for s in smolts:
        sum_w += float(s.get("weight_kg", 0.01))
    var avg_w = sum_w / float(smolts.size())
    var total = smolts.size()
    # distribute evenly across pens
    var pens = sea_site.pens
    var per_pen = int(floor(total / max(1, pens.size())))
    var remaining = total - per_pen * pens.size()
    for i in range(pens.size()):
        var count = per_pen + (1 if i < remaining else 0)
        if count <= 0:
            continue
        sea_site.stock_fish(pens[i]["id"], count, avg_w, str(time_system.day))
        # inform disease/sea lice systems of biomass change
    # update disease biomass metric
    disease.set_farm_biomass(farm_id, sea_site.total_biomass_kg / 1000.0)


# Event handlers
func _on_algal_bloom(b_zone_id: int) -> void:
    if int(b_zone_id) != zone_id:
        return
    # cause 1-3% mortality in each pen
    for p in sea_site.pens:
        var frac = randf_range(0.01, 0.03)
        var dead = int(float(p["current_fish_count"]) * frac)
        if dead > 0:
            sea_site.report_mortality(p["id"], dead, float(p["average_weight_kg"]))

func _on_storm(b_zone_id: int) -> void:
    if int(b_zone_id) != zone_id:
        return
    # storms cause small mortality and possible escapes (~0.5-2%)
    for p in sea_site.pens:
        var frac = randf_range(0.005, 0.02)
        var dead = int(float(p["current_fish_count"]) * frac)
        if dead > 0:
            sea_site.report_mortality(p["id"], dead, float(p["average_weight_kg"]))

func _on_jellyfish(b_zone_id: int) -> void:
    if int(b_zone_id) != zone_id:
        return
    # jellyfish bloom: modest mortality; 0.5-1.5%
    for p in sea_site.pens:
        var frac = randf_range(0.005, 0.015)
        var dead = int(float(p["current_fish_count"]) * frac)
        if dead > 0:
            sea_site.report_mortality(p["id"], dead, float(p["average_weight_kg"]))


func _on_payroll_processed(total_cost: float) -> void:
    # payroll events currently handled directly in daily_update; keep hook for logging or UI
    return

func to_dict() -> Dictionary:
    var state = {
        "day": time_system.day,
        "zone_id": zone_id,
        "environment": {
            "temperature": environment.get_temperature(zone_id),
            "oxygen": environment.get_oxygen(zone_id)
        },
        "sea_site": sea_site.report_status(),
        "hatchery": hatchery.to_dict(),
        "finance": finance.get_balance_sheet_snapshot(),
        "market": {"active_contracts": market.active_contracts},
        "disease": disease.get_farm_status(farm_id),
        "sea_lice": sea_lice.get_farm_lice_status(farm_id),
        "license": license.to_dict()
    }
    # attach crew report if available
    if crew_manager:
        state["crew"] = crew_manager.report()
    return state
