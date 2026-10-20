"""Terrain material and vegetation (roadmap G2-02): static guards.

The mountains read as smooth clay: a palette shader with two detail
textures blended by a UV switch, no snow season, no trees. These checks pin
the replacement:

- the terrain shader samples at most two Texture2DArrays (no loose
  samplers), declares its normal array with hint_normal, never interpolates
  texture coordinates between projections (it blends samples), fetches every
  layer with explicit gradients or LODs, and blends at most two projections;
- the arrays' layer order is the generator's, their imports keep all layers
  and VRAM compression, and the generator is offline (no downloads) and
  reproduces the committed images;
- the ground's season reaches the shaders only through the
  EnvironmentDirector, as the global uniforms project.godot declares;
- the birch and spruce meet the asset manifest (size, triangle ceilings,
  LOD1 a true silhouette of LOD0) and their build script rebuilds them.

Needs no engine (Blender only for the rebuild check).
"""

import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SHADER = GODOT / "shaders" / "terrain_shader.gdshader"
INCLUDE = GODOT / "shaders" / "ground_season.gdshaderinc"
VEG_SHADER = GODOT / "shaders" / "vegetation_shader.gdshader"
DIRECTOR = "godot/scripts/environment/EnvironmentDirector.gd"
TEXTURES = GODOT / "assets" / "textures" / "terrain"
GENERATOR = REPO / "tools" / "gen_terrain_textures.py"
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"]}
TREES = ("spruce", "birch")


def _code(path: Path) -> str:
    """Shader source without // comments."""
    return "\n".join(line.split("//", 1)[0] for line in path.read_text(encoding="utf-8").splitlines())


def _generator():
    spec = importlib.util.spec_from_file_location("gen_terrain_textures", GENERATOR)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# ── The shader ───────────────────────────────────────────────────────────────

def test_at_most_two_sampler_arrays_and_no_loose_samplers():
    code = _code(SHADER)
    arrays = re.findall(r"uniform\s+sampler2DArray\s+(\w+)", code)
    assert 1 <= len(arrays) <= 2, arrays
    assert not re.search(r"uniform\s+sampler(2D|3D|Cube)\b", code), "a loose sampler beside the arrays"


def test_normals_use_hint_normal_and_albedo_is_srgb():
    code = _code(SHADER)
    normal = re.search(r"uniform\s+sampler2DArray\s+normal_rough_ao\s*:\s*([^;=]*)", code)
    albedo = re.search(r"uniform\s+sampler2DArray\s+albedo_height\s*:\s*([^;=]*)", code)
    assert normal and "hint_normal" in normal.group(1)
    assert albedo and "source_color" in albedo.group(1)


def test_no_uv_lerp_between_projections():
    code = _code(SHADER)
    # mix()/lerp of texture coordinates swims and smears; samples are blended.
    for call in re.finditer(r"\bmix\s*\(([^;]*)", code):
        args = call.group(1)
        assert not re.search(r"\b\w*uv\w*\b", args, re.IGNORECASE), f"UV lerp: mix({args[:80]}"
    assert "lerp" not in code


def test_layer_fetches_use_explicit_gradients_and_two_projections_at_most():
    code = _code(SHADER)
    # Implicit-derivative fetches would see the jump where the chosen pair of
    # projections changes: every array fetch names its gradient or LOD.
    assert not re.search(r"\btexture\s*\(\s*(albedo_height|normal_rough_ao)", code)
    assert re.search(r"textureGrad\s*\(\s*albedo_height", code)
    assert re.search(r"textureGrad\s*\(\s*normal_rough_ao", code)
    # Biplanar: the major and the median axis only.
    assert len(re.findall(r"\bmake_proj\s*\(\s*(major|median)", code)) == 2
    assert "whiteout" in code


def test_layer_constants_follow_the_generator():
    gen = _generator()
    code = _code(SHADER)
    consts = {m.group(1): float(m.group(2)) for m in re.finditer(r"const\s+float\s+L_(\w+)\s*=\s*([\d.]+)", code)}
    names = {"rock": "ROCK", "grass": "GRASS", "heath": "HEATH", "snow": "SNOW", "shingle": "SHINGLE",
             "spruce_canopy": "SPRUCE", "birch_canopy": "BIRCH"}
    for index, layer in enumerate(gen.LAYERS):
        assert consts[names[layer]] == index, (layer, index)
    assert consts["NOISE"] == len(gen.LAYERS) + gen.NOISE_SLICES.index("noise4")
    assert consts["BUMP"] == len(gen.LAYERS) + gen.NOISE_SLICES.index("bump")
    assert float(re.search(r"NEAR_TILE_M\s*=\s*([\d.]+)", code).group(1)) == gen.NEAR_TILE_M
    assert float(re.search(r"NOISE_TEXELS\s*=\s*([\d.]+)", code).group(1)) == gen.SIZE


