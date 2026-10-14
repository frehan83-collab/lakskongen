// === The planar mirror's pass (G2-03, PlanarReflection.gd) ===
//
// The water's mirror is rendered from the camera mirrored in the water
// plane, so that camera sits under the water. Godot cannot clip a camera
// to a plane, so it would see the seabed and shallows from above, in front
// of the reflection of what stands above the water (a dark band of seabed
// where the open sea should mirror the sky). Shaders of surfaces that reach
// under the water ask lk_is_mirror_pass() and keep themselves out of it.
//
// The mirror camera is recognised per camera, not by a global switch (one
// global value holds for every viewport drawn in a frame): it leaves render
// layer 19 (PlanarReflection.NO_MIRROR_LAYER) out of its cull mask and sits
// below the water. Every other camera (the player's, the reflection probe's,
// a light's shadow camera) draws layer 19 or sits above the water.

// PlanarReflection.NO_MIRROR_LAYER: render layer 19.
const uint LK_NO_MIRROR_LAYER = 262144u;
// The still water level (FjordTerrain.water_level).
const float LK_WATER_LEVEL = 0.0;

bool lk_is_mirror_pass(uint camera_layers, vec3 camera_world) {
	return (camera_layers & LK_NO_MIRROR_LAYER) == 0u && camera_world.y < LK_WATER_LEVEL;
}

// A model-space vertex, lifted to the water plane in the mirror pass when it
// lies under it. The terrain's triangles face up, so a triangle laid flat on
// the plane faces away from a camera below it and is culled; a shore
// triangle keeps its part above the water.
vec3 lk_mirror_vertex(vec3 vertex, mat4 model, uint camera_layers, vec3 camera_world) {
	if (!lk_is_mirror_pass(camera_layers, camera_world)) {
		return vertex;
	}
	vec3 world = (model * vec4(vertex, 1.0)).xyz;
	if (world.y >= LK_WATER_LEVEL) {
		return vertex;
	}
	world.y = LK_WATER_LEVEL;
	return (inverse(model) * vec4(world, 1.0)).xyz;
}
