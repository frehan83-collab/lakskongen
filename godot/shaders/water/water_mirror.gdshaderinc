// === Fjord water: the planar mirror (G2-03) ===
//
// PlanarReflection.gd renders the scene from the camera mirrored in the
// water plane into a linear HDR texture (half the window's resolution on
// High), and hands over the view-projection that rendered it. A surface
// point is looked up by projecting it with that very matrix, so a texture
// rendered a frame earlier still lines up with what it shows. The surface
// normal turns the reflected ray: the lookup moves by the projected change
// of direction (as if what it mirrors stood far away), which breaks the
// mirror up under ripples and stretches it under chop, as on real water.

// The mirror (linear radiance) and the view-projection that rendered it.
uniform sampler2D planar_reflection : hint_default_black, filter_linear, repeat_disable;
uniform mat4 planar_view_proj;
// 0: no mirror (Low tier, rough sea, underwater), 1: the mirror is live.
uniform float planar_mix : hint_range(0.0, 1.0) = 0.0;
// How far the surface normal moves the lookup (1: as for an object at
// infinity; nearer objects move less, so a little under 1 reads true).
uniform float planar_distort : hint_range(0.0, 2.0) = 0.7;

// The mirror holds a pixel's reflection up to this roughness, fading out
// from PLANAR_ROUGH_FROM: past it the sky's radiance (which the engine
// blurs by roughness) carries the reflection.
const float PLANAR_ROUGH_FROM = 0.07;
const float PLANAR_ROUGH_TO = 0.2;
// UV margin over which the mirror fades out at the texture's edges.
const float PLANAR_EDGE = 0.03;
// Facet slope (tan) over which the mirror gives way to the sky's radiance.
const float PLANAR_SLOPE_FROM = 0.15;
const float PLANAR_SLOPE_TO = 0.35;

vec2 planar_uv_of_point(vec3 p) {
	vec4 c = planar_view_proj * vec4(p, 1.0);
	return vec2(c.x, -c.y) / max(c.w, 1e-4) * 0.5 + 0.5;
}

// Where a direction (a point at infinity) lands in the mirror.
vec2 planar_uv_of_dir(vec3 d) {
	vec4 c = planar_view_proj * vec4(d, 0.0);
	return vec2(c.x, -c.y) / max(c.w, 1e-3) * 0.5 + 0.5;
}

// The mirror's radiance for a surface pixel and the share of the
// reflection it holds (w). `flat_point` is where the view ray meets the
// still water plane, `view_w` points to the camera, `n` is the shading
// normal, `roughness` the surface's.
vec4 planar_sample(vec3 flat_point, vec3 view_w, vec3 n, float roughness) {
	if (planar_mix <= 0.0) {
		return vec4(0.0);
	}
	float w = planar_mix * (1.0 - smoothstep(PLANAR_ROUGH_FROM, PLANAR_ROUGH_TO, roughness));
	// A steep facet (a storm sea's flank) looks far off the mirrored view:
	// the sky's radiance has its direction, the mirror does not.
	w *= 1.0 - smoothstep(PLANAR_SLOPE_FROM, PLANAR_SLOPE_TO, length(n.xz) / max(n.y, 0.1));
	if (w <= 0.0) {
		return vec4(0.0);
	}
	vec3 r0 = reflect(-view_w, vec3(0.0, 1.0, 0.0));
	vec3 r = reflect(-view_w, n);
	r.y = max(r.y, 0.02);
	vec2 uv = planar_uv_of_point(flat_point) + (planar_uv_of_dir(r) - planar_uv_of_dir(r0)) * planar_distort;
	vec2 edge = min(uv, 1.0 - uv);
	w *= smoothstep(0.0, PLANAR_EDGE, min(edge.x, edge.y));
	if (w <= 0.0) {
		return vec4(0.0);
	}
	// A slightly rough surface smears the mirror, mostly along the view
	// (vertical streaks on the screen): a short vertical tap row.
	float spread = smoothstep(0.035, PLANAR_ROUGH_TO, roughness) * 0.02;
	vec3 c = texture(planar_reflection, uv).rgb * 0.4;
	c += texture(planar_reflection, uv + vec2(0.0, spread)).rgb * 0.2;
	c += texture(planar_reflection, uv - vec2(0.0, spread)).rgb * 0.2;
	c += texture(planar_reflection, uv + vec2(spread * 0.35, spread * 2.0)).rgb * 0.1;
	c += texture(planar_reflection, uv - vec2(spread * 0.35, spread * 2.0)).rgb * 0.1;
	return vec4(c, w);
}
