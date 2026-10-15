using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// Land-based and offshore facility verbs (RAS, offshore farm, post-smolt).
/// Previously only the Godot bridge could build these: it priced them from
/// its own table, never started construction (so a facility never became
/// operational, F383) and wrote into engine-only lists that were not saved
/// (F1069). The engine now owns the verb: CAPEX comes from the facility model,
/// construction starts on the build day, and the facility lands in GameState.
/// <para>
/// F2-03 (docs/FARM_TYPES_SPEC.md §3.1 decision 5): the three verbs build the
/// F2 unit kinds on the first free plot that fits, without permits, and stay
/// the scenario-start and test path (the player's verbs with permits,
/// construction phases and plots of their choosing are F2-07, F2-12 and
/// F2-14's): <see cref="BuildRASFacility"/> a grow-out land site on a pad with
/// the lots it needs, <see cref="BuildOffshoreFarm"/> an offshore sea site on a
/// slot its class is rated for, <see cref="BuildPostSmoltFacility"/> a
/// post-smolt land site on a pad with post-smolt room. No plot free: no_free_plot.
/// </para>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Refusal key of a build with no free plot that fits.</summary>
    public const string NoFreePlotKey = "no_free_plot";

    /// <summary>Build a grow-out land site (a RAS facility) of the given scale on the first free pad with the lots it needs.</summary>
    public CommandResult BuildRASFacility(string name, RASScale scale)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (!Enum.IsDefined(scale))
            return CommandResult.Fail("invalid_scale", $"Unknown RAS scale '{scale}'");

        var facility = new RASFacility
        {
            Id = NextFacilityId("ras", RASFacilities.Where(f => f.Kind == LandSiteKind.GrowOut).Select(f => f.Id)),
            Name = FacilityName(name, "RAS-anlegg", RASFacilities.Count(f => f.Kind == LandSiteKind.GrowOut)),
            Scale = scale,
            Kind = LandSiteKind.GrowOut,
            CapacityTonnesPerYear = RASFacility.DefaultCapacityTonnesPerYear(scale)
        };
        var plot = FirstFreePadFor(facility);
        if (plot == null)
            return CommandResult.Fail(NoFreePlotKey,
                $"No free land pad holds {GrowOutLotsFor(facility.CapacityTonnesPerYear)} grow-out lots");
        var paid = PayForFacility(facility.GetCAPEX(), facility.Name);
        if (!paid.Ok) return paid;

        PutOnPad(facility, plot);
        facility.StartConstruction(State.Day);
        RASFacilities.Add(facility);
        LogFacilityBuilt("RAS-anlegg under bygging",
            SimText.Nb($"{facility.Name} er under bygging på {plot.Name} ({facility.CapacityTonnesPerYear:N0} t per år)"),
            facility.CAPEX);
        return CommandResult.Success(
            $"Construction of RAS facility '{facility.Name}' started on {plot.Id} ({facility.ConstructionDays} days)",
            value: (double)facility.CAPEX, id: facility.Id);
    }

    /// <summary>
    /// Build an offshore unit of the given structure class: an offshore sea
    /// site with the class's zones, design Hs and MTB on the first free slot
    /// the class is rated for (spec §4: an OF1-class semi-submersible fits
    /// off_1, a Havfarm-class ship off_1 and off_2).
    /// </summary>
    public CommandResult BuildOffshoreFarm(string name, OffshoreType type)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (!Enum.IsDefined(type))
            return CommandResult.Fail("invalid_type", $"Unknown offshore type '{type}'");

        double designHs = OffshoreUnit.DefaultDesignHsM(type);
        var plot = FirstFreePlot(p => p.AcceptsDesignHs(designHs));
        if (plot == null)
            return CommandResult.Fail(NoFreePlotKey,
                $"No free offshore slot has a 100-year wave height within the {type} class's {designHs:F1} m");

        string id = NextFacilityId("offshore",
            State.SeaSites.Where(s => s.Kind == SiteKind.Offshore).Select(s => s.Id)
                .Concat(OffshoreFarms.Select(f => f.Id)));
        if (State.SeaSites.Any(s => s.Id == id))
            return CommandResult.Fail("duplicate_id", $"A sea site is already called '{id}'");
        string displayName = FacilityName(name, type == OffshoreType.ShipShaped ? "Skipsmerd" : "Havmerd",
            OffshoreUnits.Count);
        decimal capex = OffshoreUnit.LegacyCapexNok(type);
        var paid = PayForFacility(capex, displayName);
        if (!paid.Ok) return paid;

        var site = NewOffshoreSite(id, displayName, type, plot);
        site.Offshore!.StartConstruction(State.Day, capex);
        State.SeaSites.Add(site);
        ClaimPlot(plot, site.Id);
        LogFacilityBuilt("Havmerd under bygging",
            SimText.Nb($"{site.Label} er under bygging på {plot.Name} ({site.MABTonnes:N0} t MTB, {site.PhysicalPens.Count} soner)"),
            capex);
        return CommandResult.Success(
            $"Construction of offshore unit '{site.Label}' started on {plot.Id} ({site.Offshore.ConstructionDays} days)",
            value: (double)capex, id: site.Id);
    }

    /// <summary>
    /// A new offshore sea site of <paramref name="type"/> on <paramref name="plot"/>:
    /// the class's zones holding its volume, its design Hs and MTB, crewed on
    /// board. Not yet built (no construction started) and not yet in the game.
    /// </summary>
    internal SeaSite NewOffshoreSite(string id, string displayName, OffshoreType type, SitePlot plot)
    {
        double volume = OffshoreUnit.DefaultVolumeM3(type);
        return new SeaSite
        {
            Id = id,
            ZoneId = plot.ZoneId,
            DisplayName = displayName,
            Kind = SiteKind.Offshore,
            PlotId = plot.Id,
            Offshore = new OffshoreUnit { FarmType = type, DesignHsM = OffshoreUnit.DefaultDesignHsM(type) },
            MABTonnes = OffshoreUnit.DefaultMtbTonnes(type),
            // The legacy one-figure density reads the unit as one body of
            // water (its zones are the pens); its crew and upkeep are its own.
            NumberOfPens = 1,
            PenVolume = (float)volume,
            WatchMode = OffshoreWatchMode,
            PhysicalPens = OffshoreUnit.BuildZones(id, type, volume),
        };
    }

    /// <summary>
    /// Build a post-smolt land site of up to 4 modules on the first free pad
    /// with post-smolt room (pad_delta first), with the pre-F2 post-smolt
    /// facility's capital cost and construction time.
    /// </summary>
    public CommandResult BuildPostSmoltFacility(string name)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var legacy = new PostSmoltFacility();
        int modules = Math.Clamp((int)Math.Ceiling(legacy.AnnualSmoltCapacity / (double)PostSmoltFishPerModule),
            1, MaxPostSmoltModules);
        var facility = NewPostSmoltSite(
            NextFacilityId("postsmolt", PostSmoltSites.Select(f => f.Id).Concat(PostSmoltFacilities.Select(f => f.Id))),
            FacilityName(name, "Postsmoltanlegg", PostSmoltSites.Count), modules);
        var plot = FirstFreePadFor(facility);
        if (plot == null)
            return CommandResult.Fail(NoFreePlotKey, $"No free land pad holds {modules} post-smolt modules");
        decimal capex = legacy.GetCAPEX();
        var paid = PayForFacility(capex, facility.Name);
        if (!paid.Ok) return paid;

        PutOnPad(facility, plot);
        facility.ConstructionStartDay = State.Day;
        facility.ConstructionProgress = 0.0;
        facility.CAPEX = capex;
        RASFacilities.Add(facility);
        LogFacilityBuilt("Postsmoltanlegg under bygging",
            SimText.Nb($"{facility.Name} er under bygging på {plot.Name} ({modules} moduler, {modules * PostSmoltFishPerModule:N0} smolt)"),
            capex);
        return CommandResult.Success(
            $"Construction of post-smolt site '{facility.Name}' started on {plot.Id} ({facility.ConstructionDays} days)",
            value: (double)capex, id: facility.Id);
    }

    /// <summary>Put a new land site on <paramref name="plot"/>: its plot, its zone (the pad's water area) and the claim.</summary>
    private void PutOnPad(RASFacility facility, SitePlot plot)
    {
        var held = ClaimPlot(plot, facility.Id);
        facility.PlotId = held.Id;
        facility.ZoneId = held.ZoneId;
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
