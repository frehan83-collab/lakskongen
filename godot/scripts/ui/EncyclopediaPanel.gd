extends Control

## In-game encyclopedia browser for Lakskongen.
## Content lives in res://data/encyclopedia.json: categories and entries with
## an English (en) and a Norwegian (nb) title and text; the active locale picks
## the language (roadmap U1-03: the panel was English-only).

signal close_requested

# ── Colors (Norwegian ocean theme) ──────────────────────────────────────────
const BG_DEEP       := Color("#F4F1EA")
const BG_PANEL      := Color("#FFFFFF")
const BG_SIDEBAR    := Color("#EDE9E0")
const BG_ENTRY      := Color("#EDE9E0")
const BG_SELECTED   := Color("#1F5E73")
const BG_HOVER      := Color("#1F5E73")
const BG_SEARCH     := Color("#EDE9E0")
const BORDER_COLOR  := Color("#D6CFBF")
const TEXT_TITLE    := Color("#1C1E20")
const TEXT_BODY     := Color("#5A6168")
const TEXT_MUTED    := Color("#5A6168")
const TEXT_ACCENT   := Color("#1F5E73")
const TEXT_RELATED  := Color("#1F5E73")
const CAT_COLORS := {
	"species":     Color("#2E6B45"),
	"diseases":    Color("#B3172F"),
	"equipment":   Color("#1F5E73"),
	"regulations": Color("#8A5A00"),
	"feed":        Color("#2E6B45"),
	"markets":     Color("#7B9BC0"),
}

const DATA_PATH := "res://data/encyclopedia.json"

# ── Encyclopedia data (loaded from DATA_PATH) ─────────────────────────────────
var _categories: Array = []   # [{id, title: {en, nb}}]
var _entries: Array = []      # [{id, category, icon, title: {en, nb}, paragraphs: {en, nb}, related}]

# ── State ─────────────────────────────────────────────────────────────────────
var _current_category: String = "species"
var _current_entry_id: String = ""
var _search_text: String = ""
var _category_buttons: Dictionary = {}
var _entry_buttons: Dictionary = {}

# ── Node refs (declared in scenes/ui/EncyclopediaPanel.tscn) ────────────────
@onready var _category_list: VBoxContainer = %CategoryList
@onready var _entry_list: VBoxContainer = %EntryList
@onready var _content_scroll: ScrollContainer = %ContentScroll
@onready var _content_box: VBoxContainer = %ContentBox
@onready var _search_field: LineEdit = %SearchField
@onready var _title_label: Label = %TitleLabel
@onready var _icon_label: Label = %IconLabel
@onready var _desc_box: VBoxContainer = %DescBox
@onready var _related_box: HBoxContainer = %RelatedBox
@onready var _entry_count_label: Label = %EntryCountLabel
@onready var _header_title: Label = %HeaderTitle
@onready var _cat_header: Label = %CatHeader
@onready var _entry_header: Label = %EntryHeader
@onready var _related_label: Label = %RelatedLabel
@onready var _close_btn: Button = %CloseBtn

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_load_data()
	_apply_localization()
	_search_field.text_changed.connect(_on_search_changed)
	_close_btn.pressed.connect(_on_close_pressed)
	_populate_categories()
	_select_category(_first_category())
	hide()

func _apply_localization() -> void:
	_header_title.text = tr("UI_ENCYCLOPEDIA_TITLE")
	_cat_header.text = tr("UI_ENCYCLOPEDIA_CATEGORIES_HEADER")
	_entry_header.text = tr("UI_ENCYCLOPEDIA_ENTRIES_HEADER")
	_related_label.text = tr("UI_ENCYCLOPEDIA_SEE_ALSO")
	_search_field.placeholder_text = "🔍  " + tr("UI_ENCYCLOPEDIA_SEARCH")
	_search_field.tooltip_text = tr("UI_ENCYCLOPEDIA_SEARCH_TIP")
	_close_btn.tooltip_text = tr("UI_ENCYCLOPEDIA_CLOSE_TIP")

