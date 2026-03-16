shader_type canvas_item;

// === Tilt-Shift Post-Process Shader ===
// Creates miniature/diorama feel for the fjord view

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;

// Focus band
uniform float focus_center : hint_range(0.0, 1.0) = 0.5;
uniform float focus_width : hint_range(0.0, 0.5) = 0.15;
uniform float blur_amount : hint_range(0.0, 10.0) = 3.0;

// Saturation boost (miniature effect tends to oversaturate)
uniform float saturation_boost : hint_range(0.8, 2.0) = 1.3;

// Vignette
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.3;
uniform float vignette_radius : hint_range(0.0, 2.0) = 0.8;

// Color grading (warm miniature look)
uniform float warmth : hint_range(-0.1, 0.1) = 0.02;
uniform float contrast : hint_range(0.5, 2.0) = 1.1;

// Blur quality (higher = more samples, slower)
const int BLUR_SAMPLES = 8;

vec3 blur_sample(sampler2D tex, vec2 uv, float amount) {
	vec3 color = vec3(0.0);
	float total_weight = 0.0;
	vec2 pixel_size = vec2(1.0) / vec2(textureSize(tex, 0));

	for (int i = -BLUR_SAMPLES; i <= BLUR_SAMPLES; i++) {
		float fi = float(i);
		float weight = exp(-0.5 * (fi * fi) / (amount * amount));

		// Vertical blur (tilt-shift is primarily vertical)
		vec2 offset = vec2(0.0, fi * pixel_size.y * amount);
		color += texture(tex, uv + offset).rgb * weight;

		// Small horizontal component
		offset = vec2(fi * pixel_size.x * amount * 0.3, 0.0);
		color += texture(tex, uv + offset).rgb * weight * 0.3;

		total_weight += weight * 1.3;
	}

	return color / total_weight;
}

void fragment() {
	vec2 uv = SCREEN_UV;

	// Calculate distance from focus band
	float dist_from_center = abs(uv.y - focus_center);
	float blur_factor = smoothstep(focus_width, focus_width + 0.2, dist_from_center);

	// Sample with blur
	vec3 sharp = texture(screen_texture, uv).rgb;
	vec3 blurred = blur_sample(screen_texture, uv, blur_amount * blur_factor);
	vec3 color = mix(sharp, blurred, blur_factor);

	// Saturation boost
	float gray = dot(color, vec3(0.299, 0.587, 0.114));
	color = mix(vec3(gray), color, saturation_boost);

	// Warmth (shift toward orange/yellow)
	color.r += warmth;
	color.b -= warmth * 0.5;

	// Contrast
	color = (color - 0.5) * contrast + 0.5;

	// Vignette
	vec2 vig_uv = uv * 2.0 - 1.0;
	float vig = 1.0 - smoothstep(vignette_radius, vignette_radius + 0.5, length(vig_uv));
	color *= mix(1.0, vig, vignette_strength);

	COLOR = vec4(clamp(color, 0.0, 1.0), 1.0);
}
