shader_type spatial;
render_mode world_vertex_coords, blend_add, unshaded, cull_disabled, depth_draw_never, fog_disabled, shadows_disabled;

// Light shafts under the surface (roadmap G3-04, UnderwaterView): the
// sunlight the waves gather into beams, seen where the water scatters it.
// Each shaft is one quad of a MultiMesh: its instance origin is where it
// leaves the surface, its custom data its shape; the vertex stage lays it
// along the light's direction in the water (the sun refracted: every shaft
// is parallel, so they spread from the sun looking up and gather toward the
// antisolar point looking down) and turns it round that axis to face the
// camera. Added light (it adds to the water's own glow), unfogged: the
// shader dims it by the water between it and the camera itself.
//
// INSTANCE_CUSTOM: x width (m), y length (m), z phase (0-1), w strength (0-1).
// The quad: VERTEX.x across (-0.5 .. 0.5), VERTEX.y along (0 at the
// surface .. -1 at the shaft's end), in the instance's frame (identity basis).

// The light's direction in the water (down), and the light the shafts carry
// at full strength (linear radiance: the sun at the camera's depth times
// the shafts' share, set by UnderwaterView from the director's composition).
uniform vec3 shaft_dir = vec3(0.0, -1.0, 0.0);
uniform vec3 shaft_light = vec3(0.0);
uniform float shaft_time = 0.0;
// The attenuation a shaft fades by with the water between it and the camera
// (1/m: a share of the fog's, as the surface's veil: a bright beam keeps its
// shape through forward scattering); down the shaft the light thins by
// depth_attenuation (1/m).
uniform float attenuation = 0.2;
uniform float depth_attenuation = 0.12;
// Shafts closer than this (m) fade out, so none slices through the lens.
uniform float near_fade = 2.0;

varying float v_along;
varying float v_across;
varying vec4 v_custom;
varying vec3 v_world;

void vertex() {
	vec3 top = MODEL_MATRIX[3].xyz;
	vec3 local = VERTEX - top;
	vec3 axis = normalize(shaft_dir);
	vec3 to_camera = CAMERA_POSITION_WORLD - top;
	vec3 side = cross(axis, to_camera);
	side = length(side) > 1e-4 ? normalize(side) : vec3(1.0, 0.0, 0.0);
	float length_m = INSTANCE_CUSTOM.y;
	float width_m = INSTANCE_CUSTOM.x;
	VERTEX = top + axis * (-local.y * length_m) + side * (local.x * width_m);
	v_along = -local.y;
	v_across = local.x * 2.0;
	v_custom = INSTANCE_CUSTOM;
	v_world = VERTEX;
}

void fragment() {
	float across = v_across;
	// Soft across, brighter in the middle; faded in under the surface and
	// out toward the shaft's end.
	float core = exp(-across * across * 3.5);
	float top_fade = smoothstep(0.0, 0.06, v_along);
	float end_fade = 1.0 - smoothstep(0.45, 1.0, v_along);
	float depth = max(-v_world.y, 0.0);
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	float seen = exp(-attenuation * dist) * smoothstep(near_fade * 0.5, near_fade * 1.5, dist);
	// The waves move the beams: a slow, calm swell of each shaft's light.
	float phase = v_custom.z * 6.2831853;
	float flicker = 0.78 + 0.22 * sin(shaft_time * (0.45 + 0.35 * v_custom.z) + phase)
		* sin(shaft_time * (0.23 + 0.2 * fract(v_custom.z * 7.31)) + phase * 1.7);
	// Water scatters forward: a beam is brightest seen against the light
	// (looking up along it toward the sun), dimmest seen across or from above.
	vec3 view_dir = (v_world - CAMERA_POSITION_WORLD) / max(dist, 1e-3);
	float toward = max(dot(view_dir, -normalize(shaft_dir)), 0.0);
	float forward = 0.5 + 3.0 * toward * toward * toward;
	float light = v_custom.w * core * top_fade * end_fade * exp(-depth_attenuation * depth) * seen * flicker * forward;
	ALBEDO = shaft_light * light;
}
