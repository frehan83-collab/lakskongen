using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;

namespace LakskongenSim.Core
{
    /// <summary>Player-facing progress toward the active scenario's win condition.</summary>
    public sealed record ScenarioGoalStatus(
        string ScenarioId,
        string Objective,
        WinCondition Condition,
        double Progress01,
        bool Won,
        bool Lost,
        string LossReason,
        int? DaysLeft);

    /// <summary>
    /// A single starting sea site for a scenario: which zone it sits in, how many
    /// pens it has, how many smolts are stocked per pen, and the smolt strain
    /// (quality) used. Purely declarative — <see cref="ScenarioDefinition.ApplyStartingState"/>
    /// turns these into live <see cref="Farm.SeaSite"/> / <see cref="FishPopulation"/> objects.
    /// </summary>
    public class StartingSiteDefinition
    {
        /// <summary>Optional explicit site id. When blank a deterministic id is derived from the scenario.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Optional display name (UI only; not required by the simulation).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Environment zone id the site belongs to (created on demand).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        /// <summary>
        /// Production area (1-13, PO1-PO13) the site's zone lies in (S2-02): its
        /// sea temperature, lice spring weeks, PD zone and licence size. 0 takes
        /// the scenario's starting region's area
        /// (<see cref="Environment.ProductionAreas.DefaultForRegion"/>).
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("production_area")]
        public int ProductionArea { get; set; }

        /// <summary>Number of pens at the site (1+).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("pens")]
        public int Pens { get; set; } = 1;

        /// <summary>Smolts stocked per pen. 0 creates an empty site shell.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("smolt_count")]
        public int SmoltCount { get; set; }

        /// <summary>Smolt strain id (see <see cref="Farm.SmoltStrainRegistry"/>), e.g. "standard", "robust", "postsmolt".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("smolt_quality")]
        public string SmoltQuality { get; set; } = "standard";

        /// <summary>Starting containment tech level (Phase 4.1): "open"/"skirt"/
        /// "semi_closed"/"closed_float"/"submersible"/"offshore_closed".
        /// Blank or unknown leaves the site at the default (open net).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("containment")]
        public string Containment { get; set; } = "";

        /// <summary>Starting permeable barrier (P8): "smoltscreen" or blank.
        /// Unknown values leave the site without a barrier.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("barrier")]
        public string Barrier { get; set; } = "";

        /// <summary>
        /// Sea licences the company holds from the start, registered to this
        /// site (S2-01a). Each holds its area's licence MTB (780 t, 945 t in
        /// Troms and Finnmark) and pools in the area's biomass cap. Default 1:
        /// the capacity every starting site carried before S2-01.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("licences")]
        public int Licences { get; set; } = 1;

