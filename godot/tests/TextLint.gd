extends RefCounted

## TextLint — the label-dump lint behind roadmap U1-03 (text correctness).
##
## dump(root) lists every piece of player-facing text on screen: the visible
## Labels, rich text, button and check-box captions, option and list items,
## line-edit text and placeholders, tab titles and the tooltips of visible
## controls. offenders(root) runs the checks below over that dump and returns
## one line per finding ("[kind] path: 'text' → token"):
##
##  * format     — an unformatted placeholder (%s, %d, %.1f, %%, {0}, {name})
##  * enum       — a CamelCase identifier (PartlyCloudy, SemiClosed, GrowthRate)
##  * id         — a snake_case or UPPER_SNAKE id or translation key
##                 (po3_nord, forste_sesong, UI_DASHBOARD_TITLE, DISEASE_OUTBREAK)
##  * raw        — a raw script value (null, NaN, inf, true/false, <null>)
##  * english    — an English word in the Norwegian UI (nb locales only)
##  * decimal    — a decimal point where nb-NO writes a comma (11.49 → 11,49)
##
## Proper names and units with internal capitals (pH, kNOK, SalMax) are listed
## in ALLOWED_TOKENS; nothing else is exempt.

## Tokens that look like enums but are names or units the player should see.
const ALLOWED_TOKENS := [
	"pH", "kNOK", "kWh", "MWh", "GWh", "kWp", "mNOK", "CO₂e", "LiDAR",
	# Parody rival companies (D17) and in-game brands.
	"SalMax", "NordLaks", "GmbH",
]

## Unambiguous English words. Words that are also Norwegian (for, at, to, i,
## per, all, over, under, status, total, start, profit, offshore, smolt, storm,
## by, is, not, be, min, medium, optimal, region, spot, forward, hub, auto, sum)
## are left out on purpose.
const ENGLISH_WORDS := [
	"the", "and", "of", "with", "your", "you", "this", "that", "from", "are", "will",
	"have", "has", "been", "which", "when", "where", "there", "their", "would",
	"should", "could", "into", "than", "then", "them", "they", "these", "those",
	"what", "who", "why", "how", "our", "its", "was", "were", "an", "on", "or",
	"in", "off", "out", "up", "down", "no", "yes", "it",
	"overview", "refresh", "settings", "cancel", "close", "save", "load", "loading",
	"new", "game", "fish", "health", "finance", "weather", "market", "contract",
	"contracts", "other", "error", "none", "unknown", "revenue", "cost", "costs",
	"price", "prices", "feed", "feeding", "harvest", "pens", "site", "sites", "water",
	"temperature", "oxygen", "lice", "mortality", "growth", "sea", "day", "days",
	"week", "weeks", "month", "months", "year", "years", "today", "tomorrow",
	"high", "low", "average", "good", "bad", "poor", "excellent", "critical",
	"warning", "warnings", "alert", "alerts", "ready", "pending", "active", "completed",
	"complete", "failed", "success", "open", "closed", "available", "required",
	"level", "research", "crew", "staff", "workers", "hire", "buy", "sell", "sold",
	"loan", "loans", "debt", "cash", "equity", "assets", "income", "expenses",
	"expense", "taxes", "interest", "wind", "rain", "cloudy", "sunny", "clear", "fog",
	"calm", "heavy", "partly", "salmon", "welfare", "disease", "treatment",
	"quality", "export", "click", "select", "choose", "please", "back", "next",
	"previous", "continue", "quit", "exit", "help", "goal", "details", "view",
	"repairs", "achievements", "tutorial", "breeding", "genetics", "trait",
	"current", "value", "breed", "coming", "soon", "green", "yellow", "red",
	"optimization", "decisions", "inbox", "attention", "items", "item",
	"nothing", "right", "now", "landscape", "coastline",
	"licence", "licences", "license", "licenses", "distance", "processing",
	"annual", "key", "operators", "range", "risk", "tolerance", "max",
	"buyer", "buyers", "sign", "intervention",
]

const ID_UPPER := "\\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\\b"
const ID_SNAKE := "\\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\\b"
const ENUM_UPPER := "\\b[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+\\b"
const ENUM_LOWER := "\\b[a-z]+[A-Z][A-Za-z0-9]*\\b"
const FORMAT := "%[-+0#]*[0-9]*(?:\\.[0-9]+)?[sdifxXoceEgGv]|%%|\\{[0-9a-z_]*\\}"
const RAW := "\\b(?:null|Null|NULL|NaN|nan|inf|INF|true|false|Nil)\\b|<null>|<Object"
const DECIMAL := "(?<![0-9.,])[0-9]+\\.[0-9]+(?![0-9.,])"
const WORD := "[A-Za-zÀ-ÖØ-öø-ÿ]+"

static var _re_cache: Dictionary = {}


