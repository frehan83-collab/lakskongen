using Godot;
using LakskongenSim.Core;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Singleton C# autoload — bridges MultiplayerManager to Godot.
/// Exposes lobby state, player roster, trade offers, and leaderboard
/// as GDScript-friendly properties and signals.
/// </summary>
public partial class MultiplayerBridge : Node
{
    // ── Signals ──────────────────────────────────────────────────────────────

    [Signal] public delegate void PlayerJoinedEventHandler(string playerId, string playerName, string companyName, string color, bool isHost);
    [Signal] public delegate void PlayerLeftEventHandler(string playerId);
    [Signal] public delegate void PlayerReadyChangedEventHandler(string playerId, bool ready);
    [Signal] public delegate void AllPlayersReadyEventHandler();
    [Signal] public delegate void GameStartedEventHandler();
    [Signal] public delegate void GameEndedEventHandler();
    [Signal] public delegate void TradeOfferedEventHandler(string offerId, string fromPlayerId, string toPlayerId, string itemType, float quantity, decimal priceNok);
    [Signal] public delegate void TradeResolvedEventHandler(string offerId, string status);
    [Signal] public delegate void ChatMessageReceivedEventHandler(string playerId, string playerName, string text);
    [Signal] public delegate void LeaderboardUpdatedEventHandler(Godot.Collections.Array<Godot.Collections.Dictionary> entries);
    [Signal] public delegate void TurnChangedEventHandler(string playerId);

    // ── Manager reference ────────────────────────────────────────────────────

    public MultiplayerManager MultiplayerManager { get; private set; }

    // ── Godot lifecycle ──────────────────────────────────────────────────────

    public override void _Ready()
    {
        MultiplayerManager = new MultiplayerManager();
        WireEvents();
        GD.Print("MultiplayerBridge: initialized");
    }

    private void WireEvents()
    {
        MultiplayerManager.OnPlayerJoined += profile =>
            EmitSignal(SignalName.PlayerJoined, profile.Id, profile.Name, profile.CompanyName, profile.Color, profile.IsHost);
        MultiplayerManager.OnPlayerLeft += playerId =>
            EmitSignal(SignalName.PlayerLeft, playerId);
        MultiplayerManager.OnPlayerReady += profile =>
            EmitSignal(SignalName.PlayerReadyChanged, profile.Id, profile.IsReady);
        MultiplayerManager.OnAllPlayersReady += () =>
            EmitSignal(SignalName.AllPlayersReady);
        MultiplayerManager.OnGameStarted += () =>
            EmitSignal(SignalName.GameStarted);
        MultiplayerManager.OnGameFinished += () =>
            EmitSignal(SignalName.GameEnded);
        MultiplayerManager.OnTradeOffered += offer =>
            EmitSignal(SignalName.TradeOffered, offer.OfferId, offer.FromPlayerId, offer.ToPlayerId, offer.ItemType.ToString(), offer.Quantity, offer.PriceNok);
        MultiplayerManager.OnTradeResolved += offer =>
            EmitSignal(SignalName.TradeResolved, offer.OfferId, offer.Status.ToString());
        MultiplayerManager.OnChatMessageReceived += msg =>
            EmitSignal(SignalName.ChatMessageReceived, msg.PlayerId, msg.PlayerName, msg.Text);
        MultiplayerManager.OnLeaderboardUpdated += entries =>
        {
            var godotEntries = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var e in entries)
            {
                var dict = new Godot.Collections.Dictionary
                {
                    ["rank"] = e.Rank,
                    ["player_id"] = e.PlayerId,
                    ["player_name"] = e.PlayerName,
                    ["company_name"] = e.CompanyName,
                    ["color"] = e.Color,
                    ["profit"] = (double)e.Profit,
                    ["sustainability_score"] = (double)e.SustainabilityScore,
                    ["welfare_score"] = (double)e.WelfareScore,
                    ["composite_score"] = e.CompositeScore
                };
                godotEntries.Add(dict);
            }
            EmitSignal(SignalName.LeaderboardUpdated, godotEntries);
        };
        MultiplayerManager.OnTurnChanged += playerId =>
            EmitSignal(SignalName.TurnChanged, playerId);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Add a player to the lobby (host or remote).</summary>
    public bool AddPlayer(string name, string companyName, string color, decimal startingBalance = -1m)
    {
        return MultiplayerManager.AddPlayer(name, companyName, color, startingBalance);
    }

