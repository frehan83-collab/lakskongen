using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Core
{
    public enum ResearchBranch
    {
        Biology,
        Technology,
        Sustainability,
        Processing,
        Expansion,
        Genetics,
        Automation,
        Environment,
        Health,
        Nutrition
    }

    /// <summary>
    /// A single node in the tech tree. Immutable definition plus mutable unlock state.
    /// </summary>
    public class ResearchNode
    {
        public string Id          { get; init; } = "";
        public string Name        { get; init; } = "";
        public string Description { get; init; } = "";
        public ResearchBranch Branch { get; init; }
        public int     Tier          { get; init; }
        public decimal CostNOK       { get; init; }
        public int     ResearchDays  { get; init; }
        public List<string>              Prerequisites { get; init; } = new();
        public bool                      Unlocked      { get; set; }
        public Dictionary<string, double> Effects      { get; init; } = new();

        /// <summary>
        /// F2-04: the farm-type variants this node opens (farm-type catalogue
        /// ids, <c>Farm.FarmTypeCatalog</c>); the node is one of the variant's
        /// unlock gates. Empty for a node that opens no farm type.
        /// </summary>
        public List<string> Unlocks { get; init; } = new();

        /// <summary>
        /// F2-04: the unit kinds the node's effects reach (<see cref="ResearchScopes"/>:
        /// "sea", "offshore", "land"). Empty = every unit. A scoped node may only
        /// carry effects a unit reads (<see cref="ResearchEffectKeys.UnitScoped"/>).
        /// </summary>
        public List<string> AppliesTo { get; init; } = new();

        /// <summary>True when the node's effects reach a unit of <paramref name="scope"/> (an unscoped node reaches every unit).</summary>
        public bool AppliesToScope(string scope) => AppliesTo.Count == 0 || AppliesTo.Contains(scope);
    }

    /// <summary>
    /// Tech tree with 25 research nodes across 5 branches (the same tree ships as
    /// godot/data/research_nodes.json; ResearchTreeContractTests keeps them equal).
    /// Tracks active research progress and fires OnResearchCompleted on completion.
    /// </summary>
    public class ResearchSystem
    {
        private readonly Dictionary<string, ResearchNode> _nodes = new();

        public IReadOnlyDictionary<string, ResearchNode> Nodes => _nodes;

        /// <summary>ID of the research currently in progress (null if idle).</summary>
        public string? ActiveResearchId { get; private set; }

        /// <summary>Days elapsed on the active research.</summary>
        public int ResearchProgress { get; private set; }

        /// <summary>Fires when a research node completes.</summary>
        public event Action<ResearchNode>? OnResearchCompleted;

        public ResearchSystem()
        {
            InitializeNodes();
        }

        // ─── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Start researching a node. Validates prerequisites and idle state.
        /// Does NOT deduct cost — caller is responsible for balance check and deduction.
        /// </summary>
        public void StartResearch(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
                throw new ArgumentException($"Unknown research node: {nodeId}");
            if (node.Unlocked)
                throw new InvalidOperationException($"Node '{nodeId}' is already unlocked.");
            if (ActiveResearchId != null)
                throw new InvalidOperationException($"Research already in progress: {ActiveResearchId}");
            if (!ArePrerequisitesMet(nodeId))
                throw new InvalidOperationException($"Prerequisites not met for '{nodeId}'.");

            ActiveResearchId = nodeId;
            ResearchProgress = 0;
        }

        /// <summary>Cancel the active research (no cost refund).</summary>
        public void CancelResearch()
        {
            ActiveResearchId = null;
            ResearchProgress = 0;
        }

        /// <summary>Advance one simulation day. Fires OnResearchCompleted on completion.</summary>
        public void AdvanceDay()
        {
            if (ActiveResearchId == null) return;

            ResearchProgress++;

            if (_nodes.TryGetValue(ActiveResearchId, out var node) &&
                ResearchProgress >= node.ResearchDays)
            {
                node.Unlocked = true;
                var completedNode = node;
                ActiveResearchId = null;
                ResearchProgress = 0;
                OnResearchCompleted?.Invoke(completedNode);
            }
        }

        /// <summary>True if the node's prerequisites are all unlocked.</summary>
        public bool ArePrerequisitesMet(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node)) return false;
            return node.Prerequisites.All(prereqId =>
                _nodes.TryGetValue(prereqId, out var prereq) && prereq.Unlocked);
        }

        /// <summary>True if the node can be started (prereqs met, not unlocked, not active).</summary>
        public bool CanResearch(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node)) return false;
            return !node.Unlocked && ActiveResearchId == null && ArePrerequisitesMet(nodeId);
        }

        /// <summary>All nodes with prerequisites met and not yet unlocked.</summary>
        public IEnumerable<ResearchNode> GetAvailableNodes()
            => _nodes.Values.Where(n => !n.Unlocked && ArePrerequisitesMet(n.Id));

        /// <summary>All unlocked research node IDs (for save/load serialization).</summary>
        public IEnumerable<string> GetUnlockedIds()
            => _nodes.Values.Where(n => n.Unlocked).Select(n => n.Id);

        /// <summary>
        /// Sum the unlocked effects for a given key that reach every unit
        /// (F2-04: a node scoped to some unit kinds is left out; see
        /// <see cref="GetTotalEffect(string, string)"/>).
        /// Returns 0.0 if no unlocked node contributes to that key.
        /// </summary>
        public double GetTotalEffect(string key)
            => _nodes.Values
                .Where(n => n.Unlocked && n.AppliesTo.Count == 0 && n.Effects.ContainsKey(key))
                .Sum(n => n.Effects[key]);

        /// <summary>
        /// F2-04: sum the unlocked effects for a given key that reach a unit of
        /// <paramref name="scope"/> (<see cref="ResearchScopes"/>): every unscoped
        /// node plus the nodes scoped to that kind, in the tree's order (so a
        /// game with no scoped node unlocked sums exactly as before).
        /// </summary>
        public double GetTotalEffect(string key, string scope)
            => _nodes.Values
                .Where(n => n.Unlocked && n.AppliesToScope(scope) && n.Effects.ContainsKey(key))
                .Sum(n => n.Effects[key]);

        /// <summary>F2-04: every node (locked or not) that opens the farm-type variant <paramref name="variantId"/>, in tree order.</summary>
        public IEnumerable<ResearchNode> NodesUnlocking(string variantId)
            => _nodes.Values.Where(n => n.Unlocks.Contains(variantId));

        // ─── Save/load helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Populate research nodes from external JSON data.
        /// Clears any existing nodes and replaces them with the provided list.
        /// </summary>
        /// <summary>Restore an unlocked node without triggering the completion event.</summary>
        public void ForceUnlock(string nodeId)
        {
            if (_nodes.TryGetValue(nodeId, out var node))
                node.Unlocked = true;
        }

        /// <summary>Restore in-progress research from a saved state.</summary>
        public void RestoreProgress(string nodeId, int progress)
        {
            if (_nodes.ContainsKey(nodeId))
            {
                ActiveResearchId = nodeId;
                ResearchProgress = progress;
            }
        }

        // ─── JSON population ───────────────────────────────────────────────────

        /// <summary>
        /// Replace the code-defined node definitions with nodes loaded from JSON
        /// (godot/data/research_nodes.json, which ships the same tree). Branches
        /// are kept as written — the tech-tree UI lays out every branch — and
        /// unlock state is preserved for matching ids. Effect keys the engine does
        /// not read are reported (review F1001: a whole shipped tree once carried
        /// only dead keys).
        /// </summary>
        public void PopulateFromJson(List<ResearchNode> jsonNodes)
        {
            if (jsonNodes == null || jsonNodes.Count == 0) return;

            var previousState = new Dictionary<string, bool>();
            foreach (var kvp in _nodes)
                previousState[kvp.Key] = kvp.Value.Unlocked;

            _nodes.Clear();
            foreach (var node in jsonNodes)
            {
                node.Unlocked = previousState.TryGetValue(node.Id, out var wasUnlocked) && wasUnlocked;
                ReportUnknownEffectKeys(node.Id, node.Effects?.Keys);
                foreach (var problem in ScopeProblems(node))
                    SimLog.Warn($"Research node '{node.Id}': {problem}");
                _nodes[node.Id] = node;
            }
        }

        /// <summary>Effect keys of a node that no engine system reads.</summary>
        public static IEnumerable<string> UnknownEffectKeys(IEnumerable<string>? keys)
            => (keys ?? Enumerable.Empty<string>()).Where(k => !ResearchEffectKeys.Live.Contains(k));

        /// <summary>
        /// F2-04: what is wrong with a node's scope: an unknown unit kind, or an
        /// effect a unit does not read on a scoped node (a scoped price premium
        /// would apply to nothing).
        /// </summary>
        public static IEnumerable<string> ScopeProblems(ResearchNode node)
        {
            foreach (var scope in node.AppliesTo ?? new List<string>())
                if (!ResearchScopes.All.Contains(scope))
                    yield return $"unknown unit kind '{scope}' in appliesTo";
            if (node.AppliesTo is { Count: > 0 })
                foreach (var key in node.Effects?.Keys ?? Enumerable.Empty<string>())
                    if (!ResearchEffectKeys.UnitScoped.Contains(key))
                        yield return $"effect '{key}' cannot be scoped to a unit kind";
        }

        private static void ReportUnknownEffectKeys(string nodeId, IEnumerable<string>? keys)
        {
            foreach (var key in UnknownEffectKeys(keys))
                SimLog.Warn($"Research node '{nodeId}': effect key '{key}' has no effect (not in ResearchEffectKeys.Live)");
        }

        // ─── Mod node integration (Phase 4.6) ─────────────────────────────────

        /// <summary>
        /// Add mod research nodes to the tech tree. Mod nodes are additive —
        /// they never overwrite core nodes. If a mod node ID collides with a
        /// core node ID, the mod node is skipped (core wins).
        /// </summary>
        public void AddModNodes(List<ModResearchNode> modNodes)
        {
            if (modNodes == null || modNodes.Count == 0) return;


            foreach (var mod in modNodes)
            {
                if (_nodes.ContainsKey(mod.Id))
                {
                    // Core node with same ID exists — skip mod node
                    continue;
                }

                // Any of the schema's ten branch names; the tech-tree UI lays out all of them.
                var branch = ResearchBranch.Biology;
                if (!string.IsNullOrEmpty(mod.Branch)
                    && Enum.TryParse<ResearchBranch>(mod.Branch, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed))
                    branch = parsed;
                ReportUnknownEffectKeys(mod.Id, mod.Effects.Keys);

                _nodes[mod.Id] = new ResearchNode
                {
                    Id = mod.Id,
                    Name = mod.Name,
                    Description = mod.Description,
                    Branch = branch,
                    Tier = mod.Tier,
                    CostNOK = mod.CostNOK,
                    ResearchDays = mod.ResearchDays,
                    Prerequisites = new List<string>(mod.Prerequisites),
                    Effects = new Dictionary<string, double>(mod.Effects),
                    Unlocked = false
                };
            }
        }

        // ─── Node definitions ──────────────────────────────────────────────────

        private void InitializeNodes()
        {
            // ═════════════════════════════════════════════════════════════════
            // BRANCH 1: BIOLOGY
            // Selective breeding, genetics, and life-stage optimization
            // ═════════════════════════════════════════════════════════════════

            Add(new ResearchNode
            {
                Id = "selective-breeding",
                Name = "Selective Breeding Programme",
                Description = "Establish a systematic genetic selection programme to identify and propagate fast-growing broodstock. Improves baseline growth rates across all cohorts.",
                Branch = ResearchBranch.Biology,
                Tier = 1,
                CostNOK = 800_000m,
                ResearchDays = 45,
                Prerequisites = new(),
                Effects = new() { ["growth_rate_bonus"] = 0.04 }
            });

            Add(new ResearchNode
            {
                Id = "disease-resistance",
                Name = "Disease-Resistant Genetics",
                Description = "Use genomic selection tools (GEBV) to breed salmon with heritable resistance to ISA, PD, and CMS. Reduces disease outbreak probability and severity.",
                Branch = ResearchBranch.Biology,
                Tier = 2,
                CostNOK = 1_800_000m,
                ResearchDays = 75,
                Prerequisites = new() { "selective-breeding" },
                Effects = new() { ["disease_resistance_bonus"] = 0.12, ["mortality_reduction"] = 0.02 }
            });

            Add(new ResearchNode
            {
                Id = "feed-efficiency-genetics",
                Name = "Feed Efficiency Genetics",
                Description = "Select broodstock traits linked to superior feed conversion. Lower FCR means less feed cost per kg of fish produced and reduced environmental loading.",
                Branch = ResearchBranch.Biology,
                Tier = 2,
                CostNOK = 1_500_000m,
                ResearchDays = 60,
                Prerequisites = new() { "selective-breeding" },
                Effects = new() { ["fcr_reduction"] = 0.06 }
            });

            Add(new ResearchNode
            {
                Id = "growth-optimization",
                Name = "Growth Optimisation Protocols",
                Description = "Fine-tuned photoperiod, temperature, and feeding window protocols derived from smoltification research. Accelerates seawater growth phase by controlling TGC predictively.",
                Branch = ResearchBranch.Biology,
                Tier = 3,
                CostNOK = 3_500_000m,
                ResearchDays = 100,
                Prerequisites = new() { "disease-resistance" },
                Effects = new() { ["growth_rate_bonus"] = 0.06, ["mortality_reduction"] = 0.02 }
            });

            Add(new ResearchNode
            {
                Id = "smolt-excellence",
                Name = "Smolt Excellence Programme",
                Description = "Integrated hatchery and smolt quality programme combining genetics, nutrition, and light management. Produces grade-A smolts with superior seawater adaptation and reduced post-smolt mortality.",
                Branch = ResearchBranch.Biology,
                Tier = 4,
                CostNOK = 6_000_000m,
                ResearchDays = 150,
                Prerequisites = new() { "growth-optimization", "feed-efficiency-genetics" },
                Effects = new() { ["growth_rate_bonus"] = 0.04, ["fcr_reduction"] = 0.03, ["mortality_reduction"] = 0.03 }
            });

            // ═════════════════════════════════════════════════════════════════
            // BRANCH 2: TECHNOLOGY
            // Digital and mechanical systems for farm operations
            // ═════════════════════════════════════════════════════════════════

            Add(new ResearchNode
            {
                Id = "automated-feeding",
                Name = "Automated Feeding Systems",
                Description = "Replace manual feed distribution with demand-driven automated pellet dispensers linked to underwater appetite cameras. Eliminates overfeeding and reduces waste.",
                Branch = ResearchBranch.Technology,
                Tier = 1,
                CostNOK = 700_000m,
                ResearchDays = 40,
                Prerequisites = new(),
                Effects = new() { ["fcr_reduction"] = 0.05, ["feed_waste_reduction"] = 0.12 }
            });

            Add(new ResearchNode
            {
                Id = "sensor-networks",
                Name = "Sensor Networks",
                Description = "Deploy a mesh of IoT buoys and in-pen sensors monitoring dissolved oxygen, salinity, pH, temperature, and current in real time. Enables early warning on water quality anomalies.",
                Branch = ResearchBranch.Technology,
                Tier = 2,
                CostNOK = 2_000_000m,
                ResearchDays = 70,
                Prerequisites = new() { "automated-feeding" },
                Effects = new() { ["mortality_reduction"] = 0.02, ["sustainability_score_bonus"] = 3 }
            });

            Add(new ResearchNode
            {
                Id = "underwater-drones",
                Name = "Underwater ROV Drones",
                Description = "Remotely operated vehicles for pen inspection, net integrity checks, and early sea-lice counting. Reduces diving labour costs and gives daily biomass estimates via stereo cameras.",
                Branch = ResearchBranch.Technology,
                Tier = 3,
                CostNOK = 4_500_000m,
                ResearchDays = 110,
                Prerequisites = new() { "sensor-networks" },
                Effects = new() { [ResearchEffectKeys.DiseaseResistanceBonus] = 0.10, [ResearchEffectKeys.LiceReduction] = 0.12 }
            });

            Add(new ResearchNode
            {
                Id = "ai-monitoring",
                Name = "AI Health Monitoring",
                Description = "Computer-vision models trained on millions of salmon images detect fin damage, skin lesions, cataract, and abnormal behaviour patterns. Provides daily welfare scores per pen.",
                Branch = ResearchBranch.Technology,
                Tier = 4,
                CostNOK = 7_000_000m,
                ResearchDays = 160,
                Prerequisites = new() { "underwater-drones" },
                Effects = new() { ["disease_resistance_bonus"] = 0.08, ["mortality_reduction"] = 0.04 }
            });

            Add(new ResearchNode
            {
                Id = "predictive-analytics",
                Name = "Predictive Analytics Platform",
                Description = "Combines multi-year sensor data, weather forecasts, genomics, and market signals into a unified ML platform. Predicts disease outbreaks 14 days ahead, optimises harvest windows, and recommends feed adjustments.",
                Branch = ResearchBranch.Technology,
                Tier = 5,
                CostNOK = 12_000_000m,
                ResearchDays = 270,
                Prerequisites = new() { "ai-monitoring" },
                Effects = new() { ["mortality_reduction"] = 0.05, ["disease_resistance_bonus"] = 0.10, ["growth_rate_bonus"] = 0.03 }
            });

            // ═════════════════════════════════════════════════════════════════
            // BRANCH 3: SUSTAINABILITY
            // Environmental certifications and circular operations
            // ═════════════════════════════════════════════════════════════════

            Add(new ResearchNode
            {
                Id = "organic-certification",
                Name = "Organic Certification",
                Description = "Achieve organic aquaculture certification. Requires lower stocking density, no synthetic additives, and higher welfare standards. Commands a significant retail price premium.",
                Branch = ResearchBranch.Sustainability,
                Tier = 1,
                CostNOK = 600_000m,
                ResearchDays = 50,
                Prerequisites = new(),
                Effects = new() { ["market_price_bonus"] = 0.08, ["sustainability_score_bonus"] = 8 }
            });

            Add(new ResearchNode
            {
                Id = "renewable-energy",
                Name = "Renewable Energy Integration",
                Description = "Install offshore wind turbines, shore-side solar arrays, and tidal current generators to power site operations. Reduces diesel dependency and Scope 2 emissions.",
                Branch = ResearchBranch.Sustainability,
                Tier = 2,
                CostNOK = 2_200_000m,
                ResearchDays = 80,
                Prerequisites = new() { "organic-certification" },
                Effects = new() { ["sustainability_score_bonus"] = 6, ["carbon_reduction"] = 0.22 }
            });

            Add(new ResearchNode
            {
                Id = "carbon-capture",
                Name = "Carbon Capture via Kelp Farming",
                Description = "Integrate kelp and mussel cultivation around sea pens. Kelp sequesters CO₂, mussels filter particulates — creating a natural bioremediation buffer and saleable co-products.",
                Branch = ResearchBranch.Sustainability,
                Tier = 2,
                CostNOK = 1_800_000m,
                ResearchDays = 75,
                Prerequisites = new() { "organic-certification" },
                Effects = new() { ["sustainability_score_bonus"] = 8, ["carbon_reduction"] = 0.28 }
            });

            Add(new ResearchNode
            {
                Id = "circular-economy",
                Name = "Circular Economy Operations",
                Description = "Convert sludge and processing waste into biogas and fertiliser. Implement closed-loop water treatment to reuse process water. Zero-discharge certification eligible.",
                Branch = ResearchBranch.Sustainability,
                Tier = 3,
                CostNOK = 4_000_000m,
                ResearchDays = 105,
                Prerequisites = new() { "carbon-capture" },
                Effects = new() { ["sustainability_score_bonus"] = 10, ["fcr_reduction"] = 0.02 }
            });

            Add(new ResearchNode
            {
                Id = "sustainability-leader",
                Name = "Sustainability Leadership",
                Description = "Full integration of renewable energy, circular economy, and biodiversity net gain. Qualifies for Havbruk green licence renewal, ESG investor premium, and top-tier ASC score. The gold standard in Norwegian aquaculture.",
                Branch = ResearchBranch.Sustainability,
                Tier = 4,
                CostNOK = 7_500_000m,
                ResearchDays = 180,
                Prerequisites = new() { "circular-economy", "renewable-energy" },
                Effects = new() { ["market_price_bonus"] = 0.07, ["sustainability_score_bonus"] = 12 }
            });

            // ═════════════════════════════════════════════════════════════════
            // BRANCH 4: PROCESSING
            // Value-added products, cold chain, and export-ready packaging
            // ═════════════════════════════════════════════════════════════════

            Add(new ResearchNode
            {
                Id = "premium-products",
                Name = "Premium Product Lines",
                Description = "Develop branded retail products: Lakskongen Premium and Lakskongen Reserve. Differentiated sizing, trimming standards, and traceability QR codes to capture retail margin.",
                Branch = ResearchBranch.Processing,
                Tier = 1,
                CostNOK = 500_000m,
                ResearchDays = 35,
                Prerequisites = new(),
                Effects = new() { ["market_price_bonus"] = 0.05 }
            });

            Add(new ResearchNode
            {
                Id = "value-added-processing",
                Name = "Value-Added Processing",
                Description = "Expand processing into hot- and cold-smoked salmon, gravlaks, skin-on portions, and sashimi-trim blocks. Each category commands higher NOK/kg than fresh whole head-on gutted.",
                Branch = ResearchBranch.Processing,
                Tier = 2,
                CostNOK = 2_500_000m,
                ResearchDays = 85,
                Prerequisites = new() { "premium-products" },
                Effects = new() { ["market_price_bonus"] = 0.09 }
            });

            Add(new ResearchNode
            {
                Id = "cold-chain-optimization",
                Name = "Cold Chain Optimisation",
                Description = "Upgrade to HiCO₂ slurry ice packing, real-time temperature-logging cartons, and predictive logistics routing. Extends shelf life, reduces wastage, and unlocks longer-haul export lanes.",
                Branch = ResearchBranch.Processing,
                Tier = 2,
                CostNOK = 1_800_000m,
                ResearchDays = 65,
                Prerequisites = new() { "premium-products" },
                Effects = new() { ["market_price_bonus"] = 0.04, ["mortality_reduction"] = 0.01 }
            });

            Add(new ResearchNode
            {
                Id = "packaging-innovation",
                Name = "Packaging Innovation",
                Description = "Modified atmosphere packaging (MAP) with bio-based films extends fresh fillet shelf life to 21 days. Replaces polystyrene with certified-recyclable alternatives, supporting sustainability targets.",
                Branch = ResearchBranch.Processing,
                Tier = 3,
                CostNOK = 3_200_000m,
                ResearchDays = 95,
                Prerequisites = new() { "value-added-processing" },
                Effects = new() { ["market_price_bonus"] = 0.04, ["sustainability_score_bonus"] = 3 }
            });

            Add(new ResearchNode
            {
                Id = "sashimi-grade",
                Name = "Sashimi-Grade Export",
                Description = "Achieve Japanese import standards for sashimi-grade fresh salmon: strict parasite controls, guaranteed fat content ranges, and ultra-cold chain with ice slurry. Unlocks the highest-value market segment globally.",
                Branch = ResearchBranch.Processing,
                Tier = 4,
                CostNOK = 6_500_000m,
                ResearchDays = 160,
                Prerequisites = new() { "cold-chain-optimization", "packaging-innovation" },
                Effects = new() { ["market_price_bonus"] = 0.16 }
            });

            // ═════════════════════════════════════════════════════════════════
            // BRANCH 5: EXPANSION
            // New production environments and global market access
            // ═════════════════════════════════════════════════════════════════

            Add(new ResearchNode
            {
                Id = "deep-sea-farming",
                Name = "Deep-Sea Exposed Farming",
                Description = "Develop offshore semi-submersible pen systems for exposed, high-current sites. Opens the OF1-class offshore unit; its lice and disease effects apply to offshore units only.",
                Branch = ResearchBranch.Expansion,
                Tier = 1,
                CostNOK = 900_000m,
                ResearchDays = 55,
                Prerequisites = new(),
                Effects = new() { [ResearchEffectKeys.LiceReduction] = 0.10, [ResearchEffectKeys.DiseaseResistanceBonus] = 0.06 },
                // F2-04 (FARM_TYPES_SPEC §2C): the gate of the Hav family; its effects narrowed to offshore units.
                Unlocks = new() { "of1" },
                AppliesTo = new() { ResearchScopes.Offshore }
            });

            Add(new ResearchNode
            {
                Id = "land-based-ras",
                Name = "Land-Based RAS Facility",
                Description = "Recirculating Aquaculture System on shore. Zero sea lice, no escape risk, controlled environment year-round. Opens the post-smolt module and the hybrid land farm; its mortality and disease effects apply to land tanks only.",
                Branch = ResearchBranch.Expansion,
                Tier = 1,
                CostNOK = 1_200_000m,
                ResearchDays = 60,
                Prerequisites = new(),
                Effects = new() { ["mortality_reduction"] = 0.05, ["disease_resistance_bonus"] = 0.14 },
                // F2-04 (FARM_TYPES_SPEC §2C): the gate of the Land family; its effects narrowed to land tanks.
                Unlocks = new() { "post_smolt", "hfs" },
                AppliesTo = new() { ResearchScopes.Land }
            });

            Add(new ResearchNode
            {
                Id = "arctic-aquaculture",
                Name = "Arctic Aquaculture Methods",
                Description = "Cold-adapted strains and winterised equipment for sites above 70°N. Unique Arctic salmon products command premium branding. Collaboration with Tromsø and Finnmark local authorities.",
                Branch = ResearchBranch.Expansion,
                Tier = 2,
                CostNOK = 2_500_000m,
                ResearchDays = 80,
                Prerequisites = new() { "deep-sea-farming" },
                Effects = new() { ["growth_rate_bonus"] = 0.02, ["mortality_reduction"] = 0.03, ["sustainability_score_bonus"] = 4 }
            });

            Add(new ResearchNode
            {
                Id = "international-export",
                Name = "International Export Programme",
                Description = "Establish dedicated export desks for USA, Japan, and China markets with in-market sales agents, regulatory compliance teams, and currency hedging. Diversifies revenue beyond EU dependence.",
                Branch = ResearchBranch.Expansion,
                Tier = 3,
                CostNOK = 4_500_000m,
                ResearchDays = 120,
                Prerequisites = new() { "arctic-aquaculture" },
                Effects = new() { ["market_price_bonus"] = 0.07 }
            });

            Add(new ResearchNode
            {
                Id = "global-operations",
                Name = "Global Operations Platform",
                Description = "Integrate all sites, processing plants, logistics, and export markets into a single management platform. Real-time global supply optimisation, multi-currency finance, and multinational regulatory compliance.",
                Branch = ResearchBranch.Expansion,
                Tier = 4,
                CostNOK = 9_000_000m,
                ResearchDays = 200,
                Prerequisites = new() { "international-export", "land-based-ras" },
                Effects = new() { ["market_price_bonus"] = 0.09 }
            });
        }

        public void RegisterNodes(IEnumerable<ResearchNode> nodes)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                _nodes[node.Id] = node;
            }
        }

        private void Add(ResearchNode node) => _nodes[node.Id] = node;
    }
}
