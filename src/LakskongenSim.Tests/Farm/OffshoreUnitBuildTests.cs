using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-07's build (roadmap F2-07, docs/FARM_TYPES_SPEC.md §3.4
/// <c>BuildOffshoreUnit</c>, §6.2 the preview, §6.3 the offshore phases):
/// the refusals, the contract and its stage payments, the yard, voyage and
/// the player's tow (acceptance 4), commissioning, the consortium
/// (acceptance 6) and its buy-out.
/// </summary>
public class OffshoreUnitBuildTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;

    [Fact]
    public void Build_RefusesBeforeAnyChange_WithItsKey()
    {
        var locked = OffshoreTestUnits.RichEngine(3);
        void Refused(SimulationEngine engine, string key, string plot, OffshoreType type = OffshoreType.SemiSubmersible,
            ContractForm contract = ContractForm.Managed, double equity = 1.0)
        {
            decimal cash = engine.FinanceManager.Balance;
            int sites = engine.State.SeaSites.Count, projects = engine.State.ConstructionProjects.Count, plots = engine.State.SitePlots.Count;
            var result = engine.BuildOffshoreUnit(plot, type, contract, equity);
            Assert.False(result.Ok, $"{key}: the build went through");
            Assert.Equal(key, result.ErrorKey);
            Assert.Equal(key, engine.PreviewOffshoreBuild(plot, type, contract, equity).RefusalKey);
            Assert.Equal(cash, engine.FinanceManager.Balance);
            Assert.Equal((sites, projects, plots), (engine.State.SeaSites.Count, engine.State.ConstructionProjects.Count, engine.State.SitePlots.Count));
        }
        // A fresh company has not unlocked OF1-class (research, first harvest, a year without escapes).
        Refused(locked, SimulationEngine.OffshoreReasons.NotUnlocked, "off_1");

        var engine = OffshoreTestUnits.UnlockedForOf1(4);
        Refused(engine, SimulationEngine.OffshoreReasons.InvalidType, "off_1", OffshoreType.Submersible);
        Refused(engine, SimulationEngine.OffshoreReasons.OutOfRange, "off_1", equity: 0.5);
        Refused(engine, SimulationEngine.OffshoreReasons.PlotNotFound, "nowhere");
        Refused(engine, SimulationEngine.OffshoreReasons.NotAnOffshoreSlot, "pad_a");
        // off_2's 100-year sea (6.5 m) is above OF1-class's 5.5 m design.
        Refused(engine, SimulationEngine.OffshoreReasons.ExposureExceedsDesign, "off_2");
        // Havfarm-class is stage 3: it waits for the company's own OF1-class cycle.
        Refused(engine, SimulationEngine.OffshoreReasons.NotUnlocked, "off_2", OffshoreType.ShipShaped);
        Assert.True(engine.BuildOffshoreFarm("", OffshoreType.SemiSubmersible).Ok); // the legacy path takes off_1
        Refused(engine, SimulationEngine.OffshoreReasons.PlotTaken, "off_1");

        var poor = OffshoreTestUnits.UnlockedForOf1(5, cash: 100_000_000m);
        Refused(poor, SimulationEngine.OffshoreReasons.InsufficientFunds, "off_1");
    }

    /// <summary>
    /// The preview (spec §6.2): the contract and its stage payments per phase with
    /// a managed contract's overrun band, a turnkey contract's premium and no
    /// overrun, the consortium's 35 %, the development licences' value
    /// (8 × 780 t at the 2024 auction's 305 k/t ≈ 1.9 bn; conversion 8 × 13.8 M),
    /// no resource rent before conversion, the teething deck shown before
    /// construction (3-4 defects, 157 M), and the first stocking on the plan.
    /// </summary>
    [Fact]
    public void ThePreview_ShowsTheContractPaymentsLicencesConsortiumDeckAndFirstStocking()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(6);
        var managed = engine.PreviewOffshoreBuild("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed, 1.0);
        Assert.Equal("", managed.RefusalKey);
        Assert.Equal(720_000_000m, managed.ContractCapexNok);
        Assert.Equal(managed.ContractCapexNok, managed.CompanyCapexNok);
        Assert.Equal(OffshoreProfiles.PhaseKeys, managed.Payments.Select(p => p.Phase));
        var pay = Cal.Units.Payments;
        Assert.Equal(new[] { pay.ContractShare, pay.HullShare, pay.DeliveryShare, 0.0, pay.MooringShare, 0.0 },
            managed.Payments.Select(p => (double)(p.PaymentNok / managed.ContractCapexNok)));
        Assert.Equal(1.0, pay.ContractShare + pay.HullShare + pay.DeliveryShare + pay.MooringShare, 12);
        Assert.True(managed.Payments.Single(p => p.Phase == OffshoreProfiles.TowPhase).PlayerStarted);
        var common = CalibrationRegistry.Default.Facilities.Common;
        Assert.Equal(managed.ContractCapexNok * (decimal)common.Construction.OverrunMedian, managed.OverrunMedianNok);
        Assert.True(managed.OverrunP90Nok > managed.OverrunMedianNok);
        // Planned days: the yard's 18 months, the 11-week voyage, the tow, mooring and certificate = 670 d (spec §2A).
        Assert.Equal(670, managed.PlannedBuildDays);
        Assert.Equal(engine.State.Day + 670, managed.FirstStockingDay);
        int start = engine.State.Day;
        foreach (var p in managed.Payments)
        {
            Assert.Equal(start, p.PlannedStartDay);
            start += p.PlannedDays;
        }
        // The catalogue's days are the unit's own phases.
        var reference = common.Catalog.Of1;
        Assert.Equal(reference.YardDays, Cal.Units.Of1.YardContractDays + Cal.Units.Of1.HullDays);
        Assert.Equal(reference.VoyageDays, Cal.Units.Of1.VoyageDays);
        Assert.Equal(reference.MooringAndCertificationDays, Cal.Operability.TowHoldDays + Cal.Units.Of1.MooringDays + Cal.Units.Of1.CommissioningDays);
        Assert.Equal(reference.UnitCapexNok, Cal.Units.Of1.ContractCapexNok + Cal.Units.Of1.TeethingBudgetNok);
        // The licence value line.
        Assert.Equal(8, managed.Licences);
        Assert.Equal(8 * 780.0, managed.LicenceTonnesMtb);
        Assert.Equal(8 * 780m * 305_000m, managed.LicenceMarketValueNok);
        Assert.InRange((double)managed.LicenceMarketValueNok, 1.85e9, 1.95e9);
        Assert.Equal(8 * 13_804_348m, managed.ConversionFeeNok);
        Assert.False(managed.ResourceRentBeforeConversion);
        // The teething deck shown before construction (spec §6.2: "3-4 ukjente barnesykdommer; budsjett 157 M").
        Assert.Equal((3, 4, 157_000_000m), (managed.TeethingDefectsMin, managed.TeethingDefectsMax, managed.TeethingBudgetNok));
        Assert.Equal(new[] { 0, 60, 90, 120 }, managed.ShakedownOptionsDays);
        Assert.True(managed.Affordable);
        Assert.Equal(managed.Payments[0].PaymentNok, managed.FirstPaymentNok);

        var turnkey = engine.PreviewOffshoreBuild("off_1", OffshoreType.SemiSubmersible, ContractForm.Turnkey, 1.0);
        Assert.Equal(managed.CompanyCapexNok * (1m + (decimal)common.Construction.TurnkeyPremium), turnkey.CompanyCapexNok);
        Assert.Equal(0m, turnkey.OverrunMedianNok + turnkey.OverrunP90Nok);

        var consortium = engine.PreviewOffshoreBuild("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed, 0.35);
        Assert.Equal(0.35, consortium.ConsortiumEquityShare);
        Assert.Equal(managed.CompanyCapexNok * 0.35m, consortium.CompanyCapexNok);
        Assert.Equal(managed.ContractCapexNok * 0.35m, consortium.ConsortiumCompanyCapexNok);
        Assert.All(consortium.Payments.Zip(managed.Payments), p => Assert.Equal(p.Second.PaymentNok * 0.35m, p.First.PaymentNok));
        Assert.Equal(1.2, consortium.BuyoutMultiple);
        // A preview changes nothing.
        Assert.Empty(engine.OffshoreUnits);
        Assert.Empty(engine.State.ConstructionProjects);
    }

    /// <summary>
    /// The build: an offshore sea site of three zones on off_1, a construction
    /// project of six phases paying the contract share now, the open sea, the
    /// Driftsplan's offshore defaults; the yard, hull and voyage run by
    /// themselves, each paid at its start; then the project waits at the tow
    /// for the player, with no tug chartered, for as long as the player waits.
    /// </summary>
    [Fact]
    public void Build_ContractsTheUnit_RunsTheYardAndVoyage_ThenWaitsForTheTow()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(7);
        decimal cash = engine.FinanceManager.Balance;
        var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindOffshoreUnit(built.Id)!;
        var unit = site.Offshore!;
        var project = engine.OffshoreProjectOf(site)!;
        Assert.Equal(("off_1", 3, false), (site.PlotId, site.PhysicalPens.Count, unit.IsOperational));
        Assert.Equal(site.Id, engine.FindPlot("off_1")!.OwnerUnitId);
        Assert.Equal(OffshoreProfiles.PhaseKeys, project.Phases.Select(p => p.Key));
        Assert.Equal(720_000_000m * 0.2m, cash - engine.FinanceManager.Balance);
        Assert.Equal(ConstructionStatus.Building, project.Status);
        Assert.NotNull(engine.State.Metocean); // the open sea starts with the contract, so the tow has a forecast
        Assert.Equal(new[] { OffshorePolicies.CrewRota, OffshorePolicies.NetInspection, OffshorePolicies.Resupply,
                OffshorePolicies.SensorService, OffshorePolicies.Silage },
            engine.GetDriftsplan(site.Id).Select(e => e.Policy).OrderBy(p => p, StringComparer.Ordinal));
        var view = engine.GetOffshoreConstruction(site.Id)!;
        Assert.Equal((OffshoreProfiles.YardContractPhase, "building"), (view.CurrentPhase, view.Phases[0].Status));
        Assert.Equal(720_000_000m, unit.CAPEX);
        Assert.True(unit.HasTeethingDeck);
        Assert.Equal((700.0, 120.0, 12), (unit.SiloCapacityTonnes, unit.SilageCapacityTonnes, unit.EmployeeCount));

        var lines = new Dictionary<string, decimal>();
        engine.PnlBooked += (day, category, amount, income) => lines[category] = (lines.TryGetValue(category, out var v) ? v : 0m) + amount;
        OffshoreTestUnits.AdvanceToTow(engine, site);
        Assert.Equal(new[] { OffshoreProfiles.YardContractPhase, OffshoreProfiles.HullPhase, OffshoreProfiles.VoyagePhase },
            project.Phases.Take(project.CurrentPhase).Select(p => p.Key));
        // Contract, hull and delivery paid (0.2 + 0.5 + 0.2) plus the managed overruns.
        Assert.True(project.PaidNOK >= 720_000_000m * 0.9m, $"paid {project.PaidNOK:N0}");
        int waitingSince = engine.State.Day;
        engine.Advance(60);
        Assert.Equal(ConstructionStatus.Waiting, project.Status);
        Assert.Equal(OffshoreProfiles.TowPhase, project.Current!.Key);
        Assert.False(lines.ContainsKey("offshore_tow"), "tugs were chartered without a tow");
        Assert.False(unit.IsCommissioned);
        Assert.Equal("tow", engine.GetOffshoreConstruction(site.Id)!.WaitingReason);
        Assert.Equal(-1, unit.TowStartedDay);
        Assert.True(engine.State.Day - waitingSince >= 60);
    }

    /// <summary>
    /// Acceptance 4: the tow starts only on StartTow; a day above the tow limit
    /// during the tow sends the unit to shelter for 5-15 days with the tugs paid
    /// every day and no structural damage, the project waits for the player
    /// again (StartTow is refused while it shelters); a tow whose window holds
    /// goes on to mooring and commissioning: the silo filled and the first
    /// unit's deck of 3-4 defects drawn.
    /// </summary>
    [Fact]
    public void TheTow_StartsOnlyOnStartTow_AndAFailedWindowSheltersFiveToFifteenDaysWithoutDamage()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(8);
        var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindOffshoreUnit(built.Id)!;
        var unit = site.Offshore!;
        var project = engine.OffshoreProjectOf(site)!;
        OffshoreTestUnits.ShortenProject(engine, site);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotReady, engine.StartTow(site.Id).ErrorKey);
        OffshoreTestUnits.AdvanceToTow(engine, site, 30);

        var tow = engine.PreviewTow(site.Id)!;
        Assert.InRange(tow.HoldProbability, 0.0, 1.0);
        decimal tugDay = Cal.Costs.AnchorTugNokPerDay * Cal.Costs.TowTugs;
        Assert.Equal(tugDay, tow.TugsNokPerDay);
        Assert.Equal(tugDay * Cal.Operability.TowHoldDays, tow.CostIfHoldsNok);

        decimal towCharged = 0m;
        engine.PnlBooked += (day, category, amount, income) => { if (category == "offshore_tow") towCharged -= amount; };
        // Day 1 of the tow is calm, day 2 is 3.1 m: above the 2.5 m tow limit.
        int start = engine.State.Day;
        engine.SeaOverrideForTests = (s, day) => day == start + 2 ? 3.1 : 1.0;
        var started = engine.StartTow(site.Id);
        Assert.True(started.Ok, started.Message);
        Assert.Equal(ConstructionStatus.Building, project.Status);
        engine.Advance(2);
        Assert.Equal(ConstructionStatus.Waiting, project.Status);
        Assert.Equal(OffshoreProfiles.TowPhase, project.Current!.Key);
        int shelter = unit.TowShelterUntilDay - (start + 2) + 1;
        Assert.InRange(shelter, Cal.Costs.TowShelterDaysMin, Cal.Costs.TowShelterDaysMax);
        Assert.Equal(1.0, unit.StructuralIntegrity);
        Assert.Equal((1, 1), (unit.TowAttempts, unit.TowFailures));
        Assert.Equal(SimulationEngine.OffshoreReasons.Sheltering, engine.StartTow(site.Id).ErrorKey);
        while (engine.State.Day < unit.TowShelterUntilDay) engine.Advance(1);
        // Two tow days and every shelter day were paid; no more after the shelter.
        Assert.Equal(tugDay * (2 + shelter - 1), towCharged);
        engine.Advance(3);
        Assert.Equal(tugDay * (2 + shelter - 1), towCharged);
        Assert.Equal(1.0, unit.StructuralIntegrity);

        // A tow whose window holds: mooring, commissioning, the unit commissioned.
        Assert.True(engine.StartTow(site.Id).Ok);
        int guard = 0;
        while (!unit.IsCommissioned)
        {
            engine.Advance(1);
            Assert.True(++guard < 30, "the unit never commissioned after its tow");
        }
        Assert.Equal(ConstructionStatus.Complete, project.Status);
        Assert.True(unit.IsOperational);
        Assert.Equal(unit.SiloCapacityTonnes, unit.SiloTonnes);
        Assert.InRange(unit.TeethingDefects.Count, 3, 4); // the first unit of its class
        Assert.All(unit.TeethingDefects, d => Assert.False(d.IsRevealed));
        Assert.Contains(engine.State.FacilityAssets, a => a.UnitId == site.Id && a.CapexNok == project.PaidNOK);
    }

    /// <summary>
    /// Acceptance 6: a consortium unit (35 %) pays 35 % of every capex payment and
    /// books 35 % of the lines the unit books itself (crew, energy, maintenance,
    /// insurance, charter, tugs), and the partner settles its 65 % of the lines
    /// other systems book in full for the unit (feed, smolts) and takes 65 % of
    /// the harvest's net, so the unit's result in the company's books is 35 % of
    /// the same unit owned outright; the buy-out is the partner's share of the
    /// book value × 1.2.
    /// </summary>
    [Fact]
    public void AConsortiumUnit_Books35PercentOfEveryLine_AndIsBoughtOutAtBookTimesOnePointTwo()
    {
        (SimulationEngine Engine, SeaSite Site, Dictionary<string, decimal> Lines) Run(double equity)
        {
            var engine = OffshoreTestUnits.UnlockedForOf1(9);
            var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed, equity);
            Assert.True(built.Ok, built.Message);
            var site = engine.FindOffshoreUnit(built.Id)!;
            OffshoreTestUnits.ShortenProject(engine, site);
            OffshoreTestUnits.AdvanceToTow(engine, site, 30);
            int start = engine.State.Day;
            engine.SeaOverrideForTests = (s, day) => day <= start + 3 ? 1.0 : null; // a calm tow
            Assert.True(engine.StartTow(site.Id).Ok);
            while (!site.Offshore!.IsCommissioned) engine.Advance(1);
            engine.SeaOverrideForTests = null;
            var lines = new Dictionary<string, decimal>();
            engine.PnlBooked += (day, category, amount, income) => lines[category] = (lines.TryGetValue(category, out var v) ? v : 0m) + amount;
            Assert.True(engine.StockOffshoreFarm(site.Id, 200_000).Ok);
            engine.Advance(150);
            Assert.True(engine.HarvestOffshoreFarm(site.Id).Ok);
            engine.Advance(2); // the partner settles the harvest the next day
            return (engine, site, lines);
        }
        var full = Run(1.0);
        var share = Run(0.35);
        // The same game: the same fish, the same bookings.
        Assert.Equal(full.Site.Pens.Sum(p => p.TotalCount), share.Site.Pens.Sum(p => p.TotalCount));
        Assert.Equal(full.Engine.HarvestReports[^1].HogKg, share.Engine.HarvestReports[^1].HogKg);
        Assert.Equal(full.Site.Offshore!.Bookings.Count, share.Site.Offshore!.Bookings.Count);
        // Capex: every phase paid at 35 % (with the same overrun draws).
        Assert.Equal(engine_PaidOf(full) * 0.35m, engine_PaidOf(share), 2);
        // The unit's own lines at 35 %.
        foreach (var line in new[] { "offshore_crew", "offshore_energy", "offshore_maintenance", "offshore_insurance", "offshore_charter" })
            Assert.Equal((double)full.Lines[line] * 0.35, (double)share.Lines[line], 4);
        // The unit's result (every line but tax) is 35 % of the full unit's: feed and smolts booked
        // in full, settled with the partner; the harvest's net shared.
        decimal Result(Dictionary<string, decimal> l) => l.Where(kv => kv.Key != "tax").Sum(kv => kv.Value);
        Assert.Equal((double)Result(full.Lines) * 0.35, (double)Result(share.Lines), 2);
        Assert.True(share.Lines.ContainsKey("partner_settlement"));
        Assert.False(full.Lines.ContainsKey("partner_settlement"));

        // Buy-out at the partner's share of the book value × 1.2.
        var e = share.Engine;
        decimal own = e.State.FacilityAssets.Where(a => a.UnitId == share.Site.Id).Sum(a => a.BookValueNok);
        decimal price = e.BuyOutPriceNok(share.Site);
        Assert.Equal(own * (0.65m / 0.35m) * 1.2m, price, 2);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotConsortium, full.Engine.BuyOutPartner(full.Site.Id).ErrorKey);
        decimal cash = e.FinanceManager.Balance;
        var bought = e.BuyOutPartner(share.Site.Id);
        Assert.True(bought.Ok, bought.Message);
        Assert.Equal(price, cash - e.FinanceManager.Balance, 2);
        Assert.Equal(1.0, share.Site.Offshore!.EquityShare);
        Assert.Contains(e.State.FacilityAssets, a => a.UnitId == share.Site.Id && a.CapexNok == price);
    }

    private static decimal engine_PaidOf((SimulationEngine Engine, SeaSite Site, Dictionary<string, decimal> Lines) run)
        => run.Engine.OffshoreProjectOf(run.Site)!.PaidNOK;
}
