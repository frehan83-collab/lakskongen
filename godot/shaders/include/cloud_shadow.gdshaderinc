// === Cloud shadows on the land (G3-05, wave-2 integration) ===
//
// The clouds' shadow map (CloudShadows.gd: a seamless noise thresholded at
// the cloud cover, two periods by two, laid along the sunlight round the
// camera's focus and drifting with the sky's clouds), read by the land's
// own shaders (the terrain, its far field and the trees) as the water reads
// it (water_mood.gdshaderinc cloud_shadow_at). On the tiers without the
// CloudShadows decal (RenderQuality cloud_shadow_decal) this is the land's
// only cloud shadow; EnvironmentDirector is the only writer of these globals
// (project.godot [shader_globals]) and sets the strength to 0 wherever the
// decal draws them instead, so a slope is never darkened twice.

global uniform sampler2D lk_cloud_shadow_map;
// The map's frame: its centre and its axes scaled by 1 / the box's size.
global uniform vec3 lk_cloud_shadow_origin;
global uniform vec3 lk_cloud_shadow_axis_u;
global uniform vec3 lk_cloud_shadow_axis_v;
// Towards the sun (the decal's +Y).
global uniform vec3 lk_cloud_shadow_sun;
// How much a shadow takes off a sunlit surface's albedo (CloudShadows
// MAX_DARKEN x the director's strength); 0 draws none.
global uniform float lk_cloud_shadow_darken;

// The albedo factor at world point `p` with world normal `n`: 1 in the sun,
// down to 1 - darken under a cloud, faded on slopes turned from the sun
// exactly as the decal's normal_fade (CloudShadows.SUN_FACING) fades it.
float lk_cloud_shadow_factor(vec3 p, vec3 n) {
	if (lk_cloud_shadow_darken <= 0.0) {
		return 1.0;
	}
	vec3 d = p - lk_cloud_shadow_origin;
	// The map is periodic over its box: fract() wraps it seamlessly.
	vec2 uv = fract(vec2(dot(d, lk_cloud_shadow_axis_u), dot(d, lk_cloud_shadow_axis_v)) + 0.5);
	float facing = smoothstep(0.12, 1.0, dot(normalize(n), lk_cloud_shadow_sun) * 0.5 + 0.5);
	return 1.0 - texture(lk_cloud_shadow_map, uv).a * lk_cloud_shadow_darken * facing;
}
