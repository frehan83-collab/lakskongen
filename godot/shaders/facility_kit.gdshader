shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/include/mirror_pass.gdshaderinc"

// The farm-types facility kit's one material (LK_FacilityKit, F2-25; F2-20
// extends the per-instance part). Every land-kit mesh built by
// blender/build_land_base.py is a single surface whose UVs point at one row
// of a 1024 px trim atlas (facility_atlas.png): 32 horizontal strips of
// 32 px, U repeating along the surface every 16 m, V at the strip's centre.
// The strip is the part's colour and surface (seams, panels, corrugation);
// the atlas alpha is its roughness, or 1.0 where the strip glows at night
// (windows, lid work-lights). The vertex colour (COLOR_0.r) is baked AO.
//
// Strips are what the shader addresses instead of nodes: the lid swatch
// (dark #2A3036 by default, ref L1, or the pale cosmetic #EDEFEF, ref L2),
// the plain-wall variant (band off), the pipe-bridge legs' stretch and the
// feeder rotor's spin. With use_instance_custom a MultiMesh drives them per
// instance: INSTANCE_CUSTOM.r build progress (fragments above
// progress * build_height_m are discarded; build_height_m is per node, the
// tank type's shell height), .g lid swatch (0 dark, 1 pale), .b leg length /
// 10 m (pipe bridge) or band off (tanks, > 0.5), .a rotor: the phase in turns
// (fract), spinning at rotor_turns_per_s while a >= 1 (a tank that feeds).
// A plain mesh (a lot's merged buildings, F2-20) rises through the per-node
// build_cut_m instead: fragments above that model height are discarded.
//
// Mirror pass (G2-03): what stands below the water (quay piles, the intake
// basin's footing) is laid on the water plane for the planar mirror's camera,
// as the terrain does (mirror_pass.gdshaderinc).
//
// Mips: a strip is one texel tall at mip 5 (32 x 32), so the sampled level
// stops there and strips never bleed into each other.
//
// Roof lights (lookdev, wave 4): the skylight strip is pale polycarbonate by
// day and, unlike the glow strips, keeps its albedo; with night_light it
// glows a soft warm interior light (the dusk mood of ref L2).

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float atlas_strips = 32.0;
uniform float max_lod = 5.0;
// Baked AO: how much of it shows, and how much it also dims direct light.
uniform float ao_strength : hint_range(0.0, 1.0) = 0.85;
uniform float ao_light_affect : hint_range(0.0, 1.0) = 0.35;
// Night lights (presentation clock, D20: windows and lid work-lights light at
// every dusk and night). 0 by day; F2-20/F2-23 drive it.
uniform float night_light : hint_range(0.0, 1.0) = 0.0;
uniform float emission_energy = 2.4;
// Roof-light glow at night, relative to the windows (diffuse, dimmer).
uniform float roof_light_glow : hint_range(0.0, 1.0) = 0.25;
uniform vec3 roof_light_color : source_color = vec3(1.0, 0.86, 0.62);
// Daylight look of an emissive strip: dark glass.
uniform vec3 glass_color : source_color = vec3(0.10, 0.13, 0.15);
// Material-wide variants (a MultiMesh overrides them per instance).
uniform float lid_variant : hint_range(0.0, 1.0) = 0.0;
uniform float plain_wall : hint_range(0.0, 1.0) = 0.0;
uniform float leg_scale = 1.0;
uniform float rotor_turns_per_s = 0.5;
uniform bool use_instance_custom = false;
// Per node (F2-20): a MultiMesh's tank shell height, a plain mesh's build cut.
instance uniform float build_height_m = 13.0;
instance uniform float build_cut_m = 100000.0;
// Per node (F2-20): 1 makes a MultiMesh read its INSTANCE_CUSTOM (the shared
// material keeps use_instance_custom off for plain meshes).
instance uniform float instance_custom_on = 0.0;
// Strip rows (blender/build_land_base.py STRIPS order).
const float ROW_TANK_WALL = 0.0;
const float ROW_TANK_BAND = 1.0;
const float ROW_TANK_LID = 2.0;
const float ROW_ROTOR = 5.0;
const float ROW_BRIDGE_LEG = 14.0;
const float ROW_SKYLIGHT = 26.0;

varying float model_y;
varying float row_shift;
varying float build_cut;

float row_of(vec2 uv) {
	return floor(uv.y * atlas_strips);
}

void vertex() {
	float row = row_of(UV);
	float lid = lid_variant;
	float plain = plain_wall;
	float legs = leg_scale;
	float phase = 0.0;
	float spin = 0.0;
	build_cut = 1.0;
	if (use_instance_custom || instance_custom_on > 0.5) {
		build_cut = INSTANCE_CUSTOM.r;
		lid = INSTANCE_CUSTOM.g;
		plain = step(0.5, INSTANCE_CUSTOM.b);
		legs = INSTANCE_CUSTOM.b;
		phase = fract(INSTANCE_CUSTOM.a);
		spin = step(1.0, INSTANCE_CUSTOM.a);
	}
	// Pipe-bridge legs: stretch below the deck to the terrain.
	if (row == ROW_BRIDGE_LEG && VERTEX.y < 0.0) {
		VERTEX.y *= legs;
	}
	// The feeder's spreader turns about the feeder axis while it feeds.
	if (row == ROW_ROTOR) {
		float a = TAU * (phase + TIME * rotor_turns_per_s * spin);
		VERTEX.xz = mat2(vec2(cos(a), -sin(a)), vec2(sin(a), cos(a))) * VERTEX.xz;
		NORMAL.xz = mat2(vec2(cos(a), -sin(a)), vec2(sin(a), cos(a))) * NORMAL.xz;
	}
	// Swatch swaps move the sample to a neighbouring strip.
	row_shift = 0.0;
	if (row == ROW_TANK_LID && lid > 0.5) {
		row_shift = 1.0;
	} else if (row == ROW_TANK_BAND && plain > 0.5) {
		row_shift = ROW_TANK_WALL - ROW_TANK_BAND;
	}
	model_y = VERTEX.y;
	VERTEX = lk_mirror_vertex(VERTEX, MODEL_MATRIX, CAMERA_VISIBLE_LAYERS, CAMERA_POSITION_WORLD);
}

void fragment() {
	if ((build_cut < 0.999 && model_y > build_cut * build_height_m) || model_y > build_cut_m) {
		discard;
	}
	vec2 uv = UV + vec2(0.0, row_shift / atlas_strips);
	vec2 size = vec2(textureSize(atlas, 0));
	vec2 dx = dFdx(UV * size);
	vec2 dy = dFdy(UV * size);
	float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
	vec4 t = textureLod(atlas, uv, clamp(lod, 0.0, max_lod));
	bool glow = t.a > 0.985;
	bool roof_light = row_of(UV) == ROW_SKYLIGHT;
	ALBEDO = glow ? glass_color : t.rgb;
	ROUGHNESS = glow ? 0.12 : t.a;
	METALLIC = 0.0;
	SPECULAR = glow ? 0.6 : 0.45;
	EMISSION = glow ? t.rgb * night_light * emission_energy : vec3(0.0);
	if (roof_light) {
		EMISSION = roof_light_color * night_light * emission_energy * roof_light_glow;
	}
	AO = mix(1.0, COLOR.r, ao_strength);
	AO_LIGHT_AFFECT = ao_light_affect;
}
