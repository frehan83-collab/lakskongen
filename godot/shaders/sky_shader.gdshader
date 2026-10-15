shader_type sky;
// The cloud layers can be drawn in the half- or quarter-resolution pass and
// composited in the full-resolution one (cloud_pass, from the quality tier,
// P0-04): clouds are soft, so a quarter of the pixels gives the same look for
// a quarter of the cost. The pass not in use returns at once.
render_mode use_half_res_pass, use_quarter_res_pass;

// === Vestland Light sky (G1-04) ===
// Written for this project. Physical references (formulas, not code):
//  - single scattering through a spherical exponential atmosphere
//    (Nishita et al. 1993), Rayleigh/Mie/ozone coefficients from Bruneton
//    and Neyret, "Precomputed Atmospheric Scattering" (2008);
//  - optical depth toward the sun from Schüler's approximation of the
//    Chapman grazing-incidence function (GPU Pro 3, 2012), which also
//    gives the Earth's shadow at twilight;
//  - cloud base light from the two-stream diffuse transmission
//    1 / (1 + 0.75 tau (1 - g));
//  - aurora: 557.7 nm green curtains 100-200 km up with a 630 nm red top
//    in strong displays.
//
// The sun is the DirectionalLight: its disc is drawn at LIGHT0_DIRECTION
// with LIGHT0_COLOR * LIGHT0_ENERGY, so the sky never disagrees with the
// light. The moon light is "Light Only" (not in the sky); its direction
// comes in as moon_direction. EnvironmentDirector.gd is the only writer of
// every uniform below. TIME is not used: sky_time animates instead, and the
// cubemap (radiance) pass skips the expensive detail.

uniform float sky_time = 0.0;
// Clouds: coverage comes from the weather only; density thickens the base
// (storm); offset drifts with the wind (km).
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.3;
uniform float cloud_density : hint_range(0.0, 1.0) = 0.3;
uniform vec2 cloud_offset = vec2(0.0);
// Aurora: intensity already includes darkness, latitude, activity and the
// clear sky; offset_km puts the brightest curtains north (+) or south (-).
uniform float aurora_intensity : hint_range(0.0, 1.0) = 0.0;
uniform float aurora_offset_km = 200.0;
uniform float aurora_kp : hint_range(0.0, 9.0) = 2.0;
uniform vec3 moon_direction = vec3(0.0, -1.0, 0.0);
uniform float moon_illumination : hint_range(0.0, 1.0) = 0.0;
// The environment's fog light colour (the director's fog_light_color).
uniform vec3 haze_color : source_color = vec3(0.6, 0.66, 0.72);
// The sky's own horizon haze when visibility drops, and the colour under the
// horizon. Under a mood whose land fog is a shade darker than the horizon
// (EnvironmentMood fog_value, G3-05) it is brighter than haze_color, so far
// mountains stand darker against the sky behind them.
uniform vec3 sky_haze_color : source_color = vec3(0.6, 0.66, 0.72);
uniform float haze_amount : hint_range(0.0, 1.0) = 0.0;
// Scene-linear scale of the scattered skylight.
uniform float sky_energy = 1.0;
// The clear sky at the zenith (scene-linear, sky_energy applied): the same
// for every pixel, so the director computes it once a frame
// (EnvironmentDirector sky_radiance + twilight + night floor) instead of
// every pixel marching it. It lights the clouds from above.
uniform vec3 sky_zenith = vec3(0.06, 0.12, 0.3);
// Quality (EnvironmentDirector, RenderQuality): where the clouds are drawn
// (0 full resolution, 1 half, 2 quarter), the low layer's march steps and
// its noise octaves in the background pass.
uniform int cloud_pass : hint_range(0, 2) = 0;
uniform int cloud_steps : hint_range(2, 12) = 6;
uniform int cloud_octaves : hint_range(3, 6) = 6;
// The whole sky but its sharp detail (stars, moon and sun discs) marched in
// the half (1) or quarter (2) resolution pass, the detail added per pixel
// (0: per pixel, cloud_pass decides). The planar mirror's sky (G2-03): the
// water shows it through ripples, so a quarter of its pixels is plenty;
// sky_pass_aurora puts the aurora in that pass too.
uniform int sky_pass : hint_range(0, 2) = 0;
uniform bool sky_pass_aurora = false;
// The engine blends the environment's fog into every sky pass, the reduced-
// resolution ones too (Godot sky.glsl fog_process: rgb' = rgb (1 - k) +
// k fog_light_color + s sun_scatter_light, with k = sky_affect (1 -
// aerial_perspective) and s = sky_affect sun_scatter), so composited clouds
// would be fogged twice. The director hands over k and s; the full pass
// takes the first fogging back out (haze_color is the fog light colour).
uniform float sky_fog_k = 0.0;
uniform float sky_fog_scatter = 0.0;
// Mood grade (G3-05, EnvironmentMood): the clear sky takes the mood's hue
// (a gradient from the horizon, warmer or pinker on the sun's side, to the
// zenith) and the cloud deck the mood's deck colour, each keeping the
// luminance the atmosphere gives it. Linear colours.
uniform vec3 grade_zenith = vec3(0.11, 0.26, 0.48);
uniform vec3 grade_horizon = vec3(0.67, 0.75, 0.79);
uniform vec3 grade_horizon_sun = vec3(0.67, 0.75, 0.79);
uniform float grade_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec3 grade_deck = vec3(0.82, 0.85, 0.87);
uniform float grade_deck_amount : hint_range(0.0, 1.0) = 0.0;

