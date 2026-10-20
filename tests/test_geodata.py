"""The national map data in godot/data/geo (tools/geodata, docs/geo/GEODATA.md).

Stdlib + pytest only: CI never needs GIS packages. Checks the shipped files,
not the pipeline: the 13 production areas, the locality records and D17, the
licence record of every layer, the size budget, candidate sites against every
hard constraint (pure-Python point-in-polygon and distances), the determinism
hash of the candidate list, the grids and the base maps. It also carries the
reference UTM 33 projection the game must reproduce (manifest.json lists
reference points).
"""

from __future__ import annotations

import hashlib
import json
import math
import re
import zlib
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
GEO = REPO / "godot" / "data" / "geo"
SIZE_BUDGET = 8_000_000
PRODUCTION_AREAS_CS = REPO / "src" / "LakskongenSim" / "Environment" / "ProductionAreas.cs"
LICENCES_OK = {"NLOD 2.0", "CC BY 4.0", "Copernicus Marine Service Licence",
               "Public domain (åndsverkloven § 14)", "Project data"}
FORBIDDEN_KEYS = re.compile(r"innehaver|owner|operator|company|holder|orgnr|organisasjon|inspeksjon|selskap|firma",
                            re.IGNORECASE)
# Norway and its sea areas (lon, lat).
LON_RANGE, LAT_RANGE = (-6.0, 36.0), (56.5, 74.0)


def load(name: str) -> dict:
    return json.loads((GEO / name).read_text(encoding="utf-8"))


# --------------------------------------------------------------------------
# Reference projection: ETRS89 / UTM 33N (EPSG:25833), Krüger series to n^4
# (Karney 2011), sub-millimetre inside the zone and well beyond it.


def utm33(lon: float, lat: float) -> tuple[float, float]:
    a, f, k0, lon0 = 6378137.0, 1 / 298.257222101, 0.9996, math.radians(15.0)
    n = f / (2 - f)
    big_a = a / (1 + n) * (1 + n * n / 4 + n ** 4 / 64)
    alpha = (n / 2 - 2 * n * n / 3 + 5 * n ** 3 / 16 + 41 * n ** 4 / 180,
             13 * n * n / 48 - 3 * n ** 3 / 5 + 557 * n ** 4 / 1440,
             61 * n ** 3 / 240 - 103 * n ** 4 / 140,
             49561 * n ** 4 / 161280)
    phi, lam = math.radians(lat), math.radians(lon) - lon0
    e = 2 * math.sqrt(n) / (1 + n)
    t = math.sinh(math.atanh(math.sin(phi)) - e * math.atanh(e * math.sin(phi)))
    xi = math.atan2(t, math.cos(lam))
    eta = math.atanh(math.sin(lam) / math.sqrt(1 + t * t))
    x = eta + sum(al * math.cos(2 * j * xi) * math.sinh(2 * j * eta) for j, al in enumerate(alpha, 1))
    y = xi + sum(al * math.sin(2 * j * xi) * math.cosh(2 * j * eta) for j, al in enumerate(alpha, 1))
    return 500000.0 + k0 * big_a * x, k0 * big_a * y


def test_reference_projection_matches_the_manifest_points():
    for p in load("manifest.json")["projection_check_25833"]:
        x, y = utm33(p["lon"], p["lat"])
        assert abs(x - p["x"]) < 0.01 and abs(y - p["y"]) < 0.01, p


# --------------------------------------------------------------------------
# Geometry helpers


def rings_of(geom: dict) -> list[list[list[list[float]]]]:
    if geom["type"] == "Polygon":
        return [geom["coordinates"]]
    assert geom["type"] == "MultiPolygon", geom["type"]
    return geom["coordinates"]


def point_in_ring(x: float, y: float, ring: list[list[float]]) -> bool:
    inside = False
    j = len(ring) - 1
    for i in range(len(ring)):
        xi, yi = ring[i][0], ring[i][1]
        xj, yj = ring[j][0], ring[j][1]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def point_in_geom(x: float, y: float, geom: dict) -> bool:
    for poly in rings_of(geom):
        if point_in_ring(x, y, poly[0]) and not any(point_in_ring(x, y, h) for h in poly[1:]):
            return True
    return False


