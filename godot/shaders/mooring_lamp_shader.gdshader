shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Corner light lens (#FFC23A). Every lamp reads the same global TIME, so
// all the site's corner lights flash in sync, as aquaculture marking
// requires: one short flash every `period_s` seconds (Fl Y 4s by default).
uniform vec3 lamp_color : source_color = vec3(1.0, 0.761, 0.227);
uniform float period_s = 4.0;
uniform float flash_s = 0.5;
uniform float emission_on = 8.0;
uniform float emission_off = 0.2;

void fragment() {
	float t = mod(TIME, period_s);
	float on = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(flash_s, flash_s + 0.08, t));
	ALBEDO = lamp_color * 0.7;
	ROUGHNESS = 0.25;
	EMISSION = lamp_color * mix(emission_off, emission_on, on);
}