const float PI_ = 3.14159265;
const float R_EARTH = 6360.0;      // km
const float R_TOP = 6420.0;        // km
const float H_RAYLEIGH = 8.0;      // km
const float H_MIE = 1.2;           // km
const vec3 BETA_R = vec3(5.802e-3, 13.558e-3, 33.1e-3);  // 1/km
const float BETA_M = 3.0e-3;       // 1/km scattering (clean maritime air)
const float BETA_M_EXT = 3.33e-3;  // 1/km extinction
// Ozone absorption over its ~15 km thick layer centred at 25 km.
const vec3 OZONE_ZENITH = vec3(0.00975, 0.02822, 0.00128);
const float OZONE_RADIUS = 6385.0;
const float OBSERVER_H = 0.1;      // km
const float MIE_G = 0.8;
// Scene-linear scale of the sun in the sky model, and of sunlight on clouds
// (the DirectionalLight's energy scale), so a white cloud base and a white
// wall under the same sun match.
const float SKY_SCALE = 30.0;
const float CLOUD_LIGHT = 2.6;
const vec3 AURORA_GREEN = vec3(0.19, 0.89, 0.36);  // #78F2A0 in linear
const vec3 AURORA_RED = vec3(0.95, 0.09, 0.12);
const vec3 AURORA_VIOLET = vec3(0.45, 0.12, 0.85);
const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

