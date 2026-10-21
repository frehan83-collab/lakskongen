shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/include/mirror_pass.gdshaderinc"

// The offshore kit's one solid material (LK_OffshoreKit, farm-types F2-26).
// Every solid face of a mesh built by blender/build_offshore.py points at one
// row of a 1024 px trim atlas (offshore_atlas.png): 32 horizontal strips of
// 32 px, U repeating along the surface every 16 m, V at the strip's centre.
// The strip is the part's colour and surface (plate panels, grating, seams);
// the atlas alpha is its roughness, or 1.0 where it glows (windows, nav
// lights). Strip sampling and the mip clamp follow facility_kit.gdshader
// (the land kit): a strip is one texel tall at mip 5, so the sampled level
// stops there and strips never bleed into each other.
//
// Vertex data: COLOR.r is baked ambient occlusion; UV2.x is the build part
// group (0 Pontoons, 1 Columns, 2 Rings, 3 Bracing, 4 Topsides, 5 Net).
// `build_stage` (per instance, 0..6) reveals the groups in yard order for the
// yard picture-in-picture and the First Sight timelapse: groups below
// floor(build_stage) stand complete, the current one rises through its own
// height band (GROUP_LO..GROUP_HI: the pontoons at the keel, the topsides on
// the deck), later ones are not drawn. 6 (the default) draws the finished
// unit.
//
// Paint ages through the vessel kit's weathering tile (grime, run-down rust
// streaks), projected in model space on the dominant axis, so heave and the
// tilt pose carry it along. The splash zone is wet: rougher paint dries
// above it. Nav lanterns flash in sync at 1 Hz when `nav_lights` is up (night,
// Kystverket synchronised yellow [O:R58]); TIME is global, so every unit's
// lanterns flash together.

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float atlas_strips = 32.0;
uniform float max_lod = 5.0;
uniform float ao_strength : hint_range(0.0, 1.0) = 0.85;
uniform float ao_light_affect : hint_range(0.0, 1.0) = 0.35;
// Windows and deck lights at dusk and night (presentation clock, D20).
uniform float night_light : hint_range(0.0, 1.0) = 0.0;
uniform float emission_energy = 0.9;
uniform vec3 glass_color : source_color = vec3(0.10, 0.13, 0.15);
// F2-23 (spec §5.5 rule 1: the home view shows landmarks): far off, the
// distance fog takes the hull's safety yellow down to a grey sliver; by day
// the hull keeps a share of its own colour through it (lifted by the light
// the fog takes, from LANDMARK_FROM_M on; the presenter's landmark_lift
// is 0 at night) so the unit still reads as the yellow landmark.
uniform float landmark_lift : hint_range(0.0, 4.0) = 0.0;
uniform float fog_density = 0.0;
const float LANDMARK_FROM_M = 900.0;
const float LANDMARK_FULL_M = 1800.0;
// Nav lanterns: 0 by day; F2-21 drives it with the night.
uniform float nav_lights : hint_range(0.0, 1.0) = 0.0;
uniform float nav_energy = 9.0;
uniform float nav_flash_hz = 1.0;
uniform float nav_duty : hint_range(0.05, 0.95) = 0.35;
// Weathering (vessel_weathering.png: R grime, G roughness, B streaks).
uniform sampler2D weathering : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float weather_tile_m = 6.0;
uniform float grime : hint_range(0.0, 1.0) = 0.45;
uniform float streaks : hint_range(0.0, 1.0) = 0.3;
uniform vec3 streak_color : source_color = vec3(0.36, 0.22, 0.12);
// Wet band round the waterline (model y = 0 is the design waterline).
uniform float wet_top = 2.5;
uniform float wet_bottom = -3.0;
// The build reveal (yard and timelapse), per unit.
instance uniform float build_stage = 6.0;
// Height band (model y) each part group rises through while it is built:
// Pontoons, Columns, Rings, Bracing, Topsides, Net.
const float GROUP_LO[6] = {-43.5, -43.5, -37.0, -37.0, 14.5, -35.5};
const float GROUP_HI[6] = {-34.5, 16.0, 16.5, 16.0, 29.5, 17.0};

// Strip rows (blender/build_offshore.py STRIPS order).
const float ROW_HULL = 0.0;
const float ROW_SPLASH = 1.0;
const float ROW_WHITE = 4.0;
const float ROW_GREY = 5.0;
const float ROW_SILO = 6.0;
const float ROW_CRANE = 10.0;
const float ROW_NAV = 11.0;

