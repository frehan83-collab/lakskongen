shader_type spatial;
render_mode world_vertex_coords, cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

// === Fjord water, optics v1 (G1-05) ===
//
// OPAQUE water: no ALPHA, no screen or depth texture. The colour of the water
// column comes from Beer-Lambert absorption over the baked bathymetry
// (FjordTerrain's water bake: depth, shore distance, exposure, seabed slope),
// so deep water reads dark teal-black and the terrain never shows through it,
// while shallow water lets the seabed through. The surface reflects the sky
// and the reflection probe through the engine's Schlick Fresnel (F0 = 0.02,
// SPECULAR 0.25) and GGX highlight.
//
// Waves: the eight fixed bands of SeaStateModel (JONSWAP from the wind and
// the fetch, Carter 1982), each drawn as three Gerstner components spread
// round the band's heading and slightly detuned, so the sea never looks
// like corrugated sheet. EnvironmentDirector writes one amplitude set for
// the sheltered basin and one for the open coast; the baked exposure blends
// them. Phases are integrated on the CPU, so the waves never jump. Normals
// are analytic per pixel; a component too fine for the pixel footprint fades
// out of the normal and its slope variance moves into the roughness instead
// of aliasing into glitter.
//
// Foam: lacy surf where the shore distance field is small, running in;
// whitecaps over Monahan's share of the surface, on the crests of the local
// sea, with foam patches lingering downwind.
//
// Seen from below (cull_disabled, !FRONT_FACING): Snell's window (48.6 deg)
// shows the daylight, outside it total internal reflection mirrors the deep
// water.

#define WAVES 24

// ── Waves (written by EnvironmentDirector) ───────────────────────────────────
// wave_shape[i] = (direction x, direction z, wavenumber k = 2 pi / lambda, choppiness Q)
uniform vec4 wave_shape[WAVES];
// wave_amp[i] = (amplitude in the sheltered basin m, amplitude on the open coast m, phase rad, 0)
uniform vec4 wave_amp[WAVES];
// Whitecap coverage (Monahan fraction) and the local wind's ripple strength.
uniform float whitecaps : hint_range(0.0, 1.0) = 0.0;
// Crest field level above which the sea breaks (from Monahan's share,
// EnvironmentDirector.whitecap_threshold()).
uniform float whitecap_threshold = 4.0;
uniform float ripple_strength : hint_range(0.0, 2.0) = 0.3;
// Significant wave height of each sea (m): surf width, crest height, roughness.
uniform vec2 sea_height = vec2(0.1, 0.5);
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

const float TAU_ = 6.28318530718;
const float IOR_WATER = 1.333;
const float SNELL_COS = 0.6615; // cos(48.6 deg)
const float MIN_REFLECTION_UP = 0.03;
// The whitecap field: a crest gate (0 in the troughs, CREST_WEIGHT on the
// crests) plus breaker noise of BREAKER_SPREAD (texture A, uniform on 0-1),
// so short patches break at random along the crests instead of
// whole crest lines. The director's threshold mapping is calibrated on it.
const float CREST_WEIGHT = 2.0;
const float BREAKER_SPREAD = 2.0;

varying vec3 v_world;
varying vec2 v_p0;

vec4 bake_at(vec2 xz) {
	vec2 uv = ((xz - bake_origin) / bake_spacing + 0.5) / bake_texels;
	vec4 b = textureLod(water_bake, uv, 0.0);
	vec2 edge = min(uv, 1.0 - uv);
	float inside = smoothstep(0.0, 0.015, min(edge.x, edge.y)) * has_bake;
	// Beyond the bake: the open ocean.
	return mix(vec4(200.0, 8000.0, 1.0, 0.0), b, inside);
}

// Amplitude of component i at a point: the basin's or the coast's sea by
// exposure, fading out where the water shoals to nothing.
float component_amplitude(int i, float exposure, float depth) {
	float a = mix(wave_amp[i].x, wave_amp[i].y, exposure);
	float lambda = TAU_ / max(wave_shape[i].z, 1e-4);
	return a * (1.0 - exp(-max(depth, 0.0) / (0.08 * lambda + 0.5)));
}

