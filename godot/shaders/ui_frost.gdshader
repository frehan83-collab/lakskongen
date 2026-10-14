shader_type canvas_item;

// Frosted Ink backdrop for modal menus (pause menu, credits): the frame
// behind is read from the screen texture at a coarse mip, nine taps spread
// over that mip's texels (a cheap, smooth blur), then tinted toward Ink, so
// the fjord stays visible as atmosphere but no detail competes with the menu.
// Kyst & Stål: Ink #0B2A3A.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(0.043, 0.165, 0.227, 1.0);
// 0 = no tint, 1 = solid Ink.
uniform float tint_amount : hint_range(0.0, 1.0) = 0.62;
// Mip level read from the screen texture (higher = softer).
uniform float blur_lod : hint_range(0.0, 6.0) = 4.0;
// Darken toward the edges so the centred menu reads first.
uniform float vignette : hint_range(0.0, 1.0) = 0.35;

vec3 blurred(vec2 uv, vec2 texel) {
	vec2 step_uv = texel * exp2(blur_lod);
	vec3 sum = textureLod(screen_tex, uv, blur_lod).rgb * 4.0;
	sum += textureLod(screen_tex, uv + vec2(step_uv.x, 0.0), blur_lod).rgb * 2.0;
	sum += textureLod(screen_tex, uv - vec2(step_uv.x, 0.0), blur_lod).rgb * 2.0;
	sum += textureLod(screen_tex, uv + vec2(0.0, step_uv.y), blur_lod).rgb * 2.0;
	sum += textureLod(screen_tex, uv - vec2(0.0, step_uv.y), blur_lod).rgb * 2.0;
	sum += textureLod(screen_tex, uv + step_uv, blur_lod).rgb;
	sum += textureLod(screen_tex, uv - step_uv, blur_lod).rgb;
	sum += textureLod(screen_tex, uv + vec2(step_uv.x, -step_uv.y), blur_lod).rgb;
	sum += textureLod(screen_tex, uv + vec2(-step_uv.x, step_uv.y), blur_lod).rgb;
	return sum / 16.0;
}

void fragment() {
	vec3 frosted = mix(blurred(SCREEN_UV, SCREEN_PIXEL_SIZE), tint.rgb, tint_amount);
	vec2 centred = SCREEN_UV - vec2(0.5);
	float edge = smoothstep(0.25, 0.75, length(centred * vec2(1.2, 1.0)));
	frosted = mix(frosted, tint.rgb * 0.7, edge * vignette);
	COLOR = vec4(frosted, COLOR.a);
}
