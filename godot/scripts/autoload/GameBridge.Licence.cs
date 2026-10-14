using Godot;
using System.Linq;

// Phase 4.1 — licence auctions (growth licences for green zones).
// Thin Godot-facing projection over the engine commands; all rules live in
// SimulationEngine.Licence.cs / Finance.LicenseAuctionSystem.cs.
public partial class GameBridge : Node
{
    /// <summary>Reserve price for growth-licence auctions (NOK, calibration).</summary>
    public double GetLicenseCost()
    {
        return (double)(SimulationEngine?.LicenseReservePriceNOK ?? 150_000_000m);
    }

    /// <summary>
    /// Open a growth-licence auction for a zone the player does not operate yet.
    /// Returns a Dictionary: "success", "message", "cost", "auction_id",
    /// "closes_day" — plus the legacy keys older callers read.
    /// </summary>
    public Godot.Collections.Dictionary ApplyForLicense(string zoneId)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            return result;
        }

        var outcome = SimulationEngine.ApplyForGrowthLicence(zoneId);
        result["success"] = outcome.Ok;
        result["message"] = outcome.Message;
        result["cost"] = GetLicenseCost();
        // S2-01a: the licence an auction grants holds its area's size (945 t in Troms and Finnmark).
        result["mab_tonnes"] = (double)SimulationEngine.StandardLicenceMtbTonnes(zoneId);
        result["site_id"] = "";

        if (outcome.Ok)
        {
            var auction = SimulationEngine.State?.LicenseAuctions
                .FirstOrDefault(a => a.Id == outcome.Id);
            if (auction != null)
            {
                result["auction_id"] = auction.Id;
                result["closes_day"] = auction.ClosesDay;
                result["current_bid"] = (double)auction.CurrentBidNOK;
                result["leader"] = auction.LeaderKind;
            }
            EmitSignal(SignalName.GameStateUpdated);
        }

        return result;
    }

    /// <summary>
    /// Place (or raise) the player's bid on the open auction of a zone.
    /// Returns a Dictionary: "success", "message", "auction_id",
    /// "current_bid", "closes_day", "leader".
    /// </summary>
    public Godot.Collections.Dictionary PlaceLicenseBid(string zoneId, double amountNOK)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            return result;
        }

        var outcome = SimulationEngine.BidOnLicenseAuction(zoneId, (decimal)amountNOK);
        result["success"] = outcome.Ok;
        result["message"] = outcome.Message;
        result["auction_id"] = outcome.Id ?? "";

        var auction = SimulationEngine.State?.LicenseAuctions
            .FirstOrDefault(a => a.ZoneId == zoneId && a.Status == "open");
        if (auction != null)
        {
            result["auction_id"] = auction.Id;
            result["current_bid"] = (double)auction.CurrentBidNOK;
            result["closes_day"] = auction.ClosesDay;
            result["leader"] = auction.LeaderKind;
        }

        if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// All growth-licence auctions for the UI. Keys: "id", "zone_id",
    /// "opens_day", "closes_day", "reserve", "current_bid", "leader_kind",
    /// "leader_id", "status", "min_bid", "player_entered", "consumed".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLicenseAuctions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var auctions = SimulationEngine?.State?.LicenseAuctions;
        if (auctions == null) return result;

        int today = SimulationEngine?.State?.Day ?? 0;
        foreach (var a in auctions)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = a.Id,
                ["zone_id"] = a.ZoneId,
                ["opens_day"] = a.OpensDay,
                ["closes_day"] = a.ClosesDay,
                ["reserve"] = (double)a.ReservePriceNOK,
                ["current_bid"] = (double)a.CurrentBidNOK,
                ["leader_kind"] = a.LeaderKind,
                ["leader_id"] = a.LeaderId,
                ["status"] = a.Status,
                ["min_bid"] = (double)LakskongenSim.Finance.LicenseAuctionSystem.MinBid(a),
                ["player_entered"] = a.PlayerEntered,
                ["consumed"] = a.ConsumedByBuild,
                ["days_left"] = a.Status == "open" ? System.Math.Max(0, a.ClosesDay - today) : 0
            });
        }

        return result;
    }

    /// <summary>True while the player holds an unconsumed growth licence for the zone.</summary>
    public bool HasGrowthLicense(string zoneId)
    {
        return SimulationEngine?.HasGrowthLicence(zoneId) == true;
    }

    /// <summary>
    /// Build gate for a zone. Returns {"allowed": bool, "reason": string} —
    /// reason is "" when allowed. The first site ever and sites in an
    /// already-operated zone are a development right; entering a new zone
    /// requires a won growth licence.
    /// </summary>
    public Godot.Collections.Dictionary CanBuildSeaSite(string zoneId)
    {
        var result = new Godot.Collections.Dictionary();
        string reason = "engine_not_ready";
        bool allowed = SimulationEngine != null
            && SimulationEngine.CanBuildSeaSite(zoneId, out reason);
        result["allowed"] = allowed;
        result["reason"] = allowed ? "" : reason;
        return result;
    }

    // ─── Phase 4.2: Distressed asset acquisition ────────────────────────────

    /// <summary>
    /// Get all currently available distressed assets for the UI.
    /// Keys: "id", "type", "name", "competitor", "original_value", "discounted_price",
    /// "discount_pct", "risk_score", "months_remaining", "crisis_type", "site_id", "license_count", "mab_tonnes"
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetDistressedAssets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.CrisisCapitalism == null) return result;

        var assets = SimulationEngine.CrisisCapitalism.GetAllAvailableDistressedAssets(SimulationEngine.State.Day);
        var crises = SimulationEngine.CrisisCapitalism.GetActiveCrises(SimulationEngine.State.Day);

        foreach (var a in assets)
        {
            var crisis = crises.FirstOrDefault(c => c.Id == a.CrisisEventId);
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = a.Id,
                ["type"] = a.AssetType.ToString(),
                ["name"] = a.Name,
                ["competitor"] = a.OwningCompetitor,
                ["original_value"] = (double)a.OriginalValueNOK,
                ["discounted_price"] = (double)a.DiscountedPriceNOK,
                ["discount_pct"] = a.DiscountPercentage,
                ["risk_score"] = a.RiskScore,
                ["months_remaining"] = a.TimeRemainingMonths,
                ["crisis_type"] = crisis?.Type.ToString() ?? "unknown",
                ["site_id"] = a.SiteId ?? "",
                ["license_count"] = a.LicenseCount ?? 0,
                ["mab_tonnes"] = a.MABTonnes ?? 0f
            });
        }

        return result;
    }

    /// <summary>
    /// Attempt to acquire a distressed asset. Returns {"success": bool, "message": string, "asset": dict}.
    /// </summary>
    public Godot.Collections.Dictionary AcquireDistressedAsset(string assetId)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine == null)
        {
            result["success"] = false;
            result["message"] = "Engine not initialized";
            return result;
        }

        var outcome = SimulationEngine.AcquireDistressedAsset(assetId);
        result["success"] = outcome.Ok;
        result["message"] = outcome.Message;

        if (outcome.Ok)
        {
            // The purchase removed the listing, so describe it from the outcome.
            result["asset"] = new Godot.Collections.Dictionary
            {
                ["id"] = assetId,
                ["price_paid"] = (double)outcome.Value
            };
            EmitSignal(SignalName.GameStateUpdated);
        }

        return result;
    }
}
