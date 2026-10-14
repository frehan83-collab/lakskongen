shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

// Pen net and bird net: a tiling knotless-mesh texture (alpha = thread
// coverage, blender/build_pen.py --textures), drawn translucent. The mip
// chain averages the threads into their mean coverage, so the far net is an
// even veil instead of shimmering moire, and the threads resolve up close.
// The submerged net material draws before the transparent water
// (render_priority -1) so the sea tints it; the jump net and bird net above
// the waterline draw after it (render_priority 1).
//
// UV is in metres (u round the pen, v down the net) on the Blender meshes.
uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres of net per texture tile (the tile holds 2 x 2 diamonds).
uniform float tile_m = 0.25;
uniform float thread_alpha : hint_range(0.0, 1.0) = 0.9;
// The far veil reads a little denser than the raw mean coverage, the way a
// real net darkens the water behind it.
uniform float far_boost : hint_range(1.0, 3.0) = 1.6;
// Marine growth near the surface: greener, denser threads in the top metres.
uniform vec3 fouling_color : source_color = vec3(0.16, 0.20, 0.13);
uniform float fouling_depth = 4.0;
uniform float fouling_amount : hint_range(0.0, 1.0) = 0.45;
// Per-pen visibility (rival pens are drawn quieter, roadmap G1-07).
instance uniform float net_opacity = 1.0;

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

void fragment() {
	vec2 uv = UV / tile_m;
	float cover = texture(net_texture, uv).a;
	// How many texture tiles one pixel spans: 0 up close, >1 far away.
	vec2 fw = fwidth(uv);
	float far = smoothstep(0.15, 0.6, max(fw.x, fw.y));
	cover = min(cover * mix(1.0, far_boost, far), 1.0);
	float foul = fouling_amount * (1.0 - smoothstep(0.0, fouling_depth, -model_y)) * step(model_y, 0.3);
	ALBEDO = mix(albedo_color, fouling_color, foul);
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	ALPHA = clamp(cover * thread_alpha * (1.0 + 0.4 * foul) * net_opacity, 0.0, 1.0);
}