func _populate_categories() -> void:
	for c in _category_list.get_children():
		c.queue_free()
	_category_buttons.clear()
	for cat in _categories:
		var cid := str(cat.get("id", ""))
		var btn := _make_category_button(cid)
		_category_buttons[cid] = btn
		_category_list.add_child(btn)

# ─────────────────────────────────────────────────────────────────────────────
func show_panel() -> void:
	_search_field.text = ""
	_search_text = ""
	show()
	modulate.a = 0.0
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 1.0, 0.35)
	_select_category(_first_category())

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func hide_panel() -> void:
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.25)
	tw.tween_callback(hide)

# ─────────────────────────────────────────────────────────────────────────────
# (Shell layout now lives in scenes/ui/EncyclopediaPanel.tscn — see _apply_localization.)

# ─────────────────────────────────────────────────────────────────────────────
func _show_welcome() -> void:
	_title_label.text = tr("UI_ENCYCLOPEDIA_WELCOME")
	_icon_label.text = "📚"
	_clear_desc()
	var lbl := Label.new()
	lbl.text = tr("UI_ENCYCLOPEDIA_SELECT_INFO")
	LakskongenTheme.set_size(lbl, 16)
	LakskongenTheme.set_text_color(lbl, TEXT_BODY)
	lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_desc_box.add_child(lbl)
	_clear_related()

func _show_entry(entry_id: String) -> void:
	var entry := _get_entry(entry_id)
	if entry.is_empty():
		return
	_current_entry_id = entry_id

	_title_label.text = _text(entry.get("title", {}))
	_icon_label.text = entry.get("icon", "📄")

	_clear_desc()
	for para in _paragraphs(entry):
		var lbl := Label.new()
		lbl.text = para
		LakskongenTheme.set_size(lbl, 15)
		LakskongenTheme.set_text_color(lbl, TEXT_BODY)
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_desc_box.add_child(lbl)

	_clear_related()
	for rel_id: String in entry.get("related", []):
		var rel := _get_entry(rel_id)
		if rel.is_empty():
			continue
		var rel_btn := _make_button(rel.get("icon", "📄") + "  " + _text(rel.get("title", {})), 13, TEXT_RELATED)
		rel_btn.pressed.connect(_select_entry.bind(rel_id))
		_related_box.add_child(rel_btn)

	# Scroll to top
	_content_scroll.scroll_vertical = 0

	# Update entry button visuals
	_refresh_entry_button_styles()

func _clear_desc() -> void:
	for c in _desc_box.get_children():
		c.queue_free()

func _clear_related() -> void:
	for c in _related_box.get_children():
		c.queue_free()

# ─────────────────────────────────────────────────────────────────────────────
func _select_category(cat: String) -> void:
	_current_category = cat
	_refresh_category_styles()
	_populate_entry_list()
	# Auto-select first visible entry
	var visible := _get_visible_entries()
	if visible.size() > 0:
		_select_entry(visible[0]["id"])

func _select_entry(entry_id: String) -> void:
	_current_entry_id = entry_id
	# If entry is in a different category, switch
	var entry := _get_entry(entry_id)
	if not entry.is_empty() and entry["category"] != _current_category:
		_current_category = entry["category"]
		_refresh_category_styles()
		_populate_entry_list()
	_show_entry(entry_id)

func _populate_entry_list() -> void:
	for c in _entry_list.get_children():
		c.queue_free()
	_entry_buttons.clear()

	var visible := _get_visible_entries()
	_entry_count_label.text = "  %d  " % visible.size()

	for entry in visible:
		var btn := _make_entry_button(entry)
		_entry_buttons[entry["id"]] = btn
		_entry_list.add_child(btn)

	_refresh_entry_button_styles()

func _get_visible_entries() -> Array:
	var result: Array = []
	var search_lower := _search_text.to_lower()
	for e in _entries:
		if e["category"] != _current_category:
			continue
		if search_lower != "" and not (_text(e.get("title", {})).to_lower().contains(search_lower)):
			continue
		result.append(e)
	return result

