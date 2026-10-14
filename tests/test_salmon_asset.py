"""The Atlantic salmon asset (roadmap G3-01, asset part).

blender/build_salmon.py writes salmon_LOD0/LOD1.glb, a JSON sidecar per LOD
(triangles, materials, bbox, measured proportions, UV bounds) and the skin
atlas textures/salmon_skin.png. These tests hold the asset to the roadmap
(UVs, LOD0 about 1.5k triangles, LOD1 about 300, countershaded silver, nose
along -Z) and to the species' proportions, and check that the game loads it.
None of them needs Godot.
"""

import hashlib
import json
import os
import shutil
import struct
import subprocess
import zlib
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
FISH = GODOT / "assets" / "models" / "fish"
MANIFEST = {a["id"]: a for a in json.loads((REPO / "blender" / "asset_manifest.json").read_text())["assets"]}
SALMON = MANIFEST["salmon"]


def _side(lod: str) -> dict:
    return json.loads((FISH / f"salmon_{lod}.json").read_text())


class _Png:
    """A minimal reader for 8-bit RGBA, non-interlaced PNGs (what Blender
    writes), so the texture test needs no image library."""

    def __init__(self, path: Path):
        data = path.read_bytes()
        assert data[:8] == b"\x89PNG\r\n\x1a\n"
        pos, idat = 8, b""
        while pos < len(data):
            length, kind = struct.unpack(">I4s", data[pos:pos + 8])
            body = data[pos + 8:pos + 8 + length]
            if kind == b"IHDR":
                self.w, self.h, depth, colour, _, _, interlace = struct.unpack(">IIBBBBB", body)
                assert (depth, colour, interlace) == (8, 6, 0), "8-bit RGBA, not interlaced"
            elif kind == b"IDAT":
                idat += body
            pos += 12 + length
        raw = zlib.decompress(idat)
        stride, bpp = self.w * 4, 4
        prev = bytearray(stride)
        self.rows = []
        for y in range(self.h):
            f = raw[y * (stride + 1)]
            line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i - bpp] if i >= bpp else 0
                if f == 1:
                    line[i] = (line[i] + a) & 0xFF
                elif f == 2:
                    line[i] = (line[i] + b) & 0xFF
                elif f == 3:
                    line[i] = (line[i] + ((a + b) >> 1)) & 0xFF
                elif f == 4:
                    p = a + b - c
                    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                    line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
            self.rows.append(line)
            prev = line

    def pixels(self, x0, y0, x1, y1):
        for y in range(y0, y1):
            row = self.rows[y]
            for x in range(x0, x1):
                yield tuple(row[4 * x:4 * x + 4])


_SKIN = None


def _skin() -> _Png:
    global _SKIN
    if _SKIN is None:
        _SKIN = _Png(FISH / "textures" / "salmon_skin.png")
    return _SKIN


def _luma(rgb) -> float:
    r, g, b = (c / 255.0 for c in rgb[:3])
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def _box(img: _Png, u0: float, u1: float, v0: float, v1: float):
    """Pixel box of a UV box (v up, as Blender and the build script)."""
    return int(u0 * img.w), int((1 - v1) * img.h), int(u1 * img.w), int((1 - v0) * img.h)


def _region_mean(img: _Png, u0: float, u1: float, v0: float, v1: float):
    px = list(img.pixels(*_box(img, u0, u1, v0, v1)))
    return [sum(p[k] for p in px) / len(px) for k in range(4)]


def test_both_lods_are_exported_with_the_skin_material():
    assert SALMON["lod_levels"] == ["LOD0", "LOD1"]
    assert SALMON["materials"] == ["LK_SalmonSkin"]
    for lod in SALMON["lod_levels"]:
        assert (FISH / f"salmon_{lod}.glb").is_file()
        side = _side(lod)
        assert side["materials"] == ["LK_SalmonSkin"] and side["material_count"] == 1


def test_triangle_counts_match_the_roadmap():
    # G3-01: LOD0 about 1.5k triangles, LOD1 about 300.
    lod0, lod1 = _side("LOD0")["tri_count"], _side("LOD1")["tri_count"]
    assert 1200 <= lod0 <= 1800, lod0
    assert 200 <= lod1 <= 400, lod1
    for lod, ceiling in SALMON["tri_max"].items():
        assert _side(lod)["tri_count"] <= ceiling


@pytest.mark.parametrize("lod", ["LOD0", "LOD1"])
def test_proportions_are_an_atlantic_salmon(lod):
    dims = _side(lod)["dimensions"]
    spec = SALMON["spec"]
    assert dims["fork_length_m"] == spec["fork_length_m"] == 0.9
    lo, hi = spec["depth_over_fork_length"]
    assert lo <= dims["depth_over_fork_length"] <= hi
    lo, hi = spec["width_over_depth"]
    assert lo <= dims["width_over_depth"] <= hi
    lo, hi = spec["total_over_fork_length"]
    assert lo <= dims["total_length_m"] / dims["fork_length_m"] <= hi


