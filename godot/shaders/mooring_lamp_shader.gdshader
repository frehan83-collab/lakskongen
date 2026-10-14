shader_type spatial;
render_mode world_vertex_coords, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/site_float.gdshaderinc"

// Corner light lens (#FFC23A). Every lamp reads the same global TIME, so
// all the site's corner lights flash in sync, as aquaculture marking
// requires: one short flash every `period_s` seconds (Fl Y 4s by default).
// The light sits on its corner buoy and rides the water with it
// (site_float.gdshaderinc, the buoy's own heave and tilt).
uniform vec3 lamp_color : source_color = vec3(1.0, 0.761, 0.227);
uniform float period_s = 4.0;
uniform float flash_s = 0.5;
uniform float emission_on = 8.0;
uniform float emission_off = 0.2;
// The float the gear belongs to is this wide (m): the grid buoy.
uniform float gear_size_m = 1.3;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 pivot = vec3(origin.x, 0.0, origin.z);
	vec3 sea = float_surface(origin.xz);
	vec2 slope = sea.yz;
	float s = float_screen_scale(gear_size_m, float_m_per_px(length(CAMERA_POSITION_WORLD - pivot), PROJECTION_MATRIX[1][1], VIEWPORT_SIZE.y));
	VERTEX = float_rigid(VERTEX, pivot, slope, sea.x, s);
	NORMAL = float_rigid_dir(NORMAL, slope);
}

void fragment() {
	float t = mod(TIME, period_s);
	float on = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(flash_s, flash_s + 0.08, t));
	ALBEDO = lamp_color * 0.7;
	ROUGHNESS = 0.25;
	EMISSION = lamp_color * mix(emission_off, emission_on, on);
}
