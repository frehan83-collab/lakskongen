extends Control

## MultiplayerLobby — pre-game lobby screen for Lakskongen multiplayer.
##
## Responsibilities:
##   • Host / join session flow
##   • Live player list with ready status and color badges
##   • Session settings (map, difficulty, starting capital)
##   • Chat box (local echo; real transport handled by network layer)
##   • Start-game gate (host only, once all players are ready)
##
## Expected node layout (set up in MultiplayerLobby.tscn):
##
##   MultiplayerLobby (Control, full-screen)
##   └─ VBoxContainer
##      ├─ TitleBar (HBoxContainer)
##      │  ├─ TitleLabel (Label)          "Lakskongen — Multiplayer Lobby"
##      │  └─ SessionIdLabel (Label)
##      ├─ MainRow (HBoxContainer)
##      │  ├─ LeftPanel (VBoxContainer)
##      │  │  ├─ ConnectionBox (VBoxContainer)
##      │  │  │  ├─ NameEdit (LineEdit)
##      │  │  │  ├─ CompanyEdit (LineEdit)
##      │  │  │  ├─ ColorPicker (ColorPickerButton)
##      │  │  │  ├─ HostButton (Button)
##      │  │  │  ├─ JoinRow (HBoxContainer)
##      │  │  │  │  ├─ AddressEdit (LineEdit)
##      │  │  │  │  └─ JoinButton (Button)
##      │  │  │  └─ StatusLabel (Label)
##      │  │  └─ SettingsBox (VBoxContainer)
##      │  │     ├─ MapOption (OptionButton)
##      │  │     ├─ DifficultyOption (OptionButton)
##      │  │     ├─ CapitalRow (HBoxContainer)
##      │  │     │  ├─ CapitalLabel (Label)
##      │  │     │  └─ CapitalEdit (LineEdit)
##      │  │     └─ ModeOption (OptionButton)
##      │  └─ RightPanel (VBoxContainer)
##      │     ├─ PlayersLabel (Label)     "Players (0/4)"
##      │     ├─ PlayerList (VBoxContainer)
##      │     └─ ReadyButton (Button)
##      ├─ ChatBox (VBoxContainer)
##      │  ├─ ChatLog (RichTextLabel)
##      │  └─ ChatRow (HBoxContainer)
##      │     ├─ ChatInput (LineEdit)
##      │     └─ SendButton (Button)
##      └─ BottomBar (HBoxContainer)
##         ├─ BackButton (Button)
##         └─ StartButton (Button)       host-only

# ── Color palette ─────────────────────────────────────────────────────────────

const COLOR_READY    := Color(0.20, 0.85, 0.30)
const COLOR_NOT_READY := Color(0.95, 0.20, 0.15)
const COLOR_HOST_TAG := Color(1.00, 0.80, 0.10)
const COLOR_NEUTRAL  := Color(0.90, 0.90, 0.90)
const COLOR_SYSTEM   := Color(0.60, 0.80, 1.00)

# Available player colors (hex strings matching PlayerProfile.Color convention)
const PLAYER_COLORS := [
	"#E87B3A", "#3A8EE8", "#4EC94E", "#E83A8E",
	"#E8D43A", "#8E3AE8", "#3AE8D4", "#E83A3A",
]

# ── Node refs ─────────────────────────────────────────────────────────────────

@onready var title_label: Label               = $VBoxContainer/TitleBar/TitleLabel
@onready var session_id_label: Label          = $VBoxContainer/TitleBar/SessionIdLabel

# Connection / join
@onready var name_edit: LineEdit              = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/NameEdit
@onready var company_edit: LineEdit           = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/CompanyEdit
@onready var color_picker: ColorPickerButton  = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/ColorPicker
@onready var host_button: Button              = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/HostButton
@onready var address_edit: LineEdit           = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/JoinRow/AddressEdit
@onready var join_button: Button              = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/JoinRow/JoinButton
@onready var status_label: Label              = $VBoxContainer/MainRow/LeftPanel/ConnectionBox/StatusLabel

# Settings (host-editable)
@onready var map_option: OptionButton         = $VBoxContainer/MainRow/LeftPanel/SettingsBox/MapOption
@onready var difficulty_option: OptionButton  = $VBoxContainer/MainRow/LeftPanel/SettingsBox/DifficultyOption
@onready var capital_edit: LineEdit           = $VBoxContainer/MainRow/LeftPanel/SettingsBox/CapitalRow/CapitalEdit
@onready var mode_option: OptionButton        = $VBoxContainer/MainRow/LeftPanel/SettingsBox/ModeOption

# Player list
@onready var players_label: Label             = $VBoxContainer/MainRow/RightPanel/PlayersLabel
@onready var player_list: VBoxContainer       = $VBoxContainer/MainRow/RightPanel/PlayerList
@onready var ready_button: Button             = $VBoxContainer/MainRow/RightPanel/ReadyButton

