shader_type spatial;
render_mode cull_disabled, depth_draw_always, alpha_to_coverage_and_one, diffuse_burley, specular_schlick_ggx;

// Pen net and bird net (A2-01, G1-06): a tiling knotless-mesh texture (alpha
// = thread coverage, blender/build_pen.py --textures) drawn with
// alpha-to-coverage. MSAA resolves each pixel's thread coverage instead of a
// blend, and alpha_to_coverage_and_one writes alpha 1 after the coverage
// mask: nothing blends, the net sorts like an opaque surface. Once one pixel
// spans more of a tile than FADE_FWIDTH, the net fades to its mean coverage
// (the texture's smallest mip, which averages every thread), so a distant or
// orbiting pen shows a quiet veil instead of moire; the threads resolve up
// close.
//
// UV is in metres (u round the pen, v down the net) on the Blender meshes.
//
// Under the surface (G3-04, include/underwater_light.gdshaderinc) the net
// takes the light of its depth and the caustics' moving network, and its
// growth reads the site's net condition (net_fouling, an instance uniform:
// the sim's NetIntegrityPct, which the ROV's cleaning restores): a fouled
// net is olive, its threads ragged and thick with growth further down, the
// mesh closing up (see FOULED_*).
#include "res://shaders/include/underwater_light.gdshaderinc"

uniform vec3 albedo_color : source_color = vec3(0.10, 0.13, 0.15);
uniform sampler2D net_texture : hint_default_white, source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres of net per texture tile (the tile holds 2 x 2 diamonds).
uniform float tile_m = 0.25;
uniform float thread_alpha : hint_range(0.0, 1.0) = 0.9;
// The far veil reads a little denser than the raw mean coverage, the way a
// real net darkens the water behind it (and so it keeps at least one MSAA
// sample of four).
uniform float far_boost : hint_range(1.0, 3.0) = 1.6;
// Marine growth near the surface: greener, denser threads in the top metres.
uniform vec3 fouling_color : source_color = vec3(0.16, 0.20, 0.13);
uniform float fouling_depth = 4.0;
uniform float fouling_amount : hint_range(0.0, 1.0) = 0.45;
// Per-pen visibility (rival pens are drawn quieter, roadmap G1-07).
instance uniform float net_opacity = 1.0;
// The site's net fouling, 0 clean .. 1 heavily grown (FarmSite.set_net_fouling,
// from the site's net condition): the growth reaches down the net and
// thickens, past the clean net's band at the surface.
instance uniform float net_fouling = 0.0;
// How far a fully fouled net's growth reaches (m) and its colour (hydroid
// colonies and algae: pale olive-tan, lighter than the coated net).
uniform float fouled_depth = 18.0;
uniform vec3 fouled_color : source_color = vec3(0.34, 0.31, 0.20);

// Tiles per pixel past which the pattern gives way to its mean.
const float FADE_FWIDTH = 0.35;
const float FADE_FROM = 0.15;
// Past the texture's last mip level: the 1 x 1 average of the tile.
const float MEAN_LOD = 16.0;
// The growth on a fouled net (G3-04): hydroids (Ectopleura larynx dominates
// Norwegian pen nets July-November) and algae on the threads. The tile's
// coverage blurred over FOULED_SPREAD_LOD mips reaches ~1 cm past a thread;
// its reach is scaled 0-2x by clumps (three octaves of value noise
// FOULED_TUFT_PER_M across, weighted FOULED_TUFT_WEIGHT, stretched by
// FOULED_TUFT_STRETCH) and cut at a threshold that falls from
// FOULED_THRESHOLD.x to .y as the fouling grows (FOULED_CURVE), and the
// growth covers FOULED_DENSITY of what it reaches: ragged tufts first, then
// the mesh closes. A fully fouled net covers FOULED_COVER_GAIN times the
// clean tile's 0.15 (0.35, measured on the tile: RUN_LOG section 7), as
// Gansel et al. 2015 (PLoS One 10(7) e0131051) measured a 0.20 net reach
// 0.50 in ten weeks. Past the spread's mip the growth gives way to that mean
// (FOULED_COVER_RAMP of the fouling), as the threads give way to theirs.
const float FOULED_SPREAD_LOD = 3.0;
const vec2 FOULED_THRESHOLD = vec2(0.85, 0.02);
const float FOULED_CURVE = 2.0;
const float FOULED_EDGE = 0.10;
const vec3 FOULED_TUFT_PER_M = vec3(18.0, 70.0, 240.0);
const vec3 FOULED_TUFT_WEIGHT = vec3(0.5, 0.3, 0.2);
const vec2 FOULED_TUFT_STRETCH = vec2(0.25, 0.75);
const float FOULED_DENSITY = 0.92;
const float FOULED_COVER_GAIN = 2.35;
const vec2 FOULED_COVER_RAMP = vec2(0.1, 0.95);
// Under the surface the caustics' moving light shows on a thread's
// sun-facing top at this albedo at least (linear; wave4b int, the captain's
// art call over the review's finding: at the coated net's 1.6 % albedo the
// clean net's shimmer was about one level of 255, p99 4). The net's mean
// light stays its own albedo's (the caustics add sunlight x (i - 1), whose
// mean is 0): only the moving network reads brighter, as on a wet cord's
// lit top. Above the surface the caustics are off and nothing changes.
const vec3 UW_THREAD_CAUSTIC_ALBEDO = vec3(0.05);

varying float model_y;

