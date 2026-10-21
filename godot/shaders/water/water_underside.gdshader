shader_type spatial;
render_mode world_vertex_coords, cull_front, depth_draw_opaque, unshaded, fog_disabled, shadows_disabled;

// === The water surface seen from below (roadmap G3-04) ===
//
// WaterGrid draws its pieces with this material while the camera is under
// the surface (EnvironmentDirector: below UNDERSIDE_FROM_M, under the wave
// troughs); above it, and across the crossing, water_shader.gdshader (which
// keeps a simple underside of its own). The surface is the same: the
// vertex stage below is water_shader.gdshader's, line for line
// (tests/test_underwater.py compares them), with the same wave uniforms,
// which the director copies over every frame, the open sea's swell
// (F2-21, water/water_swell.gdshaderinc) included.
//
// Looking up from the water (unshaded: everything here is the light that
// reaches the camera):
// - Snell's window: a ray from the camera that meets the surface steeper
//   than 48.6 deg from its normal leaves the water (refract, n = 1.333) and
//   shows the sky in the air direction it leaves in: the whole sky packed
//   into the cone, zenith in the middle, the low sky and the fjord's walls
//   round the rim, the sun's glare where its refracted direction lies, each
//   n^2 brighter (radiance is conserved as L / n^2), less the share the
//   surface reflects back (Fresnel, F0 0.02, by the air-side angle: it rises
//   to 1 at the window's edge). The waves bend and break it up.
// - Outside the window the surface mirrors the water below it (total
//   internal reflection): the water's own light in the mirrored direction,
//   dark; foam from below a shade lighter.
// - The water between the camera and the surface veils it in the water's
//   own light toward the surface, the same linear function the sky draws
//   under water (horizontal radiance + a slope in height + one toward the
//   refracted sun), so the surface fades into the fogged world without a
//   seam at the horizon. The mirrored water (total internal reflection, and
//   the share Fresnel reflects inside the window) fades at the fog's own
//   attenuation (underside_fog), as any object does: a fogged float in front
//   of it then has its colour (wave4b int review: at a share of the fog the
//   dark mirror stayed darker than the fog and every float showed pale
//   against it). Only the light through Snell's window keeps its shape
//   longer (underside_attenuation, a share of the fog: a bright extended
//   source survives forward scattering far longer than a dark object keeps
//   its contrast), tinted by the water's absorption (Beer-Lambert per
//   channel).

#define WAVES 16

// ── The surface (water_shader.gdshader's own uniforms, copied by the director) ──
uniform vec4 wave_k[WAVES];
uniform vec4 wave_amp[WAVES];
uniform float ripple_strength : hint_range(0.0, 2.0) = 0.3;
uniform vec2 sea_direction = vec2(0.287, 0.958);
uniform float sea_time = 0.0;
uniform sampler2D water_bake : filter_linear, repeat_disable;
uniform vec2 bake_origin = vec2(-2000.0, -2000.0);
uniform float bake_spacing = 7.827;
uniform float bake_texels = 512.0;
uniform float has_bake = 0.0;
uniform sampler2D normal_map_1 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_map_2 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float grid_half_units = 128.0;
uniform float grid_base_spacing = 2.0;
uniform float displaced_extent = 4096.0;
// Whitecap share (Monahan): from below the foam is a shade lighter.
uniform float whitecaps : hint_range(0.0, 1.0) = 0.0;

// ── Looking up (EnvironmentDirector, linear radiance) ──
// The sky in the air: overhead, low down, and the fjord's walls round the rim.
uniform vec3 underside_zenith = vec3(0.08, 0.16, 0.32);
uniform vec3 underside_horizon = vec3(0.35, 0.42, 0.5);
uniform vec3 underside_land = vec3(0.03, 0.035, 0.03);
// Toward the sun in the air, its disc's radiance and its glare's.
uniform vec3 underside_sun_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 underside_sun = vec3(0.0);
// The water's own light (the sky's underwater function, sky_shader
// underwater_light without its radiance-pass steepening): the horizontal
// radiance, its slope in height and its slope toward the refracted sun.
uniform vec3 underside_horizontal = vec3(0.01, 0.03, 0.03);
uniform vec3 underside_vertical = vec3(0.007, 0.021, 0.021);
uniform vec3 underside_glow_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 underside_sun_slope = vec3(0.0);
// The water's attenuation per channel (1/m, bloom included), the fog's own
// attenuation (1/m: what the mirrored water fades at) and the window's
// light's (1/m, a share of the fog).
uniform vec3 underside_absorption = vec3(0.40, 0.09, 0.14);
uniform float underside_fog = 0.45;
uniform float underside_attenuation = 0.2;

const float TAU_ = 6.28318530718;
const float INV_TAU = 0.159154943092;
const float IOR_WATER = 1.333;
const float WATER_F0 = 0.02;
// The fjord's walls stand round the window's rim up to this height in the
// air (sine of the elevation): the low sky is land there.
const float LAND_TOP = 0.16;
const float SUN_DISC_COS = 0.99996;
// The wind's ripples' share of the surface's slope seen from below.
const float RIPPLE_FROM_BELOW = 0.25;