// ── Noise (PCG integer hash, value noise) ─────────────────────────────────
uint pcg(uint v) {
	uint state = v * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

float hash2(vec2 p) {
	uvec2 q = uvec2(ivec2(floor(p)) + ivec2(65536));
	return float(pcg(q.x + pcg(q.y))) * (1.0 / 4294967295.0);
}

float hash3(vec3 p) {
	uvec3 q = uvec3(ivec3(floor(p)) + ivec3(65536));
	return float(pcg(q.x + pcg(q.y + pcg(q.z)))) * (1.0 / 4294967295.0);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = hash2(i);
	float b = hash2(i + vec2(1.0, 0.0));
	float c = hash2(i + vec2(0.0, 1.0));
	float d = hash2(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float vnoise1(float x) {
	float i = floor(x);
	float f = fract(x);
	float u = f * f * (3.0 - 2.0 * f);
	return mix(hash2(vec2(i, 17.0)), hash2(vec2(i + 1.0, 17.0)), u);
}

float fbm(vec2 p, int octaves) {
	float sum = 0.0;
	float amp = 0.5;
	float norm = 0.0;
	mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int i = 0; i < octaves; i++) {
		sum += amp * vnoise(p);
		norm += amp;
		p = rot * p * 2.03 + vec2(13.1, 7.7);
		amp *= 0.5;
	}
	return sum / norm;
}

// ── Atmosphere ────────────────────────────────────────────────────────────
// Density-weighted path length to space, in scale heights, from a point at
// height h_norm (in scale heights) looking at cos_z from the local zenith.
float chapman(float x_scale, float h_norm, float cos_z) {
	float c = sqrt(x_scale + h_norm);
	if (cos_z >= 0.0) {
		return c / (c * cos_z + 1.0) * exp(-h_norm);
	}
	float x0 = sqrt(max(1.0 - cos_z * cos_z, 0.0)) * (x_scale + h_norm);
	float c0 = sqrt(x0);
	// The planet blocks the sun when the ray dips far below the horizon:
	// cap the exponent so the result is huge, never inf or NaN.
	return 2.0 * c0 * exp(min(x_scale - x0, 60.0)) - c / (1.0 - c * cos_z) * exp(-h_norm);
}

vec3 sun_transmittance(float h_km, float cos_z) {
	float ch_r = chapman(R_EARTH / H_RAYLEIGH, h_km / H_RAYLEIGH, cos_z);
	float ch_m = chapman(R_EARTH / H_MIE, h_km / H_MIE, cos_z);
	float r = R_EARTH + h_km;
	float s = r / OZONE_RADIUS;
	float ozone_mass = 1.0 / sqrt(max(1.0 - s * s * (1.0 - cos_z * cos_z), 0.02));
	if (cos_z < 0.0 && ch_r > 200.0) {
		ozone_mass = 0.0; // fully shadowed anyway
	}
	vec3 tau = BETA_R * H_RAYLEIGH * ch_r + vec3(BETA_M_EXT * H_MIE * ch_m) + OZONE_ZENITH * ozone_mass;
	return exp(-tau);
}

float phase_rayleigh(float mu) {
	return 3.0 / (16.0 * PI_) * (1.0 + mu * mu);
}

float phase_hg(float mu, float g) {
	float g2 = g * g;
	return (1.0 - g2) / (4.0 * PI_ * pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5));
}

// Distance from a point at radius r looking at mu (cos zenith) to a sphere.
float ray_sphere_exit(float r, float mu, float radius) {
	float disc = r * r * (mu * mu - 1.0) + radius * radius;
	return -r * mu + sqrt(max(disc, 0.0));
}

// Single scattering plus a multiple-scattering fill, for skylight from the
// sun (scaled by the irradiance at the top of the atmosphere).
vec3 atmosphere(vec3 dir, vec3 sun_dir, int steps, float mie_g, out vec3 view_transmittance) {
	float r0 = R_EARTH + OBSERVER_H;
	float mu = max(dir.y, 0.002);
	float t_max = ray_sphere_exit(r0, mu, R_TOP);
	vec3 sum_r = vec3(0.0);
	vec3 sum_m = vec3(0.0);
	float od_r = 0.0;
	float od_m = 0.0;
	float fsteps = float(steps);
	vec3 view_dir = normalize(vec3(dir.x, mu, dir.z));
	for (int i = 0; i < steps; i++) {
		float a = float(i) / fsteps;
		float b = float(i + 1) / fsteps;
		float t0 = t_max * a * a;
		float t1 = t_max * b * b;
		float t = 0.5 * (t0 + t1);
		float ds = t1 - t0;
		vec3 p = vec3(0.0, r0, 0.0) + view_dir * t;
		float r = length(p);
		float h = r - R_EARTH;
		float d_r = exp(-h / H_RAYLEIGH) * ds;
		float d_m = exp(-h / H_MIE) * ds;
		od_r += d_r * 0.5;
		od_m += d_m * 0.5;
		vec3 t_view = exp(-(BETA_R * od_r + vec3(BETA_M_EXT * od_m)));
		vec3 t_sun = sun_transmittance(h, dot(p / r, sun_dir));
		sum_r += d_r * t_view * t_sun;
		sum_m += d_m * t_view * t_sun;
		od_r += d_r * 0.5;
		od_m += d_m * 0.5;
	}
	view_transmittance = exp(-(BETA_R * od_r + vec3(BETA_M_EXT * od_m)));
	float cos_theta = dot(view_dir, sun_dir);
	vec3 single = BETA_R * phase_rayleigh(cos_theta) * sum_r + BETA_M * phase_hg(cos_theta, mie_g) * sum_m;
	// Multiple scattering, isotropic: keeps twilight and shadowed sky blue.
	// Weighted toward the zenith: along the long horizontal path it would
	// saturate into a white band five times brighter than the zenith.
	vec3 multi = (BETA_R * sum_r + BETA_M * sum_m) * (0.18 / (4.0 * PI_)) * (0.35 + 0.65 * mu);
	// Grade: a deeper zenith and a softer horizon band (art direction, D13).
	return (single + multi) * (0.7 + 0.5 * mu);
}

// ── Clouds ────────────────────────────────────────────────────────────────
// Distance (km) from the observer to a cloud shell at height h_km.
float shell_distance(vec3 dir, float h_km) {
	return ray_sphere_exit(R_EARTH + OBSERVER_H, dir.y, R_EARTH + h_km);
}

// The low layer's noise at a horizontal point (km): domain-warped FBM.
float low_noise(vec2 p, int octaves) {
	vec2 q = p * 0.22 + cloud_offset * 0.22;
	vec2 warp = vec2(fbm(q * 0.5 + vec2(3.1, 1.7), 2), fbm(q * 0.5 + vec2(8.3, 2.8), 2)) - 0.5;
	return fbm(q + warp * 1.4, octaves);
}

// Density (0-1) of the low layer from its noise at a normalised height in
// the slab (0 base, 1 top): flat bases, tops that narrow like cumulus,
// and one closed deck (thicker and thinner in places) under overcast.
float low_density_at(float n, float hn) {
	float cover = cloud_coverage;
	float closed = smoothstep(0.82, 0.97, cover);
	float threshold = mix(0.78, 0.18, cover);
	float soft = mix(0.10, 0.28, cover);
	float rise = mix(0.30, 0.05, closed) * hn * hn;
	float d = smoothstep(threshold + rise - soft * 0.2, threshold + rise + soft, n);
	d *= smoothstep(0.0, 0.08, hn) * (1.0 - smoothstep(0.92, 1.0, hn));
	return max(d, closed * (0.55 + 0.45 * n));
}

float low_density(vec2 p, int octaves) {
	return low_density_at(low_noise(p, octaves), 0.4);
}

float high_density(vec2 p, int octaves) {
	// Cirrus: stretched along the wind, thin.
	vec2 q = p * vec2(0.035, 0.11) + cloud_offset * 0.05;
	float n = fbm(q, octaves);
	float streak = fbm(q * vec2(1.0, 4.0) + 5.0, 2);
	float cover = clamp(0.25 + 0.6 * cloud_coverage, 0.0, 0.9);
	return smoothstep(1.0 - cover * 0.75, 1.15 - cover * 0.6, n * 0.75 + streak * 0.35) * (1.0 - smoothstep(0.85, 1.0, cloud_coverage));
}

// The low layer is a slab from 1.2 to 2.4 km marched in a few steps, so
// cumulus get flat bases, rounded tops and solid banks at the horizon (a
// flat layer turns into streaks there). Lighting per sample: sunlight at
// cloud height through the column above (two-stream diffuse transmission),
// relief toward the sun, side light from a low sun, a silver lining, and
// skylight; the column's optical thickness runs from fair-weather cumulus
// (6) to storm nimbostratus (60).
vec4 cloud_layer_low(vec3 dir, vec3 sun_dir, vec3 sky_top, vec3 moon_light, int octaves, int steps, float jitter) {
	if (dir.y <= 0.0 || cloud_coverage <= 0.01) {
		return vec4(0.0);
	}
	const float H0 = 1.2;
	const float H1 = 2.4;
	float t0 = shell_distance(dir, H0);
	float t1 = shell_distance(dir, H1);
	float tau_max = mix(6.0, 60.0, cloud_density);
	float sigma = tau_max / (H1 - H0); // per km, a vertical column of density 1 gives tau_max
	float closed = smoothstep(0.8, 0.97, cloud_coverage);
	vec3 sun_in = sun_transmittance(0.5 * (H0 + H1), sun_dir.y) * CLOUD_LIGHT;
	// Under a closed deck the eye sees the diffused light as near-neutral.
	sun_in = mix(sun_in, vec3(dot(sun_in, vec3(0.2126, 0.7152, 0.0722))), 0.9 * closed);
	float mu = dot(dir, sun_dir);
	float sun_up = max(sun_dir.y, 0.0);
	float low_sun = pow(1.0 - clamp(sun_dir.y * 5.0, 0.0, 1.0), 2.0) * smoothstep(-0.06, 0.0, sun_dir.y)
		* (1.0 - 0.85 * closed); // no side light under a closed deck
	float silver = phase_hg(mu, 0.75) * 2.5;
	vec3 open_sky = mix(sky_top, vec3(dot(sky_top, vec3(0.2126, 0.7152, 0.0722))), cloud_coverage * cloud_coverage);
	vec3 ambient = open_sky * mix(0.9, 0.35, cloud_density);
	vec2 toward = normalize(sun_dir.xz + vec2(1e-4)) * 0.8;

	// Near the horizon the slab runs for tens of km: march only its first
	// 10 km (the clouds behind are hidden or lost in haze), so the steps stay
	// close enough not to band.
	t1 = min(t1, t0 + 10.0);
	float ds = (t1 - t0) / float(steps);
	// Fewer octaves with distance: detail finer than a pixel only aliases
	// into stripes near the horizon. One octave less per doubling past 6 km.
	octaves = max(octaves - int(clamp(log2(max(t0, 1.0) / 6.0), 0.0, float(octaves - 2))), 2);
	float transmittance = 1.0;
	vec3 light = vec3(0.0);
	for (int i = 0; i < steps; i++) {
		float t = t0 + (float(i) + jitter) * ds;
		vec3 p = dir * t;
		// Height along the ray over the curved Earth.
		float h = OBSERVER_H + t * dir.y + t * t * (1.0 - dir.y * dir.y) / (2.0 * R_EARTH);
		float hn = clamp((h - H0) / (H1 - H0), 0.0, 1.0);
		float n = low_noise(p.xz, octaves);
		float d = low_density_at(n, hn);
		if (d <= 0.001) {
			continue;
		}
		float tau_step = d * sigma * ds;
		// The column above this sample, and relief toward the sun.
		float tau_column = d * tau_max;
		float t_d = 1.0 / (1.0 + 0.75 * tau_column * 0.15);
		float d_sun = low_density_at(low_noise(p.xz + toward, max(octaves - 3, 2)), min(hn + 0.15, 1.0));
		float shade = exp(-d_sun * tau_max * 0.08) * mix(0.75, 1.0, hn);
		vec3 c = sun_in * sun_up * t_d * (0.45 + 0.55 * shade) * mix(0.8, 1.2, hn);
		// The sunlit sides face the low sun: seen from anywhere, a little
		// brighter toward it (the silver lining adds the forward peak).
		c += sun_in * low_sun * (0.6 + 0.4 * mu) * 0.9 * shade;
		c += sun_in * silver * exp(-tau_column * 0.5) * shade;
		c += ambient * (1.0 - 0.3 * d) * (0.8 + 0.4 * hn);
		c += moon_light * (0.25 + 0.6 * shade);
		float absorbed = 1.0 - exp(-tau_step);
		light += transmittance * absorbed * c;
		transmittance *= 1.0 - absorbed;
		if (transmittance < 0.02) {
			break;
		}
	}
	float alpha = 1.0 - transmittance;
	if (alpha <= 0.001) {
		return vec4(0.0);
	}
	// Aerial perspective: far clouds fade into the horizon sky.
	float fade = exp(-t0 / 70.0);
	return vec4(light / alpha, alpha * mix(0.35, 1.0, fade));
}

// Scud: ragged, dark, fast fragments under a storm deck (0.6 km).
vec4 cloud_layer_scud(vec3 dir, vec3 sky_top, int octaves) {
	float storm = smoothstep(0.6, 1.0, cloud_density) * smoothstep(0.85, 1.0, cloud_coverage);
	if (dir.y <= 0.0 || storm <= 0.0) {
		return vec4(0.0);
	}
	float t = shell_distance(dir, 0.6);
	vec2 q = dir.xz * t * 0.55 + cloud_offset * 0.9;
	float n = fbm(q * vec2(1.0, 0.6), octaves);
	float d = smoothstep(0.52, 0.75, n) * storm;
	vec3 color = vec3(dot(sky_top, vec3(0.2126, 0.7152, 0.0722))) * 0.3;
	return vec4(color, d * 0.85 * exp(-t / 25.0));
}

vec4 cloud_layer_high(vec3 dir, vec3 sun_dir, vec3 sky_top, vec3 moon_light, int octaves) {
	if (dir.y <= 0.0) {
		return vec4(0.0);
	}
	float h = 8.0;
	float t = shell_distance(dir, h);
	vec2 p = dir.xz * t;
	float d = high_density(p, octaves);
	if (d <= 0.001) {
		return vec4(0.0);
	}
	vec3 sun_in = sun_transmittance(h, sun_dir.y) * CLOUD_LIGHT;
	float mu = dot(dir, sun_dir);
	vec3 color = sun_in * (0.35 + phase_hg(mu, 0.6) * 1.5) + sky_top * 0.8 + moon_light * 0.5;
	float alpha = d * 0.55 * exp(-t / 160.0);
	return vec4(color, alpha);
}

// ── Night sky ─────────────────────────────────────────────────────────────
vec3 stars(vec3 dir) {
	vec3 p = dir * 220.0;
	vec3 cell = floor(p);
	float h = hash3(cell);
	if (h < 0.965) {
		return vec3(0.0);
	}
	vec3 jitter = vec3(hash3(cell + 11.0), hash3(cell + 23.0), hash3(cell + 37.0));
	vec3 centre = cell + 0.2 + 0.6 * jitter;
	float dist = length(p - centre);
	float bright = pow((h - 0.965) / 0.035, 6.0) * 2.5 + 0.06;
	float twinkle = 0.75 + 0.25 * sin(sky_time * (1.5 + 3.0 * jitter.x) + jitter.y * 40.0);
	vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.86, 0.7), jitter.z);
	return tint * bright * twinkle * smoothstep(0.18, 0.0, dist);
}