def test_wet_band_is_at_least_a_fifth_darker_up_to_1_5_m():
    # Roadmap G2-02 acceptance 3 (measured on a lit wall by the probe:
    # 0.2-1.3 m luminance 0.69 of the dry rock above the lichen zone).
    code = _code(SHADER)
    albedo = float(re.search(r"uniform\s+float\s+wet_band_albedo[^=]*=\s*([\d.]+)", code).group(1))
    height = float(re.search(r"uniform\s+float\s+wet_band_m[^=]*=\s*([\d.]+)", code).group(1))
    assert albedo <= 0.8 and height >= 1.5
    assert re.search(r"col\s*\*\s*wet_band_albedo", code), "the wet band multiplier is not applied to the colour"


# ── The arrays ───────────────────────────────────────────────────────────────

@pytest.mark.parametrize("name,layers", [("terrain_albedo_height", 7), ("terrain_normal_rough_ao", 9)])
def test_arrays_import_every_layer_vram_compressed(name, layers):
    imp = (TEXTURES / f"{name}.png.import").read_text()
    assert 'importer="2d_array_texture"' in imp
    assert f"slices/vertical={layers}" in imp and "slices/horizontal=1" in imp
    assert "compress/mode=2" in imp, "VRAM compression"
    assert "mipmaps/generate=true" in imp
    width, height = Image.open(TEXTURES / f"{name}.png").size
    assert height == width * layers


def test_generator_works_offline():
    source = GENERATOR.read_text()
    for banned in ("http://", "https://", "urllib", "requests", "socket"):
        assert banned not in source, banned


