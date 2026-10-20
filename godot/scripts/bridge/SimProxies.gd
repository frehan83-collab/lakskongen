class_name SimProxies
extends Node

## SimProxies — thin typed getters delegating to the C# GameBridge.
## Owned by the SimBridge facade, which forwards every method unchanged.
## No signals (pure queries; farm-system mocks preserved from SimBridge).

const Formatters = preload("res://scripts/bridge/SimFormatters.gd")

var game_bridge: Node = null  ## C# GameBridge node, injected by the facade.

func _arr(raw: Variant) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	if raw != null:
		for entry in raw:
			result.append(entry)
	return result
func _farr(raw: Variant) -> Array[float]:
	var result: Array[float] = []
	if raw != null:
		for v in raw:
			result.append(v)
	return result
func _sarr(raw: Variant) -> Array[String]:
	var result: Array[String] = []
	if raw != null:
		for alert in raw:
			result.append(alert)
	return result

# ── Core fish / site ──────────────────────────────────────────────────────────

func get_fish_count_by_stage(stage_name: String) -> int: return game_bridge.GetFishCountByStage(stage_name) if game_bridge else 0
func get_biomass_by_stage(stage_name: String) -> float: return game_bridge.GetBiomassByStage(stage_name) if game_bridge else 0.0
func owns_hatchery() -> bool: return game_bridge.OwnsHatchery if game_bridge else false
func owns_processing_plant() -> bool: return game_bridge.OwnsProcessingPlant if game_bridge else false
func processing_tier() -> String: return str(game_bridge.ProcessingTier) if game_bridge else ""
func buy_processing_plant(tier: String) -> bool:
	if game_bridge and game_bridge.has_method("BuyProcessingPlant"):
		return game_bridge.BuyProcessingPlant(tier)
	return false
func get_export_market_count() -> int: return game_bridge.ExportMarketCount if game_bridge else 0
func get_processing_throughput_kg() -> float: return game_bridge.ProcessingThroughputKg if game_bridge else 0.0
func get_active_contracts() -> Array[Dictionary]: return _arr(game_bridge.GetActiveContracts()) if game_bridge else []
func get_available_offers() -> Array[Dictionary]: return _arr(game_bridge.GetAvailableOffers()) if game_bridge else []
func accept_offer(index: int) -> String:
	if game_bridge and game_bridge.has_method("AcceptOffer"):
		return str(game_bridge.AcceptOffer(index))
	return ""
func get_export_markets() -> Array[Dictionary]: return _arr(game_bridge.GetExportMarkets()) if game_bridge else []

# ── Analytics ─────────────────────────────────────────────────────────────────

## Data export (DataExportPanel): CSV files; rows written or -1.
func export_daily_snapshots(path: String) -> int: return game_bridge.ExportDailySnapshots(path) if game_bridge else -1
func export_financial_report(path: String) -> int: return game_bridge.ExportFinancialReport(path) if game_bridge else -1
func export_population_data(path: String) -> int: return game_bridge.ExportPopulationData(path) if game_bridge else -1
func export_event_history(path: String) -> int: return game_bridge.ExportEventHistory(path) if game_bridge else -1

func get_analytics_data(metric: String, days: int = 30) -> Array[float]: return _farr(game_bridge.GetAnalyticsData(metric, days)) if game_bridge else []
func get_analytics_moving_average(metric: String, days: int = 30, window_days: int = 7) -> Array[float]: return _farr(game_bridge.GetAnalyticsMovingAverage(metric, days, window_days)) if game_bridge else []
func get_analytics_trend(metric: String, days: int = 14) -> String: return game_bridge.GetAnalyticsTrend(metric, days) if game_bridge else "stable"
func get_analytics_all_time_high(metric: String) -> float: return game_bridge.GetAnalyticsAllTimeHigh(metric) if game_bridge else 0.0
func get_analytics_all_time_low(metric: String) -> float: return game_bridge.GetAnalyticsAllTimeLow(metric) if game_bridge else 0.0

# ── Research / scenarios ──────────────────────────────────────────────────────

func get_research_nodes() -> Array[Dictionary]: return _arr(game_bridge.GetResearchNodes()) if game_bridge else []
func start_research(node_id: String) -> bool: return game_bridge.StartResearch(node_id) if game_bridge else false
func cancel_research() -> void:
	if game_bridge:
		game_bridge.CancelResearch()
func is_research_unlocked(node_id: String) -> bool: return game_bridge.IsResearchUnlocked(node_id) if game_bridge else false
func get_research_effect(effect_key: String) -> float: return game_bridge.GetResearchEffect(effect_key) if game_bridge else 0.0
func get_active_research_id() -> String: return game_bridge.ActiveResearchId if game_bridge else ""
func get_research_progress() -> int: return game_bridge.ResearchProgress if game_bridge else 0
func get_research_days_remaining() -> int: return game_bridge.ResearchDaysRemaining if game_bridge else 0
func get_available_scenarios() -> Array[Dictionary]: return _arr(game_bridge.GetAvailableScenarios()) if game_bridge else []
func get_active_scenario_id() -> String: return game_bridge.GetActiveScenarioId() if game_bridge else ""
## Goal progress of the active scenario from the engine's win check (F949).
func get_scenario_goal() -> Dictionary: return game_bridge.GetScenarioGoal() if game_bridge else {}

# ── Finance / sustainability ──────────────────────────────────────────────────

func get_sustainability_breakdown() -> Dictionary: return game_bridge.GetSustainabilityBreakdown() if game_bridge else {}
func get_certification_level() -> String: return game_bridge.GetCertificationLevel() if game_bridge else "NonCompliant"
func get_price_multiplier() -> float: return game_bridge.GetPriceMultiplier() if game_bridge else 1.0
func get_revenue() -> float: return game_bridge.Revenue if game_bridge else 0.0
func get_expenses() -> float: return game_bridge.Expenses if game_bridge else 0.0
func get_profit() -> float: return game_bridge.Profit if game_bridge else 0.0

# ── Genetics / crew / achievements / weather ──────────────────────────────────

func get_breeding_candidates() -> Array[Dictionary]: return _arr(game_bridge.GetBreedingCandidates()) if game_bridge else []
func get_genetic_traits() -> Array[Dictionary]: return _arr(game_bridge.GetGeneticTraits()) if game_bridge else []
func get_inbreeding_coefficient() -> float: return game_bridge.GetInbreedingCoefficient() if game_bridge else 0.0

## Wave 6: genomic-selection preview, inbreeding status, and GE traits.
func get_genomic_selection_preview() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetGenomicSelectionPreview"):
		return _arr(game_bridge.GetGenomicSelectionPreview())
	return []

func get_inbreeding_status() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetInbreedingStatus"):
		return game_bridge.GetInbreedingStatus()
	return {"coefficient": 0.0, "penalty": 0.0, "penalty_percent": 0.0, "level": "none"}

func get_ge_traits() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetGeTraits"):
		return _arr(game_bridge.GetGeTraits())
	return []


func get_crew_list() -> Array[Dictionary]: return _arr(game_bridge.GetCrewList()) if game_bridge else []
func get_crew_role_effects() -> Array[Dictionary]: return _arr(game_bridge.GetCrewRoleEffects()) if game_bridge else []
func hire_worker(role: String) -> bool: return game_bridge.HireWorker(role) if game_bridge else false
func fire_worker(worker_id: String) -> bool: return game_bridge.FireWorker(worker_id) if game_bridge else false
func get_achievement_list() -> Array[Dictionary]: return _arr(game_bridge.GetAchievementList()) if game_bridge else []
func get_current_weather_state() -> String: return game_bridge.GetCurrentWeatherState() if game_bridge else "Calm"
## FCR goal and failing ceiling from CalibrationRegistry: {"target", "poor"}.
func get_fcr_targets() -> Dictionary: return game_bridge.GetFcrTargets() if game_bridge else {}
func get_sky_context() -> Dictionary: return game_bridge.GetSkyContext() if game_bridge else {}
func get_scene_latitude() -> float: return game_bridge.GetSceneLatitude() if game_bridge else 60.39
func get_current_temperature() -> float: return float(game_bridge.GetCurrentTemperature()) if game_bridge else 0.0
func get_current_day() -> int: return int(game_bridge.GetCurrentDay()) if game_bridge else 0

