extends Node

## NetworkManager — autoload singleton that handles Godot's ENet multiplayer.
##
## Responsibilities:
##   • Host / join sessions via LAN (ENet)
##   • Synchronize player roster, ready states, lobby settings
##   • Relay chat messages
##   • Start game and synchronize initial game state
##   • Bridge between Godot MultiplayerAPI and C# MultiplayerBridge
##
## Usage:
##   • Call NetworkManager.host_session(player_name, ...) to become host.
##   • Call NetworkManager.join_session(address, player_name, ...) to join.
##   • UI (MultiplayerLobby.gd) subscribes to NetworkManager signals.

# ── Signals ─────────────────────────────────────────────────────────────────

## Emitted when connection to host succeeds.
signal connection_succeeded()

## Emitted when connection fails.
signal connection_failed(reason: String)

## Emitted when a remote player joins the lobby.
signal player_joined(player_id: String, player_name: String, company_name: String, color: String, is_host: bool)

## Emitted when a remote player leaves.
signal player_left(player_id: String)

## Emitted when a remote player changes ready status.
signal player_ready_changed(player_id: String, ready: bool)

## Emitted when all players are ready (host only).
signal all_players_ready()

## Emitted when host starts the game; contains game settings.
signal game_started(settings: Dictionary)

## Emitted when a chat message arrives.
signal chat_message_received(player_id: String, player_name: String, text: String)

## Emitted when the local player's lobby ID is assigned.
signal local_player_id_assigned(player_id: String)

# ── Constants ───────────────────────────────────────────────────────────────

const DEFAULT_PORT := 9050
const MAX_PLAYERS := 8

# ── Node refs ───────────────────────────────────────────────────────────────

var _bridge: MultiplayerBridge

# ── State ───────────────────────────────────────────────────────────────────

var _local_player_id: String = ""
var _is_host: bool = false
var _players_sync_pending := false

# ── Godot lifecycle ─────────────────────────────────────────────────────────

func _ready() -> void:
	_bridge = MultiplayerBridge
	_multiplayer.connected_to_server.connect(_on_connected_to_server)
	_multiplayer.connection_failed.connect(_on_connection_failed)
	_multiplayer.server_disconnected.connect(_on_server_disconnected)
	_multiplayer.peer_connected.connect(_on_peer_connected)
	_multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	GD.print("NetworkManager: ready")

# ── Public API ──────────────────────────────────────────────────────────────

## Host a new multiplayer session.
func host_session(player_name: String, company_name: String, color: String, starting_capital: int) -> bool:
	if _multiplayer.multiplayer_peer != null:
		GD.print("NetworkManager: already in a session")
		return false

	var peer := ENetMultiplayerPeer.new()
	var err := peer.create_server(DEFAULT_PORT, MAX_PLAYERS)
	if err != OK:
		GD.print_err("NetworkManager: failed to create server, error ", err)
		return false

	_multiplayer.multiplayer_peer = peer
	_is_host = true

	# Register host player in the bridge
	var balance := starting_capital if starting_capital > 0 else -1.0
	var success := _bridge.AddPlayer(player_name, company_name, color, balance)
	if not success:
		GD.print_err("NetworkManager: bridge rejected host player")
		_multiplayer.multiplayer_peer = null
		return false

	_local_player_id = _bridge.GetPlayerIds().back()  # last added is us
	local_player_id_assigned.emit(_local_player_id)
	GD.print("NetworkManager: hosting session as player ", _local_player_id)
	return true

## Join an existing session at the given address.
func join_session(address: String, player_name: String, company_name: String, color: String) -> bool:
	if _multiplayer.multiplayer_peer != null:
		GD.print("NetworkManager: already in a session")
		return false

	var peer := ENetMultiplayerPeer.new()
	var err := peer.create_client(address, DEFAULT_PORT)
	if err != OK:
		GD.print_err("NetworkManager: failed to create client, error ", err)
		return false

	_multiplayer.multiplayer_peer = peer
	_is_host = false
	# Actual registration will happen after connection, via RPC from host
	_local_player_id = ""  # will be assigned by host
	GD.print("NetworkManager: attempting to join ", address)
	return true

