shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

// A world-anchored status chip (VesselChip.gd): a D13 Ink glass pill with
// a 1 px accent rim and an accent dot, billboarded and at a constant size
// on screen. It scales with view depth exactly like a fixed-size Label3D,
// so the label drawn over it stays inside it at every distance.
//
// The mesh is a 1 x 1 QuadMesh; size_px is the pill in screen pixels at
// pixel_size world units per pixel (VesselChip.PIXEL_SIZE).
instance uniform vec2 size_px = vec2(160.0, 44.0);
instance uniform vec4 accent : source_color = vec4(0.549, 0.780, 0.855, 1.0);
uniform vec4 fill : source_color = vec4(0.043, 0.165, 0.227, 0.9);
uniform float pixel_size = 0.0005;
uniform float dot_px = 12.0;
uniform float pad_px = 14.0;
// Screen-space lift above the anchor, in pixels (keeps the chip clear of
// the site labels when the camera is far away).
uniform vec2 offset_px = vec2(0.0, 0.0);
// 1 / the eye's exposure (VesselChip.set_exposure, G3-05): the pill keeps
// its day brightness at night instead of washing out.
uniform float exposure_comp = 1.0;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	float sc = -MODELVIEW_MATRIX[3].z;
	MODELVIEW_MATRIX[0] *= sc;
	MODELVIEW_MATRIX[1] *= sc;
	MODELVIEW_MATRIX[2] *= sc;
	VERTEX.xy = VERTEX.xy * size_px * pixel_size + offset_px * pixel_size;
}

void fragment() {
	vec2 p = (UV - 0.5) * size_px;
	float r = size_px.y * 0.5;
	vec2 q = abs(p) - (size_px * 0.5 - vec2(r));
	float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
	float aa = max(fwidth(d), 0.5);
	float inside = 1.0 - smoothstep(-aa, aa, d);
	float rim = 1.0 - smoothstep(0.5, 0.5 + aa, abs(d + 1.0));
	vec2 dot_c = vec2(-size_px.x * 0.5 + pad_px + dot_px * 0.5, 0.0);
	float dd = length(p - dot_c) - dot_px * 0.5;
	float dot_mask = 1.0 - smoothstep(-aa, aa, dd);
	float accent_mix = max(dot_mask, rim * 0.65);
	ALBEDO = mix(fill.rgb, accent.rgb, accent_mix) * exposure_comp;
	ALPHA = inside * mix(fill.a, 1.0, accent_mix);
}