## Treatment cards + resistance (Phase 2.2).
func get_treatment_profiles() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetTreatmentProfiles"):
		return _arr(game_bridge.GetTreatmentProfiles())
	return []
func get_resistance_levels() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetResistanceLevels"):
		return game_bridge.GetResistanceLevels()
	return {}

## Pending wellboat jobs (Phase 2.4).
func get_wellboat_jobs() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetWellboatJobs"):
		return _arr(game_bridge.GetWellboatJobs())
	return []

## Containment ladder table (Phase 4.1).
func get_containment_levels() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetContainmentLevels"):
		return _arr(game_bridge.GetContainmentLevels())
	return []

## ROC attention budget (Phase 4.2): {budget, used, site_count}.
func get_attention_budget() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetAttentionBudget"):
		return game_bridge.GetAttentionBudget()
	return {"budget": 100, "used": 0, "site_count": 0}

## Active blooms (Phase 4.3).
func get_active_blooms() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetActiveBlooms"):
		return _arr(game_bridge.GetActiveBlooms())
	return []

## Bloom-mitigation catalog: one entry per kind with fixed_cost, opex_at_full,
## max_reduction, best_for and installed_sites (P11 mitigation UI).
func get_bloom_mitigations() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetBloomMitigations"):
		return _arr(game_bridge.GetBloomMitigations())
	return []

## Install (or toggle off) a bloom mitigation at a site. Returns false for an
## unknown site/kind or when funds are insufficient.
func install_bloom_mitigation(site_id: String, kind: String) -> bool:
	if game_bridge and game_bridge.has_method("InstallBloomMitigation"):
		return game_bridge.InstallBloomMitigation(site_id, kind)
	return false

## U2-02: the tutorial's current step (GameBridge.GetTutorialStatus: active,
## skipped, step, total, kind, pace, target, title_key, text_key, do_key,
## unlocks, progress), the step list and the tab gate.
func get_tutorial_status() -> Dictionary:
	return game_bridge.GetTutorialStatus() if game_bridge else {}

func get_tutorial_steps() -> Array:
	return game_bridge.GetTutorialSteps() if game_bridge else []

func is_tutorial_tab_unlocked(tab_id: String) -> bool:
	return game_bridge.IsTutorialTabUnlocked(tab_id) if game_bridge else true

## Feeding plans (U2-04): a site's plan, every site's, the card's options
## and a plan's projected next days (twin-lite, never touches the game).
func get_feed_plan(site_id: String) -> Dictionary:
	return game_bridge.GetFeedPlan(site_id) if game_bridge else {}

func get_feed_plans() -> Array:
	return game_bridge.GetFeedPlans() if game_bridge else []

func get_feed_plan_options() -> Dictionary:
	return game_bridge.GetFeedPlanOptions() if game_bridge else {}

func preview_feed_plan(site_id: String, ration_pct: float, recipe: String, days: int = 0) -> Dictionary:
	return game_bridge.PreviewFeedPlan(site_id, ration_pct, recipe, days) if game_bridge else {"ok": false}

## Twin-lite forecast (Phase 4.5): PROJECTION, cached per day.
func get_forecast() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetForecast"):
		return game_bridge.GetForecast()
	return {"biomass_kg": [], "lice": [], "cash_nok": [], "confidence": "low"}

## Intervention advisor suggestions (Phase 5.3).
func get_intervention_ranking() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetInterventionRanking"):
		return _arr(game_bridge.GetInterventionRanking())
	return []

## Attention Inbox (Phase 4.5) — unified "what needs my attention" inbox.
func get_attention_items() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetAttentionItems"):
		return _arr(game_bridge.GetAttentionItems())
	return []

func open_attention_inbox() -> void:
	if game_bridge and game_bridge.has_method("OpenAttentionInbox"):
		game_bridge.OpenAttentionInbox()

## Night-shift ROC policy status (Phase 5.2).
func get_night_shift_status() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetNightShiftStatus"):
		return game_bridge.GetNightShiftStatus()
	return {"policy": "Standard", "premium_monthly": 0.0, "detection_chance": 0.7, "fatigue_per_night": 0, "misfire_chance": 0.0}

## Buyable smolt strains for the stocking dialog (Phase 1.4).
func get_smolt_strains() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetSmoltStrains"):
		return _arr(game_bridge.GetSmoltStrains())
	return []

## Post-smolt quality tiers (P5 carryover): {id, name, mortality_mult, growth_mult, days_mult, quality}.
func get_smolt_quality_tiers() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetSmoltQualityTiers"):
		return _arr(game_bridge.GetSmoltQualityTiers())
	return []

# ── Phase 3 depth exposure (P7/P9/P10) ────────────────────────────────────────

## P9: binding monthly pen price (NOK/kg) from the Price Council.
func get_pen_price() -> float: return float(game_bridge.PenPrice) if game_bridge else 0.0

## P9: resource-rent tax status: {enabled, paid}.
func get_resource_rent() -> Dictionary:
	if game_bridge:
		return {"enabled": bool(game_bridge.ResourceRentEnabled), "paid": float(game_bridge.ResourceRentPaid)}
	return {"enabled": false, "paid": 0.0}

## U2-06: the typed event bus (GameBridge.Events.cs).
func get_game_event_log(max_count: int) -> Array[Dictionary]: return _arr(game_bridge.GetGameEventLog(max_count)) if game_bridge else []
func get_game_event_catalog() -> Array[Dictionary]: return _arr(game_bridge.GetGameEventCatalog()) if game_bridge else []
func publish_condition_event(type_id: String, severity: String, site_id: String, message: String, variant: String, deadline_day: int) -> Dictionary:
	return game_bridge.PublishConditionEvent(type_id, severity, site_id, message, variant, deadline_day) if game_bridge else {"outcome": "rejected", "id": "", "reason": "not_initialized"}
func game_events_suppressed() -> int: return int(game_bridge.GameEventsSuppressed) if game_bridge else 0

## P10: gill-health early-warning risk for a site.
func get_gill_risk(site_id: String) -> Dictionary:
	if game_bridge and game_bridge.has_method("GetGillRisk"):
		return game_bridge.GetGillRisk(site_id)
	return {"site_id": site_id, "risk": 0.0, "warning": false, "critical": false, "reason": "no data"}

## P7: does a site's containment qualify for red-zone capacity recovery?
func qualifies_flexibility(site_id: String) -> bool:
	if game_bridge and game_bridge.has_method("QualifiesForEnvironmentalFlexibility"):
		return bool(game_bridge.QualifiesForEnvironmentalFlexibility(site_id))
	return false

## P11: catastrophe-insurance status. Keys: total_premium, mortality, bloom,
## storm, isa (each {enabled, premium, coverage, deductible}). Empty if unavailable.
func get_insurance_status() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetInsuranceStatus"):
		return game_bridge.GetInsuranceStatus()
	return {}

## P11: enable/disable one catastrophe policy (covered by the facade).
func set_insurance_policy(kind: String, enabled: bool, coverage: float, deductible: float) -> bool:
	if game_bridge and game_bridge.has_method("SetInsurancePolicy"):
		return game_bridge.SetInsurancePolicy(kind, enabled, coverage, deductible)
	return false

## P15: accumulated player-supply pressure on the spot price (0..1).
## Returns -1.0 when the underlying bridge does not expose it (caller skips).
func get_player_supply_pressure() -> float:
	if game_bridge and game_bridge.has_method("GetPlayerSupplyPressure"):
		return float(game_bridge.GetPlayerSupplyPressure())
	return -1.0