vec3 moon_disc(vec3 dir, vec3 sun_dir) {
	float radius = 0.0046; // 0.26 deg
	float c = dot(dir, moon_direction);
	if (c < cos(radius * 1.2) || moon_direction.y < -0.05) {
		return vec3(0.0);
	}
	vec3 right = normalize(cross(moon_direction, vec3(0.0, 1.0, 0.0) + vec3(1e-4, 0.0, 0.0)));
	vec3 up = cross(right, moon_direction);
	vec2 uv = vec2(dot(dir, right), dot(dir, up)) / radius;
	float r2 = dot(uv, uv);
	if (r2 > 1.0) {
		return vec3(0.0);
	}
	vec3 n = uv.x * right + uv.y * up - sqrt(1.0 - r2) * moon_direction;
	float lit = clamp(dot(n, sun_dir) * 4.0 + 0.1, 0.0, 1.0);
	float maria = 0.75 + 0.25 * fbm(uv * 2.2 + 4.0, 3);
	float edge = smoothstep(1.0, 0.92, r2);
	vec3 earthshine = vec3(0.004, 0.005, 0.007);
	return (vec3(1.0, 0.97, 0.9) * lit * maria * 1.6 + earthshine) * edge;
}

// Aurora curtains: vertical emitting sheets 100-300 km up that follow a
// wandering east-west line (north = base + wander(east), in km). A view ray
// crosses a sheet once: the height of that crossing picks the colour and
// brightness (sharp lower border near 100 km, slow fade upward), the east
// coordinate picks the rays, and the angle between the ray and the sheet
// sets the path length through it, so edge-on folds glow brightest. One
// analytic crossing per sheet: no banding, a few dozen ALU per pixel.
float aurora_line(float east, float base, float seed) {
	return base + (vnoise1(east * 0.0032 + seed + sky_time * 0.003) - 0.5) * 300.0
		+ sin(east * 0.0017 + seed * 3.0 + sky_time * 0.008) * 80.0;
}

