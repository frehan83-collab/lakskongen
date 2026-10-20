"""Land kit (farm-types spec §5.1, item F2-25): the exported kit meets the spec.

blender/build_land_base.py writes a JSON sidecar next to every .glb (triangle
count, materials, surfaces, bbox, per-part triangles and strips, measured
dimensions, per-part swatches); these tests hold the kit to the spec table:
every asset within 5% of its dimensions, every LOD inside its triangle
budget (the budgets equal the spec's), one LK_FacilityKit surface per LOD,
the swatches equal to §5.1 and present in the trim atlas, the atlas safe to
mipmap, the Godot import wired to the kit material, the preview evidence
(lid ellipse >= 40 px, dark lid vs pale wall dL >= 30 at the land framing),
and, when Blender is installed, a rebuild that reproduces the committed
files byte for byte and a build that stops over its budget.
"""

import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import zlib
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
KIT = GODOT / "assets" / "models" / "land_kit"
FACILITY = GODOT / "assets" / "models" / "facility_kit"
ATLAS = FACILITY / "textures" / "facility_atlas.png"
BUILD = (REPO / "blender" / "build_land_base.py").read_text()
SPEC = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"] if a.get("type") == "land_kit"}
KIT_IDS = ["tank_growout", "tank_pregrow", "tank_feeder", "process_hall_bay", "harvest_hall", "pipe_bridge_span",
           "quay", "intake_basin", "river_intake", "lox_tank", "genset_container", "substation", "feed_silo_land",
           "sludge_station"]
METRICS = REPO / "docs" / "run" / "evidence" / "f2_25_land_kit_metrics.json"
EVIDENCE = REPO / "docs" / "run" / "evidence" / "f2_25_land_kit.md"

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(asset, lod):
    return REPO / ASSETS[asset]["export_path"].format(lod=lod, variant="")


def _sidecar(asset, lod):
    return json.loads(_glb(asset, lod).with_suffix(".json").read_text())


def _glb_json(path):
    data = path.read_bytes()
    assert data[:4] == b"glTF"
    length = struct.unpack("<I", data[12:16])[0]
    return json.loads(data[20:20 + length])


def _strips():
    """build_land_base.STRIPS as (name, hex, roughness-or-None) rows."""
    block = BUILD[BUILD.index("STRIPS = ["):BUILD.index("STRIP_INDEX")]
    return [(m.group(1), m.group(2), None if m.group(3) == "None" else float(m.group(3)))
            for m in re.finditer(r'\("(\w+)", "(#[0-9A-F]{6})", (None|[\d.]+),', block)]


def _spec_rows():
    """Spec §5.1 rows: asset -> (hexes in its swatch cell, (LOD0, LOD1, LOD2) budget)."""
    section = SPEC[SPEC.index("### 5.1 Land kit"):SPEC.index("**Layouts**")]
    rows = {}
    last = []
    for line in section.splitlines():
        m = re.match(r"\| `(\w+)` \|", line)
        if not m:
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        hexes = re.findall(r"#[0-9A-Fa-f]{6}", cells[3]) or (last if cells[3] == "Same" else [])
        last = hexes
        nums = [int(n.replace(",", "")) if n.strip()[0].isdigit() else None
                for n in re.split(r"\s*/\s*", cells[4])]
        rows[m.group(1)] = ([h.upper() for h in hexes], tuple(nums))
    return rows


def _lab(hexc):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(int(hexc[i:i + 2], 16)) for i in (1, 3, 5))
    f = [(t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116) for t in
         ((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, 0.2126 * r + 0.7152 * g + 0.0722 * b,
          (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883)]
    return 116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])


def _de(a, b):
    return math.dist(_lab(a), _lab(b))


def _read_png(path):
    """Decode the atlas (RGBA8, every row filter 0, as build_land_base writes it)."""
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, w, h = 8, b"", 0, 0
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + n]
        if tag == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            assert (depth, ctype) == (8, 6)
        elif tag == b"IDAT":
            idat += body
        pos += 12 + n
    raw = zlib.decompress(idat)
    rows = []
    stride = 4 * w + 1
    for y in range(h):
        assert raw[y * stride] == 0, "filter 0 only"
        rows.append(raw[y * stride + 1:(y + 1) * stride])
    return w, h, rows


# ── Manifest and spec ─────────────────────────────────────────────────────────

def test_the_manifest_lists_the_whole_kit():
    assert sorted(ASSETS) == sorted(KIT_IDS)
    for aid, a in ASSETS.items():
        assert a["build_script"] == "blender/build_land_base.py" and a["build_args"] == ["--asset", aid]
        assert a["materials"] == ["LK_FacilityKit"] and a["spec_tolerance"] <= 0.05
        assert a["godot_material"] == "godot/assets/models/facility_kit/LK_FacilityKit.tres"


