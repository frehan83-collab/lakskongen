using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Integration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
// Namespace aliases for the Phase 5/6 features (sludge, carbon, cohort transfers,
// scenarios) so qualified references like Farm.SludgeValorization resolve from
// this global-namespace file.
using Farm = LakskongenSim.Farm;
using Events = LakskongenSim.Events;
using Processing = LakskongenSim.Processing;
using Logistics = LakskongenSim.Logistics;

// GameBridge.Market.cs — partial class slice (see project docs).
// <summary>
// Market slice: contract and offer management, export markets, stock-market
// status/IPO, market-intelligence reporting, competitor analysis, price
// forecasts, and dashboard market-price projection.
// </summary>
/// <summary>
/// GameBridge partial — contract and offer management, export markets, stock-market (moved verbatim from GameBridge.cs).
/// </summary>
public partial class GameBridge : Node
{
    // ── Contract & Market API ──────────────────────────────────────────────────

    /// <summary>Offer cache: real ContractSystem offers for the current day.</summary>
    private List<LakskongenSim.Market.ContractOffer> _offerCache = new();
    private int _offerCacheDay = -1;

    private void RefreshOfferCache()
    {
        var cs = SimulationEngine?.ContractSystem;
        if (cs == null) return;
        int day = SimulationEngine?.State?.Day ?? 0;
        if (day == _offerCacheDay) return;
        _offerCacheDay = day;
        decimal spot = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        double brand = (double)(SimulationEngine?.BrandReputation?.QualityReputation ?? 50m) / 100.0;
        _offerCache = cs.GenerateOffers(spot, brand);
    }

    /// <summary>
    /// Returns list of active contracts.
    /// Each entry is a Dictionary with keys: "id", "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct", "fulfilled_kg", "fulfilled_percent".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveContracts()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var contractSystem = SimulationEngine?.ContractSystem;
        if (contractSystem == null) return result;

