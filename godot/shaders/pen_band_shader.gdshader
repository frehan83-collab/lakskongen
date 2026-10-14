shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Owner colour band on a pen (roadmap G1-07): rival farms use the same pen
// assets as the player and differ only by this painted band, so no rival
// pen needs a material override. One shared material; the colour is a
// per-instance parameter (PenCage.set_owner_band).
instance uniform vec4 band_color : source_color = vec4(0.910, 0.725, 0.165, 1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform float ao_light_affect : hint_range(0.0, 1.0) = 0.35;

void fragment() {
	ALBEDO = band_color.rgb;
	ROUGHNESS = roughness;
	AO = COLOR.r;
	AO_LIGHT_AFFECT = ao_light_affect;
}
