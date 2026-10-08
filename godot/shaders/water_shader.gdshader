shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// === Norwegian Fjord Water Shader ===
// Gerstner waves, depth-based color, foam, reflections, caustics, normal map support

// Wave parameters
uniform float wave_speed : hint_range(0.0, 5.0) = 1.0;
uniform float wave_amplitude : hint_range(0.0, 2.0) = 0.3;
uniform float wave_frequency : hint_range(0.0, 10.0) = 0.15;
uniform int wave_count : hint_range(1, 8) = 3;
uniform float wave_steepness : hint_range(0.0, 1.0) = 0.4;

// Color
uniform vec3 shallow_color : source_color = vec3(0.10, 0.32, 0.36);
uniform vec3 medium_color : source_color = vec3(0.04, 0.18, 0.24);
uniform vec3 deep_color : source_color = vec3(0.01, 0.07, 0.11);
uniform float depth_fade_distance : hint_range(0.1, 100.0) = 25.0;
uniform float transparency : hint_range(0.0, 1.0) = 0.85;

// Foam
uniform float foam_amount : hint_range(0.0, 5.0) = 1.5;
uniform float foam_cutoff : hint_range(0.0, 2.0) = 0.8;
uniform vec3 foam_color : source_color = vec3(0.9, 0.95, 1.0);

// Reflections & specular
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.08;
uniform float fresnel_power : hint_range(0.0, 10.0) = 5.0;

// Caustics (underwater light)
uniform float caustic_scale : hint_range(0.1, 20.0) = 8.0;
uniform float caustic_speed : hint_range(0.0, 2.0) = 0.3;
uniform float caustic_intensity : hint_range(0.0, 1.0) = 0.3;

// Normal maps (Poly Haven Ocean Water or similar CC0)
uniform sampler2D normal_map_1 : hint_normal;
uniform sampler2D normal_map_2 : hint_normal;
uniform float normal_map_scale : hint_range(0.01, 1.0) = 0.05;
uniform vec2 normal_map_scroll_dir1 = vec2(1.0, 0.2);
uniform vec2 normal_map_scroll_dir2 = vec2(-0.3, 1.0);
uniform float normal_map_scroll_speed : hint_range(0.0, 1.0) = 0.02;
uniform float normal_map_strength : hint_range(0.0, 2.0) = 1.0;

// Depth texture for shore foam
uniform sampler2D depth_texture : hint_depth_texture;
uniform sampler2D screen_texture : hint_screen_texture;

// Foam / caustics detail textures (generated tiles)
uniform sampler2D foam_texture : hint_default_white, filter_linear, repeat_enable;
uniform sampler2D caustics_texture : hint_default_white, filter_linear, repeat_enable;
uniform float foam_scale : hint_range(0.001, 1.0) = 0.03;
uniform float foam_scroll_speed : hint_range(0.0, 1.0) = 0.02;
uniform float foam_texture_strength : hint_range(0.0, 2.0) = 0.25;
uniform float caustics_tile_scale : hint_range(0.001, 1.0) = 0.05;
uniform float caustics_tile_speed : hint_range(0.0, 1.0) = 0.05;
uniform float caustics_tile_strength : hint_range(0.0, 2.0) = 0.6;

// Wind direction (from WeatherController)
uniform vec2 wind_direction = vec2(1.0, 0.3);
uniform float wind_strength : hint_range(0.0, 3.0) = 1.0;

// Visual Adapter parameters (driven by simulation)
uniform float water_clarity : hint_range(0.0, 1.0) = 1.0;       // 0.0 = murky, 1.0 = clear
uniform float temperature_factor : hint_range(0.0, 1.0) = 0.2; // Temperature influence on color
uniform float algae_density : hint_range(0.0, 1.0) = 0.0;      // Plankton bloom green tint
uniform float turbidity : hint_range(0.0, 1.0) = 0.0;          // Sediment/particulate matter

varying float vertex_height;
varying vec3 world_pos;

// Gerstner wave function
vec3 gerstner_wave(vec2 pos, float time, vec2 direction, float freq, float amp, float steep) {
	float phase = freq * dot(direction, pos) + time * wave_speed;
	float s = sin(phase);
	float c = cos(phase);

	vec3 displacement;
	displacement.x = steep * amp * direction.x * c;
	displacement.z = steep * amp * direction.y * c;
	displacement.y = amp * s;
	return displacement;
}

// Caustic pattern
float caustic_pattern(vec2 uv, float time) {
	vec2 p = uv * caustic_scale;
	float t = time * caustic_speed;
	float v1 = sin(p.x * 1.3 + t) * sin(p.y * 1.7 - t * 0.7);
	float v2 = sin(p.x * 0.7 - t * 1.1 + 2.0) * sin(p.y * 2.3 + t * 0.5);
	float v3 = sin((p.x + p.y) * 1.1 + t * 0.8);
	return (v1 + v2 + v3) * 0.33 * 0.5 + 0.5;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 pos = world_pos.xz;

	vec3 total_displacement = vec3(0.0);
	vec2 wind_dir = normalize(wind_direction);

	// Sum multiple Gerstner waves with different directions
	for (int i = 0; i < wave_count; i++) {
		float fi = float(i);
		float angle = fi * 0.7 + 0.3;
		vec2 dir = normalize(wind_dir + vec2(cos(angle), sin(angle)) * 0.5);
		float freq = wave_frequency * (1.0 + fi * 0.3);
		float amp = wave_amplitude * wind_strength / (1.0 + fi * 0.5);
		float steep = wave_steepness / (freq * amp * float(wave_count));

		total_displacement += gerstner_wave(pos, TIME, dir, freq, amp, steep);
	}

	VERTEX += total_displacement;
	vertex_height = total_displacement.y;
}

