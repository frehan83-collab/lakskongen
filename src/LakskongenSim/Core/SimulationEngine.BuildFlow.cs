using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// The family-first build flow's reads (roadmap F2-31, docs/FARM_TYPES_SPEC.md
/// §6.1-6.2): the build preview of a variant on a plot (the land or sea
/// lane's own preview, plus the capex the company pays, the capacity cost
/// with the licence, the farming cost, the first harvest, the payback year
/// and a 36-month cash runway), the site lens's plots, and the diagnosis
/// strip's constraints ("what limits you", spec §1). All pure: nothing
/// changes, nothing draws, so a ghost placement and a preview leave the
/// engine's hash where it was. The UI holds none of these numbers.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Months the build preview's cash runway covers (spec §6.2: a 36-month runway).</summary>
    public const int BuildRunwayMonths = 36;

    /// <summary>Why a build preview has no economics.</summary>
    public static class BuildFlowReasons
    {
        /// <summary>No such catalogue variant.</summary>
        public const string UnknownVariant = "unknown_variant";
        /// <summary>A Sjø variant: the fjord builds through the zone's plan (<see cref="PlanSiteBuild"/>).</summary>
        public const string NotAPlotBuild = "not_a_plot_build";
    }

    // ── The preview ────────────────────────────────────────────────────────

    /// <summary>
    /// The build preview of <paramref name="variantId"/> on
    /// <paramref name="plotId"/> under <paramref name="contract"/>. A land
    /// variant reads <see cref="PreviewLandBuild"/> (the HFS variant's pilot lot
    /// unless <paramref name="template"/> says the full lot), an offshore one
    /// <see cref="PreviewOffshoreBuild"/> at <paramref name="equityShare"/>;
    /// <paramref name="mitigations"/> price a land plan's permit cases.
    /// </summary>
    public FarmBuildPreview PreviewFarmBuild(string variantId, string plotId, ContractForm contract = ContractForm.Managed,
        double equityShare = 1.0, IReadOnlyList<string>? mitigations = null, LandTemplate? template = null)
    {
        variantId ??= "";
        string family = FamilyOfVariant(variantId);
        var empty = new FarmBuildEconomics { VariantId = variantId, PlotId = plotId ?? "" };
        if (family == "")
            return new FarmBuildPreview(false, BuildFlowReasons.UnknownVariant, "", null, null, null, empty);
        if (family == FarmTypeIds.Sjo || State == null)
            return new FarmBuildPreview(false, BuildFlowReasons.NotAPlotBuild, family, null, null, null, empty);
        if (family == FarmTypeIds.Land)
        {
            var t = LandTemplateFor(variantId, template);
            var land = PreviewLandBuild(plotId ?? "", t, contract, mitigations);
            return new FarmBuildPreview(true, "", family, t, land, null, LandEconomics(variantId, land));
        }
        var type = variantId == FarmTypeIds.Havfarm ? OffshoreType.ShipShaped : OffshoreType.SemiSubmersible;
        var sea = PreviewOffshoreBuild(plotId ?? "", type, contract, equityShare);
        return new FarmBuildPreview(true, "", family, null, null, sea, OffshoreEconomics(variantId, sea));
    }

    /// <summary>
    /// The unlock gates a family's build path waives when it starts: money
    /// (paid phase by phase), on land the zoned pad (the permit package zones
    /// the chosen pad), at sea the development licence (F2-09's round; until it
    /// lands the build stands in for the award). The same rules as
    /// <see cref="ApplyLandPermits"/> and <see cref="BuildOffshoreUnit"/>.
    /// </summary>
    public static IReadOnlyList<FarmTypeGateKind> StartWaivedGates(string familyId) => familyId switch
    {
        FarmTypeIds.Land => new[] { FarmTypeGateKind.ZonedPad, FarmTypeGateKind.Funds },
        FarmTypeIds.Hav => new[] { FarmTypeGateKind.DevelopmentLicence, FarmTypeGateKind.Funds },
        _ => new[] { FarmTypeGateKind.Funds },
    };

    /// <summary>True when every gate of <paramref name="variantId"/> its build path does not waive is met (the player may start it).</summary>
    public bool CanStartFarmType(string variantId)
    {
        var status = FarmTypeStatus(variantId);
        if (status == null) return false;
        var waived = StartWaivedGates(status.Variant.FamilyId);
        return status.Gates.Where(g => !waived.Contains(g.Gate.Kind)).All(g => g.Met);
    }

    /// <summary>The family of a catalogue variant ("" for an unknown id).</summary>
    public static string FamilyOfVariant(string? variantId) => variantId switch
    {
        FarmTypeIds.OpenPen or FarmTypeIds.ClosedPen => FarmTypeIds.Sjo,
        FarmTypeIds.PostSmolt or FarmTypeIds.Hfs or FarmTypeIds.Ras => FarmTypeIds.Land,
        FarmTypeIds.Of1 or FarmTypeIds.Havfarm => FarmTypeIds.Hav,
        _ => "",
    };

    /// <summary>The land template a land variant builds: the HFS variant's pilot lot unless the full lot is asked for.</summary>
    public static LandTemplate LandTemplateFor(string variantId, LandTemplate? asked = null) => variantId switch
    {
        FarmTypeIds.PostSmolt => LandTemplate.PostSmoltModule,
        FarmTypeIds.Ras => LandTemplate.RasHall,
        _ => asked == LandTemplate.HfsLot ? LandTemplate.HfsLot : LandTemplate.HfsPilotLot,
    };

    private FarmBuildEconomics LandEconomics(string variantId, LandBuildPreview land)
    {
        // Permits come first: the build starts when the slowest needed case is decided.
        int wait = land.Permits
            .Where(p => p.Status is "needed" or "refused")
            .Select(p => (p.Quote.MinDays + p.Quote.MaxDays) / 2)
            .Concat(land.Permits.Where(p => p.Status == "pending").Select(p => Math.Max(0, p.DecisionDay - State.Day)))
            .DefaultIfEmpty(0).Max();
        decimal fees = land.Permits.Where(p => p.Status is "needed" or "refused").Sum(p => p.Quote.FeeNok);
        var payments = land.Payments
            .Select(p => new BuildCashStep(p.StartDay + wait, p.PaymentNok, 0m, 0m))
            .Concat(land.Payments.Select(p => new BuildCashStep(p.EndDay + wait, 0m, p.OverrunP50Nok, p.OverrunP90Nok)))
            .Append(new BuildCashStep(State.Day, fees, 0m, 0m))
            .ToList();
        decimal capex = land.Payments.Sum(p => p.PaymentNok);
        // A post-smolt module grows fish for the company's own pens: no HOG of its own to pay it back.
        bool sellsHog = land.Template != LandTemplate.PostSmoltModule;
        return Economics(new BuildMoney(variantId, land.PlotId, capex, land.OverrunP50Nok, land.OverrunP90Nok, 0m, fees,
            land.ProductionTonnes, CatalogueOpex(variantId), wait, State.Day + wait, land.FirstHarvestDay + wait,
            1.0, LandLifeYears(), payments, sellsHog));
    }

    private FarmBuildEconomics OffshoreEconomics(string variantId, OffshoreBuildPreview sea)
    {
        var payments = sea.Payments
            .Select(p => new BuildCashStep(p.PlannedStartDay, p.PaymentNok, 0m, 0m))
            .Concat(sea.Payments.Select(p => new BuildCashStep(p.PlannedStartDay + p.PlannedDays, 0m, p.OverrunMedianNok, p.OverrunP90Nok)))
            .ToList();
        var status = FarmTypeStatus(variantId);
        double capacity = status?.Variant.Axis(FarmTypeIds.Capacity).Median ?? 0.0;
        decimal teething = sea.TeethingBudgetNok * (decimal)sea.EquityShare;
        int firstHarvest = sea.FirstStockingDay + DaysToHarvestFrom(sea.FirstStockingDay, HomeProductionArea);
        return Economics(new BuildMoney(variantId, sea.PlotId, sea.CompanyCapexNok, sea.OverrunMedianNok, sea.OverrunP90Nok,
            sea.ConversionFeeNok * (decimal)sea.EquityShare, teething, capacity, CatalogueOpex(variantId), 0, State.Day,
            firstHarvest, sea.EquityShare, OffshoreLifeYears(), payments, true));
    }

    /// <summary>One dated cash movement of a build: a planned payment, and the overrun's median and 90th percentile.</summary>
    private readonly record struct BuildCashStep(int Day, decimal PaymentNok, decimal OverrunP50Nok, decimal OverrunP90Nok);

    /// <summary>What the economics are computed from (the company's share of every money figure).</summary>
    private sealed record BuildMoney(string VariantId, string PlotId, decimal CapexNok, decimal OverrunP50Nok,
        decimal OverrunP90Nok, decimal LicenceNok, decimal StartupNok, double CapacityTonnesPerYear, double? FarmingCostNokPerKgHog,
        int PermitWaitDays, int StartDay, int FirstHarvestDay, double EquityShare, int LifeYears,
        IReadOnlyList<BuildCashStep> Steps, bool SellsHog);

    private FarmBuildEconomics Economics(BuildMoney m)
    {
        double equity = m.EquityShare > 0.0 ? m.EquityShare : 1.0;
        decimal unitCost = (m.CapexNok + m.OverrunP50Nok + m.StartupNok + m.LicenceNok) / (decimal)equity;
        double capacityCost = m.CapacityTonnesPerYear > 0.0 ? (double)unitCost / m.CapacityTonnesPerYear : 0.0;
        double price = (double)(MarketSimulation?.WeeklyAvgPrice ?? 0m);
        decimal yearlyMargin = m.SellsHog && m.FarmingCostNokPerKgHog is double cost
            ? (decimal)(m.CapacityTonnesPerYear * 1000.0 * (price - cost) * equity)
            : 0m;
        decimal invested = m.CapexNok + m.OverrunP50Nok + m.StartupNok + m.LicenceNok;
        int payback = yearlyMargin > 0m
            ? m.FirstHarvestDay + (int)Math.Ceiling((double)(invested / yearlyMargin) * SimCalendar.DaysPerYear)
            : -1;
        var runway = Runway(m, yearlyMargin, out decimal loan, out double rate, out int termMonths, out decimal room);
        var low = runway.OrderBy(r => r.CashP50Nok).ThenBy(r => r.Month).First();
        return new FarmBuildEconomics
        {
            VariantId = m.VariantId,
            PlotId = m.PlotId,
            CompanyCapexNok = m.CapexNok,
            OverrunP50Nok = m.OverrunP50Nok,
            OverrunP90Nok = m.OverrunP90Nok,
            LicenceNok = m.LicenceNok,
            StartupCostsNok = m.StartupNok,
            CapacityTonnesPerYear = m.CapacityTonnesPerYear,
            CapacityCostNokPerTonneYear = capacityCost,
            FarmingCostNokPerKgHog = m.FarmingCostNokPerKgHog,
            PriceNokPerKgHog = price,
            PermitWaitDays = m.PermitWaitDays,
            StartDay = m.StartDay,
            FirstHarvestDay = m.FirstHarvestDay,
            PaybackDay = payback,
            PaybackYear = payback >= 0 ? SimCalendar.Year(payback) : -1,
            YearlyMarginNok = yearlyMargin,
            EquityShare = equity,
            OperatingNokPerMonth = OperatingCashPerMonthNok(),
            LoanNok = loan,
            LoanRate = rate,
            LoanTermMonths = termMonths,
            LoanRoomNok = room,
            Runway = runway,
            LowestCashP50Nok = low.CashP50Nok,
            LowestCashP90Nok = runway.Min(r => r.CashP90Nok),
            LowestMonth = low.Month,
        };
    }

    /// <summary>
    /// The 36 months: today's cash, plus the existing business's operating
    /// cash at its average so far, less the build's payments and overrun as
    /// they fall due, plus the new unit's margin from its first harvest. The
    /// pre-filled loan is the bank's room up to the median line's lowest
    /// shortfall, drawn today and paid down in equal slices over the unit's
    /// depreciation life with interest on what is left (the engine's own loan).
    /// </summary>
    private List<BuildRunwayMonth> Runway(BuildMoney m, decimal yearlyMargin, out decimal loan, out double rate,
        out int termMonths, out decimal room)
    {
        int today = State.Day;
        decimal cash0 = FinanceManager?.Balance ?? 0m;
        decimal operating = OperatingCashPerMonthNok();
        var months = new List<(int Day, decimal Payments, decimal P50, decimal P90, decimal Op, decimal Unit)>();
        int previous = today;
        for (int i = 1; i <= BuildRunwayMonths; i++)
        {
            int end = SimCalendar.AddMonths(today, i);
            // Month 1 also takes anything due today; month i the days [its start, its end).
            int from = i == 1 ? int.MinValue : previous;
            var due = m.Steps.Where(s => s.Day >= from && s.Day < end).ToList();
            decimal pay = due.Sum(s => s.PaymentNok);
            decimal p50 = due.Sum(s => s.OverrunP50Nok);
            decimal p90 = due.Sum(s => s.OverrunP90Nok);
            int unitDays = Math.Max(0, end - Math.Max(previous, m.FirstHarvestDay));
            decimal unit = yearlyMargin * unitDays / SimCalendar.DaysPerYear;
            months.Add((end, pay, p50, p90, operating, unit));
            previous = end;
        }
        // The median line without a loan, to size the loan against.
        decimal running = cash0;
        decimal lowest = cash0;
        foreach (var mo in months)
        {
            running += mo.Op + mo.Unit - mo.Payments - mo.P50;
            lowest = Math.Min(lowest, running);
        }
        decimal existing = State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal));
        room = Math.Max(0m, LoanCapacityNok() - existing);
        loan = Math.Min(room, Math.Max(0m, -lowest));
        rate = _balancing.LoanInterestRate;
        termMonths = Math.Max(1, m.LifeYears) * SimCalendar.MonthsPerYear;

        var result = new List<BuildRunwayMonth>(months.Count);
        decimal p50Cash = cash0, p90Cash = cash0, loanCash = cash0 + loan, principal = loan;
        int monthIndex = 0;
        int prevDay = today;
        foreach (var mo in months)
        {
            monthIndex++;
            decimal flow = mo.Op + mo.Unit - mo.Payments;
            p50Cash += flow - mo.P50;
            p90Cash += flow - mo.P90;
            int days = mo.Day - prevDay;
            decimal interest = principal * (decimal)rate * days / SimCalendar.DaysPerYear;
            decimal slice = Math.Min(principal, loan / termMonths);
            principal -= slice;
            loanCash += flow - mo.P50 - interest - slice;
            result.Add(new BuildRunwayMonth(monthIndex, mo.Day, p50Cash, p90Cash, loanCash, mo.Payments, mo.Op, mo.Unit));
            prevDay = mo.Day;
        }
        return result;
    }

    /// <summary>
    /// The company's operating cash per average month so far (NOK): revenue
    /// less cost of goods, running costs, interest and tax since the game
    /// began, over the days played (0 on the first day).
    /// </summary>
    public decimal OperatingCashPerMonthNok()
    {
        var fs = FinancialStatements;
        if (fs == null || State == null || State.Day <= 0) return 0m;
        decimal operating = fs.LifetimeRevenue - fs.LifetimeCOGS - fs.LifetimeOperatingExpenses - fs.LifetimeInterest - fs.LifetimeTaxes;
        return operating / State.Day * SimCalendar.DaysPerYear / SimCalendar.MonthsPerYear;
    }

    /// <summary>The catalogue's median farming cost of a variant (NOK/kg HOG), null where no figure is published.</summary>
    private double? CatalogueOpex(string variantId)
        => FarmTypeStatus(variantId)?.Variant.Axis(FarmTypeIds.OpexPerKg).Median;

    private int LandLifeYears() => _calibration.Facilities.Common.Depreciation.LandLifeYears;

    private int OffshoreLifeYears() => _calibration.Facilities.Common.Depreciation.OffshoreLifeYears;

    /// <summary>
    /// Days a standard smolt stocked on <paramref name="startDay"/> takes to the
    /// plan's harvest weight in <paramref name="productionArea"/>'s normal sea
    /// (the S2-03 growth law, the stocking plan's own inputs); the planning
    /// horizon when it never gets there.
    /// </summary>
    internal int DaysToHarvestFrom(int startDay, int productionArea)
    {
        var law = Growth?.Law ?? TgcGrowthLaw.Default;
        int area = ProductionAreas.HasClimatology(productionArea) ? productionArea : ProductionAreas.NationalReference;
        double latitude = ProductionAreas.LatitudeDeg(area);
        double weight = SmoltStrainRegistry.Get("standard").ReleaseWeightKg;
        double cv = law.Calibration.SmoltWeightCv;
        double target = _calibration.Biology.TargetHarvestWeightKg;
        int horizon = _calibration.Capacity.PlanningHorizonDays;
        for (int d = 0; d < horizon; d++)
        {
            if (weight >= target) return d;
            int day = startDay + d;
            var conditions = new GrowthConditions(ProductionAreas.ClimatologyC(area, day),
                DayLengthHours: DayLength.Hours(latitude, SimCalendar.DayOfYear(day)));
            var grown = law.GrowOneDay(weight, cv, LifeStage.Seawater, conditions, satiationFcr: 0.0);
            weight = grown.WeightKg;
            cv = grown.WeightCv;
        }
        return horizon;
    }
}
