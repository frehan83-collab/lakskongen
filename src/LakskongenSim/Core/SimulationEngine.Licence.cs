using System;
using System.Linq;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Phase 4.1 — green-zone growth-licence auctions.
///
/// The auction lifecycle lives here as player verbs + one post-tick phase:
/// <list type="bullet">
/// <item><see cref="ApplyForGrowthLicence"/> opens an auction for an
/// eligible zone (not red, no player site, no held licence).</item>
/// <item><see cref="BidOnLicenseAuction"/> raises the player's bid against
/// AI rivals that bid from their CashReserves.</item>
/// <item><see cref="TickLicenseAuctions"/> (post-tick, before the balance
/// mirror) drives rival raises, settles closed auctions and lets rivals
/// occasionally open their own.</item>
/// <item><see cref="CanBuildSeaSite"/> gates site construction: the first
/// site and any site in an already-operated zone are a development right;
/// entering a NEW zone requires a won (unconsumed) growth licence.</item>
/// </list>
/// Reserve price and construction cost both come from calibration
/// (150M NOK licence, 6M NOK site build) via <c>_balancing</c>.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Reserve price for growth-licence auctions (calibration: 150M NOK).</summary>
    public decimal LicenseReservePriceNOK => _balancing.LicenseCostNOK;

    /// <summary>Construction cost of a sea site (calibration: 6M NOK).</summary>
    public decimal SiteBuildCostNOK => _balancing.SeaSiteBuildCostNOK;

    /// <summary>The auction subsystem (owning the license_auction RNG stream).</summary>
    public Finance.LicenseAuctionSystem LicenseAuctionSystem { get; private set; } = null!;

    // ─── Player verbs ────────────────────────────────────────────────────────

    /// <summary>
    /// Open a growth-licence auction for a zone the player does not operate
    /// yet. Opening is free — you only pay if you win.
    /// </summary>
    public CommandResult ApplyForGrowthLicence(string zoneId)
    {
        if (State?.Zones == null || LicenseAuctionSystem == null || TrafficLightSystem == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var zone = State.Zones.FirstOrDefault(z => z.Id == zoneId);
        if (zone == null)
            return CommandResult.Fail("zone_not_found", $"Zone '{zoneId}' not found");

        if (TrafficLightSystem.GetZoneColor(zoneId) == ZoneColor.Red)
            return CommandResult.Fail("zone_red",
                $"Zone '{zoneId}' is in the red — no new growth licences are auctioned there");

        if (State.SeaSites.Any(s => s.ZoneId == zoneId))
            return CommandResult.Fail("already_operating",
                $"You already operate in zone '{zoneId}' — build there directly");

        if (HasGrowthLicence(zoneId))
            return CommandResult.Fail("licence_held",
                $"You already hold a growth licence for zone '{zoneId}'");

        if (State.LicenseAuctions.Any(a => a.ZoneId == zoneId && a.Status == "open"))
            return CommandResult.Fail("auction_open",
                $"An auction for zone '{zoneId}' is already open");

        var auction = Finance.LicenseAuctionSystem.Start(
            NewAuctionId(zoneId), zoneId, State.Day, LicenseReservePriceNOK, playerEntered: true);
        State.LicenseAuctions.Add(auction);

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Growth Licence Auction",
            Category = EventCategory.Regulation,
            Description =
                $"Auction opened for zone '{zoneId}' — closes day {auction.ClosesDay}, " +
                $"reserve {auction.ReservePriceNOK:N0} NOK"
        });
        State.TrimEventLog();

        return CommandResult.Success(
            $"Auction opened for zone '{zoneId}' (closes day {auction.ClosesDay})",
            id: auction.Id);
    }

    /// <summary>Place (or raise) the player's bid on the open auction of a zone.</summary>
    public CommandResult BidOnLicenseAuction(string zoneId, decimal amountNOK)
    {
        if (State?.LicenseAuctions == null || FinanceManager == null || LicenseAuctionSystem == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        // Prefer the live auction; fall back to the most recent closed one so
        // a late bid reports "auction_closed" instead of a misleading
        // "auction_not_found".
        var auction = State.LicenseAuctions
            .Where(a => a.ZoneId == zoneId)
            .OrderByDescending(a => a.OpensDay)
            .FirstOrDefault(a => a.Status == "open")
            ?? State.LicenseAuctions
                .Where(a => a.ZoneId == zoneId)
                .OrderByDescending(a => a.OpensDay)
                .FirstOrDefault();
        if (auction == null)
            return CommandResult.Fail("auction_not_found", $"No auction for zone '{zoneId}'");

        if (!Finance.LicenseAuctionSystem.TryPlayerBid(
                auction, amountNOK, FinanceManager.Balance, State.Day,
                out var errorKey, out var message))
            return CommandResult.Fail(errorKey, message);

        return CommandResult.Success(message, id: auction.Id);
    }

    /// <summary>True while the player holds an unconsumed growth licence for the zone.</summary>
    public bool HasGrowthLicence(string zoneId)
        => State?.LicenseAuctions?.Any(a =>
            a.ZoneId == zoneId && a.Status == "won_player" && !a.ConsumedByBuild) == true;

    /// <summary>
    /// Gate for building a sea site: first site ever and sites in an
    /// already-operated zone are free-form (development right); entering a
    /// new zone requires an unconsumed won growth licence.
    /// </summary>
    public bool CanBuildSeaSite(string zoneId, out string reason)
    {
        reason = "";
        if (State?.Zones == null || State.SeaSites == null)
        {
            reason = "engine_not_ready";
            return false;
        }
        if (!State.Zones.Any(z => z.Id == zoneId))
        {
            reason = $"Zone '{zoneId}' not found";
            return false;
        }
        if (State.SeaSites.Count == 0) return true;              // development right
        if (State.SeaSites.Any(s => s.ZoneId == zoneId)) return true; // organic growth
        if (HasGrowthLicence(zoneId)) return true;               // won at auction
        reason = $"Zone '{zoneId}' requires a growth licence — win the auction first";
        return false;
    }

    /// <summary>
    /// Build a sea site (780 t MAB, 8 pens). Charges the calibration
    /// construction cost and consumes a held growth licence when the build
    /// enters a new zone. This is the single build path the bridge proxies.
    /// </summary>
    public CommandResult BuildSeaSite(string zoneId, string name)
    {
        if (State?.SeaSites == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        if (!CanBuildSeaSite(zoneId, out var gateReason))
            return CommandResult.Fail("growth_licence_required", gateReason);

        decimal cost = SiteBuildCostNOK;
        if (FinanceManager.Balance < cost)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK to build the site");

        var siteId = string.IsNullOrWhiteSpace(name)
            ? $"site_{State.SeaSites.Count + 1}"
            : name;
        var site = new SeaSite(siteId, zoneId)
        {
            MABTonnes = 780.0,
            NumberOfPens = 8,
            PenVolume = 50000f,
            MaxAllowedDensity = 25.0f
        };
        State.SeaSites.Add(site);
        PayCost(cost, CostBooking.Capex, "sea_site");   // affordability checked above
        ConsumeGrowthLicence(zoneId);

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "License Acquisition",
            Category = EventCategory.Regulation,
            Description = $"Built new sea site '{siteId}' in zone '{zoneId}' (780T MAB)",
            BalanceChange = -cost
        });
        State.TrimEventLog();

        return CommandResult.Success(
            $"Built sea site '{siteId}' in zone '{zoneId}' for {cost:N0} NOK", id: siteId);
    }

    // ─── Daily auction tick (post-tick, before the balance mirror) ──────────

    /// <summary>
    /// One day of auction life: rival raises on open auctions, settlement of
    /// closed ones, then — while the market is quiet — the occasional
    /// rival-initiated opening.
    /// </summary>
    internal void TickLicenseAuctions()
    {
        if (LicenseAuctionSystem == null || FinanceManager == null
            || CompetitorAI == null || State?.LicenseAuctions == null)
            return;

        // 1. Rival raises (only auctions still inside their bidding window).
        LicenseAuctionSystem.RivalBids(
            State.LicenseAuctions, CompetitorAI.Competitors, State.Day);

        // 2. Settle auctions past their close day.
        foreach (var auction in State.LicenseAuctions
                     .Where(a => a.Status == "open" && State.Day > a.ClosesDay)
                     .ToList())
            ApplyAuctionSettlement(auction);

        // 3. Rival-initiated opening while nothing is on the block.
        if (!State.LicenseAuctions.Any(a => a.Status == "open"))
            MaybeOpenRivalAuction();

        PruneSettledAuctions();
    }

    /// <summary>Apply a settlement: money moves, licences grant, history logs.</summary>
    private void ApplyAuctionSettlement(LicenseAuctionState auction)
    {
        var settlement = Finance.LicenseAuctionSystem.Settle(
            auction, State.Day, FinanceManager.Balance, CompetitorAI.Competitors);

        switch (settlement.Status)
        {
            case "won_player":
            {
                // Settlement already required the player to be solvent; the
                // licence is an intangible asset, not a P&L cost.
                PayCost(settlement.AmountNOK, CostBooking.Capex, "growth_licence");
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Growth Licence Won",
                    Category = EventCategory.Regulation,
                    Description =
                        $"Won growth licence auction for zone '{auction.ZoneId}' " +
                        $"at {settlement.AmountNOK:N0} NOK",
                    BalanceChange = -settlement.AmountNOK
                });
                _dayNotes.Add(new DayNote
                {
                    Kind = "license_auction",
                    SiteId = auction.ZoneId,
                    Success = true,
                    Message =
                        $"Growth licence for '{auction.ZoneId}' won at {settlement.AmountNOK:N0} NOK",
                    Value = (double)settlement.AmountNOK
                });
                FireEvent(
                    $"🏆 Growth licence for zone '{auction.ZoneId}' won at {settlement.AmountNOK:N0} NOK");
                break;
            }
            case "won_rival":
            {
                var rival = CompetitorAI.Competitors.FirstOrDefault(
                    c => c.Id == settlement.WinnerId);
                if (rival != null)
                {
                    rival.CashReserves -= settlement.AmountNOK;
                    rival.LicenseCount++;
                    rival.SiteCount++;
                    rival.TotalMABTonnes += 780f;
                }
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Competitor Wins Licence Auction",
                    Category = EventCategory.Competitor,
                    Description =
                        $"{rival?.Name ?? settlement.WinnerId} won the growth licence for zone " +
                        $"'{auction.ZoneId}' at {settlement.AmountNOK:N0} NOK"
                });
                _dayNotes.Add(new DayNote
                {
                    Kind = "license_auction",
                    SiteId = auction.ZoneId,
                    Success = false,
                    Message =
                        $"{rival?.Name ?? settlement.WinnerId} won the growth licence for " +
                        $"'{auction.ZoneId}'",
                    Value = (double)settlement.AmountNOK
                });
                FireEvent(
                    $"📋 {rival?.Name ?? settlement.WinnerId} won the growth licence for zone '{auction.ZoneId}'");
                break;
            }
            case "forfeited":
            {
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Licence Auction Forfeited",
                    Category = EventCategory.Regulation,
                    Description =
                        $"Auction for zone '{auction.ZoneId}' closed — no bidder could cover the price"
                });
                _dayNotes.Add(new DayNote
                {
                    Kind = "license_auction",
                    SiteId = auction.ZoneId,
                    Success = false,
                    Message = $"Auction for '{auction.ZoneId}' forfeited — nobody could pay"
                });
                break;
            }
            default: // expired (no bids at all)
            {
                _dayNotes.Add(new DayNote
                {
                    Kind = "license_auction",
                    SiteId = auction.ZoneId,
                    Success = false,
                    Message = $"Auction for '{auction.ZoneId}' closed with no bids"
                });
                break;
            }
        }
        State.TrimEventLog();
    }

    /// <summary>
    /// Rivals occasionally put a growth licence of their own on the block:
    /// only while no auction is open, only for zones the player does not
    /// operate or hold a licence for, never in a red zone.
    /// </summary>
    private void MaybeOpenRivalAuction()
    {
        var eligible = State.Zones
            .Where(z => TrafficLightSystem.GetZoneColor(z.Id) != ZoneColor.Red)
            .Where(z => !State.SeaSites.Any(s => s.ZoneId == z.Id))
            .Where(z => !HasGrowthLicence(z.Id))
            .Select(z => z.Id)
            .ToList();
        if (eligible.Count == 0) return;

        var auction = LicenseAuctionSystem.MaybeOpenRivalAuction(
            id: "", eligible, State.Day, LicenseReservePriceNOK);
        if (auction == null) return;

        // The zone is picked by the RNG above; build the id around it.
        auction.Id = NewAuctionId(auction.ZoneId);
        State.LicenseAuctions.Add(auction);

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Growth Licence Auction",
            Category = EventCategory.Regulation,
            Description =
                $"Rivals opened a growth licence auction for zone '{auction.ZoneId}' — " +
                $"closes day {auction.ClosesDay}, reserve {auction.ReservePriceNOK:N0} NOK"
        });
        _dayNotes.Add(new DayNote
        {
            Kind = "license_auction",
            SiteId = auction.ZoneId,
            Success = false,
            Message = $"New growth licence auction for '{auction.ZoneId}' (closes day {auction.ClosesDay})",
            Value = (double)auction.ReservePriceNOK
        });
        FireEvent(
            $"📋 New growth licence auction for zone '{auction.ZoneId}' — closes day {auction.ClosesDay}");
        State.TrimEventLog();
    }

    /// <summary>Deterministic auction id: lic_{zoneId}_d{day}_{seq}.</summary>
    private string NewAuctionId(string zoneId)
        => $"lic_{zoneId}_d{State.Day}_{State.LicenseAuctions.Count}";

    /// <summary>
    /// Keep settled auctions out of the save forever: drop them 90 days
    /// after close, except won_player licences the player has not built with
    /// yet (those stay until <see cref="ConsumeGrowthLicence"/>).
    /// </summary>
    private void PruneSettledAuctions()
    {
        const int keepDays = 90;
        State.LicenseAuctions.RemoveAll(a =>
            a.Status != "open"
            && State.Day > a.ClosesDay + keepDays
            && !(a.Status == "won_player" && !a.ConsumedByBuild));
    }

    /// <summary>Mark the zone's held growth licence as used by a site build.</summary>
    private void ConsumeGrowthLicence(string zoneId)
    {
        var licence = State.LicenseAuctions.FirstOrDefault(a =>
            a.ZoneId == zoneId && a.Status == "won_player" && !a.ConsumedByBuild);
        if (licence != null) licence.ConsumedByBuild = true;
    }

    // ─── Phase 4.2: Distressed asset acquisition ──────────────────────────────

    /// <summary>
    /// Acquire a distressed asset from a crisis event. Transfers the actual
    /// site/licenses/MAB from the distressed competitor to the player.
    /// </summary>
    public CommandResult AcquireDistressedAsset(string assetId)
    {
        if (State == null || FinanceManager == null || CrisisCapitalism == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var assets = CrisisCapitalism.GetAllAvailableDistressedAssets();
        var asset = assets.FirstOrDefault(a => a.Id == assetId);
        if (asset == null)
            return CommandResult.Fail("asset_not_found", $"Distressed asset '{assetId}' not found or expired");

        if (FinanceManager.Balance < asset.DiscountedPriceNOK)
            return CommandResult.Fail("insufficient_funds",
                $"Need {asset.DiscountedPriceNOK:N0} NOK, have {FinanceManager.Balance:N0} NOK");

        // Execute purchase
        var result = CrisisCapitalism.PurchaseDistressedAsset(asset, FinanceManager.Balance);
        if (!result.Success)
            return CommandResult.Fail("purchase_failed", result.Message);

        // Transfer actual assets from competitor to player
        var competitor = CompetitorAI?.Competitors.FirstOrDefault(c => c.Id == asset.SourceCompetitorId);
        if (competitor != null)
        {
            // Transfer site
            if (!string.IsNullOrEmpty(asset.SiteId) && asset.MABTonnes.HasValue)
            {
                var newSite = new SeaSite(asset.SiteId!, asset.Location ?? "zone-1")
                {
                    MABTonnes = asset.MABTonnes.Value,
                    NumberOfPens = 8,
                    PenVolume = 50000f,
                    MaxAllowedDensity = 25.0f
                };
                State.SeaSites.Add(newSite);
            }

            // Transfer licenses
            if (asset.LicenseCount.HasValue && asset.LicenseCount > 0)
            {
                for (int i = 0; i < asset.LicenseCount.Value; i++)
                {
                    State.Licenses.Add(new LicenseRecord
                    {
                        SiteId = $"acquired_{asset.Id}_{i}",
                        ExpiryDay = State.Day + 3650, // 10 years
                        AnnualFee = _balancing.LicenseCostNOK * 0.1m
                    });
                }
            }

            // Reduce competitor's holdings
            if (!string.IsNullOrEmpty(asset.SiteId))
            {
                competitor.SiteCount = Math.Max(0, competitor.SiteCount - 1);
                competitor.TotalMABTonnes = (float)Math.Max(0.0, competitor.TotalMABTonnes - (asset.MABTonnes ?? 780.0));
            }
            if (asset.LicenseCount.HasValue && asset.LicenseCount > 0)
            {
                competitor.LicenseCount = Math.Max(0, competitor.LicenseCount - asset.LicenseCount.Value);
            }
        }

        // Charge player (acquired assets are capital spending)
        PayCost(asset.DiscountedPriceNOK, CostBooking.Capex, "acquisitions");

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Distressed Asset Acquisition",
            Category = EventCategory.Competitor,
            Description = $"Acquired {asset.AssetType} '{asset.Name}' from {asset.OwningCompetitor} for {asset.DiscountedPriceNOK:N0} NOK",
            BalanceChange = -asset.DiscountedPriceNOK
        });
        _dayNotes.Add(new DayNote
        {
            Kind = "license_auction",
            SiteId = asset.Id,
            Success = true,
            Message = $"Acquired distressed {asset.AssetType.ToString().ToLower()} from {asset.OwningCompetitor}",
            Value = (double)asset.DiscountedPriceNOK
        });
        FireEvent($"💰 Acquired distressed {asset.AssetType.ToString().ToLower()} from {asset.OwningCompetitor} for {asset.DiscountedPriceNOK:N0} NOK");
        State.TrimEventLog();

        return CommandResult.Success(result.Message, value: (double)asset.DiscountedPriceNOK, id: asset.Id);
    }

    // ─── Phase 4.4: Research & Vertical Integration helpers ──────────────────────

    /// <summary>
    /// Get the total unlocked research effect for a given key.
    /// Returns 0.0 if no unlocked research contributes to that key.
    /// </summary>
    public double GetResearchEffect(string key)
    {
        if (Research == null) return 0.0;
        return Research.GetTotalEffect(key);
    }

    /// <summary>
    /// Growth rate bonus from unlocked research (e.g., selective breeding, growth optimization).
    /// Additive bonus applied to SDE growth rate.
    /// </summary>
    public double GrowthRateBonus => GetResearchEffect(ResearchEffectKeys.GrowthRateBonus);

    /// <summary>
    /// FCR reduction from unlocked research (e.g., feed efficiency genetics, automated feeding).
    /// Multiplicative reduction applied to base FCR.
    /// </summary>
    public double FCRReduction => GetResearchEffect(ResearchEffectKeys.FcrReduction);

    /// <summary>
    /// Feed waste reduction from unlocked research (e.g., automated feeding systems).
    /// Reduces feed spill and overfeeding losses.
    /// </summary>
    public double FeedWasteReduction => GetResearchEffect(ResearchEffectKeys.FeedWasteReduction);

    /// <summary>
    /// Mortality reduction from unlocked research (e.g., disease resistance, sensor networks):
    /// the fraction of the baseline daily hazard removed (capped, see ResearchEffectKeys).
    /// </summary>
    public double MortalityReduction => GetResearchEffect(ResearchEffectKeys.MortalityReduction);

    /// <summary>
    /// Disease resistance bonus from unlocked research (e.g., disease-resistant genetics, AI monitoring).
    /// Redases disease outbreak probability and severity.
    /// </summary>
    public double DiseaseResistanceBonus => GetResearchEffect(ResearchEffectKeys.DiseaseResistanceBonus);

    /// <summary>
    /// Market price bonus from unlocked research (e.g., premium products, organic certification).
    /// Multiplicative bonus applied to salmon spot price.
    /// </summary>
    public double MarketPriceBonus => GetResearchEffect(ResearchEffectKeys.MarketPriceBonus);

    /// <summary>
    /// Sustainability score bonus from unlocked research (e.g., organic certification, circular economy).
    /// Additive bonus to sustainability score.
    /// </summary>
    public double SustainabilityScoreBonus => GetResearchEffect(ResearchEffectKeys.SustainabilityScoreBonus);

    /// <summary>
    /// Fraction of new sea-lice settlement blocked at every site by research
    /// (e.g., underwater drones, deep-sea farming). Combined with skirts,
    /// containment and barriers in the per-site settlement blocking.
    /// </summary>
    public double LiceReduction => GetResearchEffect(ResearchEffectKeys.LiceReduction);

    /// <summary>
    /// Fractional reduction of fuel and electricity emissions (Scope 1-2) in the
    /// carbon ledger from research (e.g., renewable energy, carbon capture).
    /// </summary>
    public double CarbonReduction => GetResearchEffect(ResearchEffectKeys.CarbonReduction);

    /// <summary>
    /// Vertical Integration: Total cost savings per kg from owned and completed integration tiers.
    /// Includes synergy multipliers for adjacent tiers and full integration bonus.
    /// </summary>
    public double VerticalIntegrationCostSavingPerKg
    {
        get
        {
            if (VerticalIntegration == null) return 0.0;
            var ownedTiers = VerticalIntegration.GetOwnedTiers();
            if (ownedTiers.Count == 0) return 0.0;
            
            // Estimate annual production for savings calculation
            double annualProductionKg = State.Populations.Sum(p => p.GetTotalBiomassKg()) * 365.0 / Math.Max(1, State.Day);
            if (annualProductionKg <= 0) annualProductionKg = 10_000_000; // fallback
            
            float savings = VerticalIntegration.CalculateTotalCostSavings(ownedTiers, (float)annualProductionKg);
            return savings / annualProductionKg; // per kg
        }
    }

    /// <summary>
    /// Vertical Integration: Quality premium multiplier from owned and completed tiers.
    /// Returns a multiplier (e.g., 1.03 = 3% premium).
    /// </summary>
    public double VerticalIntegrationQualityMultiplier
    {
        get
        {
            if (VerticalIntegration == null) return 1.0;
            var ownedTiers = VerticalIntegration.GetOwnedTiers();
            if (ownedTiers.Count == 0) return 1.0;
            return 1.0 + VerticalIntegration.CalculateQualityPremium(ownedTiers);
        }
    }

    /// <summary>
    /// Vertical Integration: Annual OPEX from owned and completed integration assets.
    /// Includes maintenance costs (CAPEX × maintenance%).
    /// </summary>
    public decimal VerticalIntegrationAnnualOpexNOK
    {
        get
        {
            if (VerticalIntegration == null) return 0m;
            return (decimal)VerticalIntegration.GetAnnualIntegrationOpex();
        }
    }

    /// <summary>
    /// Vertical Integration: Total CAPEX invested in owned integration assets.
    /// </summary>
    public decimal VerticalIntegrationTotalCapexNOK
    {
        get
        {
            if (VerticalIntegration == null) return 0m;
            return (decimal)VerticalIntegration.GetTotalIntegrationCapex();
        }
    }

    /// <summary>
    /// Get all owned integration assets for UI display.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<LakskongenSim.Expansion.IntegrationAsset> OwnedIntegrationAssets
    {
        get
        {
            if (VerticalIntegration == null) return System.Array.Empty<LakskongenSim.Expansion.IntegrationAsset>();
            return VerticalIntegration.GetOwnedAssets();
        }
    }
}
