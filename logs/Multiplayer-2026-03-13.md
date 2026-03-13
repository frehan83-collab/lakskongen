# Multiplayer Foundation — 2026-03-13

## Summary

Three new files establish the complete multiplayer foundation for Lakskongen.

---

## Files created

### `src/LakskongenSim/Core/MultiplayerManager.cs`

Pure C# class (no Godot dependency), in the `LakskongenSim.Core` namespace.

**Key types:**

| Type | Purpose |
|---|---|
| `PlayerProfile` | Per-player roster entry: id, name, company, color, balance, fish count, ready flag, cumulative profit, sustainability & welfare scores, composite rank score |
| `MultiplayerSettings` | Map, difficulty, starting capital, game mode, max players, session length |
| `TradeOffer` | Bilateral offer between two players: item type (Fish/Contract/License), quantity, price in NOK, expiry in days, status (Pending/Accepted/Rejected/Expired) |
| `LeaderboardEntry` | Serializable rank snapshot broadcast to all clients |
| `ChatMessage` | Timestamped chat line |
| `GameMode` | `TurnBased` or `Simultaneous` |
| `LobbyState` | `Waiting` / `Playing` / `Finished` |

**`MultiplayerManager` capabilities:**

- Player roster with add/remove and ready-toggle
- `StartGame()` gate: requires ≥2 players all ready
- `AdvanceDay()`: handles simultaneous ticking or turn rotation
- `UpdatePlayerStats()`: per-tick stat sync called from Godot bridge
- `RefreshLeaderboard()`: sorts by composite score (profit + sustainability + welfare), fires `OnLeaderboardUpdated`
- Trade lifecycle: `CreateTradeOffer()` → `RespondToTradeOffer()` → balance transfer + `OnTradeResolved`; auto-expiry after configurable days
- Chat with 500-char cap per message
- C# events for every state change so the Godot layer stays decoupled

---

### `godot/scripts/multiplayer/MultiplayerLobby.gd`

Pre-game lobby screen (extends `Control`).

**Features:**

- Host / join flow with name, company, and color picker inputs
- Live `PlayerList` panel: color swatch, name, company, HOST tag, READY badge
- Settings panel (host-editable only): map, difficulty, starting capital, game mode
- Ready button toggles local ready state; `StartButton` enabled only when host + all players ready
- `lobby_start_game(settings)` signal carries full settings dict to the scene router
- Chat box: local echo + `receive_chat()` method for remote messages
- Helper methods `add_remote_player()`, `remove_remote_player()`, `set_remote_player_ready()` for the network transport layer to call
- Network calls are marked with `# TODO:` comments where ENet/WebSocket integration goes

---

### `godot/scripts/multiplayer/MultiplayerHUD.gd`

In-game overlay (extends `Control`, designed to sit above the main HUD).

**Features:**

- Compact leaderboard panel: rank badge (gold/silver/bronze/grey), color swatch, name, profit (color-coded), sustainability score
- Local player highlighted in green with "(you)" tag
- `update_leaderboard(entries)` — called each tick; full rebuild of rank rows
- Trade offers panel: incoming offers show sender, quantity, item type, price; Accept/Reject buttons per offer
- `add_trade_offer(offer)` / `resolve_trade_offer(offer_id)` for network layer
- `trade_offer_requested(to_player_id)` signal — triggers a dialog for offer creation
- `trade_responded(offer_id, accepted)` signal — relayed to network transport
- Tab-key toggle (requires `toggle_multiplayer_hud` InputMap action) and collapse button
- Compact NOK formatter: 1 B / 1 M / 1 k suffixes

---

## Architecture notes

- `MultiplayerManager.cs` is intentionally transport-agnostic: it models session logic only. The Godot layer (or a future `NetworkTransport` autoload) handles ENet/WebSocket/relay.
- The shared `MarketSimulation` reference on `MultiplayerManager` ensures all players see the same price curve, preventing information asymmetry.
- Composite score formula: `profit_MNOK + sustainability × 500 + welfare × 300` — profit-dominant but sustainability and welfare remain meaningful differentiators.
- Trade balance transfers are atomic inside `RespondToTradeOffer()`; item transfers (fish, contracts, licenses) are delegated to the calling layer which has access to the per-player `SimulationEngine`.

---

## Next steps

- Implement `NetworkTransport` autoload (ENet peer-to-peer or dedicated relay)
- Wire `MultiplayerManager` events into `SimBridge` for single-source-of-truth stat pushes
- Create `TradeDialog.tscn` referenced by `MultiplayerHUD`
- Add `toggle_multiplayer_hud` action to Godot InputMap
- Persist session replay data via `SaveLoadManager` for post-game analysis
