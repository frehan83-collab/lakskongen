using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LakskongenSim.Market;

namespace LakskongenSim.Core
{
    // ── Enumerations ──────────────────────────────────────────────────────────

    public enum GameMode
    {
        TurnBased,
        Simultaneous
    }

    public enum LobbyState
    {
        Waiting,
        Playing,
        Finished
    }

    public enum TradeStatus
    {
        Pending,
        Accepted,
        Rejected,
        Expired
    }

    public enum TradeItemType
    {
        Fish,       // live smolt / harvest fish (tonnes)
        Contract,   // supply contract (id reference)
        License     // operating license (id reference)
    }

    // ── Player profile ────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of a single player's state in a multiplayer session.
    /// JSON-serializable so it can be broadcast over the network layer.
    /// </summary>
    public class PlayerProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Player";
        public string CompanyName { get; set; } = "Unnamed AS";

        /// <summary>Hex color string, e.g. "#E87B3A".</summary>
        public string Color { get; set; } = "#AAAAAA";

        public decimal Balance { get; set; }
        public int FishCount { get; set; }
        public bool IsReady { get; set; }
        public bool IsHost { get; set; }
        public bool IsConnected { get; set; } = true;

        // ── Scoring ───────────────────────────────────────────────────────────

        /// <summary>Cumulative net profit since session start (NOK).</summary>
        public decimal CumulativeProfit { get; set; }

        /// <summary>Sustainability score 0–100 (mirrors SustainabilityManager).</summary>
        public float SustainabilityScore { get; set; }

        /// <summary>Fish welfare index 0–100.</summary>
        public float WelfareScore { get; set; }

