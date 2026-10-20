"""The map UI's contract with the geo data (geo stages 2-3, docs/geo/STAGE23_PLAN.md
Track C; Track D adds its own functions here, add-only).

C1, the foundation (godot/scripts/ui/map/GeoData.gd, GeoProjection.gd):
- GeoData reads the data PNGs (suitability/poNN.png, po_index.national.png)
  only as raw bytes (FileAccess.get_file_as_bytes + Image.load_png_from_buffer),
  never through load() or preload(), so every byte keeps its value.
- The tables godot/tests/GeoMapChecks.gd pins (the po_index histogram, the
  label anchors off the open-sea mask, PO3's 12 candidate cells) equal what
  test_geodata.py's own PNG decoder reads, so the headless GDScript checks and
  the stdlib reference read the same bytes.
- No emoji under godot/scripts/ui/map (D14).
"""

from __future__ import annotations

import ast
import re
from collections import Counter
from pathlib import Path

from test_geodata import GEO, decode_png, load

REPO = Path(__file__).resolve().parent.parent
MAP_SCRIPTS = REPO / "godot" / "scripts" / "ui" / "map"
GEO_DATA = MAP_SCRIPTS / "GeoData.gd"
CHECKS = REPO / "godot" / "tests" / "GeoMapChecks.gd"
EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")
## A call that loads a resource: load(...), preload(...), ResourceLoader.load(...).
RESOURCE_LOAD = re.compile(r"(?<![\w.])(?:pre)?load\(|ResourceLoader\.load\(")


def _gd_const(name: str) -> object:
    """A literal GDScript const of GeoMapChecks.gd (a list or dict of numbers
    and strings, which Python's literal syntax reads the same way)."""
    text = CHECKS.read_text(encoding="utf-8")
    m = re.search(rf"^const {name} := (\[.*?\]|\{{.*?^\}})", text, flags=re.MULTILINE | re.DOTALL)
    assert m, f"GeoMapChecks.gd has no const {name}"
    return ast.literal_eval(m.group(1))


def _strip_comments(line: str) -> str:
    return line.split("#", 1)[0] if not line.lstrip().startswith("##") else ""


def test_geodata_reads_data_pngs_only_as_raw_bytes():
    text = GEO_DATA.read_text(encoding="utf-8")
    assert "FileAccess.get_file_as_bytes(" in text and ".load_png_from_buffer(" in text
    for path in sorted(MAP_SCRIPTS.glob("*.gd")):
        for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = _strip_comments(line)
            if RESOURCE_LOAD.search(code):
                assert "png" not in code.lower(), f"{path.name}:{n}: a data PNG through load(): {line.strip()}"
    # GeoData loads resources only in basemap_texture's imported-JPEG branch,
    # after a data PNG has gone to load_data_png.
    start = text.index("static func basemap_texture")
    end = text.index("\nstatic func ", start + 1)
    branch = text[start:end]
    jpeg_branch = start + branch.index("load_data_png(file)")
    assert branch.index('if info["is_data"]:') < branch.index("load_data_png(file)") < branch.index("\telse:")
    # Comments blanked to spaces, so offsets in the code equal offsets in text.
    code = "\n".join(_strip_comments(ln).ljust(len(ln)) for ln in text.splitlines())
    loads = [m.start() for m in RESOURCE_LOAD.finditer(code)]
    assert loads and all(jpeg_branch < at < end for at in loads), loads


def test_the_contract_check_finds_a_png_load():
    assert RESOURCE_LOAD.search('var img = load("res://data/geo/suitability/po03.png")')
    assert RESOURCE_LOAD.search('ResourceLoader.load(ROOT + "basemap/po_index.national.png")')
    assert not RESOURCE_LOAD.search("image.load_png_from_buffer(bytes)")
    assert not RESOURCE_LOAD.search("GeoData.load_data_png(file)")


def test_the_pinned_po_index_histogram_equals_the_reference_decoder():
    info = load("basemap.json")["images"]["po_index_national"]
    w, h, ch, rows = decode_png(GEO / info["file"])
    assert (w, h, ch) == (info["width"], info["height"], 1)
    counts = Counter(v for row in rows for v in row)
    assert set(counts) <= set(range(14))
    assert _gd_const("PO_INDEX_COUNTS") == [counts.get(v, 0) for v in range(14)]


def test_the_pinned_label_anchors_off_the_mask_equal_the_reference_decoder():
    info = load("basemap.json")["images"]["po_index_national"]
    _, _, _, rows = decode_png(GEO / info["file"])
    x0, _, _, y1 = info["bounds_25833"]
    off = []
    for f in load("production_areas.national.geojson")["features"]:
        x, y = f["properties"]["label_25833"]
        value = rows[int((y1 - y) // info["res_m"])][int((x - x0) // info["res_m"])]
        assert value in (0, f["properties"]["id"]), f["properties"]["code"]
        if value == 0:
            off.append(f["properties"]["id"])
    assert _gd_const("LABELS_OFF_MASK") == off == [1, 3]


def test_the_pinned_po3_candidate_cells_equal_the_reference_decoder():
    g = load("suitability.json")["grids"]["3"]
    w, h, ch, rows = decode_png(GEO / g["file"])
    assert (w, h, ch) == (g["width"], g["height"], 3)
    want = {}
    for s in load("candidates.json")["sites"]:
        if s["type"] != "open_pen" or s["po"] != 3:
            continue
        c = int((s["x"] - g["origin_25833"][0]) // g["res_m"])
        r = int((g["origin_25833"][1] - s["y"]) // g["res_m"])
        want[s["id"]] = [c, r, *rows[r][c * 3:c * 3 + 3]]
        assert want[s["id"]][2] > 0, f"{s['id']}: its grid cell is not scored"
    assert len(want) == 12
    assert _gd_const("PO3_CELLS") == want


def test_no_emoji_in_the_map_scripts():
    paths = sorted(MAP_SCRIPTS.glob("*.gd")) + [CHECKS]
    assert len(paths) >= 3
    for path in paths:
        text = path.read_text(encoding="utf-8")
        assert not EMOJI.search(text), (path.name, EMOJI.findall(text))
