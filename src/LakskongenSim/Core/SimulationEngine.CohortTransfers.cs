using System;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Cohort transfers between fjord sites (Phase 5.3), engine-owned (roadmap
/// F2-13, docs/FARM_TYPES_SPEC.md §3.4: "the bridge-owned verbs become thin
/// calls into the engine"): the bridge plans and executes through these verbs,
/// with no money or fish moved and no random source in the Godot layer. The
/// land-to-sea transfer with its wellboat lead time is
/// <see cref="TransferTankToSea"/>.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// The plan of moving <paramref name="fishCount"/> fish of
    /// <paramref name="meanWeightKg"/> from one fjord site to another for
    /// <paramref name="reason"/> (a pure read): its cost, the handling losses
    /// and the wellboat's lead time. Null for an unknown reason id.
    /// </summary>
    public CohortTransferSystem.TransferPlan? PlanCohortTransfer(string sourceSiteId, string destSiteId, int fishCount,
        double meanWeightKg, string reason)
    {
        if (State == null || !Enum.TryParse<CohortTransferSystem.TransferReason>(reason, true, out var why)) return null;
        return CohortTransferSystem.PlanTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, why, State,
            CrewEffects.HasRole(State, "veterinaer"));
    }

    /// <summary>
    /// Move the fish of a planned cohort transfer now (the wellboat has come):
    /// the handling losses at the plan's rate are booked as deaths, the rest
    /// move through the mortality ledger, the cost is booked as opex
    /// (<c>transfers</c>). Fails: engine_not_ready, invalid_reason, and the
    /// transfer's own refusals (site not found, an offshore unit, MAB, too few
    /// fish) as transfer_failed. Value = fish moved, Value2 = fish lost.
    /// </summary>
    public CommandResult ExecuteCohortTransfer(string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reason)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var plan = PlanCohortTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reason);
        if (plan == null)
            return CommandResult.Fail("invalid_reason", $"Unknown transfer reason '{reason}'");
        var result = CohortTransferSystem.ExecuteTransfer(plan, State,
            cost => PayCost(cost, CostBooking.Opex, "transfers"), Growth, Mortality);
        return result.Success
            ? CommandResult.Success(result.Message, result.FishTransferred, result.FishLost, destSiteId)
            : CommandResult.Fail("transfer_failed", result.Message, (double)result.ActualCostNok);
    }
}
