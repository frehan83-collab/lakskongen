shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/light_glow.gdshaderinc"

// A land campus's yard and work lamps after dark (roadmap F2-23, farm-types
// spec §5.4: the lid work-lights and the hall windows light at every dusk and
// night; CampusLights.gd). One camera-facing glow per lamp (a MultiMesh of
// quads, the lamp at each instance's origin) that never shrinks below
// `min_pixels` on screen, so a lit campus reads as points of light from the
// land framing out to the home view. Steady (a yard lamp does not flash).
//
// The level (`night`) is the presenter's dusk light level; the eye's
// exposure (SiteLights.exposure, up to 16x at night) would bloom the glow
// into a blot, so `exposure_comp` keeps its tuned screen brightness, as the
// site's corner lights do. The distance fog dims it like the land round it.
// The instance colour's alpha switches a lamp off (its part not built yet)
// red scales its brightness and green its size (a tank's work-lights are
// smaller than a mast's).
// A first-sight sequence lights them in a wave from the quay
// (light_wave_*, as the kit's windows and lids).

uniform vec3 glow_color : source_color = vec3(1.0, 0.82, 0.58);
uniform float size_m = 2.4;
uniform float min_pixels = 5.0;
uniform float intensity : hint_range(0.0, 4.0) = 1.5;
uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform float exposure_comp = 1.0;
uniform float fog_density = 0.0;
uniform vec2 light_wave_origin = vec2(0.0);
uniform float light_wave_radius = 10000000.0;
uniform float light_wave_width = 70.0;

varying float v_dist;
varying float v_on;
varying float v_gain;

void vertex() {
	vec3 lamp = MODEL_MATRIX[3].xyz;
	float dist;
	POSITION = glow_billboard(lamp, VERTEX.xy, size_m * COLOR.g, min_pixels * COLOR.g, VIEW_MATRIX, PROJECTION_MATRIX, VIEWPORT_SIZE.y, dist);
	v_dist = dist;
	v_on = COLOR.a * (1.0 - smoothstep(light_wave_radius - light_wave_width, light_wave_radius, distance(lamp.xz, light_wave_origin)));
	v_gain = COLOR.r;
}

void fragment() {
	float a = glow_cover(UV);
	float fog = exp(-fog_density * v_dist);
	ALBEDO = glow_color * intensity * v_gain * exposure_comp * fog;
	ALPHA = clamp(a * night * v_on, 0.0, 1.0);
}
