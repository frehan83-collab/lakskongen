// Floating site gear on the GPU (roadmap G2-04): the drawn water's height
// for the feed hoses, grid buoys, corner lights and mooring chains, so they
// ride the waves like the pens and boats do (those ride on the CPU through
// WaveField.gd, as rigid bodies).
//
// This is water_shader.gdshader's vertex displacement (component_amplitude,
// wave_phase, displacement, the grid's band limit and far fade), mirrored
// term for term, inverted by fixed-point iteration as WaveField.height_at()
// does: the height of the water over a fixed world point. SiteFloat.gd
// copies the director's wave arrays (the very ones the water material gets)
// and the water grid's placement into every material that includes this,
// once a frame. A small float also tilts with the water's slope. tests/test_site_float.py pins the constants to the water
// shader and to WaveField.gd; SiteFloat.reference_height() is the same
// algorithm on the CPU, which the UI-flow tests hold to WaveField.height_at.
//
// The water bake (depth, exposure) is the terrain's, the very texture the
// water samples (SiteFloat hands it over): over a site the exposure to the
// open sea changes enough to change a storm's waves.

#define FLOAT_WAVES 16

uniform vec4 float_wave_k[FLOAT_WAVES];
uniform vec4 float_wave_amp[FLOAT_WAVES];
// Live components (the arrays' length) and fixed-point steps of the
// inversion. Uniform loop bounds: the compiler keeps the loops instead of
// unrolling them into every call site (a site's gear compiles in moments).
uniform int float_wave_count = 0;
uniform int float_invert_steps = 4;
// The water grid: its origin (it follows the camera), half size in quads,
// base spacing and the extent past which it lies flat.
uniform vec3 float_grid_origin = vec3(0.0);
uniform float float_grid_half_units = 128.0;
uniform float float_grid_base_spacing = 2.0;
uniform float float_displaced_extent = 4096.0;
// 0 until a fjord's waves are known: the gear rests at the waterline.
uniform float float_on = 0.0;
// The terrain's water bake (R depth, B open-sea exposure) and its placement.
uniform sampler2D float_bake_tex : filter_linear, repeat_disable;
uniform vec2 float_bake_origin = vec2(-2000.0, -2000.0);
uniform float float_bake_spacing = 7.827;
uniform float float_bake_texels = 512.0;
uniform float float_has_bake = 0.0;

const float FLOAT_INV_TAU = 0.159154943092;
const float FLOAT_TAU = 6.28318530718;

// water_shader.gdshader component_amplitude().
float float_component_amplitude(vec4 wa, float exposure, float depth) {
	float a = mix(wa.x, wa.y, exposure);
	if (depth < 40.0) {
		float d = max(depth, 0.0);
		a *= d / (d + 0.08 * wa.w + 0.5);
	}
	return a;
}

// water_shader.gdshader bake_at(): beyond the bake lies the open ocean.
vec4 float_bake_at(vec2 xz) {
	vec2 uv = ((xz - float_bake_origin) / float_bake_spacing + 0.5) / float_bake_texels;
	vec4 b = textureLod(float_bake_tex, uv, 0.0);
	vec2 edge = min(uv, 1.0 - uv);
	float inside = smoothstep(0.0, 0.015, min(edge.x, edge.y)) * float_has_bake;
	return mix(vec4(200.0, 8000.0, 1.0, 0.0), b, inside);
}

// The grid's spacing at a point and how much displacement survives its far
// fade there: vec2(spacing, fade).
vec2 float_grid_reach(vec2 p) {
	float d = max(abs(p.x - float_grid_origin.x), abs(p.y - float_grid_origin.z));
	float spacing = float_grid_base_spacing * max(1.0, 2.0 * d / (float_grid_half_units * float_grid_base_spacing));
	return vec2(spacing, 1.0 - smoothstep(0.75 * float_displaced_extent, float_displaced_extent, d));
}

