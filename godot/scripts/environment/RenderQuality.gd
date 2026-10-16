class_name RenderQuality
extends RefCounted
## Quality tiers Low / Medium / High / Ultra (G5-01 groundwork, P0-04).
##
## A tier is a table of knobs; EnvironmentDirector owns one RenderQuality,
## applies the global part here (the root viewport's anti-aliasing and 3D
## scale, the RenderingServer's SSAO, depth-of-field and shadow-filter
## quality, the shadow atlas) and reads the rest while it composes every
## frame (SSAO on or off, depth of field, shadow distance and splits, glow,
## where and how finely the sky marches its clouds). Nothing else writes
## these settings (tests/test_perf_harness.py).
##
## High is the default and the target: p95 <= 16.7 ms at 2560x1440 on an
## M1 Pro at FARM, FJORD and COAST (the P0-04 harness, docs/run/evidence/
## p0_04_perf.md), with no visible loss against Ultra. Ultra keeps every
## effect at its full cost; Medium and Low are for smaller GPUs (Steam Deck
## at 1280x800 on Low or Medium, D3).
##
## The tier is the player's: the settings menu saves it as the graphics
## section's "quality" in user://settings.json; the director reads it at
## start and the menu applies a change at once (DIRECTOR_GROUP).

enum Tier { LOW, MEDIUM, HIGH, ULTRA }

const NAMES: Array[String] = ["low", "medium", "high", "ultra"]
const DEFAULT_TIER := Tier.HIGH
const SETTINGS_PATH := "user://settings.json"
const SETTINGS_SECTION := "graphics"
const SETTINGS_KEY := "quality"
## Every live EnvironmentDirector joins this group.
const DIRECTOR_GROUP := "environment_director"
## The terrain material's quality uniform (see terrain_detail).
const TERRAIN_DETAIL_UNIFORM := "quality_detail"

## Upscalers for a 3D scale below 1 (Viewport.SCALING_3D_MODE_*): MetalFX
## spatial on Apple GPUs, AMD FSR 1 elsewhere. Spatial only: a temporal
## upscaler has no motion vectors for the vertex-animated water and smears
## it.
const UPSCALER_APPLE := Viewport.SCALING_3D_MODE_METALFX_SPATIAL
const UPSCALER_OTHER := Viewport.SCALING_3D_MODE_FSR

