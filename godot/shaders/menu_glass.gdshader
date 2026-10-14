shader_type canvas_item;

// Ink glass for the main menu's column (roadmap U1-09). The live fjord
// behind the rect is read from a coarse mip of the screen texture (nine
// taps, a smooth blur) and laid under the rect's own colour: Ink at the
// glass alpha. What is drawn is exactly "Ink at alpha a over the world",
// which ThemeLint judges over a white and a black world, so the column's
// text holds its contrast over every mood (snow, sun glint, night).
// A faint light along the right edge reads as the glass's bevel.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
// Mip level read from the screen texture (higher = softer).
uniform float blur_lod : hint_range(0.0, 6.0) = 4.5;
// Width (px) and strength of the bevel highlight on the right edge.
uniform float edge_px = 1.5;
uniform float edge_light : hint_range(0.0, 0.5) = 0.10;

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
	vec3 behind = blurred(SCREEN_UV, SCREEN_PIXEL_SIZE);
	vec3 glass = mix(behind, COLOR.rgb, COLOR.a);
	// UV.x reaches 1 at the right edge; the bevel is a hairline of light.
	float px_from_edge = (1.0 - UV.x) / max(abs(dFdx(UV.x)), 1e-6);
	float bevel = 1.0 - smoothstep(0.0, edge_px, px_from_edge);
	glass = mix(glass, vec3(1.0), bevel * edge_light);
	COLOR = vec4(glass, 1.0);
}
