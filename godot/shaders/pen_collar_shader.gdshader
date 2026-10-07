shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 albedo_color : source_color = vec3(0.9, 0.6, 0.2);
uniform vec3 emission_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.3;
uniform float roughness : hint_range(0.0, 1.0) = 0.4;

void fragment() {
	ALBEDO = albedo_color;
	METALLIC = metallic;
	ROUGHNESS = roughness;
	EMISSION = emission_color;
}