varying vec3 model_pos;
varying vec3 model_normal;
varying float part_group;

void vertex() {
	model_pos = VERTEX;
	model_normal = NORMAL;
	part_group = floor(UV2.x + 0.5);
	// F2-20 mirror pass (G2-03): the pontoons, columns and bracing below the
	// waterline are laid on the water plane for the planar mirror's camera,
	// which sits under the water and would see them over the reflection.
	VERTEX = lk_mirror_vertex(VERTEX, MODEL_MATRIX, CAMERA_VISIBLE_LAYERS, CAMERA_POSITION_WORLD);
}

float row_of(vec2 uv) {
	return floor(uv.y * atlas_strips);
}

void fragment() {
	float reveal = clamp(build_stage - part_group, 0.0, 1.0);
	if (reveal < 1.0) {
		int g = clamp(int(part_group), 0, 5);
		if (reveal <= 0.0 || model_pos.y > mix(GROUP_LO[g], GROUP_HI[g], reveal)) {
			discard;
		}
	}
	vec2 size = vec2(textureSize(atlas, 0));
	vec2 dx = dFdx(UV * size);
	vec2 dy = dFdy(UV * size);
	float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
	vec4 t = textureLod(atlas, UV, clamp(lod, 0.0, max_lod));
	float row = row_of(UV);
	bool glow = t.a > 0.985;
	bool nav = row == ROW_NAV;
	vec3 albedo = t.rgb;
	float rough = t.a;
	bool paint = row == ROW_HULL || row == ROW_SPLASH || row == ROW_WHITE || row == ROW_GREY
		|| row == ROW_SILO || row == ROW_CRANE;
	if (paint) {
		vec3 n = abs(model_normal);
		vec2 wuv = n.y > max(n.x, n.z) ? model_pos.xz : (n.x > n.z ? model_pos.zy : model_pos.xy);
		wuv /= weather_tile_m;
		vec4 w = texture(weathering, wuv);
		vec4 w2 = texture(weathering, mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * wuv * 0.31 + vec2(0.37, 0.11));
		float g = mix(1.0, w.r * (0.84 + 0.32 * w2.r), grime);
		// Rust runs down the hull steel; the houses and silos stay cool grey.
		float hull = (row == ROW_HULL || row == ROW_SPLASH) ? 1.0 : 0.3;
		float st = clamp(w.b * (0.4 + 1.2 * w2.g), 0.0, 1.0) * streaks * hull;
		albedo = mix(albedo * g, streak_color * g, st * 0.5);
		rough = clamp(rough + (w.g - 0.5) * 0.3 * grime + st * 0.12, 0.05, 1.0);
	}
	float wet = smoothstep(wet_bottom - 0.5, wet_bottom + 0.5, model_pos.y) * (1.0 - smoothstep(wet_top - 1.5, wet_top, model_pos.y));
	rough = mix(rough, rough * 0.45, wet * (paint ? 1.0 : 0.5));
	albedo *= mix(1.0, 0.82, wet);
	vec3 emit = vec3(0.0);
	if (nav) {
		float ph = fract(TIME * nav_flash_hz);
		float on = 1.0 - smoothstep(nav_duty - 0.04, nav_duty, ph);
		ALBEDO = albedo * 0.6;
		ROUGHNESS = 0.2;
		emit = t.rgb * nav_lights * on * nav_energy;
	} else if (glow) {
		ALBEDO = glass_color;
		ROUGHNESS = 0.12;
		emit = t.rgb * night_light * emission_energy;
	} else {
		ALBEDO = albedo;
		ROUGHNESS = rough;
		if ((row == ROW_HULL || row == ROW_SPLASH) && landmark_lift > 0.0) {
			float d = length(VERTEX);
			float tr = exp(-fog_density * d);
			// Through haze only: in rain, snow or a storm (tr < 0.4) the chip
			// carries the read, and a lit hull in the murk would glow.
			emit = albedo * landmark_lift * (1.0 - tr) * smoothstep(0.25, 0.5, tr) * smoothstep(LANDMARK_FROM_M, LANDMARK_FULL_M, d);
		}
	}
	METALLIC = 0.0;
	SPECULAR = glow ? 0.6 : 0.45;
	EMISSION = emit;
	AO = mix(1.0, COLOR.r, ao_strength);
	AO_LIGHT_AFFECT = ao_light_affect;
}