// water_shader.gdshader displacement() at a grid point, far fade included.
vec3 float_displacement(vec2 p) {
	vec4 bake = float_bake_at(p);
	vec2 reach = float_grid_reach(p);
	vec3 d = vec3(0.0);
	for (int i = 0; i < float_wave_count; i++) {
		vec4 wk = float_wave_k[i];
		vec4 wa = float_wave_amp[i];
		if (wa.w < 3.0 * reach.x) {
			break;
		}
		float a = float_component_amplitude(wa, bake.b, bake.r) * smoothstep(3.0 * reach.x, 6.0 * reach.x, wa.w);
		if (a < 1e-4) {
			continue;
		}
		float ph = fract((dot(wk.xy, p) - wa.z) * FLOAT_INV_TAU) * FLOAT_TAU;
		d.y += a * sin(ph);
		d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);
	}
	return d * reach.y;
}

// The drawn water over the world point xz: vec3(world height, slope along
// x, slope along z). The grid point that lands on xz is found by
// fixed-point iteration (WaveField.height_at); the slope is the components'
// first-order slope there. Flat at the rest waterline while float_on is off.
vec3 float_surface(vec2 xz) {
	if (float_on < 0.5 || float_wave_count <= 0) {
		return vec3(0.0);
	}
	vec2 p = xz;
	for (int s = 0; s < float_invert_steps; s++) {
		p = xz - float_displacement(p).xz;
	}
	vec4 bake = float_bake_at(p);
	vec2 reach = float_grid_reach(p);
	float h = 0.0;
	vec2 g = vec2(0.0);
	for (int i = 0; i < float_wave_count; i++) {
		vec4 wk = float_wave_k[i];
		vec4 wa = float_wave_amp[i];
		if (wa.w < 3.0 * reach.x) {
			break;
		}
		float a = float_component_amplitude(wa, bake.b, bake.r) * smoothstep(3.0 * reach.x, 6.0 * reach.x, wa.w);
		if (a < 1e-4) {
			continue;
		}
		float ph = fract((dot(wk.xy, p) - wa.z) * FLOAT_INV_TAU) * FLOAT_TAU;
		h += a * sin(ph);
		g += a * cos(ph) * wk.xy;
	}
	return vec3(float_grid_origin.y + h * reach.y, g * reach.y);
}

// World height of the drawn water over xz.
float float_height(vec2 xz) {
	return float_surface(xz).x;
}

// Floating gear reads at a distance: a buoy never shrinks below
// float_min_pixels across (its real `size_m` near the camera, up to
// float_max_scale times it far away), the way the corner-light halos keep
// a minimum size. m_per_px: metres one pixel spans at the gear's distance.
uniform float float_min_pixels = 3.0;
uniform float float_max_scale = 3.0;

float float_screen_scale(float size_m, float m_per_px) {
	return clamp(float_min_pixels * m_per_px / max(size_m, 0.01), 1.0, float_max_scale);
}

// Metres per pixel at `dist` from the camera (projection [1][1], viewport
// height in pixels). The renderer's projection flips y, so [1][1] is
// negative: only its size counts (as in water_shader.gdshader).
float float_m_per_px(float dist, float proj11, float viewport_h) {
	return 2.0 * dist / (abs(proj11) * viewport_h);
}

// A small rigid float (buoy, light) at `origin` (its waterline): scales a
// world-space point of it about the waterline by `scale`, moves it with the
// water's height and tilts it about the waterline with the water's slope.
vec3 float_rigid(vec3 world, vec3 origin, vec2 slope, float heave, float scale) {
	vec3 rel = (world - origin) * scale;
	vec3 tilted = vec3(rel.x - rel.y * slope.x, rel.y + rel.x * slope.x + rel.z * slope.y, rel.z - rel.y * slope.y);
	return origin + tilted + vec3(0.0, heave, 0.0);
}

// The same tilt for a direction (normals).
vec3 float_rigid_dir(vec3 n, vec2 slope) {
	return normalize(vec3(n.x - n.y * slope.x, n.y + n.x * slope.x + n.z * slope.y, n.z - n.y * slope.y));
}
