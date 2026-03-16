shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// === Norwegian Fjord Terrain Shader ===
// Multi-layer texturing: rock, grass, moss, snow, beach
// Layers blend based on height and slope

// Height thresholds
uniform float water_level : hint_range(-100.0, 100.0) = 0.0;
uniform float beach_height : hint_range(0.0, 20.0) = 5.0;
uniform float grass_height : hint_range(0.0, 200.0) = 80.0;
uniform float snow_height : hint_range(50.0, 500.0) = 200.0;
uniform float snow_blend : hint_range(1.0, 100.0) = 50.0;

// Slope threshold for cliff faces
uniform float cliff_slope : hint_range(0.0, 1.0) = 0.5;

// Colors (Norwegian palette)
uniform vec3 rock_color : source_color = vec3(0.420, 0.447, 0.502);    // #6B7280
uniform vec3 moss_color : source_color = vec3(0.290, 0.486, 0.349);    // #4A7C59
uniform vec3 grass_color : source_color = vec3(0.478, 0.620, 0.435);   // #7A9E6F
uniform vec3 snow_color : source_color = vec3(0.910, 0.945, 0.961);    // #E8F1F5
uniform vec3 beach_color : source_color = vec3(0.694, 0.667, 0.584);   // Gravel/sand
uniform vec3 underwater_color : source_color = vec3(0.165, 0.420, 0.486); // Underwater rock

// Seasonal shift (0=winter, 0.5=summer, 1=winter)
uniform float season : hint_range(0.0, 1.0) = 0.5;
uniform vec3 autumn_grass : source_color = vec3(0.722, 0.639, 0.435);  // #B8A36F

// Detail
uniform float detail_scale : hint_range(0.01, 1.0) = 0.1;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.85;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

// Simple noise for variation
float noise(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float smooth_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);

	float a = noise(i);
	float b = noise(i + vec2(1.0, 0.0));
	float c = noise(i + vec2(0.0, 1.0));
	float d = noise(i + vec2(1.0, 1.0));

	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
	float height = world_pos.y;
	float slope = 1.0 - abs(dot(normalize(world_normal), vec3(0.0, 1.0, 0.0)));

	// Noise for breaking up transitions
	float n = smooth_noise(world_pos.xz * detail_scale);
	float height_noise = n * 10.0;

	// Determine terrain layer
	vec3 color = rock_color;

	// Underwater
	if (height < water_level) {
		float depth_factor = clamp((water_level - height) / 50.0, 0.0, 1.0);
		color = mix(rock_color, underwater_color, depth_factor);
	}
	// Beach zone
	else if (height < beach_height + height_noise) {
		float t = smoothstep(water_level, beach_height, height + height_noise);
		color = mix(underwater_color, beach_color, t);
	}
	// Grass zone (flat areas only)
	else if (height < grass_height + height_noise && slope < cliff_slope) {
		float t = smoothstep(beach_height, beach_height + 10.0, height + height_noise);

		// Seasonal grass color
		vec3 seasonal_grass = grass_color;
		// Summer = green, autumn = gold, winter = brown/white
		float autumn_factor = smoothstep(0.5, 0.75, season) * smoothstep(1.0, 0.75, season);
		seasonal_grass = mix(seasonal_grass, autumn_grass, autumn_factor);
		// Winter = desaturated
		float winter_factor = smoothstep(0.0, 0.15, season) + smoothstep(0.85, 1.0, season);
		winter_factor = 1.0 - winter_factor; // Invert: 1 in summer, 0 in winter
		seasonal_grass = mix(vec3(0.5, 0.48, 0.44), seasonal_grass, winter_factor);

		color = mix(beach_color, seasonal_grass, t);

		// Moss on north-facing slopes
		float north_facing = clamp(-world_normal.z, 0.0, 1.0);
		color = mix(color, moss_color, north_facing * 0.4 * (1.0 - slope));
	}
	// Cliff faces (steep slopes = exposed rock)
	else if (slope >= cliff_slope) {
		float moss_factor = smoothstep(0.7, 0.5, slope) * (1.0 - smoothstep(snow_height, snow_height + snow_blend, height));
		color = mix(rock_color, moss_color, moss_factor * 0.3);
		// Rock variation
		color += (n - 0.5) * 0.08;
	}

	// Snow (high elevation + flat, or seasonal everywhere)
	float snow_factor = smoothstep(snow_height - snow_blend + height_noise, snow_height + height_noise, height);
	// Snow prefers flat surfaces
	snow_factor *= smoothstep(cliff_slope, cliff_slope * 0.5, slope);
	// More snow in winter
	float winter_snow = smoothstep(0.15, 0.0, season) + smoothstep(0.85, 1.0, season);
	snow_factor += winter_snow * smoothstep(50.0, 100.0, height) * (1.0 - slope);
	snow_factor = clamp(snow_factor, 0.0, 1.0);
	color = mix(color, snow_color, snow_factor);

	// Detail variation
	color += (n - 0.5) * 0.04;

	ALBEDO = clamp(color, vec3(0.0), vec3(1.0));
	ROUGHNESS = mix(roughness_base, 0.95, snow_factor);  // Snow is very rough
	METALLIC = 0.0;
}
