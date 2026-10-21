"""Construction kit (farm-types spec §5.3, item F2-28): the exported kit meets the spec.

blender/build_construction_kit.py writes a JSON sidecar next to every .glb
(triangle count, materials, surfaces, bbox, per-part triangles and strips,
measured dimensions, per-part swatches); these tests hold the kit to the spec
table: every asset within 5% of its dimensions, every LOD inside its triangle
budget (the budgets equal the spec's), one LK_FacilityKit surface per LOD, the
swatches equal to §5.3 and present in the shared trim atlas, the Godot import
wired to the kit material, and, when Blender is installed, a rebuild that
reproduces the committed files byte for byte and a build that stops over its
budget.

The two acceptance criteria that are about how the kit meets the rest of the
game:
 2. the scaffold ring fits tank_growout: checked on the committed tank's own
    vertices (its concrete plinth), not on nominal numbers;
 4. every land and offshore construction phase of spec §6.3 has its asset
    set: blender/asset_manifest.json's construction_phase_sets lists, per
    spec row, the assets drawn in that phase. The cross-check reads the spec's
    two tables and the engine's phase keys (LandConstructionPhases.cs,
    OffshoreProfiles.cs), so a phase added to the engine or the spec without
    an asset set fails here.
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
KIT = GODOT / "assets" / "models" / "construction_kit"
LAND = GODOT / "assets" / "models" / "land_kit"
FACILITY = GODOT / "assets" / "models" / "facility_kit"
ATLAS = FACILITY / "textures" / "facility_atlas.png"
BUILD = (REPO / "blender" / "build_construction_kit.py").read_text()
LAND_BUILD = (REPO / "blender" / "build_land_base.py").read_text()
SPEC = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ALL_ASSETS = {a["id"]: a for a in MANIFEST["assets"]}
ASSETS = {a["id"]: a for a in MANIFEST["assets"] if a.get("type") == "construction_kit"}
KIT_IDS = ["tower_crane", "scaffold_ring", "excavator", "blast_mats"]
PHASE_SETS = MANIFEST["construction_phase_sets"]
EVIDENCE = REPO / "docs" / "run" / "evidence" / "f2_28_construction_kit.md"

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(asset, lod):
    return REPO / ALL_ASSETS[asset]["export_path"].format(lod=lod, variant="")


def _sidecar(asset, lod):
    return json.loads(_glb(asset, lod).with_suffix(".json").read_text())


def _glb_chunks(path):
    data = path.read_bytes()
    assert data[:4] == b"glTF"
    length = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + length])
    bin_start = 20 + length + 8
    return j, data[bin_start:]


def _glb_json(path):
    return _glb_chunks(path)[0]


def _accessor(j, blob, index):
    """The float rows of a glTF accessor (VEC2 / VEC3, float32)."""
    acc = j["accessors"][index]
    assert acc["componentType"] == 5126, acc
    width = {"VEC2": 2, "VEC3": 3}[acc["type"]]
    view = j["bufferViews"][acc["bufferView"]]
    stride = view.get("byteStride", 4 * width)
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return [struct.unpack_from("<%df" % width, blob, base + i * stride) for i in range(acc["count"])]


def _strips():
    """build_land_base.STRIPS as (name, hex, roughness-or-None) rows."""
    block = LAND_BUILD[LAND_BUILD.index("STRIPS = ["):LAND_BUILD.index("STRIP_INDEX")]
    return [(m.group(1), m.group(2), None if m.group(3) == "None" else float(m.group(3)))
            for m in re.finditer(r'\("(\w+)", "(#[0-9A-F]{6})", (None|[\d.]+),', block)]


def _spec_rows():
    """Spec §5.3 rows of this kit: asset -> (hexes of its swatch cell, dimension numbers of its
    dimension cell, (LOD0, LOD1, LOD2) budget with None for a missing LOD). A row can name
    several assets (`excavator`, `blast_mats` share one)."""
    section = SPEC[SPEC.index("### 5.3 Closed-pen kit"):SPEC.index("**Yard picture-in-picture")]
    rows = {}
    for line in section.splitlines():
        m = re.match(r"\| (`\w+`(?:, `\w+`)*) \|", line)
        if not m:
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        nums = [int(n.replace(",", "")) for n in re.split(r"\s*/\s*", cells[3])]
        budget = tuple(nums + [None] * (3 - len(nums)))
        hexes = [h.upper() for h in re.findall(r"#[0-9A-Fa-f]{6}", cells[2])]
        dims = [float(n) for n in re.findall(r"\d+(?:\.\d+)?", cells[1])]
        for aid in re.findall(r"`(\w+)`", cells[0]):
            if aid in KIT_IDS:
                rows[aid] = (hexes, dims, budget)
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
    """Decode the atlas (RGBA8, every row filter 0, as the land kit writes it)."""
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
        assert a["build_script"] == "blender/build_construction_kit.py" and a["build_args"] == ["--asset", aid]
        assert a["materials"] == ["LK_FacilityKit"] and a["spec_tolerance"] <= 0.05
        assert a["godot_material"] == "godot/assets/models/facility_kit/LK_FacilityKit.tres"
        assert a["atlas"] == "godot/assets/models/facility_kit/textures/facility_atlas.png"
        assert a["export_path"] == f"godot/assets/models/construction_kit/{aid}_{{lod}}.glb"


def test_every_spec_row_is_built_with_its_budget():
    rows = _spec_rows()
    assert sorted(rows) == sorted(KIT_IDS), sorted(rows)
    limits = dict(re.findall(r'"(\w+)": \((\d+, \d+, (?:\d+|None))\)', BUILD))
    for aid, (_hexes, _dims, budget) in rows.items():
        built = tuple(None if v.strip() == "None" else int(v) for v in limits[aid].split(","))
        assert built == budget, (aid, built, budget)
        wanted = {lod: n for lod, n in zip(("LOD0", "LOD1", "LOD2"), budget) if n is not None}
        assert ASSETS[aid]["tri_max"] == wanted, aid
        assert ASSETS[aid]["lod_levels"] == list(wanted), aid


def test_manifest_dimensions_are_the_specs_numbers():
    rows = _spec_rows()
    crane, scaffold, excavator = rows["tower_crane"][1], rows["scaffold_ring"][1], rows["excavator"][1]
    assert crane == [50.0, 50.0] and ASSETS["tower_crane"]["spec"] == {"mast_height_m": crane[0], "jib_length_m": crane[1]}
    assert scaffold == [29.5, 13.0] and ASSETS["scaffold_ring"]["spec"] == {"outer_diameter_m": scaffold[0], "height_m": scaffold[1]}
    assert excavator[:3] == [10.0, 3.5, 3.5]
    assert ASSETS["excavator"]["spec"] == {"length_m": excavator[0], "width_m": excavator[1], "height_m": excavator[2]}
    # The mats are "a decal stack" with no spec numbers: the manifest pins the artist estimate.
    assert ASSETS["blast_mats"]["spec"] == {"length_m": 12.0, "width_m": 8.0, "stack_height_m": 0.24}


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


def test_the_crane_is_a_50_m_mast_and_a_50_m_jib():
    # Acceptance 1, on the geometry the sidecar measured from the mast and jib parts.
    for lod in ("LOD0", "LOD1", "LOD2"):
        dims = _sidecar("tower_crane", lod)["dimensions"]
        assert abs(dims["mast_height_m"] - 50.0) <= 2.5 and abs(dims["jib_length_m"] - 50.0) <= 2.5, (lod, dims)
    for lod in ("LOD0", "LOD1"):
        # The jib reaches +X and the counter jib -X: the model's own bbox spans both.
        x_extent = _sidecar("tower_crane", lod)["bbox_size"][0]
        dims = _sidecar("tower_crane", lod)["dimensions"]
        assert x_extent >= dims["jib_length_m"] + dims["counter_jib_length_m"] - 0.5, lod


@pytest.mark.parametrize("aid", ["tower_crane"])
def test_lod2_keeps_the_footprint(aid):
    # The far impostor stands where the asset stands: inside its LOD0 box
    # and not a sliver of it (railings, pipes and lattice may go).
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


def test_verifier_passes_every_construction_kit_lod():
    for aid in KIT_IDS:
        spec = pipeline.AssetSpec.from_dict(ASSETS[aid])
        for lod in spec.lod_levels:
            result = pipeline.validate_asset(spec, lod, None, _glb(aid, lod))
            assert result.passed, (aid, lod, result.errors)


@pytest.mark.parametrize("aid", KIT_IDS)
def test_nothing_sinks_into_the_ground(aid):
    # Origins sit on the ground: no vertex of any LOD below it (the scaffold's
    # first brace tier used to dip 3 cm).
    for lod in ASSETS[aid]["lod_levels"]:
        j, blob = _glb_chunks(_glb(aid, lod))
        prim = j["meshes"][0]["primitives"][0]
        ys = [p[1] for p in _accessor(j, blob, prim["attributes"]["POSITION"])]
        assert min(ys) >= -1e-4, (aid, lod, min(ys))


# ── Swatches and the shared trim atlas ────────────────────────────────────────

def test_spec_swatches_are_strips_of_the_atlas():
    strips = {name: hexc for name, hexc, _ in _strips()}
    colours = set(strips.values())
    for aid, (hexes, _dims, _budget) in _spec_rows().items():
        assert hexes, aid
        for h in hexes:
            assert h in colours, (aid, h)


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


def test_crane_yellow_is_appended_after_the_land_strips():
    # The atlas is shared: this kit may only append a strip, so the land kit's
    # rows (and the shader's ROW_* constants) keep their indices.
    names = [s[0] for s in _strips()]
    assert names[26] == "skylight" and names[27] == "crane_yellow", names[24:]
    hexc, rough = dict((n, (h, r)) for n, h, r in _strips())["crane_yellow"]
    assert hexc == "#E6BE2E" and rough == 0.5
    w, h, rows = _read_png(ATLAS)
    line = rows[27 * 32 + 16]
    px = sorted((tuple(line[4 * x:4 * x + 3]) for x in range(w)), key=sum)
    median = "#%02X%02X%02X" % px[w // 2]
    assert _de(median, "#E6BE2E") <= 3.0, median
    assert {line[4 * x + 3] for x in range(w)} == {round(0.5 * 255)}


# ── The ring fits the tank (acceptance 2) ─────────────────────────────────────

def _tank_strip_vertices(lod, strip):
    """The committed tank_growout's vertices on one trim-atlas strip, as
    (planar radius, height) about the tank axis (glTF is Y-up)."""
    j, blob = _glb_chunks(LAND / f"tank_growout_{lod}.glb")
    attrs = j["meshes"][0]["primitives"][0]["attributes"]
    pos = _accessor(j, blob, attrs["POSITION"])
    uv = _accessor(j, blob, attrs["TEXCOORD_0"])
    row = [n for n, _h, _r in _strips()].index(strip)
    v = (row + 0.5) / 32.0  # glTF flips V: row r sits at (r + 0.5) / 32
    return [(math.hypot(p[0], p[2]), p[1]) for p, t in zip(pos, uv) if abs(t[1] - v) < 1e-3]


def _plinth_radius(lod="LOD0"):
    """The outermost reach of the committed tank_growout's concrete plinth,
    from its own vertices (the plinth is the only concrete in a tank)."""
    return max((r for r, _z in _tank_strip_vertices(lod, "concrete")), default=None)


def test_the_scaffold_ring_fits_tank_growout():
    tank = _sidecar("tank_growout", "LOD0")["dimensions"]
    shell_r = tank["shell_diameter_m"] / 2
    plinth_r = _plinth_radius("LOD0")
    assert plinth_r is not None and shell_r < plinth_r <= shell_r + 0.35, plinth_r  # the 0.3 m plinth
    ring = _sidecar("scaffold_ring", "LOD0")["dimensions"]
    # Clear of the plinth all the way round: the platform polygon's inscribed radius.
    assert ring["inner_inscribed_diameter_m"] / 2 >= plinth_r + 0.03, (ring, plinth_r)
    # And on the real vertices of the committed ring: nothing nearer the axis than the plinth.
    j, blob = _glb_chunks(_glb("scaffold_ring", "LOD0"))
    pos = _accessor(j, blob, j["meshes"][0]["primitives"][0]["attributes"]["POSITION"])
    assert min(math.hypot(p[0], p[2]) for p in pos) >= plinth_r, plinth_r
    # Spec: O29.5 x 13: it stands as tall as the shell and reaches the spec diameter.
    assert ring["height_m"] >= tank["shell_height_m"] - 1e-6
    assert abs(ring["outer_diameter_m"] - 29.5) <= 0.05 * 29.5
    # Its working platforms stay inside the neighbouring tank's walkway reach at the 32 m pitch.
    assert ring["outer_diameter_m"] / 2 + (shell_r + 1.3) < 32.0, ring


def test_the_far_scaffold_ring_clears_the_far_tank():
    # LOD1 has no plinth: the shells are bare cylinders. The ring's inner polygon clears the shell.
    shell_r = _sidecar("tank_growout", "LOD1")["dimensions"]["shell_diameter_m"] / 2
    assert _plinth_radius("LOD1") is None
    ring = _sidecar("scaffold_ring", "LOD1")["dimensions"]
    assert ring["inner_inscribed_diameter_m"] / 2 > shell_r


# ── Construction phases (acceptance 4) ────────────────────────────────────────

def _spec_phase_rows():
    """The first cells of spec §6.3's two tables, in order, with the row's
    'Visible in 3D' text."""
    section = SPEC[SPEC.index("### 6.3 Construction in 3D"):SPEC.index("### 6.4 First sight")]
    tables, current = {}, None
    for line in section.splitlines():
        m = re.match(r"\| (Land|Offshore) phase \|", line)
        if m:
            current = m.group(1).lower()
            tables[current] = []
            continue
        if current and line.startswith("|") and not line.startswith("|---"):
            cells = [c.strip() for c in line.strip("|").split("|")]
            tables[current].append((cells[0].replace("*", "").strip(), cells[1]))
        elif current and not line.strip():
            current = None
    return tables


def _engine_phase_keys():
    land_src = (REPO / "src" / "LakskongenSim" / "Farm" / "Land" / "LandConstructionPhases.cs").read_text()
    statement = re.search(r"const string SiteWorks = (.*?);", land_src, re.S).group(1)
    land = re.findall(r'"(\w+)"', statement)
    off_src = (REPO / "src" / "LakskongenSim" / "Farm" / "Offshore" / "OffshoreProfiles.cs").read_text()
    offshore = re.findall(r'const string \w+Phase = "(\w+)";', off_src)
    return {"land": land, "offshore": offshore}


def test_spec_tables_have_fourteen_phases():
    rows = _spec_phase_rows()
    assert [len(rows["land"]), len(rows["offshore"])] == [7, 7], rows


def test_engine_phase_keys_are_read_from_the_sim():
    keys = _engine_phase_keys()
    assert keys["land"] == ["site_works", "tank_rings", "treatment_hall", "sea_works", "first_tanks", "completion"]
    assert keys["offshore"] == ["yard_contract", "hull", "voyage", "tow", "mooring", "commissioning"]


@pytest.mark.parametrize("family", ["land", "offshore"])
def test_every_spec_phase_has_its_asset_set(family):
    spec_rows = _spec_phase_rows()[family]
    sets = PHASE_SETS[family]
    assert list(sets) == [name for name, _visible in spec_rows], (list(sets), spec_rows)
    for name, visible in spec_rows:
        entry = sets[name]
        assert entry["assets"], (family, name, "an empty asset set")
        for aid in entry["assets"]:
            assert aid in ALL_ASSETS, (family, name, aid)
            for lod in ALL_ASSETS[aid]["lod_levels"]:
                assert _glb(aid, lod).is_file() and _glb(aid, lod).with_suffix(".json").is_file(), (name, aid, lod)
        # An asset that is not built yet is pending and named in the spec; one that
        # exists must be in `assets` (no stale pending).
        for aid in entry["pending"]:
            assert aid not in ALL_ASSETS, (family, name, aid, "it exists now: move it into assets")
            assert aid in SPEC, (family, name, aid, "not in the spec")
        assert len(entry["assets"]) == len(set(entry["assets"])), (family, name)


def test_the_spec_words_name_their_assets():
    # The spec's own wording of a phase decides which kit pieces it needs.
    words = {"tower crane": "tower_crane", "scaffold": "scaffold_ring", "blast mats": "blast_mats",
             "anchor tugs": "offshore_service", "tugs": "offshore_service", "wellboat": "wellboat",
             "crew boat": "crew_boat", "quay": "quay"}
    seen = set()
    for family, rows in _spec_phase_rows().items():
        for name, visible in rows:
            for word, aid in words.items():
                if word in visible.lower():
                    assert aid in PHASE_SETS[family][name]["assets"], (family, name, word, aid)
                    seen.add(aid)
    assert {"tower_crane", "scaffold_ring", "blast_mats", "offshore_service", "wellboat", "crew_boat", "quay"} <= seen


def test_every_engine_phase_key_is_drawn_by_a_spec_row():
    keys = _engine_phase_keys()
    for family in ("land", "offshore"):
        drawn = [k for entry in PHASE_SETS[family].values() for k in entry["engine_phases"]]
        assert sorted(drawn) == sorted(keys[family]) or set(drawn) == set(keys[family]), (family, drawn, keys[family])
        assert set(drawn) <= set(keys[family]), (family, drawn)
        # A key never appears under two different rows' phases of another family.
        assert not set(drawn) & set(keys["offshore" if family == "land" else "land"]), family


def test_no_construction_asset_is_an_orphan():
    used = {aid for fam in ("land", "offshore") for entry in PHASE_SETS[fam].values() for aid in entry["assets"]}
    assert set(KIT_IDS) <= used, set(KIT_IDS) - used


def test_the_crane_and_the_scaffold_belong_to_their_rows():
    land = PHASE_SETS["land"]
    assert "tower_crane" in land["Treatment hall"]["assets"]
    assert "scaffold_ring" in land["Tank rings rise"]["assets"]
    assert {"blast_mats", "excavator"} <= set(land["Blast pad"]["assets"])


# ── Godot wiring ──────────────────────────────────────────────────────────────

@pytest.mark.parametrize("aid", KIT_IDS)
def test_godot_import_uses_the_kit_material_and_hand_made_lods(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        text = Path(str(_glb(aid, lod)) + ".import").read_text()
        assert "meshes/generate_lods=false" in text, (aid, lod)
        block = re.search(r'"LK_FacilityKit": \{(.*?)\}', text, re.S)
        assert block and '"use_external/enabled": true' in block.group(1), (aid, lod)
        assert '"use_external/path": "res://assets/models/facility_kit/LK_FacilityKit.tres"' in block.group(1)


def test_the_kit_material_is_the_land_kits():
    tres = (FACILITY / "LK_FacilityKit.tres").read_text()
    assert 'path="res://assets/models/facility_kit/textures/facility_atlas.png"' in tres
    assert 'resource_name = "LK_FacilityKit"' in tres


def test_the_scaffold_ring_is_as_tall_as_the_shader_builds_a_tank():
    # INSTANCE_CUSTOM.r reveals fragments up to progress x build_height_m (13 m for a
    # grow-out tank): the same progress reveals a 13 m ring in step with the wall.
    body = (GODOT / "shaders" / "include" / "facility_kit_body.gdshaderinc").read_text()
    assert re.search(r"instance uniform float build_height_m = 13\.0;", body)
    assert ASSETS["scaffold_ring"]["spec"]["height_m"] == 13.0


def _walls_top_progress():
    """The presenter's WALLS_TOP_PROGRESS: the build progress the tank walls
    stand at when the tank_rings phase ends (None if no script defines it)."""
    for path in sorted((GODOT / "scripts").rglob("*.gd")):
        m = re.search(r"^const WALLS_TOP_PROGRESS\s*:?=\s*([0-9.]+)", path.read_text(), re.M)
        if m:
            return float(m.group(1))
    return None


def test_the_ring_is_struck_before_the_walkway_shows():
    # The ring's top lift (boards at 12 m, rail at 13 m) stands in the height band of the
    # tank's walkway, so the two must not show together. The ring is struck when
    # tank_rings ends, and by then the walls' top progress has revealed nothing above
    # WALLS_TOP_PROGRESS x build_height_m. If this fails, the presenter moved the walls'
    # top, or the tank's walkway moved down: strike the ring earlier or lower it, and
    # update the "Tank rings rise" note in blender/asset_manifest.json.
    body = (GODOT / "shaders" / "include" / "facility_kit_body.gdshaderinc").read_text()
    build_height = float(re.search(r"instance uniform float build_height_m = ([0-9.]+);", body).group(1))
    top = _walls_top_progress()
    assert top is not None, "WALLS_TOP_PROGRESS is not defined under godot/scripts (moved or renamed?)"
    lowest_walkway = min(z for _r, z in _tank_strip_vertices("LOD0", "grating"))
    ring_top = _sidecar("scaffold_ring", "LOD0")["dimensions"]["height_m"]
    revealed_at_strike = top * build_height
    assert ring_top <= lowest_walkway or revealed_at_strike <= lowest_walkway + 1e-6, \
        (ring_top, revealed_at_strike, lowest_walkway)


# ── Evidence ──────────────────────────────────────────────────────────────────

def test_evidence_doc_records_the_acceptance_and_its_frames():
    doc = EVIDENCE.read_text()
    for needle in ("## Acceptance", "byte", "LOD1", "tower_crane", "scaffold_ring", "excavator", "blast_mats",
                   "construction_phase_sets", "F2-20"):
        assert needle in doc, needle
    images = re.findall(r"\]\((f2_28/[^)]+)\)", doc)
    assert len(images) >= 6
    for img in images:
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
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_construction_kit.py"), "--",
         "--out", str(tmp_path), *args], capture_output=True, text=True, timeout=900)


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("aid,lod", [("tower_crane", "LOD1"), ("tower_crane", "LOD2"), ("scaffold_ring", "LOD0"),
                                     ("excavator", "LOD1"), ("blast_mats", "LOD0")])
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
    out = _build(tmp_path, "--asset", "tower_crane", "--lod", "LOD1", "--no-bake", "--tri-limit-scale", "0.5")
    assert out.returncode != 0
    assert "tower_crane LOD1 has 274 triangles, over its 150 budget" in out.stdout + out.stderr


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_refuses_a_lod_the_spec_has_no_budget_for(tmp_path):
    out = _build(tmp_path, "--asset", "blast_mats", "--lod", "LOD2", "--no-bake")
    assert out.returncode != 0
    assert "blast_mats has no LOD2" in out.stdout + out.stderr
