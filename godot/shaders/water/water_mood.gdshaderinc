// === Fjord water: weather and night (G3-05) ===
//
// Three things the moods add to the water, each written by
// EnvironmentDirector and off (one uniform test) when it does not apply:
//
// - Frost smoke (frostrøyk): when bitter land air drains over the open
//   water (AtmosphereModel.FrostSmoke) the water steams. The smoke is a
//   layer `frost_smoke_depth` metres deep over the water; a pixel shows it
//   along the view ray's path through the layer (depth / sin of the view's
//   elevation, so the far water, seen at a graze, is veiled and the water
//   under the camera stays clear), its density in wisps streaked along the
//   wind and drifting with it. The smoke scatters the light round it:
//   `frost_smoke_light` is its radiance, from the director.
// - Cloud shadows: the same shadow map the land's decal projects
//   (CloudShadows.gd, along the sunlight), so a cloud's shadow crosses
//   from the hills onto the water. In shadow the water column loses its
//   share of direct sun.
// - Pen lamps: salmon farms light their pens from below in winter (lamps
//   hung a few metres down, against early maturation). Seen from above the
//   pen glows: the lamp's light, filtered by the water above it, leaves
//   the surface over the pen and fades past the collar. Each lamp is (x, z,
//   radius m, level); `lamp_light` is the glow's radiance at level 1,
//   already corrected for the eye's night exposure by the director.

// Frost smoke: density 0-1, the layer's depth (m), its radiance (linear),
// and the wind's drift of the wisps (m) with the wind's direction.
uniform float frost_smoke : hint_range(0.0, 1.0) = 0.0;
uniform float frost_smoke_depth = 16.0;
uniform vec3 frost_smoke_light = vec3(0.8, 0.85, 0.9);
uniform vec2 frost_smoke_drift = vec2(0.0);
uniform vec2 frost_smoke_wind = vec2(1.0, 0.0);

// Cloud shadows (CloudShadows.gd): the map (alpha = shadow), the world to
// map transform (uv = (dot(p - origin, axis_u), dot(p - origin, axis_v)) +
// 0.5, axes scaled by 1 / the map's size) and how much direct sun a
// shadow takes from the water's body (0 when no shadow is cast).
uniform sampler2D cloud_shadow_map : hint_default_transparent, filter_linear, repeat_enable;
uniform vec3 cloud_shadow_origin = vec3(0.0);
uniform vec3 cloud_shadow_axis_u = vec3(0.0001, 0.0, 0.0);
uniform vec3 cloud_shadow_axis_v = vec3(0.0, 0.0, 0.0001);
uniform float cloud_shadow_strength : hint_range(0.0, 1.0) = 0.0;

#define MAX_LAMPS 24
uniform vec4 lamp_glow[MAX_LAMPS];
uniform int lamp_count = 0;
uniform vec3 lamp_light = vec3(0.0);

// Optical depth per metre of the densest wisps at full smoke.
const float SMOKE_SIGMA = 0.0045;
// The longest path through the layer counted (m): beyond it the layer is opaque anyway.
const float SMOKE_MAX_PATH = 2500.0;

// Value noise for the smoke's wisps (an integer hash on the lattice, quintic
// fade): no texture, so no tile ever repeats across the fjord.
float smoke_hash(vec2 i) {
	uvec2 q = uvec2(ivec2(i) + ivec2(32768));
	uint h = q.x * 1597334677u ^ q.y * 3812015801u;
	h = (h ^ (h >> 16u)) * 2246822519u;
	h ^= h >> 13u;
	return float(h) * (1.0 / 4294967295.0);
}

// Gradient noise (0-1): a random unit gradient at every lattice point, so
// no blocky value-noise cells show where the smoke is thresholded.
vec2 smoke_gradient(vec2 i) {
	float angle = smoke_hash(i) * 6.28318530718;
	return vec2(cos(angle), sin(angle));
}

float smoke_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = dot(smoke_gradient(i), f);
	float b = dot(smoke_gradient(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(smoke_gradient(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(smoke_gradient(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return clamp(0.5 + 0.75 * mix(mix(a, b, u.x), mix(c, d, u.x), u.y), 0.0, 1.0);
}

// Frost smoke's opacity for a water pixel at `p` (x, z) seen along `view_w`
// (toward the camera).
float frost_smoke_cover(vec2 p, vec3 view_w) {
	if (frost_smoke <= 0.001) {
		return 0.0;
	}
	float path = min(frost_smoke_depth / max(view_w.y, 0.006), SMOKE_MAX_PATH);
	// Banks a few hundred metres across, a little longer down the wind and
	// drifting with it, with softer curls drifting faster inside them.
	vec2 along = frost_smoke_wind;
	vec2 q = vec2(dot(p, along), dot(p, vec2(-along.y, along.x)));
	vec2 a = (q - frost_smoke_drift) / vec2(300.0, 160.0);
	float banks = smoke_noise(a) * 0.65 + smoke_noise(mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * a * 2.2 + vec2(5.2, 1.3)) * 0.35;
	vec2 b = (q - frost_smoke_drift * 1.7) / vec2(70.0, 32.0) + vec2(banks * 1.8, 0.0);
	float curls = smoke_noise(b) * 0.6 + smoke_noise(b * 2.3 + vec2(1.9, 7.1)) * 0.4;
	float wisps = smoothstep(0.42, 0.85, banks * 0.7 + curls * 0.4);
	float density = frost_smoke * wisps * sqrt(wisps);
	return 1.0 - exp(-SMOKE_SIGMA * density * path);
}

// Share of direct sun a cloud shadow takes from the water at `p` (world).
float cloud_shadow_at(vec3 p) {
	if (cloud_shadow_strength <= 0.0) {
		return 0.0;
	}
	vec3 d = p - cloud_shadow_origin;
	vec2 uv = vec2(dot(d, cloud_shadow_axis_u), dot(d, cloud_shadow_axis_v)) + 0.5;
	return texture(cloud_shadow_map, uv).a * cloud_shadow_strength;
}

// The pens' lamp glow at `p` (x, z): radiance (linear) leaving the surface.
vec3 lamp_glow_at(vec2 p) {
	if (lamp_count <= 0) {
		return vec3(0.0);
	}
	float glow = 0.0;
	for (int i = 0; i < MAX_LAMPS; i++) {
		if (i >= lamp_count) {
			break;
		}
		vec4 lamp = lamp_glow[i];
		vec2 d = p - lamp.xy;
		float r2 = dot(d, d) / (lamp.z * lamp.z);
		if (r2 < 4.0) {
			// Bright over the lamp, a soft spill past the collar (r = 1).
			glow += lamp.w * (exp(-r2 * 1.6) + 0.18 * exp(-r2 * 0.45));
		}
	}
	return lamp_light * glow;
}