vec3 aurora_sheet(vec3 dir, vec2 hd, float horiz, float base, float seed, float red, float violet) {
	// Fixed point for the ground distance x where the ray meets the sheet:
	// hd.x * x = line(hd.y * x).
	if (abs(hd.x) < 0.02) {
		return vec3(0.0);
	}
	float x = base / hd.x;
	for (int i = 0; i < 4; i++) {
		x = aurora_line(hd.y * x, base, seed) / hd.x;
	}
	if (x <= 0.0 || x > 1800.0) {
		return vec3(0.0);
	}
	float east = hd.y * x;
	float t = x / max(horiz, 1e-3);
	float h = t * dir.y + x * x / (2.0 * R_EARTH);
	if (h < 92.0 || h > 420.0) {
		return vec3(0.0);
	}
	// Lower border: sharp, gently scalloped; then a slow fade upward.
	float border = 100.0 + (vnoise1(east * 0.02 + seed * 5.0) - 0.5) * 10.0;
	float green = smoothstep(border - 4.0, border + 5.0, h) * exp(-(h - border) / 55.0) * (1.0 + 1.3 * exp(-max(h - border, 0.0) / 9.0));
	float red_top = smoothstep(170.0, 230.0, h) * exp(-(h - 230.0) / 110.0) * red;
	float violet_edge = smoothstep(border - 6.0, border, h) * (1.0 - smoothstep(border, border + 8.0, h)) * violet;
	// Rays: fine vertical striations that crawl along the curtain.
	float rays = 0.3 + 0.7 * pow(vnoise1(east * 0.11 + seed * 13.0 - sky_time * 0.3), 2.0);
	rays *= 0.55 + 0.45 * vnoise1(east * 0.025 + sky_time * 0.04 + seed);
	// Bright and faint stretches along the curtain.
	rays *= 0.25 + 1.2 * pow(vnoise1(east * 0.004 + seed * 2.0 + sky_time * 0.01), 2.0);
	// Path through the sheet: brighter where the ray grazes it.
	float slope = (aurora_line(east + 5.0, base, seed) - aurora_line(east - 5.0, base, seed)) / 10.0;
	vec3 normal = normalize(vec3(1.0, 0.0, -slope));
	float gain = 1.0 / max(abs(dot(dir, normal)), 0.12);
	gain = min(gain, 5.0) * 0.35;
	return (AURORA_GREEN * green + AURORA_RED * red_top + AURORA_VIOLET * violet_edge) * rays * gain;
}

