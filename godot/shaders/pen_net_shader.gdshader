shader_type spatial;
render_mode cull_disabled;

// Procedural diamond-mesh pen net (opaque alpha-cutout: no transparency
// sorting issues against the water surface). The old texture path rendered
// a solid glass tube whenever net_texture was unassigned (default white).
// net_texture is still honored as an extra mask when one IS assigned.
uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
uniform vec2 mesh_scale = vec2(180.0, 45.0);
uniform float thread_width : hint_range(0.01, 0.4) = 0.09;
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap;

void fragment() {
	vec4 tex = texture(net_texture, UV);
	// Two diagonal thread families -> diamond mesh.
	vec2 gv = UV * mesh_scale;
	float d1 = abs(fract((gv.x + gv.y) * 0.5) - 0.5) * 2.0;
	float d2 = abs(fract((gv.x - gv.y) * 0.5) - 0.5) * 2.0;
	float d = min(d1, d2);
	float aa = fwidth(d) * 1.5 + 0.0001;
	float cover = (1.0 - smoothstep(thread_width - aa, thread_width + aa, d)) * tex.a;
	if (cover < 0.5) {
		discard;
	}
	ALBEDO = albedo_color;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
}
