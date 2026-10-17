using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LakskongenSim.Farm;

namespace LakskongenSim.Core
{
    /// <summary>
    /// The closed vocabulary a tutorial step may wait for (roadmap U2-02,
    /// acceptance 1): a step advances on a real UI event (<c>ui:&lt;mark&gt;</c>,
    /// sent by the screen the player used through <see cref="TutorialSystem.Mark"/>)
    /// or on an engine state that only the player's own verb produces (a
    /// feeding plan set, a standing order, a harvest ordered, a site built) or
    /// that the world reaches (the fish at harvest weight, the fish sold). There
    /// is no day count and no condition that is true by itself: an unknown name
    /// in <c>tutorial_steps.json</c> fails the load, and
    /// <c>TutorialStaticTests</c> scans this file for the day and for constant
    /// predicates.
    /// </summary>
    public static class TutorialConditions
    {
        /// <summary>Prefix of a UI-event condition: <c>ui:tab:pens</c> waits for the mark <c>tab:pens</c>.</summary>
        public const string UiPrefix = "ui:";

        /// <summary>
        /// The UI events a step may wait for. Each is sent by one screen when
        /// the player does it there: a tab opened by its button or shortcut
        /// (GameUI), the inbox opened (HUD bell or I), the clock started
        /// (Space, a speed button or key), the harvest report shown, the
        /// company's own production area picked on the map.
        /// </summary>
        public static readonly IReadOnlyList<string> UiMarks = new[]
        {
            TutorialMarks.Clock,
            TutorialMarks.HarvestReport,
            TutorialMarks.Inbox,
            TutorialMarks.MapAreaHome,
            TutorialMarks.Tab("finance"),
            TutorialMarks.Tab("map"),
            TutorialMarks.Tab("pens"),
            TutorialMarks.Tab("research"),
            TutorialMarks.Tab("sustainability"),
        };

        /// <summary>The engine states a step may wait for, by name.</summary>
        internal static readonly IReadOnlyDictionary<string, Func<TutorialSystem, bool>> Engine =
            new Dictionary<string, Func<TutorialSystem, bool>>(StringComparer.Ordinal)
            {
                // The player's verbs.
                ["feed_plan_set"] = t => t.FjordLocalities.Any(s => s.FeedPlan != null),
                ["lice_order_set"] = t => t.FjordLocalities.Any(s => t.HasLiceOrder(s.Id)),
                ["harvest_planned"] = t => t.State.HarvestOrders.Count > 0 || TutorialSystem.HasSold(t),
                ["restocked"] = t => t.RestockedAfterFirstHarvest,
                ["research_started"] = t => t.ResearchStartedByPlayer,
                ["build_funded"] = t => t.HasBorrowed || t.LiveBalance >= t.FundingTargetNok(),
                ["second_site_built"] = t => t.FjordLocalities.Count >= 2,
                ["second_site_stocked"] = t => t.FjordLocalities.Skip(1).Any(s => FishAt(s) > 0),
                ["every_site_lice_order"] = t => t.FjordLocalities.Count >= 2 && t.FjordLocalities.All(s => t.HasLiceOrder(s.Id)),
                // What the world reaches while the clock runs.
                ["harvest_weight_reached"] = t => t.TotalFishCount > 0 && t.AverageFishWeightKg >= TutorialSystem.HarvestTeachingWeightKg,
                ["fish_sold"] = TutorialSystem.HasSold,
            };

        /// <summary>Every condition name a step may use (UI marks with their prefix, then the engine's).</summary>
        public static IEnumerable<string> All =>
            UiMarks.Select(m => UiPrefix + m).Concat(Engine.Keys.OrderBy(k => k, StringComparer.Ordinal));

        /// <summary>The UI mark a condition waits for, or null for an engine condition.</summary>
        public static string? MarkOf(string condition)
            => condition.StartsWith(UiPrefix, StringComparison.Ordinal) ? condition[UiPrefix.Length..] : null;

        /// <summary>
        /// Parse a step's condition. Empty means "no trigger" for a trigger and
        /// is refused for a completion (a step that completes by itself teaches
        /// nothing); an unknown name throws.
        /// </summary>
        internal static Func<TutorialSystem, bool>? Parse(string? condition, string stepId, bool isCompletion)
        {
            string name = (condition ?? "").Trim();
            if (name.Length == 0)
            {
                if (isCompletion)
                    throw new InvalidDataException($"tutorial step '{stepId}': a completion condition is required");
                return null;
            }
            if (MarkOf(name) is { } mark)
            {
                if (!UiMarks.Contains(mark, StringComparer.Ordinal))
                    throw new InvalidDataException($"tutorial step '{stepId}': unknown UI mark '{mark}'");
                return t => t.HasPendingMark(mark);
            }
            if (Engine.TryGetValue(name, out var predicate))
                return predicate;
            throw new InvalidDataException($"tutorial step '{stepId}': unknown condition '{name}'");
        }

        private static int FishAt(SeaSite site) => site.Pens.Sum(p => p.TotalCount);
    }

    /// <summary>The UI events the screens send to the tutorial (<see cref="TutorialSystem.Mark"/>).</summary>
    public static class TutorialMarks
    {
        /// <summary>The clock started by the player (Space, a speed button or key).</summary>
        public const string Clock = "clock";
        /// <summary>The attention inbox opened (the HUD bell or I).</summary>
        public const string Inbox = "inbox";
        /// <summary>A harvest report shown (from its toast or the pen view).</summary>
        public const string HarvestReport = "harvest_report";
        /// <summary>The company's own production area picked on the map.</summary>
        public const string MapAreaHome = "map_area_home";

        /// <summary>A tab opened by the player: <c>tab:&lt;id&gt;</c>.</summary>
        public static string Tab(string tabId) => "tab:" + tabId;
    }

    /// <summary>How a step paces the clock while it waits (the UI applies it).</summary>
    public static class TutorialPace
    {
        /// <summary>The clock stands until the player has done it (the step waits for input).</summary>
        public const string Hold = "hold";
        /// <summary>The clock drops to 1× when the step starts; the player may wait it out.</summary>
        public const string Slow = "slow";
        /// <summary>The clock is the player's (the step waits for the world).</summary>
        public const string Run = "run";

        public static readonly IReadOnlyList<string> All = new[] { Hold, Slow, Run };
    }

    /// <summary>What a step asks for: something to do, or something to wait for.</summary>
    public static class TutorialStepKind
    {
        public const string Do = "do";
        public const string Wait = "wait";

        public static readonly IReadOnlyList<string> All = new[] { Do, Wait };
    }

    /// <summary>Progress of the current step toward its goal (a wait step's weight, the funding step's cash).</summary>
    /// <param name="Value">Where the game is now.</param>
    /// <param name="Goal">Where the step completes.</param>
    /// <param name="Unit">"kg" or "nok".</param>
    public sealed record TutorialProgress(double Value, double Goal, string Unit);
}
