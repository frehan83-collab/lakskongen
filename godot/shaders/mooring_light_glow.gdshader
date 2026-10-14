shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

// Halo of a mooring corner light: a camera-facing quad (MultiMesh, one per
// lamp) that never shrinks below `min_pixels` on screen, so the site's
// synchronised flashes read from FARM to COAST zoom. Same flash timing as
// mooring_lamp_shader.gdshader (global TIME: every lamp flashes together).
uniform vec3 glow_color : source_color = vec3(1.0, 0.761, 0.227);
uniform float size_m = 1.4;
uniform float min_pixels = 9.0;
uniform float period_s = 4.0;
uniform float flash_s = 0.5;
uniform float intensity : hint_range(0.0, 4.0) = 1.8;
uniform float idle : hint_range(0.0, 1.0) = 0.06;

void vertex() {
	vec4 center = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float dist = max(-center.z, 0.01);
	float m_per_px = 2.0 * dist / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float s = max(size_m, min_pixels * m_per_px);
	// Billboard in view space, nudged toward the camera past the lens.
	vec4 view_pos = center + vec4(VERTEX.xy * s, 0.0, 0.0);
	view_pos.xyz += normalize(-center.xyz) * 0.35;
	POSITION = PROJECTION_MATRIX * view_pos;
}

void fragment() {
	float d = length(UV - vec2(0.5)) * 2.0;
	float core = exp(-d * d * 9.0);
	float halo = exp(-d * d * 2.5) * 0.35;
	float t = mod(TIME, period_s);
	float on = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(flash_s, flash_s + 0.08, t));
	float a = (core + halo) * (1.0 - smoothstep(0.85, 1.0, d));
	ALBEDO = glow_color;
	ALPHA = clamp(a * mix(idle, 1.0, on) * intensity, 0.0, 1.0);
}