def test_every_spec_row_is_built_with_its_budget():
    rows = _spec_rows()
    assert sorted(rows) == sorted(KIT_IDS), sorted(rows)
    limits = dict(re.findall(r'"(\w+)": \((\d+, \d+, (?:\d+|None))\)', BUILD))
    for aid, (_hexes, budget) in rows.items():
        built = tuple(None if v.strip() == "None" else int(v) for v in limits[aid].split(","))
        assert built == budget, (aid, built, budget)
        assert ASSETS[aid]["tri_max"] == {lod: n for lod, n in zip(("LOD0", "LOD1", "LOD2"), budget) if n is not None}


# ── Exports ───────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("aid", KIT_IDS)
def test_every_lod_is_one_mesh_with_one_kit_surface(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        j = _glb_json(_glb(aid, lod))
        assert len(j["meshes"]) == 1 and len(j["meshes"][0]["primitives"]) == 1, (aid, lod)
        assert [m["name"] for m in j["materials"]] == ["LK_FacilityKit"], (aid, lod)
        assert [n["name"] for n in j["nodes"]] == [aid], (aid, lod)
        attrs = j["meshes"][0]["primitives"][0]["attributes"]
        assert {"POSITION", "NORMAL", "TEXCOORD_0", "COLOR_0"} <= set(attrs), (aid, lod, attrs)
        side = _sidecar(aid, lod)
        assert side["material_count"] == 1 and side["surfaces"] == 1 and side["materials"] == ["LK_FacilityKit"]
        assert side["asset"] == aid and side["lod"] == lod


@pytest.mark.parametrize("aid", KIT_IDS)
def test_dimensions_are_within_5_percent_of_the_spec(aid):
    spec = ASSETS[aid]["spec"]
    for lod in [l for l in ASSETS[aid]["lod_levels"] if l != "LOD2"]:
        dims = _sidecar(aid, lod)["dimensions"]
        for key, want in spec.items():
            got = dims[key]
            assert abs(got - want) <= 0.05 * want + 1e-9, (aid, lod, key, got, want)


@pytest.mark.parametrize("aid", KIT_IDS)
def test_lod2_keeps_the_footprint(aid):
    if "LOD2" not in ASSETS[aid]["lod_levels"]:
        pytest.skip("rides in the tank's LOD2")
    # The far impostor stands where the asset stands: inside its LOD0 box
    # and not a sliver of it (railings, pipes and fins may go).
    a = _sidecar(aid, "LOD0")["bbox_size"]
    c = _sidecar(aid, "LOD2")["bbox_size"]
    for k in range(3):
        assert 0.4 * a[k] <= c[k] <= a[k] + 0.5, (aid, k, a, c)


@pytest.mark.parametrize("aid", KIT_IDS)
def test_triangle_budgets_hold_and_lods_step_down(aid):
    asset = ASSETS[aid]
    tris = [_sidecar(aid, lod)["tri_count"] for lod in asset["lod_levels"]]
    for lod, n in zip(asset["lod_levels"], tris):
        assert n <= asset["tri_max"][lod], (aid, lod, n)
        assert n == asset["tri_budget"][lod], (aid, lod, "manifest tri_budget is stale")
    assert all(a > b for a, b in zip(tris, tris[1:])), (aid, tris)


def test_verifier_passes_every_land_kit_lod():
    for aid in KIT_IDS:
        spec = pipeline.AssetSpec.from_dict(ASSETS[aid])
        for lod in spec.lod_levels:
            result = pipeline.validate_asset(spec, lod, None, _glb(aid, lod))
            assert result.passed, (aid, lod, result.errors)


# ── Swatches and the trim atlas ───────────────────────────────────────────────

def test_spec_swatches_are_strips_of_the_atlas():
    strips = {name: hexc for name, hexc, _ in _strips()}
    colours = set(strips.values())
    for aid, (hexes, _budget) in _spec_rows().items():
        for h in hexes:
            assert h in colours, (aid, h)
    assert strips["tank_lid"] == "#2A3036" and strips["tank_lid_pale"] == "#EDEFEF"


@pytest.mark.parametrize("aid", KIT_IDS)
def test_swatch_report_matches_the_spec(aid):
    strips = {name: hexc for name, hexc, _ in _strips()}
    side = _sidecar(aid, "LOD0")
    wanted = ASSETS[aid]["spec_swatches"]
    assert wanted and set(wanted) <= set(side["swatches"]), (aid, wanted, side["swatches"])
    spec_hexes = set(_spec_rows()[aid][0])
    for part, sw in side["swatches"].items():
        assert sw["spec"] == wanted[part] and sw["spec"] in spec_hexes, (aid, part)
        assert strips[sw["strip"]] == sw["spec"], (aid, part, sw)
        assert _de(sw["hex"], sw["spec"]) <= 3.0, (aid, part, sw)
        assert side["part_strips"][part] == sw["strip"]


def test_atlas_strips_hold_their_swatch_and_survive_mipmapping():
    w, h, rows = _read_png(ATLAS)
    assert (w, h) == (1024, 1024)
    for idx, (name, hexc, rough) in enumerate(_strips()):
        block = rows[idx * 32:(idx + 1) * 32]
        # Constant down the strip: every mip level down to 32 x 32 keeps it.
        assert all(r == block[0] for r in block), name
        line = block[16]
        px = sorted((tuple(line[4 * x:4 * x + 3]) for x in range(w)), key=sum)
        median = "#%02X%02X%02X" % px[w // 2]
        assert _de(median, hexc) <= 3.0, (name, median, hexc)
        alphas = {line[4 * x + 3] for x in range(w)}
        assert alphas == ({255} if rough is None else {round(rough * 255)}), (name, alphas)


@pytest.mark.parametrize("aid", ["process_hall_bay", "harvest_hall"])
def test_hall_roofs_carry_roof_lights(aid):
    # Lookdev wave 4: from the land framing the halls are mostly roof; pale
    # roof-light bands break the dark roofs at LOD0 and LOD1 (the land
    # framing's band), on their own strip, which glows at night.
    for lod in ("LOD0", "LOD1"):
        side = _sidecar(aid, lod)
        assert side["parts"].get("Skylights", 0) > 0 and side["part_strips"]["Skylights"] == "skylight", (aid, lod)
    for name in ("facility_kit.gdshader", "facility_kit_build.gdshader"):
        shader = _kit_shader(name)
        assert "EMISSION = roof_light_color * night_light * emission_energy * roof_light_glow;" in shader, name


def _kit_shader(name):
    """A kit shader's text with its res:// includes inlined (F2-20 moved the
    kit's body to include/facility_kit_body.gdshaderinc, shared by the
    back-face-culled kit and its double-sided build twin)."""
    text = (GODOT / "shaders" / name).read_text()
    return re.sub(r'#include "res://([^"]+)"', lambda m: (GODOT / m.group(1)).read_text(), text)


def test_the_build_twin_is_the_kit_drawn_double_sided():
    # F2-20: a shell cut open by its build progress shows the inside of its
    # far wall: the same body, culling off, the inner face shaded.
    kit = (GODOT / "shaders" / "facility_kit.gdshader").read_text()
    build = (GODOT / "shaders" / "facility_kit_build.gdshader").read_text()
    include = '#include "res://shaders/include/facility_kit_body.gdshaderinc"'
    assert include in kit and include in build
    assert "render_mode cull_back," in kit and "render_mode cull_disabled," in build
    assert "#define LK_FACILITY_BUILD" in build and "#define LK_FACILITY_BUILD" not in kit
    body = (GODOT / "shaders" / "include" / "facility_kit_body.gdshaderinc").read_text()
    assert "#ifdef LK_FACILITY_BUILD" in body and "!FRONT_FACING" in body
    tres = (FACILITY / "LK_FacilityKitBuild.tres").read_text()
    assert 'path="res://shaders/facility_kit_build.gdshader"' in tres
    assert 'path="res://assets/models/facility_kit/textures/facility_atlas.png"' in tres


def test_shader_rows_follow_the_strip_order():
    names = [s[0] for s in _strips()]
    shader = _kit_shader("facility_kit.gdshader")
    rows = dict(re.findall(r"const float ROW_(\w+) = ([\d.]+);", shader))
    for const, strip in (("TANK_WALL", "tank_wall"), ("TANK_BAND", "tank_band"), ("TANK_LID", "tank_lid"),
                         ("ROTOR", "rotor"), ("BRIDGE_LEG", "bridge_leg"), ("SKYLIGHT", "skylight")):
        assert float(rows[const]) == names.index(strip), const
    # The pale lid swatch sits one row under the dark lid (the shader's swap).
    assert names.index("tank_lid_pale") == names.index("tank_lid") + 1
    mask = (GODOT / "qa" / "shaders" / "kit_strip_mask.gdshader").read_text()
    assert "row == 2.0 || row == 3.0" in mask and "row == 0.0" in mask and "row == 1.0" in mask


# ── Godot wiring ──────────────────────────────────────────────────────────────

@pytest.mark.parametrize("aid", KIT_IDS)
def test_godot_import_uses_the_kit_material_and_hand_made_lods(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        text = Path(str(_glb(aid, lod)) + ".import").read_text()
        assert "meshes/generate_lods=false" in text, (aid, lod)
        block = re.search(r'"LK_FacilityKit": \{(.*?)\}', text, re.S)
        assert block and '"use_external/enabled": true' in block.group(1), (aid, lod)
        assert '"use_external/path": "res://assets/models/facility_kit/LK_FacilityKit.tres"' in block.group(1)


def test_kit_material_reads_the_atlas_with_lossless_mips():
    tres = (FACILITY / "LK_FacilityKit.tres").read_text()
    assert 'path="res://shaders/facility_kit.gdshader"' in tres
    assert 'path="res://assets/models/facility_kit/textures/facility_atlas.png"' in tres
    assert 'resource_name = "LK_FacilityKit"' in tres
    imp = (FACILITY / "textures" / "facility_atlas.png.import").read_text()
    assert "mipmaps/generate=true" in imp and "compress/mode=0" in imp and "detect_3d/compress_to=0" in imp


def test_layout_and_preview_use_the_kit_sizes():
    layout = (GODOT / "scripts" / "qa" / "LandCampusLayout.gd").read_text()
    for key, want in (("GROWOUT_D", 28.0), ("GROWOUT_H", 13.0), ("PREGROW_D", 16.0), ("PREGROW_H", 9.0)):
        assert re.search(r"const %s := %s\n" % (key, re.escape(str(want))), layout), key
    preview = (GODOT / "scripts" / "qa" / "LandKitPreview.gd").read_text()
    assert '"tank_growout": 13.0 + 0.022 * 28.0' in preview
    assert "rise = 0.022 * d" in (REPO / "blender" / "land_designs.py").read_text()


# ── Preview evidence ──────────────────────────────────────────────────────────

@pytest.fixture(scope="module")
def metrics():
    return json.loads(METRICS.read_text())


def test_land_framing_reads_dark_lids_on_pale_walls(metrics):
    for sid in ("39_landkit_day", "39_landkit_dusk"):
        shot = metrics["shots"][sid]
        assert shot["camera"]["spec"] == {"mode": "focus", "target": "pad_a", "distance_m": 520.0, "pitch_deg": -40.0}
        tank = shot["sample_tank"]
        assert tank["lid"]["height_px"] >= 40, (sid, tank["lid"])
        assert tank["delta_l_lid_wall"] >= 30.0, (sid, tank)
        assert float(tank["wall"]["l_mean"]) > float(tank["lid"]["l_mean"]), sid
    assert metrics["shots"]["39_landkit_dusk"]["environment"]["night_light"] > 0.0
    assert metrics["shots"]["39_landkit_dusk"]["environment"]["sun_elevation_deg"] < 5.0


def test_kit_campus_stays_inside_the_draw_and_triangle_budgets(metrics):
    perf = metrics["shots"]["39_landkit_day"]["perf"]
    # Three plots (pad_a 4 lots, pad_b 2 lots, the delta) with the kit:
    # the budget is <= 20 opaque + <= 16 shadow per full campus.
    assert perf["added_opaque_draws"] <= 20 and perf["added_shadow_draws"] <= 16, perf
    assert metrics["shots"]["39_landkit_day"]["triangles_drawn"] <= 250_000


def test_evidence_doc_compares_against_the_references():
    doc = EVIDENCE.read_text()
    for needle in ("L1", "L2", "## Acceptance", "byte", "LOD1", "lid ellipse", "39_landkit_dusk"):
        assert needle in doc, needle
    for img in re.findall(r"\]\((f2_25/[^)]+)\)", doc):
        assert (EVIDENCE.parent / img).is_file(), img


# ── Rebuild (needs Blender) ───────────────────────────────────────────────────

def _blender():
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


def _build(tmp_path, *args):
    return subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_land_base.py"), "--",
         "--out", str(tmp_path), *args], capture_output=True, text=True, timeout=900)


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("aid,lod", [("tank_growout", "LOD1"), ("pipe_bridge_span", "LOD0"), ("lox_tank", "LOD2")])
def test_build_script_reproduces_the_committed_files(tmp_path, aid, lod):
    out = _build(tmp_path, "--asset", aid, "--lod", lod, "--textures", "--atlas-out", str(tmp_path / "atlas.png"))
    assert out.returncode == 0, out.stderr[-2000:]
    assert (tmp_path / "atlas.png").read_bytes() == ATLAS.read_bytes()
    committed = _glb(aid, lod)
    rebuilt = tmp_path / committed.name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(aid, lod)
    assert {k: v for k, v in new_side.items() if k != "blender_version"} == \
        {k: v for k, v in old_side.items() if k != "blender_version"}
    if new_side["blender_version"] == old_side["blender_version"]:
        assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_stops_over_its_triangle_budget(tmp_path):
    out = _build(tmp_path, "--asset", "lox_tank", "--lod", "LOD1", "--no-bake", "--tri-limit-scale", "0.5")
    assert out.returncode != 0
    assert "lox_tank LOD1 has 88 triangles, over its 60 budget" in out.stdout + out.stderr