# ── Traffic-light zones (Phase 1.3) ──────────────────────────────────────────

## Per-zone traffic-light status: one entry per zone with
## {zone, color (Green/Yellow/Red), mab_factor, capacity_tonnes}.
func get_zone_traffic_lights() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetZoneTrafficLights"):
		return _arr(game_bridge.GetZoneTrafficLights())
	return []

# ── RAS facilities ────────────────────────────────────────────────────────────

## Get list of all RAS (Recirculating Aquaculture System) facilities
func get_ras_facilities() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetRASFacilities"):
		return _arr(game_bridge.GetRASFacilities())
	push_warning("SimProxies: GetRASFacilities unavailable — returning empty list")
	return []

## Start construction of a new RAS facility
func build_ras_facility(rname: String, scale: String) -> bool:
	if game_bridge and game_bridge.has_method("BuildRASFacility"):
		return game_bridge.BuildRASFacility(rname, scale)
	push_warning("SimProxies: BuildRASFacility unavailable — build refused")
	return false

## Stock / harvest facility fish (F383) — engine verbs; {success, message, error, value, value2, id}.
func stock_ras_facility(facility_id: String, count: int, strain_id: String = "standard") -> Dictionary:
	return game_bridge.StockRASFacility(facility_id, count, strain_id) if game_bridge else {"success": false, "message": "Not initialized"}
func harvest_ras_facility(facility_id: String) -> Dictionary:
	return game_bridge.HarvestRASFacility(facility_id) if game_bridge else {"success": false, "message": "Not initialized"}
func stock_offshore_farm(farm_id: String, count: int, strain_id: String = "standard") -> Dictionary:
	return game_bridge.StockOffshoreFarm(farm_id, count, strain_id) if game_bridge else {"success": false, "message": "Not initialized"}
func harvest_offshore_farm(farm_id: String) -> Dictionary:
	return game_bridge.HarvestOffshoreFarm(farm_id) if game_bridge else {"success": false, "message": "Not initialized"}

## Harvest pipeline (S2-04): quote, order and cancel a planned harvest of a sea
## site, land facility or offshore farm (empty pen_ids = every pen with fish;
## fraction of each pen's fish), and read the open orders and harvest reports.
func quote_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0, express: bool = false) -> Dictionary:
	return game_bridge.QuoteHarvest(holding_id, pen_ids, fraction, express) if game_bridge else {"success": false, "message": "Not initialized"}
func order_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0) -> Dictionary:
	return game_bridge.OrderHarvest(holding_id, pen_ids, fraction) if game_bridge else {"success": false, "message": "Not initialized"}
func cancel_harvest_order(order_id: String) -> Dictionary:
	return game_bridge.CancelHarvestOrder(order_id) if game_bridge else {"success": false, "message": "Not initialized"}
func get_harvest_orders() -> Array[Dictionary]:
	return _arr(game_bridge.GetHarvestOrders()) if game_bridge else ([] as Array[Dictionary])
func get_harvest_reports() -> Array[Dictionary]:
	return _arr(game_bridge.GetHarvestReports()) if game_bridge else ([] as Array[Dictionary])

## Harvest planner (U2-05): a planned harvest projected today or after a wait
## (twin-lite, no RNG moves), the planner's options, an express harvest of the
## chosen pens, and the ledger's totals by category (the report's check).
func project_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0, wait_days: int = 0) -> Dictionary:
	return game_bridge.ProjectHarvest(holding_id, pen_ids, fraction, wait_days) if game_bridge else {"success": false, "message": "Not initialized"}
func get_harvest_planner_options() -> Dictionary:
	return game_bridge.GetHarvestPlannerOptions() if game_bridge else {}
func harvest_express(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0) -> Dictionary:
	return game_bridge.HarvestExpress(holding_id, pen_ids, fraction) if game_bridge else {"success": false, "message": "Not initialized"}
func get_ledger_totals() -> Dictionary:
	return game_bridge.GetLedgerTotals() if game_bridge else {"revenue": {}, "cost": {}}

## Perform maintenance on RAS facility, returns cost
func ras_maintenance(facility_id: String) -> float:
	if game_bridge and game_bridge.has_method("PerformRASMaintenance"):
		return game_bridge.PerformRASMaintenance(facility_id)
	push_warning("SimProxies: PerformRASMaintenance unavailable — returning 0")
	return 0.0

# ── Offshore farms ────────────────────────────────────────────────────────────

## Get list of all offshore farm installations
func get_offshore_farms() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetOffshoreFarms"):
		return _arr(game_bridge.GetOffshoreFarms())
	push_warning("SimProxies: GetOffshoreFarms unavailable — returning empty list")
	return []

## Start construction of offshore farm
func build_offshore_farm(offname: String, farm_type: String) -> bool:
	if game_bridge and game_bridge.has_method("BuildOffshoreFarm"):
		return game_bridge.BuildOffshoreFarm(offname, farm_type)
	push_warning("SimProxies: BuildOffshoreFarm unavailable — build refused")
	return false

## Repair offshore structure, returns cost
func repair_offshore(farm_id: String) -> float:
	if game_bridge and game_bridge.has_method("RepairOffshore"):
		return game_bridge.RepairOffshore(farm_id)
	push_warning("SimProxies: RepairOffshore unavailable — returning 0")
	return 0.0

# ── AI feeding / post-smolt ───────────────────────────────────────────────────

## Get AI feeding system status across all farms
func get_ai_feeding_status() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetAIFeedingStatus"):
		return game_bridge.GetAIFeedingStatus()
	push_warning("SimProxies: GetAIFeedingStatus unavailable — returning empty status")
	return {"is_installed": false}

## Install AI feeding system (one-time cost)
## Build / install prices for the facility buttons (GameBridge.GetBuildCosts).
func get_build_costs() -> Dictionary:
	return game_bridge.GetBuildCosts() if game_bridge else {}

func install_ai_feeding() -> float:
	if game_bridge and game_bridge.has_method("InstallAIFeeding"):
		return game_bridge.InstallAIFeeding()
	push_warning("SimProxies: InstallAIFeeding unavailable — returning 0")
	return 0.0

## Clean AI camera sensors, returns maintenance cost
func clean_ai_cameras() -> float:
	if game_bridge and game_bridge.has_method("CleanAICameras"):
		return game_bridge.CleanAICameras()
	push_warning("SimProxies: CleanAICameras unavailable — returning 0")
	return 0.0

## Get list of all post-smolt facilities
func get_post_smolt_facilities() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetPostSmoltFacilities"):
		return _arr(game_bridge.GetPostSmoltFacilities())
	push_warning("SimProxies: GetPostSmoltFacilities unavailable — returning empty list")
	return []

## Start a new post-smolt batch
func start_smolt_batch(facility_id: String, count: int) -> bool:
	if game_bridge and game_bridge.has_method("StartSmoltBatch"):
		return game_bridge.StartSmoltBatch(facility_id, count)
	push_warning("SimProxies: StartSmoltBatch unavailable — batch refused")
	return false

## Vaccinate a smolt batch, returns cost
func vaccinate_batch(batch_id: String) -> float:
	if game_bridge and game_bridge.has_method("VaccinateBatch"):
		return game_bridge.VaccinateBatch(batch_id)
	push_warning("SimProxies: VaccinateBatch unavailable — returning 0")
	return 0.0

## Transfer post-smolt batch to sea cages
func transfer_batch_to_sea(batch_id: String) -> bool:
	if game_bridge and game_bridge.has_method("TransferBatchToSea"):
		return game_bridge.TransferBatchToSea(batch_id)
	push_warning("SimProxies: TransferBatchToSea unavailable — transfer refused")
	return false

# ── Empire: competitors + progression ─────────────────────────────────────────