## Knobs:
##   msaa            Viewport.MSAA_* (the pen net resolves its threads with
##                   alpha-to-coverage, so MSAA matters there)
##   screen_aa       Viewport.SCREEN_SPACE_AA_* (SMAA on top of MSAA)
##   scale           3D render scale (1 = native)
##   hidden_scale    3D render scale while an opaque panel covers the world
##   hidden_pause    the sky holds still while a panel covers the world
##   upscaler        "auto" (MetalFX spatial on macOS, FSR 1 elsewhere) or
##                   "fsr"; fsr_sharpness for FSR (0 sharpest, 2 softest)
##   ssao            screen-space ambient occlusion on or off
##   ssao_quality    RenderingServer.ENV_SSAO_QUALITY_*
##   ssao_half       SSAO at half resolution
##   ssao_blur       SSAO blur passes
##   dof             depth of field on or off (per zoom, G1-06)
##   dof_quality     RenderingServer.DOF_BLUR_QUALITY_*
##   dof_shape       RenderingServer.DOF_BOKEH_* (box is two separable passes)
##   dof_reach       camera distance (m) by which the blur has faded out (it
##                   fades from 0.6x of it): at FJORD and COAST the haze
##                   already softens the far fjord as much, so the pass is
##                   skipped there
##   dof_near        near blur at FARM distances (the water under a FARM
##                   camera lies beyond it: nothing to blur)
##   shadow_atlas    directional shadow map size
##   shadow_splits   2 or 4 cascades
##   shadow_distance shadow distance as a share of the per-zoom distance
##   soft_shadows    RenderingServer.SHADOW_QUALITY_*
##   glow            HDR glow on or off
##   cloud_pass      0: clouds marched per pixel; 1: in the half-resolution
##                   sky pass; 2: in the quarter-resolution pass
##   cloud_steps     low cloud slab march steps
##   cloud_octaves   low cloud noise octaves
##   cloud_search    how many times the tier's steps the low cloud's first-
##                   cloud search may take on long grazing rays (G3-05: finer
##                   finds thin clouds the coarse grid steps over)
##   cloud_shadows   the clouds' shadows on the land and the water (G3-05)
##   radiance_size   Sky.RADIANCE_SIZE_* of the sky's radiance map
##   terrain_detail  0-1, handed to the terrain material as the uniform
##                   TERRAIN_DETAIL_UNIFORM for the terrain shader to scale
##                   its own detail by (textures, octaves); the shader owns
##                   what it means (the gfx-terrain track)
##   mirror          the planar mirror on the water (G2-03, PlanarReflection);
##                   off, the water reflects the sky's radiance and the
##                   probe lights the scene, as before (Low)
##   mirror_scale    the mirror's resolution as a share of the window
##   mirror_near_hz  its update rate inside the COAST band (0: every frame)
##   mirror_far_hz   its update rate at COAST distances (the roadmap's 30 Hz)
##   mirror_from_m   camera distance from which the mirror is drawn (0:
##                   always). Looking steeply down at a FARM view the water
##                   reflects a few per cent (fresnel) and the sky's
##                   radiance carries it; the mirror's ~1.5 ms there would
##                   break the High frame budget (P0-04, RUN_LOG s7)
const PRESETS := {
	"low": {
		"msaa": Viewport.MSAA_DISABLED, "screen_aa": Viewport.SCREEN_SPACE_AA_FXAA, "scale": 0.6, "hidden_scale": 0.5, "hidden_pause": true,
		"upscaler": "auto", "fsr_sharpness": 0.2,
		"ssao": false, "ssao_quality": RenderingServer.ENV_SSAO_QUALITY_VERY_LOW, "ssao_half": true, "ssao_blur": 1,
		"dof": false, "dof_quality": RenderingServer.DOF_BLUR_QUALITY_VERY_LOW, "dof_shape": RenderingServer.DOF_BOKEH_BOX,
		"dof_reach": 0.0, "dof_near": false,
		"shadow_atlas": 2048, "shadow_splits": 2, "shadow_distance": 0.6, "soft_shadows": RenderingServer.SHADOW_QUALITY_HARD,
		"glow": true, "cloud_pass": 2, "cloud_steps": 4, "cloud_octaves": 4, "radiance_size": Sky.RADIANCE_SIZE_64,
		"cloud_search": 1, "cloud_shadows": false,
		"terrain_detail": 0.0,
		"mirror": false, "mirror_scale": 0.0, "mirror_near_hz": 0.0, "mirror_far_hz": 0.0, "mirror_from_m": 0.0,
	},
	"medium": {
		"msaa": Viewport.MSAA_2X, "screen_aa": Viewport.SCREEN_SPACE_AA_SMAA, "scale": 0.67, "hidden_scale": 0.5, "hidden_pause": true,
		"upscaler": "auto", "fsr_sharpness": 0.2,
		"ssao": false, "ssao_quality": RenderingServer.ENV_SSAO_QUALITY_VERY_LOW, "ssao_half": true, "ssao_blur": 1,
		"dof": true, "dof_quality": RenderingServer.DOF_BLUR_QUALITY_VERY_LOW, "dof_shape": RenderingServer.DOF_BOKEH_BOX,
		"dof_reach": 700.0, "dof_near": false,
		"shadow_atlas": 4096, "shadow_splits": 2, "shadow_distance": 0.8, "soft_shadows": RenderingServer.SHADOW_QUALITY_SOFT_VERY_LOW,
		"glow": true, "cloud_pass": 2, "cloud_steps": 5, "cloud_octaves": 5, "radiance_size": Sky.RADIANCE_SIZE_128,
		"cloud_search": 2, "cloud_shadows": true,
		"terrain_detail": 0.5,
		"mirror": true, "mirror_scale": 0.33, "mirror_near_hz": 30.0, "mirror_far_hz": 20.0, "mirror_from_m": 300.0,
	},
	"high": {
		"msaa": Viewport.MSAA_4X, "screen_aa": Viewport.SCREEN_SPACE_AA_SMAA, "scale": 0.75, "hidden_scale": 0.5, "hidden_pause": true,
		"upscaler": "auto", "fsr_sharpness": 0.2,
		"ssao": false, "ssao_quality": RenderingServer.ENV_SSAO_QUALITY_LOW, "ssao_half": true, "ssao_blur": 2,
		"dof": true, "dof_quality": RenderingServer.DOF_BLUR_QUALITY_VERY_LOW, "dof_shape": RenderingServer.DOF_BOKEH_BOX,
		"dof_reach": 700.0, "dof_near": false,
		"shadow_atlas": 4096, "shadow_splits": 4, "shadow_distance": 1.0, "soft_shadows": RenderingServer.SHADOW_QUALITY_SOFT_VERY_LOW,
		"glow": true, "cloud_pass": 1, "cloud_steps": 6, "cloud_octaves": 6, "radiance_size": Sky.RADIANCE_SIZE_256,
		"cloud_search": 2, "cloud_shadows": true,
		"terrain_detail": 1.0,
		"mirror": true, "mirror_scale": 0.4, "mirror_near_hz": 0.0, "mirror_far_hz": 30.0, "mirror_from_m": 300.0,
	},
	"ultra": {
		"msaa": Viewport.MSAA_4X, "screen_aa": Viewport.SCREEN_SPACE_AA_SMAA, "scale": 1.0, "hidden_scale": 0.5, "hidden_pause": true,
		"upscaler": "auto", "fsr_sharpness": 0.2,
		"ssao": true, "ssao_quality": RenderingServer.ENV_SSAO_QUALITY_MEDIUM, "ssao_half": true, "ssao_blur": 2,
		"dof": true, "dof_quality": RenderingServer.DOF_BLUR_QUALITY_LOW, "dof_shape": RenderingServer.DOF_BOKEH_HEXAGON,
		"dof_reach": 100000.0, "dof_near": true,
		"shadow_atlas": 4096, "shadow_splits": 4, "shadow_distance": 1.0, "soft_shadows": RenderingServer.SHADOW_QUALITY_SOFT_LOW,
		"glow": true, "cloud_pass": 0, "cloud_steps": 6, "cloud_octaves": 6, "radiance_size": Sky.RADIANCE_SIZE_256,
		"cloud_search": 3, "cloud_shadows": true,
		"terrain_detail": 1.0,
		"mirror": true, "mirror_scale": 0.5, "mirror_near_hz": 0.0, "mirror_far_hz": 0.0, "mirror_from_m": 0.0,
	},
}

