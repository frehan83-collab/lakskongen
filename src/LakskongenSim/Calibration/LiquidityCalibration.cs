namespace LakskongenSim.Calibration
{
    /// <summary>
    /// Liquidity ladder (roadmap U2-03, on top of S1-03's solvency ladder):
    /// what running on the bank's working-capital facility costs, when the bank
    /// warns and calls the company in before the bankruptcy floor, and the
    /// rescue loan it offers then. docs/BALANCE.md §1u has the measurements.
    /// </summary>
    public sealed record LiquidityCalibration
    {
        /// <summary>
        /// Interest on negative cash (annual fraction, charged daily on the
        /// day's closing cash, actual/365). Negative cash is unpaid bills and an
        /// overdrawn account: Norway's statutory late-payment interest
        /// (forsinkelsesrente) for the second half of 2025 is 12.25 % p.a.,
        /// Norges Bank's policy rate (4.25 %) plus 8 percentage points
        /// (forsinkelsesrenteloven § 3; Finanstilsynet, "Forsinkelsesrente og
        /// standardkompensasjon fra 1. juli 2025",
        /// https://www.finanstilsynet.no/nyhetsarkiv/nyheter/2025/forsinkelsesrente-og-standardkompensasjon-for-inndrivelseskostnader-fra-1.-juli-2025),
        /// and again 12.25 % from 1 July 2026 (Finanstilsynet,
        /// https://www.finanstilsynet.no/nyhetsarkiv/nyheter/2026/forsinkelsesrente-og-standardkompensasjon-for-inndrivelseskostnader-fra-1.-juli-2026).
        /// It lies above the 5.5 % a term loan pays, so borrowing beats riding
        /// the overdraft (RUN_LOG §8 "overdraft is free").
        /// </summary>
        public double OverdraftInterestRate { get; init; } = 0.1225;

        /// <summary>Days of closing cash the bank reads the burn from.</summary>
        public int BurnWindowDays { get; init; } = 35;

        /// <summary>
        /// The burn is the median of the rolling changes over this many days
        /// (per day): a week holds the running lumps (storm repairs, monthly
        /// bills), the median of the overlapping weeks ignores a one-off.
        /// </summary>
        public int BurnStepDays { get; init; } = 7;

        /// <summary>Days of history needed before the bank forecasts anything (a new or loaded game).</summary>
        public int MinBurnHistoryDays { get; init; } = 14;

        /// <summary>
        /// Liquidity warning: at today's burn the cash reaches the bankruptcy
        /// floor within this many days. Twice the 30-day lead the roadmap asks
        /// for, so an accelerating decline (feed grows with the fish) still
        /// warns 30 days ahead (measured in docs/BALANCE.md §1u).
        /// </summary>
        public int WarningRunwayDays { get; init; } = 60;

        /// <summary>
        /// The bank calls the company in (and offers the rescue loan) at this
        /// runway: four weeks, so the 14-day offer is answered with two weeks
        /// to spare, and a last lump before the floor (a fine, a storm) still
        /// leaves the 10 days the roadmap asks for (measured in docs/BALANCE.md §1u).
        /// </summary>
        public int BankCallRunwayDays { get; init; } = 28;

        /// <summary>The episode ends (both rungs re-arm) once the runway is back above this, or cash stops falling.</summary>
        public int ReArmRunwayDays { get; init; } = 120;

        /// <summary>Days the rescue offer stands (its deadline; the clock slows and stops before it, U2-07).</summary>
        public int RescueOfferValidDays { get; init; } = 14;

        /// <summary>The rescue loan covers this many days of today's burn (rounded up).</summary>
        public int RescueLoanCoverDays { get; init; } = 120;

        /// <summary>No offer when the collateral carries less than this many days of burn.</summary>
        public int RescueLoanMinCoverDays { get; init; } = 30;

        /// <summary>
        /// Rescue loan interest (annual fraction): three-month NIBOR (about
        /// 4.5 % in 2025: 4.68 % at the end of 2024, the policy rate 4.5 % until
        /// June 2025) plus the 9.5 percentage-point margin of a senior secured
        /// fish-farm bond placed in June 2025 (a land-based salmon farmer's
        /// NOK 750 million, three-year bond: margin 950 bps, issued at 98 % of
        /// nominal; company release 2025-06-11,
        /// https://www.placera.se/pressmeddelanden/andfjord-salmon-group-andfjord-salmon-group-as--successful-bond-issue-20250611).
        /// </summary>
        public double RescueLoanInterestRate { get; init; } = 0.14;

        /// <summary>Up-front fee (fraction of the loan): that bond's 98 % issue price.</summary>
        public double RescueLoanArrangementFee { get; init; } = 0.02;

        /// <summary>Term (months): that bond's three-year tenor; repaid daily like every loan.</summary>
        public int RescueLoanTermMonths { get; init; } = 36;

        /// <summary>
        /// The bank lends against the fish in the sea: at most this share of
        /// the standing biomass at the norm price, less the drawn facility and
        /// the loans already outstanding. Biomass working-capital lines are
        /// sized on the value of the insured fish (an Icelandic sea farmer's
        /// 2023 bank package: a EUR 70 million revolver whose borrowing base
        /// is the value of insured biomass and eligible receivables, Fish
        /// Farmer, 2023, https://www.fishfarmermagazine.com/?p=145908); no
        /// public advance rate exists, so the game uses a conservative half
        /// (a design value).
        /// </summary>
        public double RescueLoanAdvanceRate { get; init; } = 0.5;

        /// <summary>Offers are round sums: the amount is rounded up to this (NOK).</summary>
        public decimal RescueLoanRoundingNok { get; init; } = 500_000m;

        /// <summary>Rescue loans a company can get in one game.</summary>
        public int MaxRescueLoans { get; init; } = 1;
    }
}
