class_name ShareBar
extends Control

## A share of a whole as a bar (roadmap U1-05): a track the width of the
## control and a fill exactly `share` of it. The "Hvorfor gikk det slik?"
## bars sat in a PanelContainer that stretched every fill to full width, so
## a 120 k cost and a 182 k cost drew the same bar.

## Fraction of the whole, 0-1.
var share: float = 0.0:
	set(v):
		share = clampf(v, 0.0, 1.0)
		queue_redraw()
## Fill colour (a Palette tone on the surface the bar sits on).
var fill_color: Color = Color("#1F5E73"):
	set(v):
		fill_color = v
		queue_redraw()

const HEIGHT := 8.0
const RADIUS := 3.0


func _ready() -> void:
	custom_minimum_size = Vector2(60.0, HEIGHT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	resized.connect(queue_redraw)


## The drawn fill's width in pixels (tests compare it with share x width).
func fill_width() -> float:
	return size.x * share


func _draw() -> void:
	var y := (size.y - HEIGHT) * 0.5
	var track := Color(get_theme_color(&"hairline", &"Palette"), 0.7)
	draw_style_box(_box(track), Rect2(0.0, y, size.x, HEIGHT))
	if share > 0.0:
		draw_style_box(_box(fill_color), Rect2(0.0, y, maxf(fill_width(), 1.0), HEIGHT))


static func _box(colour: Color) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = colour
	sb.set_corner_radius_all(int(RADIUS))
	return sb