## Leave the current session and return to main menu.
func leave_session() -> void:
	if _multiplayer.multiplayer_peer == null:
		return

	if _is_host:
		# Notify all clients
		rpc("_rpc_session_ended", "Host left the game")
	else:
		# Notify host we're leaving
		rpc_id(1, "_rpc_player_leaving", _local_player_id)

	_multiplayer.multiplayer_peer = null
	_is_host = false
	_local_player_id = ""
	_clear_bridge_players()
	GD.print("NetworkManager: left session")

## Toggle local player's ready status.
func set_ready(ready: bool) -> void:
	if _local_player_id.is_empty():
		return
	_bridge.SetPlayerReady(_local_player_id, ready)
	# Broadcast ready state to all peers
	if _is_host:
		# Host updates directly, broadcast to others
		_sync_player_state(_local_player_id)
	else:
		rpc_id(1, "_rpc_set_ready", _local_player_id, ready)

## Send a chat message.
func send_chat(text: String) -> void:
	if _local_player_id.is_empty():
		return
	_bridge.SendChatMessage(_local_player_id, text)
	# Broadcast to all peers
	if _is_host:
		_broadcast_chat(_local_player_id, text)
	else:
		rpc_id(1, "_rpc_send_chat", _local_player_id, text)

## Host only: start the game (requires all players ready).
func start_game() -> void:
	if not _is_host:
		GD.print("NetworkManager: only host can start game")
		return
	if not _bridge.StartGame():
		GD.print("NetworkManager: start game conditions not met")
		return
	# Notify all clients
	var settings := _prepare_game_settings()
	rpc("_rpc_game_started", settings)

# ── RPC methods (called remotely) ───────────────────────────────────────────

@rpc("any_peer", "call_local", "reliable")
func _rpc_register_player(player_name: String, company_name: String, color: String, starting_capital: int) -> void:
	var sender_id := str(_multiplayer.get_remote_sender_id())
	# Host adds the player to bridge
	if _is_host:
		var success := _bridge.AddPlayer(player_name, company_name, color, starting_capital)
		if success:
			var player_id := _bridge.GetPlayerIds().back()
			# Send the assigned player_id back to the client
			rpc_id(int(sender_id), "_rpc_assign_player_id", player_id)
			# Broadcast new player to all existing clients
			_sync_player_state(player_id)
		else:
			GD.print("NetworkManager: bridge rejected player ", player_name)
			# Kick the client? For now just ignore.

@rpc("authority", "call_local", "reliable")
func _rpc_assign_player_id(player_id: String) -> void:
	# Client receives its assigned player ID from host
	_local_player_id = player_id
	local_player_id_assigned.emit(_local_player_id)
	GD.print("NetworkManager: assigned player ID ", _local_player_id)

@rpc("authority", "call_local", "reliable")
func _rpc_sync_player_state(player_id: String, profile_dict: Dictionary) -> void:
	# Client updates its local bridge with remote player data
	if player_id == _local_player_id:
		return  # we already have our own data
	# Ensure player exists in bridge
	if not player_id in _bridge.GetPlayerIds():
		_bridge.AddPlayer(profile_dict["name"], profile_dict["company_name"],
			profile_dict["color"], profile_dict["balance"])
	_bridge.SetPlayerReady(player_id, profile_dict["is_ready"])
	# Notify UI
	player_joined.emit(player_id, profile_dict["name"],
		profile_dict["company_name"], profile_dict["color"], profile_dict["is_host"])

@rpc("any_peer", "call_local", "reliable")
func _rpc_set_ready(player_id: String, ready: bool) -> void:
	if _is_host:
		_bridge.SetPlayerReady(player_id, ready)
		_sync_player_state(player_id)

@rpc("any_peer", "call_local", "reliable")
func _rpc_send_chat(player_id: String, text: String) -> void:
	if _is_host:
		_bridge.SendChatMessage(player_id, text)
		_broadcast_chat(player_id, text)

@rpc("authority", "call_local", "reliable")
func _rpc_broadcast_chat(player_id: String, player_name: String, text: String) -> void:
	chat_message_received.emit(player_id, player_name, text)

@rpc("authority", "call_local", "reliable")
func _rpc_game_started(settings: Dictionary) -> void:
	game_started.emit(settings)