    /// <summary>Remove a player (disconnect).</summary>
    public void RemovePlayer(string playerId)
    {
        MultiplayerManager.RemovePlayer(playerId);
    }

    /// <summary>Set a player's ready status.</summary>
    public void SetPlayerReady(string playerId, bool ready)
    {
        MultiplayerManager.SetPlayerReady(playerId, ready);
    }

    /// <summary>Host starts the game (requires all players ready).</summary>
    public bool StartGame()
    {
        return MultiplayerManager.StartGame();
    }

    /// <summary>End the current session.</summary>
    public void EndGame()
    {
        MultiplayerManager.EndGame();
    }

    /// <summary>Advance simulation day (call from game loop).</summary>
    public void AdvanceDay()
    {
        MultiplayerManager.AdvanceDay();
    }

    /// <summary>Update player's live stats (called from SimBridge).</summary>
    public void UpdatePlayerStats(string playerId, decimal balance, int fishCount,
                                  decimal cumulativeProfit,
                                  float sustainabilityScore, float welfareScore)
    {
        MultiplayerManager.UpdatePlayerStats(playerId, balance, fishCount,
            cumulativeProfit, sustainabilityScore, welfareScore);
    }

    /// <summary>Create a trade offer between players.</summary>
    public string CreateTradeOffer(string fromPlayerId, string toPlayerId,
                                   string itemType, float quantity,
                                   decimal priceNok, string itemId = null)
    {
        var type = Enum.Parse<TradeItemType>(itemType);
        var offer = MultiplayerManager.CreateTradeOffer(fromPlayerId, toPlayerId,
            type, quantity, priceNok, itemId);
        return offer?.OfferId ?? "";
    }

    /// <summary>Respond to a pending trade offer.</summary>
    public bool RespondToTradeOffer(string offerId, bool accept)
    {
        return MultiplayerManager.RespondToTradeOffer(offerId, accept);
    }

    /// <summary>Send a chat message.</summary>
    public void SendChatMessage(string playerId, string text)
    {
        MultiplayerManager.SendChatMessage(playerId, text);
    }

    /// <summary>Get current leaderboard entries.</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLeaderboard()
    {
        var entries = MultiplayerManager.RefreshLeaderboard();
        var godotEntries = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var e in entries)
        {
            var dict = new Godot.Collections.Dictionary
            {
                ["rank"] = e.Rank,
                ["player_id"] = e.PlayerId,
                ["player_name"] = e.PlayerName,
                ["company_name"] = e.CompanyName,
                ["color"] = e.Color,
                ["profit"] = (double)e.Profit,
                ["sustainability_score"] = (double)e.SustainabilityScore,
                ["welfare_score"] = (double)e.WelfareScore,
                ["composite_score"] = e.CompositeScore
            };
            godotEntries.Add(dict);
        }
        return godotEntries;
    }

    /// <summary>Get player profile by id.</summary>
    public Godot.Collections.Dictionary GetPlayerProfile(string playerId)
    {
        if (MultiplayerManager.Players.TryGetValue(playerId, out var profile))
        {
            return new Godot.Collections.Dictionary
            {
                ["id"] = profile.Id,
                ["name"] = profile.Name,
                ["company_name"] = profile.CompanyName,
                ["color"] = profile.Color,
                ["balance"] = (double)profile.Balance,
                ["fish_count"] = profile.FishCount,
                ["is_ready"] = profile.IsReady,
                ["is_host"] = profile.IsHost,
                ["is_connected"] = profile.IsConnected,
                ["cumulative_profit"] = (double)profile.CumulativeProfit,
                ["sustainability_score"] = (double)profile.SustainabilityScore,
                ["welfare_score"] = (double)profile.WelfareScore
            };
        }
        return new Godot.Collections.Dictionary();
    }

    /// <summary>Get all player IDs.</summary>
    public Godot.Collections.Array<string> GetPlayerIds()
    {
        var ids = new Godot.Collections.Array<string>();
        foreach (var kv in MultiplayerManager.Players)
            ids.Add(kv.Key);
        return ids;
    }

    /// <summary>Current lobby state as string.</summary>
    public string LobbyState => MultiplayerManager.LobbyState.ToString();

    /// <summary>Current turn player id (for turn-based mode).</summary>
    public string CurrentTurnPlayerId => MultiplayerManager.CurrentTurnPlayerId ?? "";
}