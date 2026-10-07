using System;

namespace LakskongenSim.Logistics
{
    /// <summary>
    /// Spot-hire wellboat job (Phase 2.4). Requested via GameBridge, stored in
    /// GameState (serializable), executed on due day. Own-vs-hire and fleet
    /// simulation land in Phase 3.
    /// </summary>
    [Serializable]
    public class WellboatJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SiteId { get; set; } = "";
        /// <summary>"harvest" or "delouse".</summary>
        public string Kind { get; set; } = "harvest";
        /// <summary>Delousing method (for delouse jobs).</summary>
        public string Method { get; set; } = "";
        public int DueDay { get; set; }
        public decimal HireCost { get; set; }

        public WellboatJob() { }
    }
}
