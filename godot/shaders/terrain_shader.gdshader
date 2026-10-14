shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

// === Fjord terrain material (G2-02, "Vestland Light", roadmap D13) ===
//
// Seven PBR layers in two Texture2DArrays (tools/gen_terrain_textures.py):
// rock, grass, heath, snow, shingle, spruce canopy and birch canopy, each with
// albedo + height and normal + roughness + AO, drawn for a camera 25-3,000 m
// away (one slice covers NEAR_TILE_M of ground).
//
// Sampling: biplanar. Of the three world-axis projections, only the two the
// surface faces most are fetched; their blend weights have local support, so
// a texel never smears along a steep wall. Each projection's tangent-space
// normal is turned into world space with a whiteout blend against the
// geometric normal (Ben Golus, "Normal Mapping for a Triplanar Shader",
// 2017; implemented here from the published method). Every fetch uses
// textureGrad with the projection's own derivatives, so mip selection never
// sees the jump where the chosen pair of axes changes. UVs are never
// interpolated between projections: samples are.
//
// Two sample scales (near NEAR_TILE_M, far 5.37x larger and turned 37 deg)
// plus procedural macro variation keep the 20 m slice from repeating; the
// near scale fades out with distance, where it would only alias.
//
// Which layer goes where: steepness, altitude and the cavity term (vertex
// colour r) decide bare rock; the vegetation masks baked in the vertex colour
// (g wood density, b birch share, a meadow: VegetationScatter, the same masks
// the trees stand in) decide the woods and meadows; heath above the treeline,
// shingle at the shore. Neighbouring layers blend by their texture heights
// (soil gathers in the joints of the rock first, snow fills the hollows
// first). The season (snow line, birch leaves, wet ground) comes from the
// EnvironmentDirector through ground_season.gdshaderinc.
//
// Shore: a wet band from the waterline to wet_band_m above it (tide and
// splash), with olive wrack below and the black lichen of the splash zone
// above; vertical seepage streaks and foliation bands on the cliffs.

#include "res://shaders/ground_season.gdshaderinc"

const float L_ROCK = 0.0;
const float L_GRASS = 1.0;
const float L_HEATH = 2.0;
const float L_SNOW = 3.0;
const float L_SHINGLE = 4.0;
const float L_SPRUCE = 5.0;
const float L_BIRCH = 6.0;
const float NEAR_TILE_M = 20.0;
const float FAR_TILE_M = 107.4;
// cos and sin of the far scale's 37 degree turn.
const vec2 FAR_ROTATION = vec2(0.7986355, 0.6018150);
const vec2 FAR_OFFSET = vec2(0.371, 0.613);
const float FAR_ALBEDO_BLUR = 3.0;
const float NEAR_WARP_M = 9.0;
const float NEAR_WARP_SCALE_M = 47.0;
const float FAR_WARP_M = 40.0;
const float FAR_WARP_SCALE_M = 260.0;

uniform sampler2DArray albedo_height : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_rough_ao : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// 0 draws the flat layer palette (textures missing).
uniform float use_textures : hint_range(0.0, 1.0) = 1.0;

uniform float water_level = 0.0;
// Wet band: tide plus splash above the waterline (m).
uniform float wet_band_m : hint_range(0.0, 4.0) = 1.5;
// How much darker the wet band is (albedo multiplier).
uniform float wet_band_albedo : hint_range(0.2, 1.0) = 0.5;
uniform float treeline_m : hint_range(200.0, 900.0) = 500.0;

// Bare rock: below this slope (rise over run) soil holds; above the second it is bare.
uniform float soil_slope_tan : hint_range(0.2, 2.0) = 0.72;
uniform float bare_slope_tan : hint_range(0.3, 3.0) = 1.05;

