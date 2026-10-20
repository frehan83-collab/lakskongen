using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Tutorial;

/// <summary>
/// The minimal first-season player (roadmap U2-02): it does what the current
/// tutorial step asks, through the same verbs and UI marks the screens send,
/// and nothing else. Open a tab is the mark the tab sends; set a feeding plan
/// is <see cref="SimulationEngine.SetFeedPlan"/> (full appetite, standard
/// feed, 30 days: the card's smallest change); the lice step is the
/// "Avlus ved telling" standing order with its defaults (the old minimal
/// player's 0.4 rule); the harvest is a planned order once the mean weight
/// reaches 4.5 kg (the old minimal player's weight); the restock and the new
/// site get the stocking card's 10 000 smolts in their first empty pen; the
/// research is the cheapest project once it leaves 200 000 NOK; the funding
/// step takes the finance panel's 5 MNOK loan over 36 months when the cash
/// does not cover the build yet. After each verb it checks the steps as the
/// bridge does, so everything a step asks on one day happens on that day.
/// </summary>
internal sealed class TutorialPlayer
{
    /// <summary>The finance panel's loan (FinancePanel.LOAN_AMOUNT, LOAN_TERM_MONTHS).</summary>
    public const double LoanNok = 5_000_000;
    public const int LoanTermMonths = 36;
    /// <summary>The stocking card's default count (PenManagement.STOCK_BATCH).</summary>
    public const int StockBatch = 10_000;
    public const double OrderAtKg = 4.5;
    public const decimal ResearchReserveNok = 200_000m;

    public int LoanDay { get; private set; } = -1;
    public int ResearchDay { get; private set; } = -1;
    public int BuildDay { get; private set; } = -1;
    public decimal MinCash { get; private set; } = decimal.MaxValue;
    /// <summary>Day each step completed (step number → day).</summary>
    public Dictionary<int, int> StepDays { get; } = new();

    public static SimulationEngine NewEngine(int seed, decimal cash, bool events)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(cash);
        engine.Initialize(enableEvents: events, seed: seed);
        return engine;
    }

    /// <summary>A started scenario, as the scenario picker starts it.</summary>
    public static SimulationEngine Start(string scenarioId, int seed, bool events = true)
    {
        var probe = NewEngine(seed, 10_000_000m, events: false);
        var def = probe.Scenario.GetScenario(scenarioId)!;
        var engine = NewEngine(seed, def.StartingCapitalNOK, events);
        Assert.True(engine.StartScenario(scenarioId).Ok, $"{scenarioId} did not start");
        return engine;
    }

    /// <summary>Play until the tutorial is complete or <paramref name="horizonDays"/> pass; returns the engine.</summary>
    public SimulationEngine Play(SimulationEngine engine, int horizonDays = 1500)
    {
        engine.Tutorial.OnStepCompleted += step => StepDays[step.StepNumber] = engine.State.Day;
        for (int day = 0; day < horizonDays && !engine.Tutorial.IsComplete && !engine.Scenario.IsLost; day++)
        {
            DoToday(engine);
            if (engine.Tutorial.IsComplete) break;
            engine.Advance(1);
            MinCash = Math.Min(MinCash, engine.FinanceManager.Balance);
        }
        return engine;
    }

    /// <summary>Do what the current step asks, step after step, as long as each one completes today.</summary>
    public void DoToday(SimulationEngine engine)
    {
        for (int guard = 0; guard < 32 && engine.Tutorial.IsActive; guard++)
        {
            int before = engine.Tutorial.CurrentStepNumber;
            Act(engine, engine.Tutorial.CurrentStep!.Id);
            engine.Tutorial.CheckProgress();
            if (engine.Tutorial.IsComplete || engine.Tutorial.CurrentStepNumber == before) return;
        }
    }

    /// <summary>Do what the current step asks, once (a step may then complete).</summary>
    public void DoOne(SimulationEngine engine)
    {
        if (!engine.Tutorial.IsActive) return;
        Act(engine, engine.Tutorial.CurrentStep!.Id);
        engine.Tutorial.CheckProgress();
    }

    private void Act(SimulationEngine engine, string stepId)
    {
        var sites = engine.FjordSites().ToList();
        var home = sites.First();
        switch (stepId)
        {
            case "open_pens": engine.Tutorial.Mark(TutorialMarks.Tab("pens")); break;
            case "feed_plan": Ok(engine.SetFeedPlan(home.Id, 100.0, FeedRecipeKind.Standard, 30)); break;
            case "lice_order": Ok(engine.SetDriftsplan(home.Id, FjordPolicies.DelouseOnCount)); break;
            case "inbox": engine.Tutorial.Mark(TutorialMarks.Inbox); break;
            case "start_clock": engine.Tutorial.Mark(TutorialMarks.Clock); break;
            case "order_harvest":
                if (MeanWeightKg(home) >= OrderAtKg)
                    Ok(engine.OrderHarvest(home.Id));
                break;
            case "harvest_report": engine.Tutorial.Mark(TutorialMarks.HarvestReport); break;
            case "restock":
                if (FirstEmptyPen(home) is { } emptyPen)
                    Ok(engine.StockSmoltsInPen(home.Id, emptyPen.Id, StockBatch));
                break;
            case "research":
            {
                var node = engine.Research.Nodes.Values
                    .Where(n => engine.Research.CanResearch(n.Id)).OrderBy(n => n.CostNOK).First();
                if (engine.FinanceManager.Balance > node.CostNOK + ResearchReserveNok
                    && engine.StartResearchProject(node.Id).Ok)
                    ResearchDay = engine.State.Day;
                break;
            }
            case "regulation": engine.Tutorial.Mark(TutorialMarks.Tab("sustainability")); break;
            case "funding":
                if (engine.FinanceManager.Balance < engine.Tutorial.FundingTargetNok()
                    && engine.TakeLoan(LoanNok, LoanTermMonths, 0.0).Ok)
                    LoanDay = engine.State.Day;
                break;
            case "open_map": engine.Tutorial.Mark(TutorialMarks.Tab("map")); break;
            case "home_area": engine.Tutorial.Mark(TutorialMarks.MapAreaHome); break;
            case "build_site":
                if (engine.BuildSeaSite(home.ZoneId, "andre_anlegg").Ok)
                    BuildDay = engine.State.Day;
                break;
            case "stock_new_site":
            {
                var site = sites.Skip(1).First();
                if (FirstEmptyPen(site) is { } newPen)
                    Ok(engine.StockSmoltsInPen(site.Id, newPen.Id, StockBatch));
                break;
            }
            case "new_site_lice":
                foreach (var site in sites.Where(s => !engine.Tutorial.HasLiceOrder(s.Id)))
                    Ok(engine.SetDriftsplan(site.Id, FjordPolicies.DelouseOnCount));
                break;
            // grow, sale: the world's steps; the clock runs.
        }
    }

    private static void Ok(CommandResult result) => Assert.True(result.Ok, result.Message);

    public static double MeanWeightKg(SeaSite site)
    {
        int fish = site.Pens.Sum(p => p.TotalCount);
        return fish == 0 ? 0.0 : site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish;
    }

    private static Pen? FirstEmptyPen(SeaSite site) => site.PhysicalPens.FirstOrDefault(p => p.IsEmpty);
}