@pytest.mark.parametrize("lod", ["LOD0", "LOD1"])
def test_nose_points_along_godot_minus_z(lod):
    # The snout is at Blender +Y = Godot -Z, half a fork length ahead of the
    # origin; the school code heads fish with Basis.looking_at (-Z forward).
    dims = _side(lod)["dimensions"]
    assert abs(dims["nose_y_m"] - 0.452) < 0.005
    bbox = _side(lod)["bbox_size"]
    assert bbox[2] > 2.5 * bbox[1] > 0  # long along z, then tall, then narrow


@pytest.mark.parametrize("lod", ["LOD0", "LOD1"])
def test_uvs_cover_the_atlas(lod):
    u0, v0, u1, v1 = _side(lod)["uv_bounds"]
    assert 0.0 <= u0 and u1 <= 1.0 and 0.0 <= v0 and v1 <= 1.0
    assert u1 - u0 > 0.95 and v1 - v0 > 0.9


def test_skin_is_countershaded_silver():
    img = _skin()
    assert (img.w, img.h) == (1024, 512)
    # Body region v in [0.26, 1]: the dorsal line at the top, the belly at
    # the bottom; mid-body columns away from the head.
    back = _region_mean(img, 0.35, 0.65, 0.93, 0.99)
    flank = _region_mean(img, 0.35, 0.65, 0.55, 0.62)
    belly = _region_mean(img, 0.35, 0.65, 0.27, 0.32)
    assert _luma(back) < 0.6 * _luma(flank), (back, flank)
    assert _luma(flank) < _luma(belly), (flank, belly)
    # Silver mask in alpha: the flank is the mirror, the back much less so.
    assert flank[3] > 200 and back[3] < flank[3] - 60
    # Black spots on the back, hardly any on the belly.
    def dark_fraction(v0, v1):
        px = list(img.pixels(*_box(img, 0.1, 0.8, v0, v1)))
        return sum(1 for p in px if _luma(p) < 0.08) / len(px)
    assert dark_fraction(0.75, 0.98) > 0.005
    assert dark_fraction(0.27, 0.45) < 0.001
    # Fins below: darker than the flank, low silver.
    fins = _region_mean(img, 0.05, 0.45, 0.05, 0.2)
    assert _luma(fins) < _luma(flank) and fins[3] < 80


def test_godot_import_maps_the_skin_material():
    for lod in ("LOD0", "LOD1"):
        text = (FISH / f"salmon_{lod}.glb.import").read_text()
        assert "meshes/generate_lods=false" in text
        assert '"LK_SalmonSkin": {' in text
        assert '"use_external/path": "res://assets/models/fish/salmon_skin_material.tres"' in text
    tres = (FISH / "salmon_skin_material.tres").read_text()
    assert 'path="res://shaders/salmon_skin.gdshader"' in tres
    assert 'path="res://assets/models/fish/textures/salmon_skin.png"' in tres
    assert "mipmaps/generate=true" in (FISH / "textures" / "salmon_skin.png.import").read_text()
    shader = (GODOT / "shaders" / "salmon_skin.gdshader").read_text()
    assert "METALLIC = mix(" in shader and "c.a" in shader, "silver mask drives metallic"


def test_the_game_loads_the_new_salmon():
    code = (GODOT / "scripts" / "environment" / "FishPenManager.gd").read_text()
    assert "salmon_LOD0.glb" in code and "salmon_LOD1.glb" in code
    assert 'find_child("Salmon"' in code
    assert "salmon_v2" not in code
    assert not (FISH / "salmon_v2.glb").exists()


def _blender() -> str | None:
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_script_reproduces_the_committed_salmon(tmp_path):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_salmon.py"), "--",
         "--lod", "LOD1", "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=300)
    assert out.returncode == 0, out.stderr[-2000:]
    new_side = json.loads((tmp_path / "salmon_LOD1.json").read_text())
    old_side = _side("LOD1")
    keys = ("tri_count", "materials", "bbox_size", "dimensions", "uv_bounds")
    assert {k: new_side[k] for k in keys} == {k: old_side[k] for k in keys}
    if new_side.get("blender_version") == old_side.get("blender_version"):
        assert (hashlib.sha256((tmp_path / "salmon_LOD1.glb").read_bytes()).hexdigest()
                == hashlib.sha256((FISH / "salmon_LOD1.glb").read_bytes()).hexdigest())
