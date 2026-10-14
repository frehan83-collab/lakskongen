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

## Feed recipe + preview (Phase 4.4).
func get_feed_recipe() -> Dictionary:
	if game_bridge and game_bridge.has_method("GetFeedRecipe"):
		return game_bridge.GetFeedRecipe()
	return {"protein": 42.0, "replace": 20.0, "astax": 50.0, "cost_per_kg": 15.0, "fcr_mult": 1.0, "grade": "A", "vap_mult": 1.12}

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