## SSAO adaptive target and fade-out (the project's settings, kept).
const SSAO_ADAPTIVE_TARGET := 0.5
const SSAO_FADEOUT_FROM := 1500.0
const SSAO_FADEOUT_TO := 6000.0

var tier: int = DEFAULT_TIER
## The knobs in force: the tier's table with any overrides on top.
var knobs: Dictionary = PRESETS[NAMES[DEFAULT_TIER]].duplicate()
## True while an opaque full-screen panel covers the world.
var world_hidden := false


## Tier index for a name ("high"), or -1.
static func tier_of(name: String) -> int:
	return NAMES.find(name.strip_edges().to_lower())


## The knobs of a tier with overrides on top (unknown override keys are
## ignored with a warning, so a typo cannot pass silently).
static func knobs_for(tier_index: int, overrides: Dictionary = {}) -> Dictionary:
	var out: Dictionary = PRESETS[NAMES[clampi(tier_index, 0, NAMES.size() - 1)]].duplicate()
	for key in overrides:
		if not out.has(key):
			push_warning("RenderQuality: unknown knob '%s'" % key)
			continue
		out[key] = overrides[key]
	return out


func set_tier(tier_index: int, overrides: Dictionary = {}) -> void:
	tier = clampi(tier_index, 0, NAMES.size() - 1)
	knobs = knobs_for(tier, overrides)


