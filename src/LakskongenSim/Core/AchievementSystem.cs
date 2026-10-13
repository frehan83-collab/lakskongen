using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Achievement/milestone tracking system for player progression.
    /// 50+ achievements spanning beginner to mastery tiers.
    /// Achievements unlock notifications, badges, and bonuses.
    /// NOTE: Uses GameState.Balance for checks (to be migrated to FinanceManager in future)
    /// </summary>
    #pragma warning disable CS0618 // Type or member is obsolete
    public class AchievementSystem
    {
        private readonly GameState _state;
        private readonly Finance.FinanceManager? _financeManager;
        private readonly Dictionary<string, Achievement> _achievements = new();
        private readonly List<Achievement> _lockedAchievements = new();

        public event Action<Achievement>? OnAchievementUnlocked;

        /// <summary>Raised when a reward was paid to the FinanceManager (the engine books it).</summary>

        public event Action<decimal>? OnRewardPaid;

        public AchievementSystem(GameState state, Finance.FinanceManager? financeManager = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
            RegisterAllAchievements();
            
            _lockedAchievements.AddRange(_achievements.Values.Where(a => !_state.UnlockedAchievementIds.Contains(a.Id)));
        }

        /// <summary>
        /// Drop already-unlocked achievements from the locked list after
        /// AdoptSavedState copied the saved progress. The constructor builds
        /// the list from a fresh state; without this, CheckAchievements
        /// re-unlocks finished achievements and RE-PAYS their rewards on the
        /// first post-load tick (Phase 1 wave 6).
        /// </summary>
        internal void ResyncFromState()
        {
            _lockedAchievements.RemoveAll(a => _state.UnlockedAchievementIds.Contains(a.Id));
        }

        /// <summary>Check all achievements and unlock any that meet conditions.</summary>
        public void CheckAchievements()
        {
            for (int i = _lockedAchievements.Count - 1; i >= 0; i--)
            {
                var achievement = _lockedAchievements[i];
                if (achievement.Condition == null) continue;

                if (achievement.Condition(_state))
                {
                    UnlockAchievement(achievement);
                    _lockedAchievements.RemoveAt(i);
                }
            }
        }

        private void UnlockAchievement(Achievement achievement)
        {
            _state.UnlockedAchievementIds.Add(achievement.Id);
            SimLog.Info($"🏆 Achievement Unlocked: {achievement.Name} — {achievement.Description}");
            if (achievement.RewardNOK > 0)
            {
                if (_financeManager != null)
                {
                    _financeManager.AddRevenue(achievement.RewardNOK);
                    OnRewardPaid?.Invoke(achievement.RewardNOK);
                }
                else
                {
                    _state.Balance += achievement.RewardNOK; // Legacy fallback
                }
                SimLog.Info($"    Reward: {achievement.RewardNOK:N0} NOK");
            }
            OnAchievementUnlocked?.Invoke(achievement);
        }

        public bool IsUnlocked(string achievementId) => _state.UnlockedAchievementIds.Contains(achievementId);
        public int UnlockedCount => _state.UnlockedAchievementIds.Count;
        public int TotalCount => _achievements.Count;
        public double CompletionPercent => (_state.UnlockedAchievementIds.Count / (double)_achievements.Count) * 100.0;

        public List<Achievement> GetUnlocked() => _achievements.Values
            .Where(a => _state.UnlockedAchievementIds.Contains(a.Id))
            .OrderBy(a => a.Tier)
            .ToList();

        public List<Achievement> GetLocked() => _achievements.Values
            .Where(a => !_state.UnlockedAchievementIds.Contains(a.Id))
            .OrderBy(a => a.Tier)
            .ToList();

        // ══════════════════════════════════════════════════════════════════
        // ACHIEVEMENT DEFINITIONS (loaded from JSON)
        // ══════════════════════════════════════════════════════════════════

        private void RegisterAllAchievements()
        {
            string? dir = SimulationEngine.ResolveDataDirectory("achievements_config.json");
            if (dir == null)
            {
                Console.Error.WriteLine("ERROR: achievements_config.json not found (searched godot/data candidates)");
                return;
            }
            var jsonPath = Path.Combine(dir, "achievements_config.json");

            var jsonString = File.ReadAllText(jsonPath);
            var achievementDefinitions = JsonSerializer.Deserialize<List<AchievementDefinition>>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (achievementDefinitions == null)
            {
                Console.Error.WriteLine("ERROR: Failed to deserialize achievement definitions.");
                return;
            }

            foreach (var def in achievementDefinitions)
            {
                if (!AchievementConditionMap.Conditions.TryGetValue(def.ConditionKey, out var conditionFunc))
                {
                    Console.Error.WriteLine($"WARNING: Condition key '{def.ConditionKey}' not found for achievement '{def.Id}'");
                    continue;
                }

                if (!Enum.TryParse<AchievementTier>(def.Tier, true, out var tierEnum))
                {
                    // Config carries no tier field — default silently; warn
                    // only for genuinely unrecognized tier strings.
                    if (!string.IsNullOrWhiteSpace(def.Tier))
                        Console.Error.WriteLine($"WARNING: Invalid tier '{def.Tier}' for achievement '{def.Id}'. Defaulting to Beginner.");
                    tierEnum = AchievementTier.Beginner;
                }

                Register(new Achievement
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    Tier = tierEnum,
                    RewardNOK = def.RewardNOK,
                    Icon = def.Icon,
                    Category = def.Category,
                    Hidden = def.Hidden,
                    Condition = conditionFunc
                });
            }
        }

        private void Register(Achievement achievement) => _achievements[achievement.Id] = achievement;
    }

    public enum AchievementTier
    {
        Beginner,     // 0-100 days
        Novice,       // Year 1-2
        Intermediate, // Year 2-5
        Advanced,     // Year 5-10
        Master,       // 10+ years
        Hidden        // Secret achievements
    }

    // New class for deserializing from JSON
    public class AchievementDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = ""; // New
        public string Category { get; set; } = ""; // New
        public bool Hidden { get; set; } = false; // New
        public string Tier { get; set; } = ""; // As string for deserialization
        [JsonPropertyName("reward_nok")]
        public decimal RewardNOK { get; set; }
        // Config file uses snake_case "condition_key" — map explicitly
        // (case-insensitive matching alone does not bridge underscores,
        // which left every ConditionKey empty and all lookups failing).
        [JsonPropertyName("condition_key")]
        public string ConditionKey { get; set; } = ""; // Key to lookup Func<GameState, bool>
    }

    public class Achievement
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = ""; // New
        public string Category { get; set; } = ""; // New
        public bool Hidden { get; set; } = false; // New
        public AchievementTier Tier { get; set; }
        public decimal RewardNOK { get; set; }
        public Func<GameState, bool>? Condition { get; set; }
    }

    // Static class to map condition keys to actual Func<GameState, bool> delegates
    public static class AchievementConditionMap
    {
        /// <summary>Cash minus outstanding loan principal (a loan is not wealth).</summary>
        private static decimal NetWorth(GameState gs)
            => gs.Balance - gs.ActiveLoans.Sum(l => System.Math.Max(0m, l.Principal));

        public static readonly Dictionary<string, Func<GameState, bool>> Conditions = new()
        {
            {"HasAnyPopulations", gs => gs.Populations.Any()},
            {"SurvivedYearOne", gs => gs.Day >= 365 && gs.Balance > 0},
            {"HasHarvestedCohort", gs => gs.Populations.Any(p => p.Cohorts.Any(c => c.Stage == Data.LifeStage.Harvest))},
            // Positive cash flow over the last quarter (90 daily snapshots).
            {"ProfitableQuarter", gs =>
                gs.Day >= 90 && gs.DailySnapshots.Count > 90 &&
                gs.DailySnapshots[^1].BalanceNok > gs.DailySnapshots[^91].BalanceNok},
            // Revenue achievements measure cumulative SALES, not cash: cash
            // includes the starting capital, so they all fired on day 1 (F1006).
            {"MillionRevenue", gs => gs.TotalSalesRevenueNok >= 1_000_000m},
            {"Harvested100KFish", gs => gs.Populations.Sum(p => p.Cohorts.Where(c => c.Stage == Data.LifeStage.Harvest).Sum(c => c.Count)) >= 100_000},
            {"FCRExcellence", gs =>
                {
                    if (gs.Day < 365 || gs.DailySnapshots.Count < 365) return false;
                    
                    int validDays = 0;
                    for (int i = gs.DailySnapshots.Count - 1; i >= 0; i--)
                    {
                        var s = gs.DailySnapshots[i];
                        if (s.Day < gs.Day - 365) break;
                        
                        if (s.FCR <= 0 || s.FCR >= 1.15) return false;
                        validDays++;
                    }
                    return validDays >= 300;
                }
            },
            {"DiseaseFreeYear", gs => 
                {
                    if (gs.Day < 365) return false;
                    for (int i = gs.EventLog.Count - 1; i >= 0; i--)
                    {
                        var e = gs.EventLog[i];
                        if (e.Day < gs.Day - 365) break;
                        if (e.Category == Events.EventCategory.Disease) return false;
                    }
                    return true;
                }
            },
            {"ASCCertified", gs => gs.HasASCCertification},
            {"FiveMillionRevenue", gs => gs.TotalSalesRevenueNok >= 5_000_000m},
            {"GeneticSuperiority", gs => gs.BestGrowthBreedingValue >= 2.0},
            {"CrisisSurvivor", gs => 
                {
                    if (gs.Balance <= 0) return false;
                    for (int i = gs.EventLog.Count - 1; i >= 0; i--)
                    {
                        if (gs.EventLog[i].BalanceChange < -500_000m) return true;
                    }
                    return false;
                }
            },
            {"MultiSiteOperator", gs => gs.SeaSites.Count >= 3},
            {"SustainabilityLeader", gs =>
                {
                    if (gs.DailySnapshots.Count == 0) return false;
                    var latest = gs.DailySnapshots.Last();
                    return latest.SustainabilityScore >= 95;
                }
            },
            {"TenMillionRevenue", gs => gs.TotalSalesRevenueNok >= 10_000_000m},
            {"VerticalIntegration", gs => gs.OwnsFeedMill && gs.OwnsHatchery && gs.OwnsProcessingPlant && gs.SeaSites.Count > 0},
            {"AcquisitionSpree", gs => gs.AcquisitionCount >= 3},
            {"GlobalExporter", gs => gs.ExportMarkets.Contains("EU") &&
                                  gs.ExportMarkets.Contains("USA") &&
                                  gs.ExportMarkets.Contains("China") &&
                                  gs.ExportMarkets.Contains("Japan") &&
                                  gs.ExportMarkets.Contains("UK") &&
                                  gs.ExportMarkets.Contains("MiddleEast")
            },
            {"HundredMillionNetWorth", gs => NetWorth(gs) >= 100_000_000m},
            {"PerfectFCR", gs => gs.BestFCR > 0 && gs.BestFCR <= 1.05},
            {"ZeroEscapes", gs => 
                {
                    if (gs.Day < 1825) return false;
                    for (int i = gs.EventLog.Count - 1; i >= 0; i--)
                    {
                        var e = gs.EventLog[i];
                        if (e.Day < gs.Day - 1825) break;
                        if (e.Id == "escape-penalty") return false;
                    }
                    return true;
                }
            },
            {"DiseaseResistance", gs => gs.BestDiseaseResistanceBreedingValue >= 0.5},
            {"IndustryLeader", gs => gs.Balance >= 500_000_000m && gs.SeaSites.Count >= 20},
            {"PerfectStorm", gs =>
                {
                    bool hasIsa = false;
                    bool hasCrash = false;
                    bool hasStorm = false;
                    
                    for (int i = gs.EventLog.Count - 1; i >= 0; i--)
                    {
                        var e = gs.EventLog[i];
                        if (e.Day < gs.Day - 365) break;
                        
                        if (e.Id == "isa-outbreak") hasIsa = true;
                        else if (e.Id == "oversupply-crash") hasCrash = true;
                        else if (e.Id == "storm-damage") hasStorm = true;
                        
                        if (hasIsa && hasCrash && hasStorm) return true;
                    }
                    return false;
                }
            },
            // 1M NOK profit = net worth (cash minus loan principal) up 1M on the opening balance.
            {"SpeedrunMillionaire", gs => NetWorth(gs) - gs.OpeningBalanceNok >= 1_000_000m && gs.Day < 500},
            {"BankruptcyRecovery", gs => gs.MinimumBalanceEver < 0 && gs.Balance >= 1_000_000m},

            // ── Wave 7 depth-system achievements ──────────────────────────────
            // Self-insured: all four catastrophe perils have paid out at least
            // once, which implies full cover was carried across mortality,
            // bloom, storm and ISA risk. GameState does not expose live policy
            // state, so the payout ledger is the observable proxy.
            {"AllCatastrophePoliciesActive", gs =>
                {
                    string[] kinds = { "mortality", "bloom", "storm", "isa" };
                    foreach (var kind in kinds)
                    {
                        bool covered = false;
                        for (int i = gs.EventLog.Count - 1; i >= 0; i--)
                        {
                            if (gs.EventLog[i].Id == $"insurance-{kind}") { covered = true; break; }
                        }
                        if (!covered) return false;
                    }
                    return true;
                }
            },
            // Closed loop: a site was upgraded into closed containment from a
            // red traffic-light zone and claimed the environmental-flexibility
            // biomass credit.
            {"ClosedLoopUpgrade", gs =>
                gs.SeaSites.Any(s => s.IsClosedContainment && s.RedZoneCreditApplied)
            },
            // Genomicist: broodstock has advanced at least two generations,
            // i.e. genomic selection has been applied across breeding cycles.
            {"GenomicSelectionCompleted", gs => gs.Broodstock.Any(g => g.Generation >= 2)},
            // Weatherproof: mitigated bloom event survived while solvent.
            {"WeatherproofBloom", gs =>
                gs.SeaSites.Any(s => s.HasBubbleCurtain || s.HasO2Diffuser) &&
                gs.EventLog.Any(e => e.Id == "algal-bloom-toxic" || e.Id == "weather-algal-bloom" || e.Id == "algal-bloom") &&
                gs.DailySnapshots.Count > 0 && gs.DailySnapshots.Last().BalanceNok > 0.0
            },
            // Tax planner: profitable under the resource-rent transition campaign.
            {"ResourceRentProfitable", gs =>
                gs.ActiveScenarioId != null &&
                gs.ActiveScenarioId.ToLowerInvariant().Contains("ressursrente") &&
                gs.DailySnapshots.Count > 0 && gs.DailySnapshots.Last().BalanceNok > 0.0
            },
            // Precision: world-class feed conversion (P6).
            {"PrecisionFCR", gs => gs.BestFCR > 0.0 && gs.BestFCR <= 1.12},
            // SuperSmolt: a post-smolt facility is operating.
            {"SuperSmoltStocked", gs => gs.PostSmoltFacilities.Count > 0},
            // Bloom Ready: at least one site has bloom countermeasures installed.
            {"BloomMitigationInstalled", gs =>
                gs.SeaSites.Any(s => s.HasBubbleCurtain || s.HasO2Diffuser)
            }
        };
    }
    #pragma warning restore CS0618
}
