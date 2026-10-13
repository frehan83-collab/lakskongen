using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Events;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Land-based and offshore facility verbs (RAS, offshore farm, post-smolt).
/// Previously only the Godot bridge could build these: it priced them from
/// its own table, never started construction (so a facility never became
/// operational, F383) and wrote into engine-only lists that were not saved
/// (F1069). The engine now owns the verb: CAPEX comes from the facility model,
/// construction starts on the build day, and the facility lands in GameState.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Build a RAS (recirculating aquaculture) facility of the given scale.</summary>
    public CommandResult BuildRASFacility(string name, RASScale scale)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (!Enum.IsDefined(scale))
            return CommandResult.Fail("invalid_scale", $"Unknown RAS scale '{scale}'");

        var facility = new RASFacility
        {
            Id = NextFacilityId("ras", RASFacilities.Select(f => f.Id)),
            Name = FacilityName(name, "RAS-anlegg", RASFacilities.Count),
            Scale = scale,
            CapacityTonnesPerYear = RASFacility.DefaultCapacityTonnesPerYear(scale)
        };
        var paid = PayForFacility(facility.GetCAPEX(), facility.Name);
        if (!paid.Ok) return paid;

        facility.StartConstruction(State.Day);
        RASFacilities.Add(facility);
        LogFacilityBuilt("RAS Facility Built",
            $"Built RAS facility '{facility.Name}' ({scale} scale, {facility.CapacityTonnesPerYear:N0}t/yr capacity)",
            facility.CAPEX);
        return CommandResult.Success(
            $"Construction of RAS facility '{facility.Name}' started ({facility.ConstructionDays} days)",
            value: (double)facility.CAPEX, id: facility.Id);
    }

    /// <summary>Build an offshore farm of the given structure type.</summary>
    public CommandResult BuildOffshoreFarm(string name, OffshoreType type)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (!Enum.IsDefined(type))
            return CommandResult.Fail("invalid_type", $"Unknown offshore type '{type}'");

        var farm = new OffshoreFarm
        {
            Id = NextFacilityId("offshore", OffshoreFarms.Select(f => f.Id)),
            Name = FacilityName(name, "Offshore-anlegg", OffshoreFarms.Count),
            FarmType = type
        };
        var paid = PayForFacility(farm.GetCAPEX(), farm.Name);
        if (!paid.Ok) return paid;

        farm.StartConstruction(State.Day);
        OffshoreFarms.Add(farm);
        LogFacilityBuilt("Offshore Farm Built",
            $"Built offshore farm '{farm.Name}' ({type}, {farm.CapacityTonnes:N0}t capacity)",
            farm.CAPEX);
        return CommandResult.Success(
            $"Construction of offshore farm '{farm.Name}' started ({farm.ConstructionDays} days)",
            value: (double)farm.CAPEX, id: farm.Id);
    }

    /// <summary>Build a post-smolt facility with the default annual smolt capacity.</summary>
    public CommandResult BuildPostSmoltFacility(string name)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var facility = new PostSmoltFacility
        {
            Id = NextFacilityId("postsmolt", PostSmoltFacilities.Select(f => f.Id)),
            Name = FacilityName(name, "Post-smolt-anlegg", PostSmoltFacilities.Count)
        };
        var paid = PayForFacility(facility.GetCAPEX(), facility.Name);
        if (!paid.Ok) return paid;

        facility.StartConstruction(State.Day);
        PostSmoltFacilities.Add(facility);
        LogFacilityBuilt("Post-Smolt Facility Built",
            $"Built post-smolt facility '{facility.Name}' ({facility.AnnualSmoltCapacity:N0} smolt/yr)",
            facility.CAPEX);
        return CommandResult.Success(
            $"Construction of post-smolt facility '{facility.Name}' started ({facility.ConstructionDays} days)",
            value: (double)facility.CAPEX, id: facility.Id);
    }

    /// <summary>
    /// CAPEX leaves cash through the ledger and is booked as a balance-sheet
    /// asset (not a P&amp;L cost). It used to Spend directly, so the daily
    /// reconciliation booked every facility build as unclassified cash.
    /// </summary>
    private CommandResult PayForFacility(decimal capex, string name)
    {
        if (!TryPayCost(capex, CostBooking.Capex, "facilities"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {capex:N0} NOK to build '{name}'", (double)capex);
        return CommandResult.Success();
    }

    private void LogFacilityBuilt(string title, string description, decimal capex)
    {
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = title,
            Category = EventCategory.Equipment,
            Description = description,
            BalanceChange = -capex
        });
        State.TrimEventLog();
    }

    /// <summary>First free "&lt;prefix&gt;_N" id (stable, never reuses a live id).</summary>
    private static string NextFacilityId(string prefix, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.Ordinal);
        int n = taken.Count + 1;
        while (taken.Contains($"{prefix}_{n}")) n++;
        return $"{prefix}_{n}";
    }

    private static string FacilityName(string requested, string fallback, int existingCount)
        => string.IsNullOrWhiteSpace(requested) ? $"{fallback} {existingCount + 1}" : requested.Trim();
}
