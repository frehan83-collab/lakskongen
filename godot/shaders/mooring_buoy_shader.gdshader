shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Mooring grid buoy (blender/build_site_kit.py). The body colour comes from
// the MultiMesh instance colour (grey or orange, already linear), so every
// buoy of every site shares this one material. A wet, algae-darkened band
// rides at the waterline like on the pen collar.
uniform vec3 base_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.55;
uniform vec3 waterline_color : source_color = vec3(0.10, 0.12, 0.09);
uniform float waterline_height = 0.06;
uniform float waterline_blend = 0.12;
uniform float wet_roughness : hint_range(0.0, 1.0) = 0.3;

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

void fragment() {
	float wet = 1.0 - smoothstep(waterline_height - waterline_blend, waterline_height + waterline_blend, model_y);
	vec3 body = base_color * COLOR.rgb;
	ALBEDO = mix(body, waterline_color, wet * 0.75);
	ROUGHNESS = mix(roughness, wet_roughness, wet);
}