varying vec3 v_world;
varying vec2 v_p0;
// F2-21: the open sea's swell slope per vertex (water_swell.gdshaderinc).
varying vec3 v_swell;

// ── Vertex: water_shader.gdshader's, line for line ──
vec4 bake_at(vec2 xz) {
	vec2 uv = ((xz - bake_origin) / bake_spacing + 0.5) / bake_texels;
	vec4 b = textureLod(water_bake, uv, 0.0);
	vec2 edge = min(uv, 1.0 - uv);
	float inside = smoothstep(0.0, 0.015, min(edge.x, edge.y)) * has_bake;
	// Beyond the bake: the open ocean.
	return mix(vec4(200.0, 8000.0, 1.0, 0.0), b, inside);
}

// Amplitude of a component at a point: the basin's or the coast's sea by
// exposure; in shallow water it fades out as the depth runs out (a cheap
// stand-in for 1 - exp(-depth / (0.08 lambda + 0.5))).
float component_amplitude(vec4 wa, float exposure, float depth) {
	float a = mix(wa.x, wa.y, exposure);
	if (depth < 40.0) {
		float d = max(depth, 0.0);
		a *= d / (d + 0.08 * wa.w + 0.5);
	}
	return a;
}

// Phase in [0, 2 pi): the wavevector times a position kilometres out is
// large, and the GPU's fast sine wants a reduced argument.
float wave_phase(vec4 wk, vec4 wa, vec2 p) {
	return fract((dot(wk.xy, p) - wa.z) * INV_TAU) * TAU_;
}

#include "res://shaders/water/water_swell.gdshaderinc"

// The components are ordered longest first (EnvironmentDirector), so a loop
// can stop at the first one too short to draw. The open sea's swell (F2-21)
// rides on top.
vec3 displacement(vec2 p, float exposure, float depth, float grid_spacing) {
	vec3 d = vec3(0.0);
	for (int i = 0; i < WAVES; i++) {
		vec4 wk = wave_k[i];
		vec4 wa = wave_amp[i];
		// The grid draws a component only where it has >= 4 vertices per wavelength.
		if (wa.w < 3.0 * grid_spacing) {
			break;
		}
		float a = component_amplitude(wa, exposure, depth) * smoothstep(3.0 * grid_spacing, 6.0 * grid_spacing, wa.w);
		if (a < 1e-4) {
			continue;
		}
		float ph = wave_phase(wk, wa, p);
		d.y += a * sin(ph);
		d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);
	}
	return d + swell_displacement(p, exposure, depth, grid_spacing);
}

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec2 p0 = VERTEX.xz;
	vec2 rel = (p0 - origin.xz) / s;
	vec2 ar = abs(rel);
	// Odd vertices on a level's outer edge sit mid-edge of the coarser ring:
	// they take their neighbours' mean displacement, so the levels meet
	// without cracks.
	vec2 stitch = vec2(0.0);
	if (max(ar.x, ar.y) > grid_half_units - 0.5) {
		float along = ar.x > grid_half_units - 0.5 ? rel.y : rel.x;
		if (mod(floor(along + 0.5), 2.0) > 0.5) {
			stitch = ar.x > grid_half_units - 0.5 ? vec2(0.0, s) : vec2(s, 0.0);
		}
	}
	// Spacing the grid has here (conservative, continuous across levels).
	float d_grid = max(abs(p0.x - origin.x), abs(p0.y - origin.z));
	float grid_spacing = grid_base_spacing * max(1.0, 2.0 * d_grid / (grid_half_units * grid_base_spacing));
	vec3 disp;
	vec4 b = bake_at(p0);
	if (stitch.x + stitch.y > 0.0) {
		vec4 ba = bake_at(p0 - stitch);
		vec4 bb = bake_at(p0 + stitch);
		disp = 0.5 * (displacement(p0 - stitch, ba.b, ba.r, grid_spacing) + displacement(p0 + stitch, bb.b, bb.r, grid_spacing));
	} else {
		disp = displacement(p0, b.b, b.r, grid_spacing);
	}
	// Flat beyond the displaced levels: the far skirt and the last level's
	// edge meet without a crack.
	float far_fade = 1.0 - smoothstep(0.75 * displaced_extent, displaced_extent, d_grid);
	disp *= far_fade;
	// F2-21: the swell's slope at the vertex (the bake read once).
	v_swell = swell_slope(p0, b.b, b.r) * far_fade;
	VERTEX = vec3(p0.x, origin.y, p0.y) + disp;
	v_world = VERTEX;
	v_p0 = p0;
}

// ── Fragment: looking up ──

