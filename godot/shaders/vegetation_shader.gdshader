shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx, alpha_to_coverage;

// === Fjord trees (G2-02): spruce and birch through MultiMesh ===
//
// Meshes from blender/build_vegetation.py: UV.x is the part (0 trunk,
// 1 foliage), UV.y the baked ambient occlusion, and the foliage normals point
// out of the crown so a few low-poly masses light like soft volumes.
// INSTANCE_CUSTOM (TerrainGeneratorBridge.VegetationChunks): x tint, y a
// second variation, z scale, w the tree's base altitude (m).
//
// The season is the terrain's (ground_season.gdshaderinc): birch leaves out,
// turns yellow and drops to a purple-grey twig haze, and snow settles on the
// up-facing foliage of trees above the snow line, as in a winter fjord.

#include "res://shaders/ground_season.gdshaderinc"
#include "res://shaders/include/cloud_shadow.gdshaderinc"

// 0 spruce, 1 birch.
uniform int species : hint_range(0, 1) = 0;
uniform vec3 spruce_shade : source_color = vec3(0.090, 0.149, 0.110);   // #172619 inside
uniform vec3 spruce_lit : source_color = vec3(0.196, 0.290, 0.212);     // #324A36 outer tips
uniform vec3 birch_shade : source_color = vec3(0.184, 0.271, 0.141);    // #2F4524
uniform vec3 birch_lit : source_color = vec3(0.333, 0.447, 0.212);      // #557236
uniform vec3 spruce_bark : source_color = vec3(0.290, 0.247, 0.208);    // #4A3F35
uniform vec3 birch_bark : source_color = vec3(0.890, 0.878, 0.839);     // #E3E0D6
uniform vec3 snow_color : source_color = vec3(0.949, 0.961, 0.973);
// Birch crowns draw in a little toward their twigs when bare (fraction of full size).
uniform float bare_crown_scale : hint_range(0.4, 1.0) = 0.88;
// Share of a bare birch crown's surface the twigs still cover (the rest is
// cut away: a lacy, see-through crown in winter).
uniform float bare_twig_cover : hint_range(0.1, 1.0) = 0.38;

varying float part;
varying float occlusion;
varying float tint;
varying float variation;
varying float base_altitude;
varying vec3 normal_world;
varying float height_local;
varying vec3 local_pos;
varying vec3 world_pos;

void vertex() {
	part = UV.x;
	occlusion = UV.y;
	tint = INSTANCE_CUSTOM.x;
	variation = INSTANCE_CUSTOM.y;
	base_altitude = INSTANCE_CUSTOM.w;
	height_local = VERTEX.y;
	local_pos = VERTEX;
	if (species == 1 && part > 0.5) {
		// Bare birch: the crown's mass draws in toward the stem.
		float s = mix(bare_crown_scale, 1.0, lk_foliage);
		vec3 centre = vec3(0.3, 7.2, 0.0);
		VERTEX = centre + (VERTEX - centre) * s;
	}
	normal_world = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

// Smooth 3D value noise in the tree's own space (stable as the camera moves).
float twig_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x), mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x), mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void fragment() {
	vec3 col;
	if (species == 1 && part > 0.5) {
		// Bare and thinning birch crowns turn lacy: the twig mass keeps only
		// part of its surface (alpha scissor with alpha to coverage).
		float cover = mix(bare_twig_cover, 1.0, lk_foliage);
		float lace = twig_noise(local_pos * 2.6 + variation * 13.0) * 0.7 + twig_noise(local_pos * 7.0) * 0.3;
		ALPHA = smoothstep(1.0 - cover - 0.06, 1.0 - cover + 0.06, lace);
		ALPHA_SCISSOR_THRESHOLD = 0.5;
	}
	float shade = mix(0.5, 1.0, occlusion);
	if (part < 0.5) {
		if (species == 1) {
			// White bark with dark lenticel bands and a black base.
			float bands = step(0.82, fract(height_local * 3.1 + variation * 7.0));
			col = mix(birch_bark, vec3(0.08, 0.07, 0.07), max(bands * 0.6, 1.0 - smoothstep(0.0, 1.2, height_local)));
		} else {
			col = spruce_bark;
		}
	} else if (species == 1) {
		vec3 summer = mix(birch_shade, birch_lit, occlusion) * mix(0.88, 1.12, tint);
		col = birch_season_colour(summer, tint);
	} else {
		col = mix(spruce_shade, spruce_lit, occlusion) * mix(0.85, 1.12, tint);
	}
	col *= shade;
	// Snow on the up-facing foliage and branches of trees above the line.
	float snowy = ground_snow(base_altitude + 2.0, 0.0, 0.0, 0.08, variation);
	// Snow lies on the tops of the branch tiers and the crown, patchily.
	float facing = smoothstep(0.5, 0.95, normal_world.y) * mix(0.45, 1.0, fract(height_local * 1.7 + variation * 5.0));
	float hold = species == 0 ? 0.6 : mix(0.55, 0.25, lk_foliage);
	float snow = snowy * facing * hold * (part > 0.5 ? 1.0 : 0.35);
	col = mix(col, snow_color * mix(0.85, 1.0, occlusion), snow);
	// Cloud shadows (G3-05), as on the terrain under the trees.
	col *= lk_cloud_shadow_factor(world_pos, normal_world);
	ALBEDO = col;
	ROUGHNESS = mix(0.88, 0.7, snow);
	SPECULAR = mix(0.25, 0.45, snow);
	// Thin birch leaves let the light through.
	if (species == 1 && part > 0.5) {
		BACKLIGHT = col * 0.2 * lk_foliage * (1.0 - snow);
	}
}
