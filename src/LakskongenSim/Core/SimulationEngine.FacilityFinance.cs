using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Facility finance (roadmap F2-04, docs/FARM_TYPES_SPEC.md §3.6): the F2
/// units' asset register and its depreciation, and the resource-rent
/// ring-fence.
/// <para>
/// <b>Depreciation.</b> A land site or an offshore unit built in this game
/// enters <see cref="GameState.FacilityAssets"/> with what the company paid
/// for it, from its commissioning day; every simulated day books the day's
/// straight-line depreciation (land 20 years, offshore steel 25, calibration
/// <c>facilities.common.depreciation</c>) as the non-cash P&amp;L line
/// <see cref="FinancialStatements.Depreciation"/>: it lowers the corporate tax
/// base and the assets' book value and moves no cash, so the ledger's cash
/// reconciliation is untouched. Sea sites, processing plants and units whose
/// capex was paid before F2 are left out (spec §8).
/// </para>
/// <para>
/// <b>Ring-fence.</b> The resource-rent base is the harvest of rent-liable
/// fish at the norm price less the year's sea-phase costs. Costs of units the
/// rent does not reach (every land site; an offshore unit on licences outside
/// the rent, <see cref="IsResourceRentLiable"/>) are booked in the P&amp;L like
/// any cost and attributed here (<see cref="AttributeUnitCost"/>), and the rent
/// base leaves them out, so land opex and depreciation never lower the tax on
/// the sea's rent (spec §3.6, V:3, V:4, V:16).
/// </para>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Facility depreciation booked this fiscal year (NOK).</summary>
    public decimal FacilityDepreciationThisYear => FinancialStatements?.Depreciation ?? 0m;

    /// <summary>
    /// Put an F2 unit's capitalised cost on the asset register: depreciated
    /// from <paramref name="startDay"/> (its commissioning day) over the life of
    /// its kind. Any depreciation already due (a start day in the past) is
    /// booked at once. Returns the new asset, or null for a non-positive cost.
    /// </summary>
    public FacilityAsset? RegisterFacilityAsset(string unitId, FacilityAssetKind kind, decimal capexNok, int startDay)
    {
        if (State == null || capexNok <= 0m || string.IsNullOrEmpty(unitId)) return null;
        var lives = _calibration.Facilities.Common.Depreciation;
        var asset = new FacilityAsset
        {
            UnitId = unitId,
            Kind = kind,
            CapexNok = capexNok,
            StartDay = startDay,
            LifeYears = kind == FacilityAssetKind.OffshoreUnit ? lives.OffshoreLifeYears : lives.LandLifeYears,
        };
        State.FacilityAssets.Add(asset);
        if (startDay <= State.Day) BookFacilityDepreciation();
        return asset;
    }

    /// <summary>
    /// Book the assets' depreciation through today that is not booked yet (the
    /// post-tick phase <see cref="ConstructionAndPermitsTickSystem"/> calls it
    /// every day, after the day turns, so the in-day finance of a day sees that
    /// day's depreciation and a year's last day belongs to its year).
    /// </summary>
    internal void BookFacilityDepreciation()
    {
        if (State == null || State.FacilityAssets.Count == 0) return;
        decimal due = FacilityAssetRegister.BookThrough(State.FacilityAssets, State.Day);
        if (due > 0m) FinancialStatements?.RecordDepreciation(due);
    }

    // ─── The ring-fence ─────────────────────────────────────────────────────

    /// <summary>
    /// Attribute a cost already booked in the P&amp;L (COGS or opex) to the unit
    /// it was spent on. A unit the resource rent does not reach puts it outside
    /// the rent base (<see cref="GameState.RentRingFencedCostsThisTaxYear"/>);
    /// a rent-liable unit's cost stays a sea-phase deduction.
    /// </summary>
    internal void AttributeUnitCost(string unitId, HoldingKind kind, decimal amount)
    {
        if (State == null || amount <= 0m) return;
        if (!IsResourceRentLiable(unitId ?? "", kind))
            State.RentRingFencedCostsThisTaxYear += amount;
        NotePartnerShareOfUnitCost(unitId ?? "", kind, amount); // F2-07: a consortium partner's share
    }

    /// <summary>
    /// Attribute a cost spent on <paramref name="population"/> (its feed) to the
    /// unit that holds it: a land tank's site, an offshore pen's unit; a fjord
    /// pen's cost is the sea's and stays in the rent base.
    /// </summary>
    internal void AttributePopulationCost(FishPopulation population, decimal amount)
    {
        if (State == null || amount <= 0m || population == null) return;
        var holding = HoldingOf(population);
        switch (holding.Kind)
        {
            case HoldingKind.LandTank:
                AttributeUnitCost(holding.LandFacility?.Id ?? population.SiteId ?? "", HoldingKind.LandTank, amount);
                break;
            case HoldingKind.Offshore:
                AttributeUnitCost(holding.Site?.Id ?? population.SiteId ?? "", HoldingKind.Offshore, amount);
                break;
        }
    }

    /// <summary>
    /// This fiscal year's resource-rent base so far (NOK, may be negative): the
    /// rent-liable harvest at the norm price less the sea-phase costs, which
    /// are the year's COGS and opex without the harvest cost (S2-04: not a
    /// sea-phase cost) and without the ring-fenced costs of units the rent
    /// does not reach. Facility depreciation is not a COGS or opex line, so it
    /// never enters it.
    /// </summary>
    public decimal ResourceRentBaseThisTaxYear()
    {
        var fs = FinancialStatements;
        if (State == null || fs == null) return 0m;
        decimal seaPhaseCosts = fs.COGS + fs.OperatingExpenses - State.HarvestCostThisTaxYear - State.RentRingFencedCostsThisTaxYear;
        return State.ResourceRentHarvestValueThisTaxYear - seaPhaseCosts;
    }

    // ─── Save and load ─────────────────────────────────────────────────────

    /// <summary>
    /// Adopt the facility-finance state of a loaded save (called by
    /// <see cref="AdoptSavedState"/>): the asset register, construction
    /// projects, permit cases, their id counters and the year's ring-fenced
    /// costs. A save from before F2-04 has none of them.
    /// </summary>
    internal void AdoptFacilityFinanceState(GameState snapshot)
    {
        State.FacilityAssets.Clear();
        if (snapshot.FacilityAssets != null)
            State.FacilityAssets.AddRange(snapshot.FacilityAssets.Where(a => a != null));
        State.ConstructionProjects.Clear();
        if (snapshot.ConstructionProjects != null)
            State.ConstructionProjects.AddRange(snapshot.ConstructionProjects.Where(p => p != null));
        State.PermitCases.Clear();
        if (snapshot.PermitCases != null)
            State.PermitCases.AddRange(snapshot.PermitCases.Where(c => c != null));
        State.NextConstructionProjectNumber = Math.Max(1, snapshot.NextConstructionProjectNumber);
        State.NextPermitCaseNumber = Math.Max(1, snapshot.NextPermitCaseNumber);
        State.RentRingFencedCostsThisTaxYear = snapshot.RentRingFencedCostsThisTaxYear;
    }
}
