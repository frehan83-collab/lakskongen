shader_type spatial;
render_mode world_vertex_coords, cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, specular_occlusion_disabled;

// === Fjord water, optics v1 (G1-05) ===
//
// OPAQUE water: no ALPHA, no screen or depth texture. The colour of the water
// column comes from Beer-Lambert absorption over the baked bathymetry
// (FjordTerrain's water bake: depth, shore distance, exposure, seabed slope),
// so deep water reads dark teal-black and the terrain never shows through it,
// while shallow water lets the seabed through. The surface reflects the sky
// radiance through the engine's Schlick Fresnel and GGX highlight (F0 0.02:
// SPECULAR 0.354, since the engine's F0 is 0.16 SPECULAR^2 and it fades the
// grazing reflection of anything below F0 0.02 as specular occlusion). It
// draws on its own render layer, which the fjord's ReflectionProbe leaves out
// of its reflection mask (WaterGrid.gd).
//
// Waves: the eight fixed bands of SeaStateModel (JONSWAP from the wind and
// the fetch, Carter 1982), each drawn as two Gerstner components either side
// of the band's heading and slightly detuned, so the sea never looks like
// corrugated sheet. EnvironmentDirector writes one amplitude set for the
// sheltered basin and one for the open coast; the baked exposure blends
// them. Phases are integrated on the CPU, so the waves never jump. Normals
// are analytic per pixel; a component too fine for the pixel footprint fades
// out of the normal and its slope variance moves into the roughness instead
// of aliasing into glitter.
//
// Foam: lacy surf where the shore distance field is small, running in;
// whitecaps over Monahan's share of the surface, on the crests of the local
// sea, stretched along them.
//
// Seen from below (cull_disabled, !FRONT_FACING): Snell's window (48.6 deg)
// shows the daylight, outside it total internal reflection mirrors the deep
// water.

#define WAVES 16

#include "res://shaders/water/water_mirror.gdshaderinc"

// ── Waves (written by EnvironmentDirector) ───────────────────────────────────
// wave_k[i] = (wavevector x, wavevector z, wavenumber k = 2 pi / lambda, choppiness Q),
// ordered longest wave first
uniform vec4 wave_k[WAVES];
// wave_amp[i] = (amplitude in the sheltered basin m, amplitude on the open coast m,
// phase rad, wavelength m)
uniform vec4 wave_amp[WAVES];
// wave_peak[i] = the component's weight round the spectral peak of the
// sheltered basin's sea and of the open coast's (whitecaps break there).
uniform vec2 wave_peak[WAVES];
// Whitecap coverage (Monahan fraction) and the local wind's ripple strength.
uniform float whitecaps : hint_range(0.0, 1.0) = 0.0;
// Crest field level above which the sea breaks (from Monahan's share,
// EnvironmentDirector.whitecap_threshold()).
uniform float whitecap_threshold = 4.0;
uniform float ripple_strength : hint_range(0.0, 2.0) = 0.3;
// Significant wave height of each sea (m): surf width, crest height, roughness.
uniform vec2 sea_height = vec2(0.1, 0.5);
// Spectral peak wavelength of each sea (m).
uniform vec2 sea_peak = vec2(3.0, 20.0);
uniform vec2 sea_direction = vec2(0.287, 0.958);
// Seconds, accumulated by the director.
uniform float sea_time = 0.0;

// ── Optics ───────────────────────────────────────────────────────────────────
// Attenuation coefficient of fjord water per metre (red is absorbed fastest)
// and the reflectance of an optically deep column.
uniform vec3 absorption = vec3(0.40, 0.09, 0.14);
uniform vec3 deep_scatter = vec3(0.0022, 0.0085, 0.0105);
uniform vec3 seabed_sediment = vec3(0.11, 0.105, 0.075);
uniform vec3 seabed_rock = vec3(0.04, 0.042, 0.04);
uniform vec3 foam_color = vec3(0.86, 0.9, 0.92);
uniform float base_roughness : hint_range(0.0, 1.0) = 0.04;
// Sim water (global in v1): bloom severity and sea temperature (0 cold, 1 warm).
uniform float algae_density : hint_range(0.0, 1.0) = 0.0;
uniform float temperature_factor : hint_range(0.0, 1.0) = 0.2;
uniform float turbidity : hint_range(0.0, 1.0) = 0.0;
// Daylight seen through Snell's window from below.
uniform vec3 underside_light = vec3(0.3, 0.45, 0.5);

