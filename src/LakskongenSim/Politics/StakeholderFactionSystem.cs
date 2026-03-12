using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Politics
{
    /// <summary>
    /// Norwegian stakeholder factions in salmon farming industry
    /// </summary>
    public enum FactionType
    {
        MATTILSYNET,            // Food Safety Authority
        FISKERIDIREKTORATET,    // Fisheries Directorate
        ENVIRONMENTAL_NGOS,     // Bellona, WWF Norway, etc.
        LOCAL_COMMUNITIES,      // Local communities
        INDUSTRY_ASSOCIATION,   // Aquaculture Industry Association
        FINANCIAL_SECTOR        // Banks, insurers, investors
    }

    /// <summary>
    /// Types of sanctions that factions can impose
    /// </summary>
    public enum SanctionType
    {
        INCREASED_INSPECTIONS,  // More frequent and thorough inspections
        LICENSE_SUSPENSION,     // Temporary suspension of operating licenses
        FINE,                  // Monetary penalties
        MAB_REDUCTION,         // Maximum allowable biomass reduction
        PERMIT_DENIAL          // Denial of new permits/expansions
    }

    /// <summary>
    /// Types of bonuses factions can provide
    /// </summary>
    public enum BonusType
    {
        EXPEDITED_PERMITS,     // Faster permit processing
        FAVORABLE_LOAN_RATES,  // Better financing terms
        REDUCED_INSPECTIONS,   // Less frequent inspections
        POSITIVE_MEDIA        // Positive publicity and coverage
    }

    /// <summary>
    /// Represents a demand from a faction
    /// </summary>
    public class Demand
    {
        public int Id { get; set; }
        public FactionType FactionType { get; set; }
        public string Description { get; set; }
        public decimal CostNOK { get; set; }
        public int DurationMonths { get; set; }
        public string ConsequenceIfIgnored { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime ExpiryDate => IssuedDate.AddMonths(DurationMonths);
        public bool IsExpired => DateTime.Now > ExpiryDate;

        public Demand()
        {
            IssuedDate = DateTime.Now;
        }
    }

    /// <summary>
    /// Result of fulfilling or ignoring a demand
    /// </summary>
    public class ConsequenceResult
    {
        public bool Success { get; set; }
        public float RelationshipChange { get; set; }
        public decimal CostNOK { get; set; }
        public string Description { get; set; }
        public SanctionType? SanctionApplied { get; set; }
        public BonusType? BonusApplied { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents a stakeholder faction
    /// </summary>
    public class Faction
    {
        public FactionType Type { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        
        /// <summary>
        /// Relationship score from -1 (hostile) to +1 (excellent)
        /// </summary>
        public float RelationshipScore { get; set; }
        
        /// <summary>
        /// Political influence from 0 to 1
        /// </summary>
        public float Influence { get; set; }
        
        public List<Demand> ActiveDemands { get; set; } = new List<Demand>();
        public List<SanctionType> ActiveSanctions { get; set; } = new List<SanctionType>();
        public List<BonusType> ActiveBonuses { get; set; } = new List<BonusType>();

        /// <summary>
        /// Weighted relationship score based on faction influence
        /// </summary>
        public float WeightedRelationshipScore => RelationshipScore * Influence;
    }

    /// <summary>
    /// Manages stakeholder factions and their relationships with the player
    /// </summary>
    public class StakeholderFactionSystem
    {
        private Dictionary<FactionType, Faction> _factions;
        private List<Demand> _demandHistory;
        private Random _random;
        private int _nextDemandId;

        public event Action<FactionType, float, string> RelationshipChanged;
        public event Action<Demand> DemandIssued;
        public event Action<ConsequenceResult> ConsequenceApplied;

        public StakeholderFactionSystem()
        {
            _factions = new Dictionary<FactionType, Faction>();
            _demandHistory = new List<Demand>();
            _random = new Random();
            _nextDemandId = 1;
            InitializeFactions();
        }

        private void InitializeFactions()
        {
            _factions[FactionType.MATTILSYNET] = new Faction
            {
                Type = FactionType.MATTILSYNET,
                Name = "Mattilsynet",
                Description = "Norwegian Food Safety Authority - ensures food safety and compliance",
                RelationshipScore = 0.1f,
                Influence = 0.9f // High regulatory influence
            };

            _factions[FactionType.FISKERIDIREKTORATET] = new Faction
            {
                Type = FactionType.FISKERIDIREKTORATET,
                Name = "Fiskeridirektoratet",
                Description = "Norwegian Fisheries Directorate - manages licenses and MAB allocation",
                RelationshipScore = 0.0f,
                Influence = 0.95f // Highest influence - controls licensing
            };

            _factions[FactionType.ENVIRONMENTAL_NGOS] = new Faction
            {
                Type = FactionType.ENVIRONMENTAL_NGOS,
                Name = "Environmental NGOs",
                Description = "Bellona, WWF Norway - advocates for sustainability",
                RelationshipScore = -0.2f, // Initially skeptical
                Influence = 0.6f
            };

            _factions[FactionType.LOCAL_COMMUNITIES] = new Faction
            {
                Type = FactionType.LOCAL_COMMUNITIES,
                Name = "Local Communities",
                Description = "Local residents balancing jobs vs. environmental impact",
                RelationshipScore = 0.0f,
                Influence = 0.5f
            };

            _factions[FactionType.INDUSTRY_ASSOCIATION] = new Faction
            {
                Type = FactionType.INDUSTRY_ASSOCIATION,
                Name = "Aquaculture Industry Association",
                Description = "Industry lobby promoting growth and reduced regulation",
                RelationshipScore = 0.3f, // Generally supportive
                Influence = 0.7f
            };

            _factions[FactionType.FINANCIAL_SECTOR] = new Faction
            {
                Type = FactionType.FINANCIAL_SECTOR,
                Name = "Financial Institutions",
                Description = "Banks, insurers, and investors affecting loan rates and access",
                RelationshipScore = 0.1f,
                Influence = 0.8f // High economic influence
            };
        }

        /// <summary>
        /// Gets all factions
        /// </summary>
        public Dictionary<FactionType, Faction> GetAllFactions()
        {
            return new Dictionary<FactionType, Faction>(_factions);
        }

        /// <summary>
        /// Gets a specific faction
        /// </summary>
        public Faction GetFaction(FactionType factionType)
        {
            return _factions.TryGetValue(factionType, out var faction) ? faction : null;
        }

        /// <summary>
        /// Updates relationship with a faction
        /// </summary>
        public void UpdateRelationship(FactionType factionType, float change, string reason)
        {
            if (_factions.TryGetValue(factionType, out var faction))
            {
                var oldScore = faction.RelationshipScore;
                faction.RelationshipScore = Math.Max(-1f, Math.Min(1f, faction.RelationshipScore + change));
                
                RelationshipChanged?.Invoke(factionType, faction.RelationshipScore - oldScore, reason);
            }
        }

        /// <summary>
        /// Gets all active demands from all factions
        /// </summary>
        public List<Demand> GetActiveDemands()
        {
            var activeDemands = new List<Demand>();
            foreach (var faction in _factions.Values)
            {
                activeDemands.AddRange(faction.ActiveDemands.Where(d => !d.IsExpired));
            }
            return activeDemands;
        }

        /// <summary>
        /// Ignores a demand, triggering consequences
        /// </summary>
        public ConsequenceResult IgnoreDemand(Demand demand)
        {
            var result = new ConsequenceResult
            {
                Success = false,
                CostNOK = 0
            };

            if (_factions.TryGetValue(demand.FactionType, out var faction))
            {
                // Remove demand from active list
                faction.ActiveDemands.Remove(demand);
                
                // Apply relationship penalty
                var relationshipPenalty = -0.1f - (_random.NextSingle() * 0.1f); // -0.1 to -0.2
                UpdateRelationship(demand.FactionType, relationshipPenalty, $"Ignored demand: {demand.Description}");
                result.RelationshipChange = relationshipPenalty;

                // Apply consequences based on faction type and relationship
                if (faction.RelationshipScore < -0.3f)
                {
                    var sanction = DetermineSanction(demand.FactionType);
                    ApplyRegulatorySanction(demand.FactionType, sanction);
                    result.SanctionApplied = sanction;
                    result.Messages.Add($"{faction.Name} imposed {sanction} due to ignored demand");
                }

                result.Description = demand.ConsequenceIfIgnored;
                result.Messages.Add($"Relationship with {faction.Name} decreased by {Math.Abs(relationshipPenalty):F2}");
                
                ConsequenceApplied?.Invoke(result);
            }

            return result;
        }

        /// <summary>
        /// Fulfills a demand, gaining relationship benefits
        /// </summary>
        public ConsequenceResult FulfillDemand(Demand demand)
        {
            var result = new ConsequenceResult
            {
                Success = true,
                CostNOK = demand.CostNOK
            };

            if (_factions.TryGetValue(demand.FactionType, out var faction))
            {
                // Remove demand from active list
                faction.ActiveDemands.Remove(demand);
                
                // Apply relationship bonus
                var relationshipBonus = 0.05f + (_random.NextSingle() * 0.1f); // +0.05 to +0.15
                UpdateRelationship(demand.FactionType, relationshipBonus, $"Fulfilled demand: {demand.Description}");
                result.RelationshipChange = relationshipBonus;

                // Apply bonuses for good relationships
                if (faction.RelationshipScore > 0.5f)
                {
                    var bonus = DetermineBonus(demand.FactionType);
                    ApplyBonus(demand.FactionType, bonus);
                    result.BonusApplied = bonus;
                    result.Messages.Add($"{faction.Name} granted {bonus} for good relationship");
                }

                result.Description = $"Successfully fulfilled demand from {faction.Name}";
                result.Messages.Add($"Relationship with {faction.Name} increased by {relationshipBonus:F2}");
                result.Messages.Add($"Cost: {demand.CostNOK:N0} NOK");
                
                ConsequenceApplied?.Invoke(result);
            }

            return result;
        }

        /// <summary>
        /// Calculates overall reputation as weighted average of faction relationships
        /// </summary>
        public float GetOverallReputation()
        {
            if (!_factions.Any()) return 0f;
            
            var totalWeightedScore = _factions.Values.Sum(f => f.WeightedRelationshipScore);
            var totalInfluence = _factions.Values.Sum(f => f.Influence);
            
            return totalInfluence > 0 ? totalWeightedScore / totalInfluence : 0f;
        }

        /// <summary>
        /// Applies regulatory sanctions
        /// </summary>
        public void ApplyRegulatorySanction(FactionType factionType, SanctionType sanctionType)
        {
            if (_factions.TryGetValue(factionType, out var faction))
            {
                if (!faction.ActiveSanctions.Contains(sanctionType))
                {
                    faction.ActiveSanctions.Add(sanctionType);
                }
            }
        }

        /// <summary>
        /// Applies faction bonuses
        /// </summary>
        public void ApplyBonus(FactionType factionType, BonusType bonusType)
        {
            if (_factions.TryGetValue(factionType, out var faction))
            {
                if (!faction.ActiveBonuses.Contains(bonusType))
                {
                    faction.ActiveBonuses.Add(bonusType);
                }
            }
        }

        /// <summary>
        /// Issues a new demand from a faction
        /// </summary>
        public void IssueDemand(FactionType factionType, string description, decimal costNOK, int durationMonths, string consequence)
        {
            var demand = new Demand
            {
                Id = _nextDemandId++,
                FactionType = factionType,
                Description = description,
                CostNOK = costNOK,
                DurationMonths = durationMonths,
                ConsequenceIfIgnored = consequence
            };

            if (_factions.TryGetValue(factionType, out var faction))
            {
                faction.ActiveDemands.Add(demand);
                _demandHistory.Add(demand);
                DemandIssued?.Invoke(demand);
            }
        }

        /// <summary>
        /// Removes expired demands
        /// </summary>
        public void ProcessExpiredDemands()
        {
            foreach (var faction in _factions.Values)
            {
                var expiredDemands = faction.ActiveDemands.Where(d => d.IsExpired).ToList();
                foreach (var demand in expiredDemands)
                {
                    IgnoreDemand(demand); // Treat expired demands as ignored
                }
            }
        }

        private SanctionType DetermineSanction(FactionType factionType)
        {
            return factionType switch
            {
                FactionType.MATTILSYNET => SanctionType.INCREASED_INSPECTIONS,
                FactionType.FISKERIDIREKTORATET => SanctionType.MAB_REDUCTION,
                FactionType.ENVIRONMENTAL_NGOS => SanctionType.PERMIT_DENIAL,
                FactionType.LOCAL_COMMUNITIES => SanctionType.PERMIT_DENIAL,
                FactionType.INDUSTRY_ASSOCIATION => SanctionType.FINE,
                FactionType.FINANCIAL_SECTOR => SanctionType.FINE,
                _ => SanctionType.FINE
            };
        }

        private BonusType DetermineBonus(FactionType factionType)
        {
            return factionType switch
            {
                FactionType.MATTILSYNET => BonusType.REDUCED_INSPECTIONS,
                FactionType.FISKERIDIREKTORATET => BonusType.EXPEDITED_PERMITS,
                FactionType.ENVIRONMENTAL_NGOS => BonusType.POSITIVE_MEDIA,
                FactionType.LOCAL_COMMUNITIES => BonusType.POSITIVE_MEDIA,
                FactionType.INDUSTRY_ASSOCIATION => BonusType.EXPEDITED_PERMITS,
                FactionType.FINANCIAL_SECTOR => BonusType.FAVORABLE_LOAN_RATES,
                _ => BonusType.POSITIVE_MEDIA
            };
        }
    }
}