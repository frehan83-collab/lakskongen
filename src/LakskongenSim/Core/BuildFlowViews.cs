using System;
using System.Collections.Generic;
using LakskongenSim.Farm.Land;

namespace LakskongenSim.Core;

/// <summary>
/// One month of a build's 36-month cash runway (roadmap F2-31,
/// docs/FARM_TYPES_SPEC.md §6.2 step 4): the company's cash at the month's
/// end with the build's payments, the median and the 90th-percentile
/// overrun, and with the bank's loan drawn.
/// </summary>
/// <param name="Month">Months from today (1-36).</param>
/// <param name="Day">The sim day the month ends on.</param>
/// <param name="CashP50Nok">Cash with the planned payments and the median overrun.</param>
/// <param name="CashP90Nok">Cash with the planned payments and the 90th-percentile overrun.</param>
/// <param name="CashWithLoanNok">The median line with the pre-filled loan drawn today and paid down.</param>
/// <param name="PaymentsNok">The build's payments in the month (planned, no overrun).</param>
/// <param name="OperatingNok">The existing business's operating cash in the month.</param>
/// <param name="NewUnitNok">The new unit's operating margin in the month (after its first harvest).</param>
public sealed record BuildRunwayMonth(int Month, int Day, decimal CashP50Nok, decimal CashP90Nok,
    decimal CashWithLoanNok, decimal PaymentsNok, decimal OperatingNok, decimal NewUnitNok);

/// <summary>
/// What a farm-type build means for the company's money (roadmap F2-31, spec
/// §6.2 step 4), on top of the lane's own build preview: the capex the
/// company pays, the capacity cost per tonne of yearly HOG capacity with the
/// licence, the farming cost per kg HOG, the first harvest, the payback year
/// and the 36-month cash runway. A pure read: nothing changes, nothing draws.
/// </summary>
public sealed record FarmBuildEconomics
{
    /// <summary>The catalogue variant ("hfs", "of1", …).</summary>
    public string VariantId { get; init; } = "";
    /// <summary>The plot.</summary>
    public string PlotId { get; init; } = "";
    /// <summary>The company's capex: every planned payment, the turnkey premium included (NOK).</summary>
    public decimal CompanyCapexNok { get; init; }
    /// <summary>The company's median overrun (NOK; 0 for turnkey).</summary>
    public decimal OverrunP50Nok { get; init; }
    /// <summary>The company's 90th-percentile overrun (NOK).</summary>
    public decimal OverrunP90Nok { get; init; }
    /// <summary>The licence the capacity needs (NOK): an offshore unit's conversion fees; 0 on land.</summary>
    public decimal LicenceNok { get; init; }
    /// <summary>Other costs of getting started (NOK): permit fees on land, the teething budget at sea.</summary>
    public decimal StartupCostsNok { get; init; }
    /// <summary>Yearly capacity of the build (t HOG; live post-smolt for a post-smolt module).</summary>
    public double CapacityTonnesPerYear { get; init; }
    /// <summary>Capacity cost of the unit (NOK per tonne of yearly capacity): capex, median overrun, start-up costs and the licence.</summary>
    public double CapacityCostNokPerTonneYear { get; init; }
    /// <summary>Farming cost of the variant (NOK/kg HOG), the catalogue's median; null where no figure is published.</summary>
    public double? FarmingCostNokPerKgHog { get; init; }
    /// <summary>The market's weekly mean price (NOK/kg HOG) the payback is read at.</summary>
    public double PriceNokPerKgHog { get; init; }
    /// <summary>Days the permits take before the build can start (the slowest needed case's mid-window; 0 when none).</summary>
    public int PermitWaitDays { get; init; }
    /// <summary>The day the build would start (today plus the permit wait).</summary>
    public int StartDay { get; init; }
    /// <summary>The planned first harvest (sim day).</summary>
    public int FirstHarvestDay { get; init; }
    /// <summary>The day the company has its capex and median overrun back, or -1 when the margin never pays it.</summary>
    public int PaybackDay { get; init; } = -1;
    /// <summary>The calendar year of <see cref="PaybackDay"/>, or -1.</summary>
    public int PaybackYear { get; init; } = -1;
    /// <summary>The company's share of the unit's yearly operating margin at capacity (NOK).</summary>
    public decimal YearlyMarginNok { get; init; }
    /// <summary>The company's share of the build (1 alone; the consortium's equity share).</summary>
    public double EquityShare { get; init; } = 1.0;
    /// <summary>The existing business's operating cash per month so far (NOK; revenue less running costs, interest and tax).</summary>
    public decimal OperatingNokPerMonth { get; init; }
    /// <summary>The pre-filled loan: the bank's room, up to what keeps the median line above zero (NOK).</summary>
    public decimal LoanNok { get; init; }
    /// <summary>The loan's annual rate (the bank's lending rate).</summary>
    public double LoanRate { get; init; }
    /// <summary>The loan's term in months (the unit's depreciation life).</summary>
    public int LoanTermMonths { get; init; }
    /// <summary>The bank's loan room today (NOK).</summary>
    public decimal LoanRoomNok { get; init; }
    /// <summary>The 36 months.</summary>
    public IReadOnlyList<BuildRunwayMonth> Runway { get; init; } = Array.Empty<BuildRunwayMonth>();
    /// <summary>The lowest median cash over the runway (NOK).</summary>
    public decimal LowestCashP50Nok { get; init; }
    /// <summary>The lowest 90th-percentile cash over the runway (NOK).</summary>
    public decimal LowestCashP90Nok { get; init; }
    /// <summary>The month of <see cref="LowestCashP50Nok"/>.</summary>
    public int LowestMonth { get; init; }
}

