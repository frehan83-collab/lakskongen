shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 albedo_color : source_color = vec3(0.9, 0.6, 0.2);
uniform vec3 emission_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.3;
uniform float roughness : hint_range(0.0, 1.0) = 0.4;
// Per-pen lice/health tint. FishPenManager duplicates this material per pen
// and drives this uniform (S1: instance uniforms did not render on the QA
// setup — per-pen material instances are the boring, verified path).
uniform vec4 lice_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	// The collar is the site-health readout: the sim tint (green/amber/red)
	// colors the ring directly — a multiply against the warm base albedo was
	// too low-contrast to read in QA frames (S1 forced-state evidence, session 1).
	ALBEDO = mix(albedo_color, lice_tint.rgb, 0.85);
	METALLIC = metallic;
	ROUGHNESS = roughness;
	// Warning/critical pens glow faintly so trouble reads from COAST zoom.
	// (lice_tint.g drops as severity rises: 0.75 healthy, 0.3 critical.)
	EMISSION = emission_color + lice_tint.rgb * max(0.0, 0.5 - lice_tint.g) * 0.5;
}