using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Sustainability;
using Xunit;

namespace LakskongenSim.Tests.Economy;

/// <summary>
/// Economy ledger regressions (review next-step 8). The headline property:
/// every krone that moves is booked exactly once, so the change in cash over a
/// whole career equals the booked flows, and nothing moves "unbooked".
/// The rest pin the individual findings: F1128 (monthly tax re-taxed YTD
/// profit), F1137 (loss carry-forward), F1131 (resource rent never collected),
/// F1130 (feed waived when broke), F1138 (loan principal as OpEx), F268
/// (certification applied twice), F269 (smolt price bypassed calibration),
/// F1006 (cash achievements paid from starting capital).
/// </summary>
public class EconomyLedgerTests
{
    private const string Scenario = "po3_red_sone";

    private static SimulationEngine NewGame(int seed, bool events = false, string? scenario = Scenario)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        if (scenario != null)
            Assert.True(engine.StartScenario(scenario).Ok);
        return engine;
    }

    private static double SiteMeanWeight(LakskongenSim.Farm.SeaSite site)
    {
        int count = site.Pens.Sum(p => p.TotalCount);
        return count > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / count : 0.0;
    }

    /// <summary>Two-year career through engine verbs only, harvesting sites when ready.</summary>
    private static SimulationEngine PlayCareer(int seed, int days)
    {
        var engine = NewGame(seed, events: true);
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "robust").Ok);
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);
        Assert.True(engine.SetInsurancePolicy("mortality", true, 1.0, 250_000).Ok);
        Assert.True(engine.TakeLoan(4_000_000, 36, 0.07).Ok);
        Assert.True(engine.RequestWellboat("po3_nord", "rov").Ok);
        while (engine.State.Day < days)
        {
            switch (engine.State.Day)
            {
                case 5: engine.AcceptContractOffer(0); break;
                case 170: engine.DelouseSite("po3_nord", "thermolicer"); break;
            }
            engine.Advance(1);
            foreach (var site in engine.State.SeaSites.ToList())
                if (SiteMeanWeight(site) >= 4.5)
                    engine.HarvestSite(site.Id);
        }
        return engine;
    }

    // ── Reconciliation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(21)]
    public void Career_CashChangeEqualsBookedFlows_AndNothingMovesUnbooked(int seed)
    {
        var engine = PlayCareer(seed, days: 730);
        var fs = engine.FinancialStatements;

        Assert.Equal(0m, engine.UnbookedCashTotal);
        // Exact up to decimal rounding of long fractional amounts (~1e-22 NOK).
        decimal gap = engine.FinanceManager.Balance - engine.State.OpeningBalanceNok - fs.LifetimeNetCashFlow;
        Assert.True(System.Math.Abs(gap) < 0.000001m, $"cash and booked flows differ by {gap} NOK");
        Assert.True(fs.LifetimeRevenue > 0m, "career never sold anything");
        Assert.True(fs.LifetimeCOGS > 0m, "feed/smolts never reached COGS");
    }

    // ── Tax (F1128 / F1137 / F1131 / F342) ─────────────────────────────────────

    [Fact]
    public void ProvisionalTax_TrueUpsToTheRate_WithoutRetaxingPaidTax()
    {
        var engine = NewGame(3, scenario: null);
        engine.ReceiveIncome(1_000_000m, "harvest");
        engine.Advance(75);                             // two provisional months

        // The old path re-taxed the YTD figure net of tax every month
        // (220k + 171.6k + ...); provisional tax is a true-up to rate × pre-tax.
        var fs = engine.FinancialStatements;
        Assert.Equal(fs.PreTaxIncome * 0.22m, fs.Taxes);
    }

    [Fact]
    public void YearEndSettlement_ChargesTheRateOnPreTaxIncome_AgainstProvisionalTax()
    {
        var engine = NewGame(3, scenario: null);
        engine.ReceiveIncome(1_000_000m, "harvest");
        engine.Advance(45);                             // provisional tax paid at the first month

        var settlement = engine.SettleAnnualTax();

        Assert.Equal(settlement.PreTaxIncome * 0.22m, settlement.CorporateTax);
        Assert.Equal(settlement.CorporateTax - settlement.ProvisionalPaid, settlement.Settlement);
        Assert.Equal(settlement.CorporateTax, engine.FinancialStatements.LifetimeTaxes);
    }

    [Fact]
    public void ProvisionalTax_IsRefundedWhenTheYearEndsWithoutProfit()
    {
        var engine = NewGame(3, scenario: null);
        engine.ReceiveIncome(1_000_000m, "harvest");
        engine.Advance(45);                             // provisional tax paid at the first month
        Assert.True(engine.FinancialStatements.LifetimeTaxes > 0m);

        // The year's profit disappears before the close.
        engine.PayCost(engine.FinancialStatements.PreTaxIncome, CostBooking.Opex, "operations");
        var settlement = engine.SettleAnnualTax();

        Assert.Equal(0m, settlement.CorporateTax);
        Assert.Equal(0m, engine.FinancialStatements.LifetimeTaxes);   // everything refunded
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void LossCarryForward_OffsetsAtMostEightyPercentOfIncome()
    {
        var tax = new TaxSystem();
        Assert.Equal(0m, tax.ComputeCorporateTax(-1000m));
        Assert.Equal(44m, tax.ComputeCorporateTax(1000m));   // 800 offset, 200 taxed
        Assert.Equal(176m, tax.ComputeCorporateTax(1000m));  // remaining 200 offset
        Assert.Equal(220m, tax.ComputeCorporateTax(1000m));
        Assert.Equal(0m, tax.LossCarryForward);
    }

    [Fact]
    public void ResourceRent_IsChargedOnHarvestValueAboveTheBasicDeduction()
    {
        var engine = NewGame(3, scenario: null);
        engine.EnableResourceRentTax = true;
        decimal normPrice = engine.PenPriceCouncil.MonthlyPenPrice;
        engine.RecordHarvestForResourceRent(2_000_000.0);      // 2,000 t at the norm price

        var settlement = engine.SettleAnnualTax();

        decimal expected = (2_000_000m * normPrice - 70_000_000m) * 0.25m;
        Assert.Equal(expected, settlement.ResourceRentTax);
        Assert.Equal(expected, engine.ResourceRentPaid);
        Assert.Equal(0m, engine.State.ResourceRentHarvestValueThisTaxYear); // reset for the new year
    }

    /// <summary>
    /// Owner decision D16 (S1-03) replaces ResourceRent_IsNotChargedUnlessTheScenarioEnablesIt:
    /// the tax is ON by default (the 70M NOK deduction shields small farms)
    /// and a scenario can opt out explicitly. Asserts both directions where the
    /// old test asserted only the opt-in default.
    /// </summary>
    [Fact]
    public void ResourceRent_IsChargedByDefault_AndAScenarioCanOptOut()
    {
        var engine = NewGame(3, scenario: null);
        Assert.True(engine.EnableResourceRentTax);
        decimal normPrice = engine.PenPriceCouncil.MonthlyPenPrice;
        engine.RecordHarvestForResourceRent(2_000_000.0);
        Assert.Equal((2_000_000m * normPrice - 70_000_000m) * 0.25m, engine.SettleAnnualTax().ResourceRentTax);

        var optedOut = NewGame(3, scenario: null);
        new ScenarioDefinition { Id = "no_rent_tax", EnableResourceRentTax = false }
            .ApplyStartingState(optedOut.State, optedOut);
        Assert.False(optedOut.EnableResourceRentTax);
        optedOut.RecordHarvestForResourceRent(2_000_000.0);
        Assert.Equal(0m, optedOut.SettleAnnualTax().ResourceRentTax);

        // Every shipped scenario runs with the tax (none opts out).
        foreach (var def in engine.Scenario.All.Values)
        {
            var started = NewGame(3, scenario: null);
            def.ApplyStartingState(started.State, started);
            Assert.True(started.EnableResourceRentTax, $"{def.Id}: resource-rent tax off");
        }
    }

    // ── Costs (F1130 / F1138 / F269) ───────────────────────────────────────────

    [Fact]
    public void Feed_IsPaidOnCreditWhenCashIsShort_NeverWaived()
    {
        var engine = NewGame(5);
        engine.FinanceManager.SetBalanceForLoad(0m);
        engine.ResetLedgerBaseline();
        decimal cogsBefore = engine.FinancialStatements.LifetimeCOGS;

        engine.Advance(30);

        decimal feed = engine.FinancialStatements.LifetimeCOGS - cogsBefore;
        Assert.True(feed > 0m, "no feed was booked");
        Assert.True(engine.FinanceManager.Balance <= -feed, "feed was booked but its cash never left");
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void Loan_IsALiability_PrincipalIsNotAnExpense_InterestIs()
    {
        var engine = NewGame(5, scenario: null);
        Assert.True(engine.TakeLoan(1_200_000, 12, 0.10).Ok);
        var fs = engine.FinancialStatements;
        Assert.Equal(1_200_000m, fs.Liabilities);
        Assert.Equal(0m, fs.LifetimeRevenue);

        engine.Advance(60);

        Assert.True(fs.InterestExpense > 0m, "interest was not booked as interest");
        Assert.True(fs.LifetimeLiabilitiesRepaid > 0m, "no principal was repaid");
        Assert.False(fs.CostBreakdown.ContainsKey("loan_principal"), "principal leaked into the P&L");
        decimal drift = 1_200_000m - fs.LifetimeLiabilitiesRepaid - fs.Liabilities;
        Assert.True(System.Math.Abs(drift) < 0.000001m, $"liability off by {drift}");
    }

    [Fact]
    public void Loan_IsPricedAtLeastAtTheCalibratedRate()
    {
        var engine = NewGame(5, scenario: null);
        Assert.True(engine.TakeLoan(1_000_000, 12, 0.0).Ok);
        Assert.Equal(0.055m, engine.State.ActiveLoans.Single().InterestRate);
    }

    [Fact]
    public void Loan_BeyondTheCreditLimit_IsRefused_AndMovesNoCash()
    {
        var engine = NewGame(5, scenario: null);
        decimal cash = engine.FinanceManager.Balance;
        decimal limit = engine.LoanCapacityNok();
        Assert.Equal(cash * 3.5m, limit);   // no biomass, no debt: 3.5 × cash

        var refused = engine.TakeLoan((double)limit + 1, 12, 0.06);

        Assert.False(refused.Ok);
        Assert.Equal("credit_limit", refused.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Empty(engine.State.ActiveLoans);
    }

    [Fact]
    public void StockSmolts_ChargesTheCalibratedSmoltPrice_AsCogs()
    {
        var engine = NewGame(5);
        decimal before = engine.FinanceManager.Balance;

        Assert.True(engine.StockSmolts("po3_nord", 1_000, "standard").Ok);

        Assert.Equal(12_000m, before - engine.FinanceManager.Balance);   // 1,000 × 12 NOK × 1.0
        Assert.Equal(12_000m, engine.FinancialStatements.LifetimeCOGS);
    }

    // ── Revenue (F268) ─────────────────────────────────────────────────────────

    [Fact]
    public void Harvest_WithoutContracts_IsPricedAtSpot_CertificationAppliedOnce()
    {
        var engine = NewGame(5);
        decimal ascMultiplier = engine.SustainabilityManager.GetPriceMultiplier(CertificationLevel.ASCCertified);
        engine.SustainabilityManager.ImportState(80, CertificationLevel.ASCCertified);
        engine.MarketSimulation.UpdateCertificationLevel(CertificationLevel.ASCCertified, ascMultiplier);
        var site = engine.State.SeaSites.First(s => s.Pens.Sum(p => p.TotalCount) > 0);
        decimal spot = engine.MarketSimulation.PricePerKg;

        var harvest = engine.HarvestSite(site.Id);

        Assert.True(harvest.Ok);
        Assert.Equal((decimal)harvest.Value * spot, (decimal)harvest.Value2, 2);
    }

    // ── Achievements (F1006) ───────────────────────────────────────────────────

    public static System.Collections.Generic.IEnumerable<object[]> UnlockedScenarios()
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: 1);
        return probe.Scenario.GetUnlocked().Select(s => new object[] { s.Id }).ToList();
    }

    [Theory]
    [MemberData(nameof(UnlockedScenarios))]
    public void CashAchievements_DoNotUnlockFromStartingCapital(string scenario)
    {
        var engine = NewGame(5, scenario: scenario);
        engine.Advance(1);

        string[] cashAchievements =
        {
            "million-revenue", "five-million-revenue", "ten-million-revenue",
            "speedrun-millionaire", "hundred-million", "profitable-quarter"
        };
        var unlocked = cashAchievements.Where(engine.State.UnlockedAchievementIds.Contains).ToList();
        Assert.True(unlocked.Count == 0, $"{scenario}: unlocked on day 1 without earning anything: {string.Join(", ", unlocked)}");
    }
}
