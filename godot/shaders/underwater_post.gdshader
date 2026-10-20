shader_type canvas_item;
render_mode unshaded;

// === Underwater Post-Process ===
// Blue tint + depth-driven blur + caustic overlay. Driven from
// FjordEnvironment.gd: `active` fades 0 -> 1 when the camera drops below the
// water surface. With active == 0 the overlay is fully transparent.

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D caustics_texture : hint_default_white, filter_linear, repeat_enable;

// 0 = off (transparent), 1 = full underwater look
uniform float active : hint_range(0.0, 1.0) = 0.0;

// Blue depth tint
uniform vec3 water_tint : source_color = vec3(0.055, 0.227, 0.290); // #0F3A4A
uniform float tint_strength : hint_range(0.0, 1.0) = 0.55;

// Blur
uniform float blur_amount : hint_range(0.0, 12.0) = 2.5;

// Caustic overlay
uniform float caustics_strength : hint_range(0.0, 1.0) = 0.16;
uniform float caustics_scale : hint_range(0.01, 1.0) = 0.12;
uniform float caustics_speed : hint_range(0.0, 1.0) = 0.05;

// Edge darkening to sell the water column
uniform float edge_vignette : hint_range(0.0, 1.0) = 0.35;

vec3 blur_screen(sampler2D tex, vec2 uv, float amount) {
	vec3 color = vec3(0.0);
	float total = 0.0;
	vec2 pixel = vec2(1.0) / vec2(textureSize(tex, 0));
	for (int i = -4; i <= 4; i++) {
		float fi = float(i);
		float w = exp(-0.5 * (fi * fi) / (amount * amount + 0.0001));
		color += texture(tex, uv + vec2(pixel.x * amount, 0.0) * fi).rgb * w;
		color += texture(tex, uv + vec2(0.0, pixel.y * amount) * fi).rgb * w;
		total += 2.0 * w;
	}
	return color / total;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float a = clamp(active, 0.0, 1.0);
	if (a <= 0.001) {
		COLOR = vec4(0.0, 0.0, 0.0, 0.0);
	} else {
		vec3 sharp = texture(screen_texture, uv).rgb;
		vec3 blurred = blur_screen(screen_texture, uv, blur_amount);
		vec3 color = mix(sharp, blurred, 0.65 * a);

		// Blue depth tint that strengthens with depth.
		color = mix(color, water_tint, tint_strength * a);

		// Slow-drifting caustics on top.
		vec2 cuv = uv / max(caustics_scale, 0.001)
			+ vec2(TIME * caustics_speed, TIME * caustics_speed * 0.7);
		float caus = texture(caustics_texture, cuv).r;
		color += caus * caustics_strength * a;

		// Edge vignette.
		float vig = smoothstep(0.9, 0.35, length(uv - vec2(0.5)));
		color *= mix(1.0, vig, edge_vignette * a);

		COLOR = vec4(clamp(color, 0.0, 1.0), a);
	}
}