        /// <summary>Composite rank score used for leaderboard sorting.</summary>
        [JsonIgnore]
        public double CompositeScore =>
            (double)CumulativeProfit / 1_000_000.0   // profit weight
            + SustainabilityScore * 500.0              // sustainability weight
            + WelfareScore * 300.0;                    // welfare weight
    }

    // ── Trade system ─────────────────────────────────────────────────────────

    /// <summary>
    /// A bilateral trade offer between two players.
    /// Supports fish (tonnes), contracts, and licenses.
    /// </summary>
    public class TradeOffer
    {
        public string OfferId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string FromPlayerId { get; set; } = "";
        public string ToPlayerId { get; set; } = "";

        public TradeItemType ItemType { get; set; }

        /// <summary>
        /// Quantity (tonnes for fish; 1 for contract/license).
        /// </summary>
        public float Quantity { get; set; }

        /// <summary>Reference ID for contract or license items.</summary>
        public string? ItemId { get; set; }

        /// <summary>Agreed price in NOK.</summary>
        public decimal PriceNok { get; set; }

        public TradeStatus Status { get; set; } = TradeStatus.Pending;

        /// <summary>Day the offer was created (for expiry logic).</summary>
        public int CreatedOnDay { get; set; }

        /// <summary>Offer expires if not accepted within this many days.</summary>
        public int ExpiryDays { get; set; } = 3;
    }

    // ── Leaderboard ──────────────────────────────────────────────────────────

    public class LeaderboardEntry
    {
        public int Rank { get; set; }
        public string PlayerId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Color { get; set; } = "";
        public decimal Profit { get; set; }
        public float SustainabilityScore { get; set; }
        public float WelfareScore { get; set; }
        public double CompositeScore { get; set; }
    }

    // ── Session settings ─────────────────────────────────────────────────────

    public class MultiplayerSettings
    {
        public string MapName { get; set; } = "Norwegian Coast";
        public string Difficulty { get; set; } = "Normal";
        public decimal StartingCapital { get; set; } = 10_000_000m;
        public GameMode Mode { get; set; } = GameMode.Simultaneous;

        /// <summary>Max players (2–8).</summary>
        public int MaxPlayers { get; set; } = 4;

        /// <summary>Session length in simulated days (0 = endless).</summary>
        public int SessionLengthDays { get; set; } = 365;
    }

    // ── Chat ─────────────────────────────────────────────────────────────────

    public class ChatMessage
    {
        public string PlayerId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // ── Main manager ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pure C# multiplayer session manager — no Godot dependencies.
    /// Manages the player roster, game mode, shared market access,
    /// competitive scoring, trade offers, lobby state, and chat.
    ///
    /// The Godot layer (MultiplayerLobby.gd / MultiplayerHUD.gd) drives this
    /// manager via method calls and subscribes to its events for UI updates.
    /// Actual network transport (ENet / WebSocket / relay) is handled outside
    /// this class; it only models the session logic.
    /// </summary>
    public class MultiplayerManager
    {
        // ── Session state ──────────────────────────────────────────────────────

        public string SessionId { get; } = Guid.NewGuid().ToString("N")[..16];
        public LobbyState LobbyState { get; private set; } = LobbyState.Waiting;
        public MultiplayerSettings Settings { get; } = new();
        public GameMode Mode => Settings.Mode;

        // ── Player roster ──────────────────────────────────────────────────────

        private readonly Dictionary<string, PlayerProfile> _players = new();
        public IReadOnlyDictionary<string, PlayerProfile> Players => _players;

        // ── Shared market ─────────────────────────────────────────────────────

        /// <summary>
        /// Shared market instance — all players read from the same price curve
        /// so competition is fair.  Set by the host before play begins.
        /// </summary>
        public MarketSimulation? SharedMarket { get; set; }

        // ── Trade offers ──────────────────────────────────────────────────────

        private readonly List<TradeOffer> _tradeOffers = new();
        public IReadOnlyList<TradeOffer> TradeOffers => _tradeOffers;

        // ── Chat history ──────────────────────────────────────────────────────

        private readonly List<ChatMessage> _chatHistory = new();
        public IReadOnlyList<ChatMessage> ChatHistory => _chatHistory;

        // ── Turn tracking (TurnBased mode) ────────────────────────────────────

        private int _turnPlayerIndex = 0;
        private int _currentDay = 0;

        // ── Events ────────────────────────────────────────────────────────────

        public event Action<PlayerProfile>? OnPlayerJoined;
        public event Action<string>? OnPlayerLeft;          // playerId
        public event Action<PlayerProfile>? OnPlayerReady;
        public event Action? OnAllPlayersReady;
        public event Action? OnGameStarted;
        public event Action? OnGameFinished;
        public event Action<TradeOffer>? OnTradeOffered;
        public event Action<TradeOffer>? OnTradeResolved;
        public event Action<ChatMessage>? OnChatMessageReceived;
        public event Action<List<LeaderboardEntry>>? OnLeaderboardUpdated;
        public event Action<string>? OnTurnChanged;         // playerId whose turn it is

        // ── Player management ─────────────────────────────────────────────────

        /// <summary>
        /// Add a player to the lobby. First player is automatically the host.
        /// Returns false if the lobby is full or the session has already started.
        /// </summary>
        public bool AddPlayer(string name, string companyName, string color,
                              decimal startingBalance = -1m)
        {
            if (LobbyState != LobbyState.Waiting) return false;
            if (_players.Count >= Settings.MaxPlayers) return false;

            var balance = startingBalance < 0 ? Settings.StartingCapital : startingBalance;
            var profile = new PlayerProfile
            {
                Name        = name,
                CompanyName = companyName,
                Color       = color,
                Balance     = balance,
                IsHost      = _players.Count == 0
            };

            _players[profile.Id] = profile;
            OnPlayerJoined?.Invoke(profile);
            return true;
        }

        /// <summary>Remove a player by id (disconnect / leave lobby).</summary>
        public void RemovePlayer(string playerId)
        {
            if (!_players.ContainsKey(playerId)) return;
            _players.Remove(playerId);
            OnPlayerLeft?.Invoke(playerId);
        }

        /// <summary>Toggle a player's ready status.</summary>
        public void SetPlayerReady(string playerId, bool ready)
        {
            if (!_players.TryGetValue(playerId, out var p)) return;
            p.IsReady = ready;
            OnPlayerReady?.Invoke(p);

            if (_players.Count >= 2 && _players.Values.All(x => x.IsReady))
                OnAllPlayersReady?.Invoke();
        }

        // ── Session lifecycle ─────────────────────────────────────────────────

        /// <summary>
        /// Host transitions the session from Waiting → Playing.
        /// Returns false if conditions are not met.
        /// </summary>
        public bool StartGame()
        {
            if (LobbyState != LobbyState.Waiting) return false;
            if (_players.Count < 2) return false;
            if (!_players.Values.All(p => p.IsReady)) return false;

            LobbyState = LobbyState.Playing;
            _turnPlayerIndex = 0;
            _currentDay = 0;
            OnGameStarted?.Invoke();
            return true;
        }

        /// <summary>Host or timer ends the session → Playing → Finished.</summary>
        public void EndGame()
        {
            if (LobbyState == LobbyState.Finished) return;
            LobbyState = LobbyState.Finished;
            RefreshLeaderboard();
            OnGameFinished?.Invoke();
        }

        // ── Day / turn advancement ────────────────────────────────────────────

        /// <summary>
        /// Advance simulation day.  In Simultaneous mode all players advance
        /// together.  In TurnBased mode only the active player's engine ticks,
        /// then control passes to the next player.
        /// </summary>
        public void AdvanceDay()
        {
            if (LobbyState != LobbyState.Playing) return;

            _currentDay++;
            ExpireOldTradeOffers(_currentDay);

            if (Settings.SessionLengthDays > 0 && _currentDay >= Settings.SessionLengthDays)
            {
                EndGame();
                return;
            }

            if (Mode == GameMode.TurnBased)
            {
                _turnPlayerIndex = (_turnPlayerIndex + 1) % _players.Count;
                var nextId = _players.Keys.ElementAt(_turnPlayerIndex);
                OnTurnChanged?.Invoke(nextId);
            }

            RefreshLeaderboard();
        }

        /// <summary>Returns the id of the player whose turn it is (TurnBased).</summary>
        public string? CurrentTurnPlayerId =>
            Mode == GameMode.TurnBased && _players.Count > 0
                ? _players.Keys.ElementAt(_turnPlayerIndex % _players.Count)
                : null;

        // ── Player stat updates ───────────────────────────────────────────────

        /// <summary>
        /// Update a player's live stats (called every sim tick from the Godot bridge).
        /// </summary>
        public void UpdatePlayerStats(string playerId, decimal balance, int fishCount,
                                      decimal cumulativeProfit,
                                      float sustainabilityScore, float welfareScore)
        {
            if (!_players.TryGetValue(playerId, out var p)) return;
            p.Balance            = balance;
            p.FishCount          = fishCount;
            p.CumulativeProfit   = cumulativeProfit;
            p.SustainabilityScore = sustainabilityScore;
            p.WelfareScore       = welfareScore;
        }

        // ── Leaderboard ───────────────────────────────────────────────────────

        /// <summary>Rebuild and broadcast the sorted leaderboard.</summary>
        public List<LeaderboardEntry> RefreshLeaderboard()
        {
            var sorted = _players.Values
                .OrderByDescending(p => p.CompositeScore)
                .ToList();

            var board = sorted.Select((p, i) => new LeaderboardEntry
            {
                Rank              = i + 1,
                PlayerId          = p.Id,
                PlayerName        = p.Name,
                CompanyName       = p.CompanyName,
                Color             = p.Color,
                Profit            = p.CumulativeProfit,
                SustainabilityScore = p.SustainabilityScore,
                WelfareScore      = p.WelfareScore,
                CompositeScore    = p.CompositeScore
            }).ToList();

            OnLeaderboardUpdated?.Invoke(board);
            return board;
        }

        // ── Trade system ──────────────────────────────────────────────────────

        /// <summary>
        /// Initiate a trade offer from one player to another.
        /// Returns null if either player is not in the session.
        /// </summary>
        public TradeOffer? CreateTradeOffer(string fromPlayerId, string toPlayerId,
                                            TradeItemType itemType, float quantity,
                                            decimal priceNok, string? itemId = null)
        {
            if (!_players.ContainsKey(fromPlayerId)) return null;
            if (!_players.ContainsKey(toPlayerId)) return null;
            if (LobbyState != LobbyState.Playing) return null;

            var offer = new TradeOffer
            {
                FromPlayerId = fromPlayerId,
                ToPlayerId   = toPlayerId,
                ItemType     = itemType,
                Quantity     = quantity,
                PriceNok     = priceNok,
                ItemId       = itemId,
                CreatedOnDay = _currentDay
            };

            _tradeOffers.Add(offer);
            OnTradeOffered?.Invoke(offer);
            return offer;
        }

        /// <summary>
        /// Respond to a pending trade offer.
        /// If accepted, transfers balance between players (fish/license transfer
        /// must be completed by the calling layer).
        /// </summary>
        public bool RespondToTradeOffer(string offerId, bool accept)
        {
            var offer = _tradeOffers.FirstOrDefault(o => o.OfferId == offerId);
            if (offer == null || offer.Status != TradeStatus.Pending) return false;

            offer.Status = accept ? TradeStatus.Accepted : TradeStatus.Rejected;

            if (accept)
            {
                if (_players.TryGetValue(offer.FromPlayerId, out var seller))
                    seller.Balance += offer.PriceNok;
                if (_players.TryGetValue(offer.ToPlayerId, out var buyer))
                    buyer.Balance  -= offer.PriceNok;
            }

            OnTradeResolved?.Invoke(offer);
            return true;
        }

        private void ExpireOldTradeOffers(int currentDay)
        {
            foreach (var offer in _tradeOffers.Where(o =>
                         o.Status == TradeStatus.Pending &&
                         currentDay - o.CreatedOnDay >= o.ExpiryDays))
            {
                offer.Status = TradeStatus.Expired;
                OnTradeResolved?.Invoke(offer);
            }
        }

        // ── Chat ──────────────────────────────────────────────────────────────

        public void SendChatMessage(string playerId, string text)
        {
            if (!_players.TryGetValue(playerId, out var p)) return;
            var msg = new ChatMessage
            {
                PlayerId   = playerId,
                PlayerName = p.Name,
                Text       = text.Length > 500 ? text[..500] : text
            };
            _chatHistory.Add(msg);
            OnChatMessageReceived?.Invoke(msg);
        }

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>Human-readable summary for debug logging.</summary>
        public override string ToString() =>
            $"MultiplayerSession [{SessionId}] state={LobbyState} " +
            $"players={_players.Count}/{Settings.MaxPlayers} " +
            $"mode={Mode} day={_currentDay}";
    }
}
