extends Control

## MultiplayerHUD — in-game overlay showing competitor rankings and trade offers.
##
## Sits above the main game HUD in the Z-order.  Displays:
##   • Compact leaderboard panel (all players, ranked)
##   • Active trade offers directed at the local player
##   • Quick-trade button to initiate a new offer
##   • Toggle visibility (Tab key / button)
##
## Expected node layout (MultiplayerHUD.tscn):
##
##   MultiplayerHUD (Control, full-screen, mouse_filter = IGNORE by default)
##   └─ PanelContainer (anchor: top-right, fixed width 280 px)
##      └─ VBoxContainer
##         ├─ HeaderRow (HBoxContainer)
##         │  ├─ TitleLabel (Label)       "Competitors"
##         │  └─ ToggleButton (Button)    "▼ / ▶"
##         ├─ LeaderboardPanel (VBoxContainer)
##         │  └─ RankList (VBoxContainer)
##         ├─ Separator (HSeparator)
##         ├─ TradesHeader (HBoxContainer)
##         │  ├─ TradesLabel (Label)      "Trade Offers"
##         │  └─ NewTradeButton (Button)  "+"
##         └─ TradesList (VBoxContainer)

# ── Color palette ─────────────────────────────────────────────────────────────

const COLOR_RANK_1   := Color(1.00, 0.84, 0.00)   # gold
const COLOR_RANK_2   := Color(0.75, 0.75, 0.75)   # silver
const COLOR_RANK_3   := Color(0.80, 0.50, 0.20)   # bronze
const COLOR_RANK_N   := Color(0.85, 0.85, 0.85)   # default
const COLOR_GOOD     := Color(0.20, 0.85, 0.30)
const COLOR_WARN     := Color(1.00, 0.80, 0.10)
const COLOR_DANGER   := Color(0.95, 0.20, 0.15)
const COLOR_PENDING  := Color(0.50, 0.80, 1.00)

# ── Node refs ─────────────────────────────────────────────────────────────────

@onready var toggle_button: Button        = $PanelContainer/VBoxContainer/HeaderRow/ToggleButton
@onready var leaderboard_panel: Control   = $PanelContainer/VBoxContainer/LeaderboardPanel
@onready var rank_list: VBoxContainer     = $PanelContainer/VBoxContainer/LeaderboardPanel/RankList
@onready var new_trade_button: Button     = $PanelContainer/VBoxContainer/TradesHeader/NewTradeButton
@onready var trades_list: VBoxContainer   = $PanelContainer/VBoxContainer/TradesList

# ── Trade dialog (spawned on demand) ─────────────────────────────────────────
# The scene path is resolved at runtime; replace with actual packed scene path.
const TRADE_DIALOG_SCENE := "res://godot/scenes/multiplayer/TradeDialog.tscn"

# ── State ─────────────────────────────────────────────────────────────────────

var _local_player_id: String = ""
var _is_expanded: bool = true

## Array of leaderboard entry Dictionaries:
## { rank, player_id, player_name, company, color, profit, sustain, welfare, score }
var _leaderboard: Array[Dictionary] = []

## Array of pending trade offer Dictionaries:
## { offer_id, from_id, from_name, item_type, quantity, price_nok, created_day }
var _pending_trades: Array[Dictionary] = []

## Emitted when the player wants to create a new trade offer
signal trade_offer_requested(to_player_id: String)

## Emitted when the player accepts/rejects a trade
signal trade_responded(offer_id: String, accepted: bool)

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	toggle_button.pressed.connect(_on_toggle_pressed)
	new_trade_button.pressed.connect(_on_new_trade_pressed)
	_set_expanded(true)
	print("MultiplayerHUD: ready")

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_multiplayer_hud"):
		_on_toggle_pressed()
		get_viewport().set_input_as_handled()

# ── Public API (called by network/bridge layer) ───────────────────────────────

## Called once at game start to identify this client.
func set_local_player(player_id: String) -> void:
	_local_player_id = player_id

## Replace the leaderboard with fresh data each tick.
## entries: Array of Dictionaries matching LeaderboardEntry fields.
func update_leaderboard(entries: Array) -> void:
	_leaderboard.clear()
	for e in entries:
		_leaderboard.append(e)
	_rebuild_rank_list()

## Add a new incoming trade offer directed at the local player.
func add_trade_offer(offer: Dictionary) -> void:
	# offer keys: offer_id, from_id, from_name, item_type, quantity, price_nok, created_day
	_pending_trades.append(offer)
	_rebuild_trades_list()

## Remove a resolved trade offer (accepted, rejected, or expired).
func resolve_trade_offer(offer_id: String) -> void:
	for i in range(_pending_trades.size() - 1, -1, -1):
		if _pending_trades[i]["offer_id"] == offer_id:
			_pending_trades.remove_at(i)
			break
	_rebuild_trades_list()

# ── Leaderboard rendering ─────────────────────────────────────────────────────

func _rebuild_rank_list() -> void:
	for child in rank_list.get_children():
		child.queue_free()

	for entry in _leaderboard:
		var row := _build_rank_row(entry)
		rank_list.add_child(row)

