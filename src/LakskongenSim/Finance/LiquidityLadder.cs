using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;

namespace LakskongenSim.Finance
{
    /// <summary>Where the bank's rescue offer stands.</summary>
    public enum RescueOfferStatus
    {
        /// <summary>The offer stands until <see cref="RescueOffer.ExpiresDay"/>.</summary>
        Open = 0,
        /// <summary>The company took the loan.</summary>
        Accepted = 1,
        /// <summary>The company said no.</summary>
        Declined = 2,
        /// <summary>Nobody answered before the deadline.</summary>
        Expired = 3,
        /// <summary>The bank withdrew it: the cash recovered before an answer.</summary>
        Withdrawn = 4
    }

    /// <summary>The bank's rescue loan offer at a bank call (U2-03).</summary>
    public sealed class RescueOffer
    {
        /// <summary>Game day of the bank call.</summary>
        public int OfferedDay { get; set; }
        /// <summary>Last day the company can take it.</summary>
        public int ExpiresDay { get; set; }
        /// <summary>Principal (NOK).</summary>
        public decimal AmountNok { get; set; }
        /// <summary>Annual interest (fraction).</summary>
        public double InterestRate { get; set; }
        /// <summary>Up-front fee (fraction of the principal).</summary>
        public double ArrangementFee { get; set; }
        /// <summary>Term in calendar months.</summary>
        public int TermMonths { get; set; }
        /// <summary>Open, accepted, declined, expired or withdrawn.</summary>
        public RescueOfferStatus Status { get; set; }
        /// <summary>Day it was answered, expired or withdrawn (-1 while open).</summary>
        public int ResolvedDay { get; set; } = -1;
        /// <summary>The loan it became (empty unless accepted).</summary>
        public string LoanId { get; set; } = "";

        /// <summary>The up-front fee in NOK.</summary>
        public decimal FeeNok => decimal.Round(AmountNok * (decimal)ArrangementFee, 2);
    }

    /// <summary>
    /// Saved state of the liquidity ladder (U2-03). Additive in the save:
    /// a save without it starts with an empty history and no episode.
    /// </summary>
    public sealed class LiquidityState
    {
        /// <summary>Closing cash of the last days, oldest first (window + 1 values).</summary>
        public List<decimal> CashHistory { get; set; } = new();
        /// <summary>Day the current episode began (its first rung), -1 when none runs.</summary>
        public int EpisodeStartDay { get; set; } = -1;
        /// <summary>Day of this episode's liquidity warning (-1 = not yet).</summary>
        public int WarningDay { get; set; } = -1;
        /// <summary>Day of this episode's bank call (-1 = not yet).</summary>
        public int BankCallDay { get; set; } = -1;
        /// <summary>Liquidity warnings over the whole game.</summary>
        public int WarningsIssued { get; set; }
        /// <summary>Bank calls over the whole game.</summary>
        public int BankCallsIssued { get; set; }
        /// <summary>The rescue offer of the latest bank call (null before the first).</summary>
        public RescueOffer? Offer { get; set; }
        /// <summary>Rescue loans taken over the whole game.</summary>
        public int RescueLoansTaken { get; set; }
        /// <summary>Interest paid on negative cash over the whole game (NOK).</summary>
        public decimal OverdraftInterestPaidNok { get; set; }
    }

    /// <summary>What the bank reads off the cash: its burn and the days left before the floor.</summary>
    public readonly record struct LiquidityForecast(
        decimal CashNok,
        decimal FloorNok,
        decimal BurnPerDayNok,
        double RunwayDays,
        int ProjectedFloorDay,
        int HistoryDays)
    {
        /// <summary>True when cash falls by day (a positive burn) and the history is long enough to say so.</summary>
        public bool IsDeclining => BurnPerDayNok > 0m;

        /// <summary>Headroom to the floor (NOK, never negative).</summary>
        public decimal HeadroomNok => Math.Max(0m, CashNok - FloorNok);
    }