// Tile scales: near detail fades between these camera distances (m).
uniform float near_fade_start : hint_range(50.0, 2000.0) = 450.0;
uniform float near_fade_end : hint_range(100.0, 4000.0) = 1100.0;
uniform float near_normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float far_normal_strength : hint_range(0.0, 2.0) = 0.8;
// Height-blend depth: smaller gives crisper layer edges.
uniform float blend_depth : hint_range(0.02, 1.0) = 0.18;

// Art-direction tints per layer (multiply the textures' albedo).
uniform vec3 rock_tint : source_color = vec3(0.8);
uniform vec3 vegetation_tint : source_color = vec3(1.0);
uniform vec3 wrack_color : source_color = vec3(0.290, 0.263, 0.157);       // #4A4328
uniform vec3 black_lichen_color : source_color = vec3(0.141, 0.149, 0.157); // #242628

varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 masks;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	masks = COLOR;
}

// ── Procedural macro variation (value noise; no texture) ─────────────────────

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x),
		mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm3(vec2 p) {
	return value_noise(p) * 0.55 + value_noise(p * 2.13 + 7.1) * 0.3 + value_noise(p * 4.37 - 3.3) * 0.15;
}

// ── Biplanar projection ──────────────────────────────────────────────────────

// Tangent (u) and bitangent (v) of the plane that faces along `axis`.
vec3 axis_tangent(int axis) {
	return axis == 0 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
}

vec3 axis_bitangent(int axis) {
	return axis == 1 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
}

vec3 axis_normal(int axis, vec3 n) {
	vec3 a = axis == 0 ? vec3(1.0, 0.0, 0.0) : (axis == 1 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0));
	return a * (dot(a, n) < 0.0 ? -1.0 : 1.0);
}

// A projection: world-metre UVs, their screen derivatives, and the axis.
struct Proj {
	vec2 uv;
	vec2 dx;
	vec2 dy;
	int axis;
	float weight;
	// Smooth, non-periodic offsets (m) for the near and far samples.
	vec2 near_warp;
	vec2 far_warp;
};

Proj make_proj(int axis, float weight, vec3 p, vec3 dpdx, vec3 dpdy) {
	vec3 t = axis_tangent(axis);
	vec3 b = axis_bitangent(axis);
	Proj pr;
	pr.uv = vec2(dot(p, t), dot(p, b));
	pr.dx = vec2(dot(dpdx, t), dot(dpdx, b));
	pr.dy = vec2(dot(dpdy, t), dot(dpdy, b));
	pr.axis = axis;
	pr.weight = weight;
	// A slow domain warp shifts each tile by up to a few metres, differently
	// everywhere, so the slices never line up into a grid (the periodic
	// pattern decorrelates; the content keeps its scale).
	pr.near_warp = (vec2(value_noise(pr.uv / NEAR_WARP_SCALE_M + 3.1), value_noise(pr.uv / NEAR_WARP_SCALE_M + 9.7)) - 0.5) * 2.0 * NEAR_WARP_M;
	pr.far_warp = (vec2(value_noise(pr.uv / FAR_WARP_SCALE_M + 5.3), value_noise(pr.uv / FAR_WARP_SCALE_M + 1.9)) - 0.5) * 2.0 * FAR_WARP_M;
	return pr;
}

vec2 far_uv(vec2 uv) {
	return vec2(uv.x * FAR_ROTATION.x - uv.y * FAR_ROTATION.y, uv.x * FAR_ROTATION.y + uv.y * FAR_ROTATION.x) / FAR_TILE_M + FAR_OFFSET;
}

vec2 far_grad(vec2 d) {
	return vec2(d.x * FAR_ROTATION.x - d.y * FAR_ROTATION.y, d.x * FAR_ROTATION.y + d.y * FAR_ROTATION.x) / FAR_TILE_M;
}

// Share of the far scale in each layer: self-similar rock and heath take a
// lot, canopies and grass (with a real crown or tuft size) little.
float far_share(float layer) {
	return layer == L_ROCK ? 0.55 : (layer == L_HEATH ? 0.45 : (layer == L_SNOW ? 0.4 : 0.18));
}

