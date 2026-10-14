shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Atlantic salmon skin for the Blender salmon (blender/build_salmon.py,
// roadmap G3-01 asset): a countershaded atlas, dark steel-blue back with
// black spots, mirror-silver flanks, white belly. RGB is the albedo; A is
// the silver mask, which drives metallic and smoothness (silver scales are
// a mirror; the back and the fins are not).
//
// A gentle swim wave bends the body in the vertex shader (model space:
// the nose points along -Z, the fork is at z = +fork_length / 2), so a
// school animated on the CPU still swims; the per-fish phase comes from
// the MultiMesh instance id. G3-01 drives these uniforms from the sim.
uniform sampler2D skin : source_color, hint_default_white, filter_linear_mipmap_anisotropic;
uniform vec3 tint : source_color = vec3(1.0);
uniform float metallic_low : hint_range(0.0, 1.0) = 0.15;
uniform float metallic_high : hint_range(0.0, 1.0) = 0.85;
uniform float roughness_low : hint_range(0.0, 1.0) = 0.26;
uniform float roughness_high : hint_range(0.0, 1.0) = 0.55;
uniform float fork_length = 0.9;
// Tail sweep at the fork, in metres of the unit (0.9 m) fish.
uniform float swim_amplitude = 0.05;
uniform float swim_hz = 1.4;
// Body wavelength in metres of the unit fish (about one body length).
uniform float swim_wavelength = 0.95;

void vertex() {
	// 0 at the snout, 1 at the fork.
	float s = clamp(VERTEX.z / fork_length + 0.5, 0.0, 1.1);
	float phase = fract(sin(float(INSTANCE_ID) * 12.9898) * 43758.5453) * TAU;
	float k = TAU / swim_wavelength;
	float w = TAU * swim_hz * TIME + phase - k * VERTEX.z;
	// The head barely moves; the sweep grows towards the tail.
	float env = swim_amplitude * (0.12 + s * s);
	VERTEX.x += env * sin(w);
	// Turn the normal with the local slope of the bend.
	float slope = swim_amplitude * (2.0 * s / fork_length * sin(w)) - env * k * cos(w);
	NORMAL = normalize(vec3(NORMAL.x - NORMAL.z * slope, NORMAL.y, NORMAL.z + NORMAL.x * slope));
}

void fragment() {
	vec4 c = texture(skin, UV);
	ALBEDO = c.rgb * tint;
	METALLIC = mix(metallic_low, metallic_high, c.a);
	ROUGHNESS = mix(roughness_high, roughness_low, c.a);
	SPECULAR = 0.6;
}
