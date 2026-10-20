shader_type canvas_item;

// The national map's production-area tint (geo stages 2-3 plan, Track C;
// GEODATA §7.3). Drawn on the TextureRect of basemap/national.jpg: the raw
// po_index (an 8-bit grey ImageTexture of the same size and bounds, one
// production area id 1-13 per open-sea pixel, 0 elsewhere) picks each
// area's GeoJSON tint, mixed over the base map at about 35 % on index > 0.
// The hovered area is lifted, the selected one lifted more, and the image's
// edges fade into the deep-water frame around the map.
// The CPU reads the same raw image for hit-testing (GeoMapView.area_at_local),
// so what the player sees tinted is what a click selects.

uniform sampler2D po_index : filter_nearest, repeat_disable;
// Index 0 is unused (no area); 1-13 are the areas' tints.
uniform vec4 tints[14];
uniform float tint_strength : hint_range(0.0, 1.0) = 0.35;
uniform int hovered_id = 0;
uniform int selected_id = 0;
uniform vec4 frame_color : source_color = vec4(0.055, 0.165, 0.2, 1.0);
// Width of the fade into the frame, as a fraction of the image.
uniform float edge_fade : hint_range(0.0, 0.2) = 0.05;

int area_at(vec2 uv) {
	return int(round(texture(po_index, uv).r * 255.0));
}

void fragment() {
	vec4 base = texture(TEXTURE, UV);
	vec3 col = base.rgb;
	int id = area_at(UV);
	if (id > 0 && id < 14) {
		float s = tint_strength;
		if (id == selected_id) {
			s += 0.3;
		} else if (id == hovered_id) {
			s += 0.12;
		}
		col = mix(col, tints[id].rgb, clamp(s * tints[id].a, 0.0, 1.0));
	}
	vec2 d = min(UV, vec2(1.0) - UV);
	float e = smoothstep(0.0, edge_fade, min(d.x, d.y));
	col = mix(frame_color.rgb, col, e);
	COLOR = vec4(col, base.a);
}
