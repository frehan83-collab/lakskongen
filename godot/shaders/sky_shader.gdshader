shader_type sky;

// === Norwegian Sky Shader ===
// Dynamic sky with day/night cycle, stars, aurora borealis

// Time of day (0.0 = midnight, 0.5 = noon, 1.0 = midnight)
uniform float time_of_day : hint_range(0.0, 1.0) = 0.5;

// Season affects sky colors and sun angle
uniform float season : hint_range(0.0, 1.0) = 0.5; // 0=winter, 0.25=spring, 0.5=summer, 0.75=autumn

// Colors
uniform vec3 day_color : source_color = vec3(0.529, 0.808, 0.922);     // #87CEEB
uniform vec3 horizon_day : source_color = vec3(0.75, 0.85, 0.95);
uniform vec3 sunset_color : source_color = vec3(1.0, 0.42, 0.29);      // #FF6B4A
uniform vec3 sunset_horizon : source_color = vec3(1.0, 0.702, 0.278);  // #FFB347
uniform vec3 night_color : source_color = vec3(0.039, 0.098, 0.161);   // #0A1929
uniform vec3 night_horizon : source_color = vec3(0.05, 0.1, 0.15);

// Aurora
uniform float aurora_intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec3 aurora_green : source_color = vec3(0.31, 1.0, 0.69);      // #4FFFB0
uniform vec3 aurora_purple : source_color = vec3(0.651, 0.31, 1.0);    // #A64FFF

// Sun
uniform vec3 sun_color : source_color = vec3(1.0, 0.957, 0.902);       // #FFF4E6
uniform float sun_size : hint_range(0.0, 0.1) = 0.02;

// Stars
uniform float star_density : hint_range(0.0, 1.0) = 0.5;

// Cloud coverage
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.3;

float hash_sky(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float stars(vec3 dir) {
	vec2 cell = floor(dir.xz / dir.y * 200.0);
	float h = hash_sky(cell);
	float brightness = step(1.0 - star_density * 0.01, h);
	float twinkle = sin(h * 100.0 + TIME * (h * 2.0 + 1.0)) * 0.5 + 0.5;
	return brightness * twinkle;
}

float aurora(vec3 dir, float time) {
	if (dir.y < 0.1) return 0.0;
	float y = dir.y;
	vec2 uv = dir.xz / (y + 0.5) * 3.0;

	float wave1 = sin(uv.x * 2.0 + time * 0.3) * sin(uv.y * 1.5 + time * 0.2);
	float wave2 = sin(uv.x * 3.5 - time * 0.4) * cos(uv.y * 2.0 + time * 0.15);
	float pattern = (wave1 + wave2) * 0.5;

	// Aurora is band-like in the sky
	float band = smoothstep(0.2, 0.5, y) * smoothstep(0.8, 0.5, y);
	return max(pattern, 0.0) * band;
}

void sky() {
	vec3 dir = EYEDIR;

	// Determine sky phase
	// 0.0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset
	float t = time_of_day;

	// Day factor (1.0 at noon, 0.0 at midnight)
	float day_factor = smoothstep(0.2, 0.35, t) * smoothstep(0.8, 0.65, t);

	// Sunset/sunrise factor
	float sunrise_factor = smoothstep(0.15, 0.25, t) * smoothstep(0.35, 0.25, t);
	float sunset_factor = smoothstep(0.65, 0.75, t) * smoothstep(0.85, 0.75, t);
	float golden_hour = sunrise_factor + sunset_factor;

	// Night factor
	float night_factor = 1.0 - smoothstep(0.15, 0.3, t) + smoothstep(0.7, 0.85, t);
	night_factor = clamp(night_factor - 1.0 + step(t, 0.15) + step(0.85, t), 0.0, 1.0);
	night_factor = 1.0 - day_factor - golden_hour * 0.5;
	night_factor = clamp(night_factor, 0.0, 1.0);

	// Horizon gradient
	float horizon = 1.0 - abs(dir.y);
	horizon = pow(horizon, 3.0);

	// Base sky color
	vec3 sky_day = mix(day_color, horizon_day, horizon);
	vec3 sky_sunset = mix(sunset_color, sunset_horizon, horizon);
	vec3 sky_night = mix(night_color, night_horizon, horizon);

	vec3 sky_color = sky_day * day_factor;
	sky_color += sky_sunset * golden_hour;
	sky_color += sky_night * night_factor;

	// Stars (only at night)
	if (dir.y > 0.0) {
		float star_brightness = stars(dir) * night_factor;
		sky_color += vec3(star_brightness);
	}

	// Aurora borealis (night only, when enabled)
	if (aurora_intensity > 0.0 && night_factor > 0.1) {
		float a = aurora(dir, TIME);
		vec3 aurora_color = mix(aurora_green, aurora_purple, sin(dir.x * 5.0 + TIME * 0.2) * 0.5 + 0.5);
		sky_color += aurora_color * a * aurora_intensity * night_factor;
	}

	// Sun disc
	float sun_angle = (time_of_day - 0.25) * TAU * 0.5; // Sunrise at east
	vec3 sun_dir = normalize(vec3(cos(sun_angle), sin(sun_angle) * 0.8 + 0.1, 0.3));
	float sun_dot = dot(dir, sun_dir);
	float sun_disc = smoothstep(1.0 - sun_size, 1.0, sun_dot);
	float sun_glow = smoothstep(0.9, 1.0, sun_dot) * 0.3;
	sky_color += sun_color * (sun_disc + sun_glow) * day_factor;

	// Cloud layer (very basic)
	if (cloud_coverage > 0.0 && dir.y > 0.0) {
		vec2 cloud_uv = dir.xz / (dir.y + 0.3) * 2.0;
		float cloud = sin(cloud_uv.x * 3.0 + TIME * 0.02) * sin(cloud_uv.y * 2.0 - TIME * 0.01);
		cloud = smoothstep(1.0 - cloud_coverage, 1.0, cloud * 0.5 + 0.5);
		vec3 cloud_col = mix(vec3(0.7, 0.75, 0.8), vec3(1.0), day_factor);
		sky_color = mix(sky_color, cloud_col, cloud * 0.6);
	}

	COLOR = sky_color;
}
