using System.Collections.Generic;

namespace LakskongenSim.Core
{
    /// <summary>
    /// The sea lane's part of the saved game (roadmap F2-06..F2-09,
    /// docs/FARM_TYPES_SPEC.md §3.8): additive, null or empty by default, so a
    /// save without it loads unchanged. Kept in its own file so the land lane's
    /// additions never meet it (spec §7.6).
    /// </summary>
    public partial class GameState
    {
        /// <summary>
        /// The open sea beyond the fjord mouth (F2-06, spec §3.2 "one per game in
        /// GameState.Metocean"): null until the game first has an offshore unit
        /// (a build, a scenario start or a migrated save); from then on it draws
        /// its 8 uniforms every day from the <c>offshore_metocean</c> stream,
        /// whether or not a unit still stands. A fjord-only game never creates it,
        /// so its streams, and the shipped scenarios' golden traces, never see it.
        /// </summary>
        public Environment.OffshoreMetocean? Metocean { get; set; }

        /// <summary>F2-08: every incident at an offshore unit, open and closed, in the order they started.</summary>
        public List<Farm.Offshore.OffshoreIncident> OffshoreIncidents { get; set; } = new();

        /// <summary>F2-08: the reports written after critical events at offshore units, in the order written.</summary>
        public List<Farm.Offshore.IncidentReport> IncidentReports { get; set; } = new();

        /// <summary>F2-08: number of the next incident report ("ir_N").</summary>
        public int NextIncidentReportNumber { get; set; } = 1;

        /// <summary>F2-08: the site-withdrawal cases, in the order opened.</summary>
        public List<Finance.SiteWithdrawalCase> SiteWithdrawals { get; set; } = new();

        /// <summary>F2-08: number of the next site-withdrawal case ("sw_N").</summary>
        public int NextSiteWithdrawalNumber { get; set; } = 1;

        /// <summary>F2-09: the development-licence rounds, in the order opened (spec §3.8 <c>DevelopmentRounds</c>).</summary>
        public List<Finance.DevelopmentLicenceRound> DevelopmentRounds { get; set; } = new();

        /// <summary>F2-09: the development-licence awards (the company's and the rivals'), in the order awarded.</summary>
        public List<Finance.DevelopmentAward> DevelopmentAwards { get; set; } = new();

        /// <summary>
        /// F2-09: whether rivals may win development licences and take a plot
        /// (spec §4 <c>rivalOffshore</c>): null follows the scenario (on in every
        /// scenario but the demo scenarios, a scenario's special rule
        /// <c>rival_offshore</c> overrides), true or false forces it.
        /// </summary>
        public bool? RivalOffshore { get; set; }
    }
}
