shader_type spatial;
render_mode cull_disabled, depth_draw_always, alpha_to_coverage_and_one, diffuse_burley, specular_schlick_ggx;

// Pen net and bird net (A2-01, G1-06): a tiling knotless-mesh texture (alpha
// = thread coverage, blender/build_pen.py --textures) drawn with
// alpha-to-coverage. MSAA resolves each pixel's thread coverage instead of a
// blend, and alpha_to_coverage_and_one writes alpha 1 after the coverage
// mask: nothing blends, the net sorts like an opaque surface. Once one pixel
// spans more of a tile than FADE_FWIDTH, the net fades to its mean coverage
// (the texture's smallest mip, which averages every thread), so a distant or
// orbiting pen shows a quiet veil instead of moire; the threads resolve up
// close.
//
// UV is in metres (u round the pen, v down the net) on the Blender meshes.
uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres of net per texture tile (the tile holds 2 x 2 diamonds).
uniform float tile_m = 0.25;
uniform float thread_alpha : hint_range(0.0, 1.0) = 0.9;
// The far veil reads a little denser than the raw mean coverage, the way a
// real net darkens the water behind it (and so it keeps at least one MSAA
// sample of four).
uniform float far_boost : hint_range(1.0, 3.0) = 1.6;
// Marine growth near the surface: greener, denser threads in the top metres.
uniform vec3 fouling_color : source_color = vec3(0.16, 0.20, 0.13);
uniform float fouling_depth = 4.0;
uniform float fouling_amount : hint_range(0.0, 1.0) = 0.45;
// Per-pen visibility (rival pens are drawn quieter, roadmap G1-07).
instance uniform float net_opacity = 1.0;

// Tiles per pixel past which the pattern gives way to its mean.
const float FADE_FWIDTH = 0.35;
const float FADE_FROM = 0.15;
// Past the texture's last mip level: the 1 x 1 average of the tile.
const float MEAN_LOD = 16.0;

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

// Fraction of the net the threads cover: the tile's average alpha.
float mean_coverage() {
	return textureLod(net_texture, vec2(0.5), MEAN_LOD).a;
}

void fragment() {
	vec2 uv = UV / tile_m;
	// How many texture tiles one pixel spans: 0 up close, >1 far away.
	vec2 fw = fwidth(uv);
	float fade = smoothstep(FADE_FROM, FADE_FWIDTH, max(fw.x, fw.y));
	float cover = mix(texture(net_texture, uv).a, mean_coverage(), fade);
	cover = min(cover * mix(1.0, far_boost, fade), 1.0);
	float foul = fouling_amount * (1.0 - smoothstep(0.0, fouling_depth, -model_y)) * step(model_y, 0.3);
	ALBEDO = mix(albedo_color, fouling_color, foul);
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	ALPHA = clamp(cover * thread_alpha * (1.0 + 0.4 * foul) * net_opacity, 0.0, 1.0);
}
