shader_type spatial;
render_mode world_vertex_coords, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/site_float.gdshaderinc"

// Mooring grid buoy and hose float (blender/build_site_kit.py). The body
// colour comes from the MultiMesh instance colour (buoy yellow, grey or
// orange, already linear), so every buoy of every site shares this one
// material. A wet, algae-darkened band rides at the waterline like on the
// pen collar. Each float rides the drawn water (site_float.gdshaderinc): it
// heaves with the surface at its centre and tilts with the slope across it.
uniform vec3 base_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.55;
uniform vec3 waterline_color : source_color = vec3(0.10, 0.12, 0.09);
uniform float waterline_height = 0.06;
uniform float waterline_blend = 0.12;
uniform float wet_roughness : hint_range(0.0, 1.0) = 0.3;
// Half the span the float's tilt is measured over (m).
// The float the gear belongs to is this wide (m): the grid buoy.
uniform float gear_size_m = 1.3;

varying float model_y;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	model_y = VERTEX.y - origin.y;
	vec3 pivot = vec3(origin.x, 0.0, origin.z);
	vec3 sea = float_surface(origin.xz);
	float heave = sea.x;
	vec2 slope = sea.yz;
	float s = float_screen_scale(gear_size_m, float_m_per_px(length(CAMERA_POSITION_WORLD - pivot), PROJECTION_MATRIX[1][1], VIEWPORT_SIZE.y));
	VERTEX = float_rigid(VERTEX, pivot, slope, heave, s);
	NORMAL = float_rigid_dir(NORMAL, slope);
}

void fragment() {
	float wet = 1.0 - smoothstep(waterline_height - waterline_blend, waterline_height + waterline_blend, model_y);
	vec3 body = base_color * COLOR.rgb;
	ALBEDO = mix(body, waterline_color, wet * 0.75);
	ROUGHNESS = mix(roughness, wet_roughness, wet);
}
