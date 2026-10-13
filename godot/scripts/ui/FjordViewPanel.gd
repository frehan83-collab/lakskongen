extends Control

## "Fjord" tab: an empty, see-through panel over the 3D vista. While it is the
## active tab GameUI makes its own containers mouse-transparent too, so clicks
## reach the pens (pen details, selection), right/middle-drag orbits the
## camera and the camera hint's "Fly to pen" button can be pressed. Every
## other tab covers the vista and keeps the clicks.

func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
