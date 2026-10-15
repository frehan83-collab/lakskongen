shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Painted steel for the vessels built by blender/build_vessels.py (roadmap
// A3-01, D13 "Vestland Light": matte painterly PBR, saturated hue only on
// equipment). Every vessel material is this shader with its own colour;
// job state never reaches it (a VesselChip shows it above the boat).
//
// The meshes carry box-projected UVs in metres and baked AO in the vertex
// colour. One shared 4 m weathering tile (vessel_weathering.png: R grime,
// G roughness, B run-down streaks) and its plate-seam normal map age the
// paint; a second, rotated, larger sample breaks up the repeat.
uniform vec3 albedo_color : source_color = vec3(0.8);
uniform float roughness : hint_range(0.0, 1.0) = 0.6;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D weathering : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D plates_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_m = 4.0;
// 0 = factory new. Grime darkens and roughens, streaks run rust-brown.
uniform float grime : hint_range(0.0, 1.0) = 0.6;
uniform float streaks : hint_range(0.0, 1.0) = 0.35;
uniform vec3 streak_color : source_color = vec3(0.30, 0.20, 0.14);
uniform float normal_strength : hint_range(0.0, 1.0) = 0.6;
// Hulls only: black boot-top round the waterline, antifouling below it
// (heights in the model's own space: y = 0 is the waterline).
uniform bool waterline_paint = false;
uniform vec3 boottop_color : source_color = vec3(0.075, 0.08, 0.085);
uniform vec3 antifoul_color : source_color = vec3(0.28, 0.08, 0.07);
uniform float boottop_top = 0.45;
uniform float boottop_bottom = -0.55;
// Baked AO: how much of it shows, and how much it also dims direct light.
uniform float ao_strength : hint_range(0.0, 1.0) = 0.75;
uniform float ao_light_affect : hint_range(0.0, 1.0) = 0.3;
// Glass only: the warm light behind the windows after dark (SiteLights:
// `night` 0 by day .. 1 at night, `exposure_comp` = 1 / the eye's exposure,
// so a lit pane keeps its screen brightness and never blooms). Panes are
// cells of `window_pane_m` along the box-projected UVs (metres); some are
// dark, the rest a little uneven.
uniform vec3 window_light : source_color = vec3(0.0);
uniform float window_pane_m = 1.1;
uniform float window_dark_share : hint_range(0.0, 1.0) = 0.25;
uniform float window_display = 1.1;
uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform float exposure_comp = 1.0;

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

void fragment() {
	vec2 uv = UV / tile_m;
	vec4 w = texture(weathering, uv);
	vec2 uv2 = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * uv * 0.31 + vec2(0.37, 0.11);
	vec4 w2 = texture(weathering, uv2);
	vec3 base = albedo_color;
	if (waterline_paint) {
		float aa = fwidth(model_y) * 1.5 + 0.005;
		float above_boot = smoothstep(boottop_top - aa, boottop_top + aa, model_y);
		float below_boot = 1.0 - smoothstep(boottop_bottom - aa, boottop_bottom + aa, model_y);
		base = mix(boottop_color, base, above_boot);
		base = mix(base, antifoul_color, below_boot);
	}
	float g = mix(1.0, w.r * (0.82 + 0.36 * w2.r), grime);
	float st = clamp(w.b * (0.4 + 1.2 * w2.g), 0.0, 1.0) * streaks;
	ALBEDO = mix(base * g, streak_color * g, st * 0.55);
	ROUGHNESS = clamp(roughness + (w.g - 0.5) * 0.35 * grime + st * 0.15, 0.03, 1.0);
	METALLIC = metallic;
	NORMAL_MAP = mix(vec3(0.5, 0.5, 1.0), texture(plates_normal, uv).rgb, normal_strength);
	AO = mix(1.0, COLOR.r, ao_strength);
	AO_LIGHT_AFFECT = ao_light_affect;
	if (night > 0.0 && dot(window_light, window_light) > 0.0) {
		vec2 pane = floor(UV / window_pane_m);
		float h = fract(sin(dot(pane, vec2(12.9898, 78.233))) * 43758.5453);
		float lit = step(window_dark_share, h) * (0.65 + 0.35 * fract(h * 7.31));
		EMISSION = window_light * lit * window_display * night * exposure_comp;
	}
}