## Every player-facing string on screen: [{"path", "kind", "text"}, …].
static func dump(root: Node) -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	for c in _visible_controls(root):
		for entry in _texts(c):
			if str(entry[1]).strip_edges() != "" and not _is_path(entry):
				out.append({"path": _path(c), "kind": entry[0], "text": entry[1]})
	return out


## Findings over the dump; `locale` decides whether English words count.
static func offenders(root: Node, locale: String = "nb") -> PackedStringArray:
	var out := PackedStringArray()
	var norwegian := locale.begins_with("nb") or locale.begins_with("no") or locale.begins_with("nn")
	for e in dump(root):
		for finding in check_text(String(e["text"]), norwegian):
			out.append("[%s] %s (%s): '%s' → %s" % [finding[0], e["path"], e["kind"],
				String(e["text"]).replace("\n", " ").left(60), finding[1]])
	return out


## [[kind, token], …] for one string.
static func check_text(text: String, norwegian: bool) -> Array:
	var found: Array = []
	for m in _re(FORMAT).search_all(text):
		found.append(["format", m.get_string()])
	for m in _re(ID_UPPER).search_all(text):
		found.append(["id", m.get_string()])
	for m in _re(ID_SNAKE).search_all(text):
		found.append(["id", m.get_string()])
	for pattern in [ENUM_UPPER, ENUM_LOWER]:
		for m in _re(pattern).search_all(text):
			var tok := m.get_string()
			if not ALLOWED_TOKENS.has(tok):
				found.append(["enum", tok])
	for m in _re(RAW).search_all(text):
		found.append(["raw", m.get_string()])
	if norwegian:
		for m in _re(DECIMAL).search_all(text):
			found.append(["decimal", m.get_string()])
		for m in _re(WORD).search_all(text):
			var w := m.get_string()
			if ENGLISH_WORDS.has(w.to_lower()) and not ALLOWED_TOKENS.has(w):
				found.append(["english", w])
	return found


# ── Text extraction ───────────────────────────────────────────────────────────

## [[kind, text], …] for one control.
static func _texts(c: Control) -> Array:
	var out: Array = []
	if c is Label:
		out.append(["label", _shown(c, (c as Label).text)])
	elif c is RichTextLabel:
		out.append(["rich", (c as RichTextLabel).get_parsed_text()])
	elif c is OptionButton:
		var ob := c as OptionButton
		for i in ob.item_count:
			if not ob.is_item_separator(i):
				out.append(["option", _shown(c, ob.get_item_text(i))])
	elif c is BaseButton and c.has_method("get_text"):
		out.append(["button", _shown(c, str(c.call("get_text")))])
	elif c is LineEdit:
		var le := c as LineEdit
		out.append(["edit", le.text])
		out.append(["placeholder", _shown(c, le.placeholder_text)])
	elif c is TextEdit:
		out.append(["placeholder", _shown(c, (c as TextEdit).placeholder_text)])
	elif c is TabBar:
		var tb := c as TabBar
		for i in tb.tab_count:
			out.append(["tab", _shown(c, tb.get_tab_title(i))])
	elif c is ItemList:
		var il := c as ItemList
		for i in il.item_count:
			out.append(["item", _shown(c, il.get_item_text(i))])
	if c.tooltip_text != "":
		out.append(["tooltip", _shown(c, c.tooltip_text)])
	return out


## The text as the player sees it: controls translate their own text (a
## scene label whose text is a key shows the translation), so the lint reads
## the translated string, not the stored one.
static func _shown(c: Control, text: String) -> String:
	return c.atr(text) if c.can_auto_translate() else text


## A file path shown in a line edit (the export folder) is data, not prose.
static func _is_path(entry: Array) -> bool:
	var t := str(entry[1])
	return str(entry[0]) == "edit" and (t.begins_with("/") or t.begins_with("~") or (t.length() > 2 and t[1] == ":"))


# ── Tree helpers ──────────────────────────────────────────────────────────────

static func _visible_controls(root: Node) -> Array[Control]:
	var out: Array[Control] = []
	_collect_visible(root, out)
	return out


static func _collect_visible(n: Node, out: Array[Control]) -> void:
	if n is CanvasLayer and not (n as CanvasLayer).visible:
		return
	if n is CanvasItem and not (n as CanvasItem).visible:
		return
	if n is Control:
		var c := n as Control
		if c.size.x >= 1.0 and c.size.y >= 1.0:
			out.append(c)
	for child in n.get_children():
		_collect_visible(child, out)


static func _re(pattern: String) -> RegEx:
	if not _re_cache.has(pattern):
		var r := RegEx.new()
		r.compile(pattern)
		_re_cache[pattern] = r
	return _re_cache[pattern]


static func _path(c: Node) -> String:
	var parts := PackedStringArray()
	var n: Node = c
	var depth := 0
	while n != null and depth < 4:
		parts.insert(0, String(n.name))
		n = n.get_parent()
		depth += 1
	return "/".join(parts)