// ── Bake ─────────────────────────────────────────────────────────────────────
uniform sampler2D water_bake : filter_linear, repeat_disable;
uniform vec2 bake_origin = vec2(-2000.0, -2000.0);
uniform float bake_spacing = 7.827;
uniform float bake_texels = 512.0;
uniform float has_bake = 0.0;

// ── Detail ───────────────────────────────────────────────────────────────────
uniform sampler2D normal_map_1 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_map_2 : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// R foam lace, G foam patches, B fine bubbles, A uniform smooth noise (tools/gen_water_textures.py).
uniform sampler2D foam_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D caustics_texture : hint_default_white, filter_linear_mipmap, repeat_enable;

// ── Grid (WaterGrid.gd) ──────────────────────────────────────────────────────
uniform float grid_half_units = 128.0;
uniform float grid_base_spacing = 2.0;
// Half-extent of the displaced levels (m); the skirt beyond lies flat.
uniform float displaced_extent = 4096.0;

const float TAU_ = 6.28318530718;
const float INV_TAU = 0.159154943092;
const float IOR_WATER = 1.333;
const float SNELL_COS = 0.6615; // cos(48.6 deg)
const float MIN_REFLECTION_UP = 0.03;
// F0 = 0.16 * SPECULAR^2 = 0.02, water's normal-incidence reflectance.
const float WATER_SPECULAR = 0.354;
const float WATER_F0 = 0.02;
// The whitecap field: a crest gate (0 in the troughs, CREST_WEIGHT on the
// crests) plus breaker noise of BREAKER_SPREAD (texture A, uniform on 0-1),
// so short patches break at random along the crests instead of
// whole crest lines. The director's threshold mapping is calibrated on it.
const float CREST_WEIGHT = 2.0;
const float BREAKER_SPREAD = 2.0;
// Wave groups and gusts: a slow field that gathers the caps into patches.
const float GROUP_SPREAD = 2.5;
// The foam lace frays a cap's edge (a ragged patch, not a smooth oval).
const float LACE_RAGGED = 0.9;
// Width (m) of the shore's foam fringe in a calm (G2-03: foam wherever the
// shore distance is under 5 m).
const float SHORE_FRINGE_M = 4.0;

varying vec3 v_world;
varying vec2 v_p0;
// The still water level (the grid's height).
varying float origin_y;

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

// The components are ordered longest first (EnvironmentDirector), so a loop
// can stop at the first one too short to draw.
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
	return d;
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
	if (stitch.x + stitch.y > 0.0) {
		vec4 ba = bake_at(p0 - stitch);
		vec4 bb = bake_at(p0 + stitch);
		disp = 0.5 * (displacement(p0 - stitch, ba.b, ba.r, grid_spacing) + displacement(p0 + stitch, bb.b, bb.r, grid_spacing));
	} else {
		vec4 b = bake_at(p0);
		disp = displacement(p0, b.b, b.r, grid_spacing);
	}
	// Flat beyond the displaced levels: the far skirt and the last level's
	// edge meet without a crack.
	disp *= 1.0 - smoothstep(0.75 * displaced_extent, displaced_extent, d_grid);
	VERTEX = vec3(p0.x, origin.y, p0.y) + disp;
	v_world = VERTEX;
	origin_y = origin.y;
	v_p0 = p0;
}

