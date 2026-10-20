// A light's glow and its reflection on the water at night (the site's
// corner lights, mooring_light_glow.gdshader, and the vessels' lights,
// vessel_light_glow.gdshader). The mesh is two quads (SiteLights.glow_mesh):
// UV2.x = 0 a camera-facing glow that never shrinks below its minimum size
// on screen; UV2.x = 1 the light's glitter path, a strip lying on the water
// under the light and running towards the camera, as long as the ripples
// spread a point light's reflection (the distance squared over the camera's
// height times the slope spread), shimmering. The strip lies on the water,
// so the hulls and gear in front of it hide it like any surface.

// How bright the reflection is beside the glow's core, and the ripples'
// slope spread that sets its length.
uniform float reflection_strength : hint_range(0.0, 1.0) = 0.3;
uniform float glitter_slope = 0.045;
uniform float reflection_min_pixels = 2.5;

// Clip position of a glow corner (VERTEX.xy of the quad, -0.5..0.5) round
// `lamp` (world), at least `min_px` pixels across. dist: the lamp's depth.
vec4 glow_billboard(vec3 lamp, vec2 corner, float size_m, float min_px, mat4 view, mat4 proj, float viewport_h, out float dist) {
	vec4 center = view * vec4(lamp, 1.0);
	dist = max(-center.z, 0.01);
	float m_per_px = 2.0 * dist / (abs(proj[1][1]) * viewport_h);
	float s = max(size_m, min_px * m_per_px);
	vec4 view_pos = center + vec4(corner * s, 0.0, 0.0);
	// Nudged toward the camera past the lamp's own mesh.
	view_pos.xyz += normalize(-center.xyz) * 0.3;
	return proj * view_pos;
}

// World position of a reflection strip corner (uv 0..1: x across, y along
// from behind the light's foot towards the camera) for a light at `lamp`
// over water at height `water_y`.
vec3 glow_reflection(vec3 lamp, float water_y, vec2 uv, vec3 camera, float proj11, float viewport_h) {
	vec3 foot = vec3(lamp.x, water_y + 0.04, lamp.z);
	vec2 to_camera = camera.xz - foot.xz;
	float reach = max(length(to_camera), 1.0);
	vec2 along = to_camera / reach;
	vec2 across = vec2(-along.y, along.x);
	float height = max(camera.y - water_y, 1.0) + max(lamp.y - water_y, 0.0);
	float span = clamp(reach * reach / height * glitter_slope, 2.0, 0.45 * reach);
	float m_per_px = 2.0 * length(camera - foot) / (abs(proj11) * viewport_h);
	float width = max(0.5, reflection_min_pixels * m_per_px);
	vec2 p = foot.xz + along * mix(-0.25 * span, 0.75 * span, uv.y) + across * (uv.x - 0.5) * width;
	return vec3(p.x, foot.y, p.y);
}

// The glow's cover (0..1) at quad uv: a tight core and a soft halo.
float glow_cover(vec2 uv) {
	float d = length(uv - vec2(0.5)) * 2.0;
	float core = exp(-d * d * 15.0);
	float halo = exp(-d * d * 3.2) * 0.3;
	return (core + halo) * (1.0 - smoothstep(0.85, 1.0, d));
}

// The reflection's cover at strip uv: brightest just in front of the foot,
// broken into shimmering patches by the ripples.
float reflection_cover(vec2 uv, float time, float seed) {
	float across = exp(-pow((uv.x - 0.5) / 0.22, 2.0));
	float along = smoothstep(0.0, 0.25, uv.y) * (1.0 - smoothstep(0.45, 1.0, uv.y));
	float ripples = 0.55 + 0.45 * sin(uv.y * 47.0 + time * 2.3 + seed) * sin(uv.y * 19.0 - time * 1.3 + seed * 2.0);
	return across * along * clamp(ripples, 0.0, 1.0) * reflection_strength;
}
