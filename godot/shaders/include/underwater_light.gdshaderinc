// Light under the surface for the things that hang in the water (roadmap
// G3-04): the pen's net and collar, the fish. EnvironmentDirector writes the
// globals (project.godot [shader_globals]) while the camera is under the
// surface; above it lk_uw.x is 0 and nothing here changes a pixel.
//
// - The scene's sun under water carries the sunlight at the camera's depth
//   (filtered by the water over it). A surface deeper than the camera gets
//   less, one shallower more: uw_depth_light() is the water's Beer-Lambert
//   filter over the depth between them (red goes first).
// - Caustics: the waves gather the sunlight into a moving network of bright
//   lines (tools/gen_caustics.py: rays refracted through a wavy surface and
//   counted on a plane under it, one loop of UW_CAUSTIC_FRAMES frames,
//   godot/assets/textures/water/caustics_frames.png). A
//   point takes the pattern where its sunlight crossed the surface (projected
//   along the light), in cells that grow and soften with depth and fade
//   (UW_CAUSTIC_FADE_M) as the focus smears out. uw_caustic_light() is the
//   light it adds to a surface facing the sun (an emission: the scene's
//   sunlight is the mean, the network moves it round).

// x 1 under the surface (0 above), y the camera's depth (m), z the caustics'
// strength (0-1: the sun's height, the cloud, the waves), w their clock (s).
global uniform vec4 lk_uw;
// Toward the sun in the water (refracted, unit).
global uniform vec3 lk_uw_sun;
// The sunlight at the camera's depth (linear, as the scene's sun carries it).
global uniform vec3 lk_uw_light;
// The water's attenuation per metre (per channel, a bloom's included).
global uniform vec3 lk_uw_sigma;
// The caustic frames (a Texture2DArray, project.godot: the generated loop).
// Sampled with explicit gradients on the wrapped coordinates, so it tiles
// whatever repeat mode the global sampler has.
global uniform sampler2DArray lk_uw_caustics;

// World metres a caustic frame covers at the surface, the frames and the
// loop they play in, the intensity a texel of 1 stands for (gen_caustics.py
// I_MAX), how fast the cells grow with depth and how deep the pattern fades.
const float UW_CAUSTIC_TILE_M = 3.4;
const float UW_CAUSTIC_FRAMES = 32.0;
const float UW_CAUSTIC_LOOP_S = 9.0;
const float UW_CAUSTIC_I_MAX = 4.0;
const float UW_CAUSTIC_GROW_PER_M = 0.07;
const float UW_CAUSTIC_FADE_M = 9.0;

// The sunlight at a world height against the camera's depth.
vec3 uw_depth_light(float world_y) {
	if (lk_uw.x < 0.5) {
		return vec3(1.0);
	}
	float dz = clamp(max(-world_y, 0.0) - lk_uw.y, -lk_uw.y, 40.0);
	return exp(-lk_uw_sigma * dz);
}

// The caustic intensity at a world point (1 = the mean sunlight).
float uw_caustic(vec3 world) {
	float depth = max(-world.y, 0.0);
	// Along the sunlight up to the surface: the pattern lies on the light's line.
	vec2 at = world.xz + lk_uw_sun.xz / max(lk_uw_sun.y, 0.3) * depth;
	vec2 uv = at / (UW_CAUSTIC_TILE_M * (1.0 + depth * UW_CAUSTIC_GROW_PER_M));
	float t = lk_uw.w / UW_CAUSTIC_LOOP_S * UW_CAUSTIC_FRAMES;
	float f0 = mod(floor(t), UW_CAUSTIC_FRAMES);
	float f1 = mod(f0 + 1.0, UW_CAUSTIC_FRAMES);
	// Deeper, softer: a coarser mip (the gradients grow with depth).
	float soften = 1.0 + depth * 0.2;
	vec2 gx = dFdx(uv) * soften;
	vec2 gy = dFdy(uv) * soften;
	vec2 wrapped = fract(uv);
	float a = textureGrad(lk_uw_caustics, vec3(wrapped, f0), gx, gy).r;
	float b = textureGrad(lk_uw_caustics, vec3(wrapped, f1), gx, gy).r;
	float i = mix(a, b, fract(t)) * UW_CAUSTIC_I_MAX;
	return mix(1.0, i, exp(-depth / UW_CAUSTIC_FADE_M));
}

// The light the caustics move onto a surface of `albedo` facing `normal_world`
// at `world` (added to EMISSION; the scene's sun lights the mean). Godot
// folds the diffuse BRDF's 1/pi into a light's energy (a light of energy E
// lights a white Lambert surface facing it to E: measured, RUN_LOG
// section 7), so the caustics add the sunlight times (i - 1), no 1/pi.
vec3 uw_caustic_light(vec3 world, vec3 normal_world, vec3 albedo) {
	// (Uniform branches only: the caustic lookup takes screen derivatives.)
	if (lk_uw.x < 0.5 || lk_uw.z <= 0.001) {
		return vec3(0.0);
	}
	float i = uw_caustic(world);
	float facing = max(dot(normal_world, lk_uw_sun), 0.0);
	return albedo * lk_uw_light * uw_depth_light(world.y) * (facing * lk_uw.z * (i - 1.0));
}
