namespace LakskongenSim.Core
{
    /// <summary>
    /// The sea lane's part of the saved game (roadmap F2-06/F2-07,
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
    }
}