func _get_entry(entry_id: String) -> Dictionary:
	for e in _entries:
		if e["id"] == entry_id:
			return e
	return {}

# ── Data ──────────────────────────────────────────────────────────────────────

func _load_data() -> void:
	var file := FileAccess.open(DATA_PATH, FileAccess.READ)
	if file == null:
		push_error("EncyclopediaPanel: cannot open %s" % DATA_PATH)
		return
	var parsed = JSON.parse_string(file.get_as_text())
	if not (parsed is Dictionary):
		push_error("EncyclopediaPanel: %s is not a JSON object" % DATA_PATH)
		return
	_categories = parsed.get("categories", [])
	_entries = parsed.get("entries", [])

## The text of a {en, nb} pair in the active language (nb unless English).
func _text(pair) -> String:
	if not (pair is Dictionary):
		return str(pair)
	var lang := "nb" if SimFormatters.is_norwegian() else "en"
	return str(pair.get(lang, pair.get("en", "")))

func _paragraphs(entry: Dictionary) -> Array:
	var paras = entry.get("paragraphs", {})
	if not (paras is Dictionary):
		return []
	var lang := "nb" if SimFormatters.is_norwegian() else "en"
	return paras.get(lang, paras.get("en", []))

func _category_title(cat_id: String) -> String:
	for c in _categories:
		if str(c.get("id", "")) == cat_id:
			return _text(c.get("title", {}))
	return cat_id

func _first_category() -> String:
	return str(_categories[0].get("id", "")) if not _categories.is_empty() else ""

# ── Style refresh ─────────────────────────────────────────────────────────────
func _refresh_category_styles() -> void:
	for cat in _category_buttons:
		var btn: Button = _category_buttons[cat]
		var is_sel: bool = (cat == _current_category)
		_style_category_button(btn, cat, is_sel)

func _refresh_entry_button_styles() -> void:
	for eid in _entry_buttons:
		var btn: Button = _entry_buttons[eid]
		var is_sel: bool = (eid == _current_entry_id)
		_style_entry_button_active(btn, is_sel)

# ── Widget factories ───────────────────────────────────────────────────────────
func _make_category_button(cat: String) -> Button:
	var btn := Button.new()
	btn.text = "  " + _category_title(cat)
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.custom_minimum_size = Vector2(0, 46)
	_style_category_button(btn, cat, false)
	btn.pressed.connect(_select_category.bind(cat))
	return btn

## Category and entry rows are "ListItemButton" toggles: quiet rows, the
## selected one tinted with an info edge (theme variation, Chart surface).
func _style_category_button(btn: Button, _cat: String, selected: bool) -> void:
	btn.theme_type_variation = &"ListItemButton"
	btn.toggle_mode = true
	btn.set_pressed_no_signal(selected)

func _make_entry_button(entry: Dictionary) -> Button:
	var btn := Button.new()
	btn.text = "  " + entry.get("icon", "") + "  " + _text(entry.get("title", {}))
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.custom_minimum_size = Vector2(0, 40)
	_style_entry_button_active(btn, false)
	btn.pressed.connect(_select_entry.bind(entry["id"]))
	return btn

func _style_entry_button_active(btn: Button, selected: bool) -> void:
	btn.theme_type_variation = &"ListItemButton"
	btn.toggle_mode = true
	btn.set_pressed_no_signal(selected)

## Related-entry chips: compact neutral buttons.
func _make_button(text: String, _font_size: int, _color: Color) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.theme_type_variation = &"SmallButton"
	return btn

# (Static box/separator factories removed — shell styling lives in scenes/ui/EncyclopediaPanel.tscn.)

# ── Signal handlers ───────────────────────────────────────────────────────────
func _on_search_changed(new_text: String) -> void:
	_search_text = new_text
	_populate_entry_list()
	var visible := _get_visible_entries()
	if visible.size() > 0:
		_select_entry(visible[0]["id"])

func _on_close_pressed() -> void:
	hide_panel()
	emit_signal("close_requested")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible:
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_on_close_pressed()
		get_viewport().set_input_as_handled()
