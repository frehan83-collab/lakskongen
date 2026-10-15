using Godot;
using System.Linq;
using LakskongenSim.Core;

// U2-01 — the expansion flow: the map's areas, zones and verdicts, the build
// plan (BuildSiteDialog), the licence auction (LicenceAuctionCard) and the
// auction items of the attention inbox. A thin projection of
// SimulationEngine.Expansion.cs: every refusal travels as a reason key with
// its translation key (EXPANSION_REASON_*), never as text the UI parses.
public partial class GameBridge : Node
{
    /// <summary>The translation key of an expansion reason ("" when allowed).</summary>
    private static string ExpansionLabelKey(string reasonKey)
        => string.IsNullOrEmpty(reasonKey) ? "" : "EXPANSION_REASON_" + reasonKey.ToUpperInvariant();

    private static Godot.Collections.Dictionary NotReady()
        => new()
        {
            ["success"] = false,
            ["allowed"] = false,
            ["reason_key"] = ExpansionReasons.EngineNotReady,
            ["label_key"] = ExpansionLabelKey(ExpansionReasons.EngineNotReady),
        };

    /// <summary>
    /// The expansion map. Keys: "success", "build_cost_nok",
    /// "licence_reserve_nok", "balance_nok", "auction_length_days" and "areas"
    /// (13 rows: production_area, code, name, licence_size_t, licences,
    /// licence_mtb_t, standing_t, headroom_t, traffic_light, sea_temp_min_c,
    /// sea_temp_max_c, in_pd_zone, zones). A zone row: zone_id, label,
    /// production_area, in_catalogue, in_game, player_sites, traffic_light,
    /// can_build, build_reason_key, build_label_key, can_apply,
    /// apply_reason_key, apply_label_key, holds_licence, open_auction_id.
    /// </summary>
    public Godot.Collections.Dictionary GetExpansionOverview()
    {
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            var empty = NotReady();
            empty["areas"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return empty;
        }
        var overview = engine.GetExpansionOverview();
        var areas = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var a in overview.Areas)
        {
            var zones = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var z in a.Zones)
                zones.Add(new Godot.Collections.Dictionary
                {
                    ["zone_id"] = z.ZoneId,
                    ["label"] = z.Label,
                    ["production_area"] = z.ProductionArea,
                    ["in_catalogue"] = z.InCatalogue,
                    ["in_game"] = z.InGame,
                    ["player_sites"] = z.PlayerSites,
                    ["traffic_light"] = z.TrafficLight,
                    ["can_build"] = z.Build.Allowed,
                    ["build_reason_key"] = z.Build.ReasonKey,
                    ["build_label_key"] = ExpansionLabelKey(z.Build.ReasonKey),
                    ["can_apply"] = z.Apply.Allowed,
                    ["apply_reason_key"] = z.Apply.ReasonKey,
                    ["apply_label_key"] = ExpansionLabelKey(z.Apply.ReasonKey),
                    ["holds_licence"] = z.HoldsLicence,
                    ["open_auction_id"] = z.OpenAuctionId,
                });
            areas.Add(new Godot.Collections.Dictionary
            {
                ["production_area"] = a.ProductionArea,
                ["code"] = a.Code,
                ["name"] = a.Name,
                ["licence_size_t"] = a.LicenceSizeTonnes,
                ["licences"] = a.Licences,
                ["licence_mtb_t"] = a.LicenceMtbTonnes,
                ["standing_t"] = a.StandingTonnes,
                ["headroom_t"] = a.HeadroomTonnes,
                ["traffic_light"] = a.TrafficLight,
                ["sea_temp_min_c"] = a.SeaTemperatureMinC,
                ["sea_temp_max_c"] = a.SeaTemperatureMaxC,
                ["in_pd_zone"] = a.InPdZone,
                ["zones"] = zones,
            });
        }
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["build_cost_nok"] = (double)overview.BuildCostNok,
            ["licence_reserve_nok"] = (double)overview.LicenceReserveNok,
            ["balance_nok"] = (double)overview.BalanceNok,
            ["auction_length_days"] = overview.AuctionLengthDays,
            ["areas"] = areas,
        };
    }

    /// <summary>
    /// What building a site in a zone costs, holds and adds (BuildSiteDialog).
    /// Keys: "success", "allowed", "reason_key", "label_key", "zone_id",
    /// "label", "production_area", "new_zone", "cost_nok", "balance_nok",
    /// "locality_mtb_t", "licence_mtb_added_t" (always 0),
    /// "area_licence_mtb_t", "pens", "pen_circumference_m", "pen_net_depth_m",
    /// "pen_volume_m3", "max_fish_per_pen", "workers",
    /// "running_cost_per_month_nok".
    /// </summary>
    public Godot.Collections.Dictionary PlanSiteBuild(string zoneId)
    {
        if (SimulationEngine?.State == null) return NotReady();
        var p = SimulationEngine.PlanSiteBuild(zoneId);
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.Verdict.ReasonKey != ExpansionReasons.ZoneNotFound,
            ["allowed"] = p.Verdict.Allowed,
            ["reason_key"] = p.Verdict.ReasonKey,
            ["label_key"] = ExpansionLabelKey(p.Verdict.ReasonKey),
            ["zone_id"] = p.ZoneId,
            ["label"] = p.Label,
            ["production_area"] = p.ProductionArea,
            ["new_zone"] = p.NewZone,
            ["cost_nok"] = (double)p.CostNok,
            ["balance_nok"] = (double)p.BalanceNok,
            ["locality_mtb_t"] = p.LocalityMtbTonnes,
            ["licence_mtb_added_t"] = p.LicenceMtbAddedTonnes,
            ["area_licence_mtb_t"] = p.AreaLicenceMtbTonnes,
            ["pens"] = p.Pens,
            ["pen_circumference_m"] = p.PenCircumferenceM,
            ["pen_net_depth_m"] = p.PenNetDepthM,
            ["pen_volume_m3"] = p.PenVolumeM3,
            ["max_fish_per_pen"] = p.MaxFishPerPen,
            ["workers"] = (double)p.Workers,
            ["running_cost_per_month_nok"] = (double)p.RunningCostPerMonthNok,
            ["player_sites_in_zone"] = p.PlayerSitesInZone,
        };
    }

    /// <summary>
    /// Build a sea site in a zone from the map (the BuildSiteDialog's confirm).
    /// Keys: "success", "reason_key", "label_key", "message", "site_id",
    /// "site_label", "zone_id".
    /// </summary>
    public Godot.Collections.Dictionary BuildExpansionSite(string zoneId)
    {
        if (SimulationEngine?.State == null) return NotReady();
        var outcome = SimulationEngine.BuildSeaSite(zoneId, "");
        var result = new Godot.Collections.Dictionary
        {
            ["success"] = outcome.Ok,
            ["reason_key"] = outcome.ErrorKey,
            ["label_key"] = ExpansionLabelKey(outcome.ErrorKey),
            ["message"] = outcome.Message,
            ["zone_id"] = zoneId,
            ["site_id"] = outcome.Ok ? outcome.Id : "",
            ["site_label"] = outcome.Ok
                ? SimulationEngine.State.SeaSites.FirstOrDefault(s => s.Id == outcome.Id)?.Label ?? outcome.Id
                : "",
        };
        if (outcome.Ok)
        {
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
            EmitSignal(SignalName.GameStateUpdated);
        }
        return result;
    }

    /// <summary>
    /// What applying for a growth licence in a zone opens. Keys: "success",
    /// "allowed", "reason_key", "label_key", "zone_id", "label",
    /// "production_area", "licence_mtb_t", "reserve_nok",
    /// "reserve_per_tonne_nok", "auction_length_days", "balance_nok",
    /// "traffic_light".
    /// </summary>
    public Godot.Collections.Dictionary PreviewLicenceApplication(string zoneId)
    {
        if (SimulationEngine?.State == null) return NotReady();
        var p = SimulationEngine.PreviewLicenceApplication(zoneId);
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.Verdict.ReasonKey != ExpansionReasons.ZoneNotFound,
            ["allowed"] = p.Verdict.Allowed,
            ["reason_key"] = p.Verdict.ReasonKey,
            ["label_key"] = ExpansionLabelKey(p.Verdict.ReasonKey),
            ["zone_id"] = p.ZoneId,
            ["label"] = p.Label,
            ["production_area"] = p.ProductionArea,
            ["licence_mtb_t"] = p.LicenceMtbTonnes,
            ["reserve_nok"] = (double)p.ReservePriceNok,
            ["reserve_per_tonne_nok"] = (double)p.ReservePerTonneNok,
            ["auction_length_days"] = p.AuctionLengthDays,
            ["balance_nok"] = (double)p.BalanceNok,
            ["traffic_light"] = p.TrafficLight,
            ["reference_2022_per_tonne_nok"] = (double)LakskongenSim.Finance.LicenseAuctionSystem.StateAuction2022AveragePerTonneNok,
            ["reference_2024_per_tonne_nok"] = (double)LakskongenSim.Finance.LicenseAuctionSystem.StateAuction2024AveragePerTonneNok,
        };
    }

    /// <summary>
    /// Open the growth-licence auction for a zone ("Søk vekstlisens"; opening
    /// is free). Keys: "success", "reason_key", "label_key", "message",
    /// "auction_id".
    /// </summary>
    public Godot.Collections.Dictionary OpenLicenceAuction(string zoneId)
    {
        if (SimulationEngine?.State == null) return NotReady();
        var outcome = SimulationEngine.ApplyForGrowthLicence(zoneId);
        if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
        return new Godot.Collections.Dictionary
        {
            ["success"] = outcome.Ok,
            ["reason_key"] = outcome.ErrorKey,
            ["label_key"] = ExpansionLabelKey(outcome.ErrorKey),
            ["message"] = outcome.Message,
            ["auction_id"] = outcome.Ok ? outcome.Id : "",
        };
    }

    /// <summary>
    /// Bid in a zone's running auction. Keys: "success", "reason_key",
    /// "label_key", "message", "auction" (the auction as GetLicenceAuction
    /// returns it after the bid).
    /// </summary>
    public Godot.Collections.Dictionary BidOnLicenceAuction(string zoneId, double amountNok)
    {
        if (SimulationEngine?.State == null) return NotReady();
        var outcome = SimulationEngine.BidOnLicenseAuction(zoneId, (decimal)amountNok);
        if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
        return new Godot.Collections.Dictionary
        {
            ["success"] = outcome.Ok,
            ["reason_key"] = outcome.ErrorKey,
            ["label_key"] = ExpansionLabelKey(outcome.ErrorKey),
            ["message"] = outcome.Message,
            ["auction"] = GetLicenceAuction(zoneId),
        };
    }

    /// <summary>
    /// A zone's auction for the auction card: the running one, else the
    /// latest. Keys: "success" (false when the zone never had one), "id",
    /// "zone_id", "label", "production_area", "licence_mtb_t", "opens_day",
    /// "closes_day", "days_left", "status", "reserve_nok", "current_bid_nok",
    /// "min_bid_nok", "current_per_tonne_nok", "min_bid_per_tonne_nok",
    /// "leader_kind", "leader_id", "leader_name", "player_entered",
    /// "player_leads", "player_best_bid_nok", "rival_bids", "consumed",
    /// "balance_nok" and "bids" (newest first: day, bidder_kind, bidder_id,
    /// bidder_name, amount_nok, per_tonne_nok).
    /// </summary>
    public Godot.Collections.Dictionary GetLicenceAuction(string zoneId)
    {
        var view = SimulationEngine?.GetLicenceAuctionView(zoneId);
        if (view == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["zone_id"] = zoneId };
        var bids = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var b in view.Bids)
            bids.Add(new Godot.Collections.Dictionary
            {
                ["day"] = b.Day,
                ["bidder_kind"] = b.BidderKind,
                ["bidder_id"] = b.BidderId,
                ["bidder_name"] = b.BidderName,
                ["amount_nok"] = (double)b.AmountNok,
                ["per_tonne_nok"] = (double)b.PerTonneNok,
            });
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = view.Id,
            ["zone_id"] = view.ZoneId,
            ["label"] = view.Label,
            ["production_area"] = view.ProductionArea,
            ["licence_mtb_t"] = view.LicenceMtbTonnes,
            ["opens_day"] = view.OpensDay,
            ["closes_day"] = view.ClosesDay,
            ["days_left"] = view.DaysLeft,
            ["status"] = view.Status,
            ["reserve_nok"] = (double)view.ReserveNok,
            ["current_bid_nok"] = (double)view.CurrentBidNok,
            ["min_bid_nok"] = (double)view.MinBidNok,
            ["current_per_tonne_nok"] = (double)view.CurrentPerTonneNok,
            ["min_bid_per_tonne_nok"] = (double)view.MinBidPerTonneNok,
            ["leader_kind"] = view.LeaderKind,
            ["leader_id"] = view.LeaderId,
            ["leader_name"] = view.LeaderName,
            ["player_entered"] = view.PlayerEntered,
            ["player_leads"] = view.PlayerLeads,
            ["player_best_bid_nok"] = (double)view.PlayerBestBidNok,
            ["rival_bids"] = view.RivalBids,
            ["consumed"] = view.Consumed,
            ["balance_nok"] = (double)view.BalanceNok,
            ["today"] = SimulationEngine?.State?.Day ?? 0,
            ["reference_2022_per_tonne_nok"] = (double)LakskongenSim.Finance.LicenseAuctionSystem.StateAuction2022AveragePerTonneNok,
            ["reference_2024_per_tonne_nok"] = (double)LakskongenSim.Finance.LicenseAuctionSystem.StateAuction2024AveragePerTonneNok,
            ["bids"] = bids,
        };
    }

    /// <summary>
    /// The inbox's auction items (U2-01): every running auction the company is
    /// in (outbid first) and every won licence not yet used for a site.
    /// </summary>
    private void AppendExpansionAttentionItems(Godot.Collections.Array<Godot.Collections.Dictionary> result)
    {
        var engine = SimulationEngine;
        if (engine?.State?.LicenseAuctions == null) return;
        foreach (var auction in engine.State.LicenseAuctions)
        {
            var view = engine.GetLicenceAuctionView(auction.ZoneId);
            if (view == null || view.Id != auction.Id) continue;
            if (auction.Status == "open" && auction.PlayerEntered)
            {
                bool outbid = auction.LeaderKind == "rival";
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = outbid ? "licence_outbid" : "licence_auction",
                    ["priority"] = outbid ? "high" : "medium",
                    ["category"] = "expansion",
                    ["title"] = string.Format(UiCulture, _tr_or(outbid ? "UI_ATTENTION_AUCTION_OUTBID" : "UI_ATTENTION_AUCTION_RUNNING",
                        outbid ? "Outbid in the licence auction for {0}" : "Licence auction for {0}"), view.Label),
                    ["description"] = outbid
                        ? string.Format(UiCulture, _tr_or("UI_ATTENTION_AUCTION_OUTBID_DESC", "{0} leads with {1:N0} NOK. Minimum bid {2:N0} NOK."),
                            view.LeaderName, view.CurrentBidNok, view.MinBidNok)
                        : view.CurrentBidNok > 0m
                            ? string.Format(UiCulture, _tr_or("UI_ATTENTION_AUCTION_RUNNING_DESC", "Leading bid {0:N0} NOK. Minimum bid {1:N0} NOK."),
                                view.CurrentBidNok, view.MinBidNok)
                            : string.Format(UiCulture, _tr_or("UI_ATTENTION_AUCTION_NO_BIDS_DESC", "No bids yet. Reserve {0:N0} NOK."),
                                view.ReserveNok),
                    ["days_out"] = view.DaysLeft,
                    ["zone_id"] = auction.ZoneId,
                    ["callback"] = "open_licence_auction",
                });
            }
            else if (auction.Status == "won_player" && !auction.ConsumedByBuild)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "licence_ready",
                    ["priority"] = "medium",
                    ["category"] = "expansion",
                    ["title"] = string.Format(UiCulture, _tr_or("UI_ATTENTION_LICENCE_READY", "Growth licence ready in {0}"), view.Label),
                    ["description"] = string.Format(UiCulture, _tr_or("UI_ATTENTION_LICENCE_READY_DESC",
                        "Its {0:N0} t of MTB already counts in the production area. Build a locality in {1} to farm there."),
                        view.LicenceMtbTonnes, view.Label),
                    ["zone_id"] = auction.ZoneId,
                    ["callback"] = "open_build_site",
                });
            }
        }
    }
}