# Chat
@onready var chat_log: RichTextLabel          = $VBoxContainer/ChatBox/ChatLog
@onready var chat_input: LineEdit             = $VBoxContainer/ChatBox/ChatRow/ChatInput
@onready var send_button: Button              = $VBoxContainer/ChatBox/ChatRow/SendButton

# Bottom bar
@onready var back_button: Button              = $VBoxContainer/BottomBar/BackButton
@onready var start_button: Button             = $VBoxContainer/BottomBar/StartButton

# ── State ─────────────────────────────────────────────────────────────────────

var _local_player_id: String = ""
var _is_host: bool = false
var _is_ready: bool = false

## Array of Dictionaries: { id, name, company, color, ready, host }
var _players: Array[Dictionary] = []

## Emitted so the scene tree can transition to the game
signal lobby_start_game(settings: Dictionary)
signal lobby_cancelled()

# ── Maps / difficulties ───────────────────────────────────────────────────────

const MAPS := ["Norwegian Coast", "Vestfjorden", "Hardanger", "Troms"]
const DIFFICULTIES := ["Easy", "Normal", "Hard", "Realistic"]
const MODES := ["Simultaneous", "Turn-Based"]
const CAPITALS := [5_000_000, 10_000_000, 25_000_000, 50_000_000]

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_populate_options()
	_wire_signals()
	_set_lobby_phase(false)  # not yet in a session
	start_button.visible = false
	print("MultiplayerLobby: ready")

func _populate_options() -> void:
	for m in MAPS:
		map_option.add_item(m)
	for d in DIFFICULTIES:
		difficulty_option.add_item(d)
	difficulty_option.selected = 1  # Normal
	for mode in MODES:
		mode_option.add_item(mode)
	capital_edit.text = "10000000"
	map_option.selected = 0

func _wire_signals() -> void:
	host_button.pressed.connect(_on_host_pressed)
	join_button.pressed.connect(_on_join_pressed)
	ready_button.pressed.connect(_on_ready_pressed)
	send_button.pressed.connect(_on_send_chat)
	back_button.pressed.connect(_on_back_pressed)
	start_button.pressed.connect(_on_start_pressed)
	chat_input.text_submitted.connect(func(_t): _on_send_chat())

# ── Host / Join ───────────────────────────────────────────────────────────────

func _on_host_pressed() -> void:
	var pname := name_edit.text.strip_edges()
	var company := company_edit.text.strip_edges()
	if pname.is_empty():
		_set_status("Enter your name first.", COLOR_NOT_READY)
		return

	_is_host = true
	_local_player_id = _generate_id()
	var color := _color_to_hex(color_picker.color)

	# Register the host as first player
	var host_entry := {
		"id": _local_player_id,
		"name": pname,
		"company": company if not company.is_empty() else pname + " AS",
		"color": color,
		"ready": false,
		"host": true
	}
	_players.clear()
	_players.append(host_entry)

	_set_lobby_phase(true)
	_set_status("Hosting session. Waiting for players...", COLOR_SYSTEM)
	session_id_label.text = "Session: " + _local_player_id.left(8).to_upper()
	_refresh_player_list()
	_log_chat("[SYSTEM] You are now hosting. Share your address to invite players.", COLOR_SYSTEM)

func _on_join_pressed() -> void:
	var pname := name_edit.text.strip_edges()
	if pname.is_empty():
		_set_status("Enter your name first.", COLOR_NOT_READY)
		return

	var address := address_edit.text.strip_edges()
	if address.is_empty():
		_set_status("Enter host address.", COLOR_NOT_READY)
		return

	# Simulate connection (real ENet / WebSocket handshake goes here)
	_is_host = false
	_local_player_id = _generate_id()
	_set_lobby_phase(true)
	_set_status("Connecting to " + address + "...", COLOR_NEUTRAL)
	_log_chat("[SYSTEM] Connecting to " + address, COLOR_SYSTEM)

	# TODO: replace with actual network transport call:
	# NetworkTransport.connect_to_host(address, port)

# ── Called by network layer when a remote player joins/leaves ─────────────────

func add_remote_player(id: String, pname: String, company: String,
                        color: String, is_host: bool) -> void:
	for p in _players:
		if p["id"] == id:
			return  # already tracked
	_players.append({
		"id": id, "name": pname, "company": company,
		"color": color, "ready": false, "host": is_host
	})
	_refresh_player_list()
	_log_chat("[SYSTEM] %s joined." % pname, COLOR_SYSTEM)
	players_label.text = "Players (%d/%d)" % [_players.size(), _get_max_players()]

func remove_remote_player(id: String) -> void:
	for i in range(_players.size() - 1, -1, -1):
		if _players[i]["id"] == id:
			var pname: String = _players[i]["name"]
			_players.remove_at(i)
			_log_chat("[SYSTEM] %s left." % pname, COLOR_SYSTEM)
			break
	_refresh_player_list()
	players_label.text = "Players (%d/%d)" % [_players.size(), _get_max_players()]