    /// <summary>
    /// The bank's reading of the cash (pure, deterministic; no RNG, no clock).
    /// The burn is the median of the rolling week-long changes in the window,
    /// per day. A week is long enough to hold the running lumps (a storm
    /// repair every week or so, a monthly bill) that a median of single days
    /// skips, and the median of the overlapping weeks still ignores a one-off:
    /// a harvest payment or a site bought touches only the weeks around it.
    /// </summary>
    public static class LiquidityRules
    {
        /// <summary>
        /// Median cash fall per day over the rolling <paramref name="stepDays"/>-day
        /// changes of the history (positive = falling); 0 when the history is
        /// shorter than one step.
        /// </summary>
        public static decimal BurnPerDay(IReadOnlyList<decimal> history, int stepDays)
        {
            int step = Math.Max(1, stepDays);
            if (history == null || history.Count <= step) return 0m;
            var changes = new decimal[history.Count - step];
            for (int i = step; i < history.Count; i++)
                changes[i - step] = history[i] - history[i - step];
            Array.Sort(changes);
            int n = changes.Length;
            decimal median = n % 2 == 1 ? changes[n / 2] : (changes[n / 2 - 1] + changes[n / 2]) / 2m;
            return -median / step;
        }

        /// <summary>
        /// Today's forecast: burn, runway to <paramref name="floorNok"/> and the
        /// day the floor is reached at that burn. With fewer than
        /// <paramref name="minHistoryDays"/> days of history, or cash not
        /// falling, the runway is infinite (and the projected day -1).
        /// </summary>
        public static LiquidityForecast Forecast(IReadOnlyList<decimal> history, decimal floorNok, int today,
            int minHistoryDays, int stepDays)
        {
            int days = Math.Max(0, (history?.Count ?? 0) - 1);
            decimal cash = history != null && history.Count > 0 ? history[^1] : 0m;
            decimal burn = days >= Math.Max(stepDays, minHistoryDays) ? BurnPerDay(history!, stepDays) : 0m;
            if (burn <= 0m)
                return new LiquidityForecast(cash, floorNok, Math.Max(0m, burn), double.PositiveInfinity, -1, days);
            decimal headroom = Math.Max(0m, cash - floorNok);
            double runway = (double)(headroom / burn);
            int floorDay = today + (int)Math.Ceiling(runway);
            return new LiquidityForecast(cash, floorNok, burn, runway, floorDay, days);
        }

        /// <summary>
        /// The rescue loan the bank offers: <see cref="LiquidityCalibration.RescueLoanCoverDays"/>
        /// of today's burn rounded up, at most the collateral cap; 0 (no offer)
        /// when the cap carries less than <see cref="LiquidityCalibration.RescueLoanMinCoverDays"/>
        /// of burn.
        /// </summary>
        public static decimal RescueAmount(decimal burnPerDay, decimal collateralCapNok, LiquidityCalibration rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            if (burnPerDay <= 0m || collateralCapNok <= 0m) return 0m;
            if (collateralCapNok < burnPerDay * rules.RescueLoanMinCoverDays) return 0m;
            decimal step = rules.RescueLoanRoundingNok > 0m ? rules.RescueLoanRoundingNok : 1m;
            decimal wanted = Math.Ceiling(burnPerDay * rules.RescueLoanCoverDays / step) * step;
            decimal capped = Math.Floor(collateralCapNok / step) * step;
            decimal amount = Math.Min(wanted, capped);
            return amount >= burnPerDay * rules.RescueLoanMinCoverDays ? amount : 0m;
        }

        /// <summary>
        /// What the bank lends against: <paramref name="advanceRate"/> of the
        /// standing biomass value less the drawn facility (negative cash) and
        /// the loans already outstanding.
        /// </summary>
        public static decimal CollateralCap(decimal biomassValueNok, decimal cashNok, decimal loanPrincipalNok, double advanceRate)
            => Math.Max(0m, biomassValueNok * (decimal)advanceRate - Math.Max(0m, -cashNok) - Math.Max(0m, loanPrincipalNok));
    }
}
