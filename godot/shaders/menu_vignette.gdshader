shader_type canvas_item;

// Cinematic grade over the main menu's live fjord (roadmap U1-09): a soft
// Ink vignette in the corners, a heavier one along the bottom, and an Ink
// shade that runs out from under the menu column and fades over
// `column_fade` of the screen, so the glass edge never cuts the world in
// two. Pure alpha over the 3D frame: no screen read, a few ALU per pixel.

uniform vec4 ink : source_color = vec4(0.027, 0.122, 0.169, 1.0);
uniform float corner_strength : hint_range(0.0, 1.0) = 0.38;
uniform float bottom_strength : hint_range(0.0, 1.0) = 0.30;
uniform float column_shade : hint_range(0.0, 1.0) = 0.42;
// The column's right edge and the fade beyond it, in UV (set from script).
uniform float column_end : hint_range(0.0, 1.0) = 0.24;
uniform float column_fade : hint_range(0.01, 1.0) = 0.16;

void fragment() {
	vec2 c = UV - vec2(0.55, 0.45);
	float corner = smoothstep(0.38, 0.95, length(c * vec2(1.0, 1.3))) * corner_strength;
	float bottom = smoothstep(0.62, 1.0, UV.y) * bottom_strength;
	float column = (1.0 - smoothstep(column_end, column_end + column_fade, UV.x)) * column_shade;
	float a = 1.0 - (1.0 - corner) * (1.0 - bottom) * (1.0 - column);
	COLOR = vec4(ink.rgb, clamp(a, 0.0, 0.85));
}
