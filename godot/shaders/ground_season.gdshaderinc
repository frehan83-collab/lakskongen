// === Ground season (G2-02) ===
//
// The land's season, shared by the terrain and the trees. EnvironmentDirector
// is the only writer of these globals (RenderingServer.global_shader_parameter_set,
// declared in project.godot [shader_globals]); it composes them every frame
// from the sim calendar and latitude (GroundSeasonModel: snow line, birch
// leaves) and from the weather (fresh snow lowers the line, rain wets the
// ground).

// Altitude (m) above which gentle ground lies under snow; 0 = snow to the shore.
global uniform float lk_snow_line;
// 0 dry to 1 soaked (rain; dries slowly afterwards).
global uniform float lk_ground_wetness;
// Birch leaf cover: 0 bare twigs, 1 full canopy.
global uniform float lk_foliage;
// Birch autumn colour: 0 green, 1 full yellow.
global uniform float lk_autumn;
// Fresh snow 0-1: a snowfall in progress or just over plasters the cliffs.
global uniform float lk_fresh_snow;

// How much snow a point would hold (0-1, before the surface texture decides
// which hollows fill first). h: altitude (m); slope_tan: rise over run;
// north: how much the slope faces north (world +X), 0-1; hollow: cavity
// above 0.5 (gullies and ledges hold snow, ridges shed it); noise: 0-1 macro
// noise that frays the line.
float ground_snow(float h, float slope_tan, float north, float hollow, float noise) {
	float line = lk_snow_line - 90.0 * north - 160.0 * hollow + (noise - 0.5) * 140.0;
	float lying = smoothstep(line - 70.0, line + 50.0, h);
	// Lingering summer patches: north-facing hollows up to 350 m below the line.
	float patch_line = line - 350.0 + 200.0 * (1.0 - north);
	float patches = smoothstep(patch_line, patch_line + 160.0, h) * smoothstep(0.04, 0.2, hollow)
		* smoothstep(0.55, 0.8, noise) * 0.6;
	// Snow slides off rock steeper than about 50 degrees (pass a ledge's
	// slope, not the wall's, where snow can rest); a cliff keeps a dusting
	// in its cracks, which the surface texture turns into speckle.
	// Deep in the winter, far above the line, the cliffs carry more of it.
	float dusting = mix(0.5, 0.66, lk_fresh_snow) + 0.28 * smoothstep(-40.0, 220.0, h - line);
	float sticks = max(1.0 - smoothstep(0.75, 1.3, slope_tan), dusting * (1.0 - smoothstep(1.3, 3.2, slope_tan)));
	// The shore below the wet band is washed by the tide.
	float tide = smoothstep(1.4, 2.4, h);
	return clamp(max(lying, patches) * sticks * tide, 0.0, 1.0);
}

// Birch crown colour (linear) for the season from its summer green. Autumn
// runs from #B8853F to a rustier #8F5A2A, bare twigs from #6C6264 to #7C7372.
vec3 birch_season_colour(vec3 summer, float tone) {
	vec3 autumn = mix(vec3(0.479, 0.235, 0.050), vec3(0.275, 0.102, 0.023), tone);
	vec3 twigs = mix(vec3(0.150, 0.125, 0.130), vec3(0.205, 0.175, 0.170), tone);
	vec3 leaves = mix(summer, autumn, lk_autumn);
	return mix(twigs, leaves, lk_foliage);
}
