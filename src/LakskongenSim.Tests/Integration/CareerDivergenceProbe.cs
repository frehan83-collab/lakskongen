using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S10 — Career-divergence probe.
/// Pre-registered failure criterion (2026-10-12, unchanged since first draft):
///   A (red-light) vs B (compliant) scripted careers on the SAME SEED must
///   diverge by at least ONE of:
///     * ≥ 200,000 kg difference in forced-harvest biomass, OR
///     * ≥ 5,000,000 NOK difference in penalty revenue, OR
///     * ≥ 50,000,000 NOK difference in ending balance.
///   AND A must actually exercise the red-light mechanic (≥1 forced harvest,
///   penalty revenue > 0). A vacuous comparison of zeros is NOT a pass — it is
///   an inconclusive probe and fails loudly.
///
/// Mechanic under probe: red-zone entry issues a Mattilsynet over-MAB order
/// (14-day deadline); missing the deadline forces a penalty harvest at 70 % of
/// market price. The compliant career keeps lice suppressed, stays green and
/// never eats a penalty.
/// </summary>
public class CareerDivergenceProbe
{
    private const int ProbeSeed = 20261012;
    private const int CareerDays = 30;
    private const double RedAdultFemaleLiceLoad = 2.0; // ≥ TrafficLightSystem.RedLiceThreshold (1.0)
    private const double CompliantLiceLoad = 0.0;  // zone stays green
    private const int FishPerCohort = 60_000;      // 60k × ~5 kg ≈ 300 t biomass
    private const double ReleaseWeightKg = 5.0;
    private const double SiteMabTonnes = 0.5;      // tiny MAB → site is massively over it
    private const double MinHarvestedKgDiff = 200_000;
    private const decimal MinPenaltyRevenueDiff = 5_000_000m;
    private const decimal MinEndingBalanceDiff = 50_000_000m;

    private readonly ITestOutputHelper _out;
    public CareerDivergenceProbe(ITestOutputHelper output) => _out = output;

    private static (SimulationEngine Engine, SeaSite Site, FishPopulation Pop) BuildEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(100_000_000m);
        engine.Initialize(enableEvents: false, seed: seed);

        engine.State.Zones.Add(new Zone("zone-probe"));
        var site = new SeaSite("site-probe", "zone-probe") { MABTonnes = SiteMabTonnes };
        engine.State.SeaSites.Add(site);