func set_remote_player_ready(id: String, ready: bool) -> void:
	for p in _players:
		if p["id"] == id:
			p["ready"] = ready
			break
	_refresh_player_list()
	_check_all_ready()

# ── Ready button ──────────────────────────────────────────────────────────────

func _on_ready_pressed() -> void:
	_is_ready = not _is_ready
	for p in _players:
		if p["id"] == _local_player_id:
			p["ready"] = _is_ready
			break

	ready_button.text      = "Cancel Ready" if _is_ready else "Ready"
	ready_button.add_theme_color_override("font_color",
		COLOR_READY if _is_ready else COLOR_NOT_READY)

	_refresh_player_list()
	_check_all_ready()

	# TODO: broadcast ready state via network transport

func _check_all_ready() -> void:
	if _players.size() < 2:
		start_button.disabled = true
		return
	var all_ready := _players.all(func(p): return p["ready"])
	start_button.disabled = not (all_ready and _is_host)
	if all_ready:
		_log_chat("[SYSTEM] All players ready!", COLOR_READY)

# ── Start game ────────────────────────────────────────────────────────────────

func _on_start_pressed() -> void:
	if not _is_host: return

	var settings := {
		"map":           MAPS[map_option.selected],
		"difficulty":    DIFFICULTIES[difficulty_option.selected],
		"starting_capital": int(capital_edit.text),
		"mode":          MODES[mode_option.selected],
		"players":       _players.duplicate(true),
	}

	_log_chat("[SYSTEM] Game starting!", COLOR_SYSTEM)
	# TODO: broadcast start signal via network transport
	lobby_start_game.emit(settings)

# ── Chat ──────────────────────────────────────────────────────────────────────

func _on_send_chat() -> void:
	var text := chat_input.text.strip_edges()
	if text.is_empty(): return

	var sender := _get_local_player_name()
	_log_chat("[%s] %s" % [sender, text], COLOR_NEUTRAL)
	chat_input.clear()

	# TODO: broadcast chat message via network transport

func receive_chat(sender_name: String, text: String) -> void:
	_log_chat("[%s] %s" % [sender_name, text], COLOR_NEUTRAL)

func _log_chat(line: String, color: Color) -> void:
	chat_log.push_color(color)
	chat_log.append_text(line + "\n")
	chat_log.pop()

# ── Player list rendering ─────────────────────────────────────────────────────

func _refresh_player_list() -> void:
	for child in player_list.get_children():
		child.queue_free()

	for p in _players:
		var row := HBoxContainer.new()

		# Color swatch
		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(18, 18)
		swatch.color = Color.html(p["color"])
		row.add_child(swatch)

		# Name + company
		var lbl := Label.new()
		var host_tag := " [HOST]" if p["host"] else ""
		lbl.text = "%s — %s%s" % [p["name"], p["company"], host_tag]
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(lbl)

		# Ready badge
		var badge := Label.new()
		badge.text = "READY" if p["ready"] else "NOT READY"
		badge.add_theme_color_override("font_color",
			COLOR_READY if p["ready"] else COLOR_NOT_READY)
		row.add_child(badge)

		player_list.add_child(row)

# ── Settings lock (non-host players cannot edit) ──────────────────────────────

func _set_lobby_phase(in_lobby: bool) -> void:
	host_button.visible   = not in_lobby
	join_button.visible   = not in_lobby
	address_edit.visible  = not in_lobby
	name_edit.editable    = not in_lobby
	company_edit.editable = not in_lobby

	ready_button.visible  = in_lobby
	chat_input.editable   = in_lobby
	send_button.disabled  = not in_lobby

	var host_controls := [map_option, difficulty_option, capital_edit, mode_option]
	for ctrl in host_controls:
		ctrl.disabled = not (in_lobby and _is_host)

	if in_lobby and _is_host:
		start_button.visible = true
		start_button.disabled = true  # enabled once all ready

# ── Helpers ───────────────────────────────────────────────────────────────────

func _on_back_pressed() -> void:
	lobby_cancelled.emit()

func _set_status(text: String, color: Color) -> void:
	status_label.text = text
	status_label.add_theme_color_override("font_color", color)

func _get_local_player_name() -> String:
	for p in _players:
		if p["id"] == _local_player_id:
			return p["name"]
	return "Unknown"

func _get_max_players() -> int:
	return int(map_option.get_item_count())  # placeholder; real value from settings

func _generate_id() -> String:
	return "%x" % [randi()]

func _color_to_hex(c: Color) -> String:
	return "#%s%s%s" % [
		"%02x" % int(c.r * 255),
		"%02x" % int(c.g * 255),
		"%02x" % int(c.b * 255),
	]
