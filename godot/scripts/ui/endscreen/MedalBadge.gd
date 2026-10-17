class_name MedalBadge
extends Control

## MedalBadge — a campaign medal drawn in the UI's own shapes (roadmap U2-03):
## a ribbon in two chevrons and a struck disc with a raised rim and three
## stars for the distinctions it counts (filled when met). "none" draws an
## empty, hairline disc. No glyphs or emoji (D14); the colours are the
## medals' metals, picked to read on Ink glass.

## "none" | "bronze" | "silver" | "gold".
var medal: String = "none":
	set(v):
		medal = v
		queue_redraw()
## Distinctions met (0-3): the filled stars.
var stars: int = 0:
	set(v):
		stars = clampi(v, 0, 3)
		queue_redraw()

const METALS := {
	"bronze": [Color("#C98A55"), Color("#8A5530")],
	"silver": [Color("#D5DEE2"), Color("#8E9EA6")],
	"gold": [Color("#F2C14E"), Color("#B9851A")],
}
const RIBBON := Color("#F2876A")
const RIBBON_DARK := Color("#B5452B")
const SIZE := Vector2(72, 88)


func _ready() -> void:
	custom_minimum_size = SIZE
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _draw() -> void:
	var w := size.x
	var r := minf(w, size.y - 18.0) * 0.42
	var centre := Vector2(w * 0.5, size.y - r - 2.0)
	var won := METALS.has(medal)
	if won:
		# Two ribbon tails above the disc.
		for side: float in [-1.0, 1.0]:
			var x := centre.x + side * r * 0.42
			var tail := PackedVector2Array([Vector2(x - r * 0.28, 0.0), Vector2(x + r * 0.28, 0.0),
				Vector2(x + r * 0.28 - side * r * 0.12, centre.y - r * 0.6), Vector2(x - r * 0.28 - side * r * 0.12, centre.y - r * 0.6)])
			draw_colored_polygon(tail, RIBBON if side < 0.0 else RIBBON_DARK)
	var face: Color = METALS[medal][0] if won else Color(0, 0, 0, 0)
	var rim: Color = METALS[medal][1] if won else get_theme_color(&"stroke", &"Palette")
	if won:
		draw_circle(centre, r, rim)
		draw_circle(centre, r * 0.84, face)
		draw_arc(centre, r * 0.70, 0.0, TAU, 48, Color(rim, 0.55), 1.5, true)
	else:
		draw_arc(centre, r, 0.0, TAU, 48, rim, 1.5, true)
	# Three stars along the disc's middle: the distinctions.
	for i in 3:
		var at := centre + Vector2((float(i) - 1.0) * r * 0.46, 0.0)
		var lit := i < stars
		var tone: Color = (METALS[medal][1] if won else rim) if lit else Color(rim, 0.35)
		_star(at, r * 0.2, tone, lit)


func _star(at: Vector2, radius: float, colour: Color, filled: bool) -> void:
	var pts := PackedVector2Array()
	for k in 10:
		var a := -PI * 0.5 + k * PI / 5.0
		pts.append(at + Vector2(cos(a), sin(a)) * (radius if k % 2 == 0 else radius * 0.45))
	if filled:
		draw_colored_polygon(pts, colour)
	else:
		pts.append(pts[0])
		draw_polyline(pts, colour, 1.2, true)
