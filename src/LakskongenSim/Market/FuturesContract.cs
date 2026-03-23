using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Represents a salmon futures contract for price hedging.
    /// Based on Fish Pool ASA (Oslo) salmon futures specifications.
    /// </summary>
    public class FuturesContract
    {
        /// <summary>Unique contract identifier.</summary>
        public string ContractId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>Agreed price per kg at contract inception.</summary>
        public double StrikePrice { get; set; }

        /// <summary>Game day when contract expires and settles.</summary>
        public int ExpiryDay { get; set; }

        /// <summary>Contract size in tonnes.</summary>
        public double QuantityTonnes { get; set; }

        /// <summary>True = long (buyer), False = short (seller).</summary>
        public bool IsLong { get; set; }

        /// <summary>Day contract was created.</summary>
        public int InceptionDay { get; set; }

        /// <summary>Margin requirement as fraction of contract value (default 10%).</summary>
        public static readonly double MarginFraction = 0.10;

        /// <summary>
        /// Calculate profit/loss at settlement.
        /// Long profits when spot > strike; Short profits when spot &lt; strike.
        /// </summary>
        /// <param name="spotPrice">Current spot price (NOK/kg) at settlement</param>
        /// <returns>P&amp;L in NOK (positive = profit)</returns>
        public decimal CalculateSettlement(double spotPrice)
        {
            double priceDiff = spotPrice - StrikePrice;
            double pnl = priceDiff * QuantityTonnes * 1000.0; // Convert tonnes to kg
            return (decimal)(IsLong ? pnl : -pnl);
        }

        /// <summary>
        /// Calculate initial margin requirement for this contract.
        /// </summary>
        /// <returns>Required margin deposit in NOK</returns>
        public decimal CalculateMarginRequirement()
        {
            return (decimal)(StrikePrice * QuantityTonnes * 1000.0 * MarginFraction);
        }

        /// <summary>
        /// Calculate mark-to-market P&amp;L (unrealized) at current spot price.
        /// </summary>
        public decimal MarkToMarket(double currentSpotPrice)
        {
            return CalculateSettlement(currentSpotPrice);
        }

        /// <summary>Check if contract has expired.</summary>
        public bool IsExpired(int currentDay) => currentDay >= ExpiryDay;

        /// <summary>Days until expiry.</summary>
        public int DaysToExpiry(int currentDay) => Math.Max(0, ExpiryDay - currentDay);

        /// <summary>Total notional value of the contract in NOK.</summary>
        public decimal NotionalValue => (decimal)(StrikePrice * QuantityTonnes * 1000.0);

        public override string ToString() =>
            $"[{ContractId}] {(IsLong ? "LONG" : "SHORT")} {QuantityTonnes}t @ {StrikePrice:F2} NOK/kg, expires day {ExpiryDay}";
    }
}