@rpc("authority", "call_local", "reliable")
func _rpc_session_ended(reason: String) -> void:
	GD.print("NetworkManager: session ended: ", reason)
	leave_session()

@rpc("any_peer", "call_local", "reliable")
func _rpc_player_leaving(player_id: String) -> void:
	if _is_host:
		_bridge.RemovePlayer(player_id)
		# Notify other clients
		rpc("_rpc_player_left", player_id)

@rpc("authority", "call_local", "reliable")
func _rpc_player_left(player_id: String) -> void:
	_bridge.RemovePlayer(player_id)
	player_left.emit(player_id)

# ── Internal helpers ────────────────────────────────────────────────────────

func _on_connected_to_server() -> void:
	GD.print("NetworkManager: connected to server")
	# Register with host
	var player_name := "Player"  # TODO: get from UI
	var company_name := "AS"
	var color := "#3A8EE8"
	var capital := 10000000
	rpc_id(1, "_rpc_register_player", player_name, company_name, color, capital)
	connection_succeeded.emit()

func _on_connection_failed() -> void:
	GD.print("NetworkManager: connection failed")
	_multiplayer.multiplayer_peer = null
	connection_failed.emit("Connection failed")

func _on_server_disconnected() -> void:
	GD.print("NetworkManager: server disconnected")
	leave_session()

func _on_peer_connected(id: int) -> void:
	GD.print("NetworkManager: peer connected ", id)
	# Host will handle registration via RPC

func _on_peer_disconnected(id: int) -> void:
	GD.print("NetworkManager: peer disconnected ", id)
	if _is_host:
		# Find which player ID corresponds to this peer (need mapping)
		# For simplicity, assume peer ID equals player ID (not true).
		# We'll implement mapping later.
		pass

func _sync_player_state(player_id: String) -> void:
	if not _is_host:
		return
	var profile := _bridge.GetPlayerProfile(player_id)
	rpc("_rpc_sync_player_state", player_id, profile)

func _broadcast_chat(player_id: String, text: String) -> void:
	var profile := _bridge.GetPlayerProfile(player_id)
	rpc("_rpc_broadcast_chat", player_id, profile["name"], text)

func _prepare_game_settings() -> Dictionary:
	# Gather settings from bridge and host UI
	var settings := {
		"map": "Norwegian Coast",
		"difficulty": "Normal",
		"starting_capital": 10000000,
		"mode": "Simultaneous",
		"players": []
	}
	for player_id in _bridge.GetPlayerIds():
		var profile := _bridge.GetPlayerProfile(player_id)
		settings["players"].append({
			"id": player_id,
			"name": profile["name"],
			"company": profile["company_name"],
			"color": profile["color"],
			"ready": profile["is_ready"],
			"host": profile["is_host"]
		})
	return settings

func _clear_bridge_players() -> void:
	for player_id in _bridge.GetPlayerIds():
		_bridge.RemovePlayer(player_id)

# ── Bridge event handlers ───────────────────────────────────────────────────

func _on_bridge_player_joined(player_id: String, player_name: String,
		company_name: String, color: String, is_host: bool) -> void:
	player_joined.emit(player_id, player_name, company_name, color, is_host)

func _on_bridge_player_left(player_id: String) -> void:
	player_left.emit(player_id)

func _on_bridge_player_ready_changed(player_id: String, ready: bool) -> void:
	player_ready_changed.emit(player_id, ready)

func _on_bridge_all_players_ready() -> void:
	all_players_ready.emit()

func _on_bridge_game_started() -> void:
	# Host will handle starting via start_game()
	pass

func _on_bridge_chat_message(player_id: String, player_name: String, text: String) -> void:
	chat_message_received.emit(player_id, player_name, text)

# Connect bridge signals (call after bridge is ready)
func _connect_bridge_signals() -> void:
	_bridge.PlayerJoined.connect(_on_bridge_player_joined)
	_bridge.PlayerLeft.connect(_on_bridge_player_left)
	_bridge.PlayerReadyChanged.connect(_on_bridge_player_ready_changed)
	_bridge.AllPlayersReady.connect(_on_bridge_all_players_ready)
	_bridge.GameStarted.connect(_on_bridge_game_started)
	_bridge.ChatMessageReceived.connect(_on_bridge_chat_message)