func get_competitors() -> Array[Dictionary]: return _arr(game_bridge.GetCompetitors()) if game_bridge else []
func get_competitor_count() -> int: return game_bridge.GetCompetitorCount() if game_bridge else 0
## AI competitor farms for the grey 3D silhouettes: id, name, market_share,
## strategy, position_x/position_z (the bridge's fixed spots; the 3D view
## uses the fjord's channel slots instead, these lie inside the fjord walls).
func get_competitor_farms() -> Array[Dictionary]: return _arr(game_bridge.GetCompetitorCompanies()) if game_bridge else []
func get_market_leader() -> Dictionary: return game_bridge.GetMarketLeader() if game_bridge else {}
func get_empire_phase() -> String: return game_bridge.GetEmpirePhase() if game_bridge else "SmallFarmer"
func get_empire_score() -> int: return game_bridge.GetEmpireScore() if game_bridge else 0
func get_next_milestone() -> Dictionary: return game_bridge.GetNextMilestone() if game_bridge else {}
func get_growth_strategy() -> String: return game_bridge.GetGrowthStrategy() if game_bridge else "Organic"

# ── Empire: market + brand ────────────────────────────────────────────────────

func get_ownership_status() -> String: return game_bridge.GetOwnershipStatus() if game_bridge else "Private"
func get_share_price() -> float: return game_bridge.GetSharePrice() if game_bridge else 0.0
func get_market_cap() -> float: return game_bridge.GetMarketCap() if game_bridge else 0.0
func check_is_public() -> bool: return game_bridge.IsPublic() if game_bridge else false
func attempt_ipo() -> Dictionary: return game_bridge.AttemptIPO() if game_bridge else {"success": false, "message": "Not initialized"}
func get_brand_score() -> float: return game_bridge.GetBrandScore() if game_bridge else 50.0
func get_brand_strategy() -> String: return game_bridge.GetBrandStrategy() if game_bridge else "Commodity"
func get_certifications() -> Array[String]: return _sarr(game_bridge.GetCertifications()) if game_bridge else []
func get_pricing_power() -> float: return game_bridge.GetPricingPower() if game_bridge else 1.0

# ── Empire: crisis + integration ──────────────────────────────────────────────

func get_active_crises() -> Array[Dictionary]: return _arr(game_bridge.GetActiveCrises()) if game_bridge else []
func get_acquisition_opportunities() -> Array[Dictionary]: return _arr(game_bridge.GetAcquisitionOpportunities()) if game_bridge else []
func attempt_acquisition(asset_id: String) -> Dictionary: return game_bridge.AttemptAcquisition(asset_id) if game_bridge else {"success": false, "message": "Not initialized"}
func get_integration_tiers() -> Array[Dictionary]: return _arr(game_bridge.GetIntegrationTiers()) if game_bridge else []
func get_owned_assets() -> Array[Dictionary]: return _arr(game_bridge.GetOwnedAssets()) if game_bridge else []
func get_integration_score() -> float: return game_bridge.GetIntegrationScore() if game_bridge else 0.0
func purchase_asset(asset_id: String) -> Dictionary: return game_bridge.PurchaseAsset(asset_id) if game_bridge else {"success": false, "message": "Not initialized"}

# ── Wave 2: loans + tax + investors ────────────────────────────────────────────

func get_active_loans() -> Array[Dictionary]: return _arr(game_bridge.GetActiveLoans()) if game_bridge else []
func get_total_debt() -> float: return game_bridge.GetTotalDebt() if game_bridge else 0.0
func get_monthly_payment() -> float: return game_bridge.GetMonthlyPayment() if game_bridge else 0.0
func take_loan(amount: float, term_months: int, rate: float) -> Dictionary: return game_bridge.TakeLoan(amount, term_months, rate) if game_bridge else {"success": false, "message": "Not initialized"}
func get_loan_info() -> Dictionary: return game_bridge.GetLoanInfo() if game_bridge else {"has_loan": false}
func repay_loan(amount: float) -> Dictionary: return game_bridge.RepayLoan(amount) if game_bridge else {"success": false, "message": "Not initialized"}
func get_tax_rate() -> float: return game_bridge.GetTaxRate() if game_bridge else 0.22
func get_last_tax_paid() -> float: return game_bridge.GetLastTaxPaid() if game_bridge else 0.0
func get_annual_tax_burden() -> float: return game_bridge.GetAnnualTaxBurden() if game_bridge else 0.0
func get_investors() -> Array[Dictionary]: return _arr(game_bridge.GetInvestors()) if game_bridge else []
func get_total_investment() -> float: return game_bridge.GetTotalInvestment() if game_bridge else 0.0
func get_equity_dilution() -> float: return game_bridge.GetEquityDilution() if game_bridge else 0.0

# ── Wave 2: acquisitions + licenses + factions ─────────────────────────────────

func get_available_targets() -> Array[Dictionary]: return _arr(game_bridge.GetAvailableTargets()) if game_bridge else []
func get_owned_acquisitions() -> Array[Dictionary]: return _arr(game_bridge.GetOwnedAcquisitions()) if game_bridge else []
func attempt_acquisition_target(target_id: String) -> Dictionary: return game_bridge.AttemptAcquisitionTarget(target_id) if game_bridge else {"success": false, "message": "Not initialized"}
func get_active_licenses() -> Array[Dictionary]: return _arr(game_bridge.GetActiveLicenses()) if game_bridge else []
func get_license_cost() -> float: return game_bridge.GetLicenseCost() if game_bridge else 150_000_000.0
func apply_for_license(zone_id: String) -> Dictionary: return game_bridge.ApplyForLicense(zone_id) if game_bridge else {"success": false, "message": "Not initialized"}
func place_license_bid(zone_id: String, amount_nok: float) -> Dictionary: return game_bridge.PlaceLicenseBid(zone_id, amount_nok) if game_bridge else {"success": false, "message": "Not initialized"}
func get_license_auctions() -> Array[Dictionary]: return _arr(game_bridge.GetLicenseAuctions()) if game_bridge else []
func has_growth_license(zone_id: String) -> bool: return game_bridge.HasGrowthLicense(zone_id) if game_bridge else false
func can_build_sea_site(zone_id: String) -> Dictionary: return game_bridge.CanBuildSeaSite(zone_id) if game_bridge else {"allowed": false, "reason": "Not initialized"}

# ── Phase 4.2: Distressed asset acquisition ────────────────────────────────────────

func get_distressed_assets() -> Array[Dictionary]: return _arr(game_bridge.GetDistressedAssets()) if game_bridge else []
func acquire_distressed_asset(asset_id: String) -> Dictionary: return game_bridge.AcquireDistressedAsset(asset_id) if game_bridge else {"success": false, "message": "Not initialized"}

func get_factions() -> Array[Dictionary]: return _arr(game_bridge.GetFactions()) if game_bridge else []
func get_faction_relation(faction_id: String) -> float: return game_bridge.GetFactionRelation(faction_id) if game_bridge else 0.0
func get_active_demands() -> Array[Dictionary]: return _arr(game_bridge.GetActiveDemands()) if game_bridge else []

# ── Wave 2: market intel + welfare + milestones ────────────────────────────────

