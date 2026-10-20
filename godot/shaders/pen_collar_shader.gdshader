shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Black HDPE (D13 "Vestland Light": #1C1E20, roughness 0.6) for the pen's
// floating collar, brackets, stanchions and handrail. The collar is
// equipment, not a gameplay readout: site health lives on the pen's signal
// ring (pen_signal_ring.gdshader), never in this albedo (roadmap G1-07).
uniform vec3 albedo_color : source_color = vec3(0.110, 0.118, 0.125);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.6;
// Waterline band: wet, slightly greener plastic where waves wash the pipes.
// Heights are in the model's own space (the pen's y = 0 is the waterline),
// so the band stays put when the pen later heaves with the waves.
uniform vec3 waterline_color : source_color = vec3(0.075, 0.090, 0.078);
uniform float waterline_height = 0.08;
uniform float waterline_blend = 0.10;
uniform float wet_roughness : hint_range(0.0, 1.0) = 0.32;
// Baked ambient occlusion in the vertex colour (Blender bake, A2-01). Meshes
// without vertex colours read white, which means "no occlusion".
uniform float ao_light_affect : hint_range(0.0, 1.0) = 0.35;

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

void fragment() {
	float wet = 1.0 - smoothstep(waterline_height - waterline_blend, waterline_height + waterline_blend, model_y);
	ALBEDO = mix(albedo_color, waterline_color, wet * 0.8);
	METALLIC = metallic;
	ROUGHNESS = mix(roughness, wet_roughness, wet);
	AO = COLOR.r;
	AO_LIGHT_AFFECT = ao_light_affect;
}
