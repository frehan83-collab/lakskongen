shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// === Norwegian Fjord Terrain Shader ===
// Multi-layer texturing: rock, grass, moss, snow, beach.
// Layers blend by height and slope, sampled from tileable PBR textures with
// normal maps. Colours remain as tints/fallback so the public uniform API is
// unchanged; set `use_textures = 0.0` to get the original procedural look.

// Height thresholds
uniform float water_level : hint_range(-100.0, 100.0) = 0.0;
uniform float beach_height : hint_range(0.0, 20.0) = 5.0;
uniform float grass_height : hint_range(0.0, 200.0) = 80.0;
uniform float snow_height : hint_range(50.0, 500.0) = 200.0;
uniform float snow_blend : hint_range(1.0, 100.0) = 50.0;

// Slope threshold for cliff faces
uniform float cliff_slope : hint_range(0.0, 1.0) = 0.5;

// Colors (Norwegian palette) — used as tints over the sampled textures.
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

// --- Texture layers ---
uniform sampler2D grass_tex : hint_default_white, source_color, filter_linear, repeat_enable;
uniform sampler2D rock_tex : hint_default_white, source_color, filter_linear, repeat_enable;
uniform sampler2D snow_tex : hint_default_white, source_color, filter_linear, repeat_enable;
uniform sampler2D sand_tex : hint_default_white, source_color, filter_linear, repeat_enable;
uniform sampler2D moss_tex : hint_default_white, source_color, filter_linear, repeat_enable;

uniform sampler2D grass_normal_tex : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D rock_normal_tex : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D snow_normal_tex : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D sand_normal_tex : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D moss_normal_tex : hint_default_white, filter_linear, repeat_enable;