def bbox(geom: dict) -> tuple[float, float, float, float]:
    xs = [p[0] for poly in rings_of(geom) for p in poly[0]]
    ys = [p[1] for poly in rings_of(geom) for p in poly[0]]
    return min(xs), min(ys), max(xs), max(ys)


def seg_dist(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    dx, dy = bx - ax, by - ay
    t = 0.0 if dx == dy == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def decode_png(path: Path) -> tuple[int, int, int, list[bytes]]:
    """(width, height, channels, rows) of an 8-bit non-interlaced PNG; all five filters."""
    raw = path.read_bytes()
    assert raw[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat = 8, b""
    while pos < len(raw):
        n = int.from_bytes(raw[pos:pos + 4], "big")
        tag, data = raw[pos + 4:pos + 8], raw[pos + 8:pos + 8 + n]
        if tag == b"IHDR":
            w, h = int.from_bytes(data[:4], "big"), int.from_bytes(data[4:8], "big")
            depth, ctype, interlace = data[8], data[9], data[12]
            assert depth == 8 and interlace == 0
        elif tag == b"IDAT":
            idat += data
        pos += 12 + n
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[ctype]
    stride = w * ch
    buf = zlib.decompress(idat)
    rows, prev = [], bytearray(stride)
    for r in range(h):
        ft = buf[r * (stride + 1)]
        line = bytearray(buf[r * (stride + 1) + 1:(r + 1) * (stride + 1)])
        for i in range(stride):
            left = line[i - ch] if i >= ch else 0
            up = prev[i]
            ul = prev[i - ch] if i >= ch else 0
            if ft == 1:
                line[i] = (line[i] + left) & 255
            elif ft == 2:
                line[i] = (line[i] + up) & 255
            elif ft == 3:
                line[i] = (line[i] + (left + up) // 2) & 255
            elif ft == 4:
                p = left + up - ul
                pa, pb, pc = abs(p - left), abs(p - up), abs(p - ul)
                line[i] = (line[i] + (left if pa <= pb and pa <= pc else up if pb <= pc else ul)) & 255
        rows.append(bytes(line))
        prev = line
    return w, h, ch, rows


def jpeg_size(path: Path) -> tuple[int, int]:
    raw = path.read_bytes()
    assert raw[:2] == b"\xff\xd8"
    pos = 2
    while pos < len(raw):
        marker = raw[pos + 1]
        length = int.from_bytes(raw[pos + 2:pos + 4], "big")
        if marker in (0xC0, 0xC1, 0xC2):
            h = int.from_bytes(raw[pos + 5:pos + 7], "big")
            w = int.from_bytes(raw[pos + 7:pos + 9], "big")
            return w, h
        pos += 2 + length
    raise AssertionError(f"{path.name}: no SOF marker")


# --------------------------------------------------------------------------
# Production areas


def cs_area_names() -> list[str]:
    text = PRODUCTION_AREAS_CS.read_text(encoding="utf-8")
    block = text.split("private static readonly string[] Names =", 1)[1].split("};", 1)[0]
    return re.findall(r'"([^"]+)"', block)


@pytest.mark.parametrize("lod", ["national", "regional"])
def test_thirteen_production_areas_with_closed_valid_polygons(lod):
    fc = load(f"production_areas.{lod}.geojson")
    feats = fc["features"]
    assert [f["properties"]["id"] for f in feats] == list(range(1, 14))
    assert [f["properties"]["code"] for f in feats] == [f"PO{i}" for i in range(1, 14)]
    assert [f["properties"]["name"] for f in feats] == cs_area_names()
    for f in feats:
        for poly in rings_of(f["geometry"]):
            for ring in poly:
                assert len(ring) >= 4, f["properties"]["code"]
                assert ring[0] == ring[-1], f"{f['properties']['code']}: ring not closed"
                for lon, lat in ring:
                    assert LON_RANGE[0] <= lon <= LON_RANGE[1] and LAT_RANGE[0] <= lat <= LAT_RANGE[1]
        p = f["properties"]
        assert len(p["sea_temp_monthly_c"]) == 12
        assert point_in_geom(*p["label"], f["geometry"]) or lod == "national", p["code"]
        assert p["tint"].startswith("#") and len(p["tint"]) == 7


# --------------------------------------------------------------------------
# Localities and D17

REQUIRED = {"id", "name", "kind", "stage", "species", "capacity", "capacity_unit", "water", "municipality",
            "po", "x", "y"}
KINDS = {f"{p}_{g}" for p in ("sea", "land", "offshore") for g in ("salmonid", "marine_fish", "shellfish",
                                                                   "algae", "other")}


def test_localities_carry_the_required_attributes():
    feats = load("localities.geojson")["features"]
    assert len(feats) > 1000
    ids = [f["properties"]["id"] for f in feats]
    assert len(ids) == len(set(ids))
    for f in feats:
        p = f["properties"]
        assert REQUIRED <= set(p), REQUIRED - set(p)
        assert p["kind"] in KINDS, p["kind"]
        assert isinstance(p["species"], list) and p["name"]
        assert p["po"] is None or 1 <= p["po"] <= 13
        lon, lat = f["geometry"]["coordinates"]
        assert LON_RANGE[0] <= lon <= LON_RANGE[1] and LAT_RANGE[0] <= lat <= LAT_RANGE[1]
        x, y = utm33(lon, lat)
        assert abs(x - p["x"]) < 2 and abs(y - p["y"]) < 2, p["id"]
    sea_salmon = [f for f in feats if f["properties"]["kind"] == "sea_salmonid"]
    assert len(sea_salmon) > 800
    assert all(f["properties"].get("mtb_t", 0) >= 0 for f in sea_salmon)


def all_property_keys(obj, keys: set[str]) -> set[str]:
    if isinstance(obj, dict):
        for k, v in obj.items():
            keys.add(k)
            all_property_keys(v, keys)
    elif isinstance(obj, list):
        for v in obj:
            all_property_keys(v, keys)
    return keys


def test_no_layer_carries_an_owner_or_company_field():
    for path in sorted(GEO.rglob("*.geojson")):
        keys = set()
        for f in json.loads(path.read_text(encoding="utf-8"))["features"]:
            all_property_keys(f["properties"], keys)
        bad = sorted(k for k in keys if FORBIDDEN_KEYS.search(k))
        assert not bad, f"{path.name}: {bad}"


def test_no_real_company_names_in_any_geo_file():
    """D17 over every shipped geo file (.geojson and .md too, which the general
    text test does not scan), with the game's canonical brand list."""
    from test_text_correctness import BRAND_RE

    hits = []
    for path in sorted(GEO.rglob("*")):
        if path.suffix in (".geojson", ".json"):
            for m in BRAND_RE.finditer(path.read_text(encoding="utf-8")):
                hits.append(f"{path.name}: {m.group(1)}")
    assert not hits, hits[:20]


# --------------------------------------------------------------------------
# Licences, manifest, size budget


def shipped_files() -> list[Path]:
    return sorted(p for p in GEO.rglob("*") if p.is_file() and p.suffix != ".import")


def test_every_layer_is_listed_in_licences_md_with_a_licence():
    md = (GEO / "LICENCES.md").read_text(encoding="utf-8")
    layers = md.split("## Layers", 1)[1].split("##", 1)[0]
    sources = md.split("## Sources", 1)[1].split("##", 1)[0]
    licence_of = {}
    for line in sources.splitlines():
        m = re.match(r"\| `([a-z0-9_]+)` \|[^|]*\|[^|]*\| (?:\[([^\]]+)\]\([^)]*\)|([^|]+?)) \| (\S+) \|", line)
        if m:
            licence_of[m.group(1)] = (m.group(2) or m.group(3)).strip()
            assert re.fullmatch(r"\d{4}-\d{2}-\d{2}|derived", m.group(4)), line
    assert licence_of and set(licence_of.values()) <= LICENCES_OK, licence_of
    for path in shipped_files():
        rel = path.relative_to(GEO).as_posix()
        if rel in ("manifest.json", "LICENCES.md"):
            continue
        row = next((ln for ln in layers.splitlines() if ln.startswith(f"| `{rel}` |")), None)
        assert row, f"{rel} is not listed in LICENCES.md"
        for sid in re.findall(r"`([a-z0-9_]+)`", row.split("|")[4]):
            assert sid in licence_of, f"{rel}: source {sid} has no licence row"


def test_manifest_lists_every_file_with_its_hash():
    files = load("manifest.json")["files"]
    shipped = {p.relative_to(GEO).as_posix() for p in shipped_files()} - {"manifest.json"}
    assert set(files) == shipped
    for rel, f in files.items():
        data = (GEO / rel).read_bytes()
        assert f["bytes"] == len(data), rel
        assert f["sha256"] == hashlib.sha256(data).hexdigest(), rel


def test_size_budget():
    total = sum(p.stat().st_size for p in shipped_files())
    assert total <= SIZE_BUDGET, f"{total / 1e6:.2f} MB > {SIZE_BUDGET / 1e6:.0f} MB"
    for p in shipped_files():
        assert p.stat().st_size <= 1_500_000, f"{p.name} is {p.stat().st_size / 1e6:.2f} MB"


# --------------------------------------------------------------------------
# Candidate sites


@pytest.fixture(scope="module")
def candidates() -> dict:
    return load("candidates.json")


def test_candidate_list_is_deterministic(candidates):
    assert candidates["random"] is False and candidates["seed"] is None
    text = json.dumps(candidates["sites"], ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    assert hashlib.sha256(text.encode("utf-8")).hexdigest() == candidates["content_sha256"]
    ids = [s["id"] for s in candidates["sites"]]
    assert len(ids) == len(set(ids))
    pens = [s for s in candidates["sites"] if s["type"] == "open_pen"]
    assert sorted({s["po"] for s in pens}) == list(range(1, 14))
    for po in range(1, 14):
        ranks = [s["rank"] for s in pens if s["po"] == po]
        assert ranks == list(range(1, len(ranks) + 1))
        scores = [s["score"] for s in pens if s["po"] == po]
        assert scores == sorted(scores, reverse=True), f"PO{po}: not ranked by score"
    assert any(s["type"] == "offshore" for s in candidates["sites"])


def test_candidates_record_score_components_and_binding(candidates):
    rules = load("suitability_rules.json")
    for s in candidates["sites"]:
        assert 0 < s["score"] <= 1
        assert s["components"] and all(0 <= v <= 1 for v in s["components"].values())
        assert s["binding"] in set(s["components"]) | {"none", "stage", "river"}
        x, y = utm33(s["lon"], s["lat"])
        assert abs(x - s["x"]) < 2 and abs(y - s["y"]) < 2
    for name, c in rules["components"].items():
        assert c["source"] and c["rule"], name
    for name, h in rules["hard_constraints"].items():
        assert h["rule"], name


def test_candidates_lie_outside_every_exclusion_polygon(candidates):
    polys = [(bbox(f["geometry"]), f) for f in load("exclusions.regional.geojson")["features"]]
    assert {f["properties"]["kind"] for _, f in polys} == {"salmon_fjord", "protected_area", "coral_closure"}
    for s in candidates["sites"]:
        for (x0, y0, x1, y1), f in polys:
            if x0 <= s["lon"] <= x1 and y0 <= s["lat"] <= y1:
                assert not point_in_geom(s["lon"], s["lat"], f["geometry"]), \
                    f"{s['id']} inside {f['properties']['kind']} {f['properties']['name']}"


def test_the_polygon_check_sees_a_known_salmon_fjord():
    """Guards the guard: mid-Trondheimsfjorden lies in its national salmon fjord, the open
    Norwegian Sea west of Frøya does not."""
    fjords = [f for f in load("exclusions.regional.geojson")["features"]
              if f["properties"]["kind"] == "salmon_fjord"]
    assert any(point_in_geom(10.55, 63.50, f["geometry"]) for f in fjords)
    assert not any(point_in_geom(7.5, 63.8, f["geometry"]) for f in fjords)


def test_open_pen_candidates_lie_inside_their_production_area(candidates):
    areas = {f["properties"]["id"]: f["geometry"] for f in load("production_areas.regional.geojson")["features"]}
    for s in candidates["sites"]:
        if s["type"] == "open_pen":
            assert point_in_geom(s["lon"], s["lat"], areas[s["po"]]), s["id"]


def test_candidates_keep_clear_of_rivers_corals_farms_and_fairways(candidates):
    pts = load("constraint_points.geojson")
    kinds = pts["meta"]["kinds"]
    hard = [(f["properties"]["x"], f["properties"]["y"], kinds[f["properties"]["kind"]]["radius_m"])
            for f in pts["features"] if kinds[f["properties"]["kind"]]["hard"]]
    farms = [(f["properties"]["x"], f["properties"]["y"]) for f in load("localities.geojson")["features"]
             if f["properties"]["kind"] in ("sea_salmonid", "offshore_salmonid")]
    min_farm = load("suitability_rules.json")["hard_constraints"]["farm_too_close"]["min_km"] * 1000
    fairways = []
    for f in load("fairways.regional.geojson")["features"]:
        g = f["geometry"]
        parts = g["coordinates"] if g["type"] == "MultiLineString" else [g["coordinates"]]
        for part in parts:
            fairways.append(([utm33(lon, lat) for lon, lat in part], f["properties"]["buffer_m"]))
    for s in candidates["sites"]:
        for x, y, r in hard:
            assert math.hypot(s["x"] - x, s["y"] - y) >= r, f"{s['id']} within {r} m of a hard point"
        assert min(math.hypot(s["x"] - x, s["y"] - y) for x, y in farms) >= min_farm, s["id"]
        for line, buf in fairways:
            for (ax, ay), (bx, by) in zip(line, line[1:]):
                if abs(ax - s["x"]) < 20_000 and abs(ay - s["y"]) < 20_000:
                    assert seg_dist(s["x"], s["y"], ax, ay, bx, by) >= buf, f"{s['id']} in a fairway buffer"


# --------------------------------------------------------------------------
# Grids and base maps


def test_suitability_grids_decode_and_agree_with_the_candidates(candidates):
    suit = load("suitability.json")
    codes = {int(k) for k in suit["codes"]}
    grids = {}
    for po in range(1, 14):
        g = suit["grids"][str(po)]
        w, h, ch, rows = decode_png(GEO / g["file"])
        assert (w, h, ch) == (g["width"], g["height"], 3)
        seen = {rows[r][c * 3 + 1] for r in range(0, h, 7) for c in range(0, w, 7)}
        assert seen <= codes, seen - codes
        grids[po] = (g, rows)
    for s in candidates["sites"]:
        if s["type"] != "open_pen":
            continue
        g, rows = grids[s["po"]]
        c = int((s["x"] - g["origin_25833"][0]) // g["res_m"])
        r = int((g["origin_25833"][1] - s["y"]) // g["res_m"])
        assert rows[r][c * 3] > 0, f"{s['id']}: its grid cell is not scored"


def test_base_maps_match_their_georeference():
    bm = load("basemap.json")
    for key, img in bm["images"].items():
        path = GEO / img["file"]
        if path.suffix == ".jpg":
            assert jpeg_size(path) == (img["width"], img["height"]), key
        x0, y0, x1, y1 = img["bounds_25833"]
        assert (x1 - x0) == img["width"] * img["res_m"] and (y1 - y0) == img["height"] * img["res_m"], key
    w, h, ch, rows = decode_png(GEO / bm["images"]["po_index_national"]["file"])
    values = {v for row in rows[::5] for v in row[::5]}
    assert ch == 1 and values <= set(range(14)) and set(range(1, 14)) <= values


def test_offshore_areas_carry_their_stage():
    stages = {f["properties"]["stage"] for f in load("offshore_areas.regional.geojson")["features"]}
    assert stages == {"tilradning_2019", "anbefaling_2022", "konsekvensvurdering_2024", "kan_utlyses_2026"}