func get_market_report() -> Dictionary: return game_bridge.GetMarketReport() if game_bridge else {}
func get_profit_loss_story(top_n: int = 3) -> Dictionary: return game_bridge.GetProfitLossStory(top_n) if game_bridge else {}
## S2-05: deaths by cause in the NVI categories plus the fish that left alive (site, last N days, or company).
func get_mortality_breakdown(site_id: String = "", last_days: int = 0) -> Dictionary: return game_bridge.GetMortalityBreakdown(site_id, last_days) if game_bridge else {"success": false, "message": "Not initialized", "categories": [], "causes": [], "left_alive": []}
## U2-03: the end screen's report (verdict, figures, the P&L story's months and turning points, medal, event log).
func get_scenario_end_report() -> Dictionary: return game_bridge.GetScenarioEndReport() if game_bridge else {"success": false, "verdict": "running"}
## U2-03: the verdict as it stands: "running", "won", "lost" or "bankrupt".
func get_current_verdict() -> String: return String(game_bridge.CurrentVerdict) if game_bridge else "running"
## U2-03: the bank's reading of the cash (burn, runway, warning, bank call) and its rescue offer.
func get_liquidity_status() -> Dictionary: return game_bridge.GetLiquidityStatus() if game_bridge else {"success": false, "offer": {}}
## S2-01a: licences and MTB per production area and site, headroom, the plan's peak and the biomass orders (U2-01's expansion flow).
func get_capacity_overview() -> Dictionary: return game_bridge.GetCapacityOverview() if game_bridge else {"success": false, "message": "Not initialized", "areas": [], "sites": [], "licences": [], "breaches": []}
## S2-01a: what stocking would do to the caps (the check stock_smolts runs) and the most smolts that fit.
func preview_stocking(site_id: String, count: int, strain_id: String = "standard") -> Dictionary: return game_bridge.PreviewStocking(site_id, count, strain_id) if game_bridge else {"success": false, "allowed": false, "message": "Not initialized"}
## S2-01b: a site's real pens in mooring order (number, occupancy, fish, density, net).
func get_site_pens(site_id: String) -> Array[Dictionary]: return _arr(game_bridge.GetSitePens(site_id)) if game_bridge else ([] as Array[Dictionary])
## S2-01b: what stocking a count of smolts in one pen would do (§ 47a fish, § 25 projected density, MTB) and the most it takes.
func preview_pen_stocking(site_id: String, pen_id: String, count: int, strain_id: String = "standard", quality_tier: int = 0) -> Dictionary: return game_bridge.PreviewPenStocking(site_id, pen_id, count, strain_id, quality_tier) if game_bridge else {"success": false, "allowed": false, "message": "Not initialized"}
## S2-01a: what building a site in a zone costs and adds (a locality, 0 t of licence MTB).
func preview_build_sea_site(zone_id: String) -> Dictionary: return game_bridge.PreviewBuildSeaSite(zone_id) if game_bridge else {"success": false, "allowed": false, "reason": "Not initialized"}
## U2-01: the expansion map (13 production areas, their zones and the build and licence verdicts as reason keys).
func get_expansion_overview() -> Dictionary: return game_bridge.GetExpansionOverview() if game_bridge else {"success": false, "reason_key": "engine_not_ready", "areas": []}
## U2-01: what a site build in a zone costs, holds and adds (the BuildSiteDialog's preview).
func plan_site_build(zone_id: String) -> Dictionary: return game_bridge.PlanSiteBuild(zone_id) if game_bridge else {"success": false, "allowed": false, "reason_key": "engine_not_ready"}
## U2-01: what applying for a growth licence in a zone opens (reserve, MTB, the auction's length).
func preview_licence_application(zone_id: String) -> Dictionary: return game_bridge.PreviewLicenceApplication(zone_id) if game_bridge else {"success": false, "allowed": false, "reason_key": "engine_not_ready"}
## U2-01: a zone's licence auction for the auction card (bids newest first, the minimum bid, days left).
func get_licence_auction(zone_id: String) -> Dictionary: return game_bridge.GetLicenceAuction(zone_id) if game_bridge else {"success": false, "zone_id": zone_id}
func get_competitor_analysis() -> Array[Dictionary]: return _arr(game_bridge.GetCompetitorAnalysis()) if game_bridge else []
func get_price_forecast() -> Array[Dictionary]: return _arr(game_bridge.GetPriceForecast()) if game_bridge else []
func get_welfare_score() -> float: return game_bridge.GetWelfareScore() if game_bridge else 50.0
func get_welfare_breakdown() -> Dictionary: return game_bridge.GetWelfareBreakdown() if game_bridge else {}
func get_welfare_alerts() -> Array[String]: return _sarr(game_bridge.GetWelfareAlerts()) if game_bridge else []
func get_achieved_milestones() -> Array[Dictionary]: return _arr(game_bridge.GetAchievedMilestones()) if game_bridge else []
func get_next_milestone_info() -> Dictionary: return game_bridge.GetNextMilestone() if game_bridge else {}
func get_milestone_progress() -> float: return game_bridge.GetMilestoneProgress() if game_bridge else 0.0

# ── Phase 5.3: Sludge Valorization ──────────────────────────────────────────────

## Get sludge status for all sea sites
func get_sludge_status() -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetSludgeStatus"):
		return _arr(game_bridge.GetSludgeStatus())
	push_warning("SimProxies: GetSludgeStatus unavailable — returning empty list")
	return []

## Toggle centralized sludge hub connection for a site
func toggle_sludge_hub(site_id: String) -> bool:
	if game_bridge and game_bridge.has_method("ToggleSludgeHub"):
		return game_bridge.ToggleSludgeHub(site_id)
	push_warning("SimProxies: ToggleSludgeHub unavailable — returning false")
	return false

## Manually trigger sludge valorization at a site
func valorize_sludge_now(site_id: String) -> float:
	if game_bridge and game_bridge.has_method("ValorizeSludgeNow"):
		return game_bridge.ValorizeSludgeNow(site_id)
	push_warning("SimProxies: ValorizeSludgeNow unavailable — returning 0")
	return 0.0

# ── Phase 5.3: Cohort Transfers ────────────────────────────────────────────────

## Plan a cohort transfer between sites (cost, mortality, ETA)
func plan_cohort_transfer(source_site_id: String, dest_site_id: String, fish_count: int, mean_weight_kg: float, reason: String) -> Dictionary:
	if game_bridge and game_bridge.has_method("PlanCohortTransfer"):
		return game_bridge.PlanCohortTransfer(source_site_id, dest_site_id, fish_count, mean_weight_kg, reason)
	push_warning("SimProxies: PlanCohortTransfer unavailable — returning error")
	return {"error": "Not initialized"}

## Execute a planned cohort transfer
func execute_cohort_transfer(source_site_id: String, dest_site_id: String, fish_count: int, mean_weight_kg: float, reason: String) -> Dictionary:
	if game_bridge and game_bridge.has_method("ExecuteCohortTransfer"):
		return game_bridge.ExecuteCohortTransfer(source_site_id, dest_site_id, fish_count, mean_weight_kg, reason)
	push_warning("SimProxies: ExecuteCohortTransfer unavailable — returning error")
	return {"success": false, "message": "Not initialized"}

## Get valid destination sites for a transfer from source
func get_transfer_destinations(source_site_id: String, incoming_biomass_kg: float) -> Array[Dictionary]:
	if game_bridge and game_bridge.has_method("GetTransferDestinations"):
		return _arr(game_bridge.GetTransferDestinations(source_site_id, incoming_biomass_kg))
	push_warning("SimProxies: GetTransferDestinations unavailable — returning empty list")
	return []

# ── Phase 5.4: Carbon Ledger ────────────────────────────────────────────────────

## Get aggregated carbon ledger for all sites and facilities
func get_carbon_ledger() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetCarbonLedger"):
		return game_bridge.GetCarbonLedger()
	push_warning("SimProxies: GetCarbonLedger unavailable — returning empty dict")
	return {}

## Get carbon intensity per kg harvested (kg CO2e/kg)
func get_carbon_intensity() -> float:
	if game_bridge and game_bridge.has_method("GetCarbonIntensity"):
		return game_bridge.GetCarbonIntensity()
	push_warning("SimProxies: GetCarbonIntensity unavailable — returning 0")
	return 0.0

## Check if carbon-neutral certification threshold met
func is_carbon_neutral_year() -> bool:
	if game_bridge and game_bridge.has_method("IsCarbonNeutralYear"):
		return game_bridge.IsCarbonNeutralYear()
	push_warning("SimProxies: IsCarbonNeutralYear unavailable — returning false")
	return false