void fragment() {
	// Depth-based effects
	float depth_raw = texture(depth_texture, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
	vec4 view_pos = INV_PROJECTION_MATRIX * ndc;
	view_pos.xyz /= view_pos.w;
	// Both in view space (metres, negative forward); positive when the
	// riverbed/terrain lies behind the surface along the view ray.
	float depth_diff = VERTEX.z - view_pos.z;
	float depth_factor = clamp(depth_diff / depth_fade_distance, 0.0, 1.0);

	// Water color based on depth
	vec3 water_color = mix(shallow_color, medium_color, smoothstep(0.0, 0.5, depth_factor));
	water_color = mix(water_color, deep_color, smoothstep(0.5, 1.0, depth_factor));

	// Temperature influence on color (colder = bluer, warmer = greener)
	water_color = mix(water_color, vec3(0.1, 0.4, 0.3), temperature_factor * 0.3);

	// Algae bloom green tint
	water_color = mix(water_color, vec3(0.2, 0.6, 0.2), algae_density * 0.6);

	// Turbidity (sediment) - brownish tint
	water_color = mix(water_color, vec3(0.4, 0.3, 0.2), turbidity * 0.4);

	// Clarity affects overall transparency and color saturation
	water_color *= water_clarity;
	water_color += (1.0 - water_clarity) * vec3(0.1, 0.15, 0.2); // Murky base

	// Sample normal maps for surface detail (compatible with Compatibility renderer)
	vec2 uv1 = world_pos.xz * normal_map_scale + normal_map_scroll_dir1 * TIME * normal_map_scroll_speed;
	vec2 uv2 = world_pos.xz * normal_map_scale * 1.5 + normal_map_scroll_dir2 * TIME * normal_map_scroll_speed * 0.7;
	
	// The ocean normal maps are VRAM-compressed (RGTC): only R/G survive,
	// so rebuild Z instead of trusting the sampled blue channel.
	vec2 n1 = texture(normal_map_1, uv1).rg * 2.0 - 1.0;
	vec2 n2 = texture(normal_map_2, uv2).rg * 2.0 - 1.0;
	vec2 nxy = (n1 + n2) * 0.5 * normal_map_strength;

	// Combine normal maps with strength control
	vec3 combined_normal = normalize(vec3(nxy, 1.0));

	// Shore foam
	float shore_foam = 1.0 - smoothstep(0.0, foam_amount, depth_diff);
	
	// Wave crest foam based on vertex displacement
	float wave_foam = smoothstep(foam_cutoff, foam_cutoff + 0.2, vertex_height);
	
	// Additional foam from normal map detail (simulating micro-wave breaking).
	// .xy only: .z sits near 1.0 and would foam the whole surface.
	float detail_foam = smoothstep(0.6, 0.9, length(combined_normal.xy)) * 0.3;

	// Texture-driven foam/whitecap wisps, advected by the wind.
	vec2 foam_uv = world_pos.xz * foam_scale
		+ normalize(wind_direction) * TIME * foam_scroll_speed;
	float foam_wisp = texture(foam_texture, foam_uv).a;
	float texture_foam = foam_wisp * (shore_foam + wave_foam) * foam_texture_strength;

	float total_foam = clamp(shore_foam + wave_foam + detail_foam, 0.0, 1.0);
	total_foam = clamp(total_foam * (0.45 + 1.1 * foam_wisp) + texture_foam * 0.35, 0.0, 1.0);

	// Fresnel effect
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), fresnel_power);

	// Caustics (visible mostly in shallow water)
	float caustics = caustic_pattern(world_pos.xz, TIME) * caustic_intensity;
	float caustics_tile = texture(caustics_texture,
		world_pos.xz * caustics_tile_scale
		+ vec2(TIME * caustics_tile_speed, TIME * caustics_tile_speed * 0.6)).r;
	caustics += caustics_tile * caustics_tile_strength * caustic_intensity;
	caustics *= (1.0 - depth_factor); // Only visible in shallow areas
	caustics *= water_clarity; // Less caustics in murky water

	// Final color
	vec3 final_color = mix(water_color, foam_color, total_foam);
	final_color += caustics * vec3(0.3, 0.5, 0.6);
	final_color = mix(final_color, vec3(0.75, 0.85, 0.9), fresnel * 0.1);

	ALBEDO = final_color;
	METALLIC = metallic_value;
	ROUGHNESS = mix(roughness_value, 0.8, total_foam); // Foam is rough
	NORMAL_MAP = combined_normal * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = 0.8;
	ALPHA = mix(transparency, 1.0, total_foam + fresnel * 0.5) * water_clarity;
}