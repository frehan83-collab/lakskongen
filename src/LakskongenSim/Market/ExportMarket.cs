using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Represents exports and access to international markets.
    /// </summary>
    public class ExportMarket
    {
        public bool Open { get; set; } = true;

        public event Action<bool>? OnMarketStatusChanged;

        public void SetOpen(bool open)
        {
            Open = open;
            OnMarketStatusChanged?.Invoke(open);
        }
    }
}