vec3 aurora(vec3 dir) {
	if (aurora_intensity <= 0.001 || dir.y <= -0.01) {
		return vec3(0.0);
	}
	float horiz = length(dir.xz);
	vec2 hd = dir.xz / max(horiz, 1e-4); // (north, east)
	float red = smoothstep(3.5, 6.5, aurora_kp);
	float violet = smoothstep(5.5, 8.0, aurora_kp);
	vec3 acc = aurora_sheet(dir, hd, horiz, aurora_offset_km, 1.0, red, violet);
	acc += 0.6 * aurora_sheet(dir, hd, horiz, aurora_offset_km + 90.0, 5.0, red, violet);
	acc += 0.4 * aurora_sheet(dir, hd, horiz, aurora_offset_km - 70.0, 9.0, red, violet);
	// Diffuse glow around the oval, low in the sky.
	float t110 = ray_sphere_exit(R_EARTH + OBSERVER_H, max(dir.y, 0.0), R_EARTH + 110.0);
	float north110 = hd.x * t110 * horiz;
	float glow = exp(-pow((north110 - aurora_offset_km) / 260.0, 2.0)) * smoothstep(-0.01, 0.04, dir.y);
	acc += AURORA_GREEN * glow * 0.08;
	return acc * aurora_intensity;
}

// Twilight: once the sun is below the horizon the lit upper atmosphere and
// multiple scattering keep the sky blue (ozone) with an afterglow over the
// sun's azimuth; single scattering alone goes black within 6 degrees.
// Mirrored in EnvironmentDirector.twilight_radiance().
vec3 twilight(vec3 dir, vec3 sun_dir) {
	float e = degrees(asin(clamp(sun_dir.y, -1.0, 1.0)));
	float level = 0.035 * exp(min(e, 0.0) / 2.6) * smoothstep(2.0, -1.0, e);
	if (level <= 0.0) {
		return vec3(0.0);
	}
	float up = clamp(dir.y, 0.0, 1.0);
	vec2 flat_dir = normalize(dir.xz + vec2(1e-5));
	vec2 flat_sun = normalize(sun_dir.xz + vec2(1e-5));
	float toward = dot(flat_dir, flat_sun) * 0.5 + 0.5;
	float glow = pow(toward, 3.0) * exp(-up * 7.0) * smoothstep(-11.0, -1.0, e);
	// Belt of Venus: a pink band above the Earth's shadow, opposite the sun.
	float belt = pow(1.0 - toward, 3.0) * exp(-abs(up - 0.08) * 25.0) * smoothstep(-6.0, -1.0, e);
	vec3 zenith_blue = vec3(0.16, 0.30, 0.85);
	return level * (zenith_blue * (0.65 + 0.35 * (1.0 - up)) + vec3(1.0, 0.42, 0.16) * glow * 1.6 + vec3(0.75, 0.45, 0.55) * belt * 0.5);
}

// Airglow and starlight, artistically lifted so a moonless night reads as
// deep blue rather than black. Mirrored in EnvironmentDirector.NIGHT_SKY.
vec3 night_sky(vec3 dir) {
	return mix(vec3(0.0013, 0.0019, 0.0040), vec3(0.0022, 0.0027, 0.0045), pow(1.0 - clamp(dir.y, 0.0, 1.0), 3.0));
}

// The three cloud layers composited over an unknown background:
// premultiplied colour and coverage (rgb over, a).
vec4 clouds(vec3 dir, vec3 sun_dir, vec3 sky_top, vec3 moon_light, int octaves, int steps, int scud_octaves) {
	vec4 high = cloud_layer_high(dir, sun_dir, sky_top, moon_light, octaves - 2);
	vec4 acc = vec4(high.rgb * high.a, high.a);
	vec4 low = cloud_layer_low(dir, sun_dir, sky_top, moon_light, octaves, steps, 0.5);
	acc = vec4(low.rgb * low.a + acc.rgb * (1.0 - low.a), low.a + acc.a * (1.0 - low.a));
	vec4 scud = cloud_layer_scud(dir, sky_top, scud_octaves);
	acc = vec4(scud.rgb * scud.a + acc.rgb * (1.0 - scud.a), scud.a + acc.a * (1.0 - scud.a));
	return acc;
}