// Flat palette (the generator's layer means, linear) when textures are missing.
vec3 palette(float layer) {
	if (layer == L_ROCK) return vec3(0.110, 0.122, 0.128);
	if (layer == L_GRASS) return vec3(0.101, 0.162, 0.050);
	if (layer == L_HEATH) return vec3(0.055, 0.069, 0.033);
	if (layer == L_SNOW) return vec3(0.842, 0.874, 0.906);
	if (layer == L_SHINGLE) return vec3(0.229, 0.203, 0.162);
	if (layer == L_SPRUCE) return vec3(0.018, 0.036, 0.021);
	return vec3(0.098, 0.168, 0.043);
}

// One layer through one projection: albedo + height (rgba) and the
// tangent-space normal (xyz) with roughness and AO.
void sample_layer(float layer, Proj pr, float near_w, out vec4 alb, out vec3 tn, out vec2 rough_ao) {
	float fs = far_share(layer);
	vec2 fuv = far_uv(pr.uv + pr.far_warp);
	vec2 fdx = far_grad(pr.dx);
	vec2 fdy = far_grad(pr.dy);
	// The far albedo is read a few mips softer: it carries the large tonal
	// patches; the joints' fine lines would read as scratches at distance.
	vec4 far_a = textureGrad(albedo_height, vec3(fuv, layer), fdx * FAR_ALBEDO_BLUR, fdy * FAR_ALBEDO_BLUR);
	vec4 far_n = textureGrad(normal_rough_ao, vec3(fuv, layer), fdx, fdy);
	vec2 far_xy = (far_n.xy * 2.0 - 1.0) * far_normal_strength;
	if (near_w > 0.001) {
		vec2 nuv = (pr.uv + pr.near_warp) / NEAR_TILE_M;
		vec4 near_a = textureGrad(albedo_height, vec3(nuv, layer), pr.dx / NEAR_TILE_M, pr.dy / NEAR_TILE_M);
		vec4 near_n = textureGrad(normal_rough_ao, vec3(nuv, layer), pr.dx / NEAR_TILE_M, pr.dy / NEAR_TILE_M);
		float w_far = mix(1.0, fs, near_w);
		alb = mix(near_a, far_a, w_far);
		vec2 near_xy = (near_n.xy * 2.0 - 1.0) * near_normal_strength * near_w;
		tn = vec3(near_xy + far_xy * fs, 1.0);
		rough_ao = mix(near_n.zw, far_n.zw, w_far);
	} else {
		alb = far_a;
		tn = vec3(far_xy * fs, 1.0);
		rough_ao = far_n.zw;
	}
	if (use_textures < 0.5) {
		alb = vec4(palette(layer), 0.5);
		tn = vec3(0.0, 0.0, 1.0);
		rough_ao = vec2(0.85, 1.0);
	}
}

// Whiteout blend of a projection's tangent normal with the geometric normal,
// returned in world space.
vec3 whiteout(vec3 tn, vec3 g, int axis) {
	vec3 t = axis_tangent(axis);
	vec3 b = axis_bitangent(axis);
	vec3 n = axis_normal(axis, g);
	vec3 local_g = vec3(dot(g, t), dot(g, b), dot(g, n));
	vec3 blended = vec3(tn.xy + local_g.xy, tn.z * local_g.z);
	return blended.x * t + blended.y * b + blended.z * n;
}

// ── Layer choice ─────────────────────────────────────────────────────────────

// The two strongest soil layers and their weights (they sum to 1).
void pick_soils(float shingle, float grass, float heath, float spruce, float birch,
		out float layer_a, out float w_a, out float layer_b, out float w_b) {
	float w[5] = float[5](shingle, grass, heath, spruce, birch);
	float ids[5] = float[5](L_SHINGLE, L_GRASS, L_HEATH, L_SPRUCE, L_BIRCH);
	int ia = 0;
	for (int i = 1; i < 5; i++) {
		if (w[i] > w[ia]) ia = i;
	}
	int ib = ia == 0 ? 1 : 0;
	for (int i = 0; i < 5; i++) {
		if (i != ia && w[i] > w[ib]) ib = i;
	}
	float total = max(w[ia] + w[ib], 1e-4);
	layer_a = ids[ia];
	w_a = w[ia] / total;
	layer_b = ids[ib];
	w_b = w[ib] / total;
}

