shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/site_float.gdshaderinc"
#include "res://shaders/light_glow.gdshaderinc"

// Halo of a mooring corner light: a camera-facing glow (MultiMesh, one per
// lamp) that never shrinks below `min_pixels` on screen, so the site's
// synchronised flashes read from FARM to COAST zoom, and its glitter on the
// water at night (light_glow.gdshaderinc). Same flash timing as
// mooring_lamp_shader.gdshader (global TIME: every lamp flashes together).
// It rides the water with its lamp (site_float.gdshaderinc).
//
// The lamp is on a photocell (`night`, SiteLights.sync): by day there is no
// halo. At night the eye's exposure would multiply the halo into a bloom
// that swallows the site; `exposure_comp` (1 / exposure) keeps it at the
// screen brightness it is tuned to: a tight core just over white, a small
// soft halo. The distance fog dims it like the water round it.
uniform vec3 glow_color : source_color = vec3(1.0, 0.761, 0.227);
uniform float size_m = 1.4;
uniform float min_pixels = 8.0;
uniform float period_s = 4.0;
uniform float flash_s = 0.5;
// Screen brightness of the core during a flash, and between flashes.
uniform float intensity : hint_range(0.0, 4.0) = 2.2;
uniform float idle : hint_range(0.0, 1.0) = 0.12;
// The float the gear belongs to is this wide (m): the grid buoy.
uniform float gear_size_m = 1.3;
uniform float night : hint_range(0.0, 1.0) = 1.0;
uniform float exposure_comp = 1.0;
uniform float fog_density = 0.0;

varying float v_dist;
varying flat float v_part;
varying float v_seed;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 pivot = vec3(origin.x, 0.0, origin.z);
	vec3 sea = float_surface(origin.xz);
	vec2 slope = sea.yz;
	float gs = float_screen_scale(gear_size_m, float_m_per_px(length(CAMERA_POSITION_WORLD - pivot), PROJECTION_MATRIX[1][1], VIEWPORT_SIZE.y));
	vec3 lamp = float_rigid(origin, pivot, slope, sea.x, gs);
	v_part = UV2.x;
	v_seed = fract(origin.x * 0.137 + origin.z * 0.071) * 6.28;
	float dist;
	if (UV2.x < 0.5) {
		POSITION = glow_billboard(lamp, VERTEX.xy, size_m, min_pixels, VIEW_MATRIX, PROJECTION_MATRIX, VIEWPORT_SIZE.y, dist);
	} else {
		vec3 strip = glow_reflection(lamp, sea.x, UV, CAMERA_POSITION_WORLD, abs(PROJECTION_MATRIX[1][1]), VIEWPORT_SIZE.y);
		POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(strip, 1.0);
		dist = length(CAMERA_POSITION_WORLD - strip);
	}
	v_dist = dist;
}

void fragment() {
	float t = mod(TIME, period_s);
	float on = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(flash_s, flash_s + 0.08, t));
	float a = v_part < 0.5 ? glow_cover(UV) : reflection_cover(UV, TIME, v_seed);
	float fog = exp(-fog_density * v_dist);
	ALBEDO = glow_color * intensity * exposure_comp * fog;
	ALPHA = clamp(a * mix(idle, 1.0, on) * night, 0.0, 1.0);
}