vec3 moon_sky_light() {
	float moon_up = smoothstep(-0.02, 0.06, moon_direction.y);
	return vec3(0.012, 0.016, 0.024) * moon_illumination * moon_up;
}
// ── Mood grade (G3-05) ────────────────────────────────────────────────────
// The mood's sky colour for a direction: the horizon (the sun's side of it
// apart), rising to the zenith colour. Mirrored in EnvironmentMood.
vec3 grade_target(vec3 dir, vec3 sun_dir) {
	float up = clamp(dir.y, 0.0, 1.0);
	vec2 flat_dir = normalize(dir.xz + vec2(1e-5));
	vec2 flat_sun = normalize(sun_dir.xz + vec2(1e-5));
	// The sun's side of the horizon: a band a few degrees deep (Blåtime's
	// pink south, Midnattssol's gold).
	float toward = pow(dot(flat_dir, flat_sun) * 0.5 + 0.5, 2.0) * exp(-up * 12.0);
	vec3 horizon = mix(grade_horizon, grade_horizon_sun, toward);
	return mix(horizon, grade_zenith, smoothstep(0.0, 0.55, pow(up, 0.75)));
}

// Re-colour `rgb` toward `target` by `amount`, keeping its luminance.
vec3 grade_to(vec3 rgb, vec3 target, float amount) {
	float lt = dot(target, LUMA);
	if (amount <= 0.0 || lt <= 1e-6) {
		return rgb;
	}
	return mix(rgb, target * (dot(rgb, LUMA) / lt), amount);
}

// The clear sky's grade, held back in the sun's aureole (its glare stays
// the sun's colour).
vec3 grade_sky(vec3 rgb, vec3 dir, vec3 sun_dir) {
	float aureole = smoothstep(0.97, 0.9995, dot(dir, sun_dir)) * smoothstep(-0.05, 0.02, sun_dir.y);
	return grade_to(rgb, grade_target(dir, sun_dir), grade_amount * (1.0 - aureole));
}

// ── Compose ───────────────────────────────────────────────────────────────

// Haze toward the horizon in poor visibility. Below the horizon the map
// holds the fog colour itself: the engine's aerial perspective samples it
// whenever the camera looks down at the water, so the clear sky must not
// shine through there under a storm deck.
vec3 haze_and_ground(vec3 color, vec3 dir, vec3 sun_dir, bool cubemap) {
	float above = smoothstep(-0.02, 0.01, dir.y);
	float horizon = pow(1.0 - clamp(dir.y, 0.0, 1.0), mix(14.0, 2.5, haze_amount));
	color = mix(color, sky_haze_color, clamp(horizon * haze_amount, 0.0, 1.0));
	// Below the horizon: the clear horizon of that azimuth (warm toward the
	// sun, cool away from it), or the fog colour under cloud and haze.
	vec3 below = sky_haze_color;
	if (above < 1.0) {
		vec3 flat_dir = normalize(vec3(dir.x, 0.035, dir.z));
		vec3 t_dummy;
		vec3 clear_h = atmosphere(flat_dir, sun_dir, cubemap ? 4 : 6, cubemap ? 0.55 : MIE_G, t_dummy) * SKY_SCALE * sky_energy
			+ (twilight(flat_dir, sun_dir) + night_sky(flat_dir)) * sky_energy;
		clear_h = grade_sky(clear_h, flat_dir, sun_dir);
		float veil = max(haze_amount, smoothstep(0.5, 0.95, cloud_coverage));
		below = mix(clear_h, sky_haze_color, veil);
		// Looking down, the map stands for the sea and the land: darker than
		// the horizon, so the ambient light from below stays a dim bounce.
		below *= mix(1.0, 0.3, smoothstep(0.0, 0.3, -dir.y));
	}
	return mix(below, color, above);
}

// The sky without its per-pixel detail (stars, the moon and sun discs and,
// unless with_aurora, the aurora): the atmosphere, twilight, night floor and
// moon wash, the clouds `cl` (premultiplied) over them, the horizon haze and
// the colour below the horizon. a: the cloud cover in front of the detail.
vec4 sky_body(vec3 dir, vec3 sun_dir, bool cubemap, bool with_aurora, vec4 cl) {
	int atm_steps = cubemap ? 6 : 12;
	vec3 view_t;
	// The radiance pass widens the Mie aureole: the sun's glare must not leak
	// into the ambient light and the aerial-perspective fog.
	vec3 sky_rgb = atmosphere(dir, sun_dir, atm_steps, cubemap ? 0.55 : MIE_G, view_t) * SKY_SCALE * sky_energy;
	sky_rgb += (twilight(dir, sun_dir) + night_sky(dir)) * sky_energy;
	// Moonlit sky: a faint blue wash around the moon.
	float moon_up = smoothstep(-0.02, 0.06, moon_direction.y);
	float mu_moon = dot(dir, moon_direction);
	sky_rgb += vec3(0.006, 0.009, 0.016) * moon_illumination * moon_up * (0.35 + phase_hg(mu_moon, 0.75) * 0.6);
	sky_rgb = grade_sky(sky_rgb, dir, sun_dir);
	cl.rgb = grade_to(cl.rgb, grade_deck, grade_deck_amount);
	vec3 color = sky_rgb;
	// Background only: in the radiance map the aurora would tint the
	// aerial-perspective fog over the whole fjord green.
	if (with_aurora && !cubemap) {
		color += aurora(dir) * view_t;
	}
	color = color * (1.0 - cl.a) + cl.rgb;
	color = haze_and_ground(color, dir, sun_dir, cubemap);
	return vec4(color, cl.a);
}