        var pop = new FishPopulation(new GrowthModel(), new MortalityModel())
        {
            ZoneId = "zone-probe",
            SiteId = "site-probe",
        };
        pop.AddCohort(new FishPopulation.Cohort(
            ageDays: 400,
            count: FishPerCohort,
            meanWeightKg: ReleaseWeightKg,
            stage: LifeStage.Harvest));
        site.Stock(pop);
        engine.State.Populations.Add(pop);
        return (engine, site, pop);
    }

    [Fact]
    public void CareerDivergence_RedVsSuppressed_DivergesBeyondThreshold()
    {
        var (engineA, siteA, popA) = BuildEngine(ProbeSeed);
        var (engineB, siteB, popB) = BuildEngine(ProbeSeed);

        for (int day = 0; day < CareerDays; day++)
        {
            // A — red-light career: the operator runs the site at red lice level
            // and never delouses. The site-level lice model is the true state
            // (the weekly census mirrors it onto pens), so re-seed the model
            // every day — otherwise its own dynamics quietly "fix" the run.
            siteA.Lice.AdultFemalePerFish = RedAdultFemaleLiceLoad;
            siteA.Lice.PreAdultPerFish = 0.0;
            siteA.Lice.ChalimusPerFish = 0.0;
            siteA.Lice.AdultMalePerFish = 0.0;
            popA.SeaLiceLoad = siteA.Lice.AverageLicePerFish;
            engineA.UpdateTrafficLightZones();
            engineA.Advance(1);
            _out.WriteLine($"[S10][A] day={engineA.State.Day} zone={engineA.TrafficLightSystem.GetZoneColor("zone-probe")} " +
                           $"order={siteA.HasActiveMattilsynetOrder} deadline={siteA.MattilsynetDeadlineDay} lice={popA.SeaLiceLoad:F2}");

            // B — compliant career: lice kept suppressed; zone never goes red.
            siteB.Lice.AdultFemalePerFish = 0.0;
            siteB.Lice.PreAdultPerFish = 0.0;
            siteB.Lice.ChalimusPerFish = 0.0;
            siteB.Lice.AdultMalePerFish = 0.0;
            popB.SeaLiceLoad = CompliantLiceLoad;
            engineB.UpdateTrafficLightZones();
            engineB.Advance(1);
        }

        var a = ExtractMetrics(engineA);
        var b = ExtractMetrics(engineB);

        _out.WriteLine($"[S10] A red-light: forcedHarvests={a.ForcedHarvestEvents}, harvested={a.HarvestedKg:F0} kg, " +
                       $"penalty={a.PenaltyRevenue.ToString("N0", CultureInfo.InvariantCulture)} NOK, balance={a.EndingBalance.ToString("N0", CultureInfo.InvariantCulture)} NOK");
        _out.WriteLine($"[S10] B compliant: forcedHarvests={b.ForcedHarvestEvents}, harvested={b.HarvestedKg:F0} kg, " +
                       $"penalty={b.PenaltyRevenue.ToString("N0", CultureInfo.InvariantCulture)} NOK, balance={b.EndingBalance.ToString("N0", CultureInfo.InvariantCulture)} NOK");

        // Guard: the probe is only meaningful if A actually triggered the mechanic.
        Assert.True(a.ForcedHarvestEvents > 0 && a.PenaltyRevenue > 0m,
            $"Inconclusive probe: red-light career did not trigger a forced harvest (orders on A: {siteA.HasActiveMattilsynetOrder}; " +
            $"events: {a.ForcedHarvestEvents}). Fix the rig, not the criterion.");

        double harvestedDiff = a.HarvestedKg - b.HarvestedKg;
        decimal penaltyDiff = a.PenaltyRevenue - b.PenaltyRevenue;
        decimal balanceDiff = a.EndingBalance - b.EndingBalance;

        _out.WriteLine($"[S10] divergence: harvested={harvestedDiff:F0} kg (need ≥ {MinHarvestedKgDiff:F0}), " +
                       $"penalty={penaltyDiff.ToString("N0", CultureInfo.InvariantCulture)} (need ≥ {MinPenaltyRevenueDiff.ToString("N0", CultureInfo.InvariantCulture)}), " +
                       $"balance={balanceDiff.ToString("N0", CultureInfo.InvariantCulture)} (need ≥ {MinEndingBalanceDiff.ToString("N0", CultureInfo.InvariantCulture)})");

        bool crossed = harvestedDiff >= MinHarvestedKgDiff
                       || penaltyDiff >= MinPenaltyRevenueDiff
                       || balanceDiff >= MinEndingBalanceDiff;

        Assert.True(crossed,
            $"Pre-registered criterion not met. harvested={harvestedDiff:F0} kg, " +
            $"penalty={penaltyDiff.ToString("N0", CultureInfo.InvariantCulture)}, " +
            $"balance={balanceDiff.ToString("N0", CultureInfo.InvariantCulture)}");
    }

    private sealed record CareerMetrics(
        double HarvestedKg,
        decimal PenaltyRevenue,
        decimal EndingBalance,
        int ForcedHarvestEvents);

    private static readonly Regex ForcedHarvestTonnes = new(
        @"([0-9]+[.,][0-9]+|[0-9]+)t\s+tvangsavslattet", RegexOptions.Compiled);

    private static CareerMetrics ExtractMetrics(SimulationEngine engine)
    {
        decimal penaltyRevenue = 0m;
        if (engine.FinancialStatements?.RevenueBreakdown.TryGetValue("forced_harvest", out var rev) == true)
            penaltyRevenue = rev;

        double harvestedKg = 0;
        int forcedHarvestEvents = 0;
        foreach (var evt in engine.State.EventLog)
        {
            if (evt.Name != "Tvangsavslakt (Mattilsynet)") continue;
            forcedHarvestEvents++;
            var m = ForcedHarvestTonnes.Match(evt.Description ?? "");
            if (m.Success)
            {
                var raw = m.Groups[1].Value.Replace(',', '.');
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var tonnes))
                    harvestedKg += tonnes * 1000.0;
            }
        }

        return new CareerMetrics(harvestedKg, penaltyRevenue, engine.FinanceManager.Balance, forcedHarvestEvents);
    }
}
