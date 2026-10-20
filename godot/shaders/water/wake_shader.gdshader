shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

// === Boat wakes (G2-03, VesselWakes.gd) ===
//
// Ribbons laid on the water behind a moving boat: the white wash down the
// track (kind 0) and the two arms of the Kelvin wedge (kinds 1 and 2,
// left and right), which stand 19.47 degrees either side of the track at
// the distance the boat has gone since it passed. Each vertex carries the
// track point it was laid at, the track's sideways unit vector, when, how
// fast the boat went and its beam; the shape and the fade follow from the
// age here, so the mesh is rebuilt only when the boat lays a new point.
//
// Vertex data (VesselWakes._rebuild):
//   VERTEX  the track point (world, at the still water level)
//   NORMAL  the track's sideways unit vector (world)
//   UV      x: -1 inner edge, +1 outer edge; y: kind (0 wash, 1 left arm, 2 right arm)
//   UV2     x: time laid (s, VesselWakes clock); y: the boat's speed (m/s)
//   COLOR   r: beam / 100 m; g: strength (0-1 by speed); b: track length
//           from the first point / 1000 m (texture along the track)

uniform float now = 0.0;
// 1 in a calm, 0 once the sea's own foam hides any wake.
uniform float sea_visibility : hint_range(0.0, 1.0) = 1.0;
// Height over the still water (m): above the calm sea's ripples.
uniform float lift = 0.18;
uniform vec3 foam_color : source_color = vec3(0.9, 0.93, 0.95);
// R foam lace, G foam patches (tools/gen_water_textures.py water_foam.png).
uniform sampler2D foam_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

// Mirrored in VesselWakes.gd.
const float KELVIN_TAN = 0.35355;
const float FOAM_LIFE_S = 24.0;
const float FOAM_SPREAD = 0.45;
const float KELVIN_FADE_M = 200.0;
const float KELVIN_ALPHA = 0.3;
// An arm is a band of broken water this wide (m), widening down the wedge.
const float ARM_HALF_M = 2.5;
const float ARM_SPREAD = 0.03;

varying float v_alpha;
varying float v_across;
varying float v_kind;
varying vec2 v_tex;

void vertex() {
	float age = max(now - UV2.x, 0.0);
	float speed = UV2.y;
	float beam = COLOR.r * 100.0;
	float strength = COLOR.g;
	float behind = speed * age;
	int kind = int(UV.y + 0.5);
	float offset;
	float alpha;
	float half_width;
	if (kind == 0) {
		half_width = 0.5 * beam * (1.0 + FOAM_SPREAD * sqrt(age));
		offset = UV.x * half_width;
		alpha = strength * exp(-age / FOAM_LIFE_S);
	} else {
		float side = kind == 1 ? -1.0 : 1.0;
		half_width = ARM_HALF_M + ARM_SPREAD * behind;
		offset = side * (0.5 * beam + KELVIN_TAN * behind + UV.x * half_width);
		alpha = strength * KELVIN_ALPHA * exp(-behind / KELVIN_FADE_M) * smoothstep(0.0, 8.0, behind);
	}
	vec3 p = VERTEX + NORMAL * offset;
	p.y = lift;
	VERTEX = p;
	NORMAL = vec3(0.0, 1.0, 0.0);
	v_alpha = alpha * sea_visibility;
	v_across = UV.x;
	v_kind = float(kind);
	// Texture in metres: across by the band, along by the track.
	v_tex = vec2(UV.x * half_width, COLOR.b * 1000.0);
}

void fragment() {
	float lace = texture(foam_texture, v_tex / 9.0).r;
	float patches = texture(foam_texture, v_tex / 47.0 + 0.3).g;
	float profile;
	float grain;
	if (v_kind < 0.5) {
		// The wash: densest on the track, ragged at its edges.
		profile = 1.0 - smoothstep(0.35, 1.0, abs(v_across) + (lace - 0.5) * 0.35);
		grain = 0.45 + 0.55 * lace;
	} else {
		// An arm: broken water, not a painted line: a soft band whose
		// breaking crests come and go.
		profile = exp(-v_across * v_across * 2.5);
		grain = lace * lace * (0.4 + 0.6 * patches);
	}
	ALBEDO = foam_color;
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
	ALPHA = clamp(v_alpha * profile * grain * (0.7 + 0.3 * patches), 0.0, 1.0);
}
