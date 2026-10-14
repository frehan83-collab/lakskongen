shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// === Norwegian Fjord Terrain Shader ("Vestland Light", roadmap D13) ===
// Colour comes from a matte landscape palette chosen by slope, height and
// soil: bare granite on the walls and peaks, spruce and birch on the gentle
// slopes below the treeline, heath above it, wet rock in the splash zone,
// gravel on the delta, snow on the high flats. The tileable textures only
// add luminance and normal detail. The vertex colour's red channel is a
// cavity term from the heightfield (FjordTerrain / TerrainMesh.cs): gullies
// hold soil and shade, ridges stay bare.

// Height thresholds, in metres over the fjord terrain (walls rise 500-650 m
// to the shoulders, peaks reach about 1000 m).
uniform float water_level : hint_range(-100.0, 100.0) = 0.0;
// Splash zone and gravel band above the waterline.
uniform float beach_height : hint_range(0.0, 20.0) = 3.0;
// Treeline: forest gives way to heath and rock above it.
uniform float grass_height : hint_range(0.0, 1200.0) = 480.0;
uniform float snow_height : hint_range(50.0, 2000.0) = 960.0;
uniform float snow_blend : hint_range(1.0, 300.0) = 90.0;

// Slope threshold for bare rock (1 - normal.y; 0.28 is about 44 degrees).
uniform float cliff_slope : hint_range(0.0, 1.0) = 0.28;

// Landscape palette (D13).
uniform vec3 rock_color : source_color = vec3(0.420, 0.451, 0.471);      // granite #6B7378
uniform vec3 wet_rock_color : source_color = vec3(0.243, 0.267, 0.282);  // wet rock #3E4448
uniform vec3 moss_color : source_color = vec3(0.184, 0.290, 0.227);      // spruce #2F4A3A
uniform vec3 grass_color : source_color = vec3(0.333, 0.420, 0.243);     // birch and meadow #556B3E
uniform vec3 heath_color : source_color = vec3(0.478, 0.502, 0.333);     // heath, muted #7A8055
uniform vec3 snow_color : source_color = vec3(0.957, 0.969, 0.980);      // snow #F4F7FA
uniform vec3 beach_color : source_color = vec3(0.549, 0.525, 0.475);     // gravel #8C8679
uniform vec3 underwater_color : source_color = vec3(0.165, 0.420, 0.486); // tint for submerged rock

// Seasonal shift (0=winter, 0.5=summer, 1=winter)
uniform float season : hint_range(0.0, 1.0) = 0.5;
uniform vec3 autumn_grass : source_color = vec3(0.722, 0.522, 0.255);    // autumn birch #B8853F

// Detail
uniform float detail_scale : hint_range(0.001, 1.0) = 0.004;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.88;

// --- Texture layers (detail only) ---
uniform sampler2D rock_tex : hint_default_white, source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_tex : hint_default_white, source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

// World-space tile frequency (1 texture repeat per `1/texture_scale` metres).
uniform float texture_scale : hint_range(0.001, 0.5) = 0.04;
uniform float normal_strength : hint_range(0.0, 2.0) = 0.45;
uniform float use_textures : hint_range(0.0, 1.0) = 1.0;

varying vec3 world_pos;
varying vec3 world_normal;
varying float cavity;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	cavity = COLOR.r;
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm3(vec2 p) {
	return value_noise(p) * 0.55 + value_noise(p * 2.13 + 7.1) * 0.3 + value_noise(p * 4.37 - 3.3) * 0.15;
}