# ── F2-04: the farm-type catalogue (sim·core) ─────────────────────────────────
## The three families and seven variants with their reference values, source
## keys and unlock gates (GameBridge.FarmTypes.cs). The UI holds no farm-type
## number of its own: it reads them here.
func get_farm_type_catalog() -> Dictionary:
	return game_bridge.GetFarmTypeCatalog() if game_bridge else {"axes": [], "families": []}

# ── F2-05: the Driftsplan and the notification meter (sim·core) ──────────────
## The policies with their settings (range, default, unit, the default's source).
func get_driftsplan_catalog() -> Array[Dictionary]:
	return _arr(game_bridge.GetDriftsplanCatalog()) if game_bridge else []
## A unit's standing orders, each with its next job.
func get_driftsplan(unit_id: String) -> Array[Dictionary]:
	return _arr(game_bridge.GetDriftsplan(unit_id)) if game_bridge else []
## Set or change a standing order: {success, error, message}.
func set_driftsplan(unit_id: String, policy_kind: String, settings: Dictionary) -> Dictionary:
	return game_bridge.SetDriftsplan(unit_id, policy_kind, settings) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## End a standing order: {success, error, message}.
func clear_driftsplan(unit_id: String, policy_kind: String) -> Dictionary:
	return game_bridge.ClearDriftsplan(unit_id, policy_kind) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## "Neste planlagt" for a unit ({} when nothing is planned).
func get_next_planned(unit_id: String) -> Dictionary:
	return game_bridge.GetNextPlanned(unit_id) if game_bridge else {}
## Warnings and criticals per unit and month (spec §6.6).
func get_notification_meter() -> Array[Dictionary]:
	return _arr(game_bridge.GetNotificationMeter()) if game_bridge else []
## The notification budget the meter is measured against.
func get_notification_budget() -> Dictionary:
	return game_bridge.GetNotificationBudget() if game_bridge else {}

# ── F2-31: the family-first build flow (ui·farm) ─────────────────────────────
## The build preview of a farm-type variant on a plot: the lane's own preview
## plus the money (capex, capacity cost with the licence, first harvest,
## payback year, 36-month cash runway, the bank's loan). A read.
func preview_farm_build(variant_id: String, plot_id: String, option: String, contract: String, equity_share: float, mitigations: Array) -> Dictionary:
	return game_bridge.PreviewFarmBuild(variant_id, plot_id, option, contract, equity_share, mitigations) if game_bridge else {"ok": false, "reason_key": "engine_not_ready"}
## The diagnosis strip: the binding constraint and every other one.
func get_build_diagnosis() -> Dictionary:
	return game_bridge.GetBuildDiagnosis() if game_bridge else {"binding": {}, "constraints": []}
## The site lens's plots of a family ("land" pads, "hav" slots).
func get_build_plots(family_id: String) -> Array[Dictionary]:
	return _arr(game_bridge.GetBuildPlots(family_id)) if game_bridge else []
## Which variants the player may start now (variant id → startable, waived gates).
func get_farm_type_starts() -> Dictionary:
	return game_bridge.GetFarmTypeStarts() if game_bridge else {}
## QA-only: a digest of the whole engine state (equal digests: nothing changed).
func qa_state_digest() -> String:
	return game_bridge.QaStateDigest() if game_bridge else ""
## QA-only: research a node at once, commission a post-smolt module on a pad, count a first harvest.
func qa_open_farm_family(research_node_id: String, post_smolt_plot_id: String, first_harvest: bool) -> bool:
	return game_bridge.QaOpenFarmFamily(research_node_id, post_smolt_plot_id, first_harvest) if game_bridge else false

# ── F2-03: farm types (land sites, offshore units, their plots) ───────────────
# Two lane blocks (FARM_TYPES_SPEC §7.6): the land lane (F2-12 … F2-16) appends
# only inside the land block, the sea lane (F2-06 … F2-11) only inside the sea
# block; each forwarder goes into the matching block of SimBridge.gd.

# ═══ BEGIN sim·land lane block (F2-03 list verbs; F2-12 … F2-16 append below) ═══
## The land sites, grow-out and post-smolt (GameBridge.LandSites.cs).
func get_land_sites() -> Array[Dictionary]:
	return _arr(game_bridge.GetLandSites()) if game_bridge else []
## The land pads, free or held.
func get_land_plots() -> Array[Dictionary]:
	return _arr(game_bridge.GetLandPlots()) if game_bridge else []
