using System;
using System.Collections.Generic;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Visual Milestones System - Tracks achievement milestones and notifies via events.
    /// Pure C# — no Godot dependencies. Cinematic/camera logic lives in the Godot layer.
    /// Author: Ingrid Lunde, UI/UX Designer
    /// </summary>
    public class VisualMilestones
    {
        public enum MilestoneType
        {
            FirstHarvest,
            Revenue1M,
            Revenue10M,
            Revenue100M,
            PerfectSustainabilityQuarter,
            Fish100K,
            Fish1M,
            FirstAcquisition,
            VerticalIntegration,
            InternationalExpansion,
            CrisisSurvival
        }

        public class Milestone
        {
            public MilestoneType Type { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string CinematicType { get; set; } = string.Empty;
            public bool Achieved { get; set; }
            public DateTime? AchievedDate { get; set; }
        }

        // C# events (replaces Godot signals)
        public event Action<string, string>? MilestoneAchieved;
        public event Action<MilestoneType>? CinematicStarted;
        public event Action? CinematicFinished;

        private readonly Dictionary<MilestoneType, Milestone> _milestones = new();
        private readonly HashSet<MilestoneType> _triggeredMilestones = new();

        public VisualMilestones()
        {
            InitializeMilestones();
        }

        private void InitializeMilestones()
        {
            _milestones[MilestoneType.FirstHarvest] = new Milestone
            {
                Type = MilestoneType.FirstHarvest,
                Title = "First Harvest",
                Description = "You've harvested your first batch of salmon!",
                CinematicType = "SlowMotionNetLift"
            };

            _milestones[MilestoneType.Revenue1M] = new Milestone
            {
                Type = MilestoneType.Revenue1M,
                Title = "Millionaire",
                Description = "1,000,000 NOK in total revenue!",
                CinematicType = "ChampagneFireworks"
            };

            _milestones[MilestoneType.Revenue10M] = new Milestone
            {
                Type = MilestoneType.Revenue10M,
                Title = "Multi-Millionaire",
                Description = "10,000,000 NOK in total revenue!",
                CinematicType = "DroneOverview"
            };

            _milestones[MilestoneType.Revenue100M] = new Milestone
            {
                Type = MilestoneType.Revenue100M,
                Title = "Salmon King",
                Description = "100,000,000 NOK! You're a true Lakskongen!",
                CinematicType = "EpicMontage"
            };

            _milestones[MilestoneType.PerfectSustainabilityQuarter] = new Milestone
            {
                Type = MilestoneType.PerfectSustainabilityQuarter,
                Title = "Perfect Sustainability",
                Description = "Perfect sustainability score for an entire quarter!",
                CinematicType = "EcoBadgeDrone"
            };

            _milestones[MilestoneType.Fish100K] = new Milestone
            {
                Type = MilestoneType.Fish100K,
                Title = "Century Mark",
                Description = "100,000 fish harvested in your career!",
                CinematicType = "ProductionMontage"
            };

            _milestones[MilestoneType.Fish1M] = new Milestone
            {
                Type = MilestoneType.Fish1M,
                Title = "Production Master",
                Description = "1,000,000 fish harvested! Industry legend!",
                CinematicType = "LegendaryMontage"
            };

            _milestones[MilestoneType.FirstAcquisition] = new Milestone
            {
                Type = MilestoneType.FirstAcquisition,
                Title = "Empire Builder",
                Description = "You've acquired your first competitor!",
                CinematicType = "AcquisitionCelebration"
            };

            _milestones[MilestoneType.VerticalIntegration] = new Milestone
            {
                Type = MilestoneType.VerticalIntegration,
                Title = "Vertical Integration",
                Description = "Full control: Feed mill to distribution!",
                CinematicType = "SupplyChainViz"
            };

            _milestones[MilestoneType.InternationalExpansion] = new Milestone
            {
                Type = MilestoneType.InternationalExpansion,
                Title = "Global Player",
                Description = "Exporting to 5+ international markets!",
                CinematicType = "GlobalMap"
            };

            _milestones[MilestoneType.CrisisSurvival] = new Milestone
            {
                Type = MilestoneType.CrisisSurvival,
                Title = "Crisis Survivor",
                Description = "You survived a major crisis and recovered!",
                CinematicType = "RecoveryMontage"
            };
        }

        /// <summary>
        /// Check and trigger milestone if conditions are met.
        /// Called from game systems when events occur.
        /// </summary>
        public void CheckMilestone(MilestoneType type)
        {
            if (_triggeredMilestones.Contains(type))
                return;

            if (!_milestones.TryGetValue(type, out var milestone))
                return;

            milestone.Achieved = true;
            milestone.AchievedDate = DateTime.Now;
            _triggeredMilestones.Add(type);

            MilestoneAchieved?.Invoke(milestone.Title, milestone.Description);
            CinematicStarted?.Invoke(type);
            // Cinematic playback is handled by the Godot layer listening to CinematicStarted.
            // Signal completion via CinematicFinished when done.
        }

        /// <summary>Call this from the Godot layer when a cinematic finishes playing.</summary>
        public void NotifyCinematicFinished()
        {
            CinematicFinished?.Invoke();
        }

        /// <summary>Get all achieved milestones.</summary>
        public List<Milestone> GetAchievedMilestones()
        {
            var achieved = new List<Milestone>();
            foreach (var milestone in _milestones.Values)
            {
                if (milestone.Achieved)
                    achieved.Add(milestone);
            }
            return achieved;
        }

        /// <summary>
        /// Restore achieved milestones from save (no events, no cinematics —
        /// load path only). Unknown type names are skipped.
        /// </summary>
        public void RestoreAchieved(System.Collections.Generic.IEnumerable<string> typeNames)
        {
            foreach (var name in typeNames)
            {
                if (!System.Enum.TryParse<MilestoneType>(name, out var type)) continue;
                if (!_milestones.TryGetValue(type, out var milestone)) continue;
                milestone.Achieved = true; // AchievedDate stays null (original date not persisted)
                _triggeredMilestones.Add(type);
            }
        }

        /// <summary>Get milestone progress (total achieved / total milestones).</summary>
        public float GetProgress() =>
            (float)_triggeredMilestones.Count / _milestones.Count;

        /// <summary>Check if a specific milestone is achieved.</summary>
        public bool IsMilestoneAchieved(MilestoneType type) =>
            _triggeredMilestones.Contains(type);

        /// <summary>Get next unachieved milestone, or null if all done.</summary>
        public Milestone? GetNextMilestone()
        {
            foreach (var milestone in _milestones.Values)
            {
                if (!milestone.Achieved)
                    return milestone;
            }
            return null;
        }
    }
}
