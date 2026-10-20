shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 albedo_color : source_color = vec3(0.5, 0.5, 0.5);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.5;

void fragment() {
	ALBEDO = albedo_color;
	METALLIC = metallic;
	ROUGHNESS = roughness;
}