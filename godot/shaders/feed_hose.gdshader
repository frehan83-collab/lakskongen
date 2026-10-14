shader_type spatial;
render_mode world_vertex_coords, cull_disabled, diffuse_burley, specular_schlick_ggx, shadows_disabled;

#include "res://shaders/site_float.gdshaderinc"

// White floating feed hoses (roadmap G2-04, owner reference 1): FeedHoses.gd
// builds every hose of a site as one ribbon mesh along its centreline
// (VERTEX the centre, NORMAL the hose direction, UV.x the side -1/+1, UV.y
// metres along the hose, UV2.x the lift above the water where the hose
// climbs to the barge's deck). Here each centre point rides the drawn water
// (site_float.gdshaderinc) and the ribbon opens across the view to the
// hose's real 110 mm, or to `min_pixels` on screen when that is wider, so a
// site's hoses read as the thin white lines of the aerial photos from FARM
// to COAST zoom. The ribbon's lower edge never dips under the water.
uniform vec3 hose_color : source_color = vec3(0.929, 0.937, 0.937);
uniform float roughness : hint_range(0.0, 1.0) = 0.42;
uniform float hose_radius = 0.055;
// The hose floats this high: its centre over the water (m).
uniform float float_y = 0.035;
uniform float min_pixels = 1.6;
// Far away the minimum width eases down, so distant sites stay quiet.
uniform vec2 far_ease_m = vec2(1400.0, 3200.0);
uniform float far_min_pixels = 0.8;

void vertex() {
	vec3 t = normalize(NORMAL);
	vec3 c = VERTEX;
	float water = float_height(c.xz);
	vec3 to_cam = CAMERA_POSITION_WORLD - vec3(c.x, water, c.z);
	float dist = max(length(to_cam), 0.01);
	vec3 side = cross(t, to_cam / dist);
	side = length(side) > 1e-4 ? normalize(side) : vec3(-t.z, 0.0, t.x);
	float m_per_px = 2.0 * dist / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float px = mix(min_pixels, far_min_pixels, smoothstep(far_ease_m.x, far_ease_m.y, dist));
	float r = max(hose_radius, 0.5 * px * m_per_px);
	// Widened for the screen, the hose also floats a little higher (under a
	// pixel), so the drawn grid's coarser facets far away never cover it.
	c.y = water + float_y + (r - hose_radius) * 0.6 + UV2.x;
	vec3 p = c + side * UV.x * r;
	p.y = max(p.y, water + 0.004);
	VERTEX = p;
	vec3 face = normalize(cross(side, t));
	face = dot(face, to_cam) < 0.0 ? -face : face;
	NORMAL = normalize(face * 0.6 + side * UV.x * 0.8);
}

void fragment() {
	ALBEDO = hose_color;
	ROUGHNESS = roughness;
	SPECULAR = 0.4;
}