func _build_rank_row(entry: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)

	var is_local: bool = entry.get("player_id", "") == _local_player_id

	# Rank badge
	var rank_lbl := Label.new()
	var rank: int = entry.get("rank", 0)
	rank_lbl.text = "#%d" % rank
	rank_lbl.custom_minimum_size = Vector2(30, 0)
	rank_lbl.add_theme_color_override("font_color", _rank_color(rank))
	row.add_child(rank_lbl)

	# Player color swatch
	var swatch := ColorRect.new()
	swatch.custom_minimum_size = Vector2(14, 14)
	swatch.color = Color.html(entry.get("color", "#888888"))
	row.add_child(swatch)

	# Name (bold for local player)
	var name_lbl := Label.new()
	var pname: String = entry.get("player_name", "?")
	name_lbl.text = pname + (" (you)" if is_local else "")
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if is_local:
		name_lbl.add_theme_color_override("font_color", COLOR_GOOD)
	row.add_child(name_lbl)

	# Profit
	var profit_lbl := Label.new()
	var profit: float = entry.get("profit", 0.0)
	profit_lbl.text = _format_nok_compact(profit)
	profit_lbl.add_theme_color_override("font_color",
		COLOR_GOOD if profit >= 0 else COLOR_DANGER)
	profit_lbl.custom_minimum_size = Vector2(60, 0)
	row.add_child(profit_lbl)

	# Sustainability mini-bar (small colored square)
	var sustain: float = entry.get("sustain", 0.0)
	var sustain_lbl := Label.new()
	sustain_lbl.text = "S:%.0f" % sustain
	sustain_lbl.add_theme_color_override("font_color", _score_color(sustain))
	row.add_child(sustain_lbl)

	return row

func _rank_color(rank: int) -> Color:
	match rank:
		1: return COLOR_RANK_1
		2: return COLOR_RANK_2
		3: return COLOR_RANK_3
		_: return COLOR_RANK_N

func _score_color(score: float) -> Color:
	if score >= 70.0: return COLOR_GOOD
	elif score >= 40.0: return COLOR_WARN
	return COLOR_DANGER

# ── Trade offers rendering ────────────────────────────────────────────────────

func _rebuild_trades_list() -> void:
	for child in trades_list.get_children():
		child.queue_free()

	if _pending_trades.is_empty():
		var empty_lbl := Label.new()
		empty_lbl.text = "No pending offers"
		empty_lbl.add_theme_color_override("font_color", COLOR_RANK_N)
		trades_list.add_child(empty_lbl)
		return

	for offer in _pending_trades:
		var row := _build_trade_row(offer)
		trades_list.add_child(row)

func _build_trade_row(offer: Dictionary) -> Control:
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 2)

	# Summary line
	var info := Label.new()
	var item_type: String = offer.get("item_type", "Fish")
	var qty: float        = offer.get("quantity", 0.0)
	var price: float      = offer.get("price_nok", 0.0)
	var from_name: String = offer.get("from_name", "?")
	info.text = "%s offers %.1f t %s — %s" % [
		from_name, qty, item_type, _format_nok_compact(price)
	]
	info.add_theme_color_override("font_color", COLOR_PENDING)
	col.add_child(info)

	# Accept / Reject buttons
	var btn_row := HBoxContainer.new()

	var accept_btn := Button.new()
	accept_btn.text = "Accept"
	accept_btn.add_theme_color_override("font_color", COLOR_GOOD)
	var offer_id: String = offer.get("offer_id", "")
	accept_btn.pressed.connect(func(): _on_trade_response(offer_id, true))
	btn_row.add_child(accept_btn)

	var reject_btn := Button.new()
	reject_btn.text = "Reject"
	reject_btn.add_theme_color_override("font_color", COLOR_DANGER)
	reject_btn.pressed.connect(func(): _on_trade_response(offer_id, false))
	btn_row.add_child(reject_btn)

	col.add_child(btn_row)
	return col

# ── Trade actions ─────────────────────────────────────────────────────────────

func _on_trade_response(offer_id: String, accepted: bool) -> void:
	resolve_trade_offer(offer_id)
	trade_responded.emit(offer_id, accepted)

func _on_new_trade_pressed() -> void:
	# Open a target-player picker, then emit trade_offer_requested.
	# For now emit with empty string so the calling layer can show a dialog.
	trade_offer_requested.emit("")

# ── Toggle expand / collapse ──────────────────────────────────────────────────

func _on_toggle_pressed() -> void:
	_set_expanded(not _is_expanded)

func _set_expanded(expanded: bool) -> void:
	_is_expanded = expanded
	leaderboard_panel.visible = expanded
	trades_list.visible       = expanded
	new_trade_button.visible  = expanded
	toggle_button.text        = "▼" if expanded else "▶"

# ── Formatting helpers ────────────────────────────────────────────────────────

func _format_nok_compact(value: float) -> String:
	var abs_val := abs(value)
	var sign_str := "-" if value < 0 else ""
	if abs_val >= 1_000_000_000.0:
		return "%s%.1fB" % [sign_str, abs_val / 1_000_000_000.0]
	elif abs_val >= 1_000_000.0:
		return "%s%.1fM" % [sign_str, abs_val / 1_000_000.0]
	elif abs_val >= 1_000.0:
		return "%s%.0fk" % [sign_str, abs_val / 1_000.0]
	return "%s%.0f" % [sign_str, abs_val]
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