void fragment() {
	vec2 p0 = v_p0;
	vec4 bake = bake_at(p0);
	float depth = bake.r;
	float shore = bake.g;
	float exposure = bake.b;
	float slope = bake.a;
	// Pixel footprint on the water (m), from the distance and the grazing
	// angle: smooth everywhere (screen derivatives of the displaced grid jump
	// from one triangle to the next and printed the grid into the shading).
	vec3 to_camera = CAMERA_POSITION_WORLD - v_world;
	vec3 view_w = normalize(to_camera);
	// abs(): the projection flips Y on this backend, so [1][1] is negative.
	float pixel_angle = 2.0 / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float footprint = length(to_camera) * pixel_angle / max(abs(view_w.y), 0.12) + 1e-4;
	float hs = mix(sea_height.x, sea_height.y, exposure);

	// ── Analytic Gerstner normal, band-limited to the pixel footprint ──
	vec3 n = vec3(0.0, 1.0, 0.0);
	float lost_variance = 0.0;
	// The whitecaps break on the crests of the waves at the spectral peak
	// (the swell lifts the sea without breaking it, the short chop is too
	// small to show): the surface height of the components near the peak
	// (wave_peak: each component's weight round the peak of the basin's and
	// the coast's sea, from the director), its variance, and its quarter-
	// period lag (height_lag), from which a trailing term follows that is
	// high just after a crest has passed (the spent foam left behind it).
	float height = 0.0;
	float height_lag = 0.0;
	float height_var = 0.0;
	// The whitecaps follow the crests of the local sea until they shrink to
	// a few pixels; only then is the surface height needed for components
	// too fine to draw.
	float dominant_lambda = mix(sea_peak.x, sea_peak.y, exposure);
	bool breaking = whitecaps > 0.0008 && depth > 0.0;
	bool need_height = breaking && footprint < 0.22 * dominant_lambda;
	float keep_from = 2.0 * footprint;
	float keep_scale = 1.0 / (4.0 * footprint);
	for (int i = 0; i < WAVES; i++) {
		vec4 wk = wave_k[i];
		vec4 wa = wave_amp[i];
		float a = component_amplitude(wa, exposure, depth);
		float ka = wk.z * a;
		vec2 wp = wave_peak[i];
		float peak_a = a * mix(wp.x, wp.y, exposure);
		height_var += peak_a * peak_a;
		float t = clamp((wa.w - keep_from) * keep_scale, 0.0, 1.0);
		float keep = t * t * (3.0 - 2.0 * t);
		lost_variance += 0.5 * ka * ka * (1.0 - keep * keep);
		if (a > 1e-5 && (keep > 0.0 || need_height)) {
			float ph = wave_phase(wk, wa, p0);
			float c = cos(ph);
			float sn = sin(ph);
			// The wavevector carries the direction times k: d(height)/dx = kx a cos.
			n.x -= keep * wk.x * a * c;
			n.z -= keep * wk.y * a * c;
			n.y -= keep * wk.w * ka * sn;
			height += peak_a * sn;
			height_lag += peak_a * c;
		}
	}
	height_var *= 0.5;
	// The phase falls through pi/2 as a crest passes a point: just after it
	// (sin high, cos still positive) the spent foam lingers.
	float trail = 0.6 * height + 0.8 * height_lag;
	n = normalize(n);

	// ── Ripples: the wind's short waves, scrolling with the sea ──
	vec2 drift = sea_direction * sea_time;
	mat2 to_sea = mat2(vec2(sea_direction.x, sea_direction.y), vec2(-sea_direction.y, sea_direction.x));
	vec2 q = vec2(dot(p0, sea_direction), dot(p0, vec2(-sea_direction.y, sea_direction.x)));
	vec2 r1 = texture(normal_map_1, q / 31.0 - vec2(sea_time * 0.035, 0.0)).rg * 2.0 - 1.0;
	vec2 r2 = texture(normal_map_2, q / 11.0 * mat2(vec2(0.94, 0.34), vec2(-0.34, 0.94)) - vec2(sea_time * 0.06, 0.0)).rg * 2.0 - 1.0;
	// Back from the sea frame to world x/z.
	vec2 ripple_sea = r1 * 0.65 + r2 * 0.45;
	vec2 ripple = to_sea * ripple_sea;
	float shoal = smoothstep(0.0, 1.5, depth);
	float ripple_amount = ripple_strength * shoal;
	n = normalize(n + vec3(ripple.x, 0.0, ripple.y) * ripple_amount * 0.5);
	// The ripples' mips flatten far away: their slope moves into the roughness.
	lost_variance += 0.02 * ripple_amount * ripple_amount * smoothstep(0.05, 1.0, footprint);


	// ── Foam (only near the shore or where the sea breaks) ──
	float surge = smoothstep(0.05, 0.7, hs);
	// The surf band: a lacy fringe SHORE_FRINGE_M wide in any weather (the
	// lapping line where the fjord meets the rock; it also tells the player
	// where the shore is), widening with the sea.
	float surf_width = SHORE_FRINGE_M + 6.0 * surge + 3.0 * hs;
	float foam = 0.0;
	bool near_shore = shore < surf_width && shore > -0.5;
	if (near_shore || breaking) {
		vec2 fuv = q - vec2(sea_time * 0.25, 0.0);
		float lace = texture(foam_texture, fuv / 7.0).r;
		float bubbles = texture(foam_texture, fuv / 2.3 + 0.37).b;
		// Fixed scales only: dividing a position kilometres out by a scale
		// that varies across the water (with the exposure) shears the texture
		// into stripes.
		float patches = texture(foam_texture, fuv / 70.0).g;
		// Shore: a lapping line and a lacy fringe in a calm, a surf band a
		// few metres wide once the sea builds, its lines running in.
		float shore_band = 1.0 - smoothstep(0.0, surf_width, shore);
		float lines = 0.5 + 0.5 * sin(TAU_ * (shore / (2.5 + 3.0 * hs) + sea_time * 0.11) + patches * 3.0);
		float shore_foam = shore_band * smoothstep(0.35, 0.95, shore_band * 0.6 + lines * 0.45) * (0.45 + 0.55 * lace);
		shore_foam *= step(-0.5, shore) * mix(0.45, 1.0, surge);
		// The lapping line: the last metre or two is wet and white.
		float lap = (1.0 - smoothstep(0.0, 1.6 + hs, shore)) * step(-0.5, shore) * (0.5 + 0.5 * lace);
		shore_foam = max(shore_foam, lap * 0.6);
		// Whitecaps: a steep-crest gate plus a breaker noise (which stretch
		// of a crest breaks, at the scale of the local sea; it slowly
		// cross-fades so caps form and die) exceeds whitecap_threshold,
		// mapped from Monahan's share by the director. A bright, soft-edged
		// core on the crest, and spent foam trailing behind it (upwind, where
		// the crest has just passed), streaked out along the wind. In a gale
		// long wind streaks join them. Once the dominant crests shrink to a
		// few pixels the caps are drawn as their mean cover.
		float caps = 0.0;
		if (breaking) {
			vec2 bq = q - vec2(sea_time * 0.6, 0.0);
			float fade = 0.5 + 0.5 * sin(sea_time * 0.21);
			// Stretched along the crests (q.y), short across them; the basin's
			// short chop and the coast's long sea each at their own scale.
			vec2 bs = bq / vec2(11.0, 28.0);
			vec2 bl = bq / vec2(150.0, 390.0);
			float breaker_small = mix(texture(foam_texture, bs).a, texture(foam_texture, bs / 1.17 + 0.5).a, fade);
			// The large field changes slowly: one fetch, no cross-fade.
			float breaker_large = texture(foam_texture, bl).a;
			float breaker = mix(breaker_small, breaker_large, exposure);
			// Waves break in groups and under gusts: a slow, round field a
			// few hundred metres across gathers the caps into patches instead
			// of one on every crest (a crest-stretched one lines them up in
			// bands).
			float group = texture(foam_texture, bq / 260.0 + 0.21).a;
			float sigma_k = sqrt(max(height_var, 1e-10));
			float crest_gate = smoothstep(0.0, 1.5, height / sigma_k);
			float crest_z = CREST_WEIGHT * crest_gate + BREAKER_SPREAD * (breaker - 0.5) + GROUP_SPREAD * (group - 0.5) + (lace - 0.5) * LACE_RAGGED;
			// The core holds Monahan's share (the director's threshold is
			// calibrated on it); a faint halo softens its edge.
			float core = smoothstep(whitecap_threshold, whitecap_threshold + 0.5, crest_z);
			float halo = smoothstep(whitecap_threshold - 0.35, whitecap_threshold, crest_z) * 0.25;
			// Spent foam: where a crest that broke has just passed (the trail
			// term), on the same stretch of crest, thinning to lace streaked
			// out down the wind.
			float trail_z = CREST_WEIGHT * smoothstep(0.0, 1.5, trail / sigma_k) + BREAKER_SPREAD * (breaker - 0.5) + GROUP_SPREAD * (group - 0.5);
			float streak = texture(foam_texture, vec2(fuv.x / 26.0, fuv.y / 3.5)).r;
			float spent = smoothstep(whitecap_threshold - 0.2, whitecap_threshold + 0.3, trail_z) * (0.3 + 0.7 * streak) * lace;
			caps = max(core * (0.6 + 0.4 * lace) * (0.8 + 0.2 * bubbles), max(halo, spent * 0.3));
			// A gale draws long, thin streaks of foam down the wind (Beaufort 8+).
			float gale = smoothstep(0.03, 0.09, whitecaps);
			if (gale > 0.0) {
				float windrow = smoothstep(0.7, 0.92, texture(foam_texture, vec2(fuv.x / 140.0, fuv.y / 2.6)).r) * (0.4 + 0.6 * patches);
				caps = max(caps, windrow * gale * 0.25);
			}
			// Once the crests are too small to follow, the caps are their mean:
			// a faint whitening by Monahan's share and the spent foam's (any
			// pattern drawn at that scale tiles into a lattice towards the
			// horizon).
			float far_caps = whitecaps * 0.9 + gale * 0.02;
			caps = mix(caps, far_caps, smoothstep(0.1, 0.35, footprint / max(dominant_lambda, 0.1)));
			// Below about 5 m/s Monahan's share is a few isolated caps: none drawn.
			caps *= smoothstep(0.0, 3.0, depth) * smoothstep(0.0008, 0.004, whitecaps);
		}
		foam = clamp(max(shore_foam, caps), 0.0, 1.0);
	}

	// ── Water column: Beer-Lambert over the bake ──
	vec3 sigma = absorption;
	vec3 scatter = deep_scatter;
	// Warm water carries more life: a touch greener. A bloom greens and
	// clouds it; sediment browns it.
	scatter = mix(scatter, scatter * vec3(0.9, 1.15, 0.85), temperature_factor * 0.5);
	sigma += vec3(0.25, 0.04, 0.45) * algae_density + vec3(0.15, 0.2, 0.3) * turbidity;
	scatter = mix(scatter, vec3(0.006, 0.024, 0.010), algae_density * 0.85);
	scatter = mix(scatter, vec3(0.016, 0.017, 0.011), turbidity * 0.7);
	float cos_v = clamp(dot(n, view_w), 0.02, 1.0);
	float sin_t = sqrt(1.0 - cos_v * cos_v) / IOR_WATER;
	float mu_t = sqrt(1.0 - sin_t * sin_t);
	float water_depth = max(depth, 0.0);
	vec3 transmittance = exp(-sigma * water_depth * (1.0 + 1.0 / mu_t));
	vec3 seabed = mix(seabed_sediment, seabed_rock, smoothstep(0.35, 1.2, slope));
	// Sunlit ripples focus into caustics on a shallow seabed.
	if (water_depth < 6.0) {
		float caustic = texture(caustics_texture, p0 / 5.0 + drift * 0.03).r;
		seabed *= 0.85 + 0.6 * caustic * smoothstep(6.0, 0.5, water_depth);
	} else {
		seabed *= 0.85;
	}
	vec3 body = scatter * (1.0 - transmittance) + seabed * transmittance;

	float roughness = sqrt(base_roughness * base_roughness + 0.0004 * hs + 2.0 * lost_variance);

	if (FRONT_FACING) {
		// A facet must never mirror what lies below the horizon (the probe
		// and the sky have nothing there): bend it to the half vector of the
		// view and the lowest allowed reflection.
		vec3 r = reflect(-view_w, n);
		if (r.y < MIN_REFLECTION_UP) {
			n = normalize(view_w + normalize(vec3(r.x, MIN_REFLECTION_UP, r.z)));
		}
		// The planar mirror (G2-03) carries the share `mirror.a` of the
		// reflection, added as emission with water's Schlick fresnel; the
		// engine's sky reflection and sun highlight keep the rest (F0 scales
		// with SPECULAR^2, and below F0 0.02 the engine's grazing term scales
		// with F0 too, so SPECULAR * sqrt(1 - a) hands it exactly 1 - a).
		// The view ray's point on the still water plane looks it up.
		vec3 flat_point = CAMERA_POSITION_WORLD - to_camera * (CAMERA_POSITION_WORLD.y - origin_y) / max(to_camera.y, 1e-3);
		vec4 mirror = planar_sample(flat_point, view_w, n, roughness);
		mirror.a *= 1.0 - foam;
		float cos_r = clamp(dot(n, view_w), 0.0, 1.0);
		float fresnel = WATER_F0 + (1.0 - WATER_F0) * pow(1.0 - cos_r, 5.0);
		EMISSION = mirror.rgb * fresnel * mirror.a;
		ALBEDO = mix(body, foam_color, foam);
		ROUGHNESS = mix(clamp(roughness, 0.02, 0.4), 0.85, foam);
		SPECULAR = mix(WATER_SPECULAR, 0.1, foam) * sqrt(1.0 - mirror.a);
		METALLIC = 0.0;
		vec3 nf = normalize(mix(n, vec3(0.0, 1.0, 0.0), foam * 0.6));
		NORMAL = normalize((VIEW_MATRIX * vec4(nf, 0.0)).xyz);
	} else {
		// From below: daylight through Snell's window, the deep water mirrored
		// outside it.
		float up = clamp(-dot(view_w, n), 0.0, 1.0);
		float window = smoothstep(SNELL_COS - 0.04, SNELL_COS + 0.04, up);
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
		ROUGHNESS = 1.0;
		EMISSION = mix(scatter * 40.0 * underside_light, underside_light, window) + foam_color * foam * underside_light * 0.4;
		NORMAL = normalize((VIEW_MATRIX * vec4(-n, 0.0)).xyz);
	}
}
