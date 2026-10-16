class_name GradeBar
extends Control

## A harvest's quality grades as one stacked bar (roadmap U2-05): superior
## (ok), ordinary (warn) and production (crit) shares of the gutted kilos,
## each segment exactly its share of the width. Colour plus the legend text
## beside it carry the meaning (never colour alone).

## [superior, ordinary, production], fractions of the gutted kilos.
var shares: Array = [1.0, 0.0, 0.0]:
	set(v):
		shares = v
		queue_redraw()

const HEIGHT := 12.0
const RADIUS := 3


func _ready() -> void:
	custom_minimum_size = Vector2(160.0, HEIGHT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	resized.connect(queue_redraw)


## Width in pixels of segment `i` (0 superior, 1 ordinary, 2 production).
func segment_width(i: int) -> float:
	var total := 0.0
	for s in shares:
		total += maxf(0.0, float(s))
	if total <= 0.0 or i < 0 or i >= shares.size():
		return 0.0
	return size.x * maxf(0.0, float(shares[i])) / total


func _draw() -> void:
	var y := (size.y - HEIGHT) * 0.5
	var track := StyleBoxFlat.new()
	track.bg_color = Color(get_theme_color(&"hairline", &"Palette"), 0.7)
	track.set_corner_radius_all(RADIUS)
	draw_style_box(track, Rect2(0.0, y, size.x, HEIGHT))
	var tones := [&"ok", &"warn", &"crit"]
	var x := 0.0
	for i in mini(shares.size(), tones.size()):
		var w := segment_width(i)
		if w <= 0.0:
			continue
		draw_rect(Rect2(x, y, w, HEIGHT), get_theme_color(tones[i], &"Palette"))
		x += w