vec3 displacement(vec2 p, float exposure, float depth, float grid_spacing) {
	vec3 d = vec3(0.0);
	for (int i = 0; i < WAVES; i++) {
		vec4 sh = wave_shape[i];
		float lambda = TAU_ / max(sh.z, 1e-4);
		// The grid draws a component only where it has >= 4 vertices per wavelength.
		float a = component_amplitude(i, exposure, depth) * smoothstep(3.0 * grid_spacing, 6.0 * grid_spacing, lambda);
		float ph = mod(sh.z * dot(sh.xy, p) - wave_amp[i].z, TAU_);
		d.y += a * sin(ph);
		d.xz += sh.w * a * sh.xy * cos(ph);
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
	VERTEX = vec3(p0.x, origin.y, p0.y) + disp;
	v_world = VERTEX;
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
	float pixel_angle = 2.0 / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float footprint = length(to_camera) * pixel_angle / max(abs(view_w.y), 0.12) + 1e-4;
	float hs = mix(sea_height.x, sea_height.y, exposure);

	// ── Analytic Gerstner normal, band-limited to the pixel footprint ──
	vec3 n = vec3(0.0, 1.0, 0.0);
	float lost_variance = 0.0;
	// Surface height and its variance: the whitecaps break on the highest
	// crests of the energy-carrying waves.
	float height = 0.0;
	float height_var = 0.0;
	// Energy-weighted wavelength of the local sea (sets when its crests
	// become too small to draw one by one).
	float energy = 0.0;
	float energy_lambda = 0.0;
	for (int i = 0; i < WAVES; i++) {
		vec4 sh = wave_shape[i];
		float lambda = TAU_ / max(sh.z, 1e-4);
		float a = component_amplitude(i, exposure, depth);
		float ka = sh.z * a;
		float keep = smoothstep(2.0 * footprint, 6.0 * footprint, lambda);
		lost_variance += 0.5 * ka * ka * (1.0 - keep * keep);
		float ph = mod(sh.z * dot(sh.xy, p0) - wave_amp[i].z, TAU_);
		float c = cos(ph);
		float sn = sin(ph);
		n.x -= keep * sh.x * ka * c;
		n.z -= keep * sh.y * ka * c;
		n.y -= keep * sh.w * ka * sn;
		height += a * sn;
		height_var += 0.5 * a * a;
		energy += a * a;
		energy_lambda += a * a * lambda;
	}
	float dominant_lambda = energy_lambda / max(energy, 1e-8);
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


	// ── Foam ──
	vec2 fuv = q - vec2(sea_time * 0.25, 0.0);
	float lace = texture(foam_texture, fuv / 7.0).r;
	float bubbles = texture(foam_texture, fuv / 2.3 + 0.37).b;
	float patches = texture(foam_texture, fuv / (60.0 + 90.0 * hs)).g;
	// Shore: a lapping line in a calm, a surf band a few metres wide once
	// the sea builds, its lines running in.
	float surge = smoothstep(0.05, 0.7, hs);
	float surf_width = 1.5 + 6.0 * surge + 3.0 * hs;
	float shore_band = 1.0 - smoothstep(0.0, surf_width, shore);
	float lines = 0.5 + 0.5 * sin(TAU_ * (shore / (2.5 + 3.0 * hs) + sea_time * 0.11) + patches * 3.0);
	float shore_foam = shore_band * smoothstep(0.35, 0.95, shore_band * 0.6 + lines * 0.45) * (0.45 + 0.55 * lace);
	shore_foam *= step(-0.5, shore) * mix(0.35, 1.0, surge);
	// Whitecaps: the crest height in standard deviations plus a breaker noise
	// (which stretch of a crest breaks, at the scale of the local sea; it
	// slowly cross-fades so caps form and die) exceeds whitecap_threshold,
	// the normal quantile of Monahan's share (from the director), widened
	// for the noise's own spread. A bright core, a thinner lace of spent
	// foam just below it. Once the dominant crests shrink to a few pixels
	// the caps are drawn as their mean cover, clumped into foam fields.
	float groups = texture(foam_texture, q / 420.0 + vec2(0.31, 0.17)).a;
	vec2 bq = q - vec2(sea_time * 0.6, 0.0);
	float fade = 0.5 + 0.5 * sin(sea_time * 0.21);
	float breaker_small = mix(texture(foam_texture, bq / 11.0).a, texture(foam_texture, bq / 13.0 + 0.5).a, fade);
	float breaker_large = mix(texture(foam_texture, bq / 150.0).a, texture(foam_texture, bq / 170.0 + 0.5).a, fade);
	float breaker = mix(breaker_small, breaker_large, exposure);
	float crest_gate = smoothstep(0.0, 1.5, height / sqrt(max(height_var, 1e-8)));
	float crest_z = CREST_WEIGHT * crest_gate + BREAKER_SPREAD * (breaker - 0.5) + (lace - 0.5) * 0.3;
	float threshold = whitecap_threshold;
	float core = smoothstep(threshold, threshold + 0.3, crest_z);
	float spent = smoothstep(threshold - 0.35, threshold, crest_z) * lace * lace;
	float caps = max(core * (0.65 + 0.35 * lace) * (0.85 + 0.15 * bubbles), spent * 0.35);
	float far_caps = clamp(whitecaps * 2.6 * smoothstep(0.3, 0.85, groups), 0.0, 0.5) * (0.75 + 0.5 * patches);
	caps = mix(caps, far_caps, smoothstep(0.05, 0.22, footprint / max(dominant_lambda, 0.1)));
	// Below about 5 m/s Monahan's share is a few isolated caps: none drawn.
	caps *= smoothstep(0.0, 3.0, depth) * smoothstep(0.0008, 0.004, whitecaps);
	float foam = clamp(max(shore_foam, caps), 0.0, 1.0);

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
	float caustic = texture(caustics_texture, p0 / 5.0 + drift * 0.03).r;
	seabed *= 0.85 + 0.6 * caustic * smoothstep(6.0, 0.5, water_depth);
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
		ALBEDO = mix(body, foam_color, foam);
		ROUGHNESS = mix(clamp(roughness, 0.02, 0.4), 0.85, foam);
		SPECULAR = mix(0.25, 0.1, foam);
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
