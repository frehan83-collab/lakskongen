shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 albedo_color : source_color = vec3(0.9, 0.6, 0.2);
uniform vec3 emission_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.3;
uniform float roughness : hint_range(0.0, 1.0) = 0.4;
// Per-pen lice/health tint (instance uniform: shared material stays shared).
// FishPenManager sets this from the sim payload; (1,1,1,1) = neutral.
instance uniform vec4 lice_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	ALBEDO = albedo_color * lice_tint.rgb;
	METALLIC = metallic;
	ROUGHNESS = roughness;
	// Critical pens glow faintly red so trouble is visible from COAST zoom.
	EMISSION = emission_color + vec3(0.6, 0.05, 0.05) * (1.0 - lice_tint.g) * 0.35;
}