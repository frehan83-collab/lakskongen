shader_type spatial;
render_mode world_vertex_coords, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/site_float.gdshaderinc"

// The plain parts of the floating site gear (the buoys' galvanised eyes,
// the corner lights' HDPE mast and base): flat PBR that rides the drawn
// water with its buoy (site_float.gdshaderinc, same heave and tilt as
// mooring_buoy_shader.gdshader).
uniform vec3 albedo_color : source_color = vec3(0.5, 0.5, 0.5);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
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
	ALBEDO = albedo_color;
	METALLIC = metallic;
	ROUGHNESS = roughness;
}