// The surface's normal (up), band-limited to the pixel's footprint as the
// water shader's, with the open sea's swell (its per-vertex slope, F2-21)
// and the wind's ripples.
vec3 surface_normal(vec2 p0, float exposure, float depth, float footprint, vec3 swell) {
	vec3 n = vec3(0.0, 1.0, 0.0);
	float keep_from = 2.0 * footprint;
	float keep_scale = 1.0 / (4.0 * footprint);
	for (int i = 0; i < WAVES; i++) {
		vec4 wk = wave_k[i];
		vec4 wa = wave_amp[i];
		float a = component_amplitude(wa, exposure, depth);
		float t = clamp((wa.w - keep_from) * keep_scale, 0.0, 1.0);
		float keep = t * t * (3.0 - 2.0 * t);
		if (a > 1e-5 && keep > 0.0) {
			float ph = wave_phase(wk, wa, p0);
			float c = cos(ph);
			n.x -= keep * wk.x * a * c;
			n.z -= keep * wk.y * a * c;
			n.y -= keep * wk.w * wk.z * a * sin(ph);
		}
	}
	n -= swell * swell_keep(footprint);
	n = normalize(n);
	vec2 q = vec2(dot(p0, sea_direction), dot(p0, vec2(-sea_direction.y, sea_direction.x)));
	mat2 to_sea = mat2(vec2(sea_direction.x, sea_direction.y), vec2(-sea_direction.y, sea_direction.x));
	vec2 r1 = texture(normal_map_1, q / 31.0 - vec2(sea_time * 0.035, 0.0)).rg * 2.0 - 1.0;
	vec2 r2 = texture(normal_map_2, q / 11.0 * mat2(vec2(0.94, 0.34), vec2(-0.34, 0.94)) - vec2(sea_time * 0.06, 0.0)).rg * 2.0 - 1.0;
	vec2 ripple = to_sea * (r1 * 0.65 + r2 * 0.45);
	// Half the slope the water shader gives them from above: near the
	// critical angle a ripple swings the window's edge by several times its
	// own slope, and the full slope tore the window into islands.
	return normalize(n + vec3(ripple.x, 0.0, ripple.y) * ripple_strength * RIPPLE_FROM_BELOW);
}

// The water's own light in a direction (the sky's underwater function).
vec3 water_light(vec3 dir) {
	return max(underside_horizontal + underside_vertical * dir.y + underside_sun_slope * dot(dir, underside_glow_dir), vec3(0.0));
}

// The sky in an air direction: zenith to the low sky, the fjord's walls
// round the rim, the sun.
vec3 sky_light(vec3 t) {
	float up = clamp(t.y, 0.0, 1.0);
	vec3 sky = mix(underside_horizon, underside_zenith, sqrt(up));
	sky = mix(underside_land, sky, smoothstep(LAND_TOP * 0.5, LAND_TOP, up));
	float s = max(dot(t, underside_sun_dir), 0.0);
	vec3 sun = underside_sun * (smoothstep(SUN_DISC_COS - 0.00002, SUN_DISC_COS, s) + 0.02 * pow(s, 400.0) + 0.002 * pow(s, 24.0));
	return sky + sun;
}

void fragment() {
	vec3 to_camera = CAMERA_POSITION_WORLD - v_world;
	float dist = length(to_camera);
	vec3 view_w = to_camera / max(dist, 1e-4);
	vec4 bake = bake_at(v_p0);
	float pixel_angle = 2.0 / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float footprint = dist * pixel_angle / max(abs(view_w.y), 0.12) + 1e-4;
	vec3 n = surface_normal(v_p0, bake.b, bake.r, footprint, v_swell);
	// The ray from the camera up to the surface, and the surface's normal
	// facing down into the water.
	vec3 d = -view_w;
	vec3 nw = -n;
	vec3 r = reflect(d, nw);
	vec3 below = water_light(r);
	vec3 t = refract(d, nw, IOR_WATER);
	// The light through the window (the sky's) and the mirrored water.
	vec3 window = vec3(0.0);
	vec3 mirror = below;
	if (dot(t, t) >= 1e-8) {
		// Fresnel from inside by the air-side angle: 1 at the window's edge.
		float cos_t = clamp(dot(t, n), 0.0, 1.0);
		float f = WATER_F0 + (1.0 - WATER_F0) * pow(1.0 - cos_t, 5.0);
		window = sky_light(t) * (IOR_WATER * IOR_WATER) * (1.0 - f);
		mirror = below * f;
	}
	// Breaking crests: foam from below, a shade of the light through it.
	float foam = clamp(whitecaps * 3.0, 0.0, 0.6) * smoothstep(0.0, 3.0, bake.r);
	window = window * (1.0 - 0.75 * foam) + underside_horizontal * 4.0 * foam;
	mirror *= 1.0 - 0.75 * foam;
	// The water between veils the surface in its own light toward it: the
	// mirrored water at the fog's attenuation, the window's light longer,
	// tinted by the water's absorption.
	float t_fog = exp(-underside_fog * dist);
	float t_window = exp(-underside_attenuation * dist);
	ALBEDO = water_light(d) * (1.0 - t_fog) + mirror * t_fog + window * exp(-underside_absorption * dist) * t_window;
}
