class_name SizeClassChart
extends Control

## The weight histogram of a harvest (roadmap U2-05): gutted tonnes per size
## class (<1 .. 7+ kg HOG), each bar coloured by what the class is paid
## against the 4-5 kg reference (warn below 90 %, info up to par, ok at or
## above), its tonnes above it and its label and price factor under it; the
## reference class is outlined. Pure _draw on the theme's Palette, so it reads
## on Chart paper and on Ink glass alike.

## Gutted kilos per size class.
var hog_kg: Array = []:
	set(v):
		hog_kg = v
		queue_redraw()
## Size-class labels ("<1 kg" .. "7+ kg").
var labels: Array = []:
	set(v):
		labels = v
		queue_redraw()
## Price of each class relative to the reference (1 = 4-5 kg).
var factors: Array = []:
	set(v):
		factors = v
		queue_redraw()
## Index of the reference class (outlined).
var reference_class: int = 4:
	set(v):
		reference_class = v
		queue_redraw()
## How far the bars have grown in (0..1): the report raises them class by
## class, light to heavy, as its payoff beat; 1 draws them whole. Only the
## drawing follows it: bar_height() is always the full bar.
var grow: float = 1.0:
	set(v):
		grow = clampf(v, 0.0, 1.0)
		queue_redraw()

const PAD_TOP := 20.0
const LABEL_ROWS := 2
const LABEL_FONT_SIZE := 13
const VALUE_FONT_SIZE := 13
const ROW_GAP := 3.0
const BAR_GAP := 0.28 # share of a slot left empty between bars
const MIN_BAR_PX := 2.0
## Below this share of the reference price a class reads as a discount (warn).
const DISCOUNT_FACTOR := 0.9
## Grow-in: each class starts this share of the run after the one before it.
const GROW_STAGGER := 0.1


func _ready() -> void:
	custom_minimum_size = Vector2(420, 190)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	resized.connect(queue_redraw)


## Fill the chart from a quote or report dictionary and the planner options.
func show_classes(classes: Array, class_labels: Array, class_factors: Array, ref: int) -> void:
	labels = class_labels
	factors = class_factors
	reference_class = ref
	hog_kg = classes


## The bar colour of a class priced at `factor` of the reference.
func tone_for(factor: float) -> Color:
	if factor < DISCOUNT_FACTOR:
		return _pal(&"warn")
	if factor < 0.999:
		return _pal(&"info")
	return _pal(&"ok")


## Height in pixels of class `i`'s bar (tests compare the bars' proportions).
func bar_height(i: int) -> float:
	var top := _max_kg()
	if top <= 0.0 or i < 0 or i >= hog_kg.size():
		return 0.0
	var kg := float(hog_kg[i])
	return 0.0 if kg <= 0.0 else maxf(MIN_BAR_PX, _plot_height() * kg / top)


## How much of class `i`'s bar (of `n`) is drawn at the current `grow`:
## the classes start one after another and each eases out to full height.
func grown(i: int, n: int) -> float:
	if grow >= 1.0:
		return 1.0
	var run := 1.0 + GROW_STAGGER * float(maxi(0, n - 1))
	var t := clampf(grow * run - GROW_STAGGER * float(i), 0.0, 1.0)
	return 1.0 - pow(1.0 - t, 3.0)


func _max_kg() -> float:
	var top := 0.0
	for v in hog_kg:
		top = maxf(top, float(v))
	return top


func _label_band() -> float:
	return LABEL_ROWS * (LABEL_FONT_SIZE + ROW_GAP) + 6.0


func _plot_height() -> float:
	return maxf(0.0, size.y - PAD_TOP - _label_band())


func _draw() -> void:
	var n := hog_kg.size()
	if n == 0 or size.x <= 0.0:
		return
	var font := get_theme_default_font()
	var slot := size.x / float(n)
	var bar_w := slot * (1.0 - BAR_GAP)
	var base_y := PAD_TOP + _plot_height()
	draw_line(Vector2(0.0, base_y + 0.5), Vector2(size.x, base_y + 0.5), _pal(&"stroke"), 1.0)
	for i in n:
		var x := slot * i + (slot - bar_w) * 0.5
		var shown := grown(i, n)
		var h := bar_height(i) * shown
		var factor := float(factors[i]) if i < factors.size() else 1.0
		var kg := float(hog_kg[i])
		if h > 0.0:
			draw_rect(Rect2(x, base_y - h, bar_w, h), tone_for(factor))
		if i == reference_class:
			draw_rect(Rect2(x - 2.0, PAD_TOP - 2.0, bar_w + 4.0, _plot_height() + 2.0), _pal(&"focus"), false, 1.5)
		if kg > 0.0 and shown > 0.0:
			var value := SimFormatters.num(kg / 1000.0, 1 if kg < 10_000.0 else 0) + " t"
			_center_text(font, value, x + bar_w * 0.5, base_y - h - 5.0, VALUE_FONT_SIZE, Color(_pal(&"text"), shown))
		var label := str(labels[i]) if i < labels.size() else ""
		_center_text(font, label, x + bar_w * 0.5, base_y + LABEL_FONT_SIZE + 4.0, LABEL_FONT_SIZE, _pal(&"text"))
		_center_text(font, "×" + SimFormatters.num(factor, 2), x + bar_w * 0.5,
			base_y + 2.0 * LABEL_FONT_SIZE + ROW_GAP + 4.0, LABEL_FONT_SIZE, _pal(&"muted"))


func _center_text(font: Font, text: String, cx: float, baseline: float, font_size: int, colour: Color) -> void:
	var w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
	draw_string(font, Vector2(cx - w * 0.5, baseline), text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, colour)


func _pal(key: StringName) -> Color:
	return get_theme_color(key, &"Palette")
