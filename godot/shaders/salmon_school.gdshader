shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The pen's salmon as solid fish (roadmap G3-01): the countershaded skin of
// salmon_skin.gdshader on a school that swims on the GPU
// (fish_school.gdshaderinc: per-fish data in the MultiMesh custom data,
// written once; the pen's clocks and state in instance uniforms). The opaque
// water hides every fish under it from above, so from above this pass draws
// only the jumpers in the air (FishSchool shows it just those); under the
// water it draws the whole school at its true size. fish_shoal.gdshader is
// the school seen through the surface.
#include "res://shaders/fish_school.gdshaderinc"

uniform sampler2D skin : source_color, hint_default_white, filter_linear_mipmap_anisotropic;
uniform float metallic_low : hint_range(0.0, 1.0) = 0.15;
uniform float metallic_high : hint_range(0.0, 1.0) = 0.85;
uniform float roughness_low : hint_range(0.0, 1.0) = 0.26;
uniform float roughness_high : hint_range(0.0, 1.0) = 0.55;
// A fish whose middle is deeper than this (m) cannot be seen from above.
uniform float hidden_below = 0.6;

void vertex() {
	vec3 camera = CAMERA_POSITION_WORLD;
	bool above = camera.y > 0.0;
	int id = INSTANCE_ID;
	vec3 pos;
	vec3 fwd;
	float roll;
	float flash;
	fish_pose(INSTANCE_CUSTOM, id, pos, fwd, roll, flash);
	bool shown = float(id) < school_body.w && !(above && pos.y < -hidden_below);
	if (!shown) {
		VERTEX = vec3(0.0);
	} else {
		vec3 centre = (MODEL_MATRIX * vec4(pos, 1.0)).xyz;
		float scale = fish_scale(centre, camera, abs(PROJECTION_MATRIX[1][1]), VIEWPORT_SIZE.y, above);
		mat3 frame = fish_basis(fwd);
		VERTEX = pos + frame * fish_body_vertex(VERTEX, INSTANCE_CUSTOM.w, roll, scale);
		float c = cos(roll);
		float s = sin(roll);
		vec3 n = vec3(c * NORMAL.x - s * NORMAL.y, s * NORMAL.x + c * NORMAL.y, NORMAL.z);
		vec3 t = vec3(c * TANGENT.x - s * TANGENT.y, s * TANGENT.x + c * TANGENT.y, TANGENT.z);
		NORMAL = frame * n;
		TANGENT = frame * t;
	}
}

void fragment() {
	vec4 c = texture(skin, UV);
	ALBEDO = c.rgb;
	METALLIC = mix(metallic_low, metallic_high, c.a);
	ROUGHNESS = mix(roughness_high, roughness_low, c.a);
	SPECULAR = 0.6;
}
