using System.Collections.Generic;

namespace LakskongenSim.Farm.Land
{
    /// <summary>Where a planned land harvest stands.</summary>
    public enum LandHarvestPlanStatus
    {
        /// <summary>Waiting for its week: the order goes out when the fast before the week's Monday begins.</summary>
        Planned = 0,
        /// <summary>The S2-04 harvest order is placed (fasting, then the hall slaughters it).</summary>
        Ordered = 1,
        /// <summary>The order is complete.</summary>
        Done = 2,
        /// <summary>The player cancelled the plan before its order.</summary>
        Cancelled = 3,
        /// <summary>The order could not be placed (the tanks were empty or already ordered).</summary>
        Failed = 4,
    }

    /// <summary>
    /// A tank group's harvest planned into an ISO week (roadmap F2-15, spec
    /// §3.4 <c>PlanHarvest</c>, §6.5 "Du bestemmer når du selger"): the land
    /// tick places the S2-04 harvest order so the hall's slot falls on the
    /// week's Monday, and the fish are slaughtered in that week at each day's
    /// price with the S2-04 size and grade factors.
    /// </summary>
    public sealed class LandHarvestPlan
    {
        /// <summary>Plan id: "&lt;site&gt;/hp&lt;N&gt;".</summary>
        public string Id { get; set; } = "";

        /// <summary>The tanks harvested (tank ids).</summary>
        public List<string> TankIds { get; set; } = new();

        /// <summary>Sim day of the Monday of the planned ISO week.</summary>
        public int WeekStartDay { get; set; }

        /// <summary>ISO week number of <see cref="WeekStartDay"/> (for display).</summary>
        public int IsoWeek { get; set; }

        /// <summary>Day the plan was made.</summary>
        public int PlannedOnDay { get; set; }

        /// <summary>Where the plan stands.</summary>
        public LandHarvestPlanStatus Status { get; set; } = LandHarvestPlanStatus.Planned;

        /// <summary>The S2-04 harvest order placed for it ("" before).</summary>
        public string OrderId { get; set; } = "";

        /// <summary>Day the order was placed (-1 before).</summary>
        public int OrderedDay { get; set; } = -1;

        /// <summary>Why the order failed (a reason key), "" otherwise.</summary>
        public string FailureKey { get; set; } = "";
    }
}
