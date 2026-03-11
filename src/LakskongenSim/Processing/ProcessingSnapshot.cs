namespace LakskongenSim.Processing
{
    /// <summary>
    /// Lightweight snapshot for dashboards: plant id, tier, last-day profit and cumulative stats.
    /// </summary>
    public class ProcessingSnapshot
    {
        public string PlantId { get; set; } = string.Empty;
        public ProcessingTier Tier { get; set; } = ProcessingTier.Small;
        public int LastInputKg { get; set; }
        public int LastOutputKg { get; set; }
        public decimal LastProfit { get; set; }
        public decimal CumulativeProfit { get; set; }
    }
}