        foreach (var contract in contractSystem.ActiveContracts)
        {
            float fulfilledPercent = contract.QuantityKg > 0 ? (float)contract.FulfilledKg / contract.QuantityKg * 100f : 0f;
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = contract.Id.ToString(),
                ["buyer"] = contract.Buyer,
                ["type"] = contract.Type.ToString(),
                ["price_per_kg"] = (float)contract.PricePerKg,
                ["quantity_kg"] = (float)contract.QuantityKg,
                ["quality"] = contract.Quality.ToString(),
                ["penalty_pct"] = (float)contract.PenaltyPct,
                ["fulfilled_kg"] = (float)contract.FulfilledKg,
                ["fulfilled_percent"] = fulfilledPercent
            });
        }
        return result;
    }

    /// <summary>
    /// Returns list of available contract offers (generated from market conditions).
    /// Each entry is a Dictionary with keys: "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableOffers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var contractSystem = SimulationEngine?.ContractSystem;
        if (contractSystem == null) return result;

        // Real offers from ContractSystem, cached per day (stable within a day).
        RefreshOfferCache();
        for (int i = 0; i < _offerCache.Count; i++)
        {
            var o = _offerCache[i];
            result.Add(new Godot.Collections.Dictionary
            {
                ["index"] = i,
                ["buyer"] = o.Buyer,
                ["type"] = o.Type.ToString(),
                ["price_per_kg"] = (double)o.PricePerKg,
                ["quantity_kg"] = (double)o.QuantityKg,
                ["quality"] = o.Quality.ToString(),
                ["penalty_pct"] = (double)o.PenaltyPct,
                ["duration_weeks"] = o.DurationWeeks
            });
        }
        return result;
    }

    /// <summary>
    /// Accept a cached offer by index (Phase 2.3). Returns the contract ID
    /// string on success, empty string on failure.
    /// </summary>
    public string AcceptOffer(int index)
    {
        var cs = SimulationEngine?.ContractSystem;
        if (cs == null || index < 0 || index >= _offerCache.Count) return "";
        var offer = _offerCache[index];
        var contract = cs.AcceptOffer(offer);
        _offerCache.RemoveAt(index);
        SimulationEngine?.State?.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine?.State?.Day ?? 0,
            Name = "Kontrakt signert",
            Category = LakskongenSim.Events.EventCategory.Market,
            Description = $"Kontrakt med {contract.Buyer}: {contract.QuantityKg:N0} kg @ {contract.PricePerKg:N0} NOK/kg ({contract.DurationWeeks} uker)",
            BalanceChange = 0m
        });
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Contract signed — {contract.Buyer} {contract.QuantityKg:N0}kg @ {contract.PricePerKg:N0} NOK/kg");
        return contract.Id.ToString();
    }

    /// <summary>
    /// Returns list of export markets with status and prices.
    /// Each entry is a Dictionary with keys: "region", "name", "base_price_nok", "premium_multiplier", "player_has_access", "logistics_cost_pct", "tariff_rate".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetExportMarkets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketSystem = SimulationEngine?.InternationalMarketSystem;
        if (marketSystem == null) return result;

        var allMarkets = marketSystem.GetAllMarkets();
        foreach (var market in allMarkets.Values)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["region"] = market.Region.ToString(),
                ["name"] = market.Name,
                ["base_price_nok"] = (float)market.BasePriceNOK,
                ["premium_multiplier"] = (float)market.PremiumMultiplier,
                ["player_has_access"] = market.PlayerHasAccess,
                ["logistics_cost_pct"] = (float)market.LogisticsCostPctOfPrice,
                ["tariff_rate"] = (float)market.TariffRate,
                ["min_reputation_score"] = (float)market.MinReputationScore,
                ["required_certifications"] = new Godot.Collections.Array<string>(market.RequiredCertifications.ToArray())
            });
        }
        return result;
    }

    // ── StockMarket Methods (stubbed) ──────────────────────────────────────────

    public string GetOwnershipStatus() => "Private";
    public double GetSharePrice() => 0.0;
    public double GetMarketCap() => 0.0;
    public bool IsPublic() => false;

    public Godot.Collections.Dictionary AttemptIPO()
    {
        return new Godot.Collections.Dictionary
        {
            ["success"] = false,
            ["message"] = "IPO system not yet implemented"
        };
    }

    // ── MarketIntelligence Methods ────────────────────────────────────────────────

    /// <summary>
    /// Returns comprehensive market report as a Dictionary.
    /// Keys: "current_price", "weekly_avg", "trend", "active_events", "competitor_supply_tonnes",
    ///       "player_reputation_score", "forecasts" (Array).
    /// </summary>
    public Godot.Collections.Dictionary GetMarketReport()
    {
        var result = new Godot.Collections.Dictionary();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null)
        {
            result["current_price"] = 70.0;
            result["weekly_avg"] = 70.0;
            result["trend"] = "stable";
            result["active_events"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["competitor_supply_tonnes"] = 0.0;
            result["player_reputation_score"] = 25.0;
            result["forecasts"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return result;
        }

        var currentPrice = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        result["current_price"] = (double)currentPrice;
        result["weekly_avg"] = (double)(SimulationEngine?.MarketSimulation?.WeeklyAvgPrice ?? 70m);
        
        // Get active events
        var events = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var evt in marketIntel.ActiveEvents)
        {
            events.Add(new Godot.Collections.Dictionary
            {
                ["headline"] = evt.Headline,
                ["description"] = evt.Description,
                ["category"] = evt.Category.ToString(),
                ["remaining_weeks"] = evt.RemainingWeeks,
                ["supply_impact"] = evt.SupplyImpact,
                ["demand_impact"] = evt.DemandImpact
            });
        }
        result["active_events"] = events;
        
        result["competitor_supply_tonnes"] = marketIntel.GetTotalCompetitorSupplyTonnes();
        result["player_reputation_score"] = marketIntel.PlayerReputation.Score;
        
        // Get price forecasts
        var forecasts = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var forecastList = marketIntel.GetForecasts(currentPrice, 70m, 5m);
        foreach (var f in forecastList)
        {
            forecasts.Add(new Godot.Collections.Dictionary
            {
                ["weeks_ahead"] = f.WeeksAhead,
                ["predicted_price"] = (double)f.PredictedPrice,
                ["lower_bound"] = (double)f.LowerBound,
                ["upper_bound"] = (double)f.UpperBound,
                ["confidence"] = f.Confidence,
                ["trend"] = f.Trend.ToString()
            });
        }
        result["forecasts"] = forecasts;

        return result;
    }

    /// <summary>
    /// Returns competitor analysis as an Array of Dictionaries.
    /// Keys: "name", "country", "weekly_tonnes", "market_rank".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCompetitorAnalysis()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null) return result;

        var snapshot = marketIntel.GetCompetitorSnapshot();
        int rank = 1;
        foreach (var (name, country, weeklyTonnes) in snapshot)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["name"] = name,
                ["country"] = country,
                ["weekly_tonnes"] = weeklyTonnes,
                ["market_rank"] = rank++
            });
        }

        return result;
    }

    /// <summary>
    /// Returns price forecast for next 4 weeks as an Array of Dictionaries.
    /// Keys: "weeks_ahead", "predicted_price", "lower_bound", "upper_bound", "confidence", "trend".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPriceForecast()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null) return result;

        var currentPrice = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        var forecasts = marketIntel.GetForecasts(currentPrice, 70m, 5m);
        
        foreach (var f in forecasts)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["weeks_ahead"] = f.WeeksAhead,
                ["predicted_price"] = (double)f.PredictedPrice,
                ["lower_bound"] = (double)f.LowerBound,
                ["upper_bound"] = (double)f.UpperBound,
                ["confidence"] = f.Confidence,
                ["trend"] = f.Trend.ToString()
            });
        }

        return result;
    }

    /// <summary>
    /// Returns market price chart data (current + 30-day history) as a Godot Array of Dictionaries.
    /// Each entry: "day", "price_per_kg", "weekly_avg", "volume_tonnes".
    /// </summary>
    public Godot.Collections.Array GetMarketPrices()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var prices = _dashboardDataProvider.GetMarketPrices();
        var result = new Godot.Collections.Array();

        foreach (var price in prices)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in price)
            {
                dict[kvp.Key] = Variant.From(kvp.Value);
            }
            result.Add(dict);
        }

        return result;
    }

}