func tier_name() -> String:
	return NAMES[tier]


## 3D render scale now: the tier's, or hidden_scale behind an opaque panel.
func render_scale() -> float:
	return float(knobs["hidden_scale"]) if world_hidden else float(knobs["scale"])


## The saved tier (the graphics section's quality in user://settings.json),
## or the default.
static func load_saved_tier() -> int:
	if not FileAccess.file_exists(SETTINGS_PATH):
		return DEFAULT_TIER
	return tier_from_settings(JSON.parse_string(FileAccess.get_file_as_string(SETTINGS_PATH)))


## The tier a parsed settings file names, or the default (for any shape).
static func tier_from_settings(data: Variant) -> int:
	if typeof(data) != TYPE_DICTIONARY:
		return DEFAULT_TIER
	var section: Variant = (data as Dictionary).get(SETTINGS_SECTION, {})
	if typeof(section) != TYPE_DICTIONARY:
		return DEFAULT_TIER
	var found := tier_of(str((section as Dictionary).get(SETTINGS_KEY, "")))
	return found if found >= 0 else DEFAULT_TIER


## Apply the global part: the viewport's anti-aliasing and 3D scale, and the
## RenderingServer's SSAO, depth-of-field and shadow quality.
func apply_global(viewport: Viewport, sun: DirectionalLight3D, moon: DirectionalLight3D, sky: Sky) -> void:
	if viewport != null:
		viewport.msaa_3d = int(knobs["msaa"]) as Viewport.MSAA
		viewport.screen_space_aa = int(knobs["screen_aa"]) as Viewport.ScreenSpaceAA
		viewport.use_taa = false
		apply_scale(viewport)
	RenderingServer.environment_set_ssao_quality(int(knobs["ssao_quality"]) as RenderingServer.EnvironmentSSAOQuality,
		bool(knobs["ssao_half"]), SSAO_ADAPTIVE_TARGET, int(knobs["ssao_blur"]), SSAO_FADEOUT_FROM, SSAO_FADEOUT_TO)
	# No jitter: without TAA it would read as noise in the blur.
	RenderingServer.camera_attributes_set_dof_blur_quality(int(knobs["dof_quality"]) as RenderingServer.DOFBlurQuality, false)
	RenderingServer.camera_attributes_set_dof_blur_bokeh_shape(int(knobs["dof_shape"]) as RenderingServer.DOFBokehShape)
	RenderingServer.directional_shadow_atlas_set_size(int(knobs["shadow_atlas"]), true)
	RenderingServer.directional_soft_shadow_filter_set_quality(int(knobs["soft_shadows"]) as RenderingServer.ShadowQuality)
	var mode := DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS if int(knobs["shadow_splits"]) >= 4 else DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
	for light in [sun, moon]:
		if light != null:
			light.directional_shadow_mode = mode
	if sky != null:
		sky.radiance_size = int(knobs["radiance_size"]) as Sky.RadianceSize


## The 3D scale and its upscaler (also on every world shown/hidden change).
func apply_scale(viewport: Viewport) -> void:
	if viewport == null:
		return
	var s := render_scale()
	viewport.scaling_3d_scale = s
	if s >= 0.999:
		viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	elif world_hidden:
		# Behind a panel only thin margins show: bilinear is enough.
		viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	else:
		var mode := str(knobs.get("upscaler", "auto"))
		viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_FSR if mode == "fsr" else upscaler()
		viewport.fsr_sharpness = float(knobs.get("fsr_sharpness", 0.2))


## The planar mirror's own viewport (G2-03, PlanarReflection): no
## anti-aliasing at all. The water shows it through ripples, at a share of
## the window's resolution (mirror_scale), so MSAA or SMAA there would only
## cost.
static func apply_mirror_viewport(viewport: Viewport) -> void:
	viewport.msaa_3d = Viewport.MSAA_DISABLED
	viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_DISABLED
	viewport.use_taa = false


static func upscaler() -> Viewport.Scaling3DMode:
	return UPSCALER_APPLE if OS.get_name() == "macOS" else UPSCALER_OTHER
