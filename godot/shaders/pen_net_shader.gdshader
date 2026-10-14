shader_type spatial;
render_mode cull_disabled, depth_draw_always, alpha_to_coverage_and_one;

// Procedural diamond-mesh pen net (G1-06). The threads are drawn as an
// analytic coverage with alpha-to-coverage: MSAA resolves each pixel's thread
// coverage instead of a hard cutout, and once the mesh is too fine for the
// pixels (fwidth of the mesh coordinate past FADE_FWIDTH) the net fades to
// its mean coverage, so a distant or orbiting pen shows a quiet grey veil
// instead of moire. alpha_to_coverage_and_one writes alpha 1 after the
// coverage mask: nothing blends, the net sorts like an opaque surface.
// net_texture is still honoured as an extra mask when one is assigned.
uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
uniform vec2 mesh_scale = vec2(180.0, 45.0);
uniform float thread_width : hint_range(0.01, 0.4) = 0.09;
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap;

// Mesh cells per pixel past which the pattern gives way to its mean.
const float FADE_FWIDTH = 0.35;
const float FADE_FROM = 0.2;

// Fraction of the surface the two thread families cover: a point is bare
// only if it is off both, (1 - w)^2.
float mean_coverage(float w) {
	return 1.0 - (1.0 - w) * (1.0 - w);
}

void fragment() {
	vec4 tex = texture(net_texture, UV);
	// Two diagonal thread families -> diamond mesh.
	vec2 gv = UV * mesh_scale;
	float d1 = abs(fract((gv.x + gv.y) * 0.5) - 0.5) * 2.0;
	float d2 = abs(fract((gv.x - gv.y) * 0.5) - 0.5) * 2.0;
	float d = min(d1, d2);
	float aa = fwidth(d) * 0.75 + 0.0001;
	float thread = 1.0 - smoothstep(thread_width - aa, thread_width + aa, d);
	vec2 fw = fwidth(gv);
	float cells_per_pixel = max(fw.x, fw.y);
	float fade = smoothstep(FADE_FROM, FADE_FWIDTH, cells_per_pixel);
	ALPHA = mix(thread, mean_coverage(thread_width), fade) * tex.a;
	ALBEDO = albedo_color;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
}
