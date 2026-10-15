shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

// Flat signal ring on the water around a pen (roadmap G1-07): the pen's
// health readout and the selection marker. Drawn analytically on one quad
// lying in the local XZ plane, so the line stays crisp and anti-aliased at
// every zoom and costs two triangles. The line never gets thinner than
// `min_pixel_width` on screen, so the signal still reads from COAST zoom.
uniform float ring_radius = 28.4;
uniform float ring_half_width = 0.30;
uniform float min_pixel_width = 2.0;
// Soft falloff outside the line (metres at close range, widened with distance).
uniform float halo_width = 1.4;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.30;
// Dashes: 0 = solid line. `spin_speed` turns the dash pattern (rad/s).
uniform float dash_count = 0.0;
uniform float dash_duty : hint_range(0.05, 0.95) = 0.55;
uniform float spin_speed = 0.0;

// Per-pen state without per-pen materials: FishPenManager / PenCage write
// these through GeometryInstance3D.set_instance_shader_parameter().
instance uniform vec4 ring_color : source_color = vec4(0.498, 0.839, 0.608, 0.55);
instance uniform float pulse_hz = 0.0;
// How much of the soft halo shows: a healthy ring stays a quiet line, a
// warning glows (PenCage.HEALTH_GLOW).
instance uniform float glow = 1.0;
// 1 / the eye's exposure (SiteLights.sync): the ring keeps its day
// brightness at night instead of blooming into a halo.
uniform float exposure_comp = 1.0;

varying vec2 local_xz;

void vertex() {
	local_xz = VERTEX.xz;
}

void fragment() {
	float r = length(local_xz);
	// Metres covered by one pixel at this fragment's depth.
	float m_per_px = 2.0 * length(VERTEX) / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float hw = max(ring_half_width, 0.5 * min_pixel_width * m_per_px);
	float d = abs(r - ring_radius);
	float aa = max(fwidth(r), 1e-4);
	float line = 1.0 - smoothstep(hw - aa, hw + aa, d);
	float halo = glow * halo_strength * exp(-max(d - hw, 0.0) / max(halo_width, 3.0 * m_per_px));
	if (dash_count > 0.0) {
		// Dashes cut the line only; the glow under it stays continuous.
		float turns = (atan(local_xz.y, local_xz.x) + TIME * spin_speed) / TAU;
		float f = fract(turns * dash_count);
		// Edge softness in dash units; clamp so the atan seam stays sharp.
		float fa = clamp(fwidth(turns) * dash_count, 0.002, 0.05);
		float dash = smoothstep(0.0, fa, f) * (1.0 - smoothstep(dash_duty - fa, dash_duty, f));
		line *= dash;
	}
	float mask = max(line, halo);
	float pulse = pulse_hz > 0.0 ? 0.78 + 0.22 * sin(TIME * TAU * pulse_hz) : 1.0;
	ALBEDO = ring_color.rgb * exposure_comp;
	ALPHA = clamp(mask * ring_color.a * pulse, 0.0, 1.0);
}
