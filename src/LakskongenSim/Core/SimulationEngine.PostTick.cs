using System.Linq;

namespace LakskongenSim.Core;

/// <summary>
/// Ordered daily tick phase (Phase 1 wave 3). Runs once per simulated day
/// inside <see cref="SimulationEngine.Advance"/> AFTER <c>State.Day++</c>.
/// Registration order IS the execution order — one source of truth, no
/// separate Order integers. The contract deliberately takes the whole
/// engine (no TickContext data-bag): in-day AdvanceOneDay blocks can migrate
/// onto it later without a contract change, each carrying its own interval
/// state, reading whatever it needs from the engine directly.
/// </summary>
internal interface ITickSystem
{
    void Tick(SimulationEngine engine);
}

/// <summary>
/// POST-TICK phase 1: storm episode impacts + temperature stress. Moved out
/// of AdvanceOneDay (it previously ran mid-day, before Market/Stock blocks):
/// the episode now fires after the day turns, which shifts event day-stamps
/// by +1, defers the repair-affordability check until after daily finance,
/// and books insurance revenue in the same quarter as the episode. The
/// edge-tracking state still lives on the engine (reset in Reset(), S9
/// hygiene by construction).
/// </summary>
internal sealed class StormImpactsTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.ApplyStormAndTemperatureImpacts();
}

/// <summary>
/// POST-TICK phase 2: due wellboat jobs. Execution AFTER State.Day++ keeps
/// the booking contract (DueDay = bookingDay + delay) exact — moving this
/// inside AdvanceOneDay would deliver every boat one day early.
/// </summary>
internal sealed class WellboatExecutionTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.ProcessDueWellboatJobs();
}

/// <summary>
/// POST-TICK phase 2b: planned harvests (S2-04). After State.Day++ like the
/// wellboat jobs, so an order's slot day is exact; slaughtered batches sell
/// before the balance mirror and the solvency check see the day's cash.
/// </summary>
internal sealed class HarvestOrdersTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.ProcessHarvestOrders();
}

/// <summary>
/// POST-TICK phase (S2-01a): capacity enforcement. Runs after the day's
/// wellboat harvests (so a harvest that lands today counts) and before the
/// balance mirror (so a fee, a confiscation or a forced harvest syncs into
/// State.Balance the same day): every production area's standing biomass
/// against its licence MTB and every site against its locality MTB.
/// </summary>
internal sealed class CapacityEnforcementTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.EnforceCapacity();
}

/// <summary>
/// POST-TICK phase 3: licence auctions (Phase 4.1). Runs BEFORE the balance
/// mirror so settlement charges (player wins) sync into State.Balance the
/// same day. Drives rival raises, settlement of closed auctions and
/// rival-initiated openings.
/// </summary>
internal sealed class LicenseAuctionTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.TickLicenseAuctions();
}

/// <summary>
/// POST-TICK phase 4: balance mirror sync. Wellboat, storm and auction
/// economies land in FinanceManager after the in-day mirror, so the mirror
/// is re-synced here every day — State.Balance never reads a day stale.
/// </summary>
internal sealed class BalanceMirrorTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine)
    {
        #pragma warning disable CS0618 // Balance mirror: save-compat field by design
        engine.State.Balance = engine.FinanceManager.Balance;
        #pragma warning restore CS0618
    }
}

/// <summary>
/// POST-TICK phase 5: solvency (S1-03). Runs last, after every cash flow of
/// the day (storms, wellboats, auctions): the warning ladder and the
/// per-scenario bankruptcy floor judge the day's closing cash.
/// </summary>
internal sealed class SolvencyTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.CheckSolvency();
}

/// <summary>
/// POST-TICK phase (U2-03): interest on negative cash. After every cash flow
/// of the day and before the balance mirror, so the mirror and the solvency
/// check see the day's closing cash with its interest.
/// </summary>
internal sealed class OverdraftInterestTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.ChargeOverdraftInterest();
}

/// <summary>
/// POST-TICK phase (U2-03), last: the P&amp;L story closes the day and the
/// liquidity ladder reads the closing cash (warning, bank call, rescue offer).
/// After the solvency check, so a bankrupt company gets no bank call.
/// </summary>
internal sealed class LiquidityTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => engine.CheckLiquidity();
}