/// <summary>
/// The build preview of a farm-type variant on a plot (F2-31): the lane's own
/// preview (land or offshore) and the money around it. Exactly one of
/// <see cref="Land"/> and <see cref="Offshore"/> is set when
/// <see cref="Ok"/>.
/// </summary>
/// <param name="Ok">False for an unknown variant or a Sjø variant (the fjord builds through the zone's plan).</param>
/// <param name="ReasonKey">"" or why there is no preview ("unknown_variant", "not_a_plot_build").</param>
/// <param name="FamilyId">The variant's family.</param>
/// <param name="Template">The land template built (land variants).</param>
/// <param name="Land">The land lane's preview.</param>
/// <param name="Offshore">The sea lane's preview.</param>
/// <param name="Economics">The money.</param>
public sealed record FarmBuildPreview(bool Ok, string ReasonKey, string FamilyId, LandTemplate? Template,
    LandBuildPreview? Land, OffshoreBuildPreview? Offshore, FarmBuildEconomics Economics);

/// <summary>
/// One constraint of the company's farming today, as the build dialog's
/// diagnosis strip names it (F2-31, spec §1 "what limits you", §6.2).
/// </summary>
/// <param name="Key">"lice", "capacity", "temperature", "oxygen" or "capital".</param>
/// <param name="Score">How hard it binds (0-1).</param>
/// <param name="ProductionArea">The area it binds in (0 for the company as a whole).</param>
/// <param name="Value">The reading (a traffic-light index 0-2, a share, °C, NOK).</param>
/// <param name="Reference">What the reading is held against (the limit, the optimum, the entry rung).</param>
/// <param name="RelievedBy">The families that remove it ("land", "hav"); empty when none does.</param>
public sealed record BuildConstraint(string Key, double Score, int ProductionArea, double Value, double Reference,
    IReadOnlyList<string> RelievedBy);

/// <summary>The build dialog's diagnosis: the constraints, the binding one first.</summary>
/// <param name="Constraints">Every constraint, by score (highest first), ties by key.</param>
public sealed record BuildDiagnosis(IReadOnlyList<BuildConstraint> Constraints)
{
    /// <summary>The binding constraint, or null when the company farms nothing yet.</summary>
    public BuildConstraint? Binding => Constraints.Count > 0 ? Constraints[0] : null;
}

/// <summary>
/// One plot as the build dialog's site lens reads it (F2-31, spec §6.2 step
/// 2): the pad's intake, pumps, grid, zoning, nature sensitivity and blasting;
/// the slot's exposure, sea and lice; free or held.
/// </summary>
public sealed record BuildPlotView
{
    /// <summary>The plot.</summary>
    public string Id { get; init; } = "";
    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";
    /// <summary>"land" or "hav".</summary>
    public string FamilyId { get; init; } = "";
    /// <summary>Free to build on.</summary>
    public bool IsFree { get; init; }
    /// <summary>The unit holding it ("" when free).</summary>
    public string OwnerUnitId { get; init; } = "";
    /// <summary>World position (m).</summary>
    public double WorldX { get; init; }
    /// <summary>World position (m).</summary>
    public double WorldZ { get; init; }
    /// <summary>The real distance it stands for (km).</summary>
    public double RealDistanceKm { get; init; }
    /// <summary>Pad size (m).</summary>
    public double PadWidthM { get; init; }
    /// <summary>Pad size (m).</summary>
    public double PadDepthM { get; init; }
    /// <summary>Grow-out lots the pad takes.</summary>
    public int GrowOutLots { get; init; }
    /// <summary>Post-smolt modules the pad takes.</summary>
    public int PostSmoltModules { get; init; }
    /// <summary>Reach of the deep intake (m).</summary>
    public double DeepIntakeReachM { get; init; }
    /// <summary>Pump capacity per lot (m³/s).</summary>
    public double PumpCapacityM3sPerLot { get; init; }
    /// <summary>Grid capacity with finished projects (MW).</summary>
    public double GridCapacityMW { get; init; }
    /// <summary>Grid feeds with finished projects.</summary>
    public int GridFeeds { get; init; }
    /// <summary>Zoned for aquaculture (pre-zoned or by an approved case).</summary>
    public bool Zoned { get; init; }
    /// <summary>Pre-zoned in the scenario.</summary>
    public bool PreZoned { get; init; }
    /// <summary>Nature sensitivity (0-1), the permit odds' driver.</summary>
    public double NatureSensitivity { get; init; }
    /// <summary>Blasting the pad (NOK).</summary>
    public decimal BlastCostNok { get; init; }
    /// <summary>Exposure class ("" on a pad).</summary>
    public string Exposure { get; init; } = "";
    /// <summary>Water depth (m).</summary>
    public double WaterDepthM { get; init; }
    /// <summary>100-year significant wave height (m), the sea model's own for the three offshore slots.</summary>
    public double Hs100M { get; init; }
    /// <summary>Mean daily Hs in January at the plot (m).</summary>
    public double WinterMeanHsM { get; init; }
    /// <summary>Mean daily Hs in July at the plot (m).</summary>
    public double SummerMeanHsM { get; init; }
    /// <summary>External lice pressure: the low end of the band (or the revealed value).</summary>
    public double LiceExternalMin { get; init; }
    /// <summary>External lice pressure: the high end of the band (or the revealed value).</summary>
    public double LiceExternalMax { get; init; }
}