// World-space tile frequency (1 texture repeat per `1/texture_scale` metres).
uniform float texture_scale : hint_range(0.001, 0.5) = 0.04;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float use_textures : hint_range(0.0, 1.0) = 1.0;

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
	vec3 wn = normalize(world_normal);
	float height = world_pos.y;
	float slope = 1.0 - clamp(wn.y, 0.0, 1.0);

	// === Procedural palette colour (fallback + tint reference) ===
	float n = smooth_noise(world_pos.xz * detail_scale);
	float height_noise = n * 10.0;

	vec3 palette = rock_color;
	if (height < water_level) {
		float depth_factor = clamp((water_level - height) / 50.0, 0.0, 1.0);
		palette = mix(rock_color, underwater_color, depth_factor);
	} else if (height < beach_height + height_noise) {
		float t = smoothstep(water_level, beach_height, height + height_noise);
		palette = mix(underwater_color, beach_color, t);
	} else if (height < grass_height + height_noise && slope < cliff_slope) {
		float t = smoothstep(beach_height, beach_height + 10.0, height + height_noise);
		vec3 seasonal_grass = grass_color;
		float autumn_factor = smoothstep(0.5, 0.75, season) * smoothstep(1.0, 0.75, season);
		seasonal_grass = mix(seasonal_grass, autumn_grass, autumn_factor);
		float winter_factor = smoothstep(0.0, 0.15, season) + smoothstep(0.85, 1.0, season);
		winter_factor = 1.0 - winter_factor;
		seasonal_grass = mix(vec3(0.5, 0.48, 0.44), seasonal_grass, winter_factor);
		palette = mix(beach_color, seasonal_grass, t);
		float north_facing = clamp(-world_normal.z, 0.0, 1.0);
		palette = mix(palette, moss_color, north_facing * 0.4 * (1.0 - slope));
	} else if (slope >= cliff_slope) {
		float moss_factor = smoothstep(0.7, 0.5, slope) * (1.0 - smoothstep(snow_height, snow_height + snow_blend, height));
		palette = mix(rock_color, moss_color, moss_factor * 0.3);
		palette += (n - 0.5) * 0.08;
	}
	float snow_factor_p = smoothstep(snow_height - snow_blend + height_noise, snow_height + height_noise, height);
	snow_factor_p *= smoothstep(cliff_slope, cliff_slope * 0.5, slope);
	float winter_snow_p = smoothstep(0.15, 0.0, season) + smoothstep(0.85, 1.0, season);
	snow_factor_p += winter_snow_p * smoothstep(50.0, 100.0, height) * (1.0 - slope);
	snow_factor_p = clamp(snow_factor_p, 0.0, 1.0);
	palette = mix(palette, snow_color, snow_factor_p);

	// === Texture layers ===
	// Biplanar projection: top-down on flats, vertical on cliff faces.
	vec3 an = abs(wn);
	vec2 uv_top = world_pos.xz * texture_scale;
	vec2 uv_side = (an.x > an.z) ? vec2(world_pos.z, world_pos.y) : vec2(world_pos.x, world_pos.y);
	vec2 uv = mix(uv_top, uv_side * texture_scale, clamp(slope * 1.6, 0.0, 1.0));

	vec3 c_grass = texture(grass_tex, uv).rgb;
	vec3 c_rock = texture(rock_tex, uv).rgb;
	vec3 c_snow = texture(snow_tex, uv).rgb;
	vec3 c_sand = texture(sand_tex, uv).rgb;
	vec3 c_moss = texture(moss_tex, uv).rgb;

	vec3 nm_grass = texture(grass_normal_tex, uv).rgb * 2.0 - 1.0;
	vec3 nm_rock = texture(rock_normal_tex, uv).rgb * 2.0 - 1.0;
	vec3 nm_snow = texture(snow_normal_tex, uv).rgb * 2.0 - 1.0;
	vec3 nm_sand = texture(sand_normal_tex, uv).rgb * 2.0 - 1.0;
	vec3 nm_moss = texture(moss_normal_tex, uv).rgb * 2.0 - 1.0;

	float flatness = 1.0 - smoothstep(cliff_slope * 0.6, cliff_slope, slope);
	float upland = smoothstep(beach_height, grass_height, height);

	float snow_factor = snow_factor_p;
	float rock_factor = smoothstep(cliff_slope * 0.5, cliff_slope, slope);
	rock_factor = clamp(rock_factor + smoothstep(grass_height, grass_height + 60.0, height) * (1.0 - slope), 0.0, 1.0);
	rock_factor *= (1.0 - snow_factor);

	float grass_mask = flatness * upland * (1.0 - snow_factor) * (1.0 - rock_factor);
	float moss_mask = clamp(-wn.z, 0.0, 1.0) * grass_mask * 0.55;
	float sand_mask = smoothstep(water_level, beach_height, height)
		* (1.0 - smoothstep(beach_height, beach_height + 6.0, height)) * flatness;
	sand_mask *= (1.0 - snow_factor);

	vec3 tex_color = c_rock;
	tex_color = mix(tex_color, c_grass, grass_mask);
	tex_color = mix(tex_color, c_moss, moss_mask);
	tex_color = mix(tex_color, c_sand, sand_mask);
	tex_color = mix(tex_color, c_snow, snow_factor);

	// Seasonal tint over the sampled grass/land layers.
	float autumn_factor = smoothstep(0.5, 0.75, season) * smoothstep(1.0, 0.75, season);
	tex_color = mix(tex_color, tex_color * vec3(1.35, 1.15, 0.72), autumn_factor * grass_mask);
	float winter_factor = 1.0 - (smoothstep(0.0, 0.15, season) + smoothstep(0.85, 1.0, season));
	tex_color = mix(vec3(dot(tex_color, vec3(0.299, 0.587, 0.114))), tex_color, mix(1.0, 0.75, winter_factor));

	// Shallow-water darkening.
	float uw = smoothstep(water_level, water_level - 30.0, height);
	tex_color = mix(tex_color, tex_color * underwater_color * 1.4, uw * 0.6);

	// Blended tangent-space normal map.
	vec3 mixed_normal = nm_rock * (1.0 - grass_mask - moss_mask - sand_mask - snow_factor)
		+ nm_grass * grass_mask + nm_moss * moss_mask
		+ nm_sand * sand_mask + nm_snow * snow_factor;
	mixed_normal = normalize(mixed_normal + vec3(0.0, 0.0, 0.001));
	mixed_normal.xy *= normal_strength;

	// Choose between procedural palette and textured result (API compatible).
	vec3 color = mix(palette, tex_color, use_textures);
	color += (n - 0.5) * 0.03 * (1.0 - use_textures);

	ALBEDO = clamp(color, vec3(0.0), vec3(1.0));
	NORMAL_MAP = clamp(mixed_normal * 0.5 + 0.5, vec3(0.0), vec3(1.0));
	ROUGHNESS = mix(roughness_base, 0.95, snow_factor);
	METALLIC = 0.0;
}