void vertex() {
	model_y = VERTEX.y;
}

// Fraction of the net the threads cover: the tile's average alpha.
float mean_coverage() {
	return textureLod(net_texture, vec2(0.5), MEAN_LOD).a;
}

float tuft_hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

// Smooth value noise (0-1), one value a cell.
float tuft_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(tuft_hash(i), tuft_hash(i + vec2(1.0, 0.0)), f.x),
		mix(tuft_hash(i + vec2(0.0, 1.0)), tuft_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// One octave of the clumps: its noise, or its mean once its cells shrink
// under two pixels (no shimmer).
float tuft_octave(vec2 uv_m, float per_m, float offset, float m_per_px) {
	float n = tuft_noise(uv_m * per_m + vec2(offset));
	return mix(n, 0.5, smoothstep(0.25, 0.5, m_per_px * per_m));
}

// How strongly the growth clumps here (0-1).
float tuft_clumps(vec2 uv_m, float m_per_px) {
	float t = FOULED_TUFT_WEIGHT.x * tuft_octave(uv_m, FOULED_TUFT_PER_M.x, 0.0, m_per_px)
		+ FOULED_TUFT_WEIGHT.y * tuft_octave(uv_m, FOULED_TUFT_PER_M.y, 17.0, m_per_px)
		+ FOULED_TUFT_WEIGHT.z * tuft_octave(uv_m, FOULED_TUFT_PER_M.z, 34.0, m_per_px);
	return smoothstep(FOULED_TUFT_STRETCH.x, FOULED_TUFT_STRETCH.y, t);
}

// How far a fouling closes the mesh on the whole (0 clean .. 1 full).
float fouled_ramp(float fouled) {
	return clamp((fouled - FOULED_COVER_RAMP.x) / (FOULED_COVER_RAMP.y - FOULED_COVER_RAMP.x), 0.0, 1.0);
}

// The coverage a fouled net's growth adds round the threads near by, and the
// even fill of the holes it gives way to further off (see FOULED_*).
float fouled_growth(vec2 uv, vec2 uv_m, float fouled, float texel_lod, float m_per_px) {
	if (fouled <= 0.0) {
		return 0.0;
	}
	float reach = textureLod(net_texture, uv, FOULED_SPREAD_LOD).a * 2.0 * tuft_clumps(uv_m, m_per_px);
	float th = FOULED_THRESHOLD.y + (FOULED_THRESHOLD.x - FOULED_THRESHOLD.y) * pow(1.0 - fouled, FOULED_CURVE);
	float near = smoothstep(th, th + FOULED_EDGE, reach) * FOULED_DENSITY * smoothstep(0.0, 0.1, fouled);
	float m = mean_coverage();
	float fill = m * (FOULED_COVER_GAIN - 1.0) * fouled_ramp(fouled) / max(1.0 - m, 1e-3);
	return mix(near, fill, smoothstep(FOULED_SPREAD_LOD, FOULED_SPREAD_LOD + 1.5, texel_lod));
}

void fragment() {
	vec2 uv = UV / tile_m;
	// How many texture tiles one pixel spans: 0 up close, >1 far away.
	vec2 fw = fwidth(uv);
	float fade = smoothstep(FADE_FROM, FADE_FWIDTH, max(fw.x, fw.y));
	float foul = fouling_amount * (1.0 - smoothstep(0.0, fouling_depth, -model_y)) * step(model_y, 0.3);
	// The site's fouling: growth further down and thicker, patchy round the
	// net (0.55-1 of it), under the surface only.
	float patch = 0.775 + 0.225 * sin(UV.x * 0.9 + sin(UV.y * 0.5) * 2.0);
	float fouled = clamp(net_fouling * (1.0 - smoothstep(0.0, fouled_depth * mix(0.3, 1.0, net_fouling), -model_y))
		* patch * step(model_y, 0.0), 0.0, 1.0);
	float texel_lod = log2(max(max(fw.x, fw.y) * float(textureSize(net_texture, 0).x), 1.0));
	float m_per_px = max(fw.x, fw.y) * tile_m;
	float near = max(texture(net_texture, uv).a, fouled_growth(uv, UV, fouled, texel_lod, m_per_px));
	float far = mean_coverage() * (1.0 + (FOULED_COVER_GAIN - 1.0) * fouled_ramp(fouled));
	float cover = mix(near, far, fade);
	cover = min(cover * mix(1.0, far_boost, fade), 1.0);
	vec3 albedo = mix(mix(albedo_color, fouling_color, foul), fouled_color, fouled);
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// The threads are thin cords: their tops catch the light from above
	// whichever way the net's sheet faces. Under water the scene's sun and
	// the caustics light them so; above it the sheet's own normal stays (the
	// pens' look from the surface is unchanged). NORMAL faces the camera on
	// either side here (a cull_disabled material's side check).
	vec3 sheet = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 normal_world = normalize(mix(sheet, vec3(0.0, 1.0, 0.0), 0.7));
	if (lk_uw.x > 0.5) {
		NORMAL = normalize((VIEW_MATRIX * vec4(normal_world, 0.0)).xyz);
	}
	ALBEDO = albedo * uw_depth_light(world.y);
	EMISSION = uw_caustic_light(world, normal_world, max(albedo, UW_THREAD_CAUSTIC_ALBEDO));
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	ALPHA = clamp(cover * thread_alpha * (1.0 + 0.4 * foul) * net_opacity, 0.0, 1.0);
}