        /// <summary>
        /// The site's locality MTB (t, S2-01a). 0 = the MTB of the licences
        /// registered to it (<see cref="Licences"/> x the area's licence size),
        /// the site cap every starting site had before S2-01.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("locality_mtb_tonnes")]
        public double LocalityMtbTonnes { get; set; }
    }

    /// <summary>
    /// A catastrophe-insurance policy the player starts the scenario already
    /// holding (P11). Mirrors <see cref="GameState.InsurancePolicies"/> so it is
    /// serializable and applied additively by
    /// <see cref="ScenarioDefinition.ApplyStartingState"/>.
    /// </summary>
    public class StartingInsuranceDefinition
    {
        /// <summary>Cover kind: "Mortality", "Bloom", "Storm" or "ISA" (case-insensitive).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        /// <summary>Annual premium in NOK (>= 0).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("premium")]
        public decimal Premium { get; set; }

        /// <summary>Reimbursement fraction above the deductible, in [0,1].</summary>
        [System.Text.Json.Serialization.JsonPropertyName("coverage")]
        public double Coverage { get; set; }

        /// <summary>Retained loss (NOK) before cover applies (>= 0).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("deductible")]
        public decimal Deductible { get; set; }
    }

    /// <summary>
    /// A bloom/hypoxia countermeasure the player starts with already installed
    /// (P11). Flag-backed kinds ("bubble"/"diffuser") set the corresponding
    /// <see cref="Farm.SeaSite"/> flag; the rest are mirrored into
    /// <see cref="GameState.BloomMitigations"/>.
    /// </summary>
    public class StartingMitigationDefinition
    {
        /// <summary>Target sea-site id (may be blank for registry-only entries).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("site")]
        public string SiteId { get; set; } = "";

        /// <summary>Mitigation key: "bubble"/"diffuser"/"pumping"/"deep_feeding".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
    }

    /// <summary>
    /// A scripted bloom active from day 0 of the scenario (Phase 4.3). Lets an
    /// algal-bloom campaign open inside its mortality window so mitigation and
    /// insurance decisions matter immediately.
    /// </summary>
    public class StartingBloomDefinition
    {
        /// <summary>Affected sea-site id.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("site")]
        public string SiteId { get; set; } = "";

        /// <summary>Bloom kind: "algae", "jellyfish" or "hypoxia".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; set; } = "algae";

        /// <summary>Severity in [0,1] (clamped).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("severity")]
        public double Severity { get; set; } = 0.5;

        /// <summary>Duration in simulated days (default 21).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("duration_days")]
        public int DurationDays { get; set; } = 21;
    }

    /// <summary>
    /// A disease outbreak the scenario starts with. At the GameState level this
    /// records an outbreak event; when an engine is supplied to
    /// <see cref="ScenarioDefinition.ApplyStartingState"/> it additionally raises
    /// the integrated disease pressure and seeds the base SIR prevalence on the
    /// named (or first) population.
    /// </summary>
    public class StartingDiseaseDefinition
    {
        /// <summary>Pathogen label, e.g. "ISA", "ILA", "PD".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; } = "ISA";

        /// <summary>Site whose population should be seeded (blank = first population).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("site")]
        public string SiteId { get; set; } = "";

        /// <summary>Integrated disease-pressure multiplier applied at start (clamped engine-side).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("external_pressure")]
        public double ExternalPressure { get; set; } = 1.6;

        /// <summary>Simulated days of SIR spread applied to the seed population.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("seed_days")]
        public int SeedDays { get; set; } = 14;
    }

    /// <summary>
    /// Defines a playable scenario with starting conditions, objectives, and special rules.
    /// </summary>
    public class ScenarioDefinition
    {
        /// <summary>Unique snake_case identifier (e.g. "forste_sesong").</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name in Norwegian.</summary>
        public string Name { get; set; } = "";

        /// <summary>Flavour text describing the scenario premise.</summary>
        public string Description { get; set; } = "";

        /// <summary>Starting bank balance in NOK (may be negative for debt scenarios).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_balance")]
        public decimal StartingCapitalNOK { get; set; }

        /// <summary>Primary Norwegian production region for this scenario.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_region")]
        public ProductionRegion StartingRegion { get; set; }

        /// <summary>
        /// Difficulty rating from 1 (easiest) to 5 (hardest).
        /// Config files use names ("Easy"/"Medium"/"Hard") or numbers —
        /// the lenient converter accepts both.
        /// </summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(LenientDifficultyConverter))]
        public int Difficulty { get; set; }

        /// <summary>Primary victory objective type.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("win_condition")]
        public WinCondition WinCondition { get; set; }

        /// <summary>
        /// Scenario-specific rule overrides and numeric targets.
        /// Common keys:
        ///   "profit_target"         — decimal NOK threshold for Profit/Competition win
        ///   "sustainability_target" — int score threshold for Sustainability win
        ///   "ai_competitors"        — int count for Competition scenario
        ///   "harsh_storms"          — "true" for elevated storm frequency
        ///   "premium_prices"        — "true" for export price bonus
        ///   "start_disease"         — "true" to begin with an active disease outbreak
        ///   "start_debt"            — "true" signals negative starting capital
        ///   "regulation_pressure"   — "true" for stricter compliance thresholds
        ///   "calm_weather"          — "true" to suppress severe weather
        ///   "easy_market"           — "true" for reduced price volatility
        /// Values are always strings; parse as needed.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("special_rules")]
        public Dictionary<string, string> SpecialRules { get; set; } = new();

        /// <summary>Optional day limit — null means unlimited time.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("time_limit_days")]
        public int? TimeLimitDays { get; set; }
        /// <summary>Whether this scenario is available to play (false = locked).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("is_unlocked")]
        public bool IsUnlocked { get; set; }

        // ── Extended starting-state schema (additive; defaults keep old data safe) ──

        /// <summary>Starting sea sites (zone, pens, smolt count/quality).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_sites")]
        public List<StartingSiteDefinition> StartingSites { get; set; } = new();

        /// <summary>Research node ids the player begins with already unlocked.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_research")]
        public string[] StartingResearch { get; set; } = System.Array.Empty<string>();

        /// <summary>Buildings/assets the player begins with, e.g. "hatchery", "feed_mill",
        /// "processing_plant", "ai_feeding". Unknown values are ignored.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_buildings")]
        public string[] StartingBuildings { get; set; } = System.Array.Empty<string>();

        /// <summary>Long-form narrative / in-world lore shown on the scenario-select screen.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("story")]
        public string Story { get; set; } = "";

        /// <summary>Human-readable regulatory/gameplay restrictions (UI copy).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("restrictions")]
        public string[] Restrictions { get; set; } = System.Array.Empty<string>();

        /// <summary>Classification tags for filtering/sorting the campaign roster.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[] Tags { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// UI-facing starting cash. Mirrors <see cref="StartingCapitalNOK"/> for the
        /// scenario-select screen; 0 means "use <see cref="StartingCapitalNOK"/>".
        /// Cash is applied by the finance layer, not by <see cref="ApplyStartingState"/>.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_cash")]
        public decimal StartingCash { get; set; }

        /// <summary>
        /// Short, UI-facing objective string that names the depth system the
        /// scenario is built around (e.g. "Recover MAB via closed containment").
        /// Additive; blank for legacy scenarios.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("objective")]
        public string Objective { get; set; } = "";

        /// <summary>
        /// P9: explicit switch for the 25% resource-rent tax. When null the tax
        /// is on (owner decision D16, S1-03; the 70M NOK deduction shields
        /// small farms); false opts the scenario out.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("enable_resource_rent_tax")]
        public bool? EnableResourceRentTax { get; set; }

        /// <summary>P11: catastrophe policies the player starts subscribed to.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_insurance")]
        public List<StartingInsuranceDefinition> StartingInsurance { get; set; } = new();

        /// <summary>P11/4.3: countermeasures pre-installed at scenario start.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_mitigations")]
        public List<StartingMitigationDefinition> StartingMitigations { get; set; } = new();

        /// <summary>4.3: scripted blooms active from day 0 of the scenario.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_blooms")]
        public List<StartingBloomDefinition> StartingBlooms { get; set; } = new();

        /// <summary>Health: disease outbreak the scenario starts inside.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_disease")]
        public StartingDiseaseDefinition? StartingDisease { get; set; }

        /// <summary>
        /// Applies this scenario's starting state. Creates zones, sea sites,
        /// stocked fish populations, research flags, ownership flags and the
        /// depth-system starting state (containment/barriers, insurance,
        /// mitigations, scripted blooms, disease marker, resource-rent opt-in)
        /// directly on <paramref name="state"/>. Does NOT touch the finance
        /// balance (caller applies <see cref="StartingCapitalNOK"/>).
        ///
        /// <para>
        /// Safe to call more than once — zones, site ids, research ids and
        /// scripted bloom ids are de-duplicated and the mirrored lists are
        /// replaced rather than appended.
        /// </para>
        ///
        /// <para>
        /// When <paramref name="engine"/> is supplied the runtime-coupled parts
        /// are applied as well: insurance policies are subscribed on the live
        /// <c>InsuranceSystem</c>, the resource-rent tax is enabled, and a
        /// starting disease raises integrated disease pressure and seeds SIR
        /// prevalence. Passing null (the default) keeps the call pure and
        /// engine-independent, preserving the legacy one-argument call sites.
        /// </para>
        /// </summary>
        public void ApplyStartingState(GameState state, SimulationEngine? engine = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            // S2-01a: a scenario founds the company's licence register; capacity
            // v1 (pooled licence MTB, locality MTB, projected-peak stocking,
            // daily enforcement) applies from day 0.
            state.CapacityRules = Finance.CapacityRegime.LicencesV1;

            int siteIdx = 0;
            foreach (var siteDef in StartingSites ?? new List<StartingSiteDefinition>())
            {
                if (string.IsNullOrWhiteSpace(siteDef.Zone)) continue;

                string zoneId = siteDef.Zone;
                if (state.Zones.All(z => z.Id != zoneId))
                    state.Zones.Add(new LakskongenSim.Environment.Zone(zoneId));

                // S2-02: the zone lies in a production area; the first starting
                // site's area is the player's home area.
                int area = ProductionAreaOf(siteDef);
                var siteZone = state.Zones.First(z => z.Id == zoneId);
                if (!LakskongenSim.Environment.ProductionAreas.IsValid(siteZone.ProductionArea))
                    siteZone.ProductionArea = area;
                if (siteIdx == 0)
                    state.HomeProductionArea = siteZone.ProductionArea;

                string siteId = string.IsNullOrWhiteSpace(siteDef.Id)
                    ? $"{Id}_{zoneId}_{siteIdx + 1}"
                    : siteDef.Id;
                siteIdx++;

                if (state.SeaSites.Any(s => s.Id == siteId)) continue; // idempotent

                int penCount = siteDef.Pens > 0 ? siteDef.Pens : 1;
                int smoltPerPen = siteDef.SmoltCount > 0 ? siteDef.SmoltCount : 0;
                var strain = SmoltStrainRegistry.Get(siteDef.SmoltQuality);

                var site = new SeaSite(siteId, zoneId) { NumberOfPens = penCount, DisplayName = siteDef.Name ?? "" };
                site.Containment = ParseContainment(siteDef.Containment);
                site.Barrier = ParseBarrier(siteDef.Barrier);
                for (int pen = 0; pen < penCount; pen++)
                {
                    // With an engine, starting pens share its seeded, calibrated
                    // models exactly like StockSmolts and load do (F1072).
                    var pop = engine != null
                        ? new FishPopulation(engine.Growth, engine.Mortality)
                        : new FishPopulation(new GrowthModel(), new MortalityModel());
                    pop.ZoneId = zoneId;
                    pop.SiteId = siteId;
                    pop.SeaLiceLoad = strain.InitialLiceLoad;
                    if (smoltPerPen > 0)
                    {
                        pop.AddCohort(new FishPopulation.Cohort(
                            ageDays: 300,
                            count: smoltPerPen,
                            meanWeightKg: strain.ReleaseWeightKg,
                            stage: LifeStage.Smolt,
                            genetics: strain.BuildGenome()));
                    }
                    site.Stock(pop);
                    state.Populations.Add(pop);
                }
                if (smoltPerPen > 0) site.LastStockedDay = state.Day;

                // S2-01a: the scenario's licences, registered to this site, pool
                // in its production area; the site's own cap is its locality MTB.
                int licenceArea = siteZone.ProductionArea;
                int licenceMtb = LakskongenSim.Environment.ProductionAreas.IsValid(licenceArea)
                    ? LakskongenSim.Environment.ProductionAreas.Get(licenceArea).LicenceMtbTonnes
                    : LakskongenSim.Environment.ProductionAreas.StandardLicenceMtbTonnes;
                int licences = Math.Max(0, siteDef.Licences);
                site.MABTonnes = siteDef.LocalityMtbTonnes > 0.0
                    ? siteDef.LocalityMtbTonnes
                    : Math.Max(1, licences) * (double)licenceMtb;
                for (int l = 0; l < licences; l++)
                    SimulationEngine.GrantLicence(state, licenceArea, licenceMtb, siteId, Finance.LicenceOrigin.Scenario);

                // Keep site and global biomass/zone accounting coherent from day 0.
                state.SeaSites.Add(site);
                var zone = state.Zones.First(z => z.Id == zoneId);
                zone.CurrentFishBiomassKg += site.Pens.Sum(p => p.GetTotalBiomassKg());
            }

            foreach (var research in StartingResearch ?? System.Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(research) || state.UnlockedResearchIds.Contains(research))
                    continue;
                // Unknown ids used to be granted silently (review F1012): the player
                // was told they owned research with no node, effect or UI row.
                if (engine?.Research != null && !engine.Research.Nodes.ContainsKey(research))
                {
                    SimLog.Warn($"Scenario '{Id}': starting research '{research}' is not in the research tree; skipped");
                    continue;
                }
                state.UnlockedResearchIds.Add(research);
                // Unlock it on the live tree too: the effect getters read the live
                // tree, and the next research sync rewrites the mirror from it, so
                // granting only the mirror gave no effect and was then erased.
                engine?.Research?.ForceUnlock(research);
            }

            foreach (var building in StartingBuildings ?? System.Array.Empty<string>())
                ApplyStartingBuilding(state, building);

            ApplyInsuranceMirror(state);
            ApplyMitigations(state);
            ApplyBlooms(state);
            ApplyDiseaseMarker(state);

            // P9 / D16: the 25% resource-rent tax is on unless the scenario
            // opts out explicitly (it used to be opt-in, so no family farm or
            // multi-site career ever met it outside the ressursrente campaign).
            state.EnableResourceRentTax = EnableResourceRentTax ?? true;

            // Runtime-coupled depth wiring (additive, guarded).
            if (engine != null)
            {
                // Rebuild the live insurance policies from what we just mirrored.
                engine.RestoreInsuranceFromState(state);
                engine.EnableResourceRentTax = state.EnableResourceRentTax;

                var disease = StartingDisease;
                if (disease != null && !string.IsNullOrWhiteSpace(disease.Type))
                {
                    if (double.IsFinite(disease.ExternalPressure) && disease.ExternalPressure > 0)
                        engine.IntegratedDisease.SetExternalDiseasePressure(disease.ExternalPressure);

                    var target = string.IsNullOrWhiteSpace(disease.SiteId)
                        ? state.Populations.FirstOrDefault()
                        : state.Populations.FirstOrDefault(p => p.SiteId == disease.SiteId)
                          ?? state.Populations.FirstOrDefault();
                    if (target != null && disease.SeedDays > 0)
                        engine.Disease.Tick(target, disease.SeedDays);
                }
            }
        }

        private static void ApplyStartingBuilding(GameState state, string building)
        {
            switch ((building ?? "").Trim().ToLowerInvariant())
            {
                case "hatchery": state.OwnsHatchery = true; break;
                case "feed_mill" or "feedmill": state.OwnsFeedMill = true; break;
                case "processing_plant" or "processing": state.OwnsProcessingPlant = true; break;
                case "ai_feeding": state.OwnsAIFeedingSystem = true; break;
                // Unknown building names are ignored (forward-compatible).
            }
        }

        // ── New-schema application helpers (additive; guarded) ────────────────

        private void ApplyInsuranceMirror(GameState state)
        {
            var wanted = new List<InsurancePolicyState>();
            foreach (var policy in StartingInsurance ?? new List<StartingInsuranceDefinition>())
            {
                if (policy == null || string.IsNullOrWhiteSpace(policy.Kind)) continue;
                wanted.Add(new InsurancePolicyState
                {
                    Kind = policy.Kind.Trim(),
                    Premium = double.IsFinite((double)policy.Premium) ? (double)policy.Premium : 0.0,
                    Coverage = double.IsFinite(policy.Coverage) ? Math.Clamp(policy.Coverage, 0.0, 1.0) : 0.0,
                    Deductible = policy.Deductible > 0m ? policy.Deductible : 0m
                });
            }
            if (wanted.Count > 0)
                state.InsurancePolicies = wanted;
        }

        private void ApplyMitigations(GameState state)
        {
            var defs = StartingMitigations;
            if (defs == null || defs.Count == 0) return;

            state.BloomMitigations ??= new List<BloomMitigationState>();
            foreach (var def in defs)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.Kind)) continue;

                var site = string.IsNullOrWhiteSpace(def.SiteId)
                    ? null
                    : state.SeaSites.FirstOrDefault(s => s.Id == def.SiteId);
                var kind = ParseMitigation(def.Kind);
                switch (kind)
                {
                    case MitigationKind.BubbleCurtain:
                        if (site != null) site.HasBubbleCurtain = true;
                        break;
                    case MitigationKind.O2Diffuser:
                        if (site != null) site.HasO2Diffuser = true;
                        break;
                    default:
                        string siteId = (def.SiteId ?? "").Trim();
                        string key = def.Kind.Trim().ToLowerInvariant();
                        if (!state.BloomMitigations.Any(m => m.SiteId == siteId && m.Kind == key))
                            state.BloomMitigations.Add(new BloomMitigationState { SiteId = siteId, Kind = key });
                        break;
                }
            }
        }

        private void ApplyBlooms(GameState state)
        {
            var defs = StartingBlooms;
            if (defs == null || defs.Count == 0) return;

            state.ActiveBlooms ??= new List<Environment.BloomEvent>();
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.SiteId)) continue;

                string bloomId = $"{Id}_{def.SiteId}_{(string.IsNullOrWhiteSpace(def.Kind) ? "algae" : def.Kind.ToLowerInvariant())}_{i + 1}";
                if (state.ActiveBlooms.Any(b => b.Id == bloomId)) continue;

                state.ActiveBlooms.Add(new Environment.BloomEvent
                {
                    Id = bloomId,
                    Kind = string.IsNullOrWhiteSpace(def.Kind) ? "algae" : def.Kind.Trim().ToLowerInvariant(),
                    SiteId = def.SiteId,
                    Severity01 = Math.Clamp(def.Severity, 0.0, 1.0),
                    StartDay = state.Day,
                    DurationDays = def.DurationDays > 0 ? def.DurationDays : 21
                });
            }
        }

        /// <summary>Norwegian name of a scenario disease code ("ISA" is ILA in Norway).</summary>
        private static string DiseaseName(string type) => type.Trim().ToUpperInvariant() switch
        {
            "ISA" or "ILA" => "ILA",
            "PD" => "pankreassykdom (PD)",
            "CMS" => "hjertesprekk (CMS)",
            "AGD" => "amøbegjellesykdom (AGD)",
            "IHN" => "IHN",
            _ => type
        };
        private void ApplyDiseaseMarker(GameState state)
        {
            var disease = StartingDisease;
            if (disease == null || string.IsNullOrWhiteSpace(disease.Type)) return;

            string eventId = $"{Id}-{disease.Type.Trim().ToLowerInvariant()}-outbreak";
            if (state.EventLog.Any(e => e.Id == eventId)) return;

            state.EventLog.Add(new Events.EventRecord
            {
                Day = state.Day,
                Id = eventId,
                Name = $"Utbrudd: {DiseaseName(disease.Type)}",
                Category = Events.EventCategory.Disease,
                Description = string.IsNullOrWhiteSpace(disease.SiteId)
                    ? $"Scenarioet starter med et aktivt utbrudd av {DiseaseName(disease.Type)}."
                    : $"Scenarioet starter med et aktivt utbrudd av {DiseaseName(disease.Type)} på {state.SeaSites.FirstOrDefault(s => s.Id == disease.SiteId)?.Label ?? disease.SiteId}.",
                BalanceChange = 0m
            });
            state.TrimEventLog();
        }

        /// <summary>
        /// The production area a starting site lies in (S2-02): the one it names
        /// (<c>production_area</c>), else the starting region's
        /// (<see cref="LakskongenSim.Environment.ProductionAreas.DefaultForRegion"/>).
        /// </summary>
        public int ProductionAreaOf(StartingSiteDefinition site)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            if (LakskongenSim.Environment.ProductionAreas.IsValid(site.ProductionArea))
                return site.ProductionArea;
            return LakskongenSim.Environment.ProductionAreas.DefaultForRegion(StartingRegion);
        }

        private static ContainmentLevel ParseContainment(string? raw) =>
            (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "skirt" => ContainmentLevel.Skirt,
                "semi_closed" or "semiclosed" or "semi-closed" or "semi" => ContainmentLevel.SemiClosed,
                "closed" or "closed_float" or "closedfloat" => ContainmentLevel.ClosedFloat,
                "submersible" or "sub" => ContainmentLevel.Submersible,
                "offshore_closed" or "offshoreclosed" => ContainmentLevel.OffshoreClosed,
                "open" or "open_net" or "opennet" or "" => ContainmentLevel.OpenNet,
                _ => ContainmentLevel.OpenNet
            };

        private static BarrierKind ParseBarrier(string? raw) =>
            (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "smoltscreen" or "smolt_screen" or "smolt-screen" => BarrierKind.Smoltscreen,
                _ => BarrierKind.None
            };

        private static MitigationKind ParseMitigation(string? raw) =>
            (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "bubble" or "bubble_curtain" or "bubblecurtain" => MitigationKind.BubbleCurtain,
                "diffuser" or "o2" or "o2_diffuser" or "o2diffuser" or "oxygen" => MitigationKind.O2Diffuser,
                "pumping" or "pump" => MitigationKind.Pumping,
                "deep_feeding" or "deepfeeding" or "deep" => MitigationKind.DeepFeeding,
                _ => MitigationKind.None
            };
    }

    /// <summary>
    /// Campaign system: manages the scenario roster, tracks the active scenario,
    /// and checks win/loss conditions on every simulated day.
    ///
    /// Integration: call <see cref="CheckWinConditions"/> once per day inside
    /// SimulationEngine.Advance(), after all subsystem updates.
    /// Wire <see cref="OnScenarioWon"/> and <see cref="OnScenarioLost"/> in GameBridge
    /// to emit Godot signals to the UI layer.
    /// </summary>
    public class ScenarioSystem
    {
        private readonly GameState _state;
        private readonly Finance.FinanceManager? _financeManager;
        private readonly Dictionary<string, ScenarioDefinition> _scenarios = new();

        // ── Active scenario state ─────────────────────────────────────────────

        /// <summary>The currently active scenario, or null if none selected.</summary>
        public ScenarioDefinition? ActiveScenario { get; private set; }

        /// <summary>True while a scenario is running.</summary>
        public bool IsScenarioActive => ActiveScenario != null && !IsWon && !IsLost;

        /// <summary>True once the player has met the win condition.</summary>
        public bool IsWon { get; private set; }

        /// <summary>True once a loss condition has been triggered.</summary>
        public bool IsLost { get; private set; }

        /// <summary>Human-readable loss reason set when <see cref="IsLost"/> becomes true.</summary>
        public string LossReason { get; private set; } = "";

        // Simulated AI competitor profit for Competition scenarios. Derived from
        // days elapsed (not accumulated per call) so a loaded run continues it.
        private readonly Dictionary<string, decimal> _aiProgress = new();
        private int _scenarioStartDay;

        /// <summary>Game day the active scenario started (saved with the game).</summary>
        public int ScenarioStartDay => _scenarioStartDay;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when the player meets the scenario's win condition.</summary>
        public event Action<ScenarioDefinition>? OnScenarioWon;

        /// <summary>Fired when the player fails the scenario. Second argument is the reason string.</summary>
        public event Action<ScenarioDefinition, string>? OnScenarioLost;

        // ── Constructor ───────────────────────────────────────────────────────

        public ScenarioSystem(GameState state, Finance.FinanceManager? financeManager = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
            RegisterAllScenarios();
        }

        // ── Scenario management ───────────────────────────────────────────────

        /// <summary>
        /// Activate a scenario. Applies starting capital to <see cref="GameState.Balance"/>
        /// and seeds AI competitor state if this is a Competition scenario.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown scenario id.</exception>
        /// <exception cref="InvalidOperationException">Scenario is locked.</exception>
        public void StartScenario(string scenarioId)
        {
            if (!_scenarios.TryGetValue(scenarioId, out var scenario))
                throw new ArgumentException($"Unknown scenario id: '{scenarioId}'", nameof(scenarioId));

            if (!scenario.IsUnlocked)
                throw new InvalidOperationException($"Scenario '{scenarioId}' is locked.");

            ActiveScenario = scenario;
            IsWon = false;
            IsLost = false;
            LossReason = "";
            _scenarioStartDay = _state.Day;
            
            // Reset cash to the scenario's starting capital.
            if (_financeManager != null)
            {
                _financeManager.SetBalanceForLoad(scenario.StartingCapitalNOK);
            }
            else
            {
                #pragma warning disable CS0618
                _state.Balance = scenario.StartingCapitalNOK;
                #pragma warning restore CS0618
            }

            SeedAiCompetitors(scenario);

            SimLog.Info($"[ScenarioSystem] Started '{scenario.Name}' " +
                        $"(difficulty {scenario.Difficulty}/5, goal: {scenario.WinCondition})");
        }

        /// <summary>
        /// Re-activate a saved scenario without side effects (no cash reset, no
        /// events): the load path's counterpart to <see cref="StartScenario"/> (F243).
        /// Unknown ids clear the active scenario.
        /// </summary>
        public void RestoreActiveState(string? scenarioId, int startDay, bool isWon, bool isLost, string? lossReason)
        {
            ActiveScenario = !string.IsNullOrEmpty(scenarioId) && _scenarios.TryGetValue(scenarioId, out var scenario)
                ? scenario
                : null;
            _scenarioStartDay = startDay;
            IsWon = ActiveScenario != null && isWon;
            IsLost = ActiveScenario != null && isLost;
            LossReason = ActiveScenario != null ? lossReason ?? "" : "";
            if (ActiveScenario != null)
                SeedAiCompetitors(ActiveScenario);
            else
                _aiProgress.Clear();
        }

        private void SeedAiCompetitors(ScenarioDefinition scenario)
        {
            _aiProgress.Clear();
            if (scenario.WinCondition == WinCondition.Competition &&
                scenario.SpecialRules.TryGetValue("ai_competitors", out var countStr) &&
                int.TryParse(countStr, out var count))
            {
                for (int i = 1; i <= count; i++)
                    _aiProgress[$"ai_{i}"] = 0m;
            }
        }

        /// <summary>
        /// Evaluate win and loss conditions for the active scenario.
        /// Call once per simulated day after all subsystem updates complete.
        /// </summary>
        /// <param name="sustainabilityScore">Current 0–100 sustainability score.</param>
        /// <param name="tutorialComplete">True when TutorialSystem has finished all steps.</param>
        public void CheckWinConditions(int sustainabilityScore = 0, bool tutorialComplete = false)
        {
            if (ActiveScenario == null || IsWon || IsLost) return;

            int daysElapsed = _state.Day - _scenarioStartDay;
            decimal equity = CurrentEquity();

            // Bankruptcy is the engine's end-of-day solvency rule (S1-03): one
            // floor per scenario from BalancingConfig, which calls
            // DeclareBankruptcy. This check used its own -2M floor while the
            // bridge ended the game at -1M.

            // ── Universal loss: time limit exceeded (except Survival — handled below) ──
            if (ActiveScenario.WinCondition != WinCondition.Survival &&
                ActiveScenario.TimeLimitDays.HasValue &&
                daysElapsed > ActiveScenario.TimeLimitDays.Value)
            {
                TriggerLoss($"Tid ute etter {ActiveScenario.TimeLimitDays.Value} dager.");
                return;
            }

            // ── Per-condition win checks ──────────────────────────────────────
            switch (ActiveScenario.WinCondition)
            {
                case WinCondition.Tutorial:
                    if (tutorialComplete)
                        TriggerWin();
                    break;

                case WinCondition.Profit:
                {
                    decimal target = ProfitTarget();
                    if (equity >= target)
                        TriggerWin();
                    break;
                }

                case WinCondition.Sustainability:
                {
                    int target = (int)GetRuleDecimal("sustainability_target", 95m);
                    if (sustainabilityScore >= target)
                        TriggerWin();
                    break;
                }

                case WinCondition.Survival:
                {
                    int survivalDays = ActiveScenario.TimeLimitDays ?? 730;
                    if (daysElapsed > survivalDays)
                    {
                        // Must still be solvent at the end
                        if (equity > 0)
                            TriggerWin();
                        else
                            TriggerLoss($"Du overlevde ikke de {survivalDays} dagene med positiv balanse.");
                    }
                    break;
                }

                case WinCondition.Competition:
                {
                    decimal target = ProfitTarget();

                    // AI competitors earn linearly from the scenario start (varied
                    // rate per AI); derived from days elapsed so it survives a load.
                    int aiIdx = 0;
                    foreach (var key in _aiProgress.Keys.ToList())
                    {
                        decimal dailyRate = 3_500m + (aiIdx * 1_200m); // AI_1: 3.5k, AI_2: 4.7k, AI_3: 5.9k NOK/day
                        _aiProgress[key] = dailyRate * System.Math.Max(0, daysElapsed);
                        aiIdx++;
                    }

                    // If any AI reached target first, player loses
                    if (_aiProgress.Values.Any(v => v >= target))
                    {
                        TriggerLoss("En konkurrent nådde profittmålet før deg!");
                        return;
                    }

                    // Player wins by reaching target before any AI
                    if (equity >= target)
                        TriggerWin();
                    break;
                }
            }
        }

        // ── Goal measures (shared by the win check and the goal HUD) ─────────

        private decimal CurrentCash() => _financeManager?.Balance ??
            #pragma warning disable CS0618
            _state.Balance;
            #pragma warning restore CS0618

        /// <summary>
        /// S5: profit and solvency are judged on EQUITY (cash minus outstanding
        /// loan principal), not raw cash — otherwise one loan wins any profit
        /// scenario on day 1.
        /// </summary>
        private decimal CurrentEquity()
        {
            decimal outstandingLoans = 0m;
            if (_state.ActiveLoans != null)
                foreach (var loan in _state.ActiveLoans)
                    outstandingLoans += System.Math.Max(0m, loan.Principal);
            return CurrentCash() - outstandingLoans;
        }

        /// <summary>Equity a Profit/Competition scenario must reach.</summary>
        private decimal ProfitTarget() => GetRuleDecimal("profit_target",
            ActiveScenario?.WinCondition == WinCondition.Competition ? 10_000_000m : 5_000_000m);

        /// <summary>
        /// Progress toward the active scenario's win condition, measured the way
        /// <see cref="CheckWinConditions"/> judges it (review F949: the goal HUD
        /// re-derived it in GDScript from a tracker that was never initialised
        /// and sat at 0%). Profit/Competition: share of the way from the
        /// starting capital to the equity target. Survival: share of the time
        /// limit survived. Sustainability: score vs target. Tutorial: steps done.
        /// Null when no scenario is active.
        /// </summary>
        public ScenarioGoalStatus? GetGoalStatus(int sustainabilityScore = 0, double tutorialProgress01 = 0.0)
        {
            if (ActiveScenario == null) return null;
            int daysElapsed = System.Math.Max(0, _state.Day - _scenarioStartDay);
            int? daysLeft = ActiveScenario.TimeLimitDays is int limit
                ? System.Math.Max(0, limit - daysElapsed)
                : null;

            double progress;
            switch (ActiveScenario.WinCondition)
            {
                case WinCondition.Profit:
                case WinCondition.Competition:
                {
                    decimal start = ActiveScenario.StartingCapitalNOK;
                    decimal span = ProfitTarget() - start;
                    progress = span > 0m ? (double)((CurrentEquity() - start) / span) : 1.0;
                    break;
                }
                case WinCondition.Survival:
                    progress = ActiveScenario.TimeLimitDays is int days && days > 0
                        ? daysElapsed / (double)days
                        : 0.0;
                    break;
                case WinCondition.Sustainability:
                    progress = sustainabilityScore / (double)GetRuleDecimal("sustainability_target", 95m);
                    break;
                case WinCondition.Tutorial:
                    progress = tutorialProgress01;
                    break;
                default:
                    progress = 0.0;
                    break;
            }
            if (IsWon) progress = 1.0;

            string objective = string.IsNullOrWhiteSpace(ActiveScenario.Objective)
                ? ActiveScenario.Description
                : ActiveScenario.Objective;
            return new ScenarioGoalStatus(ActiveScenario.Id, objective, ActiveScenario.WinCondition,
                System.Math.Clamp(progress, 0.0, 1.0), IsWon, IsLost, LossReason ?? "", daysLeft);
        }

        // ── Roster queries ────────────────────────────────────────────────────

        /// <summary>All registered scenarios, keyed by id.</summary>
        public IReadOnlyDictionary<string, ScenarioDefinition> All => _scenarios;

        /// <summary>Look up a scenario by id, returning null if not found.</summary>
        public ScenarioDefinition? GetScenario(string id) =>
            _scenarios.TryGetValue(id, out var s) ? s : null;

        /// <summary>All scenarios visible to the player (unlocked or locked).</summary>
        public IEnumerable<ScenarioDefinition> GetAll() => _scenarios.Values;

        /// <summary>Only unlocked scenarios.</summary>
        public IEnumerable<ScenarioDefinition> GetUnlocked() =>
            _scenarios.Values.Where(s => s.IsUnlocked);

        /// <summary>Unlock a scenario (call after completing a prerequisite).</summary>
        public void Unlock(string scenarioId)
        {
            if (_scenarios.TryGetValue(scenarioId, out var s))
                s.IsUnlocked = true;
        }

        /// <summary>
        /// AI competitor accumulated profit for Competition scenarios (read-only).
        /// Keys are "ai_1", "ai_2", etc.
        /// </summary>
        public IReadOnlyDictionary<string, decimal> AiProgress => _aiProgress;

        // ── Internal helpers ──────────────────────────────────────────────────

        private decimal GetRuleDecimal(string key, decimal defaultValue)
        {
            if (ActiveScenario?.SpecialRules.TryGetValue(key, out var raw) == true &&
                decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
            return defaultValue;
        }

        /// <summary>
        /// The engine declared the company bankrupt (cash below the scenario's
        /// floor): a running scenario is lost with <paramref name="reason"/>.
        /// A scenario already won or lost keeps its verdict.
        /// </summary>
        public void DeclareBankruptcy(string reason)
        {
            if (ActiveScenario == null || IsWon || IsLost) return;
            TriggerLoss(reason);
        }

        private void TriggerWin()
        {
            IsWon = true;
            Console.WriteLine($"[ScenarioSystem] ★ Vunnet: '{ActiveScenario!.Name}'");
            OnScenarioWon?.Invoke(ActiveScenario!);
        }

        private void TriggerLoss(string reason)
        {
            IsLost = true;
            LossReason = reason;
            Console.WriteLine($"[ScenarioSystem] ✗ Tapt: {reason}");
            OnScenarioLost?.Invoke(ActiveScenario!, reason);
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCENARIO DEFINITIONS — 5 Norwegian campaign scenarios
        // ══════════════════════════════════════════════════════════════════════

        private void RegisterAllScenarios()
        {
            // ─── 1. FAMILIEGÅRDEN — Tutorial (Difficulty 1) ───────────────────
            //    Inherit grandfather's small farm in Hardanger. One license, low funds, learn basics.
            Register(new ScenarioDefinition
            {
                Id = "forste_sesong",
                Name = "Familiegården",
                Description =
                    "Du har arvet bestefars lille oppdrett i Hardanger. Med én lisens og små midler må du lære grunnleggende oppdrett. " +
                    "Bygg opp gården fra bunnen av og vis at du kan drive lønnsomt.",
                StartingCapitalNOK = 3_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 1,
                WinCondition = WinCondition.Tutorial,
                SpecialRules = new Dictionary<string, string>
                {
                    ["calm_weather"] = "true",
                    ["easy_market"]  = "true",
                },
                // The inherited farm. Without it the start could not afford a 6M
                // site, so the tutorial could not be played (F1013). One pen of
                // 20,000 smolts and 3M NOK: 5,000 smolts on 300k could not carry
                // a crew to the first harvest (decision log, RUN_LOG §7).
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "bestefars_anlegg", Name = "Bestefars Anlegg", Zone = "fjord_west", ProductionArea = 3, Pens = 1, SmoltCount = 20_000, SmoltQuality = "standard" },
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });

            // ─── 2. NORDLANDS UTFORDRING — Storm & Premium (Difficulty 3) ─────
            //    Harsh Nordland coast. Storms, cold, but premium sashimi market access to Japan.
            Register(new ScenarioDefinition
            {
                Id = "lofoten_eventyr",
                Name = "Nordlands Utfordring",
                Description =
                    "De harde kystene i Nordland byr på de verste stormene og kaldeste havstrømmene, " +
                    "men laksen her når premiumpriser som sashimi til Japan. " +
                    "Takle uværet og nå et overskudd på 5 000 000 NOK.",
                StartingCapitalNOK = 2_000_000m,
                StartingRegion = ProductionRegion.Nordland,
                Difficulty = 3,
                WinCondition = WinCondition.Profit,
                SpecialRules = new Dictionary<string, string>
                {
                    ["harsh_storms"]   = "true",
                    ["premium_prices"] = "true",
                    ["profit_target"]  = "5000000",
                },
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "nordland_premium", Name = "Nordland Premium", Zone = "fjord_north", ProductionArea = 9, Pens = 2, SmoltCount = 8_000, SmoltQuality = "standard" },
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });

            // ─── 3. TRØNDER-KRIGEN — Competition (Difficulty 4) ───────────────
            //    Competitive Trøndelag. 3 AI competitors. Market share battle.
            Register(new ScenarioDefinition
            {
                Id = "laksebaronene",
                Name = "Trønder-krigen",
                Description =
                    "Tre AI-konkurrenter kjemper om å bli Trøndelags største lakseaktør. " +
                    "Markedskampen er brutal – hvem som helst kan ta markedsandeler, kjøpe lisenser og dumpe priser. " +
                    "Vær den første til å nå 10 000 000 NOK i fortjeneste.",
                StartingCapitalNOK = 5_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 4,
                WinCondition = WinCondition.Competition,
                SpecialRules = new Dictionary<string, string>
                {
                    ["ai_competitors"] = "3",
                    ["profit_target"]  = "10000000",
                },
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "trondelag_base", Name = "Trøndelag Base", Zone = "fjord_west", ProductionArea = 6, Pens = 3, SmoltCount = 10_000, SmoltQuality = "standard" },
                },
                TimeLimitDays = null,
                IsUnlocked = true, // No progression yet: every scenario is open until U2-03 (roadmap U1-07).
            });

            // ─── 4. FINNMARKS GJENFØDSEL — Survival (Difficulty 5) ───────────
            //    Bankrupt Finnmark site with active ILA outbreak. Turnaround challenge.
            Register(new ScenarioDefinition
            {
                Id = "krise_manager",
                Name = "Finnmarks Gjenfødsel",
                Description =
                    "Du tar over et konkursrammet anlegg i Finnmark med et aktivt ILA-utbrudd. " +
                    "500 000 NOK i gjeld og Mattilsynet har varslet sanksjoner. " +
                    "Overlev i 730 dager og snu selskapet til lønnsomhet.",
                StartingCapitalNOK = -500_000m,
                StartingRegion = ProductionRegion.Finnmark,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_disease"]       = "true",
                    ["start_debt"]          = "true",
                    ["regulation_pressure"] = "true",
                },
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "finnmark_turnaround", Name = "Finnmark Turnaround", Zone = "fjord_north", ProductionArea = 12, Pens = 2, SmoltCount = 6_000, SmoltQuality = "standard" },
                },
                TimeLimitDays = 730,
                IsUnlocked = true, // No progression yet: every scenario is open until U2-03 (roadmap U1-07).
            });

            // ─── 5. LAKSKONGEN — Freeplay (Difficulty 1) ──────────────────────
            //    Freeplay. Start anywhere, no restrictions, build your empire.
            Register(new ScenarioDefinition
            {
                Id = "barekraft_mester",
                Name = "Lakskongen",
                Description =
                    "Fri lek med god kapital: du starter med et etablert anlegg i Vestland og bygger " +
                    "lakseimperiet ditt uten tidspress eller konkurrenter. " +
                    "Målet er å nå 100 MNOK i egenkapital.",
                StartingCapitalNOK = 50_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 1,
                WinCondition = WinCondition.Profit,
                SpecialRules = new Dictionary<string, string>
                {
                    ["calm_weather"] = "true",
                    ["easy_market"]  = "true",
                    ["profit_target"] = "100000000",
                },
                // S2-01a: the established company holds 8 licences in PO4 (6,240 t
                // MTB) on a locality cleared for 3,120 t, so building sites is an
                // empire game: a build adds a locality, never MTB (before S2-01
                // every 6M build came with 780 t). Measured in docs/BALANCE.md §1h.
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "lakskongen_hovedanlegg", Name = "Lakskongen Hovedanlegg", Zone = "fjord_west", ProductionArea = 4, Pens = 4, SmoltCount = 12_000, SmoltQuality = "standard", Licences = 8, LocalityMtbTonnes = 3_120 },
                },
                TimeLimitDays = null,
                IsUnlocked = true, // No progression yet: every scenario is open until U2-03 (roadmap U1-07).
            });

            // ══════════════════════════════════════════════════════════════════
            // CAMPAIGN SCENARIOS — grounded in real 2024–26 Norwegian aquaculture
            // events/policy (see per-scenario comments for sources).
            // ══════════════════════════════════════════════════════════════════

            // ─── 6. PO3 RØD SONE — Red-zone MAB cut (Difficulty 4) ───────────
            //    Production area PO3 placed under red traffic light → −6% MAB.
            //    Survival strategy = "environmental flexibility" via closed
            //    containment (ContainmentLevel >= SemiClosed) per
            //    regjeringen.no revidert forslag om miljøfleksibilitet (2024–25).
            Register(new ScenarioDefinition
            {
                Id = "po3_red_sone",
                Name = "PO3 Rød Sone",
                Story =
                    "Produksjonsområde PO3 har gått over til RØDT lys etter lakselustrykk og rømningsrisiko. " +
                    "Mattilsynet og Nærings- og fiskeridepartementet strammer inn: maksimal tillatt biomasse (MTB) " +
                    "kuttes med 6 %. Din eneste vei til 'miljøfleksibilitet' er lukket teknologi " +
                    "(semi-lukket og høyere), som gir biomassekreditt selv i rød sone. " +
                    "Behold lønnsomheten og overlev reguleringsbølgen.",
                Description =
                    "PO3 i rød sone: −6 % MTB, og lukkede anlegg er eneste vei til miljøfleksibilitet. Overlev i 3 år.",
                Objective =
                    "Gjenvinn MTB ved å drifte minst ett anlegg som semilukket og overlev 1095 dager i rød sone.",
                StartingCapitalNOK = 12_000_000m,
                StartingCash = 12_000_000m,
                // PO3 (Karmøy til Sotra) lies in Vestland, not Trøndelag (S2-02).
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 4,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "po3_nord", Name = "PO3 Nord", Zone = "po3_nord", ProductionArea = 3, Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust", Containment = "semi_closed" },
                    new() { Id = "po3_sor",  Name = "PO3 Sør",  Zone = "po3_sor", ProductionArea = 3,  Pens = 4, SmoltCount = 10_000, SmoltQuality = "standard" },
                },
                StartingResearch = new[] { "sensor-networks", "deep-sea-farming" },
                StartingBuildings = new[] { "ai_feeding" },
                Restrictions = new[]
                {
                    "Rød sone: −6 % MTB på alle åpne anlegg",
                    "Bare lukkede anlegg (semilukket eller bedre) kvalifiserer for miljøfleksibilitet",
                    "Minst ett anlegg må holdes semilukket for å gjenvinne MTB",
                },
                Tags = new[] { "regulering", "rød_sone", "containment", "survival", "mtb" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["red_zone"] = "true",
                    ["mab_reduction"] = "0.06",
                    ["environmental_flexibility"] = "closed_containment",
                    ["containment_target"] = "semi_closed",
                    ["min_contained_sites"] = "1",
                    ["regulation_pressure"] = "true",
                },
                TimeLimitDays = 1095, // 3 years
                IsUnlocked = true,
            });

            // ─── 7. ISA-UTBRUDDET — Mandatory cull zone (Difficulty 5) ────────
            //    SalMar reported ISA at a site in Q3 2026 → compulsory cull
            //    (bekjempelsessone), biosecurity firewalls and insurance cover.
            Register(new ScenarioDefinition
            {
                Id = "isa_utbrudd",
                Name = "ISA-utbruddet",
                Story =
                    "En nabooppdretter har varslet om ILA ved et anlegg, og Mattilsynet har opprettet en bekjempelsessone. " +
                    "Hele det smittede anlegget må slaktes ned. " +
                    "Du må bygge smittevern mellom sonene, aktivere forsikringen " +
                    "og hindre smitte til nabolokalitetene. Hver dag teller — klarer du å stanse utbruddet?",
                Description =
                    "Aktivt ILA-utbrudd med tvungen nedslakting. Bygg smittevern, krev inn forsikring og redd de friske anleggene.",
                Objective =
                    "Hold ILA-utbruddet isolert i kjerneanlegget, bygg lusebarrierer mellom sonene og overlev 540 dager med forsikringsdekning.",
                StartingCapitalNOK = 18_000_000m,
                StartingCash = 18_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "isa_kjerne", Name = "Infisert kjerneanlegg", Zone = "isa_kjerne", ProductionArea = 6, Pens = 4, SmoltCount = 8_000,  SmoltQuality = "standard" },
                    new() { Id = "isa_nord",   Name = "Nabofelt nord",          Zone = "isa_nord", ProductionArea = 6,   Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust",   Barrier = "smoltscreen" },
                    new() { Id = "isa_sor",    Name = "Nabofelt sør",           Zone = "isa_sor", ProductionArea = 6,    Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust",   Barrier = "smoltscreen" },
                },
                StartingResearch = new[] { "disease-resistance", "sensor-networks" },
                StartingBuildings = new[] { "hatchery" }, // biosecure smolt production
                StartingInsurance = new List<StartingInsuranceDefinition>
                {
                    new() { Kind = "ISA",       Premium = 300_000m, Coverage = 0.90, Deductible = 500_000m },
                    new() { Kind = "Mortality", Premium = 120_000m, Coverage = 0.70, Deductible = 250_000m },
                },
                StartingDisease = new StartingDiseaseDefinition
                {
                    Type = "ISA",
                    SiteId = "isa_kjerne",
                    ExternalPressure = 1.70,
                    SeedDays = 21,
                },
                Restrictions = new[]
                {
                    "Tvungen nedslakting i bekjempelsessonen",
                    "Smittevern mellom produksjonsområdene",
                    "Forsikringsdekning kreves for utbruddsansvar",
                    "Utbruddet må ikke spre seg til nabofeltene",
                },
                Tags = new[] { "sykdom", "isa", "krise", "biosecurity", "forsikring" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_disease"] = "true",
                    ["disease_type"] = "ISA",
                    ["mandatory_cull"] = "true",
                    ["firewalls"] = "true",
                    ["insurance"] = "true",
                    ["insurance_target"] = "isa",
                    ["biosecurity_target"] = "smoltscreen",
                    ["max_infected_sites"] = "1",
                    ["regulation_pressure"] = "true",
                },
                TimeLimitDays = 540, // ~18 months to contain and recover
                IsUnlocked = true,
            });

            // ─── 8. ALGEKATASTROFEN — Bloom mortality (Difficulty 5) ─────────
            //    Grieg Seafood lost ~1,000 tonnes of salmon in a single harmful
            //    algal bloom incident (2025) off Nordland. Survival hinges on
            //    early warning, mitigation (bubble curtain/O2 diffuser) and
            //    insurance against catastrophic mortality.
            Register(new ScenarioDefinition
            {
                Id = "algekatastrofe",
                Name = "Algekatastrofen",
                Story =
                    "Høsten rammet en stor vestlandsoppdretter hardt: en massiv, skadelig algeoppblomstring drepte rundt " +
                    "1 000 tonn laks i ett enkelt hendelsesforløp. Alger tapper oksygenet og skader gjellene. " +
                    "Tidlig varsling fra sensorer, umiddelbar mitigering (boblegardin og O2-tilførsel) og " +
                    "forsikring er forskjellen mellom overlevelse og total tap.",
                Description =
                    "1 000 t dødelighet ved én algehend. Tidlig varsling, mitigerende tiltak og forsikring avgjør.",
                Objective =
                    "Installer mitigerende tiltak (O2-diffuser/boblegardin) på alle utsatte anlegg og overlev alge-vinduet med katastrofeforsikring.",
                StartingCapitalNOK = 25_000_000m,
                StartingCash = 25_000_000m,
                StartingRegion = ProductionRegion.Nordland,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "alge_sor",   Name = "Algeutsatt sør", Zone = "alge_sor", ProductionArea = 9,  Pens = 8, SmoltCount = 14_000, SmoltQuality = "standard" },
                    new() { Id = "alge_midt",  Name = "Algeutsatt midt", Zone = "alge_midt", ProductionArea = 9, Pens = 8, SmoltCount = 14_000, SmoltQuality = "standard" },
                    new() { Id = "alge_indre", Name = "Innlandsbeskyttet", Zone = "alge_indre", ProductionArea = 9, Pens = 5, SmoltCount = 9_000, SmoltQuality = "robust" },
                },
                StartingResearch = new[] { "sensor-networks", "predictive-analytics" },
                StartingBuildings = new[] { "ai_feeding" },
                StartingMitigations = new List<StartingMitigationDefinition>
                {
                    new() { SiteId = "alge_indre", Kind = "diffuser" },
                    new() { SiteId = "alge_midt",  Kind = "pumping" },
                },
                StartingBlooms = new List<StartingBloomDefinition>
                {
                    new() { SiteId = "alge_sor", Kind = "algae", Severity = 0.60, DurationDays = 28 },
                },
                StartingInsurance = new List<StartingInsuranceDefinition>
                {
                    new() { Kind = "Bloom", Premium = 212_500m, Coverage = 0.85, Deductible = 1_000_000m },
                },
                Restrictions = new[]
                {
                    "Tidligvarslingssystem for alger påkrevd",
                    "Mitigerende tiltak (boblegardin/O2) må være operative før alge-vinduet (dag 200–300)",
                    "Katastrofeforsikring kreves",
                },
                Tags = new[] { "alger", "bloom", "katastrofe", "forsikring", "mitigering" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_bloom"] = "true",
                    ["early_warning"] = "true",
                    ["mitigation"] = "true",
                    ["mitigation_target"] = "o2_diffuser",
                    ["bloom_window_start_doy"] = "200",
                    ["bloom_window_end_doy"] = "300",
                    ["insurance"] = "true",
                    ["insurance_target"] = "bloom",
                },
                TimeLimitDays = 365, // survive one full bloom season
                IsUnlocked = true,
            });

            // ─── 9. RESSURSRENTE-OVERGANEN — 25 % cash-flow tax (Difficulty 3) ─
            //    Norwegian resource-rent ("grunnrenteskatt") transition: 25 %
            //    effective cash-flow tax with a 70 MNOK standard deduction
            //    (grunnfradrag). Reshapes profitability; scale or restructure.
            Register(new ScenarioDefinition
            {
                Id = "ressursrente",
                Name = "Ressursrente-overgangen",
                Story =
                    "Den nye grunnrentebeskatningen er vedtatt: 25 % kontantstrømskatt på havbruk, " +
                    "med et standardfradrag (grunnfradrag) på 70 millioner kroner. " +
                    "Små og mellomstore aktører kan vokse skattefritt under fradraget, men hver krone " +
                    "over 70 MNOK blir dramatisk mindre verdt. Omstrukturer, invester i verdikjeden og " +
                    "lever lønnsomt i det nye skattelandskapet.",
                Description =
                    "25 % grunnrenteskatt med 70 MNOK grunnfradrag. Restrukturer for lønnsomhet under det nye regimet.",
                Objective =
                    "Lever med 25 % grunnrenteskatt (70 MNOK grunnfradrag) og nå 25 MNOK i overskudd gjennom verdikjede-investeringer.",
                StartingCapitalNOK = 60_000_000m,
                StartingCash = 60_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 3,
                WinCondition = WinCondition.Profit,
                EnableResourceRentTax = true,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Id = "vest_1", Name = "Hovedanlegg vest", Zone = "vest_1", ProductionArea = 4, Pens = 8,  SmoltCount = 15_000, SmoltQuality = "postsmolt" },
                    new() { Id = "vest_2", Name = "Vekstanlegg nord", Zone = "vest_2", ProductionArea = 4, Pens = 8,  SmoltCount = 15_000, SmoltQuality = "postsmolt" },
                    new() { Id = "vest_3", Name = "Utvidelse sør",    Zone = "vest_3", ProductionArea = 4, Pens = 6,  SmoltCount = 12_000, SmoltQuality = "robust" },
                },
                StartingResearch = new[] { "international-export", "value-added-processing" },
                StartingBuildings = new[] { "processing_plant", "feed_mill" },
                StartingInsurance = new List<StartingInsuranceDefinition>
                {
                    new() { Kind = "Mortality", Premium = 120_000m, Coverage = 0.70, Deductible = 250_000m },
                },
                Restrictions = new[]
                {
                    "25 % grunnrenteskatt på kontantstrøm",
                    "70 MNOK standardfradrag (grunnfradrag) deles på tvers av virksomheten",
                    "Overskudd over grunnfradraget beskattes kvartalsvis",
                },
                Tags = new[] { "skatt", "grunnrente", "finans", "profit", "kontantstrøm" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["resource_rent_tax"] = "0.25",
                    ["standard_deduction"] = "70000000",
                    // Equity target = 60M starting capital + the 25M profit the
                    // objective asks for (25M won on day 1, review F1005).
                    ["profit_target"] = "85000000",
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });
        }

        public void RegisterScenarios(IEnumerable<ScenarioDefinition> scenarios)
        {
            if (scenarios == null) return;
            foreach (var scenario in scenarios)
            {
                _scenarios[scenario.Id] = scenario;
            }
        }

        /// <summary>
        /// Register mod scenarios. Converts ModScenario data to ScenarioDefinition
        /// and adds them to the scenario registry. Mod scenarios never overwrite
        /// core scenarios (same ID = skip).
        /// </summary>
        public void RegisterModScenarios(IEnumerable<ModScenario> modScenarios)
        {
            if (modScenarios == null) return;
            foreach (var mod in modScenarios)
            {
                if (_scenarios.ContainsKey(mod.Id))
                    continue; // Core scenario with same ID exists — skip

                _scenarios[mod.Id] = new ScenarioDefinition
                {
                    Id = mod.Id,
                    Name = mod.Name,
                    Description = mod.Description,
                    StartingCapitalNOK = mod.InitialBalanceNOK,
                    StartingRegion = ParseRegion(mod.StartingRegion),
                    Difficulty = mod.Difficulty,
                    WinCondition = ParseWinCondition(mod.WinCondition),
                    IsUnlocked = true,
                };
            }
        }

        private static ProductionRegion ParseRegion(string raw) =>
            Enum.TryParse<ProductionRegion>(raw, ignoreCase: true, out var r) ? r : ProductionRegion.Vestland;

        private static WinCondition ParseWinCondition(string raw) =>
            Enum.TryParse<WinCondition>(raw, ignoreCase: true, out var w) ? w : WinCondition.Profit;

        private void Register(ScenarioDefinition scenario) => _scenarios[scenario.Id] = scenario;
    }

    /// <summary>
    /// Accepts difficulty as a 1–5 number or a name ("Easy"→1,
    /// "Medium"→3, "Hard"→5). Unknown values fall back to 3 (medium).
    /// </summary>
    public class LenientDifficultyConverter : System.Text.Json.Serialization.JsonConverter<int>
    {
        public override int Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt32(out var n))
                return Math.Clamp(n, 1, 5);
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                var s = (reader.GetString() ?? "").Trim().ToLowerInvariant();
                if (int.TryParse(s, out var parsed))
                    return Math.Clamp(parsed, 1, 5);
                return s switch
                {
                    "easy" => 1,
                    "medium" or "normal" => 3,
                    "hard" => 5,
                    _ => 3,
                };
            }
            return 3;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
