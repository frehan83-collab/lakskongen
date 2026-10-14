shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

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

// 0 spruce, 1 birch.
uniform int species : hint_range(0, 1) = 0;
uniform vec3 spruce_shade : source_color = vec3(0.090, 0.149, 0.110);   // #172619 inside
uniform vec3 spruce_lit : source_color = vec3(0.196, 0.290, 0.212);     // #324A36 outer tips
uniform vec3 birch_shade : source_color = vec3(0.184, 0.271, 0.141);    // #2F4524
uniform vec3 birch_lit : source_color = vec3(0.333, 0.447, 0.212);      // #557236
uniform vec3 spruce_bark : source_color = vec3(0.290, 0.247, 0.208);    // #4A3F35
uniform vec3 birch_bark : source_color = vec3(0.890, 0.878, 0.839);     // #E3E0D6
uniform vec3 snow_color : source_color = vec3(0.949, 0.961, 0.973);
// Birch crowns shrink toward their twigs when bare (fraction of full size).
uniform float bare_crown_scale : hint_range(0.4, 1.0) = 0.78;

varying float part;
varying float occlusion;
varying float tint;
varying float variation;
varying float base_altitude;
varying vec3 normal_world;
varying float height_local;

void vertex() {
	part = UV.x;
	occlusion = UV.y;
	tint = INSTANCE_CUSTOM.x;
	variation = INSTANCE_CUSTOM.y;
	base_altitude = INSTANCE_CUSTOM.w;
	height_local = VERTEX.y;
	if (species == 1 && part > 0.5) {
		// Bare birch: the crown's mass draws in toward the stem.
		float s = mix(bare_crown_scale, 1.0, lk_foliage);
		vec3 centre = vec3(0.3, 7.3, 0.0);
		VERTEX = centre + (VERTEX - centre) * s;
	}
	normal_world = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 col;
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
	float facing = smoothstep(0.4, 0.85, normal_world.y) * mix(0.55, 1.0, fract(height_local * 1.7 + variation * 5.0));
	float hold = species == 0 ? 0.75 : mix(0.55, 0.25, lk_foliage);
	float snow = snowy * facing * hold * (part > 0.5 ? 1.0 : 0.35);
	col = mix(col, snow_color * mix(0.85, 1.0, occlusion), snow);
	ALBEDO = col;
	ROUGHNESS = mix(0.88, 0.7, snow);
	SPECULAR = mix(0.25, 0.45, snow);
	// Thin birch leaves let the light through.
	if (species == 1 && part > 0.5) {
		BACKLIGHT = col * 0.2 * lk_foliage * (1.0 - snow);
	}
}