// Season and art tint of a soil layer's albedo.
vec3 soil_colour(float layer, vec3 albedo, float tone) {
	if (layer == L_BIRCH) {
		return birch_season_colour(albedo, tone) * vegetation_tint;
	}
	if (layer == L_GRASS) {
		// Straw before the spring growth and after the first frosts.
		vec3 straw = albedo * vec3(1.45, 1.15, 0.85) * 0.9;
		return mix(albedo, straw, clamp((1.0 - lk_foliage) * 0.75 + lk_autumn * 0.35, 0.0, 1.0)) * vegetation_tint;
	}
	if (layer == L_HEATH) {
		// Bilberry and crowberry redden in autumn.
		vec3 red = albedo * vec3(1.6, 0.85, 0.7);
		return mix(albedo, red, lk_autumn * 0.45) * vegetation_tint;
	}
	if (layer == L_SPRUCE) {
		return albedo * vegetation_tint;
	}
	return albedo;
}

void fragment() {
	vec3 p = world_pos;
	vec3 g = normalize(world_normal);
	vec3 dpdx = dFdx(p);
	vec3 dpdy = dFdy(p);
	float h = p.y - water_level;
	float slope_tan = length(g.xz) / max(g.y, 0.05);
	float cavity = masks.r;
	float hollow = cavity - 0.5;
	float forest = masks.g;
	float birch_share = masks.b;
	float meadow = masks.a;
	float dist = length(p - CAMERA_POSITION_WORLD);
	float near_w = 1.0 - smoothstep(near_fade_start, near_fade_end, dist);

	float macro = fbm3(p.xz / 340.0);
	float mottle = fbm3(p.xz / 85.0 + 11.0);
	vec3 aw = abs(g);
	// Share of the Z-facing plane among the vertical ones: horizontal noise on
	// walls is read along X there and along Z on X-facing walls.
	float wz = aw.z * aw.z / max(aw.x * aw.x + aw.z * aw.z, 1e-4);

	// Cliff bands: on steep walls the rock steps in bands 15-40 m tall
	// (sheeting joints and foliation, dipping a few degrees). Some band tops
	// are ledges that face the sky and hold snow, moss and scrub; the face
	// just under a ledge lies in shadow. A ledge runs for a stretch and
	// breaks off; its width varies.
	float cliff = smoothstep(0.9, 1.7, slope_tan);
	float band_t = (p.y + dot(p.xz, vec2(0.09, -0.05))) / 24.0 + fbm3(p.xz / 300.0) * 2.2 + fbm3(p.xz / 90.0 + 2.0) * 1.1;
	float band_f = fract(band_t);
	float band_id = floor(band_t);
	float band_tone = hash12(vec2(band_id, 7.3));
	float is_ledge = step(0.6, hash12(vec2(band_id, 2.9)));
	float ledge_w = mix(0.07, 0.24, hash12(vec2(band_id, 5.1)));
	float run = mix(value_noise(vec2(band_id * 3.1, p.z / 70.0)), value_noise(vec2(band_id * 3.1 + 11.0, p.x / 70.0)), wz);
	run = smoothstep(0.5, 0.68, run) * is_ledge * cliff;
	// The ledge's lip wanders: its width breaks up every few metres.
	float lip = mix(value_noise(vec2(p.z / 6.0, band_id)), value_noise(vec2(p.x / 6.0, band_id + 3.0)), wz);
	float w_here = ledge_w * mix(0.35, 1.25, lip);
	float ledge = smoothstep(1.0 - w_here, 1.0 - w_here * 0.55, band_f) * (1.0 - smoothstep(0.97, 1.0, band_f)) * run;
	float under = (1.0 - smoothstep(0.0, 0.1, band_f)) * run * 0.7;
	// Gullies cut down the walls: snow and soil gather in them too.
	float gully = smoothstep(0.06, 0.25, hollow) * cliff;
	// Ledges and gullies are flat or sheltered enough to hold snow and soil.
	float slope_eff = mix(mix(slope_tan, 0.35, ledge), 0.7, gully * 0.8);

	// ── Masks ──
	// Bare rock where it is steep (gullies and ledges hold soil a little
	// steeper), on the high plateau's outcrops, and on the shore below the
	// shingle's reach. Woods override it: birch clings to the wall ledges.
	float soil_limit = soil_slope_tan + hollow * 0.9 + (mottle - 0.5) * 0.25;
	float bare = smoothstep(soil_limit, soil_limit + (bare_slope_tan - soil_slope_tan), slope_tan);
	// Shingle only lies where the shore is gentle; a cliff drops straight in.
	float gentle_shore = 1.0 - smoothstep(0.5, 1.0, slope_tan);
	// Glacier-scoured outcrops: ridges and knolls of the upland stay bare.
	float upland = smoothstep(treeline_m - 80.0, treeline_m + 250.0, h);
	float outcrop = smoothstep(0.42, 0.62, macro * 0.6 + mottle * 0.4 - hollow * 1.2);
	bare = max(bare, upland * outcrop * 0.9);
	bare *= 1.0 - smoothstep(0.05, 0.45, forest) * 0.9;
	bare = max(bare, 1.0 - smoothstep(0.6, 1.4, h));

	// Soils: shingle at the shore, meadow and woods from the masks, heath
	// and grass on open ground (heath above the treeline).
	float shore = (1.0 - smoothstep(2.0, 3.5, h)) * gentle_shore;
	float wood = clamp(forest * 1.6, 0.0, 1.0) * (1.0 - shore);
	float open_ground = (1.0 - wood) * (1.0 - shore);
	float alpine = smoothstep(treeline_m - 120.0, treeline_m + 40.0, h + (macro - 0.5) * 80.0);
	float grassy = clamp(meadow * 1.5 + (1.0 - alpine) * smoothstep(0.55, 0.85, mottle) * 0.6, 0.0, 1.0);
	float w_shingle = shore;
	float w_grass = open_ground * grassy;
	float w_heath = open_ground * (1.0 - grassy);
	float w_spruce = wood * (1.0 - birch_share);
	float w_birch = wood * birch_share;
	float soil_a;
	float soil_wa;
	float soil_b;
	float soil_wb;
	pick_soils(w_shingle, w_grass, w_heath, w_spruce, w_birch, soil_a, soil_wa, soil_b, soil_wb);

	float north = clamp(g.x * 2.0, 0.0, 1.0);
	float snow_amount = ground_snow(h, slope_eff, north, hollow, macro);

	// ── Biplanar projections ──
	int major = aw.x > aw.y && aw.x > aw.z ? 0 : (aw.y > aw.z ? 1 : 2);
	int minor = aw.x < aw.y && aw.x < aw.z ? 0 : (aw.y < aw.z ? 1 : 2);
	int median = 3 - major - minor;
	vec3 sharp = clamp((aw - 0.5773) / (1.0 - 0.5773), 0.0, 1.0);
	float w_major = pow(sharp[major], 0.5);
	float w_median = pow(sharp[median], 0.5);
	float w_sum = max(w_major + w_median, 1e-4);
	Proj pa = make_proj(major, w_major / w_sum, p, dpdx, dpdy);
	Proj pb = make_proj(median, w_median / w_sum, p, dpdx, dpdy);
	bool use_b = pb.weight > 0.02;

	// ── Fetch the active layers through each projection ──
	vec4 alb_r = vec4(0.0); vec3 tn_r_a = vec3(0.0, 0.0, 1.0); vec3 tn_r_b = vec3(0.0, 0.0, 1.0); vec2 ra_r = vec2(0.85, 1.0);
	vec4 alb_a = vec4(0.0); vec3 tn_a_a = vec3(0.0, 0.0, 1.0); vec3 tn_a_b = vec3(0.0, 0.0, 1.0); vec2 ra_a = vec2(0.9, 1.0);
	vec4 alb_b = vec4(0.0); vec3 tn_b_a = vec3(0.0, 0.0, 1.0); vec3 tn_b_b = vec3(0.0, 0.0, 1.0); vec2 ra_b = vec2(0.9, 1.0);
	vec4 alb_s = vec4(0.0); vec3 tn_s_a = vec3(0.0, 0.0, 1.0); vec3 tn_s_b = vec3(0.0, 0.0, 1.0); vec2 ra_s = vec2(0.65, 1.0);
	vec4 a1; vec4 a2; vec3 n1; vec3 n2; vec2 r1; vec2 r2;
	if (bare > 0.005) {
		sample_layer(L_ROCK, pa, near_w, a1, n1, r1);
		alb_r = a1; tn_r_a = n1; ra_r = r1;
		if (use_b) {
			sample_layer(L_ROCK, pb, near_w, a2, n2, r2);
			alb_r = mix(a1, a2, pb.weight); tn_r_b = n2; ra_r = mix(r1, r2, pb.weight);
		}
	}
	if (bare < 0.995) {
		sample_layer(soil_a, pa, near_w, a1, n1, r1);
		alb_a = a1; tn_a_a = n1; ra_a = r1;
		if (use_b) {
			sample_layer(soil_a, pb, near_w, a2, n2, r2);
			alb_a = mix(a1, a2, pb.weight); tn_a_b = n2; ra_a = mix(r1, r2, pb.weight);
		}
		if (soil_wb > 0.01) {
			sample_layer(soil_b, pa, near_w, a1, n1, r1);
			alb_b = a1; tn_b_a = n1; ra_b = r1;
			if (use_b) {
				sample_layer(soil_b, pb, near_w, a2, n2, r2);
				alb_b = mix(a1, a2, pb.weight); tn_b_b = n2; ra_b = mix(r1, r2, pb.weight);
			}
		}
	}
	if (snow_amount > 0.005) {
		sample_layer(L_SNOW, pa, near_w, a1, n1, r1);
		alb_s = a1; tn_s_a = n1; ra_s = r1;
		if (use_b) {
			sample_layer(L_SNOW, pb, near_w, a2, n2, r2);
			alb_s = mix(a1, a2, pb.weight); tn_s_b = n2; ra_s = mix(r1, r2, pb.weight);
		}
	}

	// ── Height-blended weights ──
	// Soil fills the rock's joints first; the stronger soil wins on its highs.
	float wr = bare;
	float wa = (1.0 - bare) * soil_wa;
	float wb = (1.0 - bare) * soil_wb;
	float hr = alb_r.a + wr;
	float ha = alb_a.a + wa;
	float hb = alb_b.a + wb;
	float top = max(max(hr, ha), hb) - blend_depth;
	float br = max(hr - top, 0.0) * step(0.005, wr);
	float ba = max(ha - top, 0.0) * step(0.005, wa);
	float bb = max(hb - top, 0.0) * step(0.005, wb);
	float bsum = max(br + ba + bb, 1e-4);
	br /= bsum;
	ba /= bsum;
	bb /= bsum;
	float ground_h = alb_r.a * br + alb_a.a * ba + alb_b.a * bb;
	// Snow fills the hollows of the ground first: rock tops, boulders and
	// crowns break through a thin cover.
	// On the walls it lies thin: a soft dusting the rock shows through, full
	// only on the ledges and in the gullies.
	float snow_soft = mix(0.12, 0.3, cliff);
	float snow = snow_amount > 0.005 ? smoothstep(ground_h - snow_soft, ground_h + snow_soft, snow_amount * 1.35 - 0.2) : 0.0;
	snow *= mix(1.0, 0.82, cliff * (1.0 - max(ledge, gully)));
	br *= 1.0 - snow;
	ba *= 1.0 - snow;
	bb *= 1.0 - snow;

	// ── Colour ──
	float tone = hash12(floor(p.xz / 9.0));
	vec3 rock = alb_r.rgb * rock_tint;
	// Rock: macro weathering, foliation bands and seepage streaks on cliffs.
	float steep = smoothstep(0.8, 1.8, slope_tan);
	rock *= mix(0.84, 1.14, macro) * mix(vec3(0.97, 1.0, 1.03), vec3(1.04, 1.0, 0.95), mottle);
	// Strata: each cliff band its own tone; shadow under the ledge above.
	rock *= mix(1.0, mix(0.86, 1.1, band_tone), cliff * 0.85);
	rock *= 1.0 - 0.3 * under;
	// Lichen-dark and freshly broken pale faces, tens of metres across, read
	// on the wall itself (vertical planes) rather than from above.
	float wall_tone = mix(fbm3(vec2(p.z, p.y) / 130.0 + 3.0), fbm3(vec2(p.x, p.y) / 130.0 + 9.0), wz);
	float flat_tone = fbm3(p.xz / 130.0 + 5.0);
	float tone_field = mix(flat_tone, wall_tone, cliff);
	rock *= mix(0.62, 1.12, smoothstep(0.22, 0.78, tone_field));
	// Gullies on the walls stay damp and dark.
	rock *= 1.0 - 0.28 * smoothstep(0.04, 0.22, hollow) * cliff;
	// Above the snow line bare rock is damp and frosted: darker.
	rock *= 1.0 - 0.15 * (1.0 - smoothstep(0.0, 400.0, lk_snow_line - h));
	// Streaks run down the face: noise stretched along Y, read in the two
	// vertical planes and blended like the textures (a coordinate taken
	// along the face's own tangent would swing with the normal and draw
	// contour lines).
	float streak_x = value_noise(vec2(p.z / 9.0, p.y / 140.0)) * 0.6 + value_noise(vec2(p.z / 3.5, p.y / 45.0)) * 0.4;
	float streak_z = value_noise(vec2(p.x / 9.0 + 17.0, p.y / 140.0)) * 0.6 + value_noise(vec2(p.x / 3.5 + 5.0, p.y / 45.0)) * 0.4;
	float streak = mix(streak_x, streak_z, wz);
	float seep = smoothstep(0.5, 0.78, streak) * steep;
	rock *= 1.0 - 0.45 * seep;
	vec3 soil_col_a = soil_colour(soil_a, alb_a.rgb, tone);
	vec3 soil_col_b = soil_colour(soil_b, alb_b.rgb, tone);
	// Moss, grass and birch scrub on the ledges below the treeline.
	float ledge_green = max(ledge, smoothstep(0.08, 0.3, hollow) * cliff * 0.6) * (1.0 - smoothstep(treeline_m - 100.0, treeline_m + 60.0, h))
		* smoothstep(0.3, 0.7, mottle + 0.2);
	vec3 moss = mix(vec3(0.040, 0.062, 0.022), vec3(0.085, 0.080, 0.032), lk_autumn) * mix(0.75, 1.0, lk_foliage);
	rock = mix(rock, moss, ledge_green * 0.85);
	vec3 col = rock * br + soil_col_a * ba + soil_col_b * bb;
	// Vegetation varies stand by stand.
	col *= mix(1.0, mix(0.86, 1.12, mottle), (ba + bb) * 0.8);
	col = mix(col, alb_s.rgb, snow);

	// Shore zonation: wrack and wet rock below the band, black lichen above.
	float wet_top = water_level + wet_band_m * mix(0.8, 1.6, steep);
	float wet = (1.0 - smoothstep(wet_top - 0.25, wet_top + 0.15, p.y)) * step(water_level - 3.0, p.y);
	float wrack = (1.0 - smoothstep(water_level + 0.5, water_level + 0.9, p.y)) * smoothstep(0.35, 0.6, mottle) * wet;
	float lichen_zone = smoothstep(wet_top, wet_top + 0.3, p.y) * (1.0 - smoothstep(wet_top + 1.2, wet_top + 2.6, p.y))
		* br * smoothstep(0.3, 0.7, streak + 0.2);
	col = mix(col, black_lichen_color * mix(0.9, 1.3, mottle), lichen_zone * 0.55);
	col = mix(col * wet_band_albedo, wrack_color * 0.7, wrack * 0.65) * wet + col * (1.0 - wet);

	// Rain: rock and soil darken and gloss; snow stays.
	float wetness = lk_ground_wetness * (1.0 - snow);
	col *= 1.0 - wetness * mix(0.2, 0.45, br);
	float seep_wet = seep * (0.25 + 0.75 * lk_ground_wetness);

	// ── Normal ──
	vec3 tn_a = tn_r_a * br + tn_a_a * ba + tn_b_a * bb + tn_s_a * snow;
	vec3 tn_b = tn_r_b * br + tn_a_b * ba + tn_b_b * bb + tn_s_b * snow;
	vec3 n_world = whiteout(normalize(tn_a), g, pa.axis) * pa.weight;
	if (use_b) {
		n_world += whiteout(normalize(tn_b), g, pb.axis) * pb.weight;
	}
	n_world = normalize(n_world + vec3(0.0, 1.3 * ledge - 0.35 * under, 0.0) * (1.0 - snow * 0.5));
	if (cliff > 0.01) {
		// Buttresses and shallow grooves, 20-60 m across: a bump from wall
		// noise in the two vertical planes (finite differences, 1.5 m).
		float e = 1.5;
		float bx = fbm3(vec2(p.z, p.y * 0.6) / 38.0);
		float bx_t = fbm3(vec2(p.z + e, p.y * 0.6) / 38.0);
		float bx_b = fbm3(vec2(p.z, (p.y + e) * 0.6) / 38.0);
		float bz = fbm3(vec2(p.x, p.y * 0.6) / 38.0 + 4.0);
		float bz_t = fbm3(vec2(p.x + e, p.y * 0.6) / 38.0 + 4.0);
		float bz_b = fbm3(vec2(p.x, (p.y + e) * 0.6) / 38.0 + 4.0);
		float k = 9.0 * cliff;
		vec3 bump_x = -k * ((bx_t - bx) / e * vec3(0.0, 0.0, 1.0) + (bx_b - bx) / e * vec3(0.0, 1.0, 0.0));
		vec3 bump_z = -k * ((bz_t - bz) / e * vec3(1.0, 0.0, 0.0) + (bz_b - bz) / e * vec3(0.0, 1.0, 0.0));
		vec3 bump = mix(bump_x, bump_z, wz);
		n_world = normalize(n_world + bump - g * dot(bump, g));
	}
	NORMAL = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);

	vec2 ra = ra_r * br + ra_a * ba + ra_b * bb + ra_s * snow;
	float rough = ra.x;
	rough = mix(rough, 0.32, max(wet * 0.8, max(wetness * br * 0.7, seep_wet * 0.6)));
	ALBEDO = clamp(col, vec3(0.0), vec3(1.0));
	ROUGHNESS = clamp(rough, 0.05, 1.0);
	METALLIC = 0.0;
	// Matte landscape (D13): vegetation reflects little; wet rock and snow more.
	float veg = clamp(ba + bb, 0.0, 1.0) * (1.0 - wet);
	SPECULAR = mix(mix(0.42, 0.22, veg), 0.5, max(wet, snow * 0.6));
	AO = clamp(ra.y * mix(1.05, 0.72, smoothstep(0.55, 0.95, cavity)), 0.0, 1.0);
	AO_LIGHT_AFFECT = 0.35;
}
