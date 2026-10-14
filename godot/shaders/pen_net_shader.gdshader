shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

// Pen net: a fine dark mesh, drawn translucent. Close up the threads read as
// a knotless diamond mesh; once a mesh cell shrinks below a few pixels the
// pattern fades to its mean coverage, so the net becomes an even veil instead
// of shimmering moire. Drawn before the (transparent) water (material
// render_priority -1) so the sea tints the submerged part.
uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
// Mesh cells per UV unit (u runs round the pen, v down the wall).
uniform vec2 mesh_scale = vec2(180.0, 45.0);
uniform float thread_width : hint_range(0.01, 0.4) = 0.09;
// Coverage of a solid thread and of the far-field veil.
uniform float thread_alpha : hint_range(0.0, 1.0) = 0.9;
uniform float fade_start = 0.25;
uniform float fade_end = 0.6;
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap;
// Per-pen visibility (rival pens are drawn quieter, roadmap G1-07).
instance uniform float net_opacity = 1.0;

void fragment() {
	vec4 tex = texture(net_texture, UV);
	vec2 gv = UV * mesh_scale;
	float d1 = abs(fract((gv.x + gv.y) * 0.5) - 0.5) * 2.0;
	float d2 = abs(fract((gv.x - gv.y) * 0.5) - 0.5) * 2.0;
	float d = min(d1, d2);
	float aa = fwidth(d) * 1.5 + 0.0001;
	float pattern = 1.0 - smoothstep(thread_width - aa, thread_width + aa, d);
	// Area share of the two thread families: 1 - (1 - w)^2.
	float mean_cover = 1.0 - (1.0 - thread_width) * (1.0 - thread_width);
	vec2 cells_per_px = fwidth(gv);
	float far = smoothstep(fade_start, fade_end, max(cells_per_px.x, cells_per_px.y));
	float cover = mix(pattern, mean_cover, far) * tex.a;
	ALBEDO = albedo_color;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	ALPHA = clamp(cover * thread_alpha * net_opacity, 0.0, 1.0);
}