# F2-12 / F2-14: lots and tanks, the build preview, permits, grid, construction.
## One land site in full: its Vannstrategi, lots, tanks and their water.
func get_land_site_detail(site_id: String) -> Dictionary:
	return game_bridge.GetLandSiteDetail(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## The preview of a new land site on a pad (budget, payments with p50/p90, schedule, first harvest, permit timeline).
func preview_land_build(plot_id: String, template: String, contract: String, mitigations: Array) -> Dictionary:
	return game_bridge.PreviewLandBuild(plot_id, template, contract, mitigations) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## The preview of expanding a land site (pilot to full lot, or the next lot or module).
func preview_land_expansion(site_id: String, contract: String) -> Dictionary:
	return game_bridge.PreviewLandExpansion(site_id, contract) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Apply for every permit a land plan on a pad still needs.
func apply_land_permits(plot_id: String, template: String, lots: int, mitigations: Array) -> Dictionary:
	return game_bridge.ApplyLandPermits(plot_id, template, lots, mitigations) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Apply for the zoning of a pad alone.
func apply_for_zoning(plot_id: String, mitigations: Array) -> Dictionary:
	return game_bridge.ApplyForZoning(plot_id, mitigations) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Appeal a refused land permit case.
func appeal_land_permit(case_id: String) -> Dictionary:
	return game_bridge.AppealLandPermit(case_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Apply again for a refused land permit with more mitigations.
func reapply_land_permit(case_id: String, mitigations: Array) -> Dictionary:
	return game_bridge.ReapplyLandPermit(case_id, mitigations) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Queue a grid project at a pad ("capacity" or "second_feed").
func start_grid_project(plot_id: String, kind: String) -> Dictionary:
	return game_bridge.StartGridProject(plot_id, kind) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Start building a land site on a pad.
func build_land_site(plot_id: String, template: String, contract: String, site_name: String) -> Dictionary:
	return game_bridge.BuildLandSite(plot_id, template, contract, site_name) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Expand a land site.
func expand_land_site(site_id: String, contract: String) -> Dictionary:
	return game_bridge.ExpandLandSite(site_id, contract) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Accelerate a land construction project's current phase.
func accelerate_land_construction(project_id: String) -> Dictionary:
	return game_bridge.AccelerateLandConstruction(project_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Cancel a land construction project.
func cancel_land_construction(project_id: String) -> Dictionary:
	return game_bridge.CancelLandConstruction(project_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Every land construction project with its phase and progress (0-1).
func get_land_construction() -> Array[Dictionary]:
	return _arr(game_bridge.GetLandConstruction()) if game_bridge else []
## The permit cases filed on a pad ("" = every pad).
func get_land_permit_cases(plot_id: String) -> Array[Dictionary]:
	return _arr(game_bridge.GetLandPermitCases(plot_id)) if game_bridge else []
## What stocking a land tank would do.
func preview_land_stocking(site_id: String, tank_id: String, count: int, strain_id: String) -> Dictionary:
	return game_bridge.PreviewLandStocking(site_id, tank_id, count, strain_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Stock a land tank.
func stock_land_tank(site_id: String, tank_id: String, count: int, strain_id: String) -> Dictionary:
	return game_bridge.StockLandTank(site_id, tank_id, count, strain_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Grade a share of one land tank's fish into an empty, ready tank.
func grade_tank(site_id: String, from_tank_id: String, to_tank_id: String, share: float) -> Dictionary:
	return game_bridge.GradeTank(site_id, from_tank_id, to_tank_id, share) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Harvest a land tank now.
func harvest_land_tank(site_id: String, tank_id: String) -> Dictionary:
	return game_bridge.HarvestLandTank(site_id, tank_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Set a land site's Vannstrategi.
func set_vannstrategi(site_id: String, reuse: float, deep_intake_share: float, setpoint_c: float, blend: bool) -> Dictionary:
	return game_bridge.SetVannstrategi(site_id, reuse, deep_intake_share, setpoint_c, blend) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Fit heat recovery to a lot.
func install_heat_recovery(lot_id: String) -> Dictionary:
	return game_bridge.InstallHeatRecovery(lot_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
# F2-13 / F2-15: the land boards' reads and verbs (F2-32 draws them).
## The land board: lots and tanks against their comfort bands, the binding constraint, the cohorts against their plans.
func get_land_board(site_id: String) -> Dictionary:
	return game_bridge.GetLandBoard(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## The three Vannstrategi presets with the engine's projection (kWh/kg, growth, maturation risk).
func get_vannstrategi_presets(site_id: String) -> Array[Dictionary]:
	return _arr(game_bridge.GetVannstrategiPresets(site_id)) if game_bridge else []
## Set a site's Vannstrategi to a preset ("sparsom", "balansert", "hoy_tetthet").
func apply_vannstrategi_preset(site_id: String, preset_id: String) -> Dictionary:
	return game_bridge.ApplyVannstrategiPreset(site_id, preset_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## A site's competence, utilisation cap, Oppkjøring milestones and levers.
func get_land_competence(site_id: String) -> Dictionary:
	return game_bridge.GetLandCompetence(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Hire an operations manager (driftsleder).
func hire_driftsleder(site_id: String) -> Dictionary:
	return game_bridge.HireDriftsleder(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Start the site's training programme.
func start_training_programme(site_id: String) -> Dictionary:
	return game_bridge.StartTrainingProgramme(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Hire an external consultancy (12 months).
func hire_consultancy(site_id: String) -> Dictionary:
	return game_bridge.HireConsultancy(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Hire the operating staff before the first fish.
func hire_land_staff_early(site_id: String) -> Dictionary:
	return game_bridge.HireLandStaffEarly(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Set a land site's feeding plan (50-100 % of the appetite).
func set_land_feed_plan(site_id: String, ration_pct: float) -> Dictionary:
	return game_bridge.SetLandFeedPlan(site_id, ration_pct) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## The harvest planner: weeks with the expected price, tanks' projected weights and size grades, plans.
func get_land_harvest_planner(site_id: String) -> Dictionary:
	return game_bridge.GetLandHarvestPlanner(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## What harvesting tanks in a week would bring.
func quote_land_harvest_plan(site_id: String, tank_ids: Array, week_start_day: int) -> Dictionary:
	return game_bridge.QuoteLandHarvestPlan(site_id, tank_ids, week_start_day) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Plan tanks' harvest into a week.
func plan_land_harvest(site_id: String, tank_ids: Array, week_start_day: int) -> Dictionary:
	return game_bridge.PlanLandHarvest(site_id, tank_ids, week_start_day) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Cancel a harvest plan whose order is not placed yet.
func cancel_land_harvest_plan(site_id: String, plan_id: String) -> Dictionary:
	return game_bridge.CancelLandHarvestPlan(site_id, plan_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## The post-smolt planner: salinity, tiers, destinations with MTB headroom.
func get_post_smolt_planner(site_id: String) -> Dictionary:
	return game_bridge.GetPostSmoltPlanner(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## What moving a tank's fish to a fjord site would do (count 0 = the whole tank).
func preview_transfer_tank_to_sea(site_id: String, tank_id: String, sea_site_id: String, count: int) -> Dictionary:
	return game_bridge.PreviewTransferTankToSea(site_id, tank_id, sea_site_id, count) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Book a wellboat to move a tank's fish to a fjord site.
func transfer_tank_to_sea(site_id: String, tank_id: String, sea_site_id: String, count: int) -> Dictionary:
	return game_bridge.TransferTankToSea(site_id, tank_id, sea_site_id, count) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Sell a post-smolt tank's fish alive.
func sell_post_smolt(site_id: String, tank_id: String, count: int) -> Dictionary:
	return game_bridge.SellPostSmolt(site_id, tank_id, count) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## Set a post-smolt site's salinity (a pad with a river intake).
func set_salinity(site_id: String, salinity_ppt: float) -> Dictionary:
	return game_bridge.SetSalinity(site_id, salinity_ppt) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## A land site's own books: farming cost per kg HOG and its energy share.
func get_land_account(site_id: String) -> Dictionary:
	return game_bridge.GetLandAccount(site_id) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
## QA and scenario path: a land site built at once on a pad (no permits, no payment).
func commission_land_site(plot_id: String, template: String, lots: int, site_name: String) -> Dictionary:
	return game_bridge.CommissionLandSite(plot_id, template, lots, site_name) if game_bridge else {"success": false, "reason_key": "engine_not_ready"}
# ═══ END sim·land lane block ═══

# ═══ BEGIN sim·sea lane block (F2-03 list verbs; F2-06 … F2-11 append below) ═══
## The offshore units (GameBridge.Offshore.cs).
func get_offshore_units() -> Array[Dictionary]:
	return _arr(game_bridge.GetOffshoreUnits()) if game_bridge else []
## The offshore slots, free or held.
func get_offshore_plots() -> Array[Dictionary]:
	return _arr(game_bridge.GetOffshorePlots()) if game_bridge else []
## F2-06: today's sea at an offshore unit and its 10-day forecast (the window board).
func get_sea_state_forecast(site_id: String) -> Dictionary:
	return game_bridge.GetSeaStateForecast(site_id) if game_bridge else {"ok": false}
## F2-07: what building an offshore unit costs before committing (capex, licence value, consortium, payments, first stocking).
func preview_offshore_build(plot_id: String, farm_type: String, contract: String, equity_share: float) -> Dictionary:
	return game_bridge.PreviewOffshoreBuild(plot_id, farm_type, contract, equity_share) if game_bridge else {"ok": false, "refusal_key": "engine_not_ready"}
## F2-07: start building an offshore unit: {success, error, message, value, value2, id}.
func build_offshore_unit(plot_id: String, farm_type: String, contract: String, equity_share: float) -> Dictionary:
	return game_bridge.BuildOffshoreUnit(plot_id, farm_type, contract, equity_share) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: an offshore unit's construction phases and progress.
func get_offshore_construction(site_id: String) -> Dictionary:
	return game_bridge.GetOffshoreConstruction(site_id) if game_bridge else {"ok": false}
## F2-07: an offshore unit's gauges (sea, silo, crew clock, alarm load, next windows, bookings, defects, sensors).
func get_offshore_unit_state(site_id: String) -> Dictionary:
	return game_bridge.GetOffshoreUnitState(site_id) if game_bridge else {"ok": false}
## F2-07: a booking's P(done by day) and expected charter, from the unit's own forecast model.
func preview_offshore_booking(site_id: String, op: String, day: int, vessel: String) -> Dictionary:
	return game_bridge.PreviewOffshoreBooking(site_id, op, day, vessel) if game_bridge else {"ok": false}
## F2-07: book a vessel operation for a day.
func book_offshore_operation(site_id: String, op: String, day: int, vessel: String, count: int = 0, detail: String = "") -> Dictionary:
	return game_bridge.BookOffshoreOperation(site_id, op, day, vessel, count, detail) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: book an operation into its first green window.
func book_offshore_first_window(site_id: String, op: String, vessel: String, count: int = 0, detail: String = "") -> Dictionary:
	return game_bridge.BookOffshoreFirstWindow(site_id, op, vessel, count, detail) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: cancel an open booking.
func cancel_offshore_booking(site_id: String, booking_id: String) -> Dictionary:
	return game_bridge.CancelOffshoreBooking(site_id, booking_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: the tow's odds and cost before it starts.
func preview_offshore_tow(site_id: String) -> Dictionary:
	return game_bridge.PreviewOffshoreTow(site_id) if game_bridge else {"ok": false}
## F2-07: start the tow to the plot.
func start_offshore_tow(site_id: String) -> Dictionary:
	return game_bridge.StartOffshoreTow(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: choose the shakedown (0, 60, 90 or 120 days).
func choose_offshore_shakedown(site_id: String, days: int) -> Dictionary:
	return game_bridge.ChooseOffshoreShakedown(site_id, days) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: fix a revealed teething defect.
func fix_teething_defect(site_id: String, defect_id: String) -> Dictionary:
	return game_bridge.FixTeethingDefect(site_id, defect_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: install a sensor package (mru, subsea_cam, echo, ais_broadband).
func install_offshore_sensors(site_id: String, kind: String) -> Dictionary:
	return game_bridge.InstallOffshoreSensors(site_id, kind) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: service the sensor packages that are due.
func service_offshore_sensors(site_id: String) -> Dictionary:
	return game_bridge.ServiceOffshoreSensors(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: repair the unit's net, structure or all.
func repair_offshore_unit(site_id: String, scope: String) -> Dictionary:
	return game_bridge.RepairOffshoreUnit(site_id, scope) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-07: buy the consortium partner out.
func buy_out_offshore_partner(site_id: String) -> Dictionary:
	return game_bridge.BuyOutOffshorePartner(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-08: the unit's handling clock (days without a window against 21/45/75), MTB headroom, tilt drivers, incident and counter-verb state.
func get_offshore_handling_state(site_id: String) -> Dictionary:
	return game_bridge.GetOffshoreHandlingState(site_id) if game_bridge else {"ok": false}
## F2-08: the unit's incident reports with their driver chains.
func get_offshore_incident_reports(site_id: String) -> Dictionary:
	return game_bridge.GetOffshoreIncidentReports(site_id) if game_bridge else {"ok": false}
## F2-08: start an alarm review.
func start_offshore_alarm_review(site_id: String) -> Dictionary:
	return game_bridge.StartOffshoreAlarmReview(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-08: upgrade a procedure (hatch).
func start_offshore_procedure_upgrade(site_id: String, kind: String) -> Dictionary:
	return game_bridge.StartOffshoreProcedureUpgrade(site_id, kind) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-08: reinforce the unit (design Hs up after the yard work).
func install_offshore_reinforcement(site_id: String) -> Dictionary:
	return game_bridge.InstallOffshoreReinforcement(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-08: appeal a decided site withdrawal.
func appeal_site_withdrawal(site_id: String) -> Dictionary:
	return game_bridge.AppealSiteWithdrawal(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-09: the development-licence round, the gate, the rival band, applications and awards.
func get_development_round() -> Dictionary:
	return game_bridge.GetDevelopmentRound() if game_bridge else {"ok": false}
## F2-09: the score breakdown a dossier would get today against the rival band.
func preview_development_application(concept: String, plot_id: String) -> Dictionary:
	return game_bridge.PreviewDevelopmentApplication(concept, plot_id) if game_bridge else {"ok": false}
## F2-09: file a development-licence application.
func apply_development_licence(concept: String, plot_id: String) -> Dictionary:
	return game_bridge.ApplyDevelopmentLicence(concept, plot_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-09: a unit's licences, conversion checklist, case and fee.
func get_offshore_licences(site_id: String) -> Dictionary:
	return game_bridge.GetOffshoreLicences(site_id) if game_bridge else {"ok": false}
## F2-09: file the conversion of a unit's development licences.
func request_licence_conversion(site_id: String) -> Dictionary:
	return game_bridge.RequestLicenceConversion(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-09: appeal a refused conversion.
func appeal_licence_conversion(site_id: String) -> Dictionary:
	return game_bridge.AppealLicenceConversion(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-09: pay an approved conversion.
func pay_licence_conversion(site_id: String) -> Dictionary:
	return game_bridge.PayLicenceConversion(site_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
## F2-09: place converted licences in a zone's production area.
func place_converted_licences(holding_ids: Array, zone_id: String) -> Dictionary:
	return game_bridge.PlaceConvertedLicences(holding_ids, zone_id) if game_bridge else {"success": false, "error": "engine_not_ready", "message": "Not initialized"}
# ═══ END sim·sea lane block ═══

# ═══ BEGIN sim·geo lane block (geo track A: production areas, hav_1's binding, rivals, anchors) ═══
# Geo stages 2-3 (docs/geo/STAGE23_PLAN.md §3): pull-only reads of
# GameBridge.Geo.cs. Track B's sim·candidates block follows this one.
## The 13 production areas: code, name, the source's traffic light, tint, areas and the label point.
func get_geo_production_areas() -> Array[Dictionary]:
	return _arr(game_bridge.GetGeoProductionAreas()) if game_bridge else []
## The Havbruk til havs candidate a plot is bound to ({"bound": false} for every plot but hav_1).
func get_geo_plot_binding(plot_id: String) -> Dictionary:
	return game_bridge.GetGeoPlotBinding(plot_id) if game_bridge else {"bound": false, "plot_id": plot_id}
## All 33 Havbruk til havs candidates, the bound one with bound_plot_id "hav_1".
func get_geo_offshore_candidates() -> Array[Dictionary]:
	return _arr(game_bridge.GetGeoOffshoreCandidates()) if game_bridge else []
## The production area (1-13) holding an EPSG:25833 point, or 0 (the polygons; display only).
func get_geo_area_at(x: float, y: float) -> int:
	return int(game_bridge.GeoAreaAt(x, y)) if game_bridge else 0
## The rival layout's signature: refetch get_geo_sites only when it changes.
func get_geo_sites_signature() -> String:
	return str(game_bridge.GetGeoSitesSignature()) if game_bridge else ""
## Every locality as packed arrays (ids, x, y, kind, stage, po, mtb_t, owner, names) with the owners.
func get_geo_sites() -> Dictionary:
	return game_bridge.GetGeoSites() if game_bridge else {}
## An area's localities by kind, its sea grow-out MTB and its top 5 rival owners.
func get_rival_summary(po: int) -> Dictionary:
	return game_bridge.GetRivalSummary(po) if game_bridge else {}
## Where each zone's farm is drawn: its anchor locality (approximate by design).
func get_zone_anchors() -> Array[Dictionary]:
	return _arr(game_bridge.GetZoneAnchors()) if game_bridge else []
# ═══ END sim·geo lane block ═══

# ═══ BEGIN sim·candidates lane block (geo stages 2-3 track B: open-pen candidates, the build at a candidate, the company's site locations) ═══
## Geo B: a production area's 12 open-pen candidates by rank (0: the home area), each with its 'why here' and its build verdict.
func get_sea_candidates(area: int) -> Array[Dictionary]: return _arr(game_bridge.GetSeaCandidates(area)) if game_bridge else ([] as Array[Dictionary])
## Geo B: what a build at a candidate costs and adds (plan_site_build's keys and figures, plus the candidate and its checks).
func plan_site_build_at(zone_id: String, candidate_id: String) -> Dictionary: return game_bridge.PlanSiteBuildAt(zone_id, candidate_id) if game_bridge else {"success": false, "allowed": false, "reason_key": "engine_not_ready"}
## Geo B: where the company's fjord localities lie (at their candidate, or approximately at their zone's anchor).
func get_player_site_locations() -> Array[Dictionary]: return _arr(game_bridge.GetPlayerSiteLocations()) if game_bridge else ([] as Array[Dictionary])
# ═══ END sim·candidates lane block ═══