float luminance(vec3 c) {
	return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void fragment() {
	vec3 wn = normalize(world_normal);
	float height = world_pos.y;
	float slope = 1.0 - clamp(wn.y, 0.0, 1.0);
	float soil = cavity - 0.5; // + in gullies, - on ridges

	// Macro variation: 250 m patches and 50 m mottling.
	float macro = fbm3(world_pos.xz * detail_scale);
	float mottle = fbm3(world_pos.xz * detail_scale * 5.0 + 11.0);

	// --- Masks ---
	// Bare rock where it is steep; soil in gullies lets plants hold on, and
	// birch climbs steeper ground low on the walls than near the shoulders.
	float rock_slope = cliff_slope + (mottle - 0.5) * 0.12 + soil * 0.30
		+ 0.16 * (1.0 - smoothstep(80.0, 420.0, height));
	float bare = smoothstep(rock_slope - 0.05, rock_slope + 0.05, slope);
	float treeline = grass_height + (macro - 0.5) * 140.0;
	float above_trees = smoothstep(treeline - 50.0, treeline + 50.0, height);
	float above_splash = smoothstep(beach_height, beach_height + 6.0, height);
	float forest = (1.0 - bare) * (1.0 - above_trees) * above_splash;
	float heath = (1.0 - bare) * above_trees;
	// Low, nearly flat ground (the head delta, skerry tops): a gravel strand
	// at the water, meadow behind it.
	float flat_low = (1.0 - smoothstep(0.03, 0.10, slope)) * (1.0 - smoothstep(beach_height + 4.0, beach_height + 14.0, height));
	float gravel = flat_low * smoothstep(water_level - 0.5, water_level + 0.2, height)
		* (1.0 - smoothstep(water_level + 0.5, water_level + 1.0, height));
	// Soil (and grass) gathers in hollows; rounded skerry domes stay bare rock.
	float meadow = flat_low * smoothstep(water_level + 0.6, water_level + 1.2, height)
		* smoothstep(-0.05, -0.01, soil) * mix(0.75, 1.0, mottle);
	// Splash zone: a few metres up a cliff, a narrow strip on a flat shore.
	float splash = mix(0.4, beach_height, smoothstep(0.05, 0.3, slope));
	float wet = (1.0 - smoothstep(water_level + 0.2, water_level + splash, height)) * step(water_level - 2.0, height);

	// Snow: high flats in summer, everything gentle above the shore in winter.
	float winter = smoothstep(0.15, 0.0, season) + smoothstep(0.85, 1.0, season);
	float snow_line = snow_height + (macro - 0.5) * 160.0 - soil * 120.0;
	float snow = smoothstep(snow_line - snow_blend, snow_line, height) * (1.0 - smoothstep(0.35, 0.55, slope));
	// Summer snow survives in patches: hollows and north faces keep it.
	snow *= mix(1.0, smoothstep(0.35, 0.65, mottle + soil * 0.8 - wn.z * 0.25), 1.0 - winter);
	snow = max(snow, winter * smoothstep(40.0, 120.0, height) * (1.0 - smoothstep(0.30, 0.50, slope)));
	snow = clamp(snow, 0.0, 1.0);

	// --- Colours ---
	vec3 granite = rock_color * mix(0.82, 1.12, macro) * mix(vec3(1.0), vec3(1.06, 1.0, 0.94), mottle);
	// Weathered upper rock is lighter; lichen warms the shoulders.
	granite = mix(granite, granite * vec3(1.10, 1.08, 1.02), smoothstep(300.0, 800.0, height) * 0.6);
	float autumn = smoothstep(0.55, 0.72, season) * smoothstep(0.92, 0.78, season);
	vec3 birch = mix(grass_color, autumn_grass, autumn);
	vec3 woods = mix(moss_color, birch, smoothstep(0.45, 0.75, mottle) * 0.7);
	vec3 moor = mix(heath_color, granite, 0.25 + 0.5 * smoothstep(treeline + 120.0, treeline + 320.0, height));
	moor = mix(moor, moor * vec3(0.92, 0.88, 0.8), smoothstep(0.4, 0.8, mottle));

	vec3 color = granite;
	color = mix(color, woods, forest);
	color = mix(color, moor, heath);
	color = mix(color, beach_color, gravel);
	color = mix(color, birch * 1.15, meadow);
	color = mix(color, wet_rock_color, wet);
	color = mix(color, snow_color, snow);

	// Gullies shade, ridges catch light.
	color *= mix(1.08, 0.72, smoothstep(0.35, 0.9, cavity));

	// Submerged rock fades into the water colour.
	float uw = smoothstep(water_level, water_level - 30.0, height);
	color = mix(color, color * underwater_color * 1.4, uw * 0.6);

	// --- Texture detail (biplanar, luminance + normals only) ---
	vec3 an = abs(wn);
	vec2 uv_top = world_pos.xz * texture_scale;
	vec2 uv_side = ((an.x > an.z) ? world_pos.zy : world_pos.xy) * texture_scale;
	float side_w = smoothstep(0.25, 0.55, slope);
	vec3 rock_detail = mix(texture(rock_tex, uv_top).rgb, texture(rock_tex, uv_side).rgb, side_w);
	vec3 veg_detail = texture(moss_tex, uv_top).rgb;
	float rock_l = luminance(rock_detail) / max(luminance(vec3(0.62, 0.64, 0.68)), 0.001);
	float veg_l = luminance(veg_detail) / max(luminance(vec3(0.33, 0.50, 0.38)), 0.001);
	float detail = mix(rock_l, veg_l, clamp(forest + heath, 0.0, 1.0));
	detail = mix(1.0, clamp(detail, 0.6, 1.4), 0.55 * use_textures * (1.0 - snow * 0.7));
	color *= detail;

	vec3 nm_rock = mix(texture(rock_normal_tex, uv_top).rgb, texture(rock_normal_tex, uv_side).rgb, side_w) * 2.0 - 1.0;
	vec3 nm_veg = texture(moss_normal_tex, uv_top).rgb * 2.0 - 1.0;
	vec3 nm_snow = texture(snow_normal_tex, uv_top).rgb * 2.0 - 1.0;
	vec3 mixed_normal = mix(nm_rock, nm_veg, clamp(forest + heath, 0.0, 1.0));
	mixed_normal = mix(mixed_normal, nm_snow, snow);
	mixed_normal.xy *= normal_strength * use_textures;
	mixed_normal = normalize(mixed_normal + vec3(0.0, 0.0, 0.001));

	ALBEDO = clamp(color, vec3(0.0), vec3(1.0));
	NORMAL_MAP = clamp(mixed_normal * 0.5 + 0.5, vec3(0.0), vec3(1.0));
	float rough = mix(roughness_base, 0.95, clamp(forest + heath, 0.0, 1.0));
	rough = mix(rough, 0.45, wet);
	rough = mix(rough, 0.7, snow);
	ROUGHNESS = rough;
	METALLIC = 0.0;
}
