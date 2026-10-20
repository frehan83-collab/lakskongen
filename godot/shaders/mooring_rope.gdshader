shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/site_float.gdshaderinc"

// Mooring lines (MooringGrid: one unit cylinder per line in a MultiMesh,
// y -0.5 at its first end, +0.5 at its second). A line end held by a float
// rides the water with it: INSTANCE_CUSTOM.x and .y weight the water's
// heave at the first and second end (1 for a chain's buoy end or a bridle's
// pen end, 0 for an end on the submerged frame or an anchor), blended
// along the line.
uniform vec3 albedo_color : source_color = vec3(0.168627, 0.188235, 0.2);
uniform float roughness : hint_range(0.0, 1.0) = 0.9;

void vertex() {
	float w = mix(INSTANCE_CUSTOM.x, INSTANCE_CUSTOM.y, clamp(VERTEX.y + 0.5, 0.0, 1.0));
	if (w > 0.0) {
		vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec3 lift = vec3(0.0, w * float_height(world.xz), 0.0);
		VERTEX += inverse(mat3(MODEL_MATRIX)) * lift;
	}
}

void fragment() {
	ALBEDO = albedo_color;
	ROUGHNESS = roughness;
}
