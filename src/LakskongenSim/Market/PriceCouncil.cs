using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Binding monthly pen price set by the Norwegian <b>Price Council</b>
    /// (prisrådet) for farmed salmon.
    ///
    /// <para>
    /// In the Norwegian market fish rarely sell direct from the pen at raw spot:
    /// the Price Council publishes a monthly binding pen price derived from the
    /// underlying base/spot series. This model reproduces that with a transparent,
    /// testable smoothing rule:
    /// </para>
    /// <list type="number">
    /// <item><description><b>Lagged 4-week average</b> of the most recent weekly spot observations.</description></item>
    /// <item><description><b>Clamped band</b> around the base pen price (default ±10%) so a single
    /// volatile week cannot move the binding price outside a sane corridor.</description></item>
    /// </list>
    ///
    /// <para>
    /// The type holds a small rolling window and exposes <see cref="MonthlyPenPrice"/>.
    /// The smoothing/clamping math is also available as pure static helpers for tests
    /// and analysis.
    /// </para>
    /// </summary>
    public class PriceCouncil
    {
        /// <summary>Number of recent weekly spot observations averaged into the pen price.</summary>
        public const int SmoothingWeeks = 4;

        /// <summary>Default half-width of the clamp band as a fraction of the base pen price (±10%).</summary>
        public const decimal DefaultBandFraction = 0.10m;

        private readonly decimal _basePenPrice;
        private readonly decimal _bandFraction;
        private readonly List<decimal> _weeklySpots = new();

        /// <summary>Current binding monthly pen price (NOK/kg). Initialised to the base price.</summary>
        public decimal MonthlyPenPrice { get; private set; }

        /// <summary>The long-run base pen price the clamp band is centred on (NOK/kg).</summary>
        public decimal BasePenPrice => _basePenPrice;

        /// <summary>The clamp half-width as a fraction of the base price.</summary>
        public decimal BandFraction => _bandFraction;

        /// <summary>Most recent weekly spot observations retained (newest last).</summary>
        public IReadOnlyList<decimal> RecentWeeklySpots => _weeklySpots;

        /// <summary>Raised whenever <see cref="MonthlyPenPrice"/> changes via <see cref="Update(decimal)"/>.</summary>
        public event Action<decimal>? OnMonthlyPenPriceChanged;

        /// <summary>
        /// Create a Price Council.
        /// </summary>
        /// <param name="basePenPrice">Long-run base pen price in NOK/kg; must be positive.</param>
        /// <param name="bandFraction">Clamp half-width as a fraction of the base (0 = pinned to base, 1 = up to 2×). Must be ≥ 0.</param>
        public PriceCouncil(decimal basePenPrice, decimal bandFraction = DefaultBandFraction)
        {
            if (basePenPrice <= 0m)
                throw new ArgumentOutOfRangeException(nameof(basePenPrice), "Base pen price must be positive.");
            if (bandFraction < 0m)
                throw new ArgumentOutOfRangeException(nameof(bandFraction), "Band fraction cannot be negative.");

            _basePenPrice = basePenPrice;
            _bandFraction = bandFraction;
            MonthlyPenPrice = basePenPrice;
        }

        /// <summary>
        /// Feed one weekly spot observation and recompute the binding monthly pen price.
        /// Returns the new <see cref="MonthlyPenPrice"/>. Raises
        /// <see cref="OnMonthlyPenPriceChanged"/> only when the value changes.
        /// </summary>
        /// <summary>State persistence: the rolling weekly-spot history and the
        /// bound monthly price must survive a save so the council continues
        /// its cadence instead of resetting (Phase 1 wave 6).</summary>
        internal (decimal[] Spots, decimal Monthly) ExportState()
            => (_weeklySpots.ToArray(), MonthlyPenPrice);

        internal void ImportState((decimal[] Spots, decimal Monthly) s)
        {
            _weeklySpots.Clear();
            _weeklySpots.AddRange(s.Spots);
            MonthlyPenPrice = s.Monthly;
        }

        public decimal Update(decimal weeklySpotPrice)
        {
            _weeklySpots.Add(weeklySpotPrice);
            if (_weeklySpots.Count > SmoothingWeeks)
                _weeklySpots.RemoveAt(0);

            var next = Compute(_weeklySpots, _basePenPrice, _bandFraction);
            if (next != MonthlyPenPrice)
            {
                MonthlyPenPrice = next;
                OnMonthlyPenPriceChanged?.Invoke(next);
            }
            return MonthlyPenPrice;
        }

        /// <summary>Feed a batch of weekly spot observations in chronological order.</summary>
        public decimal Update(IReadOnlyList<decimal> weeklySpotPrices)
        {
            if (weeklySpotPrices == null) throw new ArgumentNullException(nameof(weeklySpotPrices));
            var result = MonthlyPenPrice;
            foreach (var spot in weeklySpotPrices)
                result = Update(spot);
            return result;
        }

        /// <summary>Clear the rolling window and reset the binding price to the base.</summary>
        public void Reset()
        {
            _weeklySpots.Clear();
            MonthlyPenPrice = _basePenPrice;
        }

        /// <summary>
        /// Pure smoothing rule: lagged average of the most recent
        /// <see cref="SmoothingWeeks"/> observations, clamped to the band around
        /// <paramref name="basePenPrice"/>. An empty series returns the base price.
        /// </summary>
        public static decimal Compute(
            IReadOnlyList<decimal> recentWeeklySpots,
            decimal basePenPrice,
            decimal bandFraction = DefaultBandFraction)
        {
            if (recentWeeklySpots == null || recentWeeklySpots.Count == 0)
                return basePenPrice;

            var window = recentWeeklySpots.Count <= SmoothingWeeks
                ? recentWeeklySpots
                : recentWeeklySpots.Skip(recentWeeklySpots.Count - SmoothingWeeks).ToList();

            var average = window.Average();
            return ClampToBand(average, basePenPrice, bandFraction);
        }

        /// <summary>
        /// Clamp a candidate price into the council's allowed band around the base
        /// price: [base·(1−band), base·(1+band)]. A negative band is treated as 0.
        /// </summary>
        public static decimal ClampToBand(decimal candidate, decimal basePenPrice, decimal bandFraction)
        {
            var fraction = bandFraction > 0m ? bandFraction : 0m;
            var lower = basePenPrice * (1m - fraction);
            var upper = basePenPrice * (1m + fraction);
            return Math.Clamp(candidate, lower, upper);
        }
    }
}
