"""How the geo data in godot/data/geo is imported and exported (geo stages 2-3
P0, docs/geo/STAGE23_PLAN.md §4 step 0 and ruling OD13).

- The 14 data PNGs (suitability/po01..po13.png, basemap/po_index.national.png)
  are imported with importer="keep": the game samples their raw bytes, so
  Godot must neither recompress them nor hide them behind a .ctex.
- The 14 JPG base maps are imported as lossless textures with mipmaps (the
  1866x2186 national map is drawn at about 0.4 scale).
- Both export presets carry one explicit include_filter that packs every geo
  file the game reads raw and never the raw JPGs (their imported textures are
  exported as resources already).
- test_geodata.py keeps passing, because its shipped_files() ignores *.import.
"""

from __future__ import annotations

import re
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
GEO = GODOT / "data" / "geo"
PRESETS = GODOT / "export_presets.cfg"

DATA_PNGS = [f"suitability/po{n:02d}.png" for n in range(1, 14)] + ["basemap/po_index.national.png"]
BASE_MAPS = ["basemap/national.jpg"] + [f"basemap/po{n:02d}.jpg" for n in range(1, 14)]
INCLUDE_FILTER = ("data/geo/*.json,data/geo/*.geojson,data/geo/*.md,"
                  "data/geo/suitability/*.png,data/geo/basemap/po_index.national.png")
KEEP_IMPORT = '[remap]\n\nimporter="keep"\n'


def _rel(path: Path) -> str:
    return path.relative_to(GEO).as_posix()


def _import_params(path: Path) -> dict[str, str]:
    """key=value pairs of a .import file, sections flattened (Godot values kept raw)."""
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^([\w/]+)=(.*)$", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def _godot_matchn(text: str, pattern: str) -> bool:
    """Godot's String.matchn: case-insensitive, '*' is any run of characters
    (including '/'), '?' is one character, nothing else is special."""
    rx = "".join(".*" if c == "*" else "." if c == "?" else re.escape(c) for c in pattern)
    return re.fullmatch(rx, text, flags=re.IGNORECASE | re.DOTALL) is not None


def _filter_matches(filters: str, res_path: str) -> bool:
    """EditorExportPlatform::_edit_files_with_filter: comma-split, edges
    stripped, tested against the res:// path and the path without res://."""
    patterns = [f.strip() for f in filters.split(",") if f.strip()]
    return any(_godot_matchn(f"res://{res_path}", p) or _godot_matchn(res_path, p) for p in patterns)


def _presets() -> dict[str, dict[str, str]]:
    """The one-line string keys of every [preset.N] section. (configparser
    cannot read the file: the options sections hold multi-line scripts.)"""
    sections: dict[str, dict[str, str]] = {}
    current = None
    for line in PRESETS.read_text(encoding="utf-8").splitlines():
        header = re.fullmatch(r"\[(.+)\]", line)
        if header:
            current = header.group(1) if re.fullmatch(r"preset\.\d+", header.group(1)) else None
            if current:
                assert current not in sections, f"duplicate section [{current}]"
                sections[current] = {}
            continue
        kv = re.fullmatch(r'(\w+)="(.*)"', line)
        if current and kv:
            assert kv.group(1) not in sections[current], f"[{current}] sets {kv.group(1)} twice"
            sections[current][kv.group(1)] = kv.group(2)
    return {values["name"]: values for values in sections.values()}


def _shipped_geo_files() -> list[Path]:
    return sorted(p for p in GEO.rglob("*") if p.is_file() and p.suffix != ".import")


# --------------------------------------------------------------------------
# Imports


def test_the_data_pngs_are_kept_raw():
    pngs = sorted(_rel(p) for p in _shipped_geo_files() if p.suffix == ".png")
    assert pngs == sorted(DATA_PNGS), "a PNG was added or removed under data/geo; decide its import"
    for rel in DATA_PNGS:
        sidecar = GEO / f"{rel}.import"
        assert sidecar.is_file(), f"{rel}.import is missing (run a headless --import, then keep it)"
        assert sidecar.read_text(encoding="utf-8") == KEEP_IMPORT, f"{rel}.import is not importer=keep"


def test_the_jpg_base_maps_import_lossless_with_mipmaps():
    jpgs = sorted(_rel(p) for p in _shipped_geo_files() if p.suffix == ".jpg")
    assert jpgs == sorted(BASE_MAPS)
    for rel in BASE_MAPS:
        sidecar = GEO / f"{rel}.import"
        assert sidecar.is_file(), f"{rel}.import is missing"
        params = _import_params(sidecar)
        assert params["importer"] == '"texture"', rel
        assert params["type"] == '"CompressedTexture2D"', rel
        assert re.fullmatch(r'"uid://[a-z0-9]+"', params["uid"]), rel
        assert params["source_file"] == f'"res://data/geo/{rel}"', rel
        assert params["compress/mode"] == "0", f"{rel}: not lossless"
        assert params["mipmaps/generate"] == "true", f"{rel}: no mipmaps"
        # 0 = never switch to VRAM compression when a 3D use is detected, so the
        # sidecar stays lossless and byte-stable across worktrees.
        assert params["detect_3d/compress_to"] == "0", rel


def test_only_the_28_images_have_import_sidecars():
    sidecars = sorted(_rel(p) for p in GEO.rglob("*.import"))
    assert sidecars == sorted(f"{rel}.import" for rel in DATA_PNGS + BASE_MAPS)


# --------------------------------------------------------------------------
# Export presets (OD13)


def test_both_presets_carry_the_explicit_geo_include_filter():
    presets = _presets()
    assert set(presets) == {"macOS", "Linux"}, sorted(presets)
    for name, values in presets.items():
        assert values["export_filter"] == "all_resources", name
        assert values["include_filter"] == INCLUDE_FILTER, name


def test_the_filter_packs_every_raw_geo_file_and_never_a_raw_jpg():
    for name, values in _presets().items():
        include, exclude = values["include_filter"], values["exclude_filter"]
        for path in _shipped_geo_files():
            res_path = f"data/geo/{_rel(path)}"
            packed = _filter_matches(include, res_path)
            if path.suffix == ".jpg":
                assert not packed, f"{name}: {res_path} would ship raw next to its imported texture"
            else:
                assert packed, f"{name}: the game reads {res_path} raw but the filter misses it"
            assert not _filter_matches(exclude, res_path), f"{name}: exclude_filter drops {res_path}"
        for sidecar in GEO.rglob("*.import"):
            assert not _filter_matches(include, f"data/geo/{_rel(sidecar)}"), sidecar.name


def test_the_filter_model_follows_godot_wildcards():
    assert _godot_matchn("data/geo/suitability/po01.png", "data/geo/*.png")  # '*' crosses '/'
    assert _godot_matchn("DATA/GEO/LICENCES.MD", "data/geo/*.md")
    assert not _godot_matchn("data/geo/basemap/po01.jpg", "data/geo/basemap/po_index.national.png")
    assert not _godot_matchn("data/geo/basemap/poXindex.national.png", "data/geo/basemap/po_index.national.png")
    assert _filter_matches(" data/geo/*.json , x", "data/geo/manifest.json")


# --------------------------------------------------------------------------
# test_geodata.py stays green with the sidecars in place


def test_geodata_shipped_files_ignore_the_import_sidecars():
    from test_geodata import shipped_files

    shipped = shipped_files()
    assert not [p for p in shipped if p.suffix == ".import"]
    assert [_rel(p) for p in shipped] == [_rel(p) for p in _shipped_geo_files()]
