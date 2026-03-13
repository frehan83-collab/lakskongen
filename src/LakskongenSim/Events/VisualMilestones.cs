using System;
using System.Collections.Generic;
using Godot;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Visual Milestones System - Automatic cinematics and achievements
    /// Triggers beautiful moments when players hit major milestones
    /// Author: Ingrid Lunde, UI/UX Designer
    /// </summary>
    public partial class VisualMilestones : Node
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
            public string ScreenshotPath { get; set; } = string.Empty;
        }

        // Milestone tracking
        private Dictionary<MilestoneType, Milestone> milestones = new Dictionary<MilestoneType, Milestone>();
        private HashSet<MilestoneType> triggeredMilestones = new HashSet<MilestoneType>();
        
        // References
        private Node gameManager;
        private Node photoMode;
        private AudioStreamPlayer cinematicMusic;
        
        // Cinematic state
        private bool isPlayingCinematic = false;
        private Camera3D cinematicCamera;
        
        [Signal]
        public delegate void MilestoneAchievedEventHandler(string title, string description);
        
        [Signal]
        public delegate void CinematicStartedEventHandler(MilestoneType type);
        
        [Signal]
        public delegate void CinematicFinishedEventHandler();

        public override void _Ready()
        {
            InitializeMilestones();
            SetupCinematicCamera();
            GD.Print("✨ Visual Milestones system initialized");
        }

        private void InitializeMilestones()
        {
            // First Harvest - The beginning
            milestones[MilestoneType.FirstHarvest] = new Milestone
            {
                Type = MilestoneType.FirstHarvest,
                Title = "🐟 First Harvest",
                Description = "You've harvested your first batch of salmon!",
                CinematicType = "SlowMotionNetLift",
                Achieved = false
            };

            // Revenue milestones
            milestones[MilestoneType.Revenue1M] = new Milestone
            {
                Type = MilestoneType.Revenue1M,
                Title = "💰 Millionaire",
                Description = "1,000,000 NOK in total revenue!",
                CinematicType = "ChampagneFireworks",
                Achieved = false
            };

            milestones[MilestoneType.Revenue10M] = new Milestone
            {
                Type = MilestoneType.Revenue10M,
                Title = "💎 Multi-Millionaire",
                Description = "10,000,000 NOK in total revenue!",
                CinematicType = "DroneOverview",
                Achieved = false
            };

            milestones[MilestoneType.Revenue100M] = new Milestone
            {
                Type = MilestoneType.Revenue100M,
                Title = "👑 Salmon King",
                Description = "100,000,000 NOK! You're a true Lakskongen!",
                CinematicType = "EpicMontage",
                Achieved = false
            };

            // Sustainability
            milestones[MilestoneType.PerfectSustainabilityQuarter] = new Milestone
            {
                Type = MilestoneType.PerfectSustainabilityQuarter,
                Title = "🌿 Perfect Sustainability",
                Description = "Perfect sustainability score for an entire quarter!",
                CinematicType = "EcoBadgeDrone",
                Achieved = false
            };

            // Production milestones
            milestones[MilestoneType.Fish100K] = new Milestone
            {
                Type = MilestoneType.Fish100K,
                Title = "🎣 Century Mark",
                Description = "100,000 fish harvested in your career!",
                CinematicType = "ProductionMontage",
                Achieved = false
            };

            milestones[MilestoneType.Fish1M] = new Milestone
            {
                Type = MilestoneType.Fish1M,
                Title = "🏆 Production Master",
                Description = "1,000,000 fish harvested! Industry legend!",
                CinematicType = "LegendaryMontage",
                Achieved = false
            };

            // Expansion milestones
            milestones[MilestoneType.FirstAcquisition] = new Milestone
            {
                Type = MilestoneType.FirstAcquisition,
                Title = "🤝 Empire Builder",
                Description = "You've acquired your first competitor!",
                CinematicType = "AcquisitionCelebration",
                Achieved = false
            };

            milestones[MilestoneType.VerticalIntegration] = new Milestone
            {
                Type = MilestoneType.VerticalIntegration,
                Title = "🏭 Vertical Integration",
                Description = "Full control: Feed mill to distribution!",
                CinematicType = "SupplyChainViz",
                Achieved = false
            };

            milestones[MilestoneType.InternationalExpansion] = new Milestone
            {
                Type = MilestoneType.InternationalExpansion,
                Title = "🌍 Global Player",
                Description = "Exporting to 5+ international markets!",
                CinematicType = "GlobalMap",
                Achieved = false
            };

            milestones[MilestoneType.CrisisSurvival] = new Milestone
            {
                Type = MilestoneType.CrisisSurvival,
                Title = "⚡ Crisis Survivor",
                Description = "You survived a major crisis and recovered!",
                CinematicType = "RecoveryMontage",
                Achieved = false
            };
        }

        private void SetupCinematicCamera()
        {
            cinematicCamera = new Camera3D();
            cinematicCamera.Name = "CinematicCamera";
            AddChild(cinematicCamera);
            cinematicCamera.Current = false;
        }

        /// <summary>
        /// Check and trigger milestone if conditions are met
        /// Called from game systems when events occur
        /// </summary>
        public void CheckMilestone(MilestoneType type, Dictionary<string, object> context = null)
        {
            if (triggeredMilestones.Contains(type))
                return; // Already triggered

            if (!milestones.ContainsKey(type))
                return;

            var milestone = milestones[type];
            
            // Mark as achieved
            milestone.Achieved = true;
            milestone.AchievedDate = DateTime.Now;
            triggeredMilestones.Add(type);

            GD.Print($"🎊 Milestone achieved: {milestone.Title}");

            // Emit signal for UI
            EmitSignal(SignalName.MilestoneAchieved, milestone.Title, milestone.Description);

            // Play cinematic
            PlayCinematic(milestone, context);

            // Capture screenshot
            CaptureMilestoneScreenshot(milestone);
        }

        private void PlayCinematic(Milestone milestone, Dictionary<string, object> context)
        {
            if (isPlayingCinematic)
                return;

            isPlayingCinematic = true;
            EmitSignal(SignalName.CinematicStarted, (int)milestone.Type);

            GD.Print($"🎬 Playing cinematic: {milestone.CinematicType}");

            // Play appropriate cinematic based on type
            switch (milestone.CinematicType)
            {
                case "SlowMotionNetLift":
                    PlayFirstHarvestCinematic();
                    break;
                
                case "ChampagneFireworks":
                    PlayRevenueMillionCinematic();
                    break;
                
                case "EcoBadgeDrone":
                    PlaySustainabilityCinematic();
                    break;
                
                case "ProductionMontage":
                case "LegendaryMontage":
                    PlayProductionMilestoneCinematic(milestone.Type);
                    break;
                
                case "DroneOverview":
                case "EpicMontage":
                    PlayEmpireOverviewCinematic();
                    break;
                
                default:
                    PlayGenericCelebration();
                    break;
            }
        }

        private void PlayFirstHarvestCinematic()
        {
            // Slow-mo net lift with music swell
            Engine.TimeScale = 0.3; // Slow motion
            
            // TODO: Position camera to watch harvest
            // TODO: Add particle effects (water splash, fish jumping)
            // TODO: Play triumphant music
            
            // Reset after 5 seconds
            GetTree().CreateTimer(5.0).Timeout += () =>
            {
                Engine.TimeScale = 1.0;
                EndCinematic();
            };
        }

        private void PlayRevenueMillionCinematic()
        {
            // Champagne cork pop + fireworks over fjord
            // TODO: Spawn particle effects
            // TODO: Play celebration sound
            // TODO: Show currency counter animation
            
            GetTree().CreateTimer(4.0).Timeout += EndCinematic;
        }

        private void PlaySustainabilityCinematic()
        {
            // Drone shot showing pristine farm
            // TODO: Camera path flying over farm
            // TODO: Green overlay effects
            // TODO: Eco badge animation
            
            GetTree().CreateTimer(6.0).Timeout += EndCinematic;
        }

        private void PlayProductionMilestoneCinematic(MilestoneType type)
        {
            // Montage of harvests, growing operation
            // TODO: Multiple camera angles
            // TODO: Production statistics overlay
            
            GetTree().CreateTimer(5.0).Timeout += EndCinematic;
        }

        private void PlayEmpireOverviewCinematic()
        {
            // High-altitude overview of all operations
            // TODO: Camera zoom out to show scale
            // TODO: Highlight owned sites
            
            GetTree().CreateTimer(6.0).Timeout += EndCinematic;
        }

        private void PlayGenericCelebration()
        {
            // Simple celebration effect
            GetTree().CreateTimer(3.0).Timeout += EndCinematic;
        }

        private void EndCinematic()
        {
            isPlayingCinematic = false;
            
            // Restore normal camera
            if (cinematicCamera != null)
                cinematicCamera.Current = false;
            
            EmitSignal(SignalName.CinematicFinished);
            GD.Print("🎬 Cinematic finished");
        }

        private void CaptureMilestoneScreenshot(Milestone milestone)
        {
            // Use PhotoMode screenshot system
            var photoModeNode = GetTree().Root.GetNodeOrNull("PhotoMode");
            
            if (photoModeNode != null && photoModeNode.HasMethod("capture_screenshot"))
            {
                // Capture automatically
                photoModeNode.Call("capture_screenshot");
                
                // Store reference
                // milestone.ScreenshotPath = ... (get from PhotoMode)
                GD.Print($"📸 Auto-captured screenshot for {milestone.Title}");
            }
        }

        /// <summary>
        /// Get all achieved milestones
        /// </summary>
        public List<Milestone> GetAchievedMilestones()
        {
            var achieved = new List<Milestone>();
            foreach (var milestone in milestones.Values)
            {
                if (milestone.Achieved)
                    achieved.Add(milestone);
            }
            return achieved;
        }

        /// <summary>
        /// Get milestone progress (total achieved / total milestones)
        /// </summary>
        public float GetProgress()
        {
            return (float)triggeredMilestones.Count / milestones.Count;
        }

        /// <summary>
        /// Check if specific milestone is achieved
        /// </summary>
        public bool IsMilestoneAchieved(MilestoneType type)
        {
            return triggeredMilestones.Contains(type);
        }

        /// <summary>
        /// Get next unachieved milestone
        /// </summary>
        public Milestone? GetNextMilestone()
        {
            foreach (var milestone in milestones.Values)
            {
                if (!milestone.Achieved)
                    return milestone;
            }
            return null;
        }
    }
}