def test_generator_reproduces_the_committed_arrays(tmp_path):
    out = subprocess.run(["python3.12" if shutil.which("python3.12") else "python3", str(GENERATOR), "--out", str(tmp_path)],
                         capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    for name in ("terrain_albedo_height.png", "terrain_normal_rough_ao.png"):
        rebuilt = np.asarray(Image.open(tmp_path / name))
        committed = np.asarray(Image.open(TEXTURES / name))
        assert rebuilt.shape == committed.shape, name
        assert np.array_equal(rebuilt, committed), name


# ── The ground's season ──────────────────────────────────────────────────────

def _globals_in(path: Path) -> list[str]:
    return re.findall(r"global\s+uniform\s+float\s+(lk_\w+)", _code(path))


def test_ground_globals_are_declared_where_the_shaders_read_them():
    project = (GODOT / "project.godot").read_text()
    section = project[project.index("[shader_globals]"):]
    declared = re.findall(r'^(lk_\w+)=\{\s*"type":\s*"float"', section, re.MULTILINE)
    used = _globals_in(INCLUDE)
    assert used and set(used) <= set(declared), (used, declared)
    director = (REPO / DIRECTOR).read_text()
    listed = re.search(r"const GROUND_GLOBALS := \[([^\]]*)\]", director).group(1)
    assert set(re.findall(r'"(lk_\w+)"', listed)) == set(used)
    assert '#include "res://shaders/ground_season.gdshaderinc"' in SHADER.read_text()
    assert '#include "res://shaders/ground_season.gdshaderinc"' in VEG_SHADER.read_text()


def test_the_director_is_the_only_writer_of_global_shader_parameters():
    writers = []
    for path in sorted(GODOT.rglob("*")):
        rel = path.relative_to(REPO).as_posix()
        if path.suffix not in (".gd", ".cs") or "/addons/" in rel or "/.godot/" in rel or "/tests/" in rel:
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            code = line.split("#", 1)[0] if rel.endswith(".gd") else line.split("//", 1)[0]
            if re.search(r"global_shader_parameter_set|GlobalShaderParameterSet", code):
                writers.append(rel)
    assert set(writers) == {DIRECTOR}, writers


def test_the_terrain_no_longer_takes_a_season_of_its_own():
    terrain = (GODOT / "scripts" / "environment" / "FjordTerrain.gd").read_text()
    assert "func set_season" not in terrain
    assert not re.search(r"uniform\s+float\s+season\b", _code(SHADER))


# ── The trees ────────────────────────────────────────────────────────────────

def _side(tree: str, lod: str) -> dict:
    glb = REPO / ASSETS[tree]["export_path"].format(lod=lod)
    assert glb.is_file(), glb
    return json.loads(glb.with_suffix(".json").read_text())


@pytest.mark.parametrize("tree", TREES)
def test_trees_meet_the_manifest(tree):
    asset = ASSETS[tree]
    assert asset["type"] == "vegetation" and asset["build_script"] == "blender/build_vegetation.py"
    tol = asset["spec_tolerance"]
    lod0 = _side(tree, "LOD0")
    width, height, depth = lod0["bbox_size"]
    assert abs(height - asset["spec"]["height_m"]) <= tol * asset["spec"]["height_m"], (tree, height)
    for extent in (width, depth):
        assert abs(extent - asset["spec"]["crown_width_m"]) <= tol * asset["spec"]["crown_width_m"], (tree, extent)
    for lod in asset["lod_levels"]:
        side = _side(tree, lod)
        assert side["tri_count"] <= asset["tri_max"][lod], (tree, lod, side["tri_count"])
        assert side["materials"] == asset["materials"]
    # LOD1 keeps LOD0's silhouette (no pop at the switch), with a tenth of the triangles.
    lod1 = _side(tree, "LOD1")
    assert lod1["bbox_size"][1] >= 0.85 * height
    assert lod1["tri_count"] * 4 <= lod0["tri_count"]


def test_vegetation_draws_both_lods_by_visibility_range():
    script = (GODOT / "scripts" / "environment" / "Vegetation.gd").read_text()
    assert "visibility_range_end = LOD_DISTANCE_M" in script
    assert "visibility_range_begin = LOD_DISTANCE_M" in script
    lod = float(re.search(r"const LOD_DISTANCE_M := ([\d.]+)", script).group(1))
    assert 500.0 <= lod <= 800.0, "the full trees give way to silhouettes beyond about 600 m"
    # Bare birch thins through alpha scissor, not blending (no sorting, MSAA coverage).
    veg = _code(VEG_SHADER)
    assert "ALPHA_SCISSOR_THRESHOLD" in veg and "alpha_to_coverage" in veg


def _blender() -> str | None:
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_script_reproduces_the_committed_trees(tmp_path):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_vegetation.py"), "--",
         "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    for tree in TREES:
        for lod in ASSETS[tree]["lod_levels"]:
            committed = REPO / ASSETS[tree]["export_path"].format(lod=lod)
            rebuilt = tmp_path / committed.name
            new_side = json.loads(rebuilt.with_suffix(".json").read_text())
            old_side = json.loads(committed.with_suffix(".json").read_text())
            geometry = ("tri_count", "material_count", "materials", "bbox_size")
            assert {k: new_side.get(k) for k in geometry} == {k: old_side.get(k) for k in geometry}
            if new_side.get("blender_version") == old_side.get("blender_version"):
                assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()


# ── The shore buildings ─────────────────────────────────────────────────────

SHORE = ("naust", "shore_quay")
# The naust's stone base sinks 2.5 m into sloping ground; the roof is 0.12 m thick.
NAUST_BASE_SINK_M = 2.5
NAUST_ROOF_M = 0.12
# The quay reaches 4 m into the land behind the waterline.
QUAY_INLAND_M = 4.0


def test_shore_props_meet_the_manifest():
    naust, quay = ASSETS["naust"], ASSETS["shore_quay"]
    for asset in (naust, quay):
        assert asset["type"] == "shore_prop" and asset["build_script"] == "blender/build_shore_props.py"
        side = _side(asset["id"], "LOD0")
        assert side["tri_count"] <= asset["tri_max"]["LOD0"]
        assert side["materials"] == asset["materials"]
    tol = naust["spec_tolerance"]
    w, h, length = _side("naust", "LOD0")["bbox_size"]
    assert abs(w - naust["spec"]["width_m"]) <= tol * naust["spec"]["width_m"]
    assert abs(length - naust["spec"]["length_m"]) <= tol * naust["spec"]["length_m"]
    ridge = h - NAUST_BASE_SINK_M - NAUST_ROOF_M
    assert abs(ridge - naust["spec"]["ridge_m"]) <= tol * naust["spec"]["ridge_m"], ridge
    w, h, length = _side("shore_quay", "LOD0")["bbox_size"]
    tol = quay["spec_tolerance"]
    assert abs(w - quay["spec"]["width_m"]) <= tol * quay["spec"]["width_m"]
    assert abs(length - QUAY_INLAND_M - quay["spec"]["out_m"]) <= tol * quay["spec"]["out_m"]


def test_naust_walls_take_a_paint_per_building():
    script = (GODOT / "scripts" / "environment" / "ShoreProps.gd").read_text()
    # The wall material the Blender build names is the one Godot repaints.
    assert 'const WALL_MATERIAL := "LK_NaustWall"' in script
    assert "LK_NaustWall" in ASSETS["naust"]["materials"]
    paints = re.findall(r'Color\("#([0-9A-Fa-f]{6})"\)', script[script.index("NAUST_PAINT"):script.index("\n", script.index("NAUST_PAINT"))])
    assert len(paints) == 2, paints


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_script_reproduces_the_committed_shore_props(tmp_path):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_shore_props.py"), "--",
         "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    for prop in SHORE:
        committed = REPO / ASSETS[prop]["export_path"].format(lod="LOD0")
        rebuilt = tmp_path / committed.name
        new_side = json.loads(rebuilt.with_suffix(".json").read_text())
        old_side = json.loads(committed.with_suffix(".json").read_text())
        geometry = ("tri_count", "material_count", "materials", "bbox_size")
        assert {k: new_side.get(k) for k in geometry} == {k: old_side.get(k) for k in geometry}
        if new_side.get("blender_version") == old_side.get("blender_version"):
            assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()