// The per-pixel detail over the sky: stars, the moon and the sun (LIGHT0)
// discs and the sun's glare, and the aurora unless the body holds it; seen
// through `clear` (1 - the cloud cover) and the horizon haze. Background
// only: the radiance map (ambient, reflections, aerial perspective) must not
// count the sun a second time on top of the DirectionalLight.
vec3 sky_detail(vec3 dir, vec3 sun_dir, vec3 body, float clear, vec3 view_t, bool with_aurora) {
	float above = smoothstep(-0.02, 0.01, dir.y);
	float horizon = pow(1.0 - clamp(dir.y, 0.0, 1.0), mix(14.0, 2.5, haze_amount));
	float seen = clear * above * (1.0 - clamp(horizon * haze_amount, 0.0, 1.0));
	float dark = 1.0 - smoothstep(0.006, 0.03, dot(body, vec3(0.2126, 0.7152, 0.0722)));
	vec3 detail = stars(dir) * dark * sqrt(view_t);
	detail += moon_disc(dir, sun_dir) * view_t;
	if (LIGHT0_ENABLED) {
		float cos_t = dot(dir, sun_dir);
		float size = max(LIGHT0_SIZE, 0.0093);
		float disc = smoothstep(cos(size * 0.5), cos(size * 0.42), cos_t);
		vec3 sun_rgb = LIGHT0_COLOR * LIGHT0_ENERGY;
		detail += sun_rgb * disc * 60.0;
		detail += sun_rgb * pow(max(cos_t, 0.0), 900.0) * 0.6;
	}
	if (with_aurora) {
		detail += aurora(dir) * view_t;
	}
	return detail * seen;
}

// A reduced-resolution pass's colour as the full pass reads it: the engine
// fogs every sky pass, so the first fogging comes back out (EnvironmentDirector
// writes sky_fog_k and sky_fog_scatter from the fog it sets; haze_color is
// the fog's light colour).
vec4 unfogged(vec4 cl, vec3 dir, vec3 sun_dir) {
	vec3 scatter = vec3(0.0);
	if (LIGHT0_ENABLED) {
		scatter = LIGHT0_COLOR * LIGHT0_ENERGY * pow(max(dot(dir, sun_dir), 0.0), 8.0) * PI_;
	}
	return vec4(max((cl.rgb - sky_fog_k * haze_color - sky_fog_scatter * scatter) / max(1.0 - sky_fog_k, 0.05), vec3(0.0)), cl.a);
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	vec3 sun_dir = LIGHT0_ENABLED ? normalize(LIGHT0_DIRECTION) : vec3(0.0, -1.0, 0.0);
	vec3 sky_top = sky_zenith;
	vec3 moon_light = moon_sky_light();
	bool cubemap = AT_CUBEMAP_PASS;
	if (AT_HALF_RES_PASS || AT_QUARTER_RES_PASS) {
		int wanted = AT_HALF_RES_PASS ? 1 : 2;
		vec4 c = vec4(0.0);
		if (sky_pass == wanted) {
			// The whole sky but its per-pixel detail, with the cloud cover
			// as alpha (sky_pass: the planar mirror's sky, G2-03).
			vec4 cl = dir.y > 0.0 ? clouds(dir, sun_dir, sky_top, moon_light, cloud_octaves, cloud_steps, 4) : vec4(0.0);
			c = sky_body(dir, sun_dir, false, sky_pass_aurora, cl);
		} else if (sky_pass == 0 && cloud_pass == wanted && dir.y > 0.0) {
			// The clouds only, premultiplied, when the quality tier puts
			// them here.
			c = clouds(dir, sun_dir, sky_top, moon_light, cloud_octaves, cloud_steps, 4);
		}
		COLOR = c.rgb;
		ALPHA = c.a;
	} else if (sky_pass > 0 && !cubemap) {
		// The body from the reduced pass; the sharp detail per pixel.
		vec4 body = unfogged(sky_pass == 1 ? HALF_RES_COLOR : QUARTER_RES_COLOR, dir, sun_dir);
		vec3 view_t = sun_transmittance(OBSERVER_H, max(dir.y, 0.002));
		vec3 color = body.rgb + sky_detail(dir, sun_dir, body.rgb, 1.0 - body.a, view_t, !sky_pass_aurora);
		COLOR = max(color, vec3(0.0));
	} else {
		// The clouds: marched here, or read from the reduced-resolution pass
		// (premultiplied, so the bilinear upsample keeps soft edges right).
		vec4 cl;
		if (cubemap) {
			cl = clouds(dir, sun_dir, sky_top, moon_light, 3, 3, 2);
		} else if (cloud_pass > 0) {
			cl = unfogged(cloud_pass == 1 ? HALF_RES_COLOR : QUARTER_RES_COLOR, dir, sun_dir);
		} else {
			cl = clouds(dir, sun_dir, sky_top, moon_light, cloud_octaves, cloud_steps, 4);
		}
		vec4 body = sky_body(dir, sun_dir, cubemap, !cubemap, cl);
		vec3 color = body.rgb;
		if (!cubemap) {
			vec3 view_t = sun_transmittance(OBSERVER_H, max(dir.y, 0.002));
			color += sky_detail(dir, sun_dir, body.rgb, 1.0 - body.a, view_t, false);
		} else {
			// Soft-limit the glare near the sun in the radiance map, which
			// feeds the ambient light and the aerial-perspective fog.
			float l = dot(color, vec3(0.2126, 0.7152, 0.0722));
			color *= 1.0 / (1.0 + max(l - 1.6, 0.0) * 0.6);
		}
		COLOR = max(color, vec3(0.0));
	}
}
