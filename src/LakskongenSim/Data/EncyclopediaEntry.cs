namespace LakskongenSim.Data
{
    /// <summary>
    /// Encyclopedia entry for in-game knowledge base.
    /// </summary>
    public class EncyclopediaEntry
    {
        /// <summary>Unique identifier for the entry.</summary>
        public string Id { get; set; } = "";

        /// <summary>Display title of the entry.</summary>
        public string Title { get; set; } = "";

        /// <summary>Category for grouping entries.</summary>
        public string Category { get; set; } = "";

        /// <summary>Main content text.</summary>
        public string Content { get; set; } = "";

        /// <summary>Related entry IDs for cross-referencing.</summary>
        public List<string> RelatedIds { get; set; } = new();
    }
}
