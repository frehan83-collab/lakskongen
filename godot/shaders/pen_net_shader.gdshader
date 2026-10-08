shader_type spatial;
render_mode cull_disabled, blend_mix, depth_draw_never;

uniform vec3 albedo_color : source_color = vec3(0.15, 0.2, 0.25);
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap;

void fragment() {
	vec4 tex = texture(net_texture, UV);
	if (tex.a < alpha_cutoff) {
		discard;
	}
	ALBEDO = albedo_color;
	ALPHA = tex.a * 0.28;
}