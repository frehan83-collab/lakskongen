using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        public AchievementSystem(GameState state, Finance.FinanceManager? financeManager = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
            RegisterAllAchievements();
            
            _lockedAchievements.AddRange(_achievements.Values.Where(a => !_state.UnlockedAchievementIds.Contains(a.Id)));
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
            Console.WriteLine($"🏆 Achievement Unlocked: {achievement.Name}");
            Console.WriteLine($"    {achievement.Description}");
            if (achievement.RewardNOK > 0)
            {
                if (_financeManager != null)
                    _financeManager.AddRevenue(achievement.RewardNOK);
                else
                    _state.Balance += achievement.RewardNOK; // Legacy fallback
                Console.WriteLine($"    Reward: {achievement.RewardNOK:N0} NOK");
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
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game", "data", "achievements_config.json");
            if (!File.Exists(jsonPath))
            {
                // Fallback for development environment if running directly from project root.
                // Adjust this path based on your exact project structure.
                jsonPath = "salmon-sim/game/data/achievements_config.json";
                if (!File.Exists(jsonPath))
                {
                    Console.Error.WriteLine($"ERROR: achievements_config.json not found at {jsonPath}");
                    return;
                }
            }

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
        public decimal RewardNOK { get; set; }
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
        public static readonly Dictionary<string, Func<GameState, bool>> Conditions = new()
        {
            {"HasAnyPopulations", gs => gs.Populations.Any()},
            {"SurvivedYearOne", gs => gs.Day >= 365 && gs.Balance > 0},
            {"HasHarvestedCohort", gs => gs.Populations.Any(p => p.Cohorts.Any(c => c.Stage == Data.LifeStage.Harvest))},
            {"ProfitableQuarter", gs => gs.Balance > 1_000_000m && gs.Day >= 90},
            {"MillionRevenue", gs => gs.Balance >= 2_000_000m}, // Assuming Balance is cumulative revenue here
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
            {"FiveMillionRevenue", gs => gs.Balance >= 6_000_000m},
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
            {"TenMillionRevenue", gs => gs.Balance >= 11_000_000m},
            {"VerticalIntegration", gs => gs.OwnsFeedMill && gs.OwnsHatchery && gs.OwnsProcessingPlant && gs.SeaSites.Count > 0},
            {"AcquisitionSpree", gs => gs.AcquisitionCount >= 3},
            {"GlobalExporter", gs => gs.ExportMarkets.Contains("EU") &&
                                  gs.ExportMarkets.Contains("USA") &&
                                  gs.ExportMarkets.Contains("China") &&
                                  gs.ExportMarkets.Contains("Japan") &&
                                  gs.ExportMarkets.Contains("UK") &&
                                  gs.ExportMarkets.Contains("MiddleEast")
            },
            {"HundredMillionNetWorth", gs => gs.Balance >= 105_000_000m},
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
            {"SpeedrunMillionaire", gs => gs.Balance >= 2_000_000m && gs.Day < 500},
            {"BankruptcyRecovery", gs => gs.MinimumBalanceEver < 0 && gs.Balance >= 1_000_000m}
        };
    }
    #pragma warning restore CS